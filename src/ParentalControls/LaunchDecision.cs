namespace ParentalControls;

/// <summary>Why a launch was allowed or refused — drives what the UI shows.</summary>
public enum LaunchVerdict
{
    Allowed,

    /// <summary>The whole console is disabled by a parent (kill switch).</summary>
    SystemDisabled,

    /// <summary>This game is blocked; the kid can ask a parent.</summary>
    GameBlocked,

    /// <summary>This game hasn't been approved yet; the kid can ask a parent.</summary>
    GamePending,

    /// <summary>Outside the allowed hours (curfew / schedule).</summary>
    OutsideAllowedHours,

    /// <summary>The daily time allowance is used up.</summary>
    DailyLimitReached,
}

public sealed class LaunchDecision
{
    public required LaunchVerdict Verdict { get; init; }
    public bool IsAllowed => Verdict == LaunchVerdict.Allowed;

    /// <summary>
    /// True only when DailyLimitReached AND the policy permits a finish-match grace that
    /// hasn't been used today — so the UI can offer "10 more minutes" rather than a flat no.
    /// </summary>
    public bool GraceAvailable { get; init; }

    /// <summary>A short, kid-readable explanation for the block screen.</summary>
    public string Reason => Verdict switch
    {
        LaunchVerdict.Allowed => "",
        LaunchVerdict.SystemDisabled => "The console is paused by a parent.",
        LaunchVerdict.GameBlocked => "A parent has blocked this game.",
        LaunchVerdict.GamePending => "This game needs a parent's approval first.",
        LaunchVerdict.OutsideAllowedHours => "Games can't be played right now. Check back later.",
        LaunchVerdict.DailyLimitReached => "Today's play time is used up.",
        _ => "",
    };
}

/// <summary>
/// Decides whether a game may start, combining every rule in the policy. This is the
/// single point the launch gate calls, so the rules can never be enforced
/// inconsistently across the UI.
///
/// Order matters and is deliberate — the kill switch beats everything, and time rules
/// are checked last so a blocked game reads as "blocked" rather than "out of hours".
/// </summary>
public static class LaunchGate
{
    /// <param name="playedToday">Total play time already spent today, for the daily limit.</param>
    /// <param name="graceUsedToday">Whether the finish-match grace has already been taken today.</param>
    /// <param name="now">Current local time, injected so the rules are testable.</param>
    public static LaunchDecision Evaluate(
        ParentalPolicy policy,
        string gameId,
        TimeSpan playedToday,
        bool graceUsedToday,
        DateTime now)
    {
        // 1. Kill switch beats all — a disabled console starts nothing.
        if (policy.SystemDisabled)
        {
            return new LaunchDecision { Verdict = LaunchVerdict.SystemDisabled };
        }

        // 2. This specific game's own access.
        switch (policy.AccessFor(gameId))
        {
            case GameAccess.Blocked:
                return new LaunchDecision { Verdict = LaunchVerdict.GameBlocked };
            case GameAccess.Pending:
                return new LaunchDecision { Verdict = LaunchVerdict.GamePending };
        }

        // 3. Time window (curfew / schedule).
        var time = policy.Time;
        if (time.WindowEnabled && time.AllowedWindow is not null
            && !time.AllowedWindow.Contains(TimeOnly.FromDateTime(now)))
        {
            return new LaunchDecision { Verdict = LaunchVerdict.OutsideAllowedHours };
        }

        // 4. Daily allowance — checked last so grace only ever applies to a game that
        //    would otherwise be allowed.
        if (time.DailyLimitEnabled && playedToday >= time.DailyLimit)
        {
            return new LaunchDecision
            {
                Verdict = LaunchVerdict.DailyLimitReached,
                GraceAvailable = time.AllowFinishMatchGrace && !graceUsedToday,
            };
        }

        return new LaunchDecision { Verdict = LaunchVerdict.Allowed };
    }
}
