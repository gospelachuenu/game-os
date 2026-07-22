using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using InputDaemon;

namespace UI;

/// <summary>
/// Windowed test build only: a normal desktop window, no shell replacement, no
/// focus-stealing hooks, no fullscreen/kiosk behavior. Safe to run repeatedly on a
/// personal dev machine. The real console UI.exe (fullscreen, controller-navigable,
/// integrated with ConsoleSupervisor's FocusGuardian) is a separate future effort.
///
/// SPOTLIGHT navigation model: one flat row of games. Left/Right moves focus along
/// it and the hero above swaps to match (MainWindowViewModel.FocusedGame drives both
/// the hero and the background tint). Up/Down moves between the hero's action buttons
/// (Play / Details) and the game row. There is no multi-row grid — that was the
/// previous design.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly GamepadInputPoller _gamepadPoller;

    /// <summary>
    /// Subtle looping dashboard music, independent of the (muted) ambient video.
    /// Silent and inert unless an audio file exists in Assets/ — see AmbientMusicPlayer.
    /// </summary>
    private readonly AmbientMusicPlayer _music = new();

    /// <summary>
    /// Streaming music (YouTube Music), which keeps playing while the console is used for
    /// something else. Distinct from _music above, which is the dashboard's own ambient
    /// loop — the two are muted against each other so they never play together.
    /// </summary>
    private MusicPlayer? _streamingMusic;

    /// <summary>
    /// Durable console state. In this windowed build it writes to a %TEMP% folder
    /// rather than C:\GamingOS\State (which only exists on the real machine, behind a
    /// UWF write-through exclusion) — a real file store rather than in-memory, so
    /// device mode, the parent PIN and approvals survive a restart and can actually be
    /// tested.
    /// </summary>
    private readonly MaintenanceHub.IConsoleStateStore _consoleState =
        new MaintenanceHub.FileConsoleStateStore(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "console_state_ui_test"));

    /// <summary>
    /// Console self-update.
    ///
    /// Reads a real GitHub-published version.json when <see cref="UpdateManifestUrl"/> is
    /// configured; falls back to a simulated source otherwise, so the windowed dev build
    /// still exercises the flow without a server. The check/download/install logic is the
    /// same either way — only the source of "is there an update" changes.
    /// </summary>
    private readonly MaintenanceHub.SoftwareUpdateService _updates;

    /// <summary>Shared for the update check; one per process.</summary>
    private static readonly System.Net.Http.HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>
    /// Where the console looks for its update manifest.
    ///
    /// Read from the GAMINGOS_UPDATE_MANIFEST environment variable so the URL can be set
    /// per install without a rebuild — the real console sets it, the dev build usually
    /// leaves it unset and gets the simulated source. Point it at the raw version.json,
    /// e.g. https://github.com/<user>/<repo>/releases/latest/download/version.json.
    /// </summary>
    private static string? UpdateManifestUrl =>
        Environment.GetEnvironmentVariable("GAMINGOS_UPDATE_MANIFEST");

    /// <summary>
    /// Parental controls. DORMANT unless this device has been set up as a child's
    /// console (ControlMode.ChildDevice) — an unrestricted console gates nothing. See
    /// the distribution model: the console ships to several people, not all of them
    /// under restrictions.
    /// </summary>
    private readonly ParentalControls.ParentalControlsService _parental;


    public MainWindow()
    {
        InitializeComponent();

        // TEMPORARY: hide the browser rail button while the browser is disabled, so the
        // rail shows only what actually works. Re-enabled by an update flipping the flag.
        if (ConsoleApp.BrowserAndYouTubeDisabled)
        {
            BrowserRailButton.Visibility = Visibility.Collapsed;
        }

        // Update service and parental controls share the one console state store.
        //
        // When a manifest URL is configured (the VM and the real console), use the REAL
        // GitHub source and the REAL downloader — this is what makes an over-the-air
        // update actually work. With no URL set (the windowed laptop build) fall back to
        // the simulated pair so the flow can still be exercised without a server or a
        // real package to fetch.
        var manifestUrl = UpdateManifestUrl;
        var isReal = !string.IsNullOrWhiteSpace(manifestUrl);

        MaintenanceHub.IUpdateSource updateSource = isReal
            ? new MaintenanceHub.GitHubUpdateSource(manifestUrl!, _http)
            : new MaintenanceHub.SimulatedUpdateSource();

        MaintenanceHub.IUpdateDownloader downloader = isReal
            ? new MaintenanceHub.HttpUpdateDownloader(_http)
            : new MaintenanceHub.SimulatedUpdateDownloader(TimeSpan.FromSeconds(20));

        // Packages download next to the install so the swap script can reach them, and so
        // a partial download resumes across reboots rather than restarting.
        var downloadDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(
                System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) ?? AppContext.BaseDirectory,
            "GamingOS-Updates");

        _updates = new MaintenanceHub.SoftwareUpdateService(
            updateSource,
            downloader,
            _consoleState,
            downloadDir);

        _parental = new ParentalControls.ParentalControlsService(_consoleState);

        // Constructing the view model IS the library work — schema, seed and scan all
        // happen synchronously in there — so by the time it returns that step is
        // genuinely done, and the boot screen reports it as such rather than
        // pretending it takes time.
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        Boot.Report(BootStep.LibraryReady);

        // Setup, when it runs, is already on screen by this point — it is raised in
        // Ambient_MediaReady so the boot aperture opens onto it rather than onto the
        // dashboard. Nothing to do here but take focus.
        Boot.Completed += (_, _) =>
        {
            Focus();

            // First boot on a freshly-installed version: offer the patch notes with a
            // toast, once the dashboard is up. Only when setup isn't also running.
            if (_updates.HasUnseenUpdateNotes && !ShouldRunSetup())
            {
                ShowUpdatedToast();
            }
        };

        Boot.InstallRequested += Boot_InstallRequested;

        // Settings clears the browser's remembered site permissions, so it needs the
        // browser itself — there is one instance, and MainWindow owns it.
        Settings.Browser = Browser;

        Browser.Opened += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Collapsed;

            // Silence the console's own music while browsing — otherwise it plays over
            // every video the user watches. Same treatment as launching a game.
            _music.FadeOut();

            // NOT ducked here any more: the browser IS the player, so pausing it on open
            // would stop the very thing the user came back to.
        };
        Browser.CloseRequested += (_, _) =>
        {
            // The browser-only actions belong to the browser, not the dashboard.
            Guide.SetPermissionActionVisible(false, string.Empty);
            Guide.SetBrowserActionsVisible(false);

            // When an app is closing, ClosingAsApp has already restored the dashboard and
            // owns the shrink animation — restoring again here would fight it and bring
            // the music up before the animation had finished.
            if (_closingApp)
            {
                _closingApp = false;
                return;
            }

            ContentGrid.Visibility = Visibility.Visible;
            _music.FadeIn();

            // The page is gone, so the speakers are free again.
            _ = _streamingMusic?.UnduckAsync();

            // Announce whatever is still playing, if the track changed while the page was
            // in front and the announcement was held back.
            if (_pendingToast is { } held)
            {
                _pendingToast = null;
                ShowMusicToast(held);
            }

            Focus();
        };

        // Patch notes restore the dashboard when closed, same as the other overlays.
        PatchNotes.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        PatchNotes.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        // Fired as the wizard STARTS fading, not after — the ambient video sits
        // outside ContentGrid, so any gap between the two leaves the video playing
        // bare. The dashboard rises into place while setup is still on its way out.
        Setup.Exiting += (_, _) =>
        {
            PlayDashboardEntrance();

            // Already playing through setup in the normal first-run path; this covers
            // the F9 replay, where the music was stopped on the way in. No-op if it is
            // already running.
            _music.Start();
        };

        Setup.Completed += (_, _) =>
        {
            MarkSetupComplete();
            Focus();
        };

        // Keep the dashboard content hidden until the ambient video's first frame has
        // actually decoded, so tiles never flash in over a plain dark background
        // while the MediaElement is still starting up.
        Ambient.MediaReady += Ambient_MediaReady;

        _viewModel.GameLaunchRequested += ViewModel_GameLaunchRequested;
        _viewModel.GameDetailRequested += ViewModel_GameDetailRequested;
        _viewModel.FocusedTileChanged += ViewModel_FocusedTileChanged;
        // Start LOADING the app once the splash covers the screen — but the browser stays
        // invisible while it does.
        //
        // It cannot simply be shown here: BrowserScreen is declared AFTER LaunchOverlay in
        // MainWindow.xaml, so it renders on top of the splash, and its root is a near
        // opaque near-black. Showing it mid-launch dropped a black sheet over the splash
        // less than a second in — which is exactly the "black overlay" that kept ruining
        // this. It is only made visible in AppAboutToUncover, once the splash is done.
        LaunchTakeover.AppSplashCovered += async (_, _) =>
        {
            if (_launchingApp is { Kind: ConsoleApp.AppKind.Web, Url: { } url })
            {
                Browser.BeginAppLoad(url);
            }
        };

        // The page has loaded: let the splash finish and uncover it.
        Browser.AppReady += (_, _) => LaunchTakeover.CompleteAppLaunch();

        // Show the page only as the splash starts to lift.
        LaunchTakeover.AppAboutToUncover += (_, _) => Browser.RevealApp();

        // Watch whatever the browser plays, so the mini-player, the guide's transport
        // controls and the now-playing toasts work for it.
        Browser.MediaViewReady += (_, _) => AttachMusicWatcher();

        // Closing an app plays the launch backwards, shrinking it into its tile.
        Browser.ClosingAsApp += (_, _) =>
        {
            var app = _viewModel.FocusedApp;
            if (app is null)
            {
                return;
            }

            // Tells the CloseRequested handler (which fires next) to leave the restore
            // alone — the shrink animation below owns it.
            _closingApp = true;

            // The dashboard has to be up BEFORE the shrink starts: the tile is the
            // animation's destination, and it has no position while the grid is hidden.
            ContentGrid.Visibility = Visibility.Visible;
            _zone = DashboardZone.AppRow;
            _viewModel.RefreshAppFocus();
            UpdateZoneHighlight();

            // Let layout settle so the tile reports a real rect, then shrink into it.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var tile = FindAppTile(app);
                if (tile is null)
                {
                    _music.FadeIn();
                    _ = _streamingMusic?.UnduckAsync();
                    Focus();
                    return;
                }

                LaunchTakeover.ShrinkToTile(GetElementScreenRect(tile), app, () =>
                {
                    _music.FadeIn();
                    _ = _streamingMusic?.UnduckAsync();
                    Focus();
                });
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };

        LaunchTakeover.Dismissed += (_, _) =>
        {
            // An app is now showing in front of the dashboard, so leave the dashboard
            // hidden and the music down — the app owns the screen until it is closed.
            if (_launchingApp is not null)
            {
                _launchingApp = null;
                return;
            }

            // The launch swallow is entered from the game detail screen, which
            // collapsed the dashboard; restore it when the swallow is dismissed.
            ContentGrid.Visibility = Visibility.Visible;

            // Back on the dashboard — bring the ambient music back up.
            _music.FadeIn();
            _ = _streamingMusic?.UnduckAsync();
            Focus();
        };

        Guide.BatteryIcon = _viewModel.BatteryIcon;
        Guide.ControllerSetupRequested += (_, _) => ControllerSetup.Show();

        Guide.RevokePermissionsRequested += async (_, _) =>
        {
            Guide.PermissionDetail = "Revoking…";
            await Browser.ClearRememberedPermissionsAsync();
            Guide.PermissionDetail = "Revoked — sites will ask again";
        };

        // Bring the page back whenever the guide closes, however it was closed — B, Tab,
        // Escape, or the scrim. Without this, closing it any way other than MENU would
        // leave the browser showing an empty frame.
        Guide.Closed += (_, _) => Browser.SetPageVisible(true);

        // Confirms too — the same action deserves the same safeguard whichever route
        // reaches it.
        Guide.CloseBrowserRequested += (_, _) => Browser.ConfirmExit();

        // Music transport from the guide — the only way to control playback without
        // leaving whatever is on screen, which is the point of having it there.
        Guide.PlayPauseRequested += async (_, _) => await RunMusicCommandAsync(p => p.PlayPauseAsync());
        Guide.NextTrackRequested += async (_, _) => await RunMusicCommandAsync(p => p.NextAsync());
        Guide.PreviousTrackRequested += async (_, _) => await RunMusicCommandAsync(p => p.PreviousAsync());
        Guide.VolumeChanged += (_, volume) => _ = _streamingMusic?.SetVolumeAsync(volume);

        ControllerSetup.CloseRequested += (_, _) => Focus();

        // Selecting a game opens the detail screen (keeps ambient video, hides
        // dashboard content). Its Play button fires the real launch swallow; backing
        // out restores the dashboard.
        GameDetail.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        GameDetail.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };
        GameDetail.PlayRequested += (detail, thumbRect) =>
        {
            // THE LAUNCH GATE. On an unrestricted console this always allows; on a
            // child device it applies the full policy. Refused launches never start
            // the swallow animation — they show why instead.
            var decision = _parental.CanLaunch(detail.Entry.GameId);
            if (!decision.IsAllowed)
            {
                ShowLaunchBlocked(detail.Entry.GameId, detail.GameName, decision);
                return;
            }

            _pendingLaunchRect = thumbRect;

            // Prefer the real cover image for the launch swallow (with the overlay's
            // own darkening scrim keeping the text readable); fall back to the gradient
            // art when the game has no cover image.
            Brush launchArt = detail.IconSource is not null
                ? new ImageBrush(detail.IconSource) { Stretch = Stretch.UniformToFill }
                : detail.ArtBrush;

            _viewModel.RaiseLaunch(detail.Entry, launchArt, detail.GameName);
        };

        // Updates is its OWN screen, not an overlay layered on top of the dashboard —
        // hide the ambient video and dashboard content entirely while it's open
        // instead of leaving them rendering/playing underneath a translucent panel.
        // Updates takes over the home screen's CONTENT (game rows + rail), but keeps
        // the SAME ambient video playing behind it — so the screen swaps what's shown
        // without the background ever cutting to flat black. Only ContentGrid is
        // hidden; Ambient keeps running underneath, and UpdatesScreen's own background
        // is transparent so the video shows through.
        Updates.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        Updates.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        // Installing an AMD driver takes the whole screen with controls locked
        // (plan.md §6.1) — nothing is reachable until it completes.
        Updates.DriverInstallRequested += update =>
            DriverInstall.Show(update.Title, $"Version {update.Version} · AMD Radeon RX 6600M");

        // Settings gets the same treatment — same ambient video behind it.
        Settings.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        Settings.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        // Real physical controller support (Xbox natively via XInput; PlayStation
        // controllers work too as long as something on the system — Steam, DS4Windows
        // — translates them into an XInput device, which Windows does not do on its
        // own). Drives the exact same navigation methods the keyboard handler below
        // calls, so there is one single source of truth for what each action does.
        _gamepadPoller = new GamepadInputPoller(new XInputGamepadReader());
        _gamepadPoller.MoveUp += () =>
        {
            // Guide first — it is drawn over the page and owns navigation while open.
            if (Guide.IsOpen) { Guide.MoveSelection(-1); return; }

            if (Browser.IsForeground)
            {
                // The keyboard owns input while it is up — it is drawn over everything.
                if (Browser.IsTyping) { Browser.MoveTypingSelection(0, -1); return; }
                // Swallowed while a prompt is up: their buttons are side by side, so
                // vertical movement means nothing and must not reach the page behind.
                if (Browser.IsConfirmingExit || Browser.IsPromptingPermission) return;
                // On the start screen, up reaches the address bar — the only way to get
                // to it without a mouse.
                if (Browser.IsOnHome) { Browser.MoveHomeVertical(-1); return; }
                if (Browser.UsesKeyNavigation) Browser.SendKey(NativeKeyboard.VirtualKey.Up);
                return;
            }
            if (IsUiLocked) return;
            if (Updates.Visibility == Visibility.Visible) Updates.MoveSelection(-1);
            else if (Settings.Visibility == Visibility.Visible) Settings.MoveSelection(-1);
            else if (Guide.IsOpen) Guide.MoveSelection(-1);
            else if (_isRailFocused) MoveRailSelection(-1);
            else if (!IsAnyOverlayOpen) MoveZone(-1);
        };
        _gamepadPoller.MoveDown += () =>
        {
            if (Guide.IsOpen) { Guide.MoveSelection(1); return; }

            if (Browser.IsForeground)
            {
                if (Browser.IsTyping) { Browser.MoveTypingSelection(0, 1); return; }
                if (Browser.IsConfirmingExit || Browser.IsPromptingPermission) return;
                if (Browser.IsOnHome) { Browser.MoveHomeVertical(1); return; }
                if (Browser.UsesKeyNavigation) Browser.SendKey(NativeKeyboard.VirtualKey.Down);
                return;
            }
            if (IsUiLocked) return;
            if (Updates.Visibility == Visibility.Visible) Updates.MoveSelection(1);
            else if (Settings.Visibility == Visibility.Visible) Settings.MoveSelection(1);
            else if (Guide.IsOpen) Guide.MoveSelection(1);
            else if (_isRailFocused) MoveRailSelection(1);
            else if (!IsAnyOverlayOpen) MoveZone(1);
        };
        // On the Updates screen, Left/Right moves between the detail panel's two
        // action buttons. In the Guide Menu (single-column) Left/Right is meaningless
        // and swallowed. On the dashboard it moves along the game row, or between the
        // hero's Play/Details buttons when the hero zone has focus.
        // The boot screen's install offer is checked BEFORE IsUiLocked in each of
        // these: that flag deliberately blocks all input while the boot screen is up,
        // which would otherwise make the offer unanswerable from a controller.
        _gamepadPoller.MoveLeft += () =>
        {
            if (Boot.IsAwaitingUpdateChoice) { Boot.HandleOfferButton(left: true, false, false, false); return; }
            // The guide's music rows use Left/Right — transport selection and volume —
            // so it gets horizontal input rather than swallowing it.
            if (Guide.IsOpen) { Guide.MoveHorizontal(-1); return; }

            if (Browser.IsForeground)
            {
                if (Browser.IsTyping) { Browser.MoveTypingSelection(-1, 0); return; }
                if (Browser.IsConfirmingExit) { Browser.MoveExitSelection(-1); return; }
                // A permission prompt owns input while it is up — it must be answerable,
                // and the page behind it must not react underneath.
                if (Browser.IsPromptingPermission) Browser.MovePermissionSelection(-1);
                // Start screen picks a tile; leanback takes an arrow key; an ordinary
                // page is left alone because the stick already drives its cursor.
                else if (Browser.IsOnHome) Browser.MoveHomeSelection(-1);
                else if (Browser.UsesKeyNavigation) Browser.SendKey(NativeKeyboard.VirtualKey.Left);
                return;
            }
            if (IsUiLocked) return;
            if (PatchNotes.Visibility == Visibility.Visible) PatchNotes.Back();
            else if (Updates.Visibility == Visibility.Visible) Updates.MoveButtonSelection(-1);
            else if (!IsAnyOverlayOpen) MoveHorizontal(-1);
        };
        _gamepadPoller.MoveRight += () =>
        {
            if (Boot.IsAwaitingUpdateChoice) { Boot.HandleOfferButton(false, right: true, false, false); return; }
            if (Guide.IsOpen) { Guide.MoveHorizontal(1); return; }

            if (Browser.IsForeground)
            {
                if (Browser.IsTyping) { Browser.MoveTypingSelection(1, 0); return; }
                if (Browser.IsConfirmingExit) { Browser.MoveExitSelection(1); return; }
                if (Browser.IsPromptingPermission) Browser.MovePermissionSelection(1);
                else if (Browser.IsOnHome) Browser.MoveHomeSelection(1);
                else if (Browser.UsesKeyNavigation) Browser.SendKey(NativeKeyboard.VirtualKey.Right);
                return;
            }
            if (IsUiLocked) return;
            if (PatchNotes.Visibility == Visibility.Visible) PatchNotes.Next();
            else if (Updates.Visibility == Visibility.Visible) Updates.MoveButtonSelection(1);
            else if (!IsAnyOverlayOpen) MoveHorizontal(1);
        };
        _gamepadPoller.Confirm += () =>
        {
            if (Boot.IsAwaitingUpdateChoice) { Boot.HandleOfferButton(false, false, confirm: true, false); return; }
            if (LaunchBlocked.Visibility == Visibility.Visible)
            {
                // A confirms the primary action: Ask a parent if offered, else OK.
                if (AskParentButton.Visibility == Visibility.Visible
                    && BlockedRequestSent.Visibility != Visibility.Visible)
                {
                    AskParent_Click(this, null!);
                }
                else
                {
                    HideLaunchBlocked();
                }
                return;
            }
            // The guide sits ON TOP of the browser, so it takes input first — otherwise
            // its buttons are unreachable and A goes to the page behind it.
            if (Guide.IsOpen) { Guide.ActivateSelected(); return; }

            if (Browser.IsForeground)
            {
                if (Browser.IsTyping) Browser.PressTypingKey();
                // The address bar, when it has the highlight, opens the keyboard.
                else if (Browser.IsAddressFocused) Browser.OpenAddressEntry();
                else if (Browser.IsConfirmingExit) Browser.ActivateExitSelection();
                else if (Browser.IsPromptingPermission) Browser.ConfirmPermission();
                // Start screen opens a destination; leanback takes Enter; an ordinary
                // page gets a click wherever the pointer is.
                else if (Browser.IsOnHome) Browser.ActivateHomeSelection();
                else if (Browser.UsesKeyNavigation) Browser.SendKey(NativeKeyboard.VirtualKey.Return);
                else Browser.ClickCursor();
                return;
            }
            if (IsUiLocked) return;
            if (PatchNotes.Visibility == Visibility.Visible) PatchNotes.Confirm();
            else if (Settings.Visibility == Visibility.Visible) Settings.ActivateSelected();
            else if (Updates.Visibility == Visibility.Visible) Updates.ActivateSelected();
            else if (GameDetail.Visibility == Visibility.Visible) GameDetail.ActivateSelected();
            else if (Guide.IsOpen) Guide.ActivateSelected();
            else if (_isRailFocused) ActivateRailSelection();
            else if (!IsAnyOverlayOpen) ActivateDashboardSelection();
        };
        _gamepadPoller.Back += () =>
        {
            if (Boot.IsAwaitingUpdateChoice) { Boot.HandleOfferButton(false, false, false, cancel: true); return; }
            if (LaunchBlocked.Visibility == Visibility.Visible) { HideLaunchBlocked(); return; }
            // Guide first, for the same reason as Confirm: it is the topmost thing.
            if (Guide.IsOpen) { Guide.Close(); return; }

            // B backs out WITHIN the TV app — closing a video, leaving a menu — which is
            // what its own interface expects. HOLD B leaves the music app entirely; see
            // the BackHeld handler.

            if (Browser.IsForeground)
            {
                // Backing out of the keyboard abandons what was typed.
                if (Browser.IsTyping) { Browser.CancelTyping(); return; }
                // Backing out of the exit prompt means "no, stay".
                if (Browser.IsConfirmingExit) { Browser.CancelExit(); return; }
                // Backing out of a permission prompt declines it.
                if (Browser.IsPromptingPermission) Browser.CancelPermission();
                // On leanback, Escape backs out WITHIN YouTube (closing a video, leaving
                // a menu) — its own navigation, which is what a user expects. Elsewhere
                // B walks the browser's history.
                else if (Browser.UsesKeyNavigation)
                {
                    // Backing out of a VIDEO stops it. YouTube's own behaviour is to
                    // return to the browse screen with the audio still running, which
                    // sounds exactly like the background-playback feature but is not —
                    // it is a video playing to nobody. The page decides music vs video
                    // itself, so a wrong guess here cannot silence someone's music.
                    _ = _streamingMusic?.StopIfVideoAsync();

                    Browser.SendKey(NativeKeyboard.VirtualKey.Escape);
                }
                else Browser.GoBack();
                return;
            }
            if (IsUiLocked) return;
            if (PatchNotes.Visibility == Visibility.Visible)
            {
                PatchNotes.Back();
            }
            else if (ControllerSetup.Visibility == Visibility.Visible)
            {
                ControllerSetup.Hide();
            }
            else if (Updates.Visibility == Visibility.Visible)
            {
                Updates.Hide();
            }
            else if (Settings.Visibility == Visibility.Visible)
            {
                Settings.Hide();
            }
            else if (GameDetail.Visibility == Visibility.Visible)
            {
                GameDetail.Hide();
            }
            else if (Guide.IsOpen)
            {
                Guide.Close();
            }
            else if (_isRailFocused)
            {
                SetRailFocused(false);
            }
        };
        // Continuous stick motion drives the browser's pointer. Only while a PAGE is
        // showing — on the browser's own start screen the discrete Move events pick
        // tiles instead, which is the right feel for a row of buttons.
        _gamepadPoller.StickMoved += (x, y, elapsed) =>
        {
            // The music app is the TV interface: arrow keys drive it, and a cursor
            // drifting over it would be both useless and confusing.
            if (_musicOnScreen) return;

            // Never on a key-navigated page: leanback is driven by arrow keys, and a
            // cursor drifting over it is both useless and confusing.
            if (Browser.IsForeground
                && !Browser.IsOnHome
                && !Browser.UsesKeyNavigation)
            {
                Browser.MoveCursor(x, y, elapsed);
            }
        };

        // Holding B leaves the browser. Needed because on a key-navigated page a TAP of B
        // belongs to the page (YouTube uses it to close a video), so without this there is
        // no button that reliably gets you out of a full-screen site.
        _gamepadPoller.BackHeld += () =>
        {
            if (!Browser.IsForeground) return;

            if (Guide.IsOpen) Guide.Close();
            if (Browser.IsPromptingPermission) Browser.CancelPermission();

            // While something is PLAYING, hold B leaves without asking and without
            // stopping it — the page is parked off-screen and keeps going, which is how
            // background music works on this console. There is nothing to confirm because
            // nothing is being lost.
            // MUSIC ONLY. A video left running in the background is just audio from a
            // picture nobody can see, so only recognised music earns the free pass.
            if (_streamingMusic is { Current.CanPlayInBackground: true })
            {
                var nowPlaying = _streamingMusic.Current;
                Browser.LeavePlaying();

                // Say what is playing as they arrive back on the dashboard — this is the
                // moment it becomes useful, and the track itself may have started long
                // before while the page was still in front.
                _pendingToast = null;
                ShowMusicToast(nowPlaying);
                return;
            }

            // Anything else — a video, or nothing playing — is a page being closed for
            // good, so ask first.
            Browser.ConfirmExit();
        };

        _gamepadPoller.ToggleGuideMenu += () =>
        {
            // While browsing, MENU opens the guide OVER the page rather than quitting.
            // On a full-screen page the rail is hidden, so this is the only route to
            // system functions — including revoking a site's microphone access, which
            // is needed precisely while the offending page is in front of you.
            if (Browser.IsForeground)
            {
                // While typing, MENU means "done" — the keyboard's own hint says so, and
                // opening the guide over a half-typed address would be a strange answer.
                if (Browser.IsTyping) { Browser.AcceptTyping(); return; }

                if (Browser.IsPromptingPermission) Browser.CancelPermission();
                ToggleGuideOverBrowser();
                return;
            }

            if (!IsUiLocked) Guide.Toggle();
        };
        // X and Y serve the on-screen keyboard only. Backspace and shift get their own
        // buttons because reaching them across the grid is the slowest part of typing.
        _gamepadPoller.Secondary += () =>
        {
            if (Browser.IsForeground && Browser.IsTyping) Browser.TypingBackspace();
        };

        _gamepadPoller.Tertiary += () =>
        {
            if (Browser.IsForeground && Browser.IsTyping) Browser.TypingShift();
        };

        _musicToastTimer.Tick += (_, _) =>
        {
            _musicToastTimer.Stop();
            HideMusicToast();
        };

        _gamepadPoller.Start();

        // Ask whether a newer version of the console software exists. Fire-and-forget
        // rather than awaited: the constructor cannot be async, and the check is
        // internally bounded by a 3s timeout, so it either lands quickly or reports
        // failure without ever delaying startup.
        _ = CheckForSoftwareUpdateAsync();

        Boot.Report(BootStep.ControllersReady);

        // Live clock in the top-right status line.
        _clockTimer.Tick += (_, _) => _viewModel.RefreshClock();
        _clockTimer.Start();

        Loaded += (_, _) => UpdateZoneHighlight();
        Closed += (_, _) =>
        {
            _gamepadPoller.Stop();
            _clockTimer.Stop();
            _music.Stop();
        };
    }

    private readonly System.Windows.Threading.DispatcherTimer _clockTimer =
        new() { Interval = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// True while any full-screen/modal overlay is showing (Guide Menu, Controller
    /// Setup wizard, Updates screen, or the launch takeover). Dashboard tile
    /// navigation must be suppressed whenever this is true, otherwise stray gamepad
    /// input meant for whichever overlay is open silently moves focus on the
    /// dashboard underneath, which only becomes visible once the overlay closes.
    /// </summary>
    private bool IsAnyOverlayOpen =>
        Guide.IsOpen
        || ControllerSetup.Visibility == Visibility.Visible
        || Updates.Visibility == Visibility.Visible
        || Settings.Visibility == Visibility.Visible
        || GameDetail.Visibility == Visibility.Visible
        || LaunchTakeover.Visibility == Visibility.Visible
        || PatchNotes.Visibility == Visibility.Visible
        || LaunchBlocked.Visibility == Visibility.Visible
        || Browser.IsForeground
        || IsUiLocked;

    /// <summary>
    /// True while the driver-install takeover owns the screen. plan.md §6.1 requires
    /// user controls to be LOCKED during an install — every input path checks this
    /// first and swallows the input, so nothing (not even Back or the Guide button)
    /// can be reached over it.
    /// </summary>
    private bool IsUiLocked =>
        DriverInstall.Visibility == Visibility.Visible
        || Boot.Visibility == Visibility.Visible
        // First-run setup owns all input while it is up. IsFinishing extends that
        // past the point the wizard stops being Visible: the exit animation runs for
        // about a second, and without it the A press that entered the final PIN digit
        // was still held on the dashboard poller's next tick — which launched
        // whichever game happened to have focus.
        || Setup.Visibility == Visibility.Visible
        || Setup.IsFinishing;

    /// <summary>
    /// True while gamepad/keyboard focus has moved off the dashboard onto the side
    /// rail's Updates/Settings buttons. Left at the first game in the row hands focus
    /// here; Right, or Confirm activating a button, hands it back.
    /// </summary>
    private bool _isRailFocused;
    private int _railFocusIndex;
    // Order must match the visual order in the rail (top to bottom) and the switch in
    // ActivateRailSelection.
    private static readonly string[] RailButtonNames =
        { "BrowserRailBorder", "UpdatesRailBorder", "SettingsRailBorder" };

    /// <summary>
    /// Which zone of the Spotlight dashboard has focus. The hero's action buttons and
    /// the game row are the two navigable zones; Up/Down moves between them, Left/Right
    /// means "previous/next button" in the hero and "previous/next game" in the row.
    /// </summary>
    /// <summary>
    /// MiniPlayer is only reachable while something is playing — MoveZone skips it
    /// otherwise, so the dashboard does not have a dead stop at the bottom.
    /// </summary>
    private enum DashboardZone { Hero, GameRow, AppRow, MiniPlayer }

    private DashboardZone _zone = DashboardZone.GameRow;

    /// <summary>0 = Play, 1 = Details. Only meaningful while _zone == Hero.</summary>
    private int _heroButtonIndex;

    /// <summary>The thumbnail rect (window-relative) the last Play was launched from — the swallow grows out of it.</summary>
    private Rect _pendingLaunchRect;

    /// <summary>Up/Down: move between the hero's action buttons and the game row.</summary>
    private void MoveZone(int delta)
    {
        if (_isRailFocused)
        {
            return;
        }

        // Hero â†’ games â†’ apps â†’ mini-player, top to bottom, matching their order on
        // screen. Clamped rather than wrapping: on a console, holding down should come to
        // rest at the bottom, not loop back to the top.
        var zones = new List<DashboardZone>
        {
            DashboardZone.Hero,
            DashboardZone.GameRow,
            DashboardZone.AppRow,
        };

        // Only offered when there is actually music to control.
        if (MiniPlayer.Visibility == Visibility.Visible)
        {
            zones.Add(DashboardZone.MiniPlayer);
        }

        var order = zones.ToArray();
        var index = Array.IndexOf(order, _zone);

        // If the mini-player vanished while focused, fall back to the apps row.
        if (index < 0)
        {
            index = order.Length - 1;
        }

        var next = order[Math.Clamp(index + delta, 0, order.Length - 1)];

        if (next == _zone)
        {
            return;
        }

        _zone = next;
        _heroButtonIndex = 0;

        // Each row shows its highlight only while it holds focus — otherwise both light
        // up at once and there is no telling which one A will act on.
        _viewModel.RefreshAppFocus(rowHasFocus: _zone == DashboardZone.AppRow);
        _viewModel.RefreshGameFocus(rowHasFocus: _zone == DashboardZone.GameRow);

        UpdateZoneHighlight();
    }

    /// <summary>Left/Right: along the game row, or between the hero's Play/Details buttons.</summary>
    private void MoveHorizontal(int delta)
    {
        if (_isRailFocused)
        {
            if (delta > 0)
            {
                SetRailFocused(false);
            }

            return;
        }

        if (_zone == DashboardZone.Hero)
        {
            _heroButtonIndex = Math.Clamp(_heroButtonIndex + delta, 0, 1);
            UpdateZoneHighlight();
            return;
        }

        if (_zone == DashboardZone.MiniPlayer)
        {
            // Left/Right skips tracks — the obvious meaning for a transport control.
            if (delta > 0)
            {
                _ = _streamingMusic?.NextAsync();
            }
            else
            {
                _ = _streamingMusic?.PreviousAsync();
            }

            return;
        }

        if (_zone == DashboardZone.AppRow)
        {
            // Same rule as the game row: left off the first app reaches the side rail.
            var atFirstApp = _viewModel.Apps.Count > 0
                && ReferenceEquals(_viewModel.FocusedApp, _viewModel.Apps[0]);

            if (delta < 0 && atFirstApp)
            {
                SetRailFocused(true);
                return;
            }

            _viewModel.MoveAppFocus(delta);
            return;
        }

        // In the game row: moving left off the first game hands focus to the side rail.
        var atFirst = _viewModel.Games.Count > 0 && ReferenceEquals(_viewModel.FocusedGame, _viewModel.Games[0]);
        if (delta < 0 && atFirst)
        {
            SetRailFocused(true);
            return;
        }

        _viewModel.MoveFocus(delta);
    }

    /// <summary>Confirm on the dashboard: hero buttons act, game row opens the focused game's details.</summary>
    private void ActivateDashboardSelection()
    {
        if (_zone == DashboardZone.Hero)
        {
            if (_heroButtonIndex == 0)
            {
                PlayFocusedGame();
            }
            else
            {
                _viewModel.OpenFocusedDetails();
            }

            return;
        }

        if (_zone == DashboardZone.AppRow)
        {
            LaunchFocusedApp();
            return;
        }

        if (_zone == DashboardZone.MiniPlayer)
        {
            _ = _streamingMusic?.PlayPauseAsync();
            return;
        }

        _viewModel.OpenFocusedDetails();
    }

    /// <summary>
    /// Opens the highlighted app, growing the launch splash out of its tile.
    ///
    /// Apps deliberately do NOT go through the parental launch gate: that gate is about
    /// games, and blocking YouTube belongs to the browser's own policy where it can be
    /// applied per-site rather than all-or-nothing. See docs/browser-design.md.
    /// </summary>
    private void LaunchFocusedApp()
    {
        var app = _viewModel.FocusedApp;
        if (app is null)
        {
            return;
        }

        var tile = FindAppTile(app);
        LaunchApp(app, tile is null ? default : GetElementScreenRect(tile));
    }

    /// <summary>Finds the on-screen tile for an app, so the splash can grow out of it.</summary>
    private FrameworkElement? FindAppTile(ConsoleApp app)
    {
        var container = AppRowItems.ItemContainerGenerator.ContainerFromItem(app) as FrameworkElement;

        // The ItemsControl wraps each item in a ContentPresenter; the tile is inside it.
        return container is null ? null : FindVisualChild<Border>(container) ?? container;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the launch sequence for an app: the splash grows from the tile, and the app's
    /// own screen opens behind it once it is ready.
    /// </summary>
    private void LaunchApp(ConsoleApp app, Rect fromRect)
    {
        // Already running in the background? Then this is a RETURN, not a launch.
        //
        // Going through the splash would navigate the browser to the app's URL and throw
        // away the page that was playing — the music would stop the moment the user
        // pressed the tile. Sliding the live page back is both correct and quicker.
        if (Browser.IsPlayingInBackground)
        {
            ContentGrid.Visibility = Visibility.Collapsed;
            _music.FadeOut();

            // ResumeApp, not Show: Show opens the BROWSER, which would leave the app's
            // page running invisibly behind an empty browsing view.
            Browser.ResumeApp();
            return;
        }

        _launchingApp = app;

        ContentGrid.Visibility = Visibility.Collapsed;
        _music.FadeOut();

        // No ducking: the app being opened IS the player, so pausing it here would stop
        // the music the moment the user went back to it.
        LaunchTakeover.ShowForApp(fromRect, app);
    }

    /// <summary>The app whose splash is currently showing, if any.</summary>
    private ConsoleApp? _launchingApp;

    /// <summary>Set while an app's closing animation is running, so the browser's own close handler stands aside.</summary>
    private bool _closingApp;

    // ---------------- streaming music ----------------

    /// <summary>True while the music page is on screen rather than parked off it.</summary>
    private bool _musicOnScreen;

    private readonly System.Windows.Threading.DispatcherTimer _musicToastTimer = new()
    {
        Interval = TimeSpan.FromSeconds(6),
    };

    /// <summary>
    /// A track that started while the browser was in front, held back until the user
    /// leaves. Without this the announcement is lost entirely: tracks change while you
    /// are still in YouTube, and by the time you leave nothing new fires.
    /// </summary>
    private MusicState? _pendingToast;

    /// <summary>
    /// Returns the console to its normal audio: streaming music resumes if the console
    /// paused it, otherwise the ambient loop comes back.
    ///
    /// One place rather than a FadeIn beside every UnduckAsync, because the two must
    /// never both happen — the ambient loop playing under a song is exactly the kind of
    /// double audio this whole mechanism exists to prevent.
    /// </summary>
    private void RestoreConsoleAudio()
    {
        if (_streamingMusic is { HasSession: true })
        {
            _ = _streamingMusic.UnduckAsync();
            return;
        }

        _music.FadeIn();
    }

    /// <summary>
    /// Brings up the music service, creating it on first use, and shows its page.
    /// </summary>
    /// <summary>
    /// Watches whatever the BROWSER is playing, so the console's own playback controls
    /// work for it.
    ///
    /// There is no separate music app: YouTube is the one player, and this simply reports
    /// what it is doing to the mini-player, the guide's transport controls and the
    /// now-playing toasts. Attaching to the browser rather than running a second web view
    /// is what keeps it to one YouTube instead of two.
    /// </summary>
    /// <summary>
    /// The character a key produces, or null for keys that type nothing.
    ///
    /// Deliberately small: letters, digits and the punctuation a web address needs. A full
    /// keyboard-layout mapping would be a lot of code for a field whose whole point is
    /// that most users are on a controller.
    /// </summary>
    private static string? KeyToText(Key key)
    {
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (key is >= Key.A and <= Key.Z)
        {
            var letter = (char)('a' + (key - Key.A));
            return shift ? char.ToUpperInvariant(letter).ToString() : letter.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9 && !shift)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        return key switch
        {
            Key.OemPeriod or Key.Decimal => ".",
            Key.OemMinus or Key.Subtract => shift ? "_" : "-",
            Key.OemQuestion or Key.Divide => "/",
            Key.OemSemicolon => shift ? ":" : ";",
            Key.D2 when shift => "@",
            _ => null,
        };
    }

    /// <summary>
    /// Runs a transport command and reports what the page actually did.
    ///
    /// TEMPORARY diagnostic: the previous skip buttons used selectors from a different
    /// site and silently matched nothing, so every press was a no-op with no way to tell.
    /// Showing the outcome in the guide makes a dead button say why.
    /// </summary>
    private async Task RunMusicCommandAsync(Func<MusicPlayer, Task> command)
    {
        if (_streamingMusic is null)
        {
            return;
        }

        await command(_streamingMusic);
    }

    private void AttachMusicWatcher()
    {
        if (_streamingMusic is not null)
        {
            return;
        }

        _streamingMusic = new MusicPlayer(Browser.MediaView);
        _streamingMusic.StateChanged += (_, state) => UpdateMiniPlayer(state);
        _streamingMusic.TrackChanged += (_, state) => ShowMusicToast(state);

        Guide.SetVolumeDisplay(_streamingMusic.Volume);

        _ = _streamingMusic.AttachAsync();
    }


    private void UpdateMiniPlayer(MusicState state)
    {
        // The mini-player is a MUSIC control. A video's title sitting on the dashboard
        // would suggest it is playing in the background when it is not — leaving a video
        // stops it, so there would be nothing to control.
        if (!state.HasTrack || (!state.IsMusic && !state.IsAd))
        {
            MiniPlayer.Visibility = Visibility.Collapsed;
            Guide.SetNowPlaying(false, string.Empty, string.Empty, null, false);


            // Focus cannot stay on a widget that has just gone.
            if (_zone == DashboardZone.MiniPlayer)
            {
                _zone = DashboardZone.AppRow;
                _viewModel.RefreshAppFocus();
                UpdateZoneHighlight();
            }

            return;
        }

        MiniPlayer.Visibility = Visibility.Visible;
        MiniPlayerTitle.Text = state.IsAd ? "Advert" : state.Title;
        MiniPlayerArtist.Text = state.IsAd ? "Music resumes shortly" : state.Artist;

        // Paused music still deserves a mini-player — it says what is loaded, and the
        // icon carries the state rather than the widget vanishing and reappearing.
        MiniPlayerIcon.Text = state.IsPlaying ? "▶" : "⏸";

        var art = LoadArtwork(state.ArtworkUrl);
        MiniPlayerArt.Source = art;

        // Keep the guide's now-playing card in step, so opening it mid-game shows what is
        // actually on rather than whatever was playing when it was last opened.
        Guide.SetNowPlaying(
            hasMusic: true,
            title: state.IsAd ? "Advert" : state.Title,
            artist: state.IsAd ? "Music resumes shortly" : state.Artist,
            artwork: art,
            isPlaying: state.IsPlaying);
    }

    /// <summary>
    /// Album art from the service. Downloaded by WPF itself rather than fetched here —
    /// BitmapImage handles caching and does it off the UI thread.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapImage? LoadArtwork(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            return image;
        }
        catch (Exception e) when (e is UriFormatException or NotSupportedException or System.IO.IOException)
        {
            // Missing art is not worth a failure — the tile just stays blank.
            return null;
        }
    }

    /// <summary>
    /// Announces a new track top-left for a few seconds.
    ///
    /// Only while the browser is NOT in front: with the page on screen the user can see
    /// what is playing, so a toast over it is noise. The moment they leave, this becomes
    /// the only way to know what came on.
    /// </summary>
    private void ShowMusicToast(MusicState state)
    {
        // Only music earns an announcement — a video does not play on after you leave, so
        // there would be nothing to announce.
        if (!state.HasTrack || (!state.IsMusic && !state.IsAd))
        {
            return;
        }

        // Nothing to say while the user is looking straight at the page.
        if (Browser.IsForeground)
        {
            // But remember it: leaving is exactly when they want to know what is on, and
            // the track will not change again just because they left.
            _pendingToast = state;
            return;
        }

        MusicToastKicker.Text = state.IsAd ? "ADVERT" : "NOW PLAYING";
        MusicToastTitle.Text = state.IsAd ? "Advert playing" : state.Title;
        MusicToastArtist.Text = state.IsAd ? "Music resumes shortly" : state.Artist;
        MusicToastArt.Source = LoadArtwork(state.ArtworkUrl);

        MusicToast.Visibility = Visibility.Visible;

        // Clear any in-flight animation first, or a track change during the previous
        // toast's fade leaves it stuck part-way.
        MusicToast.BeginAnimation(OpacityProperty, null);
        MusicToastSlide.BeginAnimation(TranslateTransform.YProperty, null);

        MusicToast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));

        MusicToastSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-60, 0, TimeSpan.FromSeconds(0.42))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

        _musicToastTimer.Stop();
        _musicToastTimer.Start();
    }

    private void HideMusicToast()
    {
        var fade = new DoubleAnimation(MusicToast.Opacity, 0, TimeSpan.FromSeconds(0.4));
        fade.Completed += (_, _) => MusicToast.Visibility = Visibility.Collapsed;
        MusicToast.BeginAnimation(OpacityProperty, fade);

        MusicToastSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -60, TimeSpan.FromSeconds(0.4))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
    }

    private void AppTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConsoleApp app })
        {
            return;
        }

        // Clicking also moves focus, so returning from the app leaves the highlight on
        // the tile that was used rather than wherever the controller last was.
        _zone = DashboardZone.AppRow;
        _viewModel.FocusApp(app);
        UpdateZoneHighlight();

        LaunchApp(app, GetElementScreenRect((FrameworkElement)sender));
    }

    /// <summary>Paints the accent ring on whichever dashboard element currently has focus.</summary>
    private void UpdateZoneHighlight()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var heroActive = _zone == DashboardZone.Hero && !_isRailFocused;

        PlayButtonRing.BorderBrush = heroActive && _heroButtonIndex == 0 ? accent : Brushes.Transparent;
        DetailsButtonRing.BorderBrush = heroActive && _heroButtonIndex == 1 ? accent : Brushes.Transparent;

        // Mini-player shows its own ring when focused, so it is clear that A and
        // Left/Right now mean playback rather than navigation.
        MiniPlayer.BorderBrush = _zone == DashboardZone.MiniPlayer && !_isRailFocused
            ? accent
            : new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));
    }

    /// <summary>
    /// Launches the focused game, with the swallow growing out of the PLAY BUTTON —
    /// the thing the user actually pressed — rather than the game's cover tile down in
    /// the row. Growing from the tile made the animation appear to come in from the
    /// side of the screen instead of from the point of interaction.
    /// </summary>
    private void PlayFocusedGame()
    {
        // The dashboard hero's Play button is a SECOND launch path (the game detail
        // screen is the other). Both must pass the gate — a game blocked on one screen
        // cannot be launchable from another.
        var focused = _viewModel.FocusedGame;
        if (focused is not null)
        {
            var decision = _parental.CanLaunch(focused.Entry.GameId);
            if (!decision.IsAllowed)
            {
                ShowLaunchBlocked(focused.Entry.GameId, focused.GameName, decision);
                return;
            }
        }

        _pendingLaunchRect = GetElementScreenRect(HeroPlayButton);
        _viewModel.PlayFocusedGame();
    }

    /// <summary>
    /// An element's bounds relative to RootGrid — the coordinate space LaunchOverlay's
    /// panel is positioned in, so the swallow can start exactly over it.
    /// </summary>
    private Rect GetElementScreenRect(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        return element.TransformToVisual(RootGrid)
                      .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private void SetRailFocused(bool focused)
    {
        _isRailFocused = focused;

        // Start past the hidden browser button while it is disabled.
        _railFocusIndex = ConsoleApp.BrowserAndYouTubeDisabled ? 1 : 0;

        // Focus is on the rail now, so neither row should still look selected. Coming
        // back, only the row that actually holds focus lights up again.
        _viewModel.RefreshGameFocus(rowHasFocus: !focused && _zone == DashboardZone.GameRow);
        _viewModel.RefreshAppFocus(rowHasFocus: !focused && _zone == DashboardZone.AppRow);

        UpdateRailHighlight();
        UpdateZoneHighlight();
    }

    private void MoveRailSelection(int delta)
    {
        _railFocusIndex = Math.Clamp(_railFocusIndex + delta, 0, RailButtonNames.Length - 1);

        // Skip the hidden browser button (index 0) while it is disabled — landing focus on
        // an invisible control would look like the highlight vanished.
        if (ConsoleApp.BrowserAndYouTubeDisabled && _railFocusIndex == 0)
        {
            _railFocusIndex = 1;
        }

        UpdateRailHighlight();
    }

    private void ActivateRailSelection()
    {
        switch (_railFocusIndex)
        {
            // Browser disabled for now — see ConsoleApp.BrowserAndYouTubeDisabled. The
            // button is hidden and skipped, but guard here too so nothing can reach it.
            case 0:
                if (!ConsoleApp.BrowserAndYouTubeDisabled) Browser.Show();
                break;
            case 1: Updates.Show(); break;
            default: Settings.Show(); break;
        }
    }

    private void UpdateRailHighlight()
    {
        var accentBrush = (Brush)FindResource("Theme.AccentPrimaryBrush");
        for (var i = 0; i < RailButtonNames.Length; i++)
        {
            var button = i switch
            {
                0 => BrowserRailButton,
                1 => UpdatesRailButton,
                _ => SettingsRailButton,
            };
            button.ApplyTemplate();
            if (button.Template.FindName(RailButtonNames[i], button) is Border border)
            {
                border.BorderBrush = _isRailFocused && _railFocusIndex == i ? accentBrush : Brushes.Transparent;
            }
        }
    }

    private void ViewModel_GameDetailRequested(GameTileViewModel tile)
    {
        GameDetail.Show(tile);
    }

    private void ViewModel_GameLaunchRequested(Brush art, string gameName, string uri)
    {
        System.Diagnostics.Debug.WriteLine($"[LaunchOverlay] Would launch: {uri}");

        // The console's own ambient loop stops — it is background furniture, and a game
        // has its own soundtrack.
        _music.FadeOut();

        // Streaming music deliberately KEEPS PLAYING into a game: it is a deliberate
        // choice by the user, the kind of thing people put on instead of a game's own
        // music. Both mix through Windows, so turning the game's music down is the user's
        // call. A VIDEO is different — see the browser handler, which does duck.


        // Grow the swallow out of the detail screen's thumbnail (captured when Play was
        // pressed), falling back to the full window if we somehow have no rect.
        var startRect = _pendingLaunchRect.Width > 0
            ? _pendingLaunchRect
            : new Rect(0, 0, ActualWidth, ActualHeight);
        LaunchTakeover.Show(startRect, art, gameName);
    }

    /// <summary>
    /// Keeps the newly-focused game centred-ish in the horizontal row, and cross-fades
    /// the hero so the title/art swap reads as a deliberate transition rather than a
    /// hard cut. Spotlight's row scrolls sideways, so this works on HorizontalOffset.
    /// </summary>
    private void ViewModel_FocusedTileChanged(GameTileViewModel tile)
    {
        AnimateHeroSwap();

        var tileElement = FindTileElement(tile);
        if (tileElement is null)
        {
            return;
        }

        var transform = tileElement.TransformToVisual(GameRowScroller);
        var tileLeft = transform.Transform(new Point(0, 0)).X;
        var tileRight = tileLeft + tileElement.ActualWidth;

        double? targetOffset = null;
        if (tileLeft < 0)
        {
            targetOffset = GameRowScroller.HorizontalOffset + tileLeft - 40;
        }
        else if (tileRight > GameRowScroller.ViewportWidth)
        {
            targetOffset = GameRowScroller.HorizontalOffset + (tileRight - GameRowScroller.ViewportWidth) + 40;
        }

        if (targetOffset is null)
        {
            return;
        }

        AnimateScrollTo(Math.Max(0, targetOffset.Value));
    }

    /// <summary>
    /// Fades and lifts the hero slightly whenever the focused game changes, so the
    /// whole-screen swap has motion behind it instead of the text just popping.
    /// </summary>
    private void AnimateHeroSwap()
    {
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.34) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        var lift = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.34) };
        lift.KeyFrames.Add(new LinearDoubleKeyFrame(14, KeyTime.FromPercent(0)));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        HeroPanel.BeginAnimation(OpacityProperty, fade);
        HeroSlide.BeginAnimation(TranslateTransform.YProperty, lift);
    }

    /// <summary>
    /// ScrollViewer has no dependency property for HorizontalOffset that
    /// BeginAnimation can target directly, so the scroll is driven manually via a
    /// DispatcherTimer stepping through eased values instead of a real Storyboard.
    /// </summary>
    private void AnimateScrollTo(double targetOffset)
    {
        var startOffset = GameRowScroller.HorizontalOffset;
        const int steps = 16;
        var duration = TimeSpan.FromSeconds(0.28);
        var stepInterval = TimeSpan.FromTicks(duration.Ticks / steps);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = stepInterval };
        var currentStep = 0;
        timer.Tick += (_, _) =>
        {
            currentStep++;
            var progress = Math.Min(1.0, currentStep / (double)steps);
            var eased = easing.Ease(progress);
            GameRowScroller.ScrollToHorizontalOffset(startOffset + (targetOffset - startOffset) * eased);

            if (currentStep >= steps)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Walks the visual tree under SectionsScrollViewer looking for the FrameworkElement
    /// whose DataContext is the given tile — works across however many nested rows/
    /// ItemsControls the dashboard has, unlike indexing a single named ItemsControl.
    /// </summary>
    private FrameworkElement? FindTileElement(GameTileViewModel tile)
    {
        return FindByDataContext(RootGrid, tile);
    }

    private static FrameworkElement? FindByDataContext(DependencyObject root, object dataContext)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } element && ReferenceEquals(element.DataContext, dataContext))
            {
                return element;
            }

            var found = FindByDataContext(child, dataContext);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the currently-focused tile's realized visual element and returns its
    /// bounds relative to the root Grid (the same coordinate space LaunchOverlay's
    /// Panel is positioned in), so the launch takeover can start exactly over the
    /// tile the user selected and grow from there — a "swallow" transition rather
    /// than a separate screen appearing on top.
    /// </summary>
    private Rect GetFocusedTileScreenRect()
    {
        var focusedTile = _viewModel.FocusedGame;
        if (focusedTile is null)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        var tileElement = FindTileElement(focusedTile);
        if (tileElement is null)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        var transform = tileElement.TransformToVisual(RootGrid);
        var topLeft = transform.Transform(new Point(0, 0));

        return new Rect(topLeft.X, topLeft.Y, tileElement.ActualWidth, tileElement.ActualHeight);
    }

    private void Ambient_MediaReady(object? sender, EventArgs e)
    {
        // On a FIRST RUN the boot aperture must open onto the setup wizard, not the
        // dashboard. Everything the reveal will expose therefore has to be in place
        // BEFORE Boot.Report(AmbientReady) below starts the hand-off — showing setup
        // later, on Boot.Completed, meant the dashboard was briefly visible through
        // the opening aperture before setup covered it.
        if (ShouldRunSetup())
        {
            ContentGrid.Opacity = 0;

            // fadeIn: false — setup is sitting under the boot screen and the aperture
            // is about to expose it. Fading in here would mean the square opens onto a
            // screen still materialising instead of one already there.
            Setup.Show(fadeIn: false);

            // Music plays through setup as well as the dashboard — it is the same
            // console either way, and starting it only at the hand-off left the whole
            // first-run experience silent.
            _music.Start();

            Boot.Report(BootStep.AmbientReady);
            return;
        }

        // Snap the dashboard to fully visible rather than fading it in. It is sitting
        // UNDERNEATH the boot screen at this point, so the user cannot see it yet —
        // the boot screen's panels opening is the one and only reveal. Fading here as
        // well meant the dashboard was already on screen before the panels parted,
        // which made the whole opening effect pointless.
        ContentGrid.BeginAnimation(OpacityProperty, null);
        ContentGrid.Opacity = 1;

        // Bring the music up with the visuals rather than before them, so the
        // dashboard arrives as one thing. No-op if no audio file is present.
        _music.Start();

        // The video's first frame has decoded — the last genuinely async startup
        // step. The boot screen can now hand off to the dashboard underneath it.
        Boot.Report(BootStep.AmbientReady);
    }

    /// <summary>
    /// The first-run arrival: the dashboard builds itself on screen, one piece at a
    /// time, from off the edges.
    ///
    ///   0.00s  background wash comes up
    ///   0.10s  side rail slides in from beyond the left edge
    ///   0.28s  top status bar drops in from above
    ///   0.42s  hero swings up from below and settles
    ///   0.62s  game tiles fly up one after another, 55ms apart
    ///
    /// Each piece travels a real distance from OUTSIDE the screen rather than nudging
    /// a few pixels, and they overlap rather than queueing — that overlap is what
    /// stops it feeling like a checklist of animations playing in turn.
    ///
    /// Only ever runs after first-run setup. Normal boots keep the aperture reveal as
    /// their one and only entrance; animating underneath it is what made that reveal
    /// pointless the first time round.
    /// </summary>
    private void PlayDashboardEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Ground first, so everything else arrives onto a surface that already exists.
        ContentGrid.BeginAnimation(OpacityProperty, null);
        ContentGrid.Opacity = 0;
        ContentGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.45)) { EasingFunction = ease });

        // Rail: in from off the left edge. 84px wide, so -110 starts it clear of the
        // screen rather than merely offset.
        SideRailSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-110, 0, TimeSpan.FromSeconds(0.62))
            {
                BeginTime = TimeSpan.FromSeconds(0.10),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
            });

        // Status bar: down from above the top edge.
        TopStatusSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-90, 0, TimeSpan.FromSeconds(0.55))
            {
                BeginTime = TimeSpan.FromSeconds(0.28),
                EasingFunction = ease,
            });

        // Hero: up from below, far enough to read as entering rather than shifting.
        // Uses the EXISTING HeroSlide transform — the focus lift animates the same
        // property, and a second transform would silently fight it.
        HeroPanel.BeginAnimation(OpacityProperty, null);
        HeroPanel.Opacity = 0;
        HeroPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            {
                BeginTime = TimeSpan.FromSeconds(0.42),
                EasingFunction = ease,
            });

        HeroSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(150, 0, TimeSpan.FromSeconds(0.75))
            {
                BeginTime = TimeSpan.FromSeconds(0.42),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            });

        // Apps row: last in, from below the bottom edge. Arriving after the games row
        // keeps the reading order top-to-bottom rather than everything landing at once.
        AppRow.BeginAnimation(OpacityProperty, null);
        AppRow.Opacity = 0;
        AppRow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            {
                BeginTime = TimeSpan.FromSeconds(0.72),
                EasingFunction = ease,
            });

        AppRowSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(120, 0, TimeSpan.FromSeconds(0.7))
            {
                BeginTime = TimeSpan.FromSeconds(0.72),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            });

        AnimateGameTilesIn();
    }

    /// <summary>
    /// Flies the game tiles up one after another, so the row assembles rather than
    /// appearing whole. The stagger is the entire point — 55ms apart is enough to
    /// read as a sequence without turning into a queue you have to wait out.
    /// </summary>
    private void AnimateGameTilesIn()
    {
        // The ItemsControl generates containers asynchronously, so on the first pass
        // there is usually nothing to animate yet. Wait for the generator to finish
        // rather than animating an empty row.
        if (GameRowItems.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
        {
            void OnReady(object? s, EventArgs e)
            {
                if (GameRowItems.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                {
                    return;
                }

                GameRowItems.ItemContainerGenerator.StatusChanged -= OnReady;
                AnimateGameTilesIn();
            }

            GameRowItems.ItemContainerGenerator.StatusChanged += OnReady;
            return;
        }

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

        for (var i = 0; i < GameRowItems.Items.Count; i++)
        {
            if (GameRowItems.ItemContainerGenerator.ContainerFromIndex(i)
                is not FrameworkElement tile)
            {
                continue;
            }

            var begin = TimeSpan.FromSeconds(0.62 + i * 0.055);

            var slide = new TranslateTransform(0, 120);
            tile.RenderTransform = slide;

            tile.BeginAnimation(OpacityProperty, null);
            tile.Opacity = 0;
            tile.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { BeginTime = begin });

            var rise = new DoubleAnimation(120, 0, TimeSpan.FromSeconds(0.6))
            {
                BeginTime = begin,
                EasingFunction = ease,
            };

            // Clear the transform once it lands: the tiles carry their own scale
            // animations for focus, and a leftover translate would offset them.
            rise.Completed += (_, _) => tile.RenderTransform = null;
            slide.BeginAnimation(TranslateTransform.YProperty, rise);
        }
    }

    /// <summary>
    /// The boot-time software update check, and the background download that may
    /// follow it.
    ///
    /// Checking and downloading are deliberately separate. The check is a small
    /// request that either answers in a moment or times out; the download is tens of
    /// megabytes whose duration depends entirely on the connection. Boot waits for the
    /// first and never for the second — otherwise a slow line would hold the whole
    /// console at its logo screen.
    ///
    /// An update downloaded during one session is offered for install on the NEXT
    /// boot, by which point the files are local and the install genuinely takes
    /// seconds rather than minutes.
    /// </summary>
    private async Task CheckForSoftwareUpdateAsync()
    {
        var result = await _updates.CheckAsync();

        // Reported regardless of the outcome. A failed check is a normal event — no
        // network, server down — and is explicitly not a reason to hold up a boot.
        Boot.Report(BootStep.UpdateChecked);

        switch (result.Outcome)
        {
            case MaintenanceHub.UpdateCheckOutcome.ReadyToInstall when result.Release is not null:
                // Downloaded on an earlier boot. Because the files are already local
                // the install is quick, which is what the offer's subtitle says.
                Boot.ShowUpdateOffer(
                    result.Release.Version,
                    "Already downloaded · takes about 20 seconds");
                break;

            case MaintenanceHub.UpdateCheckOutcome.UpdateAvailable when result.Release is not null:
                // TEST BUILD ONLY: show the install offer immediately rather than
                // downloading first.
                //
                // On the real console this branch starts a background download and the
                // offer appears on the NEXT boot, once the files are local. That can
                // never happen here because this build's state store is in-memory and
                // starts empty every run, so the offer would otherwise be unreachable
                // to look at.
                if (ShowUpdateOfferForTesting)
                {
                    Boot.ShowUpdateOffer(
                        result.Release.Version,
                        $"{result.Release.SizeLabel} · takes about a minute");
                    break;
                }

                // Download in the background and offer it on the NEXT boot. Boot never
                // waits for a transfer whose duration depends on the connection.
                _updates.StartBackgroundDownload(result.Release);
                break;
        }
    }

    /// <summary>
    /// Set false to exercise the real path (download now, offer next boot) instead of
    /// showing the offer on every run.
    /// </summary>
    private const bool ShowUpdateOfferForTesting = true;

    /// <summary>
    /// Runs an install the user accepted from the boot screen.
    ///
    /// Two paths. When a real package was downloaded (the VM and the console), it is
    /// unpacked, a swap script is launched, and the console EXITS so the script can
    /// replace its now-unlocked files and relaunch. When there is no real package (the
    /// windowed laptop build), it falls back to stepping a progress bar so the screen can
    /// still be exercised — there is nothing to actually install there.
    /// </summary>
    private async void Boot_InstallRequested(string version)
    {
        // Recorded BEFORE anything is applied, so a power cut midway still leaves the
        // console knowing which version it came from.
        _updates.BeginInstall(version);

        var packagePath = _updates.PendingPackagePath;
        var haveRealPackage = !string.IsNullOrEmpty(packagePath) && System.IO.File.Exists(packagePath);

        if (haveRealPackage)
        {
            await ApplyRealUpdateAsync(packagePath!);
            return;
        }

        // No real package — simulate, so the flow is still demonstrable on the dev laptop.
        var steps = new (int Percent, string Caption)[]
        {
            (15, "Preparing…"),
            (40, "Applying files…"),
            (70, "Updating settings…"),
            (95, "Finishing up…"),
            (100, "Done"),
        };

        foreach (var (percent, caption) in steps)
        {
            await Task.Delay(600);
            Boot.ReportInstallProgress(percent, caption);
        }

        await Task.Delay(400);
        Boot.CompleteInstall();
    }

    /// <summary>
    /// Applies a downloaded package for real: unpack, hand off to the swap script, exit.
    ///
    /// The swap cannot happen from inside this process — a running program cannot
    /// overwrite its own files — so the last thing done here is quit and let the script
    /// take over. The unpacking (the slow, failure-prone part) is done FIRST, while the
    /// console is still up and can show an honest error, rather than after it has quit.
    /// </summary>
    private async Task ApplyRealUpdateAsync(string packagePath)
    {
        var installer = new MaintenanceHub.UpdateInstaller();

        Boot.ReportInstallProgress(20, "Unpacking…");

        // Unpacking is CPU/disk work; keep it off the UI thread so the progress bar the
        // user is watching does not freeze mid-install.
        var stageDir = await Task.Run(() => installer.StagePackage(packagePath));

        if (stageDir is null)
        {
            // Package was corrupt or truncated. Discard it so the next boot re-downloads
            // rather than trying to install the same broken file again.
            _updates.DiscardPending();
            Boot.ReportInstallProgress(0, "Update failed — will retry next boot");
            await Task.Delay(2500);
            Boot.CompleteInstall();
            return;
        }

        Boot.ReportInstallProgress(70, "Applying…");
        await Task.Delay(500);

        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                  ?? System.IO.Path.Combine(AppContext.BaseDirectory, "UI.exe");

        // Reboot to finish, so the file swap happens inside the machine's own restart and
        // the user never sees the Windows shell. Opt out with GAMINGOS_UPDATE_RELAUNCH=1
        // to relaunch in place instead — for testing on the VM without rebooting each time.
        var relaunchInPlace =
            Environment.GetEnvironmentVariable("GAMINGOS_UPDATE_RELAUNCH") == "1";

        var launched = installer.ApplyStagedAndRelaunch(stageDir, exe, reboot: !relaunchInPlace);

        if (!launched)
        {
            _updates.DiscardPending();
            Boot.ReportInstallProgress(0, "Update failed — will retry next boot");
            await Task.Delay(2500);
            Boot.CompleteInstall();
            return;
        }

        Boot.ReportInstallProgress(100, "Restarting…");
        await Task.Delay(800);

        // Hand the screen to the swap script and get out of its way — it is waiting on
        // this process to exit before it can replace the files.
        Application.Current.Shutdown();
    }

    private System.Windows.Threading.DispatcherTimer? _updatedToastTimer;

    // The game the block screen is currently about, so "Ask a parent" queues the right one.
    private string? _blockedGameId;
    private string? _blockedGameName;

    /// <summary>
    /// Shows the parental block screen with a reason. "Ask a parent" is only offered
    /// for a game that a parent COULD approve (pending or blocked) — for a time-limit
    /// or curfew refusal, or the whole console being disabled, asking achieves nothing,
    /// so only "OK" is shown.
    /// </summary>
    private void ShowLaunchBlocked(string gameId, string gameName, ParentalControls.LaunchDecision decision)
    {
        _blockedGameId = gameId;
        _blockedGameName = gameName;

        var canAsk = decision.Verdict is ParentalControls.LaunchVerdict.GamePending
            or ParentalControls.LaunchVerdict.GameBlocked;

        BlockedTitle.Text = canAsk ? "Ask a parent" : "Not right now";
        BlockedReason.Text = decision.Reason;
            BlockedIcon.Text = decision.Verdict == ParentalControls.LaunchVerdict.DailyLimitReached ? "⏰" : "🔒";

        AskParentButton.Visibility = canAsk ? Visibility.Visible : Visibility.Collapsed;
        BlockedRequestSent.Visibility = Visibility.Collapsed;
        BlockedButtons.Visibility = Visibility.Visible;

        LaunchBlocked.Visibility = Visibility.Visible;
        LaunchBlocked.BeginAnimation(OpacityProperty, null);
        LaunchBlocked.Opacity = 0;
        LaunchBlocked.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
    }

    private void HideLaunchBlocked()
    {
        var fade = new DoubleAnimation(LaunchBlocked.Opacity, 0, TimeSpan.FromSeconds(0.2));
        fade.Completed += (_, _) => LaunchBlocked.Visibility = Visibility.Collapsed;
        LaunchBlocked.BeginAnimation(OpacityProperty, fade);

        // Restore the dashboard. The block can interrupt the DETAIL-SCREEN launch path,
        // which collapses itself and hides ContentGrid expecting the launch swallow to
        // take over and restore it. When the launch is blocked instead, that restore
        // never runs — leaving just the ambient video on screen. Bring it back here.
        ContentGrid.Visibility = Visibility.Visible;
        Focus();
    }

    private void AskParent_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_blockedGameId is not null && _blockedGameName is not null)
        {
            _parental.RequestApproval(_blockedGameId, _blockedGameName);
        }

        // Confirm in place so the kid sees their request landed, then close on its own
        // after a moment. Leaving only the tick with the buttons hidden was a dead end
        // — no OK to press and Esc dropped the whole dashboard.
        BlockedButtons.Visibility = Visibility.Collapsed;
        BlockedRequestSent.Visibility = Visibility.Visible;

        var dismiss = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.8),
        };
        dismiss.Tick += (_, _) =>
        {
            dismiss.Stop();
            HideLaunchBlocked();
        };
        dismiss.Start();
    }

    private void BlockedOk_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => HideLaunchBlocked();

    /// <summary>
    /// Shows the "Updated" toast bottom-right, offering the patch notes. It auto-
    /// dismisses after a while so it never lingers, but — importantly — dismissing the
    /// TOAST does not mark the notes as seen. Only opening the notes (or the console
    /// moving on to a later version) does that, so a kid clearing the toast once
    /// doesn't permanently bury what changed.
    /// </summary>
    private void ShowUpdatedToast()
    {
        UpdatedToastTitle.Text = $"Now on version {_updates.InstalledVersion}";

        UpdatedToast.Visibility = Visibility.Visible;
        UpdatedToast.BeginAnimation(OpacityProperty, null);
        UpdatedToast.Opacity = 0;
        UpdatedToast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)) { BeginTime = TimeSpan.FromSeconds(0.6) });

        _updatedToastTimer?.Stop();
        _updatedToastTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        _updatedToastTimer.Tick += (_, _) =>
        {
            _updatedToastTimer!.Stop();
            HideUpdatedToast();
        };
        _updatedToastTimer.Start();
    }

    private void HideUpdatedToast()
    {
        _updatedToastTimer?.Stop();
        var fade = new DoubleAnimation(UpdatedToast.Opacity, 0, TimeSpan.FromSeconds(0.3));
        fade.Completed += (_, _) => UpdatedToast.Visibility = Visibility.Collapsed;
        UpdatedToast.BeginAnimation(OpacityProperty, fade);
    }

    private void SeeWhatsNew_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HideUpdatedToast();

        // The notes are what mark the update as "seen" — opening them clears the flag,
        // so they appear exactly once per version.
        _updates.MarkNotesSeen();

        var release = MaintenanceHub.SimulatedUpdateSource.SampleRelease;
        PatchNotes.Show(release.Version, release.Notes, packageDirectory: null);
    }

    private void DismissUpdated_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Dismissing the toast does NOT mark the notes as seen — the toast can return
        // on the next boot. Only viewing the notes clears them.
        HideUpdatedToast();
    }

    /// <summary>
    /// Marker file recording that first-run setup has been completed.
    ///
    /// Windowed TEST BUILD ONLY — deliberately a file in %TEMP% rather than anything
    /// resembling real console state. On target hardware this becomes a proper
    /// settings store on a UWF write-through path, alongside the parent PIN; a marker
    /// in a volatile temp folder would be wiped by the write filter on every reboot
    /// and setup would run forever.
    ///
    /// Delete this file (or press F9) to see the wizard again.
    /// </summary>
    private static string SetupMarkerPath =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "console_setup_complete.marker");

    private static bool ShouldRunSetup() => !System.IO.File.Exists(SetupMarkerPath);

    private static void MarkSetupComplete()
    {
        try
        {
            System.IO.File.WriteAllText(SetupMarkerPath, DateTime.UtcNow.ToString("o"));
        }
        catch (System.IO.IOException)
        {
            // Not being able to record completion is not worth taking the UI down
            // for — the cost is that setup runs again next launch.
        }
    }

    /// <summary>
    /// True when the process was started with --fullscreen (or --kiosk).
    ///
    /// Deliberately OPT-IN rather than the default. A borderless, always-on-top window
    /// covering the screen is genuinely awkward to escape from, and this build is run
    /// on a personal development laptop — see the project's standing constraint about
    /// never running kiosk-style code there. The VM and the real console pass the flag;
    /// a plain `dotnet run` on a dev machine stays a normal, closable window.
    /// </summary>
    private static bool IsFullscreenRequested =>
        Environment.GetCommandLineArgs()
                   .Any(a => a.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--kiosk", StringComparison.OrdinalIgnoreCase));

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!IsFullscreenRequested)
        {
            return;
        }

        // Borderless full-screen: no title bar, no chrome, covering the whole display.
        // WindowState is set AFTER WindowStyle because switching to None while already
        // maximised leaves a window sized to the work area (i.e. stopping short of the
        // taskbar) rather than the full screen.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;

        // Topmost keeps the console above anything Windows raises behind it. Only
        // applied under --kiosk: it makes the window hard to get away from, which is
        // right for an appliance and wrong for a test build.
        if (Environment.GetCommandLineArgs()
                       .Any(a => a.Equals("--kiosk", StringComparison.OrdinalIgnoreCase)))
        {
            Topmost = true;
        }
    }

    /// <summary>
    /// Test-build-only shortcuts, on PreviewKeyDown so they fire BEFORE any overlay or
    /// child element can swallow the key, and regardless of where focus sits. None of
    /// this exists on the real console.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+M — toggle this device between Unrestricted and ChildDevice, so the
        // launch gate can be exercised. (Not an F-key: the laptop's F-keys are hardware
        // hotkeys for display/brightness.)
        if (e.Key == Key.M && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _parental.Device.Mode = _parental.IsActive
                ? ParentalControls.ControlMode.Unrestricted
                : ParentalControls.ControlMode.ChildDevice;

            // Give it a PIN in child mode so approvals can be tested, and pre-unlock it.
            if (_parental.IsActive && !_parental.Pin.IsSet)
            {
                _parental.Pin.Set("1234");
            }

            var mode = _parental.IsActive ? "CHILD DEVICE (locked down)" : "UNRESTRICTED";
            Title = $"Gaming OS — {mode} · device {_parental.Device.Id}";
            e.Handled = true;
            return;
        }

        // Ctrl+B — open the browser. Disabled for now along with the tile and rail button.
        if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && !ConsoleApp.BrowserAndYouTubeDisabled
            && !Browser.IsForeground
            && Boot.Visibility != Visibility.Visible)
        {
            Browser.Show();
            e.Handled = true;
            return;
        }

        // Ctrl+N — open the patch notes on demand. NOT an F-key: F11 never reached the
        // app (Windows and the laptop's own hotkeys claim most of them), which is the
        // same reason Ctrl+M replaced F8 for the mode toggle.
        if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && PatchNotes.Visibility != Visibility.Visible
            && Boot.Visibility != Visibility.Visible)
        {
            var release = MaintenanceHub.SimulatedUpdateSource.SampleRelease;
            PatchNotes.Show(release.Version, release.Notes, packageDirectory: null);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Test-build convenience: force the update install offer onto the boot screen.
        //
        // It normally only appears when an update was downloaded on an EARLIER boot,
        // which cannot happen in this build because its state store is in-memory and
        // starts empty every run. Without this the offer would be unreachable to look
        // at. Not present on the real console.
        // Test-build convenience: toggle this device between Unrestricted and
        // ChildDevice so the launch gate can actually be exercised. Not present on the
        // real console — mode is set at setup there. (Ctrl+M — the laptop's F-keys are
        // hardware hotkeys for display/brightness and can't be used.)
        // Test-build convenience: show the "what's new" patch notes on demand. Not
        // reachable from a controller; not present on the real console.

        // Browser. On the start screen the arrows pick a destination; on a page the
        // keys fall through to the web view so typing still works.
        if (Browser.IsForeground)
        {
            // A real keyboard types straight into the on-screen one. Handled here rather
            // than by focusing a TextBox because the console's window keeps focus for
            // controller input, so key events arrive at the window either way.
            if (Browser.IsTyping)
            {
                switch (e.Key)
                {
                    case Key.Enter: Browser.AcceptTyping(); break;
                    case Key.Escape: Browser.CancelTyping(); break;
                    case Key.Back: Browser.TypingBackspace(); break;
                    case Key.Space: Browser.TypeCharacter(" "); break;
                    default:
                        var typed = KeyToText(e.Key);
                        if (typed is not null) Browser.TypeCharacter(typed);
                        break;
                }

                e.Handled = true;
                return;
            }

            // The exit confirmation owns the keyboard while it is up, so Escape means
            // "no, stay" rather than walking history behind the dialog.
            if (Browser.IsConfirmingExit)
            {
                switch (e.Key)
                {
                    case Key.Left: Browser.MoveExitSelection(-1); break;
                    case Key.Right: Browser.MoveExitSelection(1); break;
                    case Key.Enter: Browser.ActivateExitSelection(); break;
                    case Key.Escape or Key.Back: Browser.CancelExit(); break;
                }

                e.Handled = true;
                return;
            }

            if (e.Key is Key.Escape or Key.Back)
            {
                Browser.GoBack();
                e.Handled = true;
                return;
            }

            if (Browser.IsOnHome)
            {
                switch (e.Key)
                {
                    case Key.Left: Browser.MoveHomeSelection(-1); e.Handled = true; break;
                    case Key.Right: Browser.MoveHomeSelection(1); e.Handled = true; break;
                    case Key.Enter: Browser.ActivateHomeSelection(); e.Handled = true; break;
                }
            }

            return;
        }

        // Block screen: Enter confirms the primary action, Esc dismisses.
        if (LaunchBlocked.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter && AskParentButton.Visibility == Visibility.Visible
                && BlockedRequestSent.Visibility != Visibility.Visible)
            {
                AskParent_Click(this, null!);
            }
            else if (e.Key is Key.Enter or Key.Escape or Key.Back)
            {
                HideLaunchBlocked();
            }
            e.Handled = true;
            return;
        }

        // Patch notes take keyboard input while open: left/right page, Enter advances,
        // Esc/Back leaves.
        if (PatchNotes.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Left: PatchNotes.Back(); break;
                case Key.Right: PatchNotes.Next(); break;
                case Key.Enter: PatchNotes.Confirm(); break;
                case Key.Escape or Key.Back: PatchNotes.Hide(); break;
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F10 && Boot.Visibility == Visibility.Visible)
        {
            Boot.ShowUpdateOffer("1.4.0", "Already downloaded · takes about 20 seconds");
            e.Handled = true;
            return;
        }

        // Test-build convenience: replay first-run setup without hunting down the
        // marker file. Not reachable from a controller, so it can't be hit by accident
        // on the console itself.
        // Escape hatch for the fullscreen/kiosk build: a borderless top-most window
        // with no title bar has no other way out, and being unable to close the thing
        // you are testing is its own kind of trap. F12 drops back to a normal window.
        // The real console will not have this.
        if (e.Key == Key.F12 && IsFullscreenRequested)
        {
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9 && Setup.Visibility != Visibility.Visible)
        {
            try
            {
                System.IO.File.Delete(SetupMarkerPath);
            }
            catch (System.IO.IOException)
            {
                // Nothing useful to do; the wizard still runs for this session.
            }

            // Music keeps playing across the transition — setup has the same ambient
            // track as the dashboard, so stopping and restarting it would be an
            // audible break for no reason.
            ContentGrid.BeginAnimation(OpacityProperty, null);
            ContentGrid.Opacity = 0;
            Setup.Show();
            e.Handled = true;
            return;
        }

        // The boot screen's install offer takes input before anything else — it is on
        // screen, it is a question, and it must be answerable without a mouse.
        if (Boot.IsAwaitingUpdateChoice)
        {
            Boot.HandleOfferKey(e.Key);
            e.Handled = true;
            return;
        }

        // Setup owns the whole screen while it's up. Keys are forwarded to it
        // explicitly rather than relying on OnKeyDown: the Window handles input for
        // the whole app, so the UserControl would never see them on its own.
        if (Setup.Visibility == Visibility.Visible)
        {
            Setup.HandleKey(e.Key);
            e.Handled = true;
            return;
        }

        // plan.md 6.1: controls are locked during a driver install.
        if (IsUiLocked)
        {
            return;
        }

        if (ControllerSetup.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Tab or Key.B or Key.Escape)
            {
                ControllerSetup.Hide();
            }

            return;
        }

        if (Updates.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Updates.MoveSelection(-1);
                    break;
                case Key.Down:
                    Updates.MoveSelection(1);
                    break;
                case Key.Left:
                    Updates.MoveButtonSelection(-1);
                    break;
                case Key.Right:
                    Updates.MoveButtonSelection(1);
                    break;
                case Key.Enter:
                    Updates.ActivateSelected();
                    break;
                case Key.Tab or Key.B or Key.Escape:
                    Updates.Hide();
                    break;
            }

            return;
        }

        if (Settings.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Tab or Key.B or Key.Escape)
            {
                Settings.Hide();
            }

            return;
        }

        if (GameDetail.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    GameDetail.ActivateSelected();
                    break;
                case Key.B or Key.Escape or Key.Tab:
                    GameDetail.Hide();
                    break;
            }

            return;
        }

        if (Guide.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Guide.MoveSelection(-1);
                    break;
                case Key.Down:
                    Guide.MoveSelection(1);
                    break;
                case Key.Enter:
                    Guide.ActivateSelected();
                    break;
                case Key.Tab or Key.Escape:
                    Guide.Close();
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveHorizontal(-1);
                break;
            case Key.Right:
                MoveHorizontal(1);
                break;
            case Key.Up:
                if (_isRailFocused) MoveRailSelection(-1);
                else MoveZone(-1);
                break;
            case Key.Down:
                if (_isRailFocused) MoveRailSelection(1);
                else MoveZone(1);
                break;
            case Key.Enter:
                if (_isRailFocused) ActivateRailSelection();
                else ActivateDashboardSelection();
                break;
            case Key.B when _isRailFocused:
                SetRailFocused(false);
                break;
            case Key.Tab:
                Guide.Toggle();
                break;
            case Key.O when Guide.IsOpen:
                Guide.ToggleOverlay();
                break;
        }
    }

    /// <summary>Clicking a cover tile focuses that game (the hero swaps to it) and opens its details.</summary>
    private void GameTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameTileViewModel game })
        {
            return;
        }

        _viewModel.FocusGame(game);
        _viewModel.OpenFocusedDetails();
    }

    private void HeroPlayButton_Click(object sender, RoutedEventArgs e) => PlayFocusedGame();

    private void HeroDetailsButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenFocusedDetails();

    /// <summary>
    /// Opens the guide menu on top of the browser, or closes it again.
    ///
    /// The web view has to be hidden while it is up: WebView2 renders into its own child
    /// window which paints over WPF content regardless of ZIndex, so a menu drawn above
    /// it would simply be invisible. The page keeps running underneath — audio included,
    /// which is deliberate: pausing a video because someone opened a menu would be worse.
    /// </summary>
    private async void ToggleGuideOverBrowser()
    {
        if (Guide.IsOpen)
        {
            // Guide.Closed restores the page — no need to do it here as well.
            Guide.Close();
            return;
        }

        // Ask BEFORE opening, so the menu appears complete rather than growing an extra
        // row a moment later and shifting what the highlight is sitting on.
        var granted = await Browser.GetGrantedPermissionOriginsAsync();

        Guide.SetPermissionActionVisible(
            granted.Count > 0,
            granted.Count > 0 ? "Microphone: " + string.Join(", ", granted) : string.Empty);

        Browser.SetPageVisible(false);
        Guide.SetBrowserActionsVisible(true);
        Guide.Open();
    }

    private void BrowserRailButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleApp.BrowserAndYouTubeDisabled) return;
        Browser.Show();
    }

    private void UpdatesRailButton_Click(object sender, RoutedEventArgs e)
    {
        Updates.Show();
    }

    private void SettingsRailButton_Click(object sender, RoutedEventArgs e)
    {
        Settings.Show();
    }

}
