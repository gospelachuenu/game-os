using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaintenanceHub;

/// <summary>
/// The real update source: a <c>version.json</c> manifest published on GitHub.
///
/// The console fetches one small JSON file, compares its version to what is installed,
/// and returns a release to download if it is newer. The manifest — not the GitHub
/// Releases API — is the contract, deliberately: it keeps the console's parsing to a
/// handful of fields we control, so a change to GitHub's own API shape cannot break the
/// update path, and the patch notes are authored rather than scraped from a release body.
///
/// Publishing a release is: build the app, attach the zip to a GitHub Release (or drop it
/// anywhere reachable over HTTPS), and update version.json to point at it. No console
/// rebuild, no per-machine step — every console picks it up at its next boot.
///
/// Every failure resolves to "no update" rather than throwing. This runs at boot on a
/// timeout, and a console that will not start because a manifest is malformed or a server
/// is down is a far worse outcome than one that checks again next time.
/// </summary>
public sealed class GitHubUpdateSource : IUpdateSource
{
    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    /// <param name="manifestUrl">
    /// The raw URL of version.json — e.g. a GitHub raw link or a release asset URL.
    /// </param>
    /// <param name="http">
    /// Shared HttpClient. Injected rather than created so the caller controls its
    /// lifetime and so tests can supply a fake handler.
    /// </param>
    public GitHubUpdateSource(string manifestUrl, HttpClient http)
    {
        _manifestUrl = manifestUrl;
        _http = http;
    }

    public async Task<SoftwareRelease?> CheckForUpdateAsync(
        string currentVersion, CancellationToken ct = default)
    {
        UpdateManifest? manifest;

        try
        {
            // A cache-buster: GitHub's raw endpoint and most CDNs cache aggressively, and
            // a console that keeps seeing a stale manifest would never notice a new
            // release. A changing query string sidesteps it without needing cache headers
            // we do not control.
            var url = _manifestUrl
                + (_manifestUrl.Contains('?') ? "&" : "?")
                + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The boot timeout fired. Rethrow so the checker records CheckFailed rather
            // than mistaking a timeout for "up to date".
            throw;
        }
        catch (Exception e) when (e is HttpRequestException
                                   or JsonException
                                   or System.IO.IOException
                                   or OperationCanceledException)
        {
            // Network down, DNS failure, garbage JSON, or a socket-level timeout that was
            // not our cancellation. None of it is worth failing a boot over.
            return null;
        }

        if (manifest?.Version is null || manifest.PackageUrl is null)
        {
            return null;
        }

        // The whole point of the check: only newer versions count. IsNewer compares
        // numerically, so 1.10.0 correctly beats 1.9.0.
        if (!SoftwareVersion.IsNewer(manifest.Version, currentVersion))
        {
            return null;
        }

        return new SoftwareRelease
        {
            Version = manifest.Version,
            PackageUrl = manifest.PackageUrl,
            SizeBytes = manifest.SizeBytes ?? 0,
            Notes = MapNotes(manifest.Notes),
        };
    }

    private static IReadOnlyList<PatchNoteBlock> MapNotes(List<ManifestNote>? notes)
    {
        if (notes is null || notes.Count == 0)
        {
            return Array.Empty<PatchNoteBlock>();
        }

        var blocks = new List<PatchNoteBlock>(notes.Count);

        foreach (var note in notes)
        {
            if (!TryParseKind(note.Kind, out var kind))
            {
                // An unknown note kind is skipped rather than allowed to break the whole
                // set — a newer manifest format must degrade, not crash an older console.
                continue;
            }

            blocks.Add(new PatchNoteBlock
            {
                Kind = kind,
                Text = note.Text,
                ImagePath = note.Image,
            });
        }

        return blocks;
    }

    private static bool TryParseKind(string? raw, out PatchNoteKind kind)
    {
        kind = PatchNoteKind.New;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return Enum.TryParse(raw, ignoreCase: true, out kind);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// The shape of version.json. Kept minimal and separate from <see cref="SoftwareRelease"/>
    /// so the on-the-wire format and the console's internal model can evolve independently.
    /// </summary>
    private sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("packageUrl")] public string? PackageUrl { get; set; }
        [JsonPropertyName("sizeBytes")] public long? SizeBytes { get; set; }
        [JsonPropertyName("notes")] public List<ManifestNote>? Notes { get; set; }
    }

    private sealed class ManifestNote
    {
        /// <summary>"New", "Improved", "Fixed", "Image", or "Video" — case-insensitive.</summary>
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }

        /// <summary>Image/Video entries: a path RELATIVE to the update package.</summary>
        [JsonPropertyName("image")] public string? Image { get; set; }
    }
}
