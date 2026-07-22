using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using InputDaemon;

namespace UI;

/// <summary>The wizard's steps, in order. Pairing will slot in after Controller.</summary>
public enum SetupStep
{
    Language,
    Network,
    Controller,
    Pin,
}

/// <summary>
/// First-run setup: language, network, controller, parent PIN. Shown instead of the
/// dashboard when the console has never been configured, then handed off with a
/// scatter-and-fade into the home screen.
///
/// PAIRING IS DELIBERATELY ABSENT. The design has a QR-code step between Controller
/// and PIN that links the console to a parent account on the web portal, but that
/// portal does not exist yet — so rather than show a QR code that pairs with nothing,
/// the step is left out entirely. Everything here is built to accommodate it: the
/// steps are an enum walked by index, so inserting Pairing and bumping the "STEP n OF
/// 4" labels is the whole change.
///
/// Navigation is controller-first (D-pad moves, A selects, B goes back), with the
/// keyboard mirroring it for desk testing. The gamepad is polled here rather than
/// reusing the dashboard's poller because that one is wired to dashboard actions and
/// isn't running yet during setup.
/// </summary>
public partial class SetupWizard : UserControl
{
    /// <summary>Digits in the parent PIN.</summary>
    private const int PinLength = 4;

    private readonly INetworkManager _network;
    private readonly IGamepadReader _gamepad = new XInputGamepadReader();
    private readonly System.Windows.Threading.DispatcherTimer _inputTimer =
        new() { Interval = TimeSpan.FromMilliseconds(90) };

    public ObservableCollection<LanguageRowViewModel> Languages { get; } = new();
    public ObservableCollection<NetworkRowViewModel> Networks { get; } = new();
    public ObservableCollection<ControllerRowViewModel> Controllers { get; } = new();
    public ObservableCollection<PinKeyViewModel> PinKeys { get; } = new();

    private SetupStep _step = SetupStep.Language;
    private int _focusIndex;
    private readonly List<Border> _dots = new();
    private readonly List<Border> _pinBoxes = new();
    private string _pin = string.Empty;
    private bool _busy;

    /// <summary>True while the "console is ready" prompt is waiting to be dismissed.</summary>
    private bool _awaitingReadyPress;

    // Edge detection: the poller runs at ~11Hz, so without tracking the previous
    // frame a single button press would register as a dozen.
    private GamepadSnapshot _previousPad;

    /// <summary>XInput's documented left-stick deadzone constant, same value the dashboard poller uses.</summary>
    private const short StickDeadZone = 7849;

    /// <summary>How long a held stick waits before repeating, so it steps rather than races.</summary>
    private static readonly TimeSpan StickRepeatDelay = TimeSpan.FromMilliseconds(220);

    private int _lastStickX;
    private int _lastStickY;
    private DateTime _lastStickMoveUtc = DateTime.MinValue;

    /// <summary>Raised once setup is complete and the dashboard should take over.</summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Raised the moment the exit animation STARTS, well before Completed.
    ///
    /// The dashboard has to begin arriving while this screen is still fading, not
    /// after: the ambient video is a sibling of the dashboard content rather than a
    /// child, so as soon as the wizard turns transparent the video is exposed. Waiting
    /// for Completed left a gap where the video played with no UI over it.
    /// </summary>
    public event EventHandler? Exiting;

    /// <summary>
    /// True from the moment setup starts finishing until the dashboard has taken over.
    ///
    /// Visibility alone is not enough to gate input against: this control stays Visible
    /// for about a second while the exit animation runs, and the dashboard's own gamepad
    /// poller is live the whole time. The A press that entered the final PIN digit was
    /// therefore still held on the next poll and launched whatever game had focus.
    /// </summary>
    public bool IsFinishing { get; private set; }

    /// <summary>
    /// The PIN the user set, hashed. Never stored or exposed in plaintext — the
    /// parental-controls work will persist this.
    /// </summary>
    public string? ParentPinHash { get; private set; }

    public SetupWizard() : this(new SimulatedNetworkManager())
    {
    }

