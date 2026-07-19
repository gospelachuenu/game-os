using HardwareManager;

namespace HardwareManager.Tests;

internal sealed class FakeGpuHardwareApi : IGpuHardwareApi
{
    public GpuState CurrentState { get; set; }
    public bool RejectNextApply { get; set; }
    public GpuDesiredState? LastAppliedState { get; private set; }
    public List<GpuDesiredState> AllAppliedStates { get; } = new();

    public GpuState GetGpuState(string targetGpu) => CurrentState;

    public bool ApplyDesiredState(GpuDesiredState desiredState)
    {
        AllAppliedStates.Add(desiredState);

        if (RejectNextApply)
        {
            RejectNextApply = false;
            return false;
        }

        LastAppliedState = desiredState;
        CurrentState = new GpuState(
            desiredState.CoreClockMhz,
            desiredState.VoltageMv,
            desiredState.PowerLimitPercent,
            desiredState.FanTargetCelsius);

        return true;
    }

    public int GetCurrentTemperatureCelsius() => 70;
}
