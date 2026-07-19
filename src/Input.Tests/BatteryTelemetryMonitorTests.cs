using InputDaemon;

namespace Input.Tests;

public class BatteryTelemetryMonitorTests
{
    [Theory]
    [InlineData(BatteryLevel.Full, BatteryUiState.Full)]
    [InlineData(BatteryLevel.Medium, BatteryUiState.Medium)]
    [InlineData(BatteryLevel.Low, BatteryUiState.Low)]
    [InlineData(BatteryLevel.Empty, BatteryUiState.Critical)]
    public void Classify_MapsAlkalineNimhLevelsToExpectedUiState(BatteryLevel level, BatteryUiState expected)
    {
        var result = BatteryTelemetryMonitor.Classify(new BatterySnapshot(BatteryType.Nimh, level));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_WiredTypeAlwaysMapsToCharging_RegardlessOfLevel()
    {
        var result = BatteryTelemetryMonitor.Classify(new BatterySnapshot(BatteryType.Wired, BatteryLevel.Empty));
        Assert.Equal(BatteryUiState.Charging, result);
    }

    [Fact]
    public void Classify_DisconnectedTypeMapsToUnknown()
    {
        var result = BatteryTelemetryMonitor.Classify(new BatterySnapshot(BatteryType.Disconnected, BatteryLevel.Full));
        Assert.Equal(BatteryUiState.Unknown, result);
    }

    [Fact]
    public void ShouldShowCriticalToast_SuppressedWhenGameIsFocused()
    {
        var result = BatteryTelemetryMonitor.ShouldShowCriticalToast(BatteryUiState.Critical, isGameCurrentlyFocused: true);
        Assert.False(result);
    }

    [Fact]
    public void ShouldShowCriticalToast_ShownWhenGameNotFocused()
    {
        var result = BatteryTelemetryMonitor.ShouldShowCriticalToast(BatteryUiState.Critical, isGameCurrentlyFocused: false);
        Assert.True(result);
    }

    [Theory]
    [InlineData(BatteryUiState.Full)]
    [InlineData(BatteryUiState.Medium)]
    [InlineData(BatteryUiState.Low)]
    [InlineData(BatteryUiState.Charging)]
    public void ShouldShowCriticalToast_NeverTrueForNonCriticalStates(BatteryUiState state)
    {
        var focused = BatteryTelemetryMonitor.ShouldShowCriticalToast(state, isGameCurrentlyFocused: false);
        var unfocused = BatteryTelemetryMonitor.ShouldShowCriticalToast(state, isGameCurrentlyFocused: true);

        Assert.False(focused);
        Assert.False(unfocused);
    }
}
