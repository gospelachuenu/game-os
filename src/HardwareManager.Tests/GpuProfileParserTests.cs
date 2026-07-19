using HardwareManager;

namespace HardwareManager.Tests;

public class GpuProfileParserTests
{
    private const string SampleProfileJson = """
        {
          "ProfileName": "RX6600M_Balanced",
          "TargetGPU": "AMD Radeon RX 6600M",
          "DesiredState": {
            "CoreClockMHz": 2300,
            "VoltageMv": 950,
            "PowerLimitPercent": -10,
            "FanTargetCelsius": 75
          },
          "SafetyThresholds": {
            "MaxAllowedCelsius": 85,
            "MinAllowedVoltageMv": 900
          }
        }
        """;

    [Fact]
    public void Parse_ReadsAllFieldsFromPlanMdSampleJson()
    {
        var profile = GpuProfileParser.Parse(SampleProfileJson);

        Assert.NotNull(profile);
        Assert.Equal("RX6600M_Balanced", profile!.ProfileName);
        Assert.Equal("AMD Radeon RX 6600M", profile.TargetGpu);
        Assert.Equal(2300, profile.DesiredState.CoreClockMhz);
        Assert.Equal(950, profile.DesiredState.VoltageMv);
        Assert.Equal(-10, profile.DesiredState.PowerLimitPercent);
        Assert.Equal(75, profile.DesiredState.FanTargetCelsius);
        Assert.Equal(85, profile.SafetyThresholds.MaxAllowedCelsius);
        Assert.Equal(900, profile.SafetyThresholds.MinAllowedVoltageMv);
    }

    [Fact]
    public void Parse_ReturnsNullOnInvalidJson()
    {
        var profile = GpuProfileParser.Parse("{ not valid json");
        Assert.Null(profile);
    }

    [Fact]
    public void Load_ReadsFromDiskAndParses()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gpu_profile_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, SampleProfileJson);
            var profile = GpuProfileParser.Load(tempFile);
            Assert.Equal("RX6600M_Balanced", profile.ProfileName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_ThrowsFormatExceptionOnInvalidJson()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gpu_profile_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ not valid json");
            Assert.Throws<FormatException>(() => GpuProfileParser.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
