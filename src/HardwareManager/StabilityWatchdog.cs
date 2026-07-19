namespace HardwareManager;

/// <summary>
/// Implements plan.md §7.2's post-injection stability check: monitor temperature over
/// a window and trip if it ever exceeds the safety threshold. The real caller samples
/// on a wall-clock cadence (e.g. once/second for 30s); this class takes a sequence of
/// already-collected samples so the pass/fail decision is a pure function and testable
/// without a real 30-second wait.
/// </summary>
public static class StabilityWatchdog
{
    public static bool EvaluateTelemetry(IEnumerable<int> temperatureSamplesCelsius, int maxAllowedCelsius)
    {
        foreach (var sample in temperatureSamplesCelsius)
        {
            if (sample > maxAllowedCelsius)
            {
                return false;
            }
        }

        return true;
    }
}
