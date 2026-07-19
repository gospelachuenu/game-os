namespace InputDaemon;

public sealed class XInputGamepadReader : IGamepadReader
{
    public GamepadSnapshot GetState(int userIndex)
    {
        var result = XInputNative.XInputGetState(userIndex, out var state);
        if (result != XInputNative.ERROR_SUCCESS)
        {
            return new GamepadSnapshot(false, 0, 0, 0, 0, 0);
        }

        return new GamepadSnapshot(
            IsConnected: true,
            Buttons: (XInputButtons)state.Gamepad.wButtons,
            LeftThumbX: state.Gamepad.sThumbLX,
            LeftThumbY: state.Gamepad.sThumbLY,
            LeftTrigger: state.Gamepad.bLeftTrigger,
            RightTrigger: state.Gamepad.bRightTrigger);
    }

    public BatterySnapshot GetBatteryInformation(int userIndex)
    {
        var result = XInputNative.XInputGetBatteryInformation(
            userIndex,
            XInputNative.BATTERY_DEVTYPE_GAMEPAD,
            out var info);

        if (result != XInputNative.ERROR_SUCCESS)
        {
            return new BatterySnapshot(BatteryType.Disconnected, BatteryLevel.Empty);
        }

        return new BatterySnapshot((BatteryType)info.BatteryType, (BatteryLevel)info.BatteryLevel);
    }
}
