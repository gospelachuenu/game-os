namespace InputDaemon;

public readonly record struct MouseEmulationOutput(
    bool IsActive,
    int CursorDeltaX,
    int CursorDeltaY,
    bool LeftClick,
    bool RightClick);

/// <summary>
/// Implements plan.md §9.4's Stray Window Navigator: holding LB remaps the left stick
/// to cursor movement and A/B to left/right clicks. Pure translation from a gamepad
/// snapshot to mouse deltas; the actual SendInput/cursor-move calls stay outside this
/// class so the mapping math is testable without touching the real cursor.
/// </summary>
public static class MouseEmulationMapper
{
    private const short DeadZone = 7849; // XInput's documented left-stick deadzone constant
    private const double MaxPixelsPerTick = 18.0;

    public static MouseEmulationOutput Map(GamepadSnapshot snapshot)
    {
        var isLbHeld = snapshot.Buttons.HasFlag(XInputButtons.LeftShoulder);

        if (!isLbHeld)
        {
            return new MouseEmulationOutput(false, 0, 0, false, false);
        }

        var deltaX = ApplyDeadZoneAndScale(snapshot.LeftThumbX);
        var deltaY = ApplyDeadZoneAndScale(snapshot.LeftThumbY);

        return new MouseEmulationOutput(
            IsActive: true,
            CursorDeltaX: deltaX,
            CursorDeltaY: -deltaY, // screen Y grows downward; stick Y grows upward
            LeftClick: snapshot.Buttons.HasFlag(XInputButtons.A),
            RightClick: snapshot.Buttons.HasFlag(XInputButtons.B));
    }

    private static int ApplyDeadZoneAndScale(short axisValue)
    {
        if (Math.Abs((int)axisValue) < DeadZone)
        {
            return 0;
        }

        var normalized = axisValue / 32767.0;
        return (int)Math.Round(normalized * MaxPixelsPerTick);
    }
}
