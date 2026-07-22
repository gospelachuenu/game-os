namespace MaintenanceHub;

/// <summary>
/// Durable key/value state for the console — the small set of facts that MUST survive
/// a reboot: which software version is installed, whether an update has been
/// downloaded, the parent PIN hash, whether first-run setup has completed.
///
/// WHY THIS IS A SEAM RATHER THAN JUST WRITING A FILE
///
/// On the real console the system volume runs under the Unified Write Filter, which
/// redirects every write into a RAM overlay and discards it on reboot. Ordinary file
/// writes therefore LOOK like they succeed and then silently vanish. Anything that
/// needs to persist has to live in a directory registered with
/// `uwfmgr file add-exclusion`.
///
/// This was verified rather than assumed: a file written to an excluded directory
/// survived a reboot while a file outside it was wiped, confirming both that the
/// exclusion works and that the filter was genuinely active.
///
/// The implementation is therefore not "write a file anywhere" but "write a file to
/// the one path the write filter has been told to leave alone", and that path is a
/// deployment concern rather than something the calling code should know about.
/// </summary>
public interface IConsoleStateStore
{
    /// <summary>Reads a value, or null when the key has never been set.</summary>
    string? Get(string key);

    /// <summary>Writes a value, replacing any existing one.</summary>
    void Set(string key, string value);

    /// <summary>Removes a key. Removing a key that does not exist is not an error.</summary>
    void Remove(string key);

    /// <summary>True when the underlying store is readable and writable.</summary>
    bool IsAvailable { get; }
}

/// <summary>
/// The keys the console persists. Collected here rather than scattered as string
/// literals so that a typo cannot silently create a second, permanently-empty setting.
/// </summary>
public static class ConsoleStateKeys
{
    /// <summary>Software version currently installed, e.g. "1.4.0".</summary>
    public const string InstalledVersion = "software.installed_version";

    /// <summary>
    /// Version running BEFORE the last update was applied. Set immediately before an
    /// install so that, after the reboot, the console can tell it has just been
    /// updated and offer the patch notes.
    /// </summary>
    public const string PreviousVersion = "software.previous_version";

    /// <summary>Version downloaded and waiting to install, if any.</summary>
    public const string PendingVersion = "software.pending_version";

    /// <summary>Absolute path to the downloaded update package.</summary>
    public const string PendingPackagePath = "software.pending_package";

    /// <summary>
    /// Set when an update has been installed but its "what's new" screen has not been
    /// shown yet. Cleared once the user has seen or dismissed it, so the notes appear
    /// exactly once per version.
    /// </summary>
    public const string UpdateNotesUnseen = "software.notes_unseen";

    /// <summary>
    /// The patch notes for the pending/just-installed update, as JSON.
    ///
    /// Stored because the notes arrive with the DOWNLOAD but are shown after the INSTALL,
    /// a boot later — the release object is long gone by then, so without persisting them
    /// the "what's new" screen has nothing real to display.
    /// </summary>
    public const string UpdateNotesJson = "software.notes_json";

    /// <summary>Marks first-run setup as complete.</summary>
    public const string SetupComplete = "setup.complete";

    /// <summary>Hashed parent PIN. Never stored in plaintext.</summary>
    public const string ParentPinHash = "parental.pin_hash";

    /// <summary>Per-console random salt for the PIN hash.</summary>
    public const string ParentPinSalt = "parental.pin_salt";
}
