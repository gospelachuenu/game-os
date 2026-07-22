using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using InputDaemon;

namespace UI;

/// <summary>
/// Controller setup wizard: polls all 4 XInput slots (0-3) live and tracks each
/// slot's state (NotConnected / Connected / Confirmed-by-any-button-press). Opened
/// on demand from the Guide Menu. Uses the same IGamepadReader abstraction the real
/// navigation poller uses, just queried across all 4 user indices instead of only 0.
/// </summary>
public partial class ControllerWizard : UserControl
{
    public ObservableCollection<ControllerSlotViewModel> Slots { get; } = new();

    private readonly IGamepadReader _reader;
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public event EventHandler? CloseRequested;

    public ControllerWizard()
    {
        InitializeComponent();

        for (var slot = 0; slot < 4; slot++)
        {
            Slots.Add(new ControllerSlotViewModel(slot));
        }

        _reader = new XInputGamepadReader();
        _pollTimer.Tick += (_, _) => Poll();
    }

    public void Show()
    {
        foreach (var slot in Slots)
        {
            slot.State = ControllerSlotState.NotConnected;
        }

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));

        _pollTimer.Start();
    }

    public void Hide()
    {
        _pollTimer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.2));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void Poll()
    {
        foreach (var slot in Slots)
        {
            var snapshot = _reader.GetState(slot.SlotNumber);

            if (!snapshot.IsConnected)
            {
                slot.State = ControllerSlotState.NotConnected;
                continue;
            }

            if (slot.State == ControllerSlotState.NotConnected)
            {
                slot.State = ControllerSlotState.Connected;
            }

            if (slot.State == ControllerSlotState.Connected && snapshot.Buttons != 0)
            {
                slot.State = ControllerSlotState.Confirmed;
            }
        }
    }
}
