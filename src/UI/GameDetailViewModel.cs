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

    // Recommended graphics settings CHOSEN for the detected hardware (see
    // RecommendedSettingsAdvisor), not a fixed constant — so it reflects the machine
    // it's running on. Hardware is mocked in this test build (DetectedHardware.Simulated).
    public string RecommendedPreset { get; }
    public string RecommendedResolution { get; }
    public string RecommendedFrameRate { get; }
    public string RecommendedHdr { get; }
    public string RecommendedBasis { get; }

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

        var recommended = RecommendedSettingsAdvisor.Recommend(DetectedHardware.Simulated);
        RecommendedPreset = recommended.Preset;
        RecommendedResolution = recommended.Resolution;
        RecommendedFrameRate = recommended.FrameRate;
        RecommendedHdr = recommended.Hdr;
        RecommendedBasis = recommended.BasisSummary;
    }
}
