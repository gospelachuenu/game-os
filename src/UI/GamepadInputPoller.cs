using System.Windows.Threading;
using InputDaemon;

namespace UI;

/// <summary>
/// Polls a real physical controller via the existing Input project's XInputGamepadReader
/// (raw Windows XInput) and translates D-pad / left-stick / face-button presses into
/// the same navigation actions the keyboard already drives. XInput itself doesn't
/// distinguish controller brand — a real Xbox controller works directly; a
/// PlayStation DualShock/DualSense controller works too AS LONG AS something on the
/// system (Steam running in the background, DS4Windows, etc.) is translating it into
/// an XInput device, which Windows does not do natively for PlayStation pads. No
/// separate DirectInput/HID code path was added — this seam handles both
/// transparently once a controller is recognized as an XInput device.
///
/// Buttons are edge-detected (fires once per fresh press, not once per poll while
/// held) the same way EmergencyKillChordDetector already does, reusing that pattern
/// rather than re-inventing it.
/// </summary>
public sealed class GamepadInputPoller
{
    private const int PollIntervalMs = 33; // ~30Hz — plenty responsive for menu navigation
    private const short StickDeadZone = 7849; // XInput's documented left-stick deadzone constant
    private static readonly TimeSpan StickRepeatDelay = TimeSpan.FromMilliseconds(220);

    private readonly IGamepadReader _reader;
    private readonly DispatcherTimer _timer;

    /// <summary>XInput supports four controller slots; a pad is not necessarily on 0.</summary>
    private const int MaxControllerSlots = 4;

    private XInputButtons _previousButtons;
    private DateTime _lastStickMoveUtc = DateTime.MinValue;
    private int _lastStickDirectionX;
    private int _lastStickDirectionY;

    /// <summary>
    /// Which XInput slot the live controller is on.
    ///
    /// Polling slot 0 alone is wrong and fails SILENTLY: Windows assigns a slot on
    /// connection and a pad routinely lands on 1, 2 or 3 — after a reconnect, or when
    /// something else (Steam's virtual pad, a wireless dongle) already holds 0. When
    /// that happened every poll saw "not connected" and returned, so no controller
    /// input reached the app at all and nothing indicated why.
    /// </summary>
    private int _activeSlot;

    /// <summary>True when a controller was found on some slot at the last poll.</summary>
    public bool IsControllerConnected { get; private set; }

    /// <summary>The slot the live controller is on, or -1 when none is connected.</summary>
    public int ActiveSlot => IsControllerConnected ? _activeSlot : -1;

    public event Action? MoveUp;
    public event Action? MoveDown;
    public event Action? MoveLeft;
    public event Action? MoveRight;
    public event Action? Confirm;
    public event Action? Back;
    public event Action? ToggleGuideMenu;

    /// <summary>
    /// B held down rather than tapped.
    ///
    /// Exists because on a key-navigated page (YouTube's TV interface) a tap of B belongs
    /// to the PAGE — it closes a video or leaves a menu, which is what a user expects —
    /// leaving no button free to exit the browser itself. Holding is the console
    /// convention for "I mean the system, not what is on screen".
    ///
    /// Fires once per hold; the subsequent release does NOT raise <see cref="Back"/>, so
    /// holding never also triggers the tap action.
    /// </summary>
    public event Action? BackHeld;

    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(650);

    private DateTime _backPressedAtUtc;
    private bool _backHoldFired;

    /// <summary>
    /// The left stick's position each poll, normalised to -1..1 with the deadzone
    /// applied, plus seconds since the previous poll.
    ///
    /// Separate from the discrete Move* events because a POINTER needs continuous
    /// motion — the stepped, repeat-delayed navigation those events provide is right
    /// for hopping between tiles and useless for driving a cursor. Arguments are
    /// (x, y, elapsedSeconds).
    /// </summary>
    public event Action<double, double, double>? StickMoved;

    private DateTime _lastStickSampleUtc = DateTime.UtcNow;

