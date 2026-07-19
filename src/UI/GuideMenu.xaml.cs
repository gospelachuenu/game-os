using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Quick-access side menu (plan.md §9.1): slides in from the right over whatever is
/// currently on screen. Toggled by Tab in this test build. Volume/backlight sliders
/// and the overlay toggle are visual-only here (IsHitTestVisible="False" on the
/// controls themselves) since there's no real audio/DDC-CI/overlay backend wired to
/// this test build yet — they exist to prove the panel layout, not to control
/// anything real.
/// </summary>
public partial class GuideMenu : UserControl
{
    public static readonly DependencyProperty BatteryIconProperty =
        DependencyProperty.Register(nameof(BatteryIcon), typeof(BatteryIconViewModel), typeof(GuideMenu), new PropertyMetadata(null));

    public BatteryIconViewModel? BatteryIcon
    {
        get => (BatteryIconViewModel?)GetValue(BatteryIconProperty);
        set => SetValue(BatteryIconProperty, value);
    }

    public static readonly DependencyProperty SelectedActionIndexProperty =
        DependencyProperty.Register(nameof(SelectedActionIndex), typeof(int), typeof(GuideMenu), new PropertyMetadata(0));

    /// <summary>
    /// Kept at a constant 0 — Set Up Controller is the only gamepad-activatable
    /// action in this menu now that Updates moved to its own dedicated rail button.
    /// Still exposed (rather than deleted outright) so the existing highlight
    /// DataTrigger in GuideMenu.xaml and MainWindow's MoveSelection/ActivateSelected
    /// calls keep working unchanged if a second action item is ever added back here.
    /// </summary>
    public int SelectedActionIndex
    {
        get => (int)GetValue(SelectedActionIndexProperty);
        set => SetValue(SelectedActionIndexProperty, value);
    }

    private const int ActionCount = 1;

    public bool IsOpen { get; private set; }

    public event EventHandler? ControllerSetupRequested;

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _overlayEnabled = true;

    public GuideMenu()
    {
        InitializeComponent();

        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();

        UpdateOverlayToggleVisual(animate: false);
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("h:mm tt");
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        IsOpen = true;
        IsHitTestVisible = true;
        Panel.IsHitTestVisible = true;
        Scrim.IsHitTestVisible = true;
        SelectedActionIndex = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(PanelTransform.X, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = easing });
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
    }

    public void Close()
    {
        IsOpen = false;

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slideOut = new DoubleAnimation(PanelTransform.X, Panel.Width, TimeSpan.FromSeconds(0.25)) { EasingFunction = easing };
        var fadeOut = new DoubleAnimation(Scrim.Opacity, 0, TimeSpan.FromSeconds(0.25));
        fadeOut.Completed += (_, _) =>
        {
            Panel.IsHitTestVisible = false;
            Scrim.IsHitTestVisible = false;
            IsHitTestVisible = false;
        };

        PanelTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        Scrim.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void ToggleOverlay()
    {
        _overlayEnabled = !_overlayEnabled;
        UpdateOverlayToggleVisual(animate: true);
    }

    /// <summary>Moves the gamepad highlight between the menu's action buttons. No wraparound, matching dashboard row navigation.</summary>
    public void MoveSelection(int delta)
    {
        SelectedActionIndex = Math.Clamp(SelectedActionIndex + delta, 0, ActionCount - 1);
    }

    /// <summary>Activates whichever action button currently has the gamepad highlight — the Confirm-button equivalent of clicking it.</summary>
    public void ActivateSelected()
    {
        if (SelectedActionIndex == 0)
        {
            Close();
            ControllerSetupRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ControllerSetupButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ControllerSetupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOverlayToggleVisual(bool animate)
    {
        var targetColor = _overlayEnabled
            ? (Color)ColorConverter.ConvertFromString("#33D17A")
            : (Color)ColorConverter.ConvertFromString("#2A2A33");

        OverlayPill.Background = new SolidColorBrush(targetColor);

        var targetMargin = _overlayEnabled ? new Thickness(23, 0, 0, 0) : new Thickness(3, 0, 0, 0);
        if (animate)
        {
            OverlayKnob.BeginAnimation(MarginProperty, new ThicknessAnimation(OverlayKnob.Margin, targetMargin, TimeSpan.FromSeconds(0.18)));
        }
        else
        {
            OverlayKnob.Margin = targetMargin;
        }
    }
}
