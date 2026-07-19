namespace InputDaemon;

/// <summary>
/// Detects the Guide+Start hardware fallback chord from plan.md §5.2 and fires once
/// per fresh press (not once per poll while the chord stays held).
/// </summary>
public sealed class EmergencyKillChordDetector
{
    private bool _wasChordActiveLastPoll;

    public event Action? ChordTriggered;

    public void Observe(GamepadSnapshot snapshot)
    {
        var isChordActiveNow = snapshot.IsConnected
            && snapshot.Buttons.HasFlag(XInputButtons.Guide)
            && snapshot.Buttons.HasFlag(XInputButtons.Start);

        if (isChordActiveNow && !_wasChordActiveLastPoll)
        {
            ChordTriggered?.Invoke();
        }

        _wasChordActiveLastPoll = isChordActiveNow;
    }
}