    public SetupWizard(INetworkManager network)
    {
        InitializeComponent();
        _network = network;

        Languages.Add(new LanguageRowViewModel { Label = "English (UK)", IsSelected = true });
        Languages.Add(new LanguageRowViewModel { Label = "English (US)" });
        Languages.Add(new LanguageRowViewModel { Label = "Français" });
        Languages.Add(new LanguageRowViewModel { Label = "Deutsch" });
        LanguageList.ItemsSource = Languages;

        NetworkList.ItemsSource = Networks;

        for (var slot = 0; slot < 4; slot++)
        {
            Controllers.Add(new ControllerRowViewModel { SlotNumber = slot });
        }
        ControllerList.ItemsSource = Controllers;

        // Phone layout: 1-9 across three rows, then a blank, 0, and delete. Deliberately
        // NOT 0-9 in order — every phone and every ATM puts 1 at the top left, and a
        // keypad that doesn't is immediately wrong to use.
        for (var d = 1; d <= 9; d++)
        {
            PinKeys.Add(new PinKeyViewModel { Label = d.ToString(), Digit = d });
        }

        PinKeys.Add(new PinKeyViewModel { Label = string.Empty, Digit = null, IsSpacer = true });
        PinKeys.Add(new PinKeyViewModel { Label = "0", Digit = 0 });
        PinKeys.Add(new PinKeyViewModel { Label = "⌫", Digit = null });
        PinPad.ItemsSource = PinKeys;

        BuildDots();
        BuildPinBoxes();

        _inputTimer.Tick += (_, _) => PollInput();
    }

