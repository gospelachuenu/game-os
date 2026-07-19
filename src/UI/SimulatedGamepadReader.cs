using InputDaemon;

namespace UI;

/// <summary>
/// Fake IGamepadReader for this windowed test build — no real XInput device is polled.
/// Cycles through battery levels so the status bar's battery icon/text can be exercised
/// end-to-end (via the real BatteryTelemetryMonitor.Classify logic) without a physical
/// controller attached to this laptop.
/// </summary>
public sealed class SimulatedGamepadReader : IGamepadReader
{
    private static readonly BatteryLevel[] CycleLevels =
    [
        BatteryLevel.Full, BatteryLevel.Medium, BatteryLevel.Low, BatteryLevel.Empty,
    ];

    private int _index;

    public GamepadSnapshot GetState(int userIndex) => new(false, 0, 0, 0, 0, 0);

    public BatterySnapshot GetBatteryInformation(int userIndex)
    {
        var level = CycleLevels[_index % CycleLevels.Length];
        _index++;
        return new BatterySnapshot(BatteryType.Nimh, level);
    }
}
