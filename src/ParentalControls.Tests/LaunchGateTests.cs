using ParentalControls;

namespace ParentalControls.Tests;

public class LaunchGateTests
{
    private static readonly DateTime Noon = new(2026, 7, 21, 12, 0, 0);

    private static ParentalPolicy PolicyWith(GameAccess access, string gameId = "game1") =>
        new() { GameAccess = new Dictionary<string, GameAccess> { [gameId] = access } };

    private static LaunchDecision Evaluate(
        ParentalPolicy policy, string gameId = "game1",
        TimeSpan? played = null, bool graceUsed = false, DateTime? now = null)
        => LaunchGate.Evaluate(policy, gameId, played ?? TimeSpan.Zero, graceUsed, now ?? Noon);

    [Fact]
    public void AllowedGame_InNoOtherConstraints_Launches()
    {
        var decision = Evaluate(PolicyWith(GameAccess.Allowed));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void BlockedGame_IsRefused()
    {
        var decision = Evaluate(PolicyWith(GameAccess.Blocked));

        Assert.Equal(LaunchVerdict.GameBlocked, decision.Verdict);
    }

    [Fact]
    public void UnknownGame_DefaultsToPending()
    {
        // The safe default: a newly installed game is not silently playable.
        var decision = Evaluate(ParentalPolicy.Default, gameId: "never-seen");

        Assert.Equal(LaunchVerdict.GamePending, decision.Verdict);
    }

    [Fact]
    public void SystemDisabled_BeatsAnAllowedGame()
    {
        // The kill switch must win over everything — a game the parent previously
        // allowed still cannot start while the console is disabled.
        var policy = new ParentalPolicy
        {
            SystemDisabled = true,
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
        };

        Assert.Equal(LaunchVerdict.SystemDisabled, Evaluate(policy).Verdict);
    }

    [Fact]
    public void OutsideAllowedWindow_IsRefused()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls
            {
                WindowEnabled = true,
                AllowedWindow = new TimeWindow { StartMinutes = 8 * 60, EndMinutes = 11 * 60 },
            },
        };

        // Noon is after the 08:00–11:00 window.
        Assert.Equal(LaunchVerdict.OutsideAllowedHours, Evaluate(policy).Verdict);
    }

    [Fact]
    public void InsideAllowedWindow_Launches()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls
            {
                WindowEnabled = true,
                AllowedWindow = new TimeWindow { StartMinutes = 8 * 60, EndMinutes = 20 * 60 },
            },
        };

        Assert.True(Evaluate(policy).IsAllowed);
    }

    [Fact]
    public void WindowWrappingPastMidnight_IsHandled()
    {
        // A 22:00–02:00 window must contain 23:00 and 01:00 but not noon.
        var window = new TimeWindow { StartMinutes = 22 * 60, EndMinutes = 2 * 60 };

        Assert.True(window.Contains(new TimeOnly(23, 0)));
        Assert.True(window.Contains(new TimeOnly(1, 0)));
        Assert.False(window.Contains(new TimeOnly(12, 0)));
    }

    [Fact]
    public void DailyLimitReached_IsRefused()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls { DailyLimitEnabled = true, DailyLimit = TimeSpan.FromHours(2) },
        };

        var decision = Evaluate(policy, played: TimeSpan.FromHours(2));

        Assert.Equal(LaunchVerdict.DailyLimitReached, decision.Verdict);
    }

    [Fact]
    public void UnderDailyLimit_Launches()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls { DailyLimitEnabled = true, DailyLimit = TimeSpan.FromHours(2) },
        };

        Assert.True(Evaluate(policy, played: TimeSpan.FromMinutes(90)).IsAllowed);
    }

    [Fact]
    public void GraceOffered_WhenAllowedAndUnused()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls
            {
                DailyLimitEnabled = true,
                DailyLimit = TimeSpan.FromHours(2),
                AllowFinishMatchGrace = true,
            },
        };

        var decision = Evaluate(policy, played: TimeSpan.FromHours(2), graceUsed: false);

        Assert.Equal(LaunchVerdict.DailyLimitReached, decision.Verdict);
        Assert.True(decision.GraceAvailable);
    }

    [Fact]
    public void GraceNotOffered_OnceUsed()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls
            {
                DailyLimitEnabled = true,
                DailyLimit = TimeSpan.FromHours(2),
                AllowFinishMatchGrace = true,
            },
        };

        var decision = Evaluate(policy, played: TimeSpan.FromHours(2), graceUsed: true);

        Assert.False(decision.GraceAvailable);
    }

    [Fact]
    public void GraceNotOffered_WhenParentDisabledIt()
    {
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Allowed },
            Time = new TimeControls
            {
                DailyLimitEnabled = true,
                DailyLimit = TimeSpan.FromHours(2),
                AllowFinishMatchGrace = false,
            },
        };

        Assert.False(Evaluate(policy, played: TimeSpan.FromHours(2)).GraceAvailable);
    }

    [Fact]
    public void BlockedGame_ReadsAsBlocked_NotOutOfHours()
    {
        // Ordering check: a blocked game outside the window must say "blocked", the
        // more specific and honest reason, not "out of hours".
        var policy = new ParentalPolicy
        {
            GameAccess = new Dictionary<string, GameAccess> { ["game1"] = GameAccess.Blocked },
            Time = new TimeControls
            {
                WindowEnabled = true,
                AllowedWindow = new TimeWindow { StartMinutes = 8 * 60, EndMinutes = 11 * 60 },
            },
        };

        Assert.Equal(LaunchVerdict.GameBlocked, Evaluate(policy).Verdict);
    }
}
