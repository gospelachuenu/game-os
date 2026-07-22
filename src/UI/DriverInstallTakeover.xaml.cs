using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace UI;

/// <summary>
/// Full-screen modal shown while a driver installs (plan.md §6.1). Locks the UI: no
/// back affordance, no rail, and MainWindow suppresses all navigation input while it
/// is visible. Uses an indeterminate sweeping bar rather than a percentage because
/// silent AMD driver packages report no linear progress (§6.2, State 2).
///
/// In this test build nothing is actually installed — Show() runs for a fixed
/// simulated duration then completes, so the screen and its warning copy can be
/// exercised without touching real drivers. See dev-environment-constraint memory:
/// no system-level operations run on this machine.
/// </summary>
public partial class DriverInstallTakeover : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DriverInstallTakeover),
            new PropertyMetadata("AMD Adrenalin Graphics Driver"));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(DriverInstallTakeover),
            new PropertyMetadata("Version 24.7.1 · AMD Radeon RX 6600M"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Raised when the simulated install finishes and the takeover has closed.</summary>
    public event EventHandler? Completed;

    private readonly System.Windows.Threading.DispatcherTimer _simulatedInstallTimer =
        new() { Interval = TimeSpan.FromSeconds(6) };

    private Storyboard? _sweep;
    private Storyboard? _pulse;

    public DriverInstallTakeover()
    {
        InitializeComponent();

        _simulatedInstallTimer.Tick += (_, _) =>
        {
            _simulatedInstallTimer.Stop();
            Hide();
        };
    }

    public void Show(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));

        StartSweep();
        StartPulse();
        _simulatedInstallTimer.Start();
    }

    private void Hide()
    {
        _sweep?.Stop();
        _pulse?.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.3));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            Completed?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Sweeps the highlight bar across the track forever. Width is read at run time so
    /// the sweep spans whatever the modal's actual laid-out width turns out to be.
    /// </summary>
    private void StartSweep()
    {
        // Defer until layout has run — TrackCanvas.ActualWidth reads 0 immediately
        // after Visibility flips (WPF does not lay out synchronously).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var travel = TrackCanvas.ActualWidth + SweepBar.Width;

            var slide = new DoubleAnimation
            {
                From = -SweepBar.Width,
                To = TrackCanvas.ActualWidth,
                Duration = TimeSpan.FromSeconds(1.5),
                RepeatBehavior = RepeatBehavior.Forever,
            };

            Storyboard.SetTarget(slide, SweepBar);
            Storyboard.SetTargetProperty(slide, new PropertyPath("(Canvas.Left)"));

            _sweep = new Storyboard();
            _sweep.Children.Add(slide);
            _sweep.Begin();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Slow pulse on the status dot so the screen reads as alive during a long install.</summary>
    private void StartPulse()
    {
        var pulse = new DoubleAnimation
        {
            From = 1.0,
            To = 0.25,
            Duration = TimeSpan.FromSeconds(0.9),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };

        Storyboard.SetTarget(pulse, PulseDot);
        Storyboard.SetTargetProperty(pulse, new PropertyPath("Opacity"));

        _pulse = new Storyboard();
        _pulse.Children.Add(pulse);
        _pulse.Begin();
    }
}
