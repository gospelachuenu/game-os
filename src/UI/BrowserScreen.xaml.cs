using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace UI;

/// <summary>
/// The console's web browser: YouTube, light browsing, and downloading launcher
/// installers. Its own screen, opened from the rail, with the ambient video behind it.
///
/// RESTRICTIONS FOLLOW THE DEVICE MODE. On an unrestricted console (the default) this
/// is an ordinary browser — no filtering, downloads just work. Only on a child device
/// is the parental BrowserPolicy enforced. That is deliberate: the console goes to
/// several different people, and an adult recipient should not meet a kid's browser.
///
/// Controller input is a CURSOR rather than element-to-element focus. A web page has no
/// fixed set of focusable tiles the way a console screen does, so spatial navigation
/// breaks on any page that does something unusual; a pointer always works.
///
/// This stage hosts the browser and its chrome. Policy enforcement, PIN-gated downloads
/// and the YouTube TV mode come next — see docs/browser-design.md.
/// </summary>
public partial class BrowserScreen : UserControl
{
    /// <summary>
    /// A destination on the browser's start screen. Deliberately a small fixed set
    /// rather than a search box: the console has no keyboard by default, so a
    /// controller user needs somewhere to GO, not somewhere to type.
    /// </summary>
    public sealed class HomeTile : System.ComponentModel.INotifyPropertyChanged
    {
        public required string Title { get; init; }
        public required string Glyph { get; init; }
        public required string Url { get; init; }

        private bool _isFocused;
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                _isFocused = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFocused)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// The start-screen destinations. YouTube points at the TV interface, which is
    /// built for D-pad navigation — the one place a console browser genuinely works
    /// well without a pointer.
    /// </summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<HomeTile> _homeTiles = new()
    {
        new HomeTile { Title = "YouTube", Glyph = "▶", Url = "https://www.youtube.com/tv" },
        new HomeTile { Title = "Steam", Glyph = "🎮", Url = "https://store.steampowered.com" },
        new HomeTile { Title = "Epic Games", Glyph = "🎯", Url = "https://store.epicgames.com" },
        new HomeTile { Title = "EA", Glyph = "🕹", Url = "https://www.ea.com/ea-app" },
    };

    private int _homeIndex;

    /// <summary>Raised on Show() — MainWindow hides the dashboard content behind this.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the user closes the browser.</summary>
    public event EventHandler? CloseRequested;

    private bool _initialised;
    private bool _runtimeMissing;

    /// <summary>
    /// Which of the two web views is currently on screen.
    ///
    /// The app view holds YouTube; the browsing view holds everything else. Keeping them
    /// apart is what lets music carry on in the app while the user opens the browser —
    /// with one shared view, browsing navigated away from whatever was playing.
    /// </summary>
    private bool _appViewActive;

    /// <summary>
    /// The view the user is looking at. Everything that reads or drives "the page" goes
    /// through here rather than naming a field, so the same logic serves both.
    /// </summary>
    private Microsoft.Web.WebView2.Wpf.WebView2 ActiveWeb => _appViewActive ? AppWeb : Web;

    /// <summary>
    /// A TV user agent, used ONLY for youtube.com.
    ///
    /// YouTube serves its D-pad-friendly "leanback" interface at /tv, but redirects
    /// straight to the mouse-driven desktop site unless the browser identifies as a
    /// television. Since leanback is the one part of the web that genuinely works with
    /// a controller, it is worth claiming to be a PS4 to get it.
    ///
    /// Scoped to YouTube on purpose — sending a TV agent to every site would get the
    /// console served odd mobile/TV layouts everywhere else.
    ///
    /// VERIFIED against youtube.com/tv: this agent returns ~180KB of TVHTML5/Cobalt app,
    /// where an ordinary Chrome agent gets a 5.7KB "download the app" notice instead.
    /// Worth re-testing that way if YouTube ever appears to serve the desktop site again,
    /// rather than assuming the interface has been discontinued.
    /// </summary>
    private const string TvUserAgent =
        "Mozilla/5.0 (PS4; Leanback Shell) Cobalt/26.lts.0-qa; compatible;";

    /// <summary>The normal desktop agent, restored for every non-YouTube site.</summary>
    private string? _defaultUserAgent;

    /// <summary>
    /// A plain desktop agent, used when the real one was never captured.
    ///
    /// The app view is created already pretending to be a television, so on the paths
    /// where it initialises first there is no genuine desktop agent to read back. Without
    /// this fallback the TV agent leaks to ordinary sites and they render as TV layouts.
    /// </summary>
    private const string DesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0";

    /// <summary>
    /// True once the user has chosen YouTube, until they leave for somewhere else.
    ///
    /// This is deliberately NOT re-derived from whatever URL happens to be loading.
    /// Getting to leanback involves redirects through hosts that are not youtube.com at
    /// all — the consent gate and the sign-in flow both live on other Google domains —
    /// and a per-navigation check flipped the console back to desktop mode mid-redirect,
    /// taking the TV user agent with it. The result was YouTube's mouse-driven site with
    /// a cursor on it and a D-pad that did nothing.
    ///
    /// A session ends only by going Home or closing the browser.
    /// </summary>
    private bool _inYouTubeSession;

    /// <summary>
    /// Hosts that count as part of a YouTube session. Google's own sign-in and consent
    /// pages are keyboard-navigable, so staying in key mode across them is also the
    /// right behaviour for the user rather than merely convenient for the redirect.
    /// </summary>
    private static readonly string[] YouTubeSessionHosts =
    {
        "youtube.com",
        "youtu.be",
        "google.com",
        "googleapis.com",
        "googleusercontent.com",
        "gstatic.com",
        "ytimg.com",
    };

    /// <summary>
    /// True when the current page navigates by ARROW KEYS rather than a pointer.
    ///
    /// YouTube's leanback interface is built for exactly this — it is how a TV remote
    /// drives it — so on YouTube the controller sends key events and the page behaves
    /// natively. Everywhere else there is no such convention, so the cursor is the only
    /// thing that reliably works.
    /// </summary>
    public bool UsesKeyNavigation { get; private set; }

