namespace MaintenanceHub;

/// <summary>
/// Seam over wuapi.dll's Windows Update Agent state tracking (§6.2, State 1). The real
/// implementation calls into the Windows Update COM API, which needs a live Windows
/// Update session to exercise meaningfully — not implemented in this dev environment.
/// </summary>
public interface IWindowsUpdateTracker
{
    /// <summary>0-100 inclusive.</summary>
    int GetPercentComplete();
}
