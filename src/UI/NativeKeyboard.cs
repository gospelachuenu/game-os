namespace UI;

/// <summary>
/// The set of keys the controller can send to a web page.
///
/// WHY THERE IS NO SendInput HERE ANY MORE
///
/// This class used to synthesise OS-level key presses with SendInput. That does not
/// work for WebView2 and cost a great deal of time to discover, so it is worth writing
/// down: WebView2 renders the page in a separate process, and SendInput delivers to
/// whatever window holds OS keyboard focus. Hosted inside a WPF control, the renderer's
/// child window does not reliably hold that focus, so the call SUCCEEDS — returning a
/// perfectly healthy event count — while the page receives nothing at all. Chasing the
/// focus chain, scan codes and the extended-key flag does not fix it either.
///
/// WebView2 exposes no keyboard-send API of its own (still an open request against the
/// SDK), so keys go through the DevTools protocol instead — see BrowserScreen.SendKey,
/// which uses Input.dispatchKeyEvent. That injects straight into the renderer's input
/// pipeline, arrives as a trusted event, and does not depend on window focus.
///
/// Values are Windows virtual-key codes; BrowserScreen maps them to DOM key names.
/// </summary>
public static class NativeKeyboard
{
    public enum VirtualKey : ushort
    {
        Back = 0x08,
        Return = 0x0D,
        Escape = 0x1B,
        Space = 0x20,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
    }
}