    public BrowserScreen()
    {
        InitializeComponent();
        HomeTiles.ItemsSource = _homeTiles;
        UpdateHomeHighlight();

        Keyboard.Accepted += (_, text) =>
        {
            SetPageVisible(true);

            if (!string.IsNullOrWhiteSpace(text))
            {
                Navigate(text);
            }
        };

        Keyboard.Cancelled += (_, _) => SetPageVisible(true);

        _appReadyFallback.Tick += (_, _) =>
        {
            _appReadyFallback.Stop();

            if (IsAppMode && !_appReadyAnnounced)
            {
                _appReadyAnnounced = true;
                AppReady?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>True while the start screen is showing rather than a web page.</summary>
    public bool IsOnHome => HomeScreen.Visibility == Visibility.Visible;

    private void ShowHome()
    {
        HomeScreen.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Collapsed;
        ActiveWeb.Visibility = Visibility.Collapsed;
        AddressText.Text = "Start browsing";

        // Back into the framed layout — leaving YouTube's full-screen mode on would
        // render the start screen edge to edge with no chrome to leave by.
        SetFullScreenContent(false);
        _inYouTubeSession = false;
        UsesKeyNavigation = false;

        // Start on the tiles, not the address bar — going somewhere is the common case,
        // and typing is the deliberate one.
        MoveHomeVertical(1);

        HideCursor();
        UpdateHomeHighlight();
    }

    /// <summary>Moves the start-screen highlight. Clamped — wrapping on four tiles is disorienting.</summary>
    public void MoveHomeSelection(int delta)
    {
        if (!IsOnHome)
        {
            return;
        }

        _homeIndex = Math.Clamp(_homeIndex + delta, 0, _homeTiles.Count - 1);
        UpdateHomeHighlight();
    }

    /// <summary>Opens the highlighted start-screen destination.</summary>
    public void ActivateHomeSelection()
    {
        if (IsOnHome && _homeIndex >= 0 && _homeIndex < _homeTiles.Count)
        {
            Navigate(_homeTiles[_homeIndex].Url);
        }
    }

    private void UpdateHomeHighlight()
    {
        for (var i = 0; i < _homeTiles.Count; i++)
        {
            _homeTiles[i].IsFocused = i == _homeIndex;
        }
    }

    private void HomeTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HomeTile tile)
        {
            _homeIndex = _homeTiles.IndexOf(tile);
            UpdateHomeHighlight();
            Navigate(tile.Url);
        }
    }

    /// <summary>
    /// Returns to the APP (YouTube) exactly as it was left, still playing.
    ///
    /// Separate from <see cref="Show"/> because the two mean different things now: this
    /// resumes the app view, while Show opens the browser. Sharing one entry point meant
    /// pressing the rail's Browser button took the user back to YouTube.
    /// </summary>
    public void ResumeApp()
    {
        _appViewActive = true;
        IsAppMode = true;

        _playingOffScreen = false;
        RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);
        IsHitTestVisible = true;
        Visibility = Visibility.Visible;
        Opacity = 1;

        // An app gets the whole screen, with none of the browser's chrome.
        SetFullScreenContent(true);
        Placeholder.Visibility = Visibility.Collapsed;
        HomeScreen.Visibility = Visibility.Collapsed;

        // Bring the app's page back into the frame and put the browsing one away. The
        // browsing view MAY be collapsed — nothing of value is playing in it — but the
        // app view is only ever moved, never collapsed.
        UnparkAppView();
        Web.Visibility = Visibility.Collapsed;
        _pageHiddenForOverlay = false;

        // The app is YouTube's TV interface, which is driven by arrow keys. Browsing in
        // between will have turned this off for the cursor, so put it back or the D-pad
        // does nothing on return.
        _inYouTubeSession = true;
        UsesKeyNavigation = true;
        HideCursor();

        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        // Opened as a browser, not an app: make sure the chrome an app launch strips off
        // is back, or the toolbar stays missing for the rest of the session.
        IsAppMode = false;
        SetFullScreenContent(false);

        // The BROWSING view. The app view keeps running behind it — that is the whole
        // point of the split: music carries on while the user browses.
        //
        // MOVED OFF-SCREEN, NOT COLLAPSED. Collapsing makes the page report itself
        // `hidden`, which is exactly when Chromium suspends media — so collapsing the app
        // view here silenced the music the moment the browser opened. It stays visible and
        // is simply pushed out of the frame.
        _appViewActive = false;
        ParkAppView();

        // Never treat opening the browser as returning to a backgrounded app.
        _playingOffScreen = false;
        RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);
        IsHitTestVisible = true;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        Opened?.Invoke(this, EventArgs.Empty);

