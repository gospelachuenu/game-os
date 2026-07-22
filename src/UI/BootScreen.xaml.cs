using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UI;

/// <summary>
/// The startup steps the boot screen reports, in the order they complete. Only
/// AmbientReady is genuinely asynchronous — the library work all happens
/// synchronously in MainWindowViewModel's constructor, before the first frame is
/// even rendered. The earlier steps are therefore reported as already-done rather
/// than pretended to take time.
/// </summary>
public enum BootStep
{
    Starting,
    LibraryReady,

    /// <summary>
    /// The console has asked whether a newer version of itself exists.
    ///
    /// This is the only startup step that depends on a machine outside the house, so
    /// it is bounded by a short timeout and reported as done whether it succeeded or
    /// not — a console that will not start because a server is down would be a far
    /// worse failure than one that checks again tomorrow.
    /// </summary>
    UpdateChecked,

    ControllersReady,
    AmbientReady,
}

/// <summary>
/// "Monolith" boot screen: centred mark, wordmark, hairline progress bar. Doubles as
/// the real loader — it covers the blank moment between process start and the ambient
/// video's first decoded frame (which the dashboard already waits on before fading
/// in), then crossfades away.
///
/// The progress bar tracks genuine startup signals via Report(). It is deliberately
/// NOT a timer pretending to be progress: if the video takes longer to decode on
/// slower hardware, the bar simply waits at that step rather than completing early
/// and leaving a stalled screen.
///
/// A minimum on-screen time is enforced so the brand moment doesn't flash past on a
/// fast machine — but that floor only ever ADDS time to a boot that was already
/// finished; it never delays a boot that is still working.
/// </summary>
public partial class BootScreen : UserControl
{
    /// <summary>How long the mark stays up at minimum, so it reads as intentional rather than a flicker.</summary>
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromSeconds(2.2);

    /// <summary>
    /// Hard ceiling on how long the boot screen may ever hold the UI. AmbientBackground
    /// already fires MediaReady immediately when the video file is missing, but a
    /// CORRUPT file raises MediaFailed instead and no ready signal would arrive. Rather
    /// than trap the user on a boot screen forever, give up after this and hand off
    /// regardless — a dashboard over a black background beats a frozen splash.
    /// </summary>
    private static readonly TimeSpan MaximumVisible = TimeSpan.FromSeconds(10);

    private readonly DateTime _shownAtUtc = DateTime.UtcNow;
    private readonly System.Windows.Threading.DispatcherTimer _failsafe =
        new() { Interval = MaximumVisible };

    private BootStep _step = BootStep.Starting;
    private bool _handedOff;

    /// <summary>Raised once the boot screen has fully faded out and the dashboard should take over.</summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Raised when the user chooses to install a waiting update. The host performs the
    /// install and calls <see cref="ReportInstallProgress"/> as it goes.
    /// </summary>
    public event Action<string>? InstallRequested;

    /// <summary>
    /// How long the install offer waits before continuing on its own.
    ///
    /// It defaults to PLAY FIRST rather than installing: a console left switched on
    /// with nobody in the room must end up at the dashboard, not sitting on a prompt.
    /// Defaulting the other way would mean an unattended machine could start a
    /// multi-minute install nobody asked for.
    /// </summary>
    private static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(10);

    private readonly System.Windows.Threading.DispatcherTimer _offerTimer =
        new() { Interval = TimeSpan.FromSeconds(1) };

    private int _offerSecondsLeft;
    private string? _offerVersion;

    /// <summary>
    /// Blocks the hand-off while the install offer or an install is on screen.
    ///
    /// Without this the boot sequence would carry on underneath: the aperture would
    /// open onto the dashboard while the user was still reading the offer, and the
    /// question would vanish before it could be answered.
    /// </summary>
    private bool _awaitingUserChoice;

