namespace MaintenanceHub;

/// <summary>
/// How the Maintenance Hub UI should render progress for a given update type (§6.2).
/// </summary>
public enum ProgressDisplayMode
{
    /// <summary>0-100% linear string with a minimalist loading ring (Windows OS updates).</summary>
    LinearPercent,

    /// <summary>Indeterminate looping track — silent AMD packages report no linear progress.</summary>
    Indeterminate,

    /// <summary>High-speed processing bar for internal data validation steps.</summary>
    HighSpeedProcessingBar,
}
