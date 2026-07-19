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
            }
        }
    }

    public string Label => $"Player {SlotNumber + 1}";

    public ControllerSlotViewModel(int slotNumber)
    {
        SlotNumber = slotNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
