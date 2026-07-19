namespace ConsoleLibrary.Tests;

public class GhostTileProgressTests
{
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(500, 1000, 50)]
    [InlineData(1000, 1000, 100)]
    [InlineData(250, 1000, 25)]
    public void CalculatePercent_ComputesExpectedRatio(long current, long total, double expected)
    {
        var result = GhostTileProgress.CalculatePercent(current, total);
        Assert.Equal(expected, result, precision: 5);
    }

    [Fact]
    public void CalculatePercent_ClampsAboveOneHundredWhenStagingExceedsExpected()
    {
        var result = GhostTileProgress.CalculatePercent(currentStagingBytes: 1500, totalExpectedBytes: 1000);
        Assert.Equal(100, result);
    }

    [Fact]
    public void CalculatePercent_ReturnsZeroWhenExpectedSizeIsZero()
    {
        var result = GhostTileProgress.CalculatePercent(currentStagingBytes: 500, totalExpectedBytes: 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculatePercent_ReturnsZeroWhenExpectedSizeIsNegative()
    {
        var result = GhostTileProgress.CalculatePercent(currentStagingBytes: 500, totalExpectedBytes: -100);
        Assert.Equal(0, result);
    }
}
