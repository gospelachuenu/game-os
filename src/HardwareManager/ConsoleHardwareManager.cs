namespace HardwareManager;

public enum ReconciliationOutcome
{
    AlreadyMatched,
    LockedAfterStableInjection,
    RolledBackAfterInstability,
    RolledBackAfterDriverRejection,
}

/// <summary>
/// Implements plan.md §7.2's ConsoleHardwareManager: reconcile actual GPU state
/// against a declarative desired-state policy, verify stability post-injection, and
/// fall back to stock settings on rejection or instability.
/// </summary>
public sealed class ConsoleHardwareManager
{
    private readonly IGpuHardwareApi _hardwareApi;
    private readonly Func<GpuDesiredState, IEnumerable<int>> _sampleTemperatureWindow;
    private readonly Action<string> _log;

    public ConsoleHardwareManager(
        IGpuHardwareApi hardwareApi,
        Func<GpuDesiredState, IEnumerable<int>> sampleTemperatureWindow,
        Action<string>? log = null)
    {
        _hardwareApi = hardwareApi;
        _sampleTemperatureWindow = sampleTemperatureWindow;
        _log = log ?? (_ => { });
    }

    public ReconciliationOutcome Reconcile(GpuProfile targetProfile, GpuProfile stockProfile)
    {
        var currentState = _hardwareApi.GetGpuState(targetProfile.TargetGpu);

        if (currentState.Matches(targetProfile.DesiredState))
        {
            return ReconciliationOutcome.AlreadyMatched;
        }

        _log("Hardware mismatch caught. Attempting state reconciliation...");
        var injectionAccepted = _hardwareApi.ApplyDesiredState(targetProfile.DesiredState);

        if (!injectionAccepted)
        {
            _log("Driver rejected profile configuration. Enforcing safe factory defaults.");
            _hardwareApi.ApplyDesiredState(stockProfile.DesiredState);
            return ReconciliationOutcome.RolledBackAfterDriverRejection;
        }

        var samples = _sampleTemperatureWindow(targetProfile.DesiredState);
        var isStable = StabilityWatchdog.EvaluateTelemetry(samples, targetProfile.SafetyThresholds.MaxAllowedCelsius);

        if (isStable)
        {
            _log($"Desired State [{targetProfile.ProfileName}] successfully locked.");
            return ReconciliationOutcome.LockedAfterStableInjection;
        }

        _log("Instability caught during verification loop! Tripping rollback system.");
        _hardwareApi.ApplyDesiredState(stockProfile.DesiredState);
        return ReconciliationOutcome.RolledBackAfterInstability;
    }
}
