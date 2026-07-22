using System.Runtime.InteropServices;

namespace UI;

/// <summary>
/// Moves and clicks the real Windows pointer, so a controller can drive web pages.
///
/// WHY THE REAL CURSOR RATHER THAN SYNTHETIC EVENTS
///
/// WebView2 renders its content out of process. The ordinary WPF control offers no way
/// to inject mouse input into the page — `SendMouseInput` exists only on the
/// composition-hosted controller, which would mean restructuring the entire browser
/// around a different hosting model. Since WebView2 responds to a genuine mouse exactly
/// as any application does, moving the actual pointer achieves the same thing with far
/// less machinery, and works on every page including ones that handle input unusually.
///
/// Only used while the browser is open. Nothing else in the console touches the
/// pointer — a console UI has no business moving the user's mouse otherwise.
/// </summary>
internal static class NativeCursor
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>One notch of a mouse wheel, as Windows defines it.</summary>
    private const int WheelDelta = 120;

    public static void SetPosition(int screenX, int screenY) => SetCursorPos(screenX, screenY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Nudges the pointer from wherever it currently is.
    ///
    /// Reads the live position rather than tracking one internally, so the cursor stays
    /// consistent with anything else that moves it — the mouse, or another screen's own
    /// pointer handling.
    /// </summary>
    public static void MoveBy(double dx, double dy)
    {
        if (!GetCursorPos(out var p))
        {
            return;
        }

        SetCursorPos((int)Math.Round(p.X + dx), (int)Math.Round(p.Y + dy));
    }

    public static void LeftClick()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>Positive scrolls up (away from the user), negative scrolls down.</summary>
    public static void Wheel(int notches) =>
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, notches * WheelDelta, UIntPtr.Zero);
}
