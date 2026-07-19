using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CecController;
using ConsoleLibrary;
using GameLauncher;
using InputDaemon;

namespace UI;

/// <summary>
/// Spotlight dashboard model: ONE flat list of games navigated left/right, where the
/// focused game drives the entire screen — the hero title/art/actions above and the
/// ambient background's tint. There is no multi-row grid and no separate hero tile
/// (both existed in the previous design); the hero IS whatever is focused in Games.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    /// <summary>Every game, in on-screen left-to-right order. Left/Right moves focus; the hero mirrors the focused entry.</summary>
    public ObservableCollection<GameTileViewModel> Games { get; } = new();

    public ObservableCollection<NavRailItemViewModel> RailItems { get; } = BuildRailItems();

    private static ObservableCollection<NavRailItemViewModel> BuildRailItems()
    {
        var items = new ObservableCollection<NavRailItemViewModel>();
        items.Add(new NavRailItemViewModel { Glyph = "Lib", IsActive = true });
        items.Add(new NavRailItemViewModel { Glyph = "Fnd", IsActive = false });
        items.Add(new NavRailItemViewModel { Glyph = "Frd", IsActive = false });
        return items;
    }

    private GameTileViewModel? _focusedGame;

    /// <summary>
    /// The currently focused game — drives the hero panel and the ambient background
    /// tint. Everything the hero shows binds through this single property, so moving
    /// along the row swaps the whole screen in one notification.
    /// </summary>
    public GameTileViewModel? FocusedGame
    {
        get => _focusedGame;
        private set
        {
            if (!ReferenceEquals(_focusedGame, value))
            {
                _focusedGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AmbientTintBrush));
            }
        }
    }

    /// <summary>
    /// Background wash colour for the focused game, as a soft radial gradient. This is
    /// what makes the whole screen react to selection — the single biggest visual gap
    /// versus a real console dashboard.
    /// </summary>
    public Brush AmbientTintBrush
    {
        get
        {
            var tint = FocusedGame?.Activity?.AmbientTint ?? Color.FromRgb(0xA8, 0xE0, 0x3B);
            return new RadialGradientBrush
            {
                GradientOrigin = new System.Windows.Point(0.72, 0.22),
                Center = new System.Windows.Point(0.72, 0.22),
                RadiusX = 0.9,
                RadiusY = 0.95,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x5C, tint.R, tint.G, tint.B), 0),
                    new GradientStop(Color.FromArgb(0x22, tint.R, tint.G, tint.B), 0.45),
                    new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1),
                },
            };
        }
    }

    public BatteryIconViewModel BatteryIcon { get; private set; } = new(BatteryUiState.Unknown);
    public string SoundRoutingStatusText { get; private set; } = "Audio: Not routed";

    public string ClockLabel => DateTime.Now.ToString("h:mm tt");

    /// <summary>Raised when a game is "launched" — carries the art brush, display name, and the real URI GameLauncher built.</summary>
    public event Action<Brush, string, string>? GameLaunchRequested;

    /// <summary>Raised when the user opens a game's detail page (Details button / Y).</summary>
    public event Action<GameTileViewModel>? GameDetailRequested;

    /// <summary>Raised whenever focus moves to a different game, so the view can scroll it into view.</summary>
    public event Action<GameTileViewModel>? FocusedTileChanged;

    private int _focusedIndex;

    private readonly SimulatedGamepadReader _gamepadReader = new();
    private readonly FakeAudioEndpointRouter _audioRouter = new();
    private readonly SoundDeviceEnforcer _soundDeviceEnforcer;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MainWindowViewModel()
    {
        _soundDeviceEnforcer = new SoundDeviceEnforcer(_audioRouter, tvHdmiEndpointId: "simulated-tv-hdmi-endpoint");

        var dbPath = Path.Combine(Path.GetTempPath(), "console_lib_ui_test.db");
        var database = new GameLibraryDatabase(dbPath);
        database.EnsureSchema();
        SampleLibrarySeeder.SeedIfEmpty(database);

        var entries = database.GetAll();

        double SimulatedPercent(GameEntry e) => e.Status == GameStatus.Downloading ? 61 : 0;

        // Installed games first (most-recently-played reads first on a console home
        // screen), then anything still downloading.
        var ordered = entries
            .OrderBy(e => e.Status == GameStatus.Downloading ? 1 : 0)
            .ToList();

        foreach (var entry in ordered)
        {
            Games.Add(GameTileViewModel.FromEntry(entry, SimulatedPercent(entry)));
        }

        if (Games.Count > 0)
        {
            _focusedIndex = 0;
            Games[0].IsFocused = true;
            FocusedGame = Games[0];
        }

        RefreshBatteryStatus();

        // Simulate the CEC "TV's HDMI audio endpoint just connected" event once at
        // startup, exercising SoundDeviceEnforcer end-to-end without real CEC hardware.
        _soundDeviceEnforcer.OnTvAudioEndpointConnected();
        SoundRoutingStatusText = $"Audio routed to: {_audioRouter.LastRoutedEndpointId}";

        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(DashboardTheme theme)
    {
        foreach (var game in Games)
        {
            game.RefreshArt();
        }
    }

    /// <summary>Left/Right: move focus along the game row. No wraparound.</summary>
    public void MoveFocus(int delta)
    {
        if (Games.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(_focusedIndex + delta, 0, Games.Count - 1);
        if (next == _focusedIndex)
        {
            return;
        }

        Games[_focusedIndex].IsFocused = false;
        _focusedIndex = next;
        Games[_focusedIndex].IsFocused = true;
        FocusedGame = Games[_focusedIndex];

        FocusedTileChanged?.Invoke(Games[_focusedIndex]);
    }

    /// <summary>Moves focus directly to a specific game — used when clicking a tile with the mouse.</summary>
    public void FocusGame(GameTileViewModel game)
    {
        var index = Games.IndexOf(game);
        if (index < 0 || index == _focusedIndex)
        {
            return;
        }

        Games[_focusedIndex].IsFocused = false;
        _focusedIndex = index;
        game.IsFocused = true;
        FocusedGame = game;

        FocusedTileChanged?.Invoke(game);
    }

    /// <summary>Opens the focused game's detail page.</summary>
    public void OpenFocusedDetails()
    {
        if (FocusedGame is not null)
        {
            GameDetailRequested?.Invoke(FocusedGame);
        }
    }

    /// <summary>
    /// Fires the launch swallow for the focused game, building the launch URI via
    /// GameLauncher.LaunchUriBuilder (same logic the real console would use). Never
    /// calls Process.Start — windowed test build on a personal laptop, see
    /// dev-environment-constraint memory.
    /// </summary>
    public void PlayFocusedGame()
    {
        if (FocusedGame is null)
        {
            return;
        }

        RaiseLaunch(FocusedGame.Entry, FocusedGame.ArtBrush, FocusedGame.GameName);
    }

    /// <summary>Fires the launch swallow for a specific game entry — called from the game detail screen's Play button.</summary>
    public void RaiseLaunch(GameEntry entry, Brush art, string gameName)
    {
        var uri = LaunchUriBuilder.Build(entry);
        GameLaunchRequested?.Invoke(art, gameName, uri);
    }

    /// <summary>Re-reads the wall clock for the dashboard's status line.</summary>
    public void RefreshClock() => OnPropertyChanged(nameof(ClockLabel));

    public void RefreshBatteryStatus()
    {
        var snapshot = _gamepadReader.GetBatteryInformation(userIndex: 0);
        var state = BatteryTelemetryMonitor.Classify(snapshot);
        BatteryIcon = new BatteryIconViewModel(state);
        OnPropertyChanged(nameof(BatteryIcon));
    }
}