    public BootScreen()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            LayoutGroundPanels();
            PlayIntro();
        };

        // Keep the ground covering the window if it is ever resized before hand-off,
        // otherwise the dashboard underneath would show through the gap.
        SizeChanged += (_, _) =>
        {
            if (!_handedOff)
            {
                LayoutGroundPanels();
            }
        };

        _failsafe.Tick += (_, _) =>
        {
            // An offer or install is on screen: the console is not hung, it is waiting
            // for a person. Leave the failsafe armed rather than stopping it, so it
            // still protects the boot once the choice has been made.
            if (_awaitingUserChoice)
            {
                return;
            }

            _failsafe.Stop();
            if (!_handedOff)
            {
                // Never reached AmbientReady — hand off anyway rather than hang.
                AnimateProgressTo(1.0);
                StatusText.Text = "READY";
                HandOffWhenMinimumElapsed();
            }
        };
        _failsafe.Start();
    }

    /// <summary>
    /// Positions the four ground panels flush around where the mark sits, so together
    /// they cover the entire window with solid black from the very first frame.
    ///
    /// This MUST happen at load, not at hand-off. The panels ARE the boot screen's
    /// opaque ground — the root Grid itself is transparent — so if they are unsized
    /// the ambient video and dashboard show straight through the whole boot sequence.
    /// (That was a real bug: the reveal appeared useless because the UI underneath had
    /// been visible the entire time.)
    /// </summary>
    private void LayoutGroundPanels()
    {
        var w = RootGrid.ActualWidth;
        var h = RootGrid.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // The ground covers the whole window...
        GroundOuter.Rect = new Rect(0, 0, w, h);

        // ...with a zero-size hole until hand-off, so nothing shows through during boot.
        if (!_handedOff)
        {
            var (left, top, _, _) = GetOpeningRect();
            GroundHole.Rect = new Rect(left, top, 0, 0);
        }
    }

    /// <summary>
    /// The mark's rendered footprint in this control's coordinates — the rect the
    /// ground opens from. Read rather than assumed, since the mark stack sits 40px
    /// above true centre and may be scaled.
    /// </summary>
    private (double Left, double Top, double Width, double Height) GetOpeningRect()
    {
        if (Mark.ActualWidth <= 0 || RootGrid.ActualWidth <= 0)
        {
            return (0, 0, 0, 0);
        }

        // Ask WPF for the mark's REAL on-screen box, transforms included. Translating
        // a single point instead misses how MarkStack's ScaleTransform displaces its
        // children about the stack's own centre — which left the opening sitting near
        // the square rather than exactly on it.
        var bounds = Mark.TransformToVisual(RootGrid)
                         .TransformBounds(new Rect(0, 0, Mark.ActualWidth, Mark.ActualHeight));

        return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>Mark fades and scales up, then the progress row follows — deliberately unhurried.</summary>
    private void PlayIntro()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        MarkStack.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.75)) { EasingFunction = ease });

        var scale = new DoubleAnimation(0.93, 1.0, TimeSpan.FromSeconds(0.9)) { EasingFunction = ease };
        MarkScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        MarkScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        ProgressStack.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            {
                BeginTime = TimeSpan.FromSeconds(0.45),
                EasingFunction = ease,
            });
    }

    /// <summary>
    /// Reports a completed startup step. Advances the bar and status caption, and
    /// once everything is ready, hands off (respecting the minimum visible time).
    /// </summary>
    public void Report(BootStep step)
    {
        if (step <= _step && step != BootStep.Starting)
        {
            return;
        }

        _step = step;

        var (fraction, caption) = step switch
        {
            BootStep.LibraryReady => (0.4, "LIBRARY READY"),
            BootStep.UpdateChecked => (0.6, "CHECKING FOR UPDATES"),
            BootStep.ControllersReady => (0.75, "DETECTING CONTROLLERS"),
            BootStep.AmbientReady => (1.0, "READY"),
            _ => (0.15, "STARTING"),
        };

        AnimateProgressTo(fraction);
        StatusText.Text = caption;

        if (step == BootStep.AmbientReady)
        {
            HandOffWhenMinimumElapsed();
        }
    }

    private void AnimateProgressTo(double fraction)
    {
        // The fill is a fixed-width child of a 260px track.
        const double trackWidth = 260;

        var target = Math.Clamp(fraction, 0, 1) * trackWidth;
        var animation = new DoubleAnimation(target, TimeSpan.FromSeconds(0.55))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        ProgressFill.BeginAnimation(WidthProperty, animation);
    }

    /// <summary>
    /// Waits out any remaining minimum-visible time, then fades the whole screen away
    /// and raises Completed. Guarded so a second AmbientReady report can't double-fire.
    /// </summary>
    /// <summary>
    /// Shows the "update ready, install now or play first?" offer, pausing the boot
    /// hand-off until it is answered or times out.
    ///
    /// Only ever called for an update that is ALREADY DOWNLOADED, which is what lets
    /// the subtitle honestly say seconds rather than minutes — and that honesty is
    /// what makes "Install now" an easy thing to agree to.
    /// </summary>
    public void ShowUpdateOffer(string version, string sizeNote)
    {
        // The check that triggers this is asynchronous, so it can land after the boot
        // screen has already handed off. Showing an offer over a dashboard the user is
        // already using would be worse than skipping it — the update simply waits for
        // the next boot, which is the design anyway.
        if (_handedOff)
        {
            return;
        }

        // Let the normal boot loading run its course FIRST, then reveal the offer.
        // The update check can resolve almost instantly (a fast server, or the
        // simulated source), which would otherwise snap the offer up before the user
        // has seen the console load at all. Holding until the mark has settled and the
        // bar has advanced makes it read as "booted, checked, found something".
        // Set immediately so the boot cannot hand off during the settle wait below —
        // otherwise the aperture could open before the offer was ever shown.
        _awaitingUserChoice = true;

        var shownFor = DateTime.UtcNow - _shownAtUtc;
        var settleWait = TimeSpan.FromSeconds(1.4) - shownFor;
        if (settleWait > TimeSpan.Zero)
        {
            var wait = new System.Windows.Threading.DispatcherTimer { Interval = settleWait };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                ShowUpdateOffer(version, sizeNote);
            };
            wait.Start();
            return;
        }

        _offerVersion = version;
        _offerSecondsLeft = (int)OfferTimeout.TotalSeconds;

        PopulateVersion(version);
        OfferSizeNote.Text = sizeNote;
        UpdateOfferCountdown.Text = $"Starting in {_offerSecondsLeft}s…";

        // Boot transforms into the offer. The wordmark, tagline and progress bar are
        // boot furniture — they fade. The Monolith mark stays, and TRAVELS from centre
        // to the slab's right column, where it sits above the buttons.
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.32));
        Wordmark.BeginAnimation(OpacityProperty, fade);
        Tagline.BeginAnimation(OpacityProperty, fade);
        ProgressStack.BeginAnimation(OpacityProperty, fade);

        // Slab fades in first so its layout exists; the mark's move to the slot is then
        // measured against it (deferred to after layout).
        UpdateOffer.Visibility = Visibility.Visible;
        UpdateOffer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.45)));

        Dispatcher.BeginInvoke(new Action(() => MoveMarkTo(OfferMarkSlot)),
            System.Windows.Threading.DispatcherPriority.Loaded);

        // Highlight Install now without arming it: the countdown still falls through
        // to Play first if nobody answers.
        _installSelected = true;
        HighlightOfferButton();

        _offerTimer.Tick -= OfferTimer_Tick;
        _offerTimer.Tick += OfferTimer_Tick;
        _offerTimer.Start();
    }

    /// <summary>
    /// Fills in the version numeral, accenting the minor component so it reads as the
    /// meaningful part of the bump — "1.<b>4</b>.0".
    /// </summary>
    private void PopulateVersion(string version)
    {
        InstallVersionText.Text = version;

        var parts = version.Split('.');
        var runs = OfferVersionText.Inlines.OfType<System.Windows.Documents.Run>().ToArray();

        if (parts.Length >= 3 && runs.Length == 3)
        {
            runs[0].Text = parts[0] + ".";
            runs[1].Text = parts[1];
            runs[2].Text = "." + parts[2];
        }
        else if (runs.Length == 3)
        {
            // Unexpected shape — show it plainly rather than a mangled split.
            runs[0].Text = version;
            runs[1].Text = string.Empty;
            runs[2].Text = string.Empty;
        }
    }

    /// <summary>
    /// Slides the real Monolith mark so its centre lands on the centre of the given
    /// target element. Used to move it from screen centre onto the slab's slot.
    ///
    /// Uses the one real mark rather than a copy, so it is visually continuous — the
    /// same square through boot, the offer, and the reveal.
    /// </summary>
    private void MoveMarkTo(FrameworkElement target)
    {
        var targetCentre = target.TransformToAncestor(this)
            .Transform(new Point(target.ActualWidth / 2, target.ActualHeight / 2));

        // Where the mark's centre sits with the transform at zero: its own current
        // rendered centre, minus whatever the transform currently applies.
        var markCentre = Mark.TransformToAncestor(this)
            .Transform(new Point(Mark.ActualWidth / 2, Mark.ActualHeight / 2));

        var dx = _markHomeOffsetX + (targetCentre.X - markCentre.X);
        var dy = _markHomeOffsetY + (targetCentre.Y - markCentre.Y);

        _markHomeOffsetX = dx;
        _markHomeOffsetY = dy;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        MarkSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(dx, TimeSpan.FromSeconds(0.55)) { EasingFunction = ease });
        MarkSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(dy, TimeSpan.FromSeconds(0.55)) { EasingFunction = ease });
    }

    // The mark's transform offset while parked on the slab, so the return-to-centre
    // knows how far it has to travel back.
    private double _markHomeOffsetX;
    private double _markHomeOffsetY;

    private void HighlightOfferButton()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var idle = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));

        InstallNowButton.BorderBrush = _installSelected ? accent : idle;
        PlayFirstButton.BorderBrush = _installSelected ? idle : accent;
    }

    private void OfferTimer_Tick(object? sender, EventArgs e)
    {
        _offerSecondsLeft--;

        if (_offerSecondsLeft <= 0)
        {
            PlayFirst_Click(this, null!);
            return;
        }

        UpdateOfferCountdown.Text = $"Starting in {_offerSecondsLeft}s…";
    }

    private void PlayFirst_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _offerTimer.Stop();
        _awaitingUserChoice = false;

        // Fade the slab out FAST. It sits above the loading bar (higher ZIndex), so
        // while it is still visible the loader is hidden behind it — a slow fade here
        // is exactly what made the returning text and line look delayed. Snapping it
        // out lets the loader read the instant it is brought back.
        var fade = new DoubleAnimation(UpdateOffer.Opacity, 0, TimeSpan.FromSeconds(0.12));
        fade.Completed += (_, _) => UpdateOffer.Visibility = Visibility.Collapsed;
        UpdateOffer.BeginAnimation(OpacityProperty, fade);

        // Return the mark to TRUE centre, show the loading, then open into the OS.
        RecentreMarkThen(HandOffWhenMinimumElapsed);
    }

    /// <summary>
    /// The launch beat: the mark travels from the slab back to screen CENTRE, a short
    /// loading bar shows beneath it, and only then does <paramref name="after"/> run —
    /// which is the aperture reveal. This is the "square returns home, loads, and opens
    /// into the OS" sequence.
    /// </summary>
    private void RecentreMarkThen(Action after)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var travel = TimeSpan.FromSeconds(0.45);

        // Back to a zero transform = the mark's original centred position.
        MarkSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, travel) { EasingFunction = ease });
        MarkSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, travel) { EasingFunction = ease });
        _markHomeOffsetX = 0;
        _markHomeOffsetY = 0;

        // Restore the EXACT original startup look — the same wordmark, tagline and the
        // same bottom progress bar the boot began with. These were faded out when the
        // offer appeared; bringing all three back means the post-update screen is
        // identical to the pre-update one, which is what was asked for.
        //
        // BeginAnimation(null) + set base value + fresh fade on each, so no leftover
        // animation clock from the intro or the fade-out holds the property.
        foreach (var el in new UIElement[] { Wordmark, Tagline, ProgressStack })
        {
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 0;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        }

        StatusText.Text = "STARTING";
        AnimateProgressTo(1.0);

        // Hold on the centred mark + loading long enough to actually READ it before
        // the aperture opens. The earlier ~0.8s total let the text flash in and out;
        // this gives it a genuine beat. The progress bar's own animation is ~0.55s, so
        // waiting well past that means the reveal never interrupts a bar still filling.
        // Long enough for the progress bar to actually REACH the end before the
        // aperture opens. AnimateProgressTo runs for 0.55s, so anything shorter than
        // that plus a beat cuts the line off mid-fill — which looked like the bar
        // never completing and then vanishing.
        var hold = new System.Windows.Threading.DispatcherTimer
        {
            Interval = travel + TimeSpan.FromSeconds(0.95),
        };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            after();
        };
        hold.Start();
    }

    private void InstallNow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_offerVersion is null)
        {
            return;
        }

        _offerTimer.Stop();

        // Stays true throughout the install: nothing may hand off to the dashboard
        // while files are being replaced underneath it.
        _awaitingUserChoice = true;

        // The install panel reuses the Hero slab's frame, so crossfade between them
        // rather than cutting — the version numeral and the migrated mark stay put and
        // only the right column's content changes.
        InstallVersionText.Text = _offerVersion;
        InstallProgress.Visibility = Visibility.Visible;
        InstallProgress.Opacity = 0;
        InstallProgress.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));

        var offerOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
        offerOut.Completed += (_, _) => UpdateOffer.Visibility = Visibility.Collapsed;
        UpdateOffer.BeginAnimation(OpacityProperty, offerOut);

        InstallRequested?.Invoke(_offerVersion);
    }

    /// <summary>
    /// True while the install offer is waiting for an answer, so the host knows to
    /// route input here instead of to the dashboard.
    /// </summary>
    public bool IsAwaitingUpdateChoice => _awaitingUserChoice && UpdateOffer.Visibility == Visibility.Visible;

    /// <summary>
    /// Left/right move the highlight, A/Enter confirms, B/Escape chooses Play first.
    ///
    /// The offer must be answerable from a controller: it appears on a boot screen on
    /// a machine with no keyboard, and a prompt a child cannot dismiss with the pad in
    /// their hand would be a dead end.
    /// </summary>
    public void HandleOfferKey(System.Windows.Input.Key key)
    {
        if (!IsAwaitingUpdateChoice)
        {
            return;
        }

        switch (key)
        {
            case System.Windows.Input.Key.Left:
            case System.Windows.Input.Key.Right:
                SetOfferSelection(!_installSelected);
                break;

            case System.Windows.Input.Key.Enter:
                if (_installSelected) InstallNow_Click(this, null!);
                else PlayFirst_Click(this, null!);
                break;

            case System.Windows.Input.Key.Escape:
                PlayFirst_Click(this, null!);
                break;
        }
    }

    /// <summary>Gamepad equivalent of <see cref="HandleOfferKey"/>.</summary>
    public void HandleOfferButton(bool left, bool right, bool confirm, bool cancel)
    {
        if (!IsAwaitingUpdateChoice)
        {
            return;
        }

        if (left || right)
        {
            SetOfferSelection(!_installSelected);
        }
        else if (confirm)
        {
            if (_installSelected) InstallNow_Click(this, null!);
            else PlayFirst_Click(this, null!);
        }
        else if (cancel)
        {
            PlayFirst_Click(this, null!);
        }
    }

    /// <summary>
    /// Which button is highlighted. Starts on Install now, but the COUNTDOWN still
    /// defaults to Play first — highlighting the action we would like without making
    /// it what happens to an unattended console.
    /// </summary>
    private bool _installSelected = true;

    private void SetOfferSelection(bool install)
    {
        _installSelected = install;
        HighlightOfferButton();

        // Any deliberate interaction cancels the countdown: someone is clearly present
        // and reading it, so timing them out would be rude.
        _offerTimer.Stop();
        UpdateOfferCountdown.Text = string.Empty;
    }

    /// <summary>
    /// Updates the install progress bar. The host calls this as the installer works.
    /// </summary>
    public void ReportInstallProgress(int percent, string caption)
    {
        // Measure the track rather than assuming a width. It is HorizontalAlignment
        // Stretch, so its real width comes from the column — a hardcoded value left the
        // fill stopping partway across and never reaching the end.
        var trackWidth = InstallTrack.ActualWidth > 0 ? InstallTrack.ActualWidth : 320;

        InstallFill.Width = trackWidth * Math.Clamp(percent, 0, 100) / 100.0;
        InstallCaption.Text = caption;
    }

    /// <summary>
    /// Called when the install has finished and the console can continue booting.
    /// On the real machine an install is normally followed by a restart; this exists
    /// for the case where it is not.
    /// </summary>
    public void CompleteInstall()
    {
        _awaitingUserChoice = false;

        var fade = new DoubleAnimation(InstallProgress.Opacity, 0, TimeSpan.FromSeconds(0.25));
        fade.Completed += (_, _) => InstallProgress.Visibility = Visibility.Collapsed;
        InstallProgress.BeginAnimation(OpacityProperty, fade);

        // Same as Play first: the mark returns to centre before the aperture opens.
        RecentreMarkThen(HandOffWhenMinimumElapsed);
    }

    private void HandOffWhenMinimumElapsed()
    {
        if (_handedOff)
        {
            return;
        }

        // An offer or an install is on screen. Handing off now would open the aperture
        // onto the dashboard while the user was still reading the question.
        if (_awaitingUserChoice)
        {
            return;
        }

        _handedOff = true;
        _failsafe.Stop();

        var elapsed = DateTime.UtcNow - _shownAtUtc;
        var remaining = MinimumVisible - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var hold = new System.Windows.Threading.DispatcherTimer { Interval = remaining + TimeSpan.FromMilliseconds(1) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            FadeOutAndComplete();
        };
        hold.Start();
    }

    /// <summary>
    /// The aperture hand-off: rather than crossfading away, the mark's own rounded
    /// square becomes the shape the boot screen is painted through, then expands until
    /// it has carried the black ground off-screen — revealing the dashboard beneath.
    ///
    /// Sequence:
    ///   1. Wordmark, tagline, progress and bloom fade out, leaving only the mark.
    ///   2. The full-bleed opacity mask snaps down to exactly the mark's rect, and the
    ///      mark itself is hidden at the same instant — the mask now *is* the square,
    ///      so visually nothing changes at the swap.
    ///   3. That rect grows past the window bounds, taking the boot ground with it.
    /// </summary>
    private void FadeOutAndComplete()
    {
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // 1. Strip the supporting text, leaving only the mark, and give it a slight
        //    push outward so it reads as beginning to open the screen.
        var strip = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.32))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        Wordmark.BeginAnimation(OpacityProperty, strip);
        Tagline.BeginAnimation(OpacityProperty, strip);
        ProgressStack.BeginAnimation(OpacityProperty, strip);

        // NB: the text is faded, NOT collapsed. Collapsing would let MarkStack
        // recentre and the mark would visibly jump. The opening is aligned by reading
        // the mark's real rendered bounds instead (GetOpeningRect), so the layout can
        // stay exactly as the user sees it.
        var swell = new DoubleAnimation(1.0, 1.12, TimeSpan.FromSeconds(0.36)) { EasingFunction = easeOut };
        MarkScale.BeginAnimation(ScaleTransform.ScaleXProperty, swell);
        MarkScale.BeginAnimation(ScaleTransform.ScaleYProperty, swell);

        // 2/3. After the strip, hand the square over to the mask and grow it.
        // Let the strip finish and the mark's swell land before the panels move, so
        // the sequence reads as: text clears -> square pushes -> screen opens.
        var handover = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };

        handover.Tick += (_, _) =>
        {
            handover.Stop();
            BeginApertureGrowth(easeOut);
        };

        handover.Start();
    }

    /// <summary>
    /// The reveal. A single rounded-rectangle hole is clipped out of the black ground
    /// and grown from the mark's footprint until it clears the window, opening onto
    /// the dashboard underneath. The hole keeps the mark's 26px corner radius, so the
    /// opening matches the square it grows from rather than showing sharp corner
    /// wedges against it.
    ///
    /// A plain Border + Clip geometry — no OpacityMask, no DrawingBrush, nothing whose
    /// coordinate mapping can silently go wrong (two earlier mask attempts both failed
    /// exactly there).
    /// </summary>
    private void BeginApertureGrowth(IEasingFunction easing)
    {
        var w = RootGrid.ActualWidth;
        var h = RootGrid.ActualHeight;

        GroundOuter.Rect = new Rect(0, 0, w, h);

        // Start from the mark's CURRENT rendered footprint — it has just swelled to
        // 1.12x, so the opening must match the square the user is actually looking at.
        var (left, top, openW, openH) = GetOpeningRect();
        var from = new Rect(left, top, openW, openH);

        // The hole now sits exactly where the mark is, so the mark can go: the green
        // square hands over to the opening it created.
        GroundHole.Rect = from;
        MarkStack.Visibility = Visibility.Hidden;

        // Grow until every corner of the window is inside the hole. Measured to the
        // furthest corner from the opening's centre, since the mark sits above true
        // centre and the bottom corners are therefore further away.
        var cx = left + openW / 2;
        var cy = top + openH / 2;
        var reach = Math.Max(
            Math.Max(cx, w - cx),
            Math.Max(cy, h - cy)) * 1.6;

        var to = new Rect(cx - reach, cy - reach, reach * 2, reach * 2);

        // Slow enough to read as a deliberate reveal rather than a flicker. The whole
        // point is watching the screen open, so this is the moment to linger on.
        var duration = TimeSpan.FromSeconds(1.5);

        var grow = new RectAnimation(from, to, duration) { EasingFunction = easing };

        // Scale the corner radius with the opening so the rounding stays proportional
        // rather than shrinking to a hairline as it expands.
        var radius = new DoubleAnimation(26, 26 * (reach * 2 / Math.Max(openW, 1)), duration)
        {
            EasingFunction = easing,
        };

        grow.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            Completed?.Invoke(this, EventArgs.Empty);
        };

        GroundHole.BeginAnimation(RectangleGeometry.RectProperty, grow);
        GroundHole.BeginAnimation(RectangleGeometry.RadiusXProperty, radius);
        GroundHole.BeginAnimation(RectangleGeometry.RadiusYProperty, radius);

        // The bloom fades with the ground rather than lingering over the dashboard.
        Bloom.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
    }
}
