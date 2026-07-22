using System.Text.Json;
using MaintenanceHub;

namespace ParentalControls;

/// <summary>
/// Persists the parental policy and the day's play usage through the console's durable
/// state store — the UWF-excluded path, so a kid rebooting cannot wipe the policy or
/// reset their own play timer.
///
/// The policy and the usage counter are stored SEPARATELY on purpose: the policy comes
/// from the portal and is replaced wholesale on sync, while usage accrues locally
/// through the day and resets at midnight. Mixing them would mean a policy sync either
/// clobbering the timer or having to carefully preserve it.
/// </summary>
public sealed class ParentalPolicyStore
{
    private const string PolicyKey = "parental.policy";
    private const string UsageKey = "parental.usage";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IConsoleStateStore _state;

    public ParentalPolicyStore(IConsoleStateStore state) => _state = state;

    /// <summary>
    /// The current policy, or Default when none is stored (a fresh, unpaired console).
    /// A corrupt stored policy also falls back to Default rather than throwing —
    /// failing OPEN would be wrong here (it would unlock a locked console), so Default
    /// is the safe fallback: it allows nothing.
    /// </summary>
    public ParentalPolicy Load()
    {
        var json = _state.Get(PolicyKey);
        if (string.IsNullOrEmpty(json))
        {
            return ParentalPolicy.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<ParentalPolicy>(json, JsonOptions) ?? ParentalPolicy.Default;
        }
        catch (JsonException)
        {
            return ParentalPolicy.Default;
        }
    }

    /// <summary>
    /// Stores a policy, but ONLY if its revision is newer than what is already held.
    /// A stale poll response (an older revision arriving late) must never roll the
    /// policy backward — e.g. re-allow a game a parent has just blocked. Returns true
    /// if the policy was actually applied.
    /// </summary>
    public bool Save(ParentalPolicy policy)
    {
        var current = Load();
        if (policy.Revision < current.Revision)
        {
            return false;
        }

        _state.Set(PolicyKey, JsonSerializer.Serialize(policy, JsonOptions));
        return true;
    }

    // ---------------- daily usage ----------------

    private sealed class UsageRecord
    {
        public string Date { get; set; } = "";       // local date, so it resets at midnight
        public long PlayedSeconds { get; set; }
        public bool GraceUsed { get; set; }
    }

    private UsageRecord LoadUsage(DateTime now)
    {
        var json = _state.Get(UsageKey);
        UsageRecord? record = null;

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                record = JsonSerializer.Deserialize<UsageRecord>(json, JsonOptions);
            }
            catch (JsonException)
            {
                record = null;
            }
        }

        var today = now.ToString("yyyy-MM-dd");

        // A record from a previous day is stale — the allowance resets at midnight, so
        // start fresh rather than carrying yesterday's minutes forward.
        if (record is null || record.Date != today)
        {
            return new UsageRecord { Date = today };
        }

        return record;
    }

    private void SaveUsage(UsageRecord record) =>
        _state.Set(UsageKey, JsonSerializer.Serialize(record, JsonOptions));

    /// <summary>How long has been played today. Zero on a new day.</summary>
    public TimeSpan PlayedToday(DateTime now) =>
        TimeSpan.FromSeconds(LoadUsage(now).PlayedSeconds);

    /// <summary>Whether the finish-match grace has been used today.</summary>
    public bool GraceUsedToday(DateTime now) => LoadUsage(now).GraceUsed;

    /// <summary>Adds elapsed play to today's total.</summary>
    public void AddPlayTime(TimeSpan elapsed, DateTime now)
    {
        var record = LoadUsage(now);
        record.PlayedSeconds += (long)elapsed.TotalSeconds;
        SaveUsage(record);
    }

    /// <summary>Records that the one-shot finish-match grace has been taken today.</summary>
    public void MarkGraceUsed(DateTime now)
    {
        var record = LoadUsage(now);
        record.GraceUsed = true;
        SaveUsage(record);
    }
}
