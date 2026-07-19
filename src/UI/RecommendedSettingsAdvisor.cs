namespace UI;

/// <summary>
/// A snapshot of the detected system hardware the recommended-settings advice is
/// based on. In this windowed test build the values are mocked (see
/// DetectedHardware.Simulated), but the shape mirrors what a real detector on target
/// hardware would fill in — GPU model + VRAM + the panel's native resolution/refresh/
/// HDR capability — so the advisor logic is the real thing, only the inputs are fake.
/// </summary>
public sealed class DetectedHardware
{
    public required string GpuModel { get; init; }
    public required int GpuVramGb { get; init; }
    public required int DisplayWidth { get; init; }
    public required int DisplayHeight { get; init; }
    public required int DisplayRefreshHz { get; init; }
    public required bool DisplaySupportsHdr { get; init; }

    /// <summary>
    /// Stand-in for a real GPU/display probe. Modelled on a mid-to-high AMD build with
    /// a 4K120 HDR panel — matches the project's AMD-card target (see project memory).
    /// </summary>
    public static DetectedHardware Simulated { get; } = new()
    {
        GpuModel = "AMD Radeon RX 7800 XT",
        GpuVramGb = 16,
        DisplayWidth = 3840,
        DisplayHeight = 2160,
        DisplayRefreshHz = 120,
        DisplaySupportsHdr = true,
    };
}

/// <summary>
/// The concrete recommended settings the advisor produced for a game on this
/// hardware — what the game detail screen shows under "Recommended for your system".
/// </summary>
public sealed class RecommendedSettings
{
    public required string Preset { get; init; }
    public required string Resolution { get; init; }
    public required string FrameRate { get; init; }
    public required string Hdr { get; init; }
    public required string BasisSummary { get; init; }
}

/// <summary>
/// Picks graphics settings a game should run well with on the detected hardware,
/// rather than a fixed "High / 4K / 60" constant. The tiering here is deliberately
/// simple (GPU VRAM as a rough capability proxy + the panel's own native
/// resolution/refresh/HDR) — enough to make the recommendation visibly reflect the
/// machine it ran on. A real build would feed in benchmarked per-GPU/per-game data.
/// </summary>
public static class RecommendedSettingsAdvisor
{
    public static RecommendedSettings Recommend(DetectedHardware hw)
    {
        // GPU capability tier from VRAM (a coarse proxy that's fine for a mock).
        var (preset, targetFps, capResolution) = hw.GpuVramGb switch
        {
            >= 16 => ("Ultra", 120, (w: 3840, h: 2160)),
            >= 12 => ("High", 90, (w: 3840, h: 2160)),
            >= 8 => ("High", 60, (w: 2560, h: 1440)),
            >= 6 => ("Medium", 60, (w: 1920, h: 1080)),
            _ => ("Low", 60, (w: 1920, h: 1080)),
        };

        // Never recommend beyond what the panel can actually display: clamp the
        // resolution to the display's native size, and the frame rate to its refresh.
        var recWidth = Math.Min(capResolution.w, hw.DisplayWidth);
        var recHeight = Math.Min(capResolution.h, hw.DisplayHeight);
        var recFps = Math.Min(targetFps, hw.DisplayRefreshHz);

        var hdr = hw.DisplaySupportsHdr ? "HDR On" : "HDR Unsupported";

        return new RecommendedSettings
        {
            Preset = preset,
            Resolution = $"{recWidth} × {recHeight}",
            FrameRate = $"{recFps} FPS",
            Hdr = hdr,
            BasisSummary = $"Based on your {hw.GpuModel} · {hw.GpuVramGb} GB and {hw.DisplayWidth}×{hw.DisplayHeight} {hw.DisplayRefreshHz}Hz display",
        };
    }
}
