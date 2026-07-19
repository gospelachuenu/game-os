using InputDaemon;

namespace Input.Tests;

public class MouseEmulationMapperTests
{
    private static GamepadSnapshot Snapshot(
        XInputButtons buttons, short leftX = 0, short leftY = 0) =>
        new(true, buttons, leftX, leftY, 0, 0);

    [Fact]
    public void Map_InactiveWhenLbNotHeld()
    {
        var result = MouseEmulationMapper.Map(Snapshot(0, leftX: 20000));
        Assert.False(result.IsActive);
        Assert.Equal(0, result.CursorDeltaX);
    }

    [Fact]
    public void Map_ActiveWhenLbHeld()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder));
        Assert.True(result.IsActive);
    }

    [Fact]
    public void Map_ZeroDeltaWithinDeadZone()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder, leftX: 5000, leftY: 5000));
        Assert.Equal(0, result.CursorDeltaX);
        Assert.Equal(0, result.CursorDeltaY);
    }

    [Fact]
    public void Map_PositiveStickXProducesPositiveDeltaX()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder, leftX: 32767));
        Assert.True(result.CursorDeltaX > 0);
    }

    [Fact]
    public void Map_NegativeStickXProducesNegativeDeltaX()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder, leftX: -32768));
        Assert.True(result.CursorDeltaX < 0);
    }

    [Fact]
    public void Map_PositiveStickYProducesNegativeDeltaY_ScreenCoordinatesAreInverted()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder, leftY: 32767));
        Assert.True(result.CursorDeltaY < 0);
    }

    [Fact]
    public void Map_AButtonMapsToLeftClick()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder | XInputButtons.A));
        Assert.True(result.LeftClick);
        Assert.False(result.RightClick);
    }

    [Fact]
    public void Map_BButtonMapsToRightClick()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder | XInputButtons.B));
        Assert.True(result.RightClick);
        Assert.False(result.LeftClick);
    }

    [Fact]
    public void Map_NoClicksWhenAOrBNotHeld()
    {
        var result = MouseEmulationMapper.Map(Snapshot(XInputButtons.LeftShoulder));
        Assert.False(result.LeftClick);
        Assert.False(result.RightClick);
    }
}
