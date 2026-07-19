namespace HardwareManager;

public readonly record struct GpuState(int CoreClockMhz, int VoltageMv, int PowerLimitPercent, int FanTargetCelsius)
{
    /// <summary>
    /// Exact-match comparison against a desired state. plan.md §7.2 treats mismatch as
    /// "any field differs", not a tolerance band — the reconciliation loop's job is to
    /// notice drift and re-apply, so approximate matching would mask real drift.
    /// </summary>
    public bool Matches(GpuDesiredState desired) =>
        CoreClockMhz == desired.CoreClockMhz &&
        VoltageMv == desired.VoltageMv &&
        PowerLimitPercent == desired.PowerLimitPercent &&
        FanTargetCelsius == desired.FanTargetCelsius;
}

/// <summary>
/// Seam over the ADLX/vendor wrapper library (plan.md §7.2's NativeHardwareAPI).
/// The real implementation is P/Invoke into ADLX and is NOT implemented yet — it
/// requires the actual RX 6600M board to write and validate (see the ADLX spike
/// discussed but not yet performed). This interface lets the reconciliation state
/// machine be built and fully unit-tested against a fake now.
/// </summary>
public interface IGpuHardwareApi
{
    GpuState GetGpuState(string targetGpu);

    /// <summary>Returns false if the driver rejected the requested configuration outright.</summary>
    bool ApplyDesiredState(GpuDesiredState desiredState);

    /// <summary>Current GPU temperature in Celsius, used by the stability watchdog.</summary>
    int GetCurrentTemperatureCelsius();
}
