namespace MaintenanceHub;

/// <summary>
/// Coordinates the console's self-update: check at boot, download in the background,
/// install on a later boot.
///
/// This is the single entry point the UI talks to. Splitting check from download
/// matters here — the check is awaited during boot because it is fast and bounded, the
/// download is fired and forgotten because it is neither.
/// </summary>
public sealed class SoftwareUpdateService
{
    private readonly SoftwareUpdateChecker _checker;
    private readonly IUpdateDownloader _downloader;
    private readonly IConsoleStateStore _state;
    private readonly string _downloadDirectory;

    private CancellationTokenSource? _downloadCts;

    /// <summary>Raised as a background download progresses. Fires on a background thread.</summary>
    public event Action<DownloadProgress>? DownloadProgressed;

    /// <summary>Raised when a background download finishes, successfully or otherwise.</summary>
    public event Action<bool>? DownloadCompleted;

    public SoftwareUpdateService(
        IUpdateSource source,
        IUpdateDownloader downloader,
        IConsoleStateStore state,
        string? downloadDirectory = null)
    {
        _checker = new SoftwareUpdateChecker(source, state);
        _downloader = downloader;
        _state = state;
        _downloadDirectory = downloadDirectory ?? Path.Combine(FileConsoleStateStore.DefaultDirectory, "Updates");
    }

    public string InstalledVersion => _checker.InstalledVersion;
    public string? PreviousVersion => _checker.PreviousVersion;

    /// <summary>
    /// The version downloaded and waiting to install. Read in Boot B of the UWF dance to
    /// stamp the install once the swap is actually happening — not before, on Boot A,
    /// where recording it would lie if the swap then failed.
    /// </summary>
    public string? PendingVersion => _checker.PendingVersion;

    /// <summary>
    /// The downloaded package waiting to be installed, or null if none is ready. Set when
    /// a download completes; read by the installer to know what to unpack.
    /// </summary>
    public string? PendingPackagePath => LastResult?.PendingPackagePath
        ?? _state.Get(ConsoleStateKeys.PendingPackagePath);

    /// <summary>True when an update was installed and its notes have not been shown yet.</summary>
    public bool HasUnseenUpdateNotes => _checker.HasUnseenUpdateNotes;

    /// <summary>
    /// The patch notes for the just-installed update — the real ones from its manifest,
    /// saved at download and restored here. Empty if the update carried none.
    /// </summary>
    public IReadOnlyList<PatchNoteBlock> InstalledNotes => _checker.InstalledNotes;

    /// <summary>The most recent check's result, or null if no check has run this session.</summary>
    public UpdateCheckResult? LastResult { get; private set; }

    /// <summary>True while a background download is in flight.</summary>
    public bool IsDownloading => _downloadCts is not null;

    /// <summary>
    /// The boot-time check. Bounded by SoftwareUpdateChecker.CheckTimeout and never
    /// throws, so it is safe to await during startup.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        LastResult = await _checker.CheckAsync(ct);
        return LastResult;
    }

    /// <summary>
    /// Starts downloading in the background and returns immediately.
    ///
    /// Deliberately NOT awaited by the boot sequence. A 78 MB package on a slow line
    /// would otherwise hold the console at its logo screen; instead boot continues to
    /// the dashboard and the transfer carries on behind it.
    /// </summary>
    public void StartBackgroundDownload(SoftwareRelease release)
    {
        if (IsDownloading)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _downloadCts = cts;

        var path = Path.Combine(_downloadDirectory, $"{release.Version}.pkg");

        _ = Task.Run(async () =>
        {
            var progress = new Progress<DownloadProgress>(p => DownloadProgressed?.Invoke(p));
            var ok = await _downloader.DownloadAsync(release, path, progress, cts.Token);

            if (ok)
            {
                // Recorded only on success, so a partial transfer is never offered for
                // install. An interrupted download resumes from its .part file on the
                // next boot instead.
                _checker.RecordDownloaded(release, path);
            }

            _downloadCts = null;
            DownloadCompleted?.Invoke(ok);
        }, cts.Token);
    }

    /// <summary>
    /// Stops an in-flight download, leaving the partial file in place to resume from.
    /// Called when the console is shutting down.
    /// </summary>
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
        _downloadCts = null;
    }

    /// <summary>
    /// Records that an update is being applied. Called immediately BEFORE the installer
    /// runs, so that a power cut mid-install still leaves the console knowing which
    /// version it came from.
    /// </summary>
    public void BeginInstall(string version) => _checker.RecordInstalling(version);

    /// <summary>Marks the patch notes as seen, so they appear exactly once per version.</summary>
    public void MarkNotesSeen() => _checker.MarkNotesSeen();

    /// <summary>Discards a downloaded package that turned out to be unusable.</summary>
    public void DiscardPending() => _checker.DiscardPending();
}
