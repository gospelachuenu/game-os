using InputDaemon;

namespace Input.Tests;

public class SleepAutomationTests
{
    [Fact]
    public void Evaluate_ReturnsNoneWhenBelowDimThreshold()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.FromMinutes(9));
        Assert.Equal(SleepAutomationAction.None, result);
    }

    [Fact]
    public void Evaluate_ReturnsDimAtExactlyTenMinutes()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.FromMinutes(10));
        Assert.Equal(SleepAutomationAction.DimDisplay, result);
    }

    [Fact]
    public void Evaluate_ReturnsDimBetweenTenAndTwentyMinutes()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.FromMinutes(15));
        Assert.Equal(SleepAutomationAction.DimDisplay, result);
    }

    [Fact]
    public void Evaluate_ReturnsSuspendAtExactlyTwentyMinutes()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.FromMinutes(20));
        Assert.Equal(SleepAutomationAction.EnterSuspend, result);
    }

    [Fact]
    public void Evaluate_ReturnsSuspendWellBeyondTwentyMinutes()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.FromHours(2));
        Assert.Equal(SleepAutomationAction.EnterSuspend, result);
    }

    [Fact]
    public void Evaluate_ReturnsNoneForZeroIdleTime()
    {
        var result = SleepAutomation.Evaluate(TimeSpan.Zero);
        Assert.Equal(SleepAutomationAction.None, result);
    }
}
