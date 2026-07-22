using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI;

public enum ControllerSlotState
{
    NotConnected,
    Connected,
    Confirmed,
}

public sealed class ControllerSlotViewModel : INotifyPropertyChanged
{
    public int SlotNumber { get; }

    private ControllerSlotState _state = ControllerSlotState.NotConnected;
    public ControllerSlotState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(IsLit));
            }
        }
    }

    public string Label => $"PLAYER {SlotNumber + 1}";

    /// <summary>Short state caption shown under each pad glyph on the setup screen.</summary>
    public string StateLabel => State switch
    {
        ControllerSlotState.Confirmed => "READY",
        ControllerSlotState.Connected => "PRESS ANY BUTTON",
        _ => "EMPTY SLOT",
    };

    /// <summary>True while this slot has a controller lit up (connected or confirmed) — drives the pad glyph's accent styling.</summary>
    public bool IsLit => State != ControllerSlotState.NotConnected;

    public ControllerSlotViewModel(int slotNumber)
    {
        SlotNumber = slotNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
