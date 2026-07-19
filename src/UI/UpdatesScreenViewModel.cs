using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI;

/// <summary>
/// Backs the Updates screen. Entirely mock data for this windowed test build — the
/// real screen would source this from MaintenanceHub, which itself would call the
/// Windows Update Agent API (covers both OS updates and AMD driver updates that
/// Windows Update distributes; AMD's own Adrenalin-side driver push is not something
/// a third-party app can trigger, so it is deliberately out of scope here).
/// </summary>
public sealed class UpdatesScreenViewModel : INotifyPropertyChanged
{
    public ObservableCollection<UpdateItemViewModel> Updates { get; } = new();

    private UpdateItemViewModel? _selectedUpdate;
    public UpdateItemViewModel? SelectedUpdate
    {
        get => _selectedUpdate;
        set
        {
            if (!ReferenceEquals(_selectedUpdate, value))
            {
                _selectedUpdate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasAvailableUpdates => Updates.Any(u => u.State != UpdateItemState.Installed);

    public int AvailableCount => Updates.Count(u => u.State != UpdateItemState.Installed);

    public string LastCheckedLabel => "Last checked just now (simulated)";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public UpdatesScreenViewModel()
    {
        Updates.Add(new UpdateItemViewModel
        {
            Title = "2026-07 Cumulative Update for Windows 11",
            Kind = UpdateKind.WindowsUpdate,
            Version = "KB5041234",
            SizeLabel = "612 MB",
            State = UpdateItemState.Available,
            PatchNotes = "• Improves reliability of HDMI-CEC power state switching\n" +
                         "• Fixes a controller reconnect issue affecting some Bluetooth gamepads\n" +
                         "• Addresses a memory leak in the shell notification area\n" +
                         "• General security and stability improvements",
        });

        Updates.Add(new UpdateItemViewModel
        {
            Title = "AMD Adrenalin Graphics Driver",
            Kind = UpdateKind.AmdDriver,
            Version = "24.7.1",
            SizeLabel = "748 MB",
            State = UpdateItemState.ReadyToInstall,
            PatchNotes = "• Adds day-one optimization for recently released titles\n" +
                         "• Fixes a rare flicker on variable refresh rate displays when switching between windowed and fullscreen\n" +
                         "• Improves encode quality for game capture at high frame rates\n" +
                         "• Resolves an issue where GPU fan curves reset after sleep",
        });

        Updates.Add(new UpdateItemViewModel
        {
            Title = "Windows Feature Update, Version 25H1",
            Kind = UpdateKind.WindowsUpdate,
            Version = "Build 27810",
            SizeLabel = "3.8 GB",
            State = UpdateItemState.Downloading,
            DownloadProgressPercent = 42,
            PatchNotes = "• New console-mode power and sleep controls\n" +
                         "• Updated Bluetooth stack with faster controller pairing\n" +
                         "• Refreshed default HDR calibration flow\n" +
                         "• Numerous under-the-hood performance improvements",
        });

        Updates.Add(new UpdateItemViewModel
        {
            Title = "AMD Chipset Driver",
            Kind = UpdateKind.AmdDriver,
            Version = "6.02.25.632",
            SizeLabel = "94 MB",
            State = UpdateItemState.Available,
            PatchNotes = "• Improves PCIe link stability under heavy load\n" +
                         "• Minor USB controller power management fixes",
        });

        SelectedUpdate = Updates.FirstOrDefault();
    }

    /// <summary>
    /// Drops an update out of the list once it finishes installing — an installed
    /// update is no longer a pending/available update, so it should not linger in the
    /// list. Returns the item that should take focus next (the neighbour that slides
    /// into the removed slot), or null if the list is now empty.
    /// </summary>
    public UpdateItemViewModel? RemoveCompleted(UpdateItemViewModel completed)
    {
        var index = Updates.IndexOf(completed);
        if (index < 0)
        {
            return SelectedUpdate;
        }

        Updates.Remove(completed);
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(HasAvailableUpdates));

        if (Updates.Count == 0)
        {
            SelectedUpdate = null;
            return null;
        }

        return Updates[Math.Clamp(index, 0, Updates.Count - 1)];
    }
}
