using System.Text.Json.Serialization;

namespace HardwareManager;

public sealed class GpuProfile
{
    [JsonPropertyName("ProfileName")]
    public required string ProfileName { get; init; }

    [JsonPropertyName("TargetGPU")]
    public required string TargetGpu { get; init; }

    [JsonPropertyName("DesiredState")]
    public required GpuDesiredState DesiredState { get; init; }

    [JsonPropertyName("SafetyThresholds")]
    public required GpuSafetyThresholds SafetyThresholds { get; init; }
}

public sealed class GpuDesiredState
{
    [JsonPropertyName("CoreClockMHz")]
    public required int CoreClockMhz { get; init; }

    [JsonPropertyName("VoltageMv")]
    public required int VoltageMv { get; init; }

    [JsonPropertyName("PowerLimitPercent")]
    public required int PowerLimitPercent { get; init; }

    [JsonPropertyName("FanTargetCelsius")]
    public required int FanTargetCelsius { get; init; }
}

public sealed class GpuSafetyThresholds
{
    [JsonPropertyName("MaxAllowedCelsius")]
    public required int MaxAllowedCelsius { get; init; }

    [JsonPropertyName("MinAllowedVoltageMv")]
    public required int MinAllowedVoltageMv { get; init; }
}
