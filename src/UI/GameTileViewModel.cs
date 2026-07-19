using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ConsoleLibrary;

namespace UI;

public sealed class GameTileViewModel : INotifyPropertyChanged
{
    private bool _isFocused;
    private Brush _artBrush;

    public required string GameName { get; init; }
    public required string StatusLabel { get; init; }
    public required bool IsDownloading { get; init; }
    public required GameEntry Entry { get; init; }
    public ImageSource? IconSource { get; init; }

    /// <summary>Download/playback progress 0-100. Only meaningful for downloading tiles and the hero's continue-playing tile.</summary>
    public double ProgressPercent { get; init; }

    /// <summary>Simulated play activity + the ambient tint colour for this game — see GameActivityProvider.</summary>
    public GameActivity Activity { get; init; } = null!;

    /// <summary>Provider name in caps for the hero kicker ("STEAM" / "EPIC").</summary>
    public string ProviderLabel => Entry.Provider.ToString().ToUpperInvariant();

    public Brush ArtBrush
    {
        get => _artBrush;
        private set
        {
            _artBrush = value;
            OnPropertyChanged();
        }
    }

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnPropertyChanged();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    private GameTileViewModel(GameEntry entry, string statusLabel, bool isDownloading)
    {
        Entry = entry;
        GameName = entry.GameName;
        StatusLabel = statusLabel;
        IsDownloading = isDownloading;
        _artBrush = TileArtGenerator.GenerateGradient(entry.GameName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Re-derives ArtBrush from the currently active theme. Called on ThemeManager.ThemeChanged.</summary>
    public void RefreshArt() => ArtBrush = TileArtGenerator.GenerateGradient(GameName);

    public static GameTileViewModel FromEntry(GameEntry entry, double simulatedDownloadPercent = 0)
    {
        var isDownloading = entry.Status == GameStatus.Downloading;

        var statusLabel = entry.Status switch
        {
            GameStatus.Installed => "Ready",
            GameStatus.Downloading => $"Downloading {simulatedDownloadPercent:0}%",
            _ => "Unknown",
        };

        return new GameTileViewModel(entry, statusLabel, isDownloading)
        {
            IconSource = GameIconLoader.TryLoad(entry.ImageUrl),
            ProgressPercent = simulatedDownloadPercent,
            Activity = GameActivityProvider.For(entry),
        };
    }
}