    public GamepadInputPoller(IGamepadReader reader)
    {
        _reader = reader;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        var snapshot = ReadActiveController();
        if (!snapshot.IsConnected)
        {
            _previousButtons = 0;
            IsControllerConnected = false;
            return;
        }

        IsControllerConnected = true;

        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadUp, MoveUp);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadDown, MoveDown);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadLeft, MoveLeft);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadRight, MoveRight);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.A, Confirm);
        HandleBackButton(snapshot.Buttons);

        // Guide button is frequently intercepted by the OS/Xbox app before it ever
        // reaches a foreground application via XInput, so Start is the reliable
        // fallback for opening the guide menu on physical hardware in this test build.
        HandleButtonEdge(snapshot.Buttons, XInputButtons.Guide, ToggleGuideMenu);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.Start, ToggleGuideMenu);

        HandleStickNavigation(snapshot.LeftThumbX, snapshot.LeftThumbY);
        PublishRawStick(snapshot.LeftThumbX, snapshot.LeftThumbY);

        _previousButtons = snapshot.Buttons;
    }

    /// <summary>
    /// Reads whichever slot the controller is actually on.
    ///
    /// Sticks with the last known slot while it stays connected — that is the common
    /// case and costs one call. Only when it goes quiet does this sweep the other three,
    /// so a pad that reconnects onto a different slot is picked up automatically rather
    /// than leaving the console unresponsive until it is restarted.
    /// </summary>
    private GamepadSnapshot ReadActiveController()
    {
        var current = _reader.GetState(_activeSlot);
        if (current.IsConnected)
        {
            return current;
        }

        for (var slot = 0; slot < MaxControllerSlots; slot++)
        {
            if (slot == _activeSlot)
            {
                continue;
            }

            var candidate = _reader.GetState(slot);
            if (candidate.IsConnected)
            {
                // Moving slots means the previous pad's held buttons are meaningless;
                // clearing avoids a phantom "press" the first time the new one reports.
                _previousButtons = 0;
                _activeSlot = slot;
                return candidate;
            }
        }

        return current;
    }

    /// <summary>
    /// Publishes the stick as a continuous value for anything that needs smooth motion
    /// rather than steps. Deadzone applied here so subscribers get a clean zero at rest
    /// and do not each have to reimplement it.
    /// </summary>
    private void PublishRawStick(short thumbX, short thumbY)
    {
        if (StickMoved is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastStickSampleUtc).TotalSeconds;
        _lastStickSampleUtc = now;

        // Clamp the step: if the app was stalled or the poller paused, a huge elapsed
        // value would fling the cursor across the screen in one jump.
        elapsed = Math.Min(elapsed, 0.1);

        var x = Math.Abs((int)thumbX) > StickDeadZone ? thumbX / 32767.0 : 0;

        // XInput's Y is positive-UP, the opposite of screen coordinates.
        var y = Math.Abs((int)thumbY) > StickDeadZone ? -thumbY / 32767.0 : 0;

        if (x != 0 || y != 0)
        {
            StickMoved.Invoke(x, y, elapsed);
        }
    }

    /// <summary>
    /// Distinguishes a tap of B from a hold.
    ///
    /// The tap fires on RELEASE rather than on press: until the button comes up there is
    /// no way to know which it was, and firing on press would send the tap action and then
    /// the hold action for a single gesture.
    /// </summary>
    private void HandleBackButton(XInputButtons current)
    {
        var isDownNow = current.HasFlag(XInputButtons.B);
        var wasDownBefore = _previousButtons.HasFlag(XInputButtons.B);

        if (isDownNow && !wasDownBefore)
        {
            _backPressedAtUtc = DateTime.UtcNow;
            _backHoldFired = false;
            return;
        }

        if (isDownNow)
        {
            // Fires the moment the threshold passes, while still held — waiting for
            // release would make a deliberate hold feel unresponsive.
            if (!_backHoldFired && DateTime.UtcNow - _backPressedAtUtc >= HoldDuration)
            {
                _backHoldFired = true;
                BackHeld?.Invoke();
            }

            return;
        }

        if (wasDownBefore && !_backHoldFired)
        {
            Back?.Invoke();
        }
    }

    private void HandleButtonEdge(XInputButtons current, XInputButtons flag, Action? handler)
    {
        var isDownNow = current.HasFlag(flag);
        var wasDownBefore = _previousButtons.HasFlag(flag);

        if (isDownNow && !wasDownBefore)
        {
            handler?.Invoke();
        }
    }

    /// <summary>
    /// The left stick drives the same Up/Down/Left/Right actions as the D-pad, with
    /// a deadzone plus a repeat delay so holding the stick over in one direction
    /// repeatedly navigates instead of firing once and then requiring the stick to
    /// re-center and re-tilt for every step.
    /// </summary>
    private void HandleStickNavigation(short stickX, short stickY)
    {
        var directionX = Math.Abs((int)stickX) > StickDeadZone ? Math.Sign(stickX) : 0;
        var directionY = Math.Abs((int)stickY) > StickDeadZone ? Math.Sign(stickY) : 0;

        if (directionX == 0 && directionY == 0)
        {
            _lastStickDirectionX = 0;
            _lastStickDirectionY = 0;
            return;
        }

        // Prioritize whichever axis has the larger deflection so a diagonal-ish tilt
        // reads as one clear direction instead of firing both axes at once.
        if (Math.Abs((int)stickX) < Math.Abs((int)stickY))
        {
            directionX = 0;
        }
        else
        {
            directionY = 0;
        }

        var directionChanged = directionX != _lastStickDirectionX || directionY != _lastStickDirectionY;
        var repeatDelayElapsed = DateTime.UtcNow - _lastStickMoveUtc >= StickRepeatDelay;

        if (!directionChanged && !repeatDelayElapsed)
        {
            return;
        }

        _lastStickDirectionX = directionX;
        _lastStickDirectionY = directionY;
        _lastStickMoveUtc = DateTime.UtcNow;

        if (directionY > 0)
        {
            MoveUp?.Invoke(); // XInput's Y axis is positive-up
        }
        else if (directionY < 0)
        {
            MoveDown?.Invoke();
        }
        else if (directionX < 0)
        {
            MoveLeft?.Invoke();
        }
        else if (directionX > 0)
        {
            MoveRight?.Invoke();
        }
    }
}
