using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI;

public enum UpdateKind
{
    WindowsUpdate,
    AmdDriver,
}

public enum UpdateItemState
{
    Available,
    Downloading,
    ReadyToInstall,
    Installed,
}

/// <summary>
/// A single update entry shown on the Updates screen — either a Windows Update or an
/// AMD driver update (per the MaintenanceHub design: both real update classes route
/// through the Windows Update Agent API on real hardware, AMD driver-specific tools
/// like Adrenalin are not something a third-party app can trigger). This is mock data
/// for the test screen; the real build would populate this from MaintenanceHub.
/// </summary>
public sealed class UpdateItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private UpdateItemState _state;

    private double _downloadProgressPercent;

    public required string Title { get; init; }
    public required UpdateKind Kind { get; init; }
    public required string Version { get; init; }
    public required string SizeLabel { get; init; }
    public required string PatchNotes { get; init; }

    /// <summary>0-100. Only meaningful while State == Downloading; drives the detail panel's progress bar and percent label.</summary>
    public double DownloadProgressPercent
    {
        get => _downloadProgressPercent;
        set
        {
            if (_downloadProgressPercent != value)
            {
                _downloadProgressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public required UpdateItemState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string KindLabel => Kind == UpdateKind.WindowsUpdate ? "WINDOWS UPDATE" : "AMD DRIVER";

    public string StateLabel => State switch
    {
        UpdateItemState.Available => "Available",
        UpdateItemState.Downloading => "Downloading…",
        UpdateItemState.ReadyToInstall => "Ready to install",
        UpdateItemState.Installed => "Installed",
        _ => "Unknown",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
