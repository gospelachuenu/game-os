namespace ConsoleLibrary;

/// <summary>
/// Implements the download progress formula from plan.md §4.3:
/// Progress% = (current staging folder size / total expected size from manifest) * 100.
/// </summary>
public static class GhostTileProgress
{
    public static double CalculatePercent(long currentStagingBytes, long totalExpectedBytes)
    {
        if (totalExpectedBytes <= 0)
        {
            return 0;
        }

        var percent = (double)currentStagingBytes / totalExpectedBytes * 100.0;
        return Math.Clamp(percent, 0.0, 100.0);
    }
}
