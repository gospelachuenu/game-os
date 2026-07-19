namespace InputDaemon;

public enum BatteryUiState
{
    Full,
    Medium,
    Low,
    Critical,
    Charging,
    Unknown,
}

/// <summary>
/// Implements plan.md §5.4: classifies raw XInput battery telemetry into a UI-facing
/// state, and suppresses low-battery toast alerts while a game window has focus so
/// notifications don't interrupt gameplay.
/// </summary>
public sealed class BatteryTelemetryMonitor
{
    public static BatteryUiState Classify(BatterySnapshot snapshot)
    {
        if (snapshot.Type == BatteryType.Wired)
        {
            return BatteryUiState.Charging;
        }

        if (snapshot.Type == BatteryType.Disconnected)
        {
            return BatteryUiState.Unknown;
        }

        return snapshot.Level switch
        {
            BatteryLevel.Full => BatteryUiState.Full,
            BatteryLevel.Medium => BatteryUiState.Medium,
            BatteryLevel.Low => BatteryUiState.Low,
            BatteryLevel.Empty => BatteryUiState.Critical,
            _ => BatteryUiState.Unknown,
        };
    }

    /// <summary>
    /// Critical-battery toasts are queued/muted while a game has focus (§5.4's
    /// "In-Game Suppression"); every other state is always safe to surface since it's
    /// not an interrupting alert.
    /// </summary>
    public static bool ShouldShowCriticalToast(BatteryUiState state, bool isGameCurrentlyFocused)
    {
        if (state != BatteryUiState.Critical)
        {
            return false;
        }

        return !isGameCurrentlyFocused;
    }
}
