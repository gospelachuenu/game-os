namespace GameLauncher;

/// <summary>
/// Implements plan.md §3.3/§3.4: tracks isStoreModeActive, which the Supervisor's
/// FocusGuardian reads to decide whether to suspend focus reclaiming. A WMI-observed
/// game process start always wins and forces store mode back off, even if the UI
/// thinks it's still in the Store tab (§3.4).
/// </summary>
public sealed class StoreModeGate
{
    public bool IsStoreModeActive { get; private set; }

    public event Action? StoreModeExited;

    public void EnterStoreTab()
    {
        IsStoreModeActive = true;
    }

    public void ExitStoreTab()
    {
        SetInactive();
    }

    /// <summary>
    /// Called when the WMI Win32_ProcessStartTrace watcher observes a new game process.
    /// Forces store mode off regardless of current UI tab state.
    /// </summary>
    public void OnGameProcessObserved()
    {
        SetInactive();
    }

    private void SetInactive()
    {
        var wasActive = IsStoreModeActive;
        IsStoreModeActive = false;

        if (wasActive)
        {
            StoreModeExited?.Invoke();
        }
    }
}
