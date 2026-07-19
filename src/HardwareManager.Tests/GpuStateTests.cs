using HardwareManager;

namespace HardwareManager.Tests;

public class GpuStateTests
{
    private static GpuDesiredState MakeDesired() => new()
    {
        CoreClockMhz = 2300,
        VoltageMv = 950,
        PowerLimitPercent = -10,
        FanTargetCelsius = 75,
    };

    [Fact]
    public void Matches_TrueWhenAllFieldsIdentical()
    {
        var state = new GpuState(2300, 950, -10, 75);
        Assert.True(state.Matches(MakeDesired()));
    }

    [Fact]
    public void Matches_FalseWhenCoreClockDiffers()
    {
        var state = new GpuState(2200, 950, -10, 75);
        Assert.False(state.Matches(MakeDesired()));
    }

    [Fact]
    public void Matches_FalseWhenVoltageDiffersEvenSlightly()
    {
        var state = new GpuState(2300, 949, -10, 75);
        Assert.False(state.Matches(MakeDesired()));
    }

    [Fact]
    public void Matches_FalseWhenPowerLimitDiffers()
    {
        var state = new GpuState(2300, 950, -5, 75);
        Assert.False(state.Matches(MakeDesired()));
    }

    [Fact]
    public void Matches_FalseWhenFanTargetDiffers()
    {
        var state = new GpuState(2300, 950, -10, 70);
        Assert.False(state.Matches(MakeDesired()));
    }
}
