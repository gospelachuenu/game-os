namespace ConsoleSupervisor;

public sealed class FocusGuardian : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _hook;

    private uint _activeGameProcessId;
    private uint _uiProcessId;

    public bool IsStoreModeActive { get; set; }

    public FocusGuardian()
    {
        _callback = OnForegroundChanged;
    }

    public void SetActiveGameProcessId(uint processId) => _activeGameProcessId = processId;

    public void SetUiProcessId(uint processId) => _uiProcessId = processId;

    public void Start()
    {
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_hook == IntPtr.Zero)
        {
            Console.WriteLine("[FocusGuardian] Failed to install SetWinEventHook.");
        }
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (IsStoreModeActive || hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var foregroundProcessId);

        bool isExpectedSurface =
            foregroundProcessId == _activeGameProcessId ||
            foregroundProcessId == _uiProcessId;

        if (!isExpectedSurface && _activeGameProcessId != 0)
        {
            Console.WriteLine($"[FocusGuardian] Foreground deviated to PID {foregroundProcessId}. Reclaiming focus.");
            Reclaim();
        }
    }

    private void Reclaim()
    {
        // Prefer restoring the active game window; fall back to UI.exe if no game is running.
        var targetHandle = FindWindowForProcess(_activeGameProcessId) ?? FindWindowForProcess(_uiProcessId);
        if (targetHandle is IntPtr handle && handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }
    }

    private static IntPtr? FindWindowForProcess(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.MainWindowHandle;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
