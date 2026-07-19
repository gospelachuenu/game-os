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

    private XInputButtons _previousButtons;
    private DateTime _lastStickMoveUtc = DateTime.MinValue;
    private int _lastStickDirectionX;
    private int _lastStickDirectionY;

    public event Action? MoveUp;
    public event Action? MoveDown;
    public event Action? MoveLeft;
    public event Action? MoveRight;
    public event Action? Confirm;
    public event Action? Back;
    public event Action? ToggleGuideMenu;

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
        var snapshot = _reader.GetState(userIndex: 0);
        if (!snapshot.IsConnected)
        {
            _previousButtons = 0;
            return;
        }

        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadUp, MoveUp);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadDown, MoveDown);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadLeft, MoveLeft);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.DPadRight, MoveRight);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.A, Confirm);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.B, Back);

        // Guide button is frequently intercepted by the OS/Xbox app before it ever
        // reaches a foreground application via XInput, so Start is the reliable
        // fallback for opening the guide menu on physical hardware in this test build.
        HandleButtonEdge(snapshot.Buttons, XInputButtons.Guide, ToggleGuideMenu);
        HandleButtonEdge(snapshot.Buttons, XInputButtons.Start, ToggleGuideMenu);

        HandleStickNavigation(snapshot.LeftThumbX, snapshot.LeftThumbY);

        _previousButtons = snapshot.Buttons;
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
