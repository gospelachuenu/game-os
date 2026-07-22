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

    /// <summary>
    /// Raised once the splash has fully covered the screen and the app behind it can be
    /// prepared unseen. Apps only — a game's splash is purely decorative.
    /// </summary>
    public event EventHandler? AppSplashCovered;

    /// <summary>
    /// Raised the instant before the splash fades away, so the app can make itself
    /// visible under cover rather than appearing through it while still loading.
    /// </summary>
    public event EventHandler? AppAboutToUncover;

    /// <summary>
    /// True while this splash is standing in for an APP rather than a game.
    ///
    /// The difference matters: a game's splash runs on a timer because nothing real is
    /// being started, whereas an app's must wait for the actual page to load. Dismissing
    /// on a timer would uncover a half-loaded browser.
    /// </summary>
    private bool _isAppLaunch;

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

        _isAppLaunch = false;

        // A game keeps the scrim and vignette: its cover art can be any brightness, and
        // the launching text has to stay readable over it. An app launch collapses these,
        // so restore them here.
        Scrim.BeginAnimation(OpacityProperty, null);
        Scrim.Opacity = 1;
        Scrim.Visibility = Visibility.Visible;
        Vignette.BeginAnimation(OpacityProperty, null);
        Vignette.Opacity = 1;
        Vignette.Visibility = Visibility.Visible;
        BlackOut.BeginAnimation(OpacityProperty, null);
        BlackOut.Opacity = 0;
        AppTextShade.BeginAnimation(OpacityProperty, null);
        AppTextShade.Opacity = 0;

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

    /// <summary>
    /// Launch splash for an app: same grow-from-the-tile transition as a game, but held
    /// open until <see cref="CompleteAppLaunch"/> says the app is actually ready rather
    /// than dismissed on a timer.
    /// </summary>
    public void ShowForApp(Rect startRect, ConsoleApp app)
    {
        _readyTimer.Stop();
        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        _isAppLaunch = true;
        _appLaunchStartedUtc = DateTime.UtcNow;

        GameArt = app.Art;
        GameName = app.Name;
        StatusText.Text = app.Category.ToUpperInvariant();

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
        Panel.CornerRadius = new CornerRadius(14);

        // Clear the black frame a previous handover left behind, or this launch would
        // start already blacked out.
        BlackOut.BeginAnimation(OpacityProperty, null);
        BlackOut.Opacity = 0;

        // NO SCRIM FOR AN APP. The 60% black scrim and vignette exist so a game's
        // LAUNCHING text stays readable over cover art of unknown brightness — but an
        // app's art is a flat brand colour chosen here, and darkening it just turns
        // YouTube red into muddy maroon.
        //
        // COLLAPSED, not transparent: opacity alone left them in the tree where a
        // leftover animation clock could drive them back up mid-launch. Removing them
        // from rendering entirely is the only way to be certain they cannot tint anything.
        Scrim.BeginAnimation(OpacityProperty, null);
        Scrim.Visibility = Visibility.Collapsed;
        Vignette.BeginAnimation(OpacityProperty, null);
        Vignette.Visibility = Visibility.Collapsed;

        // Soft pool of shade behind the wordmark instead, fading in with the text.
        AppTextShade.BeginAnimation(OpacityProperty, null);
        AppTextShade.Opacity = 0;
        AppTextShade.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
            {
                BeginTime = TimeSpan.FromSeconds(0.3),
            });

        Dispatcher.BeginInvoke(new Action(() =>
        {
            BeginGrowAnimation(startRect);

            // Tell the host once the splash covers the screen, so the app can be brought
            // up behind it without the user seeing it assemble.
            var covered = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.55) };
            covered.Tick += (_, _) =>
            {
                covered.Stop();
                AppSplashCovered?.Invoke(this, EventArgs.Empty);
            };
            covered.Start();
        }), DispatcherPriority.Loaded);

        // No _readyTimer here: an app is ready when it says so, not after 1.8 seconds.
    }

    /// <summary>
    /// Called when the app behind the splash has finished loading. Shows the ready tick
    /// briefly, then uncovers the app.
    /// </summary>
    public void CompleteAppLaunch()
    {
        if (!_isAppLaunch || Visibility != Visibility.Visible)
        {
            return;
        }

        // The page can be ready almost immediately — a warm WebView2 fires ContentLoading
        // within a few hundred milliseconds, while the tile is still growing and the
        // spinner has barely appeared. Handing over at that moment guts the launch: the
        // splash is cut off mid-animation and the app snaps in.
        //
        // So readiness only ARMS the handover; the splash still plays its minimum. If the
        // page was slow, this has already elapsed and the handover runs immediately.
        var elapsed = DateTime.UtcNow - _appLaunchStartedUtc;
        var remaining = MinimumAppSplash - elapsed;

        if (remaining <= TimeSpan.Zero)
        {
            Hide();
            return;
        }

        var wait = new DispatcherTimer { Interval = remaining };
        wait.Tick += (_, _) =>
        {
            wait.Stop();

            // Still the same launch? A second one may have started in the meantime.
            if (_isAppLaunch && Visibility == Visibility.Visible)
            {
                Hide();
            }
        };
        wait.Start();
    }

    /// <summary>
    /// How long an app's splash always plays for, however fast the page loads.
    ///
    /// Covers the grow-from-tile (0.55s) plus enough of the branding to register as a
    /// launch rather than a flicker. Long enough to feel deliberate, short enough not to
    /// be standing between the user and content that is already loaded.
    /// </summary>
    private static readonly TimeSpan MinimumAppSplash = TimeSpan.FromSeconds(1.9);

    private DateTime _appLaunchStartedUtc;

    /// <summary>
    /// Plays the launch in reverse: the app's panel shrinks from full screen back into
    /// its tile, so it is obvious where the thing went.
    /// </summary>
    public void ShrinkToTile(Rect targetRect, ConsoleApp app, Action onComplete)
    {
        _readyTimer.Stop();
        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        _isAppLaunch = false;

        GameArt = app.Art;
        GameName = app.Name;

        // No status text on the way out — "LAUNCHING" while closing would be nonsense,
        // and a closing animation wants less on screen, not more.
        StatusPanel.BeginAnimation(OpacityProperty, null);
        StatusPanel.Opacity = 0;
        SpinnerArc.Visibility = Visibility.Collapsed;
        ReadyCheck.BeginAnimation(OpacityProperty, null);
        ReadyCheck.Opacity = 0;

        // Clean brand colour on the way out too — the scrims would make the shrinking
        // panel a different, darker colour than the tile it is returning to.
        Scrim.BeginAnimation(OpacityProperty, null);
        Scrim.Visibility = Visibility.Collapsed;
        Vignette.BeginAnimation(OpacityProperty, null);
        Vignette.Visibility = Visibility.Collapsed;
        AppTextShade.BeginAnimation(OpacityProperty, null);
        AppTextShade.Opacity = 0;
        BlackOut.BeginAnimation(OpacityProperty, null);
        BlackOut.Opacity = 0;

        IsHitTestVisible = true;
        Visibility = Visibility.Visible;

        Panel.Width = ActualWidth;
        Panel.Height = ActualHeight;
        Panel.Margin = new Thickness(0);
        Panel.CornerRadius = new CornerRadius(0);

        var duration = TimeSpan.FromSeconds(0.42);

        // EaseIn on the way out against the launch's EaseInOut: closing should feel
        // decisive — it accelerates away rather than easing to a gentle stop.
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        Panel.BeginAnimation(WidthProperty,
            new DoubleAnimation(ActualWidth, targetRect.Width, duration) { EasingFunction = easing });
        Panel.BeginAnimation(HeightProperty,
            new DoubleAnimation(ActualHeight, targetRect.Height, duration) { EasingFunction = easing });
        Panel.BeginAnimation(MarginProperty, new ThicknessAnimation(
            new Thickness(0),
            new Thickness(targetRect.Left, targetRect.Top, 0, 0),
            duration) { EasingFunction = easing });

        // Corners round off as it returns to tile shape, the inverse of the launch.
        var radiusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration.TotalSeconds / 4) };
        var radiusStep = 0;
        radiusTimer.Tick += (_, _) =>
        {
            radiusStep += 4;
            Panel.CornerRadius = new CornerRadius(Math.Min(radiusStep, 14));
            if (radiusStep >= 14)
            {
                radiusTimer.Stop();
            }
        };
        radiusTimer.Start();

        // Fades out over the last stretch, so the panel dissolves into the tile rather
        // than landing on top of it and popping out of existence.
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.16))
        {
            BeginTime = TimeSpan.FromSeconds(0.3),
        };

        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            Opacity = 1;
            onComplete();
        };

        BeginAnimation(OpacityProperty, fade);
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
        if (_isAppLaunch)
        {
            _isAppLaunch = false;
            HandOverToApp();
            return;
        }

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

    /// <summary>
    /// Hands the screen from the splash to a loaded app as three DISTINCT steps:
    /// the splash finishes, the screen settles to black, then the app appears.
    ///
    /// Deliberately not a cross-fade. The splash is the app's colour under a heavy black
    /// scrim and a vignette, so dissolving it directly into the page drags that darkness
    /// across YouTube — the page looks dimmed and grubby rather than revealed. Passing
    /// through a clean black frame instead makes the darkness a deliberate beat rather
    /// than a smear over content, and the app then arrives against a neutral ground.
    /// </summary>
    private void HandOverToApp()
    {
        // 1. Let the splash finish: the branding lifts away on its own, uninterrupted,
        //    taking the shade behind it with it.
        var textOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.28))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        StatusPanel.BeginAnimation(OpacityProperty, textOut);
        AppTextShade.BeginAnimation(OpacityProperty, textOut);

        // 2. Reveal the loaded page UNDERNEATH, while the splash is still fully opaque.
        //    Nothing of it is visible yet — this only puts it in place to be uncovered.
        var reveal = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.3) };
        reveal.Tick += (_, _) =>
        {
            reveal.Stop();
            AppAboutToUncover?.Invoke(this, EventArgs.Empty);

            // 3. Then dissolve the splash off it. One clean fade from the app's colour
            //    straight to the page — no intermediate black frame, which only ever
            //    added a step between the user and content that was already loaded.
            var lift = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(0.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };

            lift.Completed += (_, _) =>
            {
                Visibility = Visibility.Collapsed;
                IsHitTestVisible = false;
                Opacity = 1;
                Dismissed?.Invoke(this, EventArgs.Empty);
            };

            BeginAnimation(OpacityProperty, lift);
        };

        reveal.Start();
    }
}
