using System.Runtime.InteropServices;

namespace InputDaemon;

[StructLayout(LayoutKind.Sequential)]
public struct XInputGamepad
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputBatteryInformation
{
    public byte BatteryType;
    public byte BatteryLevel;
}

[Flags]
public enum XInputButtons : ushort
{
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public enum BatteryType : byte
{
    Disconnected = 0x00,
    Wired = 0x01,
    Alkaline = 0x02,
    Nimh = 0x03,
    Unknown = 0xFF,
}

public enum BatteryLevel : byte
{
    Empty = 0x00,
    Low = 0x01,
    Medium = 0x02,
    Full = 0x03,
}

internal static class XInputNative
{
    private const string DllName = "xinput1_4.dll";
    public const int BATTERY_DEVTYPE_GAMEPAD = 0x00;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    [DllImport(DllName)]
    public static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    [DllImport(DllName)]
    public static extern int XInputGetBatteryInformation(int dwUserIndex, byte devType, out XInputBatteryInformation pBatteryInformation);
}