        _ = EnsureBrowserAsync();
    }

    /// <summary>
    /// Raised when a page opened via <see cref="BeginAppLoad"/> has finished loading, so
    /// the launch splash covering it knows it can lift.
    /// </summary>
    public event EventHandler? AppReady;

    /// <summary>
    /// Raised as an app closes, before this screen disappears — the host uses it to run
    /// the shrink-back-to-the-tile animation.
    /// </summary>
    public event EventHandler? ClosingAsApp;

    /// <summary>
    /// The APP view, so the console's playback controls watch YouTube specifically.
    ///
    /// Deliberately not the active view: the mini-player, guide transport and toasts are
    /// music controls, and pointing them at the browsing view would report a web page's
    /// audio as "now playing" and let the transport buttons drive it.
    /// </summary>
    public Microsoft.Web.WebView2.Wpf.WebView2 MediaView => AppWeb;

    /// <summary>Raised once the web view is ready to be watched.</summary>
    public event EventHandler? MediaViewReady;

    /// <summary>True while this browser is standing in for an app rather than being browsed directly.</summary>
    public bool IsAppMode { get; private set; }

    /// <summary>Guards against announcing readiness twice for one launch.</summary>
    private bool _appReadyAnnounced;

    /// <summary>
    /// Releases the splash even if the page never finishes loading.
    ///
    /// Without this a dead connection or a site that never fires NavigationCompleted
    /// would leave the console stuck on a spinner with no way forward — the splash covers
    /// everything, so there would be nothing to press.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _appReadyFallback = new()
    {
        Interval = TimeSpan.FromSeconds(12),
    };

    /// <summary>
    /// Opens straight at a URL as an APP, with no fade and no start screen.
    ///
    /// Appears instantly rather than fading: this is called while the launch splash
    /// already covers the screen, so the browser is assembling out of sight and any
    /// transition of its own would only be seen as a flicker when the splash lifts.
    /// </summary>
    public async void BeginAppLoad(string url)
    {
        IsAppMode = true;

        // The APP view from here on — separate from the browsing view, so opening the
        // browser later does not navigate away from what this is playing.
        _appViewActive = true;

        _appReadyAnnounced = false;
        _appReadyFallback.Stop();
        _appReadyFallback.Start();

        // VISIBLE BUT FULLY TRANSPARENT. This screen renders above the launch splash and
        // its root is a near-opaque near-black, so showing it outright drops a black
        // sheet over the splash while it is still playing — the black overlay that kept
        // ruining the launch.
        //
        // Opacity 0 rather than Collapsed because WebView2 needs a laid-out, visible host
        // to render into: collapsed, it does not load at all and the splash would wait
        // forever. RevealApp() fades this in once the splash is finished.
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;

        // An app is NOT a browser showing a site: no toolbar, no address bar, no framed
        // box, no "Loading…" placeholder. Stripping the chrome up front matters because
        // this screen becomes visible while the splash is still covering it — anything
        // left on shows through the moment the splash lifts, and the console looks like
        // it opened a web browser instead of an app.
        SetFullScreenContent(true);
        Placeholder.Visibility = Visibility.Collapsed;
        HomeScreen.Visibility = Visibility.Collapsed;

        // Back in the frame — a previous browsing session will have pushed it out, and a
        // page loaded off-screen would never appear.
        UnparkAppView();

        // The web view stays HIDDEN until the splash is ready to lift. WebView2's child
        // window paints over everything WPF draws, so the moment it becomes visible it
        // punches straight through the splash covering it — the page appears mid-load,
        // half-rendered, and the launch animation is ruined. RevealApp() shows it.
        //
        // Safe to collapse here, unlike everywhere else: this is a fresh launch, so there
        // is nothing playing yet for a `hidden` page to have suspended.
        ActiveWeb.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Collapsed;

        Opened?.Invoke(this, EventArgs.Empty);

        await EnsureBrowserAsync();

        if (!_initialised)
        {
            // No browser runtime — nothing will ever load, so release the splash rather
            // than leaving the user staring at a spinner forever.
            AppReady?.Invoke(this, EventArgs.Empty);
            return;
        }

        NavigateHidden(url);
    }

    /// <summary>
    /// Navigates without revealing the web view, so the page loads out of sight behind
    /// the launch splash.
    /// </summary>
    private void NavigateHidden(string url)
    {
        if (ActiveWeb.CoreWebView2 is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var target = url.Contains("://") ? url : "https://" + url.Trim();

        ApplyUserAgentFor(target);

        HomeScreen.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Collapsed;

        // Deliberately NOT showing Web here — that is RevealApp's job.
        ActiveWeb.CoreWebView2.Navigate(target);
    }

    /// <summary>
    /// Shows the loaded page. Called as the splash lifts, so the first thing the user
    /// sees is a finished page rather than one assembling itself.
    /// </summary>
    public void RevealApp()
    {
        if (!IsAppMode)
        {
            return;
        }

        ActiveWeb.Visibility = Visibility.Visible;

        // Straight to full opacity, no fade: this is called while the splash still covers
        // the screen, so a fade here would just be a slower way of arriving at the same
        // hidden state. The splash's own lift is the visible transition.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }


    /// <summary>
    /// Leaves the browser WITHOUT stopping what it is playing.
    ///
    /// The page is moved off-screen rather than navigated away or collapsed. That
    /// distinction is the whole mechanism: off-screen it still reports
    /// `visibilityState: "visible"`, so Chromium keeps its audio running; collapsed it
    /// reports `hidden`, which is exactly when browsers suspend media. Measured, not
    /// assumed.
    ///
    /// Coming back is ResumeApp(), which slides the same live page back on screen — so
    /// the user returns to the thing that was playing, not a fresh page.
    /// </summary>
    public void LeavePlaying()
    {
        if (IsPromptingPermission)
        {
            CancelPermission();
        }

        ExitPrompt.Visibility = Visibility.Collapsed;
        HideCursor();

        // Whatever is playing lives in the APP view, so that is what stays alive. The
        // browsing view is collapsed outright — it has nothing running worth keeping.
        _appViewActive = true;
        Web.Visibility = Visibility.Collapsed;
        UnparkAppView();

        _playingOffScreen = true;

        // Off-screen, not collapsed. Collapsing would silence it.
        RenderTransform = new System.Windows.Media.TranslateTransform(-10000, 0);
        IsHitTestVisible = false;

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while the browser is parked off-screen still playing.</summary>
    private bool _playingOffScreen;

    /// <summary>
    /// True when the browser is parked off-screen with a live page on it.
    ///
    /// The host checks this before launching: re-launching would navigate away and kill
    /// whatever is playing, so a "launch" in this state has to be a return instead.
    /// </summary>
    public bool IsPlayingInBackground => _playingOffScreen;

    /// <summary>
    /// True when the browser is genuinely IN FRONT of the user and should receive input.
    ///
    /// Not the same as Visibility. A browser left playing in the background is still
    /// "Visible" — it has to be, or Chromium would suspend its audio — but it is parked
    /// off-screen and the user is looking at the dashboard. Every input handler must test
    /// this rather than Visibility, or the D-pad keeps driving a page nobody can see.
    /// </summary>
    public bool IsForeground => Visibility == Visibility.Visible && !_playingOffScreen;

    public void Hide()
    {
        // Deny any outstanding permission request. Its deferral must be completed or the
        // page is left waiting on an answer that can never arrive.
        if (IsPromptingPermission)
        {
            CancelPermission();
        }

        // Leave no dialog behind to greet the user next time the browser opens.
        ExitPrompt.Visibility = Visibility.Collapsed;
        _pageHiddenForOverlay = false;

        // Stop whatever the BROWSING page is doing — leaving a video decoding behind the
        // dashboard would make noise after the user has left. The app view is deliberately
        // untouched: closing the browser must not stop the music, which is the entire
        // reason the two views exist.
        if (_initialised && ActiveWeb.CoreWebView2 is not null)
        {
            ActiveWeb.CoreWebView2.Navigate("about:blank");
        }

        HideCursor();

        _appReadyFallback.Stop();

        // An app shrinks back into its tile rather than fading, mirroring the way it
        // grew out of it. The host runs that animation because only it knows where the
        // tile is; this screen just needs to be gone by the end of it.
        if (IsAppMode)
        {
            IsAppMode = false;
            ClosingAsApp?.Invoke(this, EventArgs.Empty);

            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // If the APP still has something playing, the screen cannot simply collapse —
        // collapsing makes the page report itself hidden and Chromium suspends its audio,
        // so closing the browser would silence the music. Park off-screen instead, which
        // keeps it sounding, and let the host know music is still going.
        if (AppHasLivePage)
        {
            var leave = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.25));
            leave.Completed += (_, _) =>
            {
                Opacity = 1;
                LeavePlaying();
            };
            BeginAnimation(OpacityProperty, leave);
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.25));
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Pushes the app view out of the frame while keeping it VISIBLE.
    ///
    /// The distinction is the whole mechanism. Off-screen, the page still reports
    /// `visibilityState: "visible"` and Chromium keeps its audio running; collapsed, it
    /// reports `hidden` and media is suspended. Measured, not assumed — and getting this
    /// wrong is what stopped the music when the browser opened.
    /// </summary>
    private void ParkAppView()
    {
        AppWeb.Visibility = Visibility.Visible;
        AppWeb.IsHitTestVisible = false;
        AppWeb.RenderTransform = new System.Windows.Media.TranslateTransform(-10000, 0);
    }

    /// <summary>Brings the app view back into the frame.</summary>
    private void UnparkAppView()
    {
        AppWeb.Visibility = Visibility.Visible;
        AppWeb.IsHitTestVisible = true;
        AppWeb.RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);
    }

    /// <summary>
    /// True when the app view holds a real page rather than a blank one.
    ///
    /// Used to decide whether closing the browser may collapse the whole screen: with an
    /// app still loaded it must not, because collapsing suspends its audio.
    /// </summary>
    private bool AppHasLivePage
    {
        get
        {
            var source = AppWeb.CoreWebView2?.Source;
            return !string.IsNullOrEmpty(source) && source != "about:blank";
        }
    }

    /// <summary>
    /// Brings the web view up, creating it on first use.
    ///
    /// The WebView2 runtime is NOT guaranteed to be installed — it is a separate
    /// component, not part of Windows everywhere. A console that crashed because it was
    /// missing would be a very poor failure, so this reports "browser unavailable"
    /// instead and the rest of the console carries on.
    /// </summary>
    private async Task EnsureBrowserAsync()
    {
        if (_runtimeMissing)
        {
            ShowUnavailable();
            return;
        }

        if (_initialised)
        {
            // Already running. If it was started in the background by Settings, it has
            // never been shown a start screen — give it one now.
            if (Visibility == Visibility.Visible && !IsOnHome && ActiveWeb.Visibility != Visibility.Visible)
            {
                ShowHome();
            }

            return;
        }

        // Only dress the screen when it is actually on show AS A BROWSER. Settings starts
        // it in the background to read stored permissions, and an app launch has a splash
        // of its own covering the screen — neither should leave browser furniture behind.
        var visible = Visibility == Visibility.Visible && !IsAppMode;

        if (visible)
        {
            PlaceholderTitle.Text = "Browser";
            PlaceholderBody.Text = "Starting…";
            Placeholder.Visibility = Visibility.Visible;
        }

        try
        {
            // A user-data folder under %LOCALAPPDATA% rather than beside the exe: on the
            // real console the install directory is read-only under the write filter,
            // and WebView2 must be able to write its profile.
            var userData = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GamingOS", "Browser");

            // ONE environment shared by BOTH views. A user data folder allows only one
            // WebView2 session at a time, so two separately created environments pointing
            // at the same folder would fail outright. Sharing it also means the two views
            // share a single browser process — the second costs a renderer rather than a
            // whole stack — and share cookies, so one YouTube sign-in covers both.
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);

            await Web.EnsureCoreWebView2Async(env);
            await AppWeb.EnsureCoreWebView2Async(env);

            // WebView2 paints WHITE before a page has rendered — a hard flash on a dark
            // console, and the reason an app launch showed a bright frame between the
            // splash and the page. DefaultBackgroundColor is the supported fix: the
            // control renders this instead of white from initialisation onward, with no
            // visibility toggling or timing tricks.
            var backdrop = System.Drawing.Color.FromArgb(0xFF, 0x08, 0x0A, 0x0F);
            Web.DefaultBackgroundColor = backdrop;
            AppWeb.DefaultBackgroundColor = backdrop;

            _initialised = true;
            WireEvents(Web, isAppView: false);
            WireEvents(AppWeb, isAppView: true);

            // The console's playback controls can start watching now that there is a
            // page to watch.
            MediaViewReady?.Invoke(this, EventArgs.Empty);

            // Drop any remembered DENIAL left by an earlier build, which used to persist
            // refusals too. Those are unreachable from the UI and present as a broken
            // microphone, so they are cleared rather than inherited. Allows are kept.
            await ClearRememberedDenialsAsync();

            // Land on the start screen rather than a blank page — a controller user has
            // no way to type a URL. Skipped when starting in the background, so the
            // browser opens on Home properly the first time it is actually shown.
            if (visible)
            {
                ShowHome();
            }
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException
                                   or System.IO.IOException
                                   or UnauthorizedAccessException
                                   or InvalidOperationException)
        {
            _runtimeMissing = true;
            ShowUnavailable();
        }
    }

    private void ShowUnavailable()
    {
        ActiveWeb.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = "Browser unavailable";
        PlaceholderBody.Text =
            "The web browser component isn't installed on this console. Everything else works normally.";
    }

    /// <summary>
    /// Wires both views. Called once per view, with <paramref name="isAppView"/> saying
    /// which one — chrome updates (the address bar, the loading text) belong only to the
    /// view actually on screen, or a background page's navigation would rewrite the
    /// toolbar of the one the user is looking at.
    /// </summary>
    private void WireEvents(Microsoft.Web.WebView2.Wpf.WebView2 view, bool isAppView)
    {
        var core = view.CoreWebView2;

        // True while THIS view is the one the user is looking at.
        bool IsShowing() => _appViewActive == isAppView;

        core.NavigationStarting += (_, e) =>
        {
            // Session tracking belongs to the view actually on screen. The app view
            // navigating in the background must not switch the browsing view out of TV
            // mode, or vice versa — they are independent pages.
            if (IsShowing())
            {
                // Only ever ENDS a session, never starts one. A page cannot put the
                // console into TV mode by navigating; that follows from what the user
                // picked.
                TrackNavigation(e.Uri);
            }

            if (!IsShowing()) return;

            StatusText.Text = "Loading…";
            AddressText.Text = e.Uri;
        };

        core.NavigationCompleted += (_, _) =>
        {
            if (!IsShowing()) return;

            StatusText.Text = string.Empty;
            UpdateNavButtons();

            // The page now covers the screen, so the hint underneath it is just a stale
            // artefact. Removed rather than left to a timer that could outlive the load.
            HideExitHint();
        };

        // Readiness for an app launch is signalled from ContentLoading, not
        // NavigationCompleted. NavigationCompleted waits for sub-resources — on YouTube
        // that is seconds after the interface is on screen, so the splash would sit there
        // covering a page that had already finished drawing. ContentLoading fires as the
        // document starts rendering, which is the moment worth uncovering.
        core.ContentLoading += (_, _) =>
        {
            // The launch splash waits on the APP view only — the browsing view loading a
            // page has nothing to do with an app launch.
            if (!isAppView || !IsAppMode || _appReadyAnnounced)
            {
                return;
            }

            _appReadyAnnounced = true;

            // A beat for the first frame to paint. Without it the handover can begin on a
            // page that has started rendering but has not yet put anything on screen.
            // The splash then adds ~840ms more cover (fade to black, hold, reveal) before
            // any of the page is actually visible, so this only needs to catch the
            // earliest case.
            var settle = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300),
            };

            settle.Tick += (_, _) =>
            {
                settle.Stop();
                AppReady?.Invoke(this, EventArgs.Empty);
            };

            settle.Start();
        };

        core.SourceChanged += (_, _) =>
        {
            if (!IsShowing()) return;

            AddressText.Text = string.IsNullOrEmpty(core.Source) || core.Source == "about:blank"
                ? "Start browsing"
                : core.Source;
            UpdateNavButtons();
        };

        // Permission prompts (microphone for YouTube voice search, camera, location…).
        // Handled here rather than left to Chromium for two reasons: its dialog is a
        // mouse-and-keyboard affair a controller cannot dismiss, and the decision belongs
        // to the console's own policy, not to whatever the page asks for.
        core.PermissionRequested += (_, e) =>
        {
            e.Handled = true;

            // Deferral: the answer arrives from the user, long after this returns.
            var deferral = e.GetDeferral();
            PromptForPermission(e, deferral);
        };

        // Keep everything inside this one view: a page asking for a new window would
        // otherwise open a second, chrome-less browser the console cannot manage.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        // Suppress Chromium's own dialogs — they are mouse-and-keyboard affairs that a
        // controller cannot dismiss, so they would strand the user.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // Required for SendKey: controller input is delivered through the DevTools
        // protocol, which is unavailable when this is off. On by default, set explicitly
        // so that hardening the browser later cannot silently break the controller.
        core.Settings.AreDevToolsEnabled = true;
    }

    /// <summary>
    /// The console's own answer to a page's permission request.
    ///
    /// Deliberately NOT a straight pass-through of Chromium's prompt. Most permissions
    /// have no meaning on a TV appliance and are denied outright without troubling the
    /// user; only the ones that unlock a real feature are worth asking about. Anything
    /// unrecognised is denied, so a capability added by a future runtime cannot become
    /// available here just because nobody thought about it.
    /// </summary>
    private void PromptForPermission(CoreWebView2PermissionRequestedEventArgs e,
                                     CoreWebView2Deferral deferral)
    {
        var origin = ShortOrigin(e.Uri);

        var question = e.PermissionKind switch
        {
            // Voice search on YouTube's TV interface. The one permission that earns its
            // place: typing on a controller is miserable, so speaking is a real win.
            CoreWebView2PermissionKind.Microphone =>
                $"Let {origin} use the microphone for voice search?",

            // Everything else is refused without asking. A console has no camera, its
            // location is fixed and private, and a web page has no business reading the
            // clipboard or firing notifications at someone playing a game.
            _ => null,
        };

        if (question is null)
        {
            e.State = CoreWebView2PermissionState.Deny;
            deferral.Complete();
            return;
        }

        ShowPermissionPrompt(question, granted =>
        {
            e.State = granted
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;

            // Remember an ALLOW so the question is asked once rather than on every visit;
            // WebView2 stores it in the profile under %LOCALAPPDATA%, surviving restarts.
            //
            // Never remember a DENY. A stored refusal is indistinguishable from a broken
            // microphone: the page stops asking, the console stops prompting, and the user
            // is told to "check your settings" with no setting anywhere to change. Asking
            // again next time costs one button press; a permanent silent block cannot be
            // undone from the UI at all.
            e.SavesInProfile = granted;

            deferral.Complete();
        });
    }

    /// <summary>
    /// Forgets every remembered permission answer, so pages ask again. For a
    /// "Reset site permissions" action — the only way to take back a mistaken Allow.
    /// </summary>
    public Task ClearRememberedPermissionsAsync() => ResetStoredPermissionsAsync(deniedOnly: false);

    /// <summary>
    /// The sites currently allowed to use something, newest API permitting.
    ///
    /// Reported so Settings can name them rather than claim vaguely that permissions
    /// exist — "youtube.com" is a fact the user can act on.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetGrantedPermissionOriginsAsync()
    {
        // Start the browser if it has not run yet. Without this, Settings would report
        // "no permissions" on a fresh boot purely because nobody had opened the browser
        // — a confident-sounding answer that happens to be false.
        await EnsureBrowserAsync();

        if (!_initialised || ActiveWeb.CoreWebView2 is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var settings = await ActiveWeb.CoreWebView2.Profile.GetNonDefaultPermissionSettingsAsync();

            return settings
                .Where(s => s.PermissionState == CoreWebView2PermissionState.Allow)
                .Select(s => ShortOrigin(s.PermissionOrigin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or NotImplementedException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Forgets remembered REFUSALS only, leaving granted permissions intact.
    ///
    /// Run at startup to heal profiles written by an earlier build that persisted denials.
    /// A stored refusal makes a page report a broken device rather than ask again, and
    /// nothing in the console's UI could clear it.
    /// </summary>
    private Task ClearRememberedDenialsAsync() => ResetStoredPermissionsAsync(deniedOnly: true);

    private async Task ResetStoredPermissionsAsync(bool deniedOnly)
    {
        if (!_initialised || ActiveWeb.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var profile = ActiveWeb.CoreWebView2.Profile;
            var permissions = await profile.GetNonDefaultPermissionSettingsAsync();

            foreach (var setting in permissions)
            {
                if (deniedOnly && setting.PermissionState != CoreWebView2PermissionState.Deny)
                {
                    continue;
                }

                // Back to Default: the page may ask again, and the console decides afresh.
                await profile.SetPermissionStateAsync(
                    setting.PermissionKind,
                    setting.PermissionOrigin,
                    CoreWebView2PermissionState.Default);
            }
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or NotImplementedException)
        {
            // Older runtimes lack the profile permission APIs. Not worth failing over —
            // the user simply keeps whatever answers were stored.
        }
    }

    /// <summary>Host of a URL, for showing the user who is asking.</summary>
    private static string ShortOrigin(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? parsed.Host[4..]
                : parsed.Host
            : "this site";

    private void UpdateNavButtons()
    {
        var core = ActiveWeb.CoreWebView2;
        if (core is null)
        {
            return;
        }

        BackButton.Opacity = core.CanGoBack ? 1.0 : 0.35;
        ForwardButton.Opacity = core.CanGoForward ? 1.0 : 0.35;
    }

    // ---------------- navigation ----------------

    /// <summary>
    /// Back through history, then back to the start screen, then out of the browser.
    /// Three stops rather than two so Back never dumps the user straight to the
    /// dashboard from a page they were reading.
    /// </summary>
    public void GoBack()
    {
        if (_initialised && ActiveWeb.CoreWebView2?.CanGoBack == true)
        {
            ActiveWeb.CoreWebView2.GoBack();
            return;
        }

        if (!IsOnHome && _initialised)
        {
            ActiveWeb.CoreWebView2?.Navigate("about:blank");
            ShowHome();
            return;
        }

        Hide();
    }

    public void GoForward()
    {
        if (_initialised && ActiveWeb.CoreWebView2?.CanGoForward == true)
        {
            ActiveWeb.CoreWebView2.GoForward();
        }
    }

    public void Reload() => ActiveWeb.CoreWebView2?.Reload();

    /// <summary>Navigates to a URL, tolerating input without a scheme.</summary>
    public void Navigate(string url)
    {
        if (!_initialised || ActiveWeb.CoreWebView2 is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var target = url.Contains("://") ? url : "https://" + url.Trim();

        ApplyUserAgentFor(target);

        // Leaving the start screen for real content.
        HomeScreen.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Collapsed;
        ActiveWeb.Visibility = Visibility.Visible;

        ActiveWeb.CoreWebView2.Navigate(target);
    }

    /// <summary>
    /// Starts a YouTube session, or ends it, based on a destination the USER chose.
    /// Call this from Navigate — not from a navigation event, which also fires for
    /// redirects the user never asked for.
    ///
    /// Must run BEFORE navigating: the agent is read when the request is made, so
    /// setting it afterwards has no effect on the page being loaded.
    /// </summary>
    private void ApplyUserAgentFor(string url)
    {
        _inYouTubeSession = IsYouTubeSessionUrl(url);
        ApplySessionMode();
    }

    /// <summary>
    /// Keeps a YouTube session alive across redirects, and ends it if the page navigates
    /// somewhere genuinely unrelated (an ad click, an external link).
    /// </summary>
    private void TrackNavigation(string url)
    {
        if (_inYouTubeSession && !IsYouTubeSessionUrl(url))
        {
            _inYouTubeSession = false;
        }

        ApplySessionMode();
    }

    private static bool IsYouTubeSessionUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // about:blank and friends: not a departure, so leave the session as it is.
            return true;
        }

        var host = uri.Host;

        // Suffix match on a host boundary — a bare Contains would let
        // "youtube.com.example.net" masquerade as YouTube.
        return YouTubeSessionHosts.Any(known =>
            host.Equals(known, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Puts the browser into key-navigation or cursor mode to match the session.</summary>
    private void ApplySessionMode()
    {
        var core = ActiveWeb.CoreWebView2;
        if (core is null)
        {
            return;
        }

        // Capture the real desktop agent ONCE, and only from a view that has not already
        // been switched to the TV one. Reading it back off a view that is currently
        // pretending to be a PS4 records the TV agent as the "default", after which every
        // ordinary site is served a television layout — which is exactly what made Google
        // look like an old TV interface.
        if (_defaultUserAgent is null && !_inYouTubeSession)
        {
            _defaultUserAgent = core.Settings.UserAgent;
        }

        // The TV agent belongs to the APP view alone. The session's host list is wide on
        // purpose — sign-in and consent redirect through several Google domains and must
        // not drop out of TV mode mid-flow — but in the BROWSING view that same breadth
        // meant visiting Google got a television layout.
        var isYouTube = _inYouTubeSession && _appViewActive;

        core.Settings.UserAgent = isYouTube
            ? TvUserAgent
            : _defaultUserAgent ?? DesktopUserAgent;

        // Leanback drives by arrow keys; everything else needs the pointer.
        UsesKeyNavigation = isYouTube;

        if (UsesKeyNavigation)
        {
            HideCursor();
        }

        // Full screen is for the YOUTUBE APP only — the TV interface is a television UI in
        // its own right, and the console's chrome around it would waste the screen.
        // Everything in the browsing view keeps its toolbar and frame.
        SetFullScreenContent(isYouTube);

        ButtonHints.Text = UsesKeyNavigation
            ? "D-pad  Move        A  Select        B  Back        HOLD B  Exit"
            : "Stick  Move        A  Click        B  Back        HOLD B  Exit";
    }

    /// <summary>
    /// Shows the "Hold B to exit" hint, then fades it.
    ///
    /// Shown BEFORE the page is handed the screen, while the web view is still hidden:
    /// WebView2's child window paints over WPF content, so a hint drawn once the page is
    /// up would be invisible. This is why it appears as the page loads rather than
    /// on demand.
    /// </summary>
    private void ShowExitHint()
    {
        ExitHint.Visibility = Visibility.Visible;

        // Clear any running clock first — re-entering full screen while a previous fade
        // is mid-flight would otherwise leave the hint stuck at partial opacity.
        ExitHint.BeginAnimation(OpacityProperty, null);
        ExitHint.Opacity = 1;
    }

    /// <summary>
    /// Hides the exit hint. Called once the page has actually painted, since from that
    /// moment the hint is behind it and only a stale artefact.
    /// </summary>
    private void HideExitHint()
    {
        if (ExitHint.Visibility != Visibility.Visible)
        {
            return;
        }

        ExitHint.BeginAnimation(OpacityProperty, null);
        ExitHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Gives the page the entire screen (no toolbar, hints, margins or rounded frame)
    /// or puts it back in its framed box.
    /// </summary>
    private void SetFullScreenContent(bool fullScreen)
    {
        ChromeBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        HintBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;

        // The hint is for the BROWSER only. During an app launch it would sit on the
        // splash — text over a loading screen the user did not ask for — and an app's
        // exit belongs in its own confirmation, not a badge on the way in.
        if (fullScreen && !IsAppMode)
        {
            ShowExitHint();
        }

        ContentArea.Margin = fullScreen ? new Thickness(0) : new Thickness(28, 0, 28, 0);
        ContentFrame.BorderThickness = new Thickness(fullScreen ? 0 : 1);
        ContentFrame.CornerRadius = new CornerRadius(fullScreen ? 0 : 10);
    }

    /// <summary>
    /// The DevTools-protocol description of each key the controller sends: the DOM key
    /// name, the physical `code`, and the Windows virtual-key value.
    ///
    /// Web apps disagree about which of these they read — YouTube's TV interface keys
    /// off `code` for arrows while plenty of pages check `key` — so all three are sent
    /// and the event is indistinguishable from a real keypress.
    /// </summary>
    private static (string Key, string Code, int WindowsVirtualKey) DescribeKey(
        NativeKeyboard.VirtualKey key) => key switch
    {
        NativeKeyboard.VirtualKey.Left => ("ArrowLeft", "ArrowLeft", 37),
        NativeKeyboard.VirtualKey.Up => ("ArrowUp", "ArrowUp", 38),
        NativeKeyboard.VirtualKey.Right => ("ArrowRight", "ArrowRight", 39),
        NativeKeyboard.VirtualKey.Down => ("ArrowDown", "ArrowDown", 40),
        NativeKeyboard.VirtualKey.Return => ("Enter", "Enter", 13),
        NativeKeyboard.VirtualKey.Escape => ("Escape", "Escape", 27),
        NativeKeyboard.VirtualKey.Back => ("Backspace", "Backspace", 8),
        NativeKeyboard.VirtualKey.Space => (" ", "Space", 32),
        _ => ("", "", 0),
    };

    /// <summary>
    /// Sends a key to the page — how the controller drives YouTube's TV interface.
    ///
    /// Uses the DevTools protocol's Input.dispatchKeyEvent rather than an OS-level
    /// SendInput. THIS DISTINCTION IS THE WHOLE THING: WebView2 exposes no keyboard-send
    /// API (still an open request against the SDK), and a synthesised OS key only reaches
    /// the page if the renderer's child window genuinely holds keyboard focus — which,
    /// hosted inside a WPF control, it does not reliably do. SendInput therefore appeared
    /// to succeed while the page received nothing at all.
    ///
    /// CDP injects directly into the renderer's input pipeline, so the event arrives as
    /// a trusted keypress with no dependence on window focus.
    /// </summary>
    public async void SendKey(NativeKeyboard.VirtualKey key)
    {
        if (!_initialised || ActiveWeb.CoreWebView2 is null)
        {
            return;
        }

        var (domKey, code, virtualKey) = DescribeKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        // rawKeyDown rather than keyDown: for non-text keys such as arrows this is what
        // a real key press generates, and it is what page handlers listen for.
        var down = BuildKeyEvent("rawKeyDown", domKey, code, virtualKey);
        var up = BuildKeyEvent("keyUp", domKey, code, virtualKey);

        try
        {
            await ActiveWeb.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", down);
            await ActiveWeb.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", up);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or JsonException)
        {
            // A dropped navigation key is not worth tearing the console down for; the
            // next press retries. Deliberately narrow so genuine faults still surface.
        }
    }

    private static string BuildKeyEvent(string type, string domKey, string code, int virtualKey) =>
        $"{{\"type\":\"{type}\",\"key\":\"{domKey}\",\"code\":\"{code}\"," +
        $"\"windowsVirtualKeyCode\":{virtualKey},\"nativeVirtualKeyCode\":{virtualKey}}}";

    // ---------------- exit confirmation ----------------

    /// <summary>True while the exit confirmation owns controller input.</summary>
    public bool IsConfirmingExit => ExitPrompt.Visibility == Visibility.Visible;

    /// <summary>Cancel is the default, so a stray press never discards what you were watching.</summary>
    private bool _exitConfirmSelected;

    /// <summary>
    /// Asks before leaving the current site.
    ///
    /// Names the site rather than saying "close the browser": on a full-screen page the
    /// console's own chrome is invisible, so from the user's point of view they are in
    /// YouTube, not in a browser that happens to be showing it.
    /// </summary>
    public void ConfirmExit()
    {
        if (IsConfirmingExit)
        {
            return;
        }

        // Nothing to confirm on the start screen — there is no page to lose.
        if (IsOnHome)
        {
            Hide();
            return;
        }

        var site = CurrentSiteName();
        ExitQuestion.Text = site is null ? "Close the browser?" : $"Close {site}?";

        _exitConfirmSelected = false;
        UpdateExitButtons();

        // The page must go before the dialog can be seen — WebView2 paints over WPF.
        SetPageVisible(false);
        ExitPrompt.Visibility = Visibility.Visible;
    }

    /// <summary>A friendly name for the current site, for the confirmation text.</summary>
    private string? CurrentSiteName()
    {
        var source = ActiveWeb.CoreWebView2?.Source;
        if (string.IsNullOrEmpty(source) || source == "about:blank")
        {
            return null;
        }

        var host = ShortOrigin(source);

        // "youtube.com" reads worse than "YouTube" in a sentence aimed at a child.
        return host switch
        {
            "youtube.com" or "youtu.be" => "YouTube",
            "store.steampowered.com" => "Steam",
            "store.epicgames.com" => "Epic Games",
            "ea.com" => "EA",
            _ => host,
        };
    }

    public void MoveExitSelection(int delta)
    {
        if (!IsConfirmingExit || delta == 0)
        {
            return;
        }

        _exitConfirmSelected = delta < 0;
        UpdateExitButtons();
    }

    public void ActivateExitSelection()
    {
        if (!IsConfirmingExit)
        {
            return;
        }

        if (_exitConfirmSelected)
        {
            ExitPrompt.Visibility = Visibility.Collapsed;
            Hide();
        }
        else
        {
            CancelExit();
        }
    }

    /// <summary>Dismisses the confirmation and puts the page back.</summary>
    public void CancelExit()
    {
        if (!IsConfirmingExit)
        {
            return;
        }

        ExitPrompt.Visibility = Visibility.Collapsed;
        SetPageVisible(true);
    }

    private void UpdateExitButtons()
    {
        var accent = TryFindResource("Theme.AccentPrimaryBrush") as System.Windows.Media.Brush;
        var idle = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x3A));

        ExitConfirmButton.BorderBrush = _exitConfirmSelected ? accent ?? idle : idle;
        ExitCancelButton.BorderBrush = _exitConfirmSelected ? idle : accent ?? idle;
    }

    private void ExitConfirm_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ExitPrompt.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void ExitCancel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => CancelExit();

    // ---------------- permission prompt ----------------

    /// <summary>Set while the prompt is up; receives the user's answer exactly once.</summary>
    private Action<bool>? _permissionAnswer;

    /// <summary>True while the permission prompt owns controller input.</summary>
    public bool IsPromptingPermission => PermissionPrompt.Visibility == Visibility.Visible;

    private bool _permissionAllowSelected;

    private void ShowPermissionPrompt(string question, Action<bool> answer)
    {
        _permissionAnswer = answer;
        PermissionQuestion.Text = question;

        // Defaults to Deny. On a console used by a child, the safe answer should be the
        // one a stray button press lands on.
        _permissionAllowSelected = false;
        UpdatePermissionButtons();

        // The web view MUST be hidden while the prompt is up. WebView2 hosts a real child
        // HWND which paints over WPF content regardless of ZIndex or declaration order
        // ("airspace"), so a dialog drawn on top of it is simply invisible. Collapsing the
        // view is the only reliable way to be seen. The page keeps running underneath.
        _webHiddenForPrompt = ActiveWeb.Visibility == Visibility.Visible;
        if (_webHiddenForPrompt)
        {
            ActiveWeb.Visibility = Visibility.Collapsed;
        }

        PermissionPrompt.Visibility = Visibility.Visible;
    }

    /// <summary>Whether the web view was visible before the prompt covered it.</summary>
    private bool _webHiddenForPrompt;

    /// <summary>
    /// Shows or hides the page so WPF content can be drawn over the browser.
    ///
    /// Necessary because WebView2 hosts a real child window that paints over everything
    /// WPF puts on top of it, whatever the ZIndex. Anything that must appear above the
    /// page — the guide menu, a permission prompt — has to hide it first.
    ///
    /// Only ever re-shows a page that was actually showing: called on the start screen,
    /// this must not conjure a blank web view over the tiles.
    /// </summary>
    public void SetPageVisible(bool visible)
    {
        if (!visible)
        {
            _pageHiddenForOverlay = ActiveWeb.Visibility == Visibility.Visible;
            if (_pageHiddenForOverlay)
            {
                ActiveWeb.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_pageHiddenForOverlay)
        {
            ActiveWeb.Visibility = Visibility.Visible;
            _pageHiddenForOverlay = false;
        }
    }

    private bool _pageHiddenForOverlay;

    public void MovePermissionSelection(int delta)
    {
        if (!IsPromptingPermission || delta == 0)
        {
            return;
        }

        _permissionAllowSelected = delta < 0;
        UpdatePermissionButtons();
    }

    public void ConfirmPermission() => AnswerPermission(_permissionAllowSelected);

    /// <summary>Backing out of the prompt denies — the same as declining.</summary>
    public void CancelPermission() => AnswerPermission(false);

    private void AnswerPermission(bool granted)
    {
        if (!IsPromptingPermission)
        {
            return;
        }

        PermissionPrompt.Visibility = Visibility.Collapsed;

        if (_webHiddenForPrompt)
        {
            ActiveWeb.Visibility = Visibility.Visible;
            _webHiddenForPrompt = false;
        }

        // Cleared before invoking: the callback completes the deferral, and a second
        // answer would try to complete it twice.
        var answer = _permissionAnswer;
        _permissionAnswer = null;
        answer?.Invoke(granted);
    }

    private void UpdatePermissionButtons()
    {
        var accent = TryFindResource("Theme.AccentPrimaryBrush") as System.Windows.Media.Brush;

        PermissionAllow.BorderBrush = _permissionAllowSelected
            ? accent ?? PermissionAllow.BorderBrush
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x3A));

        PermissionDeny.BorderBrush = _permissionAllowSelected
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x3A))
            : accent ?? PermissionDeny.BorderBrush;
    }

    private void PermissionAllow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => AnswerPermission(true);

    private void PermissionDeny_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => AnswerPermission(false);

    // ---------------- address entry ----------------

    /// <summary>True while the on-screen keyboard owns controller input.</summary>
    public bool IsTyping => Keyboard.IsOpen;

    /// <summary>
    /// True while the address bar has the controller highlight.
    ///
    /// Reached by pressing UP from the start screen's tiles — without it the address bar
    /// is mouse-only, which on a console means unreachable.
    /// </summary>
    public bool IsAddressFocused { get; private set; }

    /// <summary>Moves focus between the start-screen tiles and the address bar above them.</summary>
    public void MoveHomeVertical(int delta)
    {
        if (!IsOnHome)
        {
            return;
        }

        IsAddressFocused = delta < 0;

        var accent = TryFindResource("Theme.AccentPrimaryBrush") as System.Windows.Media.Brush;

        AddressBar.BorderBrush = IsAddressFocused
            ? accent ?? AddressBar.BorderBrush
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x3A));

        // Clear the tile highlight while the address bar has focus, so only one thing
        // looks selected.
        for (var i = 0; i < _homeTiles.Count; i++)
        {
            _homeTiles[i].IsFocused = !IsAddressFocused && i == _homeIndex;
        }
    }

    /// <summary>
    /// Opens the on-screen keyboard to type a web address.
    ///
    /// The page is hidden first: WebView2 paints over WPF content, so a keyboard drawn on
    /// top of a live page would simply be invisible.
    /// </summary>
    public void OpenAddressEntry()
    {
        var current = ActiveWeb.CoreWebView2?.Source;
        var seed = string.IsNullOrEmpty(current) || current == "about:blank"
            ? string.Empty
            : current;

        SetPageVisible(false);
        Keyboard.Show("Enter address", seed);
    }

    /// <summary>
    /// Types a character from a REAL keyboard while the on-screen one is up.
    ///
    /// The on-screen keyboard exists because the console has no keyboard — but when one
    /// is present there is no reason to make someone hunt keys with a D-pad, so physical
    /// typing goes straight into the same field.
    /// </summary>
    public void TypeCharacter(string text) => Keyboard.Type(text);

    /// <summary>Moves the keyboard highlight. Routed from the controller.</summary>
    public void MoveTypingSelection(int dx, int dy)
    {
        if (dx != 0) Keyboard.MoveHorizontal(dx);
        if (dy != 0) Keyboard.MoveVertical(dy);
    }

    public void PressTypingKey() => Keyboard.Activate();
    public void TypingBackspace() => Keyboard.Backspace();
    public void TypingShift() => Keyboard.ToggleShift();
    public void CancelTyping() => Keyboard.Cancel();
    public void AcceptTyping() => Keyboard.Accept();

    private void AddressBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenAddressEntry();

    private void Back_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => GoBack();
    private void Forward_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => GoForward();
    private void Reload_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Reload();
    private void Close_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Hide();

    // ---------------- controller cursor ----------------

    private double _cursorX = 400;
    private double _cursorY = 300;

    /// <summary>Cursor travel per second at full stick deflection.</summary>
    private const double CursorSpeed = 900;

    /// <summary>
    /// Moves the on-screen pointer. Called from the gamepad poll with normalised stick
    /// values (-1..1) and the elapsed time, so speed does not depend on poll rate.
    /// </summary>
    public void MoveCursor(double dx, double dy, double elapsedSeconds)
    {
        if (!_initialised || CursorLayer.ActualWidth <= 0)
        {
            return;
        }

        _cursorX = Math.Clamp(_cursorX + dx * CursorSpeed * elapsedSeconds, 0, CursorLayer.ActualWidth);
        _cursorY = Math.Clamp(_cursorY + dy * CursorSpeed * elapsedSeconds, 0, CursorLayer.ActualHeight);

        // The marker is drawn as well as moving the real pointer: Windows' own arrow is
        // easy to lose on a busy page from across a room, and a console needs a target
        // you can find at a glance.
        Cursor.Visibility = Visibility.Visible;
        Canvas.SetLeft(Cursor, _cursorX - Cursor.Width / 2);
        Canvas.SetTop(Cursor, _cursorY - Cursor.Height / 2);

        var screen = CursorLayer.PointToScreen(new Point(_cursorX, _cursorY));
        NativeCursor.SetPosition((int)screen.X, (int)screen.Y);
    }

    private void HideCursor() => Cursor.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Clicks the page at the cursor's position.
    ///
    /// Drives the REAL Windows pointer rather than synthesising events into WebView2.
    /// `CoreWebView2.SendMouseInput` only exists on the composition-hosted controller,
    /// not the ordinary WPF control, and restructuring the whole browser around
    /// composition hosting to gain it would be a large change for no extra capability:
    /// WebView2 handles a genuine mouse perfectly well, so moving the actual cursor and
    /// clicking gets the same result and works on any page.
    /// </summary>
    public void ClickCursor()
    {
        if (!_initialised || Cursor.Visibility != Visibility.Visible)
        {
            return;
        }

        var screen = CursorLayer.PointToScreen(new Point(_cursorX, _cursorY));

        NativeCursor.SetPosition((int)screen.X, (int)screen.Y);
        NativeCursor.LeftClick();
    }

    /// <summary>Scrolls the page under the pointer.</summary>
    public void Scroll(int notches)
    {
        if (!_initialised)
        {
            return;
        }

        var screen = CursorLayer.PointToScreen(new Point(_cursorX, _cursorY));
        NativeCursor.SetPosition((int)screen.X, (int)screen.Y);
        NativeCursor.Wheel(notches);
    }
}
