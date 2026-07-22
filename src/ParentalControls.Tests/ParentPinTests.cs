using MaintenanceHub;
using ParentalControls;

namespace ParentalControls.Tests;

public class ParentPinTests
{
    private static ParentPin NewPin(out IConsoleStateStore state)
    {
        state = new InMemoryConsoleStateStore();
        return new ParentPin(state);
    }

    [Fact]
    public void IsSet_FalseUntilSet()
    {
        var pin = NewPin(out _);
        Assert.False(pin.IsSet);

        pin.Set("1234");
        Assert.True(pin.IsSet);
    }

    [Fact]
    public void CorrectPin_Verifies()
    {
        var pin = NewPin(out _);
        pin.Set("1234");

        Assert.True(pin.Verify("1234"));
    }

    [Fact]
    public void WrongPin_Fails()
    {
        var pin = NewPin(out _);
        pin.Set("1234");

        Assert.False(pin.Verify("0000"));
    }

    [Fact]
    public void Pin_SurvivesANewInstance()
    {
        // The PIN persists (through the state store), so a reboot doesn't clear it.
        var pin = NewPin(out var state);
        pin.Set("4821");

        var reloaded = new ParentPin(state);
        Assert.True(reloaded.IsSet);
        Assert.True(reloaded.Verify("4821"));
    }

    [Fact]
    public void StoredHash_IsNotThePlaintextPin()
    {
        var pin = NewPin(out var state);
        pin.Set("1234");

        // The whole point of hashing: the PIN must not be recoverable from storage.
        Assert.DoesNotContain("1234", state.Get(ConsoleStateKeys.ParentPinHash));
    }

    [Fact]
    public void SamePin_ProducesDifferentHashes_ViaSalt()
    {
        // A per-console salt means two consoles with the same PIN store different
        // hashes — so cracking one tells you nothing about another.
        var a = NewPin(out var stateA);
        var b = NewPin(out var stateB);
        a.Set("1234");
        b.Set("1234");

        Assert.NotEqual(
            stateA.Get(ConsoleStateKeys.ParentPinHash),
            stateB.Get(ConsoleStateKeys.ParentPinHash));
    }

    [Fact]
    public void CorrectPin_OpensGracePeriod()
    {
        var pin = NewPin(out _);
        pin.Set("1234");
        Assert.False(pin.IsUnlocked);

        pin.Verify("1234");
        Assert.True(pin.IsUnlocked);
    }

    [Fact]
    public void WrongPin_DoesNotUnlock()
    {
        var pin = NewPin(out _);
        pin.Set("1234");

        pin.Verify("9999");
        Assert.False(pin.IsUnlocked);
    }

    [Fact]
    public void Lock_EndsGraceEarly()
    {
        var pin = NewPin(out _);
        pin.Set("1234");
        pin.Verify("1234");
        Assert.True(pin.IsUnlocked);

        pin.Lock();
        Assert.False(pin.IsUnlocked);
    }

    [Fact]
    public void ChangingPin_InvalidatesTheOldOne()
    {
        var pin = NewPin(out _);
        pin.Set("1111");
        pin.Set("2222");

        Assert.False(pin.Verify("1111"));
        Assert.True(pin.Verify("2222"));
    }
}