    /// <summary>
    /// Raises the wizard.
    /// </summary>
    /// <param name="fadeIn">
    /// False when the boot screen's aperture is about to reveal this — the same rule
    /// the dashboard follows. Setup sits UNDERNEATH the boot screen at that moment, so
    /// the green square opening is the one and only reveal; fading in as well means the
    /// aperture exposes a half-transparent screen still materialising, which throws
    /// away the effect. True when opening from an already-visible dashboard (F9),
    /// where there is no aperture and a fade is the right transition.
    /// </param>
    public void Show(bool fadeIn = true)
    {
        Visibility = Visibility.Visible;
        _awaitingReadyPress = false;

        // Clear stick state so a stick still held from whatever opened this doesn't
        // count as a fresh deflection on the first poll.
        _lastStickX = 0;
        _lastStickY = 0;
        _lastStickMoveUtc = DateTime.MinValue;

        Shapes.Reset();
        Shapes.Start();

        BeginAnimation(OpacityProperty, null);

        if (fadeIn)
        {
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4)));
        }
        else
        {
            Opacity = 1;
        }

        // When the aperture is the introduction, the first step must be settled
        // before the hole opens — no slide, no fade.
        GoToStep(SetupStep.Language, animate: fadeIn);
        _inputTimer.Start();

        // Deliberately NOT calling Focus() here.
        //
        // MainWindow handles KeyDown for the whole application and forwards keys to
        // this control. Taking keyboard focus onto the UserControl stopped the
        // Window's handler firing at all, so the keyboard did nothing on the wizard
        // while working everywhere else. The gamepad was unaffected because it is
        // polled rather than routed through WPF focus, which is what made the bug
        // look like "controller only by design".
        Window.GetWindow(this)?.Focus();
    }

    /// <summary>
    /// Ends setup. The steps fade out and a brief "getting everything ready" panel
    /// takes their place, then the shapes scatter and the whole screen fades to reveal
    /// the dashboard.
    ///
    /// The closing panel is not decoration: dropping straight from the last PIN digit
    /// into a live dashboard was jarring, and it gives the swallow somewhere to breathe
    /// rather than firing the instant a button is pressed.
    /// </summary>
    private void Finish()
    {
        IsFinishing = true;
        _inputTimer.Stop();

        // Swap the step content for the closing panel. The step dots and button hints
        // go too — there is nothing left to navigate.
        Stage.Visibility = Visibility.Collapsed;
        BottomChrome.Visibility = Visibility.Collapsed;
        ReadyPrompt.Visibility = Visibility.Collapsed;
        ReadyBarFill.Width = 0;
        Closing.Visibility = Visibility.Visible;
        Closing.BeginAnimation(OpacityProperty, null);
        Closing.Opacity = 0;
        Closing.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));

        _ = RunClosingStepsAsync();
    }

    /// <summary>
    /// Walks the finishing work, moving the bar as each step lands.
    ///
    /// These are placeholders in the windowed test build — the real console writes
    /// settings, scans the library and registers controllers here. The caption names
    /// the step actually running rather than sitting on one generic message, so a
    /// slow step looks like a slow step rather than a hang.
    /// </summary>
    private async Task RunClosingStepsAsync()
    {
        var steps = new (string Caption, int DelayMs)[]
        {
            ("Saving your settings…", 700),
            ("Checking your games…", 900),
            ("Preparing your console…", 700),
        };

        for (var i = 0; i < steps.Length; i++)
        {
            ClosingCaption.Text = steps[i].Caption;
            await Task.Delay(steps[i].DelayMs);

            var target = ReadyBarTrack.Width * ((i + 1) / (double)steps.Length);
            ReadyBarFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(target, TimeSpan.FromSeconds(0.4))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
        }

        await Task.Delay(450);
        ShowReadyPrompt();
    }

    /// <summary>
    /// Setup is finished — the console waits for the user rather than dropping them
    /// into the dashboard on a timer. Input is re-armed here (and only here) so the
    /// press that dismisses this can't be the one that finished the PIN.
    /// </summary>
    private void ShowReadyPrompt()
    {
        _awaitingReadyPress = true;

        ClosingTitle.Text = "Your console is ready";
        ClosingCaption.Text = "Everything is set up and good to go.";

        ReadyPrompt.Visibility = Visibility.Visible;
        ReadyPrompt.BeginAnimation(OpacityProperty, null);

        // Gentle pulse so it reads as waiting for you rather than as static text.
        ReadyPrompt.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });

        // Clear whatever was held when setup finished, so a still-depressed button
        // can't immediately dismiss this screen.
        _previousPad = _gamepad.GetState(0);
        _inputTimer.Start();
    }

    /// <summary>Dismisses the ready prompt and starts the hand-off to the dashboard.</summary>
    private void DismissReadyPrompt()
    {
        if (!_awaitingReadyPress)
        {
            return;
        }

        _awaitingReadyPress = false;
        _inputTimer.Stop();
        ReadyPrompt.BeginAnimation(OpacityProperty, null);
        BeginExit();
    }

    /// <summary>
    /// The hand-off proper: shapes accelerate outward and fade, content goes first so
    /// they are briefly alone on screen, then the whole wizard fades to reveal the
    /// dashboard underneath.
    /// </summary>
    private void BeginExit()
    {
        // Tell the dashboard to start arriving NOW, so it is already coming up
        // underneath as this screen fades rather than appearing after it has gone.
        Exiting?.Invoke(this, EventArgs.Empty);

        Shapes.Scatter();

        Closing.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35)));

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.55))
        {
            BeginTime = TimeSpan.FromSeconds(0.45),
        };
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            Shapes.Stop();

            // Reset for a possible replay (F9) — otherwise a second run would open
            // on the closing panel with everything at zero opacity.
            Closing.Visibility = Visibility.Collapsed;
            Closing.BeginAnimation(OpacityProperty, null);
            Closing.Opacity = 1;
            BottomChrome.Visibility = Visibility.Visible;
            Stage.Visibility = Visibility.Visible;
            Stage.BeginAnimation(OpacityProperty, null);
            Stage.Opacity = 1;

            IsFinishing = false;
            Completed?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- step navigation ----------------

    private void GoToStep(SetupStep step, bool animate = true)
    {
        var forward = step > _step;
        _step = step;
        _focusIndex = 0;
        _busy = false;

        StepLanguage.Visibility = step == SetupStep.Language ? Visibility.Visible : Visibility.Collapsed;
        StepNetwork.Visibility = step == SetupStep.Network ? Visibility.Visible : Visibility.Collapsed;
        StepController.Visibility = step == SetupStep.Controller ? Visibility.Visible : Visibility.Collapsed;
        StepPin.Visibility = step == SetupStep.Pin ? Visibility.Visible : Visibility.Collapsed;

        var panel = ActivePanel();
        if (panel is not null)
        {
            if (!animate)
            {
                // First step during a boot reveal: the aperture is what introduces
                // this screen, so the panel must already be settled and opaque behind
                // it. Sliding in here would show a half-transparent panel drifting
                // into place through the opening hole.
                panel.BeginAnimation(OpacityProperty, null);
                panel.Opacity = 1;
                panel.RenderTransform = null;
            }
            else
            {
                // Slide in from the side the step came from, so Back visibly reverses
                // rather than repeating the forward motion.
                var from = forward ? 34 : -34;
                var slide = new TranslateTransform(from, 0);
                panel.RenderTransform = slide;
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.26)));
                slide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(from, 0, TimeSpan.FromSeconds(0.26))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    });
            }
        }

        UpdateDots();
        UpdateHints();
        UpdateNextButton();

        if (step == SetupStep.Network)
        {
            _ = ScanNetworksAsync();
        }

        if (step == SetupStep.Pin)
        {
            _pin = string.Empty;
            UpdatePinBoxes();
            PinStatus.Visibility = Visibility.Collapsed;
        }

        UpdateFocusHighlight();
    }

    private StackPanel? ActivePanel() => _step switch
    {
        SetupStep.Language => StepLanguage,
        SetupStep.Network => StepNetwork,
        SetupStep.Controller => StepController,
        SetupStep.Pin => StepPin,
        _ => null,
    };

    private void Advance()
    {
        if (_step == SetupStep.Pin)
        {
            Finish();
            return;
        }

        GoToStep(_step + 1);
    }

    private void GoBack()
    {
        if (_step == SetupStep.Language)
        {
            return;
        }

        GoToStep(_step - 1);
    }

    // ---------------- focus ----------------

    private int FocusableCount() => _step switch
    {
        SetupStep.Language => Languages.Count,
        SetupStep.Network => Networks.Count,
        SetupStep.Controller => 0,      // nothing to pick; advances when a pad is confirmed
        SetupStep.Pin => PinKeys.Count,
        _ => 0,
    };

    /// <summary>Columns in the PIN keypad — 1-9, then blank / 0 / delete.</summary>
    private const int PinPadColumns = 3;

    /// <summary>
    /// Single entry point for directional input, shared by the D-pad and the left
    /// stick so the two can never drift apart in behaviour.
    ///
    /// The PIN keypad is a 3-wide grid where all four directions mean something —
    /// left/right steps one key, up/down jumps a row. Every other step is a vertical
    /// list, where horizontal input does nothing.
    /// </summary>
    private void Navigate(int dx, int dy)
    {
        if (_step == SetupStep.Pin)
        {
            if (dx != 0) MoveFocus(dx);
            if (dy != 0) MoveFocus(dy * PinPadColumns);
            return;
        }

        if (dy != 0) MoveFocus(dy);
    }

    /// <summary>
    /// Lets the left stick drive navigation exactly as the D-pad does — most people
    /// reach for the stick first, and a menu that only answers to the D-pad feels
    /// broken.
    ///
    /// Mirrors the dashboard's GamepadInputPoller: the same deadzone constant, the
    /// same repeat delay so a held stick steps rather than races, and the same
    /// dominant-axis rule so a diagonal tilt resolves to one clear direction instead
    /// of firing both axes at once.
    /// </summary>
    private void HandleStick(short thumbX, short thumbY)
    {
        var dx = Math.Abs((int)thumbX) > StickDeadZone ? Math.Sign(thumbX) : 0;
        var dy = Math.Abs((int)thumbY) > StickDeadZone ? Math.Sign(thumbY) : 0;

        if (dx == 0 && dy == 0)
        {
            _lastStickX = 0;
            _lastStickY = 0;
            return;
        }

        // Whichever axis is pushed further wins, so a slightly-off tilt doesn't move
        // diagonally.
        if (Math.Abs((int)thumbX) < Math.Abs((int)thumbY))
        {
            dx = 0;
        }
        else
        {
            dy = 0;
        }

        var directionChanged = dx != _lastStickX || dy != _lastStickY;
        var repeatElapsed = DateTime.UtcNow - _lastStickMoveUtc >= StickRepeatDelay;

        if (!directionChanged && !repeatElapsed)
        {
            return;
        }

        _lastStickX = dx;
        _lastStickY = dy;
        _lastStickMoveUtc = DateTime.UtcNow;

        // XInput's Y axis is positive-UP, the opposite of screen coordinates.
        Navigate(dx, -dy);
    }

    private void MoveFocus(int delta)
    {
        var count = FocusableCount();
        if (count == 0)
        {
            return;
        }

        // Clamp rather than wrap: on a list, running off the end and reappearing at
        // the top is disorienting when you can't see the whole list at once.
        var next = Math.Clamp(_focusIndex + delta, 0, count - 1);

        // Focus must never rest on the keypad's blank cell.
        //
        // Vertical moves (delta = ±3) step over it, so pressing Down from 7 continues
        // to 0 rather than stopping on nothing. Horizontal moves do NOT: skipping left
        // from 0 would land on 9, which is a row up — that reads as the highlight
        // jumping rather than moving left. Staying put is the honest response to
        // "there is nothing to your left".
        if (_step == SetupStep.Pin && PinKeys[next].IsSpacer)
        {
            var isVertical = Math.Abs(delta) == PinPadColumns;
            var skipped = isVertical ? next + Math.Sign(delta) : -1;

            next = skipped >= 0 && skipped < count && !PinKeys[skipped].IsSpacer
                ? skipped
                : _focusIndex;
        }

        _focusIndex = next;
        UpdateFocusHighlight();
    }

    private void UpdateFocusHighlight()
    {
        for (var i = 0; i < Languages.Count; i++)
        {
            Languages[i].IsFocused = _step == SetupStep.Language && i == _focusIndex;
        }

        for (var i = 0; i < Networks.Count; i++)
        {
            Networks[i].IsFocused = _step == SetupStep.Network && i == _focusIndex;
        }

        for (var i = 0; i < PinKeys.Count; i++)
        {
            PinKeys[i].IsFocused = _step == SetupStep.Pin && i == _focusIndex;
        }
    }

    private void Confirm()
    {
        if (_busy)
        {
            return;
        }

        switch (_step)
        {
            case SetupStep.Language:
                // Selects only. Advancing is a separate act via Next, so the choice is
                // visible before it is committed to.
                foreach (var l in Languages)
                {
                    l.IsSelected = false;
                }
                Languages[_focusIndex].IsSelected = true;
                break;

            case SetupStep.Network:
                if (Networks.Count > 0)
                {
                    _ = ConnectAsync(Networks[_focusIndex]);
                }
                break;

            case SetupStep.Controller:
                // Always advances. A confirmed pad moves on by itself via the poller;
                // reaching here means the user chose to skip, and refusing to move
                // would strand anyone whose controller isn't connected yet.
                Advance();
                break;

            case SetupStep.Pin:
                // Focus should never rest on the spacer, but guard anyway rather than
                // entering a key that isn't one.
                if (!PinKeys[_focusIndex].IsSpacer)
                {
                    EnterPinKey(PinKeys[_focusIndex]);
                }
                break;
        }
    }

    // ---------------- network ----------------

    private async Task ScanNetworksAsync()
    {
        _busy = true;
        NetworkSubtitle.Text = "Looking for networks nearby…";
        Networks.Clear();

        var found = await _network.ScanAsync();

        Networks.Clear();
        foreach (var n in found)
        {
            Networks.Add(new NetworkRowViewModel { Network = n });
        }

        NetworkSubtitle.Text = "The console needs internet to check for updates.";
        _busy = false;
        _focusIndex = 0;
        UpdateFocusHighlight();
    }

    private async Task ConnectAsync(NetworkRowViewModel row)
    {
        _busy = true;
        NetworkStatus.Visibility = Visibility.Visible;
        NetworkStatus.Foreground = (Brush)FindResource("Theme.AccentPrimaryBrush");
        NetworkStatus.Text = $"Connecting to {row.Ssid}…";

        // A password prompt belongs here for secured networks the console has never
        // joined. The on-screen keyboard it needs isn't built yet, so for now known
        // and open networks connect and the rest report what's missing rather than
        // silently failing.
        string? password = row.Network is { IsSecured: true, IsKnown: false } ? "simulated" : null;

        var ok = await _network.ConnectAsync(row.Network, password);

        if (ok)
        {
            foreach (var n in Networks)
            {
                n.IsConnected = false;
            }

            row.IsConnected = true;
            NetworkStatus.Text = $"Connected to {row.Ssid}";
            _busy = false;

            await Task.Delay(600);
            if (_step == SetupStep.Network)
            {
                Advance();
            }

            return;
        }

        NetworkStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x4A));
        NetworkStatus.Text = $"Couldn't connect to {row.Ssid}. Choose another network.";
        _busy = false;
    }

    // ---------------- PIN ----------------

    private void BuildPinBoxes()
    {
        for (var i = 0; i < PinLength; i++)
        {
            var box = new Border
            {
                Width = 54,
                Height = 54,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x0E, 0x10, 0x16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            _pinBoxes.Add(box);
            PinBoxes.Children.Add(box);
        }
    }

    private void UpdatePinBoxes()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var idle = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));

        for (var i = 0; i < _pinBoxes.Count; i++)
        {
            var filled = i < _pin.Length;
            _pinBoxes[i].BorderBrush = filled ? accent : idle;
            ((TextBlock)_pinBoxes[i].Child).Text = filled ? "•" : string.Empty;
        }
    }

    private void EnterPinKey(PinKeyViewModel key)
    {
        if (key.Digit is null)
        {
            if (_pin.Length > 0)
            {
                _pin = _pin[..^1];
            }
        }
        else if (_pin.Length < PinLength)
        {
            _pin += key.Digit.Value.ToString();
        }

        UpdatePinBoxes();

        if (_pin.Length == PinLength)
        {
            ParentPinHash = HashPin(_pin);
            Advance();
        }
    }

    /// <summary>
    /// Hashes the PIN so it is never held or persisted in plaintext.
    ///
    /// NOTE: a plain SHA-256 over a 4-digit PIN is trivially brute-forced — all
    /// 10,000 possibilities can be enumerated instantly. This is a placeholder for
    /// the wizard's flow, NOT the final scheme. The real one needs a per-console
    /// random salt and a slow KDF (PBKDF2/Argon2), which is part of the parental-
    /// controls work where the storage location is settled too.
    /// </summary>
    private static string HashPin(string pin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));

    // ---------------- chrome ----------------

    private void BuildDots()
    {
        foreach (var _ in Enum.GetValues<SetupStep>())
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
            };

            _dots.Add(dot);
            StepDots.Children.Add(dot);
        }
    }

    private void UpdateDots()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var idle = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));

        for (var i = 0; i < _dots.Count; i++)
        {
            _dots[i].Background = i == (int)_step ? accent : idle;
        }
    }

    private void UpdateHints() =>
        ButtonHints.Text = _step switch
        {
            SetupStep.Language => "A  Select        ▶  Next",

            // Nothing here is selectable — press any button on a pad to confirm it,
            // then move on. Stating the way forward matters: without it, anyone
            // setting up before their controller is connected just sits looking at
            // "Searching…" with no idea the step can be passed.
            SetupStep.Controller => "▶  Skip        B  Back",

            SetupStep.Network => "A  Connect        B  Back",
            SetupStep.Pin => "A  Enter digit        B  Back",
            _ => "A  Select        B  Back",
        };

    // ---------------- input ----------------

    private void PollInput()
    {
        // Waiting on the ready prompt: ANY button dismisses it, so this is checked
        // before the step-specific handling below.
        if (_awaitingReadyPress)
        {
            var readyPad = _gamepad.GetState(0);
            var newlyPressed = readyPad.Buttons & ~_previousPad.Buttons;
            _previousPad = readyPad;

            if (readyPad.IsConnected && newlyPressed != 0)
            {
                DismissReadyPrompt();
            }

            return;
        }

        // Controller step: watch all four slots so plugging a pad in is reflected
        // live, and advance on its own once one is confirmed — asking a kid to press
        // A after they've already pressed a button to confirm is a pointless step.
        if (_step == SetupStep.Controller)
        {
            var anyConfirmed = false;

            foreach (var row in Controllers)
            {
                var snapshot = _gamepad.GetState(row.SlotNumber);

                if (!snapshot.IsConnected)
                {
                    row.State = ControllerSlotState.NotConnected;
                    continue;
                }

                if (row.State == ControllerSlotState.NotConnected)
                {
                    row.State = ControllerSlotState.Connected;
                }

                if (row.State == ControllerSlotState.Connected && snapshot.Buttons != 0)
                {
                    row.State = ControllerSlotState.Confirmed;
                }

                anyConfirmed |= row.IsConfirmed;
            }

            // Deliberately does NOT auto-advance any more. A confirmed pad shows its
            // green "Ready" row and the user presses Next (or Start) when they have
            // seen it — the same select-then-confirm rhythm as every other step.
            // Auto-advancing meant the confirmation you just triggered vanished
            // before you could read it.
        }

        var pad = _gamepad.GetState(0);
        if (!pad.IsConnected)
        {
            _previousPad = pad;
            return;
        }

        // The stick navigates too, with its own deadzone and repeat timing rather
        // than edge detection — it is an analogue axis, not a button.
        HandleStick(pad.LeftThumbX, pad.LeftThumbY);

        // Edge-detected: only act on a button that wasn't held on the previous poll.
        // Without this the ~11Hz poll would register a single press a dozen times.
        var pressed = pad.Buttons & ~_previousPad.Buttons;
        _previousPad = pad;

        if (pressed == 0)
        {
            return;
        }

        // During the controller step ANY button press confirms the pad that sent it,
        // handled above. Swallow the press here so it doesn't also fall through to
        // Confirm() and skip the step the user just satisfied. B still needs to work
        // as Back, and A only reaches Confirm() when no pad was confirmed — which is
        // exactly the "skip for now" case.
        if (_step == SetupStep.Controller)
        {
            if (pressed.HasFlag(XInputButtons.B))
            {
                GoBack();
            }

            return;
        }

        if (pressed.HasFlag(XInputButtons.DPadLeft)) Navigate(-1, 0);
        if (pressed.HasFlag(XInputButtons.DPadRight)) Navigate(1, 0);
        if (pressed.HasFlag(XInputButtons.DPadUp)) Navigate(0, -1);
        if (pressed.HasFlag(XInputButtons.DPadDown)) Navigate(0, 1);

        if (pressed.HasFlag(XInputButtons.A)) Confirm();
        if (pressed.HasFlag(XInputButtons.B)) GoBack();

        // Start advances the step, mirroring the Next button. A separate button from
        // A matters: A now only SELECTS, so without this the pad could pick a
        // language but never move on.
        if (pressed.HasFlag(XInputButtons.Start)) GoNext();
    }

    /// <summary>
    /// Commits the current step and moves on. Bound to the Next button, and to Start
    /// on the controller so the pad has a way to advance that is distinct from
    /// selecting.
    ///
    /// Not every step needs it: Network advances itself once a connection succeeds
    /// (pressing Next after watching it connect would be busywork), and PIN advances
    /// on the fourth digit. Next is disabled on those rather than hidden, so the
    /// layout does not jump around between steps.
    /// </summary>
    private void GoNext()
    {
        if (_busy || !IsNextEnabled)
        {
            return;
        }

        Advance();
    }

    /// <summary>
    /// Whether Next currently does anything. Language always can; Controller can
    /// (it doubles as "skip"); Network and PIN advance on their own.
    /// </summary>
    private bool IsNextEnabled => _step switch
    {
        SetupStep.Language => true,
        SetupStep.Controller => true,
        _ => false,
    };

    /// <summary>Greys Next out on the steps where it has no meaning.</summary>
    private void UpdateNextButton()
    {
        var enabled = IsNextEnabled;

        NextButton.Opacity = enabled ? 1.0 : 0.35;
        NextButton.Cursor = enabled
            ? System.Windows.Input.Cursors.Hand
            : System.Windows.Input.Cursors.Arrow;

        // On the controller step there is nothing to choose, so Next reads as what it
        // actually does there.
        NextButtonText.Text = _step == SetupStep.Controller ? "Skip" : "Next";
    }

    private void NextButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Stop the click also reaching Root_Click, which would skip the controller
        // step a second time.
        e.Handled = true;
        GoNext();
    }

    // ---------------- mouse ----------------
    //
    // The console itself has no mouse — every screen is built around the D-pad and
    // stick. These handlers exist because the windowed test build is driven on a
    // laptop and in a VM, where a controller is not always to hand, and a screen you
    // can see but cannot click is needlessly awkward to test.
    //
    // Each one moves the focus index to the clicked item and then runs the SAME
    // Confirm() path the controller uses, rather than duplicating the action. That
    // way mouse and controller can never drift apart in behaviour.

    private void LanguageRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LanguageRowViewModel row)
        {
            return;
        }

        _focusIndex = Languages.IndexOf(row);
        UpdateFocusHighlight();
        Confirm();
    }

    private void NetworkRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NetworkRowViewModel row)
        {
            return;
        }

        _focusIndex = Networks.IndexOf(row);
        UpdateFocusHighlight();
        Confirm();
    }

    private void PinKey_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PinKeyViewModel key || key.IsSpacer)
        {
            return;
        }

        _focusIndex = PinKeys.IndexOf(key);
        UpdateFocusHighlight();
        Confirm();
    }

    /// <summary>
    /// Any click dismisses the "console is ready" prompt, mirroring "press any
    /// button". Wired on the root so the whole screen is the target rather than
    /// asking someone to hit a specific line of text.
    /// </summary>
    private void Root_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_awaitingReadyPress)
        {
            DismissReadyPrompt();
            return;
        }

        // Nothing else: clicking empty space must NOT advance a step. The Next button
        // is the only way forward now, and a stray click skipping a step was exactly
        // the behaviour this change set out to remove.
    }

    /// <summary>
    /// Keyboard mirror of the controller bindings, for desk testing.
    ///
    /// Called by MainWindow rather than being an OnKeyDown override: the Window
    /// handles key input for the whole application, so a UserControl only sees
    /// keystrokes if it holds keyboard focus — which this never does, because
    /// navigation is driven by the gamepad poller rather than WPF focus.
    /// </summary>
    public void HandleKey(System.Windows.Input.Key key)
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        // Mirrors the gamepad: any key dismisses the ready prompt.
        if (_awaitingReadyPress)
        {
            DismissReadyPrompt();
            return;
        }

        // On the PIN step, typing a number enters it directly — anyone at a keyboard
        // will try this before they try arrow-keying around a keypad. Backspace
        // deletes. The on-screen pad stays for controller use.
        if (_step == SetupStep.Pin)
        {
            var digit = key switch
            {
                >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9
                    => key - System.Windows.Input.Key.D0,
                >= System.Windows.Input.Key.NumPad0 and <= System.Windows.Input.Key.NumPad9
                    => key - System.Windows.Input.Key.NumPad0,
                _ => -1,
            };

            if (digit >= 0)
            {
                EnterPinKey(new PinKeyViewModel { Label = digit.ToString(), Digit = digit });
                return;
            }
        }

        switch (key)
        {
            // On the keypad, up/down jump a row and left/right step one key. On the
            // list steps, up/down step one row.
            case System.Windows.Input.Key.Up when _step == SetupStep.Pin:
                MoveFocus(-PinPadColumns);
                break;

            case System.Windows.Input.Key.Down when _step == SetupStep.Pin:
                MoveFocus(PinPadColumns);
                break;

            case System.Windows.Input.Key.Left when _step == SetupStep.Pin:
                MoveFocus(-1);
                break;

            case System.Windows.Input.Key.Right when _step == SetupStep.Pin:
                MoveFocus(1);
                break;

            case System.Windows.Input.Key.Up:
                MoveFocus(-1);
                break;

            case System.Windows.Input.Key.Down:
                MoveFocus(1);
                break;

            case System.Windows.Input.Key.Enter:
                // Enter selects, matching A on the pad.
                Confirm();
                break;

            // Tab / Right-arrow advance, matching Next and Start. On the PIN step
            // Right is already used to walk the keypad, so it is excluded there by
            // the case above catching it first.
            case System.Windows.Input.Key.Tab:
            case System.Windows.Input.Key.Right:
                GoNext();
                break;

            // On the PIN step Backspace deletes a digit rather than leaving the step —
            // that is what it does everywhere else a PIN is typed. Escape still backs
            // out.
            case System.Windows.Input.Key.Back when _step == SetupStep.Pin:
                EnterPinKey(new PinKeyViewModel { Label = "⌫", Digit = null });
                break;

            case System.Windows.Input.Key.Escape:
            case System.Windows.Input.Key.Back:
                GoBack();
                break;
        }
    }
}
