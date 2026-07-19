using HardwareManager;

namespace HardwareManager.Tests;

public class StabilityWatchdogTests
{
    [Fact]
    public void EvaluateTelemetry_StableWhenAllSamplesBelowThreshold()
    {
        var samples = new[] { 70, 72, 71, 73, 74 };
        Assert.True(StabilityWatchdog.EvaluateTelemetry(samples, maxAllowedCelsius: 85));
    }

    [Fact]
    public void EvaluateTelemetry_UnstableWhenAnySampleExceedsThreshold()
    {
        var samples = new[] { 70, 72, 90, 73, 74 };
        Assert.False(StabilityWatchdog.EvaluateTelemetry(samples, maxAllowedCelsius: 85));
    }

    [Fact]
    public void EvaluateTelemetry_StableWhenSampleExactlyAtThreshold()
    {
        var samples = new[] { 85 };
        Assert.True(StabilityWatchdog.EvaluateTelemetry(samples, maxAllowedCelsius: 85));
    }

    [Fact]
    public void EvaluateTelemetry_UnstableWhenSampleOneAboveThreshold()
    {
        var samples = new[] { 86 };
        Assert.False(StabilityWatchdog.EvaluateTelemetry(samples, maxAllowedCelsius: 85));
    }

    [Fact]
    public void EvaluateTelemetry_StableWhenNoSamplesProvided()
    {
        Assert.True(StabilityWatchdog.EvaluateTelemetry(Array.Empty<int>(), maxAllowedCelsius: 85));
    }

    [Fact]
    public void EvaluateTelemetry_UnstableIfExceedanceHappensLateInWindow()
    {
        var samples = new[] { 70, 70, 70, 70, 95 };
        Assert.False(StabilityWatchdog.EvaluateTelemetry(samples, maxAllowedCelsius: 85));
    }
}
