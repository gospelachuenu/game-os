using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Full-screen "launching a game" takeover that visually grows out of the tile the
/// user selected (a "swallow" transition) rather than appearing as a separate
/// overlay. Never actually starts a real process (windowed test build on the user's
/// personal laptop — see dev-environment-constraint memory); simulates the
/// launch → ready flow purely visually.
/// </summary>
public partial class LaunchOverlay : UserControl
{
    public static readonly DependencyProperty GameArtProperty =
        DependencyProperty.Register(nameof(GameArt), typeof(Brush), typeof(LaunchOverlay), new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty GameNameProperty =
        DependencyProperty.Register(nameof(GameName), typeof(string), typeof(LaunchOverlay), new PropertyMetadata(string.Empty));

    public Brush GameArt
    {
        get => (Brush)GetValue(GameArtProperty);
        set => SetValue(GameArtProperty, value);
    }

    public string GameName
    {
        get => (string)GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    private readonly DispatcherTimer _readyTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private readonly DispatcherTimer _dismissTimer = new() { Interval = TimeSpan.FromSeconds(1.1) };

    public event EventHandler? Dismissed;

    public LaunchOverlay()
    {
        InitializeComponent();

        _readyTimer.Tick += (_, _) =>
        {
            _readyTimer.Stop();
            TransitionToReady();
        };

        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            Hide();
        };
    }

    /// <summary>
    /// startRect is the launched tile's bounds relative to this control's own root
    /// (obtained via tile.TransformToVisual(this)) — the panel is placed exactly over
    /// the tile, then its size/position animate to fill the whole control, so the
    /// tile appears to grow directly into the full-screen splash.
    /// </summary>
    public void Show(Rect startRect, Brush art, string gameName)
    {
        // Stop any in-flight timers/animations left over from a previous Show()/Hide()
        // cycle (e.g. the user launched a second tile while the first launch's
        // dismiss fade-out was still animating). Without this, a leftover Opacity
        // animation from Hide() can keep driving this control back toward 0 right
        // after Show() sets Visibility=Visible, making the overlay flash briefly
        // then disappear instead of staying open.
        _readyTimer.Stop();
        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        GameArt = art;
        GameName = gameName;
        StatusText.Text = "LAUNCHING";
        SpinnerArc.BeginAnimation(OpacityProperty, null);
        SpinnerArc.Opacity = 1;
        SpinnerArc.Visibility = Visibility.Visible;
        ReadyCheck.BeginAnimation(OpacityProperty, null);
        ReadyCheck.Opacity = 0;
        StatusPanel.BeginAnimation(OpacityProperty, null);
        StatusPanel.Opacity = 0;
        IsHitTestVisible = true;
        Visibility = Visibility.Visible;

        Panel.Width = startRect.Width;
        Panel.Height = startRect.Height;
        Panel.Margin = new Thickness(startRect.Left, startRect.Top, 0, 0);
        Panel.CornerRadius = new CornerRadius(18);

        // Setting Visibility=Visible does not make a layout pass happen
        // synchronously — ActualWidth/ActualHeight on this control still read as 0
        // immediately after the assignment above (this control was Collapsed a
        // moment ago), so the grow-to-fullscreen animation would previously animate
        // toward 0 instead of toward the real window size. Deferring to a Loaded-
        // priority dispatcher callback lets a layout pass happen first.
        Dispatcher.BeginInvoke(new Action(() => BeginGrowAnimation(startRect)), DispatcherPriority.Loaded);

        _readyTimer.Stop();
        _readyTimer.Start();
    }

    private void BeginGrowAnimation(Rect startRect)
    {
        var targetWidth = ActualWidth;
        var targetHeight = ActualHeight;

        var duration = TimeSpan.FromSeconds(0.55);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        Panel.BeginAnimation(WidthProperty, new DoubleAnimation(startRect.Width, targetWidth, duration) { EasingFunction = easing });
        Panel.BeginAnimation(HeightProperty, new DoubleAnimation(startRect.Height, targetHeight, duration) { EasingFunction = easing });
        Panel.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(startRect.Left, startRect.Top, 0, 0),
            new Thickness(0),
            duration) { EasingFunction = easing });

        // CornerRadius has no built-in WPF animation type; fade it to square via a
        // few discrete steps timed against the same duration instead.
        var radiusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration.TotalSeconds / 4) };
        var radiusStep = 18;
        radiusTimer.Tick += (_, _) =>
        {
            radiusStep -= 6;
            Panel.CornerRadius = new CornerRadius(Math.Max(radiusStep, 0));
            if (radiusStep <= 0)
            {
                radiusTimer.Stop();
            }
        };
        radiusTimer.Start();

        var statusFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35)) { BeginTime = TimeSpan.FromSeconds(0.3) };
        StatusPanel.BeginAnimation(OpacityProperty, statusFadeIn);
    }

    private void TransitionToReady()
    {
        StatusText.Text = "READY";

        var spinnerFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25));
        spinnerFadeOut.Completed += (_, _) => SpinnerArc.Visibility = Visibility.Collapsed;
        SpinnerArc.BeginAnimation(OpacityProperty, spinnerFadeOut);

        ReadyCheck.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)) { BeginTime = TimeSpan.FromSeconds(0.15) });

        _dismissTimer.Stop();
        _dismissTimer.Start();
    }

    private void Hide()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            Opacity = 1;
            Dismissed?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
