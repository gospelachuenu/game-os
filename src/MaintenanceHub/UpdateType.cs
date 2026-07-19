namespace MaintenanceHub;

/// <summary>
/// The three update categories from plan.md §6.2, backed by the integer state variable
/// the tracking layer sends to the frontend.
/// </summary>
public enum UpdateType
{
    WindowsOsUpdate = 1,
    AmdDriverUpdate = 2,
    ConsoleSoftwarePatch = 3,
}
