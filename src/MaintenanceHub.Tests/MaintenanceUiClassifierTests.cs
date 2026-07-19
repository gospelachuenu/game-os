using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class MaintenanceUiClassifierTests
{
    [Fact]
    public void Classify_WindowsOsUpdate_UsesLinearPercentWithNoWarning()
    {
        var state = MaintenanceUiClassifier.Classify(UpdateType.WindowsOsUpdate);

        Assert.Equal(ProgressDisplayMode.LinearPercent, state.DisplayMode);
        Assert.Null(state.WarningText);
    }

    [Fact]
    public void Classify_AmdDriverUpdate_UsesIndeterminateWithMandatoryWarning()
    {
        var state = MaintenanceUiClassifier.Classify(UpdateType.AmdDriverUpdate);

        Assert.Equal(ProgressDisplayMode.Indeterminate, state.DisplayMode);
        Assert.Equal(MaintenanceUiClassifier.AmdDriverWarningText, state.WarningText);
    }

    [Fact]
    public void Classify_AmdDriverWarning_MentionsFlickerBlackScreenAndAudio()
    {
        var state = MaintenanceUiClassifier.Classify(UpdateType.AmdDriverUpdate);

        Assert.Contains("flicker", state.WarningText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("black", state.WarningText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audio", state.WarningText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not interrupt", state.WarningText);
    }

    [Fact]
    public void Classify_ConsoleSoftwarePatch_UsesHighSpeedProcessingBarWithNoWarning()
    {
        var state = MaintenanceUiClassifier.Classify(UpdateType.ConsoleSoftwarePatch);

        Assert.Equal(ProgressDisplayMode.HighSpeedProcessingBar, state.DisplayMode);
        Assert.Null(state.WarningText);
    }

    [Fact]
    public void Classify_ThrowsOnUnknownUpdateType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaintenanceUiClassifier.Classify((UpdateType)99));
    }

    [Theory]
    [InlineData(UpdateType.WindowsOsUpdate, 1)]
    [InlineData(UpdateType.AmdDriverUpdate, 2)]
    [InlineData(UpdateType.ConsoleSoftwarePatch, 3)]
    public void UpdateType_BackingIntegerMatchesPlanMdStateNumbers(UpdateType type, int expectedInt)
    {
        Assert.Equal(expectedInt, (int)type);
    }
}
