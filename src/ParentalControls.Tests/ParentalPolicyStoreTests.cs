using MaintenanceHub;
using ParentalControls;

namespace ParentalControls.Tests;

public class ParentalPolicyStoreTests
{
    private static readonly DateTime Today = new(2026, 7, 21, 12, 0, 0);
    private static readonly DateTime Tomorrow = new(2026, 7, 22, 9, 0, 0);

    private static ParentalPolicyStore NewStore() => new(new InMemoryConsoleStateStore());

    [Fact]
    public void Load_ReturnsDefault_WhenNothingStored()
    {
        var policy = NewStore().Load();

        // Default allows nothing — the safe state for an unpaired console.
        Assert.False(policy.SystemDisabled);
        Assert.Equal(GameAccess.Pending, policy.AccessFor("anything"));
    }

    [Fact]
    public void SavedPolicy_RoundTrips()
    {
        var store = NewStore();
        var policy = new ParentalPolicy
        {
            Revision = 5,
            SystemDisabled = true,
            GameAccess = new Dictionary<string, GameAccess> { ["g"] = GameAccess.Allowed },
        };

        store.Save(policy);
        var loaded = store.Load();

        Assert.True(loaded.SystemDisabled);
        Assert.Equal(GameAccess.Allowed, loaded.AccessFor("g"));
        Assert.Equal(5, loaded.Revision);
    }

    [Fact]
    public void Save_RejectsAnOlderRevision()
    {
        // A stale poll response arriving late must not roll policy backward — e.g.
        // re-allow a game the parent just blocked.
        var store = NewStore();
        store.Save(new ParentalPolicy { Revision = 10, SystemDisabled = true });

        var applied = store.Save(new ParentalPolicy { Revision = 3, SystemDisabled = false });

        Assert.False(applied);
        Assert.True(store.Load().SystemDisabled); // unchanged
    }

    [Fact]
    public void Save_AcceptsSameOrNewerRevision()
    {
        var store = NewStore();
        store.Save(new ParentalPolicy { Revision = 10 });

        Assert.True(store.Save(new ParentalPolicy { Revision = 10 }));
        Assert.True(store.Save(new ParentalPolicy { Revision = 11 }));
    }

    [Fact]
    public void CorruptPolicy_FallsBackToDefault_NotThrow()
    {
        // Corrupt policy must fail CLOSED (Default allows nothing), never open —
        // failing open would unlock a locked console.
        var state = new InMemoryConsoleStateStore();
        state.Set("parental.policy", "{ not valid json");
        var store = new ParentalPolicyStore(state);

        var policy = store.Load();

        Assert.Equal(GameAccess.Pending, policy.AccessFor("g"));
    }

    [Fact]
    public void PlayTime_Accrues()
    {
        var store = NewStore();
        store.AddPlayTime(TimeSpan.FromMinutes(30), Today);
        store.AddPlayTime(TimeSpan.FromMinutes(15), Today);

        Assert.Equal(TimeSpan.FromMinutes(45), store.PlayedToday(Today));
    }

    [Fact]
    public void PlayTime_ResetsNextDay()
    {
        // The daily allowance resets at midnight; yesterday's minutes must not carry.
        var store = NewStore();
        store.AddPlayTime(TimeSpan.FromHours(2), Today);

        Assert.Equal(TimeSpan.Zero, store.PlayedToday(Tomorrow));
    }

    [Fact]
    public void Grace_TracksPerDay()
    {
        var store = NewStore();
        Assert.False(store.GraceUsedToday(Today));

        store.MarkGraceUsed(Today);
        Assert.True(store.GraceUsedToday(Today));

        // New day = grace available again.
        Assert.False(store.GraceUsedToday(Tomorrow));
    }

    [Fact]
    public void PolicyAndUsage_AreIndependent()
    {
        // Syncing a new policy must not clobber the running play timer.
        var store = NewStore();
        store.AddPlayTime(TimeSpan.FromMinutes(40), Today);

        store.Save(new ParentalPolicy { Revision = 2, SystemDisabled = true });

        Assert.Equal(TimeSpan.FromMinutes(40), store.PlayedToday(Today));
    }
}
