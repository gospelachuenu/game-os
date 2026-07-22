using MaintenanceHub;

namespace ParentalControls;

/// <summary>
/// The single entry point the UI uses for everything parental. It composes the policy
/// store, the PIN, the approval queue and usage tracking, so the UI never has to
/// coordinate them itself — and so the enforcement rules live in one place rather than
/// being re-implemented per screen.
/// </summary>
public sealed class ParentalControlsService
{
    private readonly ParentalPolicyStore _policyStore;
    private readonly ApprovalQueue _queue;
    private readonly Func<DateTime> _now;

    public ParentPin Pin { get; }
    public DeviceIdentity Device { get; }

    public ParentalControlsService(IConsoleStateStore state, Func<DateTime>? now = null)
    {
        _policyStore = new ParentalPolicyStore(state);
        _queue = new ApprovalQueue(state);
        Pin = new ParentPin(state);
        Device = new DeviceIdentity(state);
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>
    /// True only when this console is set up as a child's device. On an unrestricted
    /// console (the default) the entire parental layer is dormant — no gating, no PIN,
    /// no queue — which is why every public method below short-circuits when this is
    /// false.
    /// </summary>
    public bool IsActive => Device.IsChildDevice;

    public ParentalPolicy Policy => _policyStore.Load();

    /// <summary>Applies a policy from the portal. Ignored if older than the current one.</summary>
    public bool ApplyPolicy(ParentalPolicy policy) => _policyStore.Save(policy);

    /// <summary>Number of games waiting on a parent's decision — drives the Settings badge. Always 0 on an unrestricted console.</summary>
    public int PendingRequestCount => IsActive ? _queue.Count : 0;

    public IReadOnlyList<ApprovalRequest> PendingRequests =>
        IsActive ? _queue.All() : Array.Empty<ApprovalRequest>();

    /// <summary>
    /// Whether a game may start right now, applying every rule. The UI calls this in
    /// front of a launch and shows the block reason when it is refused.
    /// </summary>
    public LaunchDecision CanLaunch(string gameId)
    {
        // Unrestricted console: everything launches. The parental rules never run, so
        // an adult recipient — or your own device — has no gating at all.
        if (!IsActive)
        {
            return new LaunchDecision { Verdict = LaunchVerdict.Allowed };
        }

        var now = _now();
        return LaunchGate.Evaluate(
            Policy,
            gameId,
            _policyStore.PlayedToday(now),
            _policyStore.GraceUsedToday(now),
            now);
    }

    /// <summary>
    /// Records a kid's "Ask a parent" request. Called when a blocked or pending game is
    /// pressed; the game name is stored so the parent sees what was asked for.
    /// </summary>
    public void RequestApproval(string gameId, string gameName) =>
        _queue.Add(gameId, gameName, _now());

    /// <summary>
    /// A parent's decision on a game — allow or block — which also clears it from the
    /// queue. Guarded by the PIN grace period: refuses unless the parent has recently
    /// entered the PIN, so the kid cannot approve their own games.
    /// </summary>
    public bool Decide(string gameId, bool allow)
    {
        if (!Pin.IsUnlocked)
        {
            return false;
        }

        var policy = Policy;
        var access = new Dictionary<string, GameAccess>(policy.GameAccess)
        {
            [gameId] = allow ? GameAccess.Allowed : GameAccess.Blocked,
        };

        // A local parent decision bumps the revision so it is not overwritten by an
        // equal-or-older policy arriving from a later poll before the portal has caught
        // up. (The portal will win once it stamps a higher revision.)
        _policyStore.Save(new ParentalPolicy
        {
            SystemDisabled = policy.SystemDisabled,
            GameAccess = access,
            Time = policy.Time,
            Browser = policy.Browser,
            Revision = policy.Revision + 1,
        });

        _queue.Remove(gameId);
        return true;
    }

    /// <summary>Adds elapsed play toward today's limit. Called as a game runs.</summary>
    public void RecordPlayTime(TimeSpan elapsed) => _policyStore.AddPlayTime(elapsed, _now());

    /// <summary>Takes the one-shot finish-match grace, if available.</summary>
    public bool UseFinishMatchGrace()
    {
        var now = _now();
        var decision = LaunchGate.Evaluate(Policy, "", _policyStore.PlayedToday(now),
            _policyStore.GraceUsedToday(now), now);

        // Grace is only meaningful at the daily limit, and only once per day.
        if (decision.Verdict != LaunchVerdict.DailyLimitReached || !decision.GraceAvailable)
        {
            return false;
        }

        _policyStore.MarkGraceUsed(now);
        return true;
    }
}
