namespace MaintenanceHub;

/// <summary>What the boot-time update check concluded.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>Nothing newer available.</summary>
    UpToDate,

    /// <summary>A newer release exists and should be downloaded in the background.</summary>
    UpdateAvailable,

    /// <summary>An update was downloaded on an earlier boot and is ready to install.</summary>
    ReadyToInstall,

    /// <summary>The check could not be completed — no network, timeout, server down.</summary>
    CheckFailed,
}

public sealed class UpdateCheckResult
{
    public required UpdateCheckOutcome Outcome { get; init; }
    public SoftwareRelease? Release { get; init; }

    /// <summary>Set when Outcome is ReadyToInstall — where the downloaded package sits.</summary>
    public string? PendingPackagePath { get; init; }
}

/// <summary>
/// The boot-time update check (plan.md §6.2, and the agreed boot chain).
///
/// The console checks at STARTUP rather than surfacing a badge on the dashboard: the
/// logo screen is where it should find out, not somewhere a child has to notice a dot.
///
/// Checking and downloading are deliberately SEPARATE. A check costs a few hundred
/// milliseconds against a small manifest; a download is tens of megabytes and varies
/// enormously with the connection. Boot waits for the first and never for the second —
/// otherwise a slow line holds the whole console at the logo screen.
///
/// An update downloaded on one boot is offered for install on the NEXT one, by which
/// point the files are already local and the install honestly takes seconds rather
/// than minutes. That is what makes "Install now" an easy thing to agree to.
/// </summary>
public sealed class SoftwareUpdateChecker
{
    /// <summary>
    /// How long the boot check may take before giving up.
    ///
    /// This is the first thing in the console that can block startup on a machine
    /// outside the house. A console that will not start because a server is down is a
    /// far worse failure than one that checks again tomorrow, so this is short and
    /// failure is not treated as an error.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IUpdateSource _source;
    private readonly IConsoleStateStore _state;

    public SoftwareUpdateChecker(IUpdateSource source, IConsoleStateStore state)
    {
        _source = source;
        _state = state;
    }

    /// <summary>The version currently running, defaulting to 1.0.0 on a fresh console.</summary>
    public string InstalledVersion =>
        _state.Get(ConsoleStateKeys.InstalledVersion) ?? "1.0.0";

    /// <summary>
    /// True when the console booted into a version different from the one it last
    /// recorded — i.e. an update was applied and the "what's new" notes are owed.
    /// </summary>
    public bool HasUnseenUpdateNotes =>
        _state.Get(ConsoleStateKeys.UpdateNotesUnseen) is not null;

    public string? PreviousVersion => _state.Get(ConsoleStateKeys.PreviousVersion);

    /// <summary>
    /// Runs the boot check. Never throws: every failure path resolves to CheckFailed,
    /// because nothing here is worth stopping a boot for.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        // An already-downloaded update takes priority: there is no point asking the
        // server about a version whose files are sitting on disk waiting.
        var pendingVersion = _state.Get(ConsoleStateKeys.PendingVersion);
        var pendingPath = _state.Get(ConsoleStateKeys.PendingPackagePath);

        if (pendingVersion is not null
            && pendingPath is not null
            && SoftwareVersion.IsNewer(pendingVersion, InstalledVersion))
        {
            return new UpdateCheckResult
            {
                Outcome = UpdateCheckOutcome.ReadyToInstall,
                PendingPackagePath = pendingPath,
                Release = new SoftwareRelease
                {
                    Version = pendingVersion,
                    PackageUrl = string.Empty,
                    SizeBytes = 0,
                },
            };
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);

            var release = await _source.CheckForUpdateAsync(InstalledVersion, timeout.Token);

            return release is null
                ? new UpdateCheckResult { Outcome = UpdateCheckOutcome.UpToDate }
                : new UpdateCheckResult { Outcome = UpdateCheckOutcome.UpdateAvailable, Release = release };
        }
        catch (OperationCanceledException)
        {
            // Timed out, or the caller gave up. Either way boot continues.
            return new UpdateCheckResult { Outcome = UpdateCheckOutcome.CheckFailed };
        }
        catch (Exception)
        {
            // Network stack unavailable, DNS failure, malformed manifest. Deliberately
            // broad: there is no failure here worth surfacing to a child, and none
            // worth delaying a boot over.
            return new UpdateCheckResult { Outcome = UpdateCheckOutcome.CheckFailed };
        }
    }

    /// <summary>
    /// Records a completed download so the next boot can offer to install it.
    /// </summary>
    public void RecordDownloaded(SoftwareRelease release, string packagePath)
    {
        _state.Set(ConsoleStateKeys.PendingVersion, release.Version);
        _state.Set(ConsoleStateKeys.PendingPackagePath, packagePath);

        // Keep the notes now, while we still have them — they are shown a boot later,
        // after the install, by which point the release object is gone.
        _state.Set(ConsoleStateKeys.UpdateNotesJson,
            System.Text.Json.JsonSerializer.Serialize(release.Notes));
    }

    /// <summary>
    /// The patch notes for the just-installed update, restored from what was saved at
    /// download time. Empty if none were stored (a manifest without notes, or an update
    /// from before notes were persisted).
    /// </summary>
    public IReadOnlyList<PatchNoteBlock> InstalledNotes
    {
        get
        {
            var json = _state.Get(ConsoleStateKeys.UpdateNotesJson);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<PatchNoteBlock>();
            }

            try
            {
                return System.Text.Json.JsonSerializer
                    .Deserialize<List<PatchNoteBlock>>(json) ?? new List<PatchNoteBlock>();
            }
            catch (System.Text.Json.JsonException)
            {
                return Array.Empty<PatchNoteBlock>();
            }
        }
    }

    /// <summary>
    /// Records that an update is about to be applied. Called BEFORE the install, so
    /// that if the machine loses power mid-install the previous version is still known.
    /// </summary>
    public void RecordInstalling(string version)
    {
        _state.Set(ConsoleStateKeys.PreviousVersion, InstalledVersion);
        _state.Set(ConsoleStateKeys.InstalledVersion, version);
        _state.Set(ConsoleStateKeys.UpdateNotesUnseen, version);
        _state.Remove(ConsoleStateKeys.PendingVersion);
        _state.Remove(ConsoleStateKeys.PendingPackagePath);
    }

    /// <summary>Clears the owed patch notes, so they are shown exactly once per version.</summary>
    public void MarkNotesSeen() => _state.Remove(ConsoleStateKeys.UpdateNotesUnseen);

    /// <summary>
    /// Forgets a downloaded update. Used when the package turns out to be unusable —
    /// better to re-download than to keep offering an install that cannot succeed.
    /// </summary>
    public void DiscardPending()
    {
        _state.Remove(ConsoleStateKeys.PendingVersion);
        _state.Remove(ConsoleStateKeys.PendingPackagePath);
    }
}
