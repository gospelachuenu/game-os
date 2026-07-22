using MaintenanceHub;
using ParentalControls;

namespace ParentalControls.Tests;

public class DeviceIdentityTests
{
    [Fact]
    public void Id_IsStable_AcrossInstances()
    {
        // The id is generated once and must not change on reboot — a portal keys on it.
        var state = new InMemoryConsoleStateStore();
        var first = new DeviceIdentity(state).Id;
        var again = new DeviceIdentity(state).Id;

        Assert.Equal(first, again);
    }

    [Fact]
    public void Id_IsUnique_PerConsole()
    {
        var a = new DeviceIdentity(new InMemoryConsoleStateStore()).Id;
        var b = new DeviceIdentity(new InMemoryConsoleStateStore()).Id;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Id_IsReadableFormat()
    {
        var id = new DeviceIdentity(new InMemoryConsoleStateStore()).Id;

        // Grouped with dashes, no ambiguous characters.
        Assert.Contains("-", id);
        Assert.DoesNotContain("0", id);
        Assert.DoesNotContain("O", id);
        Assert.DoesNotContain("1", id);
    }

    [Fact]
    public void Mode_DefaultsToUnrestricted()
    {
        // A fresh console is open — the crucial default for general distribution.
        var device = new DeviceIdentity(new InMemoryConsoleStateStore());

        Assert.Equal(ControlMode.Unrestricted, device.Mode);
        Assert.False(device.IsChildDevice);
    }

    [Fact]
    public void Mode_PersistsWhenSet()
    {
        var state = new InMemoryConsoleStateStore();
        new DeviceIdentity(state).Mode = ControlMode.ChildDevice;

        Assert.True(new DeviceIdentity(state).IsChildDevice);
    }
}

public class ControlModeGatingTests
{
    private static ParentalControlsService NewService(ControlMode mode, out IConsoleStateStore state)
    {
        state = new InMemoryConsoleStateStore();
        var svc = new ParentalControlsService(state, () => new DateTime(2026, 7, 21, 15, 0, 0));
        svc.Device.Mode = mode;
        return svc;
    }

    [Fact]
    public void Unrestricted_EverythingLaunches_NoGating()
    {
        var svc = NewService(ControlMode.Unrestricted, out _);

        // A never-seen game on an unrestricted console just launches — no Pending,
        // no PIN, nothing. This is the "distributed to an adult" case.
        Assert.True(svc.CanLaunch("any-game").IsAllowed);
        Assert.False(svc.IsActive);
    }

    [Fact]
    public void Unrestricted_IgnoresEvenAStrictPolicy()
    {
        // Even if a policy somehow exists, an unrestricted console does not enforce it.
        var svc = NewService(ControlMode.Unrestricted, out _);
        svc.ApplyPolicy(new ParentalPolicy { Revision = 1, SystemDisabled = true });

        Assert.True(svc.CanLaunch("g1").IsAllowed);
    }

    [Fact]
    public void ChildDevice_EnforcesTheGate()
    {
        var svc = NewService(ControlMode.ChildDevice, out _);

        // Now the parental layer is live: an unapproved game is pending.
        Assert.True(svc.IsActive);
        Assert.Equal(LaunchVerdict.GamePending, svc.CanLaunch("g1").Verdict);
    }

    [Fact]
    public void Unrestricted_ShowsNoPendingRequests()
    {
        var svc = NewService(ControlMode.Unrestricted, out _);
        svc.RequestApproval("g1", "Fortnite");

        // No badge, no queue on an open console — even if something tried to add one.
        Assert.Equal(0, svc.PendingRequestCount);
    }
}
