namespace CecController;

/// <summary>
/// Mirrors the subset of Microsoft.Win32.PowerModeChanged / S3 wake-vs-sleep
/// transitions plan.md §8.3 hooks into, decoupled from the actual
/// SystemEvents.PowerModeChanged event args so the mapping logic is testable
/// without a real OS power-state transition.
/// </summary>
public enum PowerStateTransition
{
    ResumeFromSleep,
    EnteringSuspend,
}
