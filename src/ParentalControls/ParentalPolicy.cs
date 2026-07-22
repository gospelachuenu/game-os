namespace ParentalControls;

/// <summary>Whether a specific game may be started.</summary>
public enum GameAccess
{
    /// <summary>Newly seen, awaiting a parent's decision. The DEFAULT for anything installed.</summary>
    Pending,

    /// <summary>Approved — may start (subject to time rules).</summary>
    Allowed,

    /// <summary>Refused — will not start; shows "Ask a parent".</summary>
    Blocked,
}

/// <summary>Whether the browser may be opened at all, and how restricted it is when it can.</summary>
public enum BrowserAccess
{
    /// <summary>Browser cannot be opened.</summary>
    Off,

    /// <summary>Only sites on the allowlist load; everything else is refused.</summary>
    AllowlistOnly,

    /// <summary>The locked-down YouTube-restricted experience only.</summary>
    YouTubeOnly,

    /// <summary>Open browsing. Deliberately last — never a default.</summary>
    Open,
}

/// <summary>
/// A daily time window during which games may run, e.g. 08:00–20:00. Outside it, games
/// are gated the same as a curfew.
/// </summary>
public sealed class TimeWindow
{
    /// <summary>Minutes past midnight the window opens (0–1440).</summary>
    public required int StartMinutes { get; init; }

    /// <summary>Minutes past midnight the window closes (0–1440).</summary>
    public required int EndMinutes { get; init; }

    /// <summary>True if the given local time falls inside the window.</summary>
    public bool Contains(TimeOnly time)
    {
        var m = time.Hour * 60 + time.Minute;

        // A window that wraps past midnight (e.g. 22:00–02:00) is treated as two
        // spans; without this a late-evening curfew could never be expressed.
        return StartMinutes <= EndMinutes
            ? m >= StartMinutes && m < EndMinutes
            : m >= StartMinutes || m < EndMinutes;
    }
}

/// <summary>
/// The time controls for a console. A game must satisfy BOTH the window (when it may
/// run) and the daily allowance (how long) to be startable.
/// </summary>
public sealed class TimeControls
{
    /// <summary>When enabled, games may only run inside <see cref="AllowedWindow"/>.</summary>
    public bool WindowEnabled { get; init; }

    public TimeWindow? AllowedWindow { get; init; }

    /// <summary>When enabled, total play across the day may not exceed <see cref="DailyLimit"/>.</summary>
    public bool DailyLimitEnabled { get; init; }

    public TimeSpan DailyLimit { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Whether the kid may request one "finish your match" grace period when the daily
    /// limit is hit. The parent controls this: it spares an online match from being
    /// cut mid-round, without letting the limit be dodged. See
    /// [[parental-controls-design]].
    /// </summary>
    public bool AllowFinishMatchGrace { get; init; } = true;

    public TimeSpan GraceDuration { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Browser controls — its own surface, not a single on/off gate.</summary>
public sealed class BrowserPolicy
{
    public BrowserAccess Access { get; init; } = BrowserAccess.Off;

    /// <summary>Hostnames permitted under AllowlistOnly, lower-cased, no scheme.</summary>
    public IReadOnlyList<string> Allowlist { get; init; } = Array.Empty<string>();

    /// <summary>Downloads are refused inside the console browser regardless of access level.</summary>
    public bool BlockDownloads { get; init; } = true;
}

/// <summary>
/// The complete parental policy for a console — everything the portal edits and the
/// console enforces. Immutable: a new policy replaces the old one wholesale, which
/// makes syncing a downloaded policy a single atomic swap rather than a merge.
/// </summary>
public sealed class ParentalPolicy
{
    /// <summary>
    /// The remote kill switch. When true, the console shows a "Paused by a parent" lock
    /// and NOTHING else works. STICKY — it must survive reboots and network loss, so a
    /// kid cannot clear it by pulling the cable. Only a parent re-enabling via the
    /// portal lifts it.
    /// </summary>
    public bool SystemDisabled { get; init; }

    /// <summary>Per-game access, keyed by the library's game id. Absent id = Pending.</summary>
    public IReadOnlyDictionary<string, GameAccess> GameAccess { get; init; }
        = new Dictionary<string, GameAccess>();

    public TimeControls Time { get; init; } = new();

    public BrowserPolicy Browser { get; init; } = new();

    /// <summary>
    /// Monotonic version stamped by the portal. The console keeps the highest it has
    /// seen and ignores anything older, so a stale poll response can never roll policy
    /// backward (e.g. re-enable a game a parent just blocked).
    /// </summary>
    public long Revision { get; init; }

    /// <summary>The default a fresh, unpaired console runs under: nothing allowed yet.</summary>
    public static ParentalPolicy Default { get; } = new();

    /// <summary>Access for a game, defaulting to Pending when the id is unknown.</summary>
    public GameAccess AccessFor(string gameId) =>
        GameAccess.TryGetValue(gameId, out var access) ? access : ParentalControls.GameAccess.Pending;
}
