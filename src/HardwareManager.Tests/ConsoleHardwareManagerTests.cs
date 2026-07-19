using HardwareManager;

namespace HardwareManager.Tests;

public class ConsoleHardwareManagerTests
{
    private static GpuDesiredState Desired(int coreClock = 2300, int voltage = 950, int power = -10, int fan = 75) => new()
    {
        CoreClockMhz = coreClock,
        VoltageMv = voltage,
        PowerLimitPercent = power,
        FanTargetCelsius = fan,
    };

    private static GpuProfile MakeProfile(string name, GpuDesiredState desired, int maxCelsius = 85) => new()
    {
        ProfileName = name,
        TargetGpu = "AMD Radeon RX 6600M",
        DesiredState = desired,
        SafetyThresholds = new GpuSafetyThresholds { MaxAllowedCelsius = maxCelsius, MinAllowedVoltageMv = 900 },
    };

    [Fact]
    public void Reconcile_ReturnsAlreadyMatchedWhenCurrentStateMatchesDesired()
    {
        var api = new FakeGpuHardwareApi { CurrentState = new GpuState(2300, 950, -10, 75) };
        var manager = new ConsoleHardwareManager(api, _ => new[] { 70 });

        var target = MakeProfile("Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        var outcome = manager.Reconcile(target, stock);

        Assert.Equal(ReconciliationOutcome.AlreadyMatched, outcome);
        Assert.Empty(api.AllAppliedStates);
    }

    [Fact]
    public void Reconcile_LocksInWhenInjectionAcceptedAndTelemetryStable()
    {
        var api = new FakeGpuHardwareApi { CurrentState = new GpuState(2000, 1000, 0, 60) };
        var manager = new ConsoleHardwareManager(api, _ => new[] { 70, 72, 74 });

        var target = MakeProfile("Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        var outcome = manager.Reconcile(target, stock);

        Assert.Equal(ReconciliationOutcome.LockedAfterStableInjection, outcome);
        Assert.Single(api.AllAppliedStates);
        Assert.Equal(2300, api.CurrentState.CoreClockMhz);
    }

    [Fact]
    public void Reconcile_RollsBackToStockWhenDriverRejectsInjection()
    {
        var api = new FakeGpuHardwareApi
        {
            CurrentState = new GpuState(2000, 1000, 0, 60),
            RejectNextApply = true,
        };
        var manager = new ConsoleHardwareManager(api, _ => new[] { 70 });

        var target = MakeProfile("Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        var outcome = manager.Reconcile(target, stock);

        Assert.Equal(ReconciliationOutcome.RolledBackAfterDriverRejection, outcome);
        Assert.Equal(2000, api.CurrentState.CoreClockMhz);
    }

    [Fact]
    public void Reconcile_RollsBackToStockWhenTelemetryUnstableAfterAcceptedInjection()
    {
        var api = new FakeGpuHardwareApi { CurrentState = new GpuState(2000, 1000, 0, 60) };
        var manager = new ConsoleHardwareManager(api, _ => new[] { 70, 90, 72 }); // 90 exceeds 85 threshold

        var target = MakeProfile("Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        var outcome = manager.Reconcile(target, stock);

        Assert.Equal(ReconciliationOutcome.RolledBackAfterInstability, outcome);
        Assert.Equal(2000, api.CurrentState.CoreClockMhz);
        Assert.Equal(2, api.AllAppliedStates.Count); // attempted desired, then rolled back to stock
    }

    [Fact]
    public void Reconcile_PassesTargetDesiredStateToTemperatureSampler()
    {
        var api = new FakeGpuHardwareApi { CurrentState = new GpuState(2000, 1000, 0, 60) };
        GpuDesiredState? seenByStamper = null;
        var manager = new ConsoleHardwareManager(api, desired =>
        {
            seenByStamper = desired;
            return new[] { 70 };
        });

        var target = MakeProfile("Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        manager.Reconcile(target, stock);

        Assert.NotNull(seenByStamper);
        Assert.Equal(2300, seenByStamper!.CoreClockMhz);
    }

    [Fact]
    public void Reconcile_LogsExpectedMessagesInOrder()
    {
        var messages = new List<string>();
        var api = new FakeGpuHardwareApi { CurrentState = new GpuState(2000, 1000, 0, 60) };
        var manager = new ConsoleHardwareManager(api, _ => new[] { 70 }, log: messages.Add);

        var target = MakeProfile("RX6600M_Balanced", Desired());
        var stock = MakeProfile("Stock", Desired(2000, 1000, 0, 60));

        manager.Reconcile(target, stock);

        Assert.Contains(messages, m => m.Contains("mismatch"));
        Assert.Contains(messages, m => m.Contains("successfully locked"));
    }
}
