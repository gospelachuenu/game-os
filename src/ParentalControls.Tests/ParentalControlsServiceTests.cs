using MaintenanceHub;
using ParentalControls;

namespace ParentalControls.Tests;

public class ApprovalQueueTests
{
    private static readonly DateTime Now = new(2026, 7, 21, 18, 0, 0);

    private static ApprovalQueue NewQueue() => new(new InMemoryConsoleStateStore());

    [Fact]
    public void Add_ThenListed()
    {
        var q = NewQueue();
        q.Add("g1", "Fortnite", Now);

        Assert.Equal(1, q.Count);
        Assert.True(q.Contains("g1"));
    }

    [Fact]
    public void Adding_SameGameTwice_DoesNotDuplicate()
    {
        // A kid pressing the same blocked game twice must not stack up two requests.
        var q = NewQueue();
        q.Add("g1", "Fortnite", Now);
        q.Add("g1", "Fortnite", Now.AddMinutes(5));

        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Remove_ClearsARequest()
    {
        var q = NewQueue();
        q.Add("g1", "Fortnite", Now);
        q.Remove("g1");

        Assert.Equal(0, q.Count);
        Assert.False(q.Contains("g1"));
    }

    [Fact]
    public void Requests_SurviveANewInstance()
    {
        var state = new InMemoryConsoleStateStore();
        new ApprovalQueue(state).Add("g1", "Fortnite", Now);

        // A request made before bedtime is still there next morning.
        Assert.True(new ApprovalQueue(state).Contains("g1"));
    }
}

public class ParentalControlsServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 21, 15, 0, 0);

    private static ParentalControlsService NewService(out IConsoleStateStore state)
    {
        state = new InMemoryConsoleStateStore();
        var svc = new ParentalControlsService(state, () => Now);
        // These tests exercise the parental layer, so the device is a child device.
        // The dormant-when-unrestricted behaviour is covered by ControlModeGatingTests.
        svc.Device.Mode = ControlMode.ChildDevice;
        return svc;
    }

    [Fact]
    public void FreshConsole_GamesArePending()
    {
        var svc = NewService(out _);

        Assert.Equal(LaunchVerdict.GamePending, svc.CanLaunch("g1").Verdict);
    }

    [Fact]
    public void RequestApproval_ShowsInQueue()
    {
        var svc = NewService(out _);
        svc.RequestApproval("g1", "Fortnite");

        Assert.Equal(1, svc.PendingRequestCount);
        Assert.Equal("Fortnite", svc.PendingRequests[0].GameName);
    }

    [Fact]
    public void Decide_RequiresPinUnlock()
    {
        var svc = NewService(out _);
        svc.RequestApproval("g1", "Fortnite");

        // Without the PIN entered, a decision is refused — the kid can't approve
        // their own game.
        var applied = svc.Decide("g1", allow: true);

        Assert.False(applied);
        Assert.Equal(LaunchVerdict.GamePending, svc.CanLaunch("g1").Verdict);
    }

    [Fact]
    public void Decide_Allows_WhenPinUnlocked()
    {
        var svc = NewService(out _);
        svc.Pin.Set("1234");
        svc.Pin.Verify("1234"); // opens grace
        svc.RequestApproval("g1", "Fortnite");

        var applied = svc.Decide("g1", allow: true);

        Assert.True(applied);
        Assert.True(svc.CanLaunch("g1").IsAllowed);
        Assert.Equal(0, svc.PendingRequestCount); // cleared from queue
    }

    [Fact]
    public void Decide_Block_RefusesLaunchAndClearsQueue()
    {
        var svc = NewService(out _);
        svc.Pin.Set("1234");
        svc.Pin.Verify("1234");
        svc.RequestApproval("g1", "Fortnite");

        svc.Decide("g1", allow: false);

        Assert.Equal(LaunchVerdict.GameBlocked, svc.CanLaunch("g1").Verdict);
        Assert.Equal(0, svc.PendingRequestCount);
    }

    [Fact]
    public void ApplyPolicy_FromPortal_TakesEffect()
    {
        var svc = NewService(out _);

        svc.ApplyPolicy(new ParentalPolicy
        {
            Revision = 1,
            GameAccess = new Dictionary<string, GameAccess> { ["g1"] = GameAccess.Allowed },
        });

        Assert.True(svc.CanLaunch("g1").IsAllowed);
    }

    [Fact]
    public void SystemDisabled_BlocksEverything()
    {
        var svc = NewService(out _);
        svc.ApplyPolicy(new ParentalPolicy
        {
            Revision = 1,
            SystemDisabled = true,
            GameAccess = new Dictionary<string, GameAccess> { ["g1"] = GameAccess.Allowed },
        });

        Assert.Equal(LaunchVerdict.SystemDisabled, svc.CanLaunch("g1").Verdict);
    }

    [Fact]
    public void LocalDecision_BumpsRevision_SoAnEqualPollDoesNotOverwriteIt()
    {
        var svc = NewService(out _);
        svc.ApplyPolicy(new ParentalPolicy { Revision = 5 });
        svc.Pin.Set("1234");
        svc.Pin.Verify("1234");

        svc.Decide("g1", allow: true); // now revision 6

        // A poll response at the OLD revision must not undo the local decision.
        svc.ApplyPolicy(new ParentalPolicy { Revision = 5 });

        Assert.True(svc.CanLaunch("g1").IsAllowed);
    }
}
