using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ConsoleLibrary;

namespace UI;

/// <summary>
/// Backs the game detail screen shown when a game tile is selected (replacing the
/// old straight-to-launch swallow). Carries everything the detail screen presents:
/// the game's art/thumbnail, provider/status info, a simulated "update pending"
/// download state (progress bar), and mock recommended graphics settings. Most of
/// this is placeholder for the test build — the real build would source update state
/// from MaintenanceHub and recommended settings from a per-game profile in
/// HardwareManager.
/// </summary>
public sealed class GameDetailViewModel : INotifyPropertyChanged
{
    private double _updateProgressPercent;
    private bool _hasUpdatePending;

    public GameEntry Entry { get; }
    public string GameName { get; }
    public Brush ArtBrush { get; }
    public ImageSource? IconSource { get; }
    public string ProviderLabel { get; }
    public string StatusLabel { get; }

    // NOTE: this used to expose "recommended settings" (preset/resolution/fps/HDR)
    // for the detail screen. Removed 2026-07-19 — it produced identical values for
    // every game because it only ever looked at the GPU, so it presented a fiction as
    // a recommendation. Genuinely per-game settings would need per-engine knowledge of
    // what each option costs, and the console can neither read nor write a game's own
    // config; games auto-detect far better than we could. See RecommendedSettingsAdvisor
    // (kept, unused) for the logic if this is ever revisited from measured data.

    /// <summary>Simulated play activity (last played / play time / size / achievements) — see GameActivityProvider.</summary>
    public GameActivity Activity { get; }

    /// <summary>Accent wash for this game, matching the tint the Spotlight home screen uses.</summary>
    public Brush AmbientTintBrush { get; }

    public bool HasUpdatePending
    {
        get => _hasUpdatePending;
        set
        {
            if (_hasUpdatePending != value)
            {
                _hasUpdatePending = value;
                OnPropertyChanged();
            }
        }
    }

    public double UpdateProgressPercent
    {
        get => _updateProgressPercent;
        set
        {
            if (Math.Abs(_updateProgressPercent - value) > double.Epsilon)
            {
                _updateProgressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public GameDetailViewModel(GameTileViewModel tile)
    {
        Entry = tile.Entry;
        GameName = tile.GameName;
        ArtBrush = tile.ArtBrush;
        IconSource = tile.IconSource;

        ProviderLabel = Entry.Provider.ToString().ToUpperInvariant();

        StatusLabel = Entry.Status switch
        {
            GameStatus.Installed => "Installed",
            GameStatus.Downloading => "Downloading",
            _ => "Not installed",
        };

        // Downloading games stand in for "update in progress" on the detail screen;
        // installed games have no pending update in this mock.
        _hasUpdatePending = Entry.Status == GameStatus.Downloading;
        _updateProgressPercent = _hasUpdatePending ? tile.ProgressPercent : 0;

        Activity = tile.Activity ?? GameActivityProvider.For(Entry);

        var tint = Activity.AmbientTint;
        AmbientTintBrush = new RadialGradientBrush
        {
            GradientOrigin = new System.Windows.Point(0.72, 0.2),
            Center = new System.Windows.Point(0.72, 0.2),
            RadiusX = 0.9,
            RadiusY = 0.95,
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x52, tint.R, tint.G, tint.B), 0),
                new GradientStop(Color.FromArgb(0x1E, tint.R, tint.G, tint.B), 0.45),
                new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1),
            },
        };

    }
}
