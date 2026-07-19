using GameLauncher;

namespace GameLauncher.Tests;

public class StoreModeGateTests
{
    [Fact]
    public void EnterStoreTab_SetsIsStoreModeActiveTrue()
    {
        var gate = new StoreModeGate();
        gate.EnterStoreTab();
        Assert.True(gate.IsStoreModeActive);
    }

    [Fact]
    public void ExitStoreTab_SetsIsStoreModeActiveFalse()
    {
        var gate = new StoreModeGate();
        gate.EnterStoreTab();
        gate.ExitStoreTab();
        Assert.False(gate.IsStoreModeActive);
    }

    [Fact]
    public void OnGameProcessObserved_ForcesStoreModeOffEvenIfUiThinksItsStillActive()
    {
        var gate = new StoreModeGate();
        gate.EnterStoreTab();

        gate.OnGameProcessObserved();

        Assert.False(gate.IsStoreModeActive);
    }

    [Fact]
    public void OnGameProcessObserved_RaisesStoreModeExitedWhenTransitioningFromActive()
    {
        var gate = new StoreModeGate();
        gate.EnterStoreTab();
        var fired = false;
        gate.StoreModeExited += () => fired = true;

        gate.OnGameProcessObserved();

        Assert.True(fired);
    }

    [Fact]
    public void OnGameProcessObserved_DoesNotRaiseEventWhenAlreadyInactive()
    {
        var gate = new StoreModeGate();
        var fired = false;
        gate.StoreModeExited += () => fired = true;

        gate.OnGameProcessObserved();

        Assert.False(fired);
    }

    [Fact]
    public void ExitStoreTab_DoesNotRaiseEventWhenAlreadyInactive()
    {
        var gate = new StoreModeGate();
        var fired = false;
        gate.StoreModeExited += () => fired = true;

        gate.ExitStoreTab();

        Assert.False(fired);
    }
}
