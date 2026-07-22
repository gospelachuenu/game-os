namespace MaintenanceHub;

/// <summary>
/// A console software release, as advertised by the update source.
/// </summary>
public sealed class SoftwareRelease
{
    /// <summary>Semantic version, e.g. "1.4.0".</summary>
    public required string Version { get; init; }

    /// <summary>Where the package can be downloaded from.</summary>
    public required string PackageUrl { get; init; }

    /// <summary>Package size in bytes, for progress reporting.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Patch notes, shipped WITH the release rather than fetched separately: an update
    /// that installs but whose notes fail to load would show an empty screen.
    /// </summary>
    public IReadOnlyList<PatchNoteBlock> Notes { get; init; } = Array.Empty<PatchNoteBlock>();

    public string SizeLabel => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024.0 / 1024.0:0.#} MB"
        : $"{SizeBytes / 1024.0:0} KB";
}

public enum PatchNoteKind
{
    New,
    Improved,
    Fixed,
    Image,

    /// <summary>
    /// A short, muted, auto-looping clip shown in place of a screenshot — for changes
    /// better shown moving than still (an animation, an interaction). Same slot and the
    /// same package-relative path rules as Image; the renderer plays it silently on a
    /// loop rather than displaying a frame.
    /// </summary>
    Video,
}

/// <summary>
/// One entry in a set of patch notes. An ordered list of these replaces what used to
/// be a single string, so that images can sit between entries.
/// </summary>
public sealed class PatchNoteBlock
{
    public required PatchNoteKind Kind { get; init; }

    /// <summary>Text entries: the sentence. Image entries: the caption.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Image entries only. A path RELATIVE to the update package — never a URL, and
    /// never absolute. Patch notes are content arriving from outside the console, so
    /// the path is untrusted input and must be resolved strictly inside the package
    /// directory.
    /// </summary>
    public string? ImagePath { get; init; }
}

/// <summary>
/// Where the console looks for its own updates.
///
/// The real implementation fetches a small JSON manifest over HTTPS. It is behind a
/// seam because this is the first thing in the console that can block startup on a
/// machine outside the house: the boot-time check must be able to fail without taking
/// the console with it, and that failure has to be testable.
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// Returns the newest available release, or null when there is nothing newer than
    /// <paramref name="currentVersion"/>.
    ///
    /// Implementations must respect the cancellation token: the boot-time check runs
    /// with a short timeout and gives up rather than delaying startup.
    /// </summary>
    Task<SoftwareRelease?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default);
}

/// <summary>
/// Stand-in update source for development and tests. Returns a canned release so the
/// whole update path can be exercised without a server existing.
/// </summary>
public sealed class SimulatedUpdateSource : IUpdateSource
{
    private readonly SoftwareRelease? _release;
    private readonly TimeSpan _delay;

    /// <summary>
    /// A release with a mixture of illustrated and unillustrated changes, which is the
    /// case the notes screens have to handle: Spotlight for the ones with images, then
    /// a closing grouped list for everything else.
    /// </summary>
    public static SoftwareRelease SampleRelease { get; } = new()
    {
        Version = "1.4.0",
        PackageUrl = "https://example.invalid/gamingos/1.4.0.zip",
        SizeBytes = 78 * 1024 * 1024,
        Notes = new[]
        {
            new PatchNoteBlock { Kind = PatchNoteKind.New, Text = "Games can now be removed from the console without opening a launcher." },
            new PatchNoteBlock { Kind = PatchNoteKind.Image, ImagePath = "remove-game.png", Text = "The new Remove option on a game's details page" },
            new PatchNoteBlock { Kind = PatchNoteKind.New, Text = "Your controller's battery level now shows in the top bar." },
            new PatchNoteBlock { Kind = PatchNoteKind.Improved, Text = "The library scans about twice as fast on startup." },
            new PatchNoteBlock { Kind = PatchNoteKind.Fixed, Text = "The console no longer forgets which game you last played after a restart." },
            new PatchNoteBlock { Kind = PatchNoteKind.Fixed, Text = "Cover art no longer goes missing for some Epic games." },
        },
    };

    /// <param name="release">The release to advertise, or null to simulate "up to date".</param>
    /// <param name="delay">Artificial latency, so the boot step's timeout can be exercised.</param>
    public SimulatedUpdateSource(SoftwareRelease? release = null, TimeSpan? delay = null)
    {
        _release = release ?? SampleRelease;
        _delay = delay ?? TimeSpan.FromMilliseconds(150);
    }

    public async Task<SoftwareRelease?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct);

        if (_release is null)
        {
            return null;
        }

        return SoftwareVersion.IsNewer(_release.Version, currentVersion) ? _release : null;
    }
}

/// <summary>
/// Version comparison for console software versions.
///
/// Deliberately not string comparison: "1.10.0" sorts BEFORE "1.9.0" alphabetically,
/// which would make the console refuse a genuine update and — worse — do so silently.
/// </summary>
public static class SoftwareVersion
{
    /// <summary>True when <paramref name="candidate"/> is a strictly newer version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParse(candidate, out var a) || !TryParse(current, out var b))
        {
            // An unparseable version is not a reason to install something. Refusing is
            // the safe direction: the worst case is a missed update, not a bad one.
            return false;
        }

        return a.CompareTo(b) > 0;
    }

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Version.TryParse rejects a bare "1.4", so normalise to three components.
        var parts = text.Trim().Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var normalised = parts.Length == 2 ? $"{text.Trim()}.0" : text.Trim();
        return Version.TryParse(normalised, out version!);
    }
}
