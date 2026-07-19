namespace MaintenanceHub;

public sealed record MaintenanceUiState(ProgressDisplayMode DisplayMode, string? WarningText);

/// <summary>
/// Maps the integer update-state variable from plan.md §6.2 to how the Maintenance
/// Hub UI should render it. Pure mapping logic, decoupled from the actual wuapi.dll /
/// AMD installer / patch-validation tracking that produces the UpdateType in the
/// first place.
/// </summary>
public static class MaintenanceUiClassifier
{
    public const string AmdDriverWarningText =
        "WARNING: The screen will flicker, flash black, and audio may cut out briefly " +
        "while the graphics engine restarts. Do not interrupt this process.";

    public static MaintenanceUiState Classify(UpdateType updateType) => updateType switch
    {
        UpdateType.WindowsOsUpdate => new MaintenanceUiState(ProgressDisplayMode.LinearPercent, WarningText: null),
        UpdateType.AmdDriverUpdate => new MaintenanceUiState(ProgressDisplayMode.Indeterminate, AmdDriverWarningText),
        UpdateType.ConsoleSoftwarePatch => new MaintenanceUiState(ProgressDisplayMode.HighSpeedProcessingBar, WarningText: null),
        _ => throw new ArgumentOutOfRangeException(nameof(updateType), updateType, null),
    };
}
