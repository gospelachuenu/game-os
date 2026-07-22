namespace MaintenanceHub;

/// <summary>Progress of an in-flight update download.</summary>
public sealed class DownloadProgress
{
    public required long BytesReceived { get; init; }
    public required long TotalBytes { get; init; }

    public int PercentComplete => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesReceived * 100 / TotalBytes, 0, 100);

    public string Label => TotalBytes <= 0
        ? $"{BytesReceived / 1024.0 / 1024.0:0.#} MB"
        : $"{BytesReceived / 1024.0 / 1024.0:0.#} MB of {TotalBytes / 1024.0 / 1024.0:0.#} MB";
}

/// <summary>
/// Downloads console update packages in the background.
///
/// RESUMABLE BY DESIGN. The download starts during boot and continues while the child
/// uses the dashboard, so it is routinely interrupted — the console gets switched off
/// mid-transfer. Restarting a 78 MB download from zero every time the machine is
/// turned off would mean it never completes on a console that gets short sessions,
/// which is exactly how a child's console gets used.
///
/// Resume works by keeping the partial file and asking the server to continue from
/// where it stopped (HTTP Range). Servers that do not support ranges are handled by
/// starting again rather than producing a corrupt file.
/// </summary>
public interface IUpdateDownloader
{
    /// <summary>
    /// Downloads a release to <paramref name="destinationPath"/>, resuming a partial
    /// file if one is present. Returns true on success.
    ///
    /// Never throws for network reasons: a failed download is a normal event to be
    /// retried on the next boot, not an error worth surfacing to a child.
    /// </summary>
    Task<bool> DownloadAsync(
        SoftwareRelease release,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// HTTP implementation with range-request resume.
/// </summary>
public sealed class HttpUpdateDownloader : IUpdateDownloader
{
    private readonly HttpClient _http;

    /// <summary>Copy buffer. 80 KB is large enough to keep syscalls down without holding much memory.</summary>
    private const int BufferSize = 81920;

    public HttpUpdateDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public async Task<bool> DownloadAsync(
        SoftwareRelease release,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            // Download to a .part file and only rename on success, so a partial
            // transfer can never be mistaken for a complete package.
            var partPath = destinationPath + ".part";
            var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

            // Already have the whole thing from a previous run.
            if (existing > 0 && existing == release.SizeBytes)
            {
                File.Move(partPath, destinationPath, overwrite: true);
                return true;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, release.PackageUrl);

            if (existing > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            }

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            // A server that ignores the Range header answers 200 with the WHOLE file
            // rather than 206 with the remainder. Appending that to an existing partial
            // would produce a corrupt package, so start over instead.
            var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (existing > 0 && !resuming)
            {
                existing = 0;
            }

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var total = release.SizeBytes > 0
                ? release.SizeBytes
                : (response.Content.Headers.ContentLength ?? 0) + existing;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(
                partPath,
                resuming ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            var buffer = new byte[BufferSize];
            var received = existing;
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;

                progress?.Report(new DownloadProgress
                {
                    BytesReceived = received,
                    TotalBytes = total,
                });
            }

            await target.FlushAsync(ct);
            await target.DisposeAsync();

            File.Move(partPath, destinationPath, overwrite: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Console shutting down, or the caller gave up. The .part file stays put so
            // the next boot resumes rather than starting again.
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// Simulated downloader for development and tests: reports progress on a timer and
/// writes a placeholder file, with no network involved.
/// </summary>
public sealed class SimulatedUpdateDownloader : IUpdateDownloader
{
    private readonly TimeSpan _duration;
    private readonly bool _succeeds;

    public SimulatedUpdateDownloader(TimeSpan? duration = null, bool succeeds = true)
    {
        _duration = duration ?? TimeSpan.FromSeconds(6);
        _succeeds = succeeds;
    }

    public async Task<bool> DownloadAsync(
        SoftwareRelease release,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        const int steps = 40;
        var interval = _duration / steps;

        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(interval, ct);

            progress?.Report(new DownloadProgress
            {
                BytesReceived = release.SizeBytes * i / steps,
                TotalBytes = release.SizeBytes,
            });
        }

        if (!_succeeds)
        {
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(destinationPath, $"simulated package {release.Version}", ct);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
