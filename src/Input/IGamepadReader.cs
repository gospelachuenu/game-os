namespace InputDaemon;

public readonly record struct GamepadSnapshot(
    bool IsConnected,
    XInputButtons Buttons,
    short LeftThumbX,
    short LeftThumbY,
    byte LeftTrigger,
    byte RightTrigger);

public readonly record struct BatterySnapshot(BatteryType Type, BatteryLevel Level);

public interface IGamepadReader
{
    GamepadSnapshot GetState(int userIndex);
    BatterySnapshot GetBatteryInformation(int userIndex);
}
