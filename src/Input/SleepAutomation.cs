namespace InputDaemon;

public enum SleepAutomationAction
{
    None,
    DimDisplay,
    EnterSuspend,
}

/// <summary>
/// Implements plan.md §5.5's inactivity ladder: dim at 10 minutes idle, suspend at 20.
/// Pure function of elapsed idle time so it can be driven by a fake clock in tests
/// rather than a real Timer/XInput poll loop.
/// </summary>
public static class SleepAutomation
{
    public static readonly TimeSpan DimThreshold = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SuspendThreshold = TimeSpan.FromMinutes(20);

    public static SleepAutomationAction Evaluate(TimeSpan idleDuration)
    {
        if (idleDuration >= SuspendThreshold)
        {
            return SleepAutomationAction.EnterSuspend;
        }

        if (idleDuration >= DimThreshold)
        {
            return SleepAutomationAction.DimDisplay;
        }

        return SleepAutomationAction.None;
    }
}
