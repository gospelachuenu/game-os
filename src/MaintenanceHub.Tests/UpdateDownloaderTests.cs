using System.Net;
using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void PercentComplete_IsZero_WhenTotalIsUnknown()
    {
        // A server that sends no Content-Length leaves TotalBytes at 0. Dividing by it
        // would throw; reporting 0% is the honest answer when the total is unknown.
        var p = new DownloadProgress { BytesReceived = 5_000, TotalBytes = 0 };

        Assert.Equal(0, p.PercentComplete);
    }

    [Fact]
    public void PercentComplete_IsClamped()
    {
        var p = new DownloadProgress { BytesReceived = 200, TotalBytes = 100 };

        Assert.Equal(100, p.PercentComplete);
    }
}

public class SoftwareUpdateServiceTests
{
    private static SoftwareRelease Release => new()
    {
        Version = "1.4.0",
        PackageUrl = "https://example.invalid/pkg",
        SizeBytes = 1024,
    };

    [Fact]
    public async Task Download_RecordsPendingUpdate_OnSuccess()
    {
        var state = new InMemoryConsoleStateStore();
        var dir = NewTempDir();

        try
        {
            var service = new SoftwareUpdateService(
                new SimulatedUpdateSource(),
                new SimulatedUpdateDownloader(TimeSpan.FromMilliseconds(50)),
                state,
                dir);

            var done = new TaskCompletionSource<bool>();
            service.DownloadCompleted += ok => done.TrySetResult(ok);

            service.StartBackgroundDownload(Release);

            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("1.4.0", state.Get(ConsoleStateKeys.PendingVersion));
            Assert.NotNull(state.Get(ConsoleStateKeys.PendingPackagePath));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task Download_RecordsNothing_OnFailure()
    {
        // A partial or failed transfer must never be offered for install.
        var state = new InMemoryConsoleStateStore();
        var dir = NewTempDir();

        try
        {
            var service = new SoftwareUpdateService(
                new SimulatedUpdateSource(),
                new SimulatedUpdateDownloader(TimeSpan.FromMilliseconds(50), succeeds: false),
                state,
                dir);

            var done = new TaskCompletionSource<bool>();
            service.DownloadCompleted += ok => done.TrySetResult(ok);

            service.StartBackgroundDownload(Release);

            Assert.False(await done.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(state.Get(ConsoleStateKeys.PendingVersion));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task Download_ReportsProgress()
    {
        var dir = NewTempDir();

        try
        {
            var service = new SoftwareUpdateService(
                new SimulatedUpdateSource(),
                new SimulatedUpdateDownloader(TimeSpan.FromMilliseconds(100)),
                new InMemoryConsoleStateStore(),
                dir);

            var seen = new List<int>();
            service.DownloadProgressed += p => { lock (seen) seen.Add(p.PercentComplete); };

            var done = new TaskCompletionSource<bool>();
            service.DownloadCompleted += _ => done.TrySetResult(true);

            service.StartBackgroundDownload(Release);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

            lock (seen)
            {
                Assert.NotEmpty(seen);
                Assert.Equal(100, seen[^1]);
            }
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void StartBackgroundDownload_IsIgnored_WhenOneIsAlreadyRunning()
    {
        var dir = NewTempDir();

        try
        {
            var service = new SoftwareUpdateService(
                new SimulatedUpdateSource(),
                new SimulatedUpdateDownloader(TimeSpan.FromSeconds(5)),
                new InMemoryConsoleStateStore(),
                dir);

            service.StartBackgroundDownload(Release);
            Assert.True(service.IsDownloading);

            // Second call must not start a competing transfer writing to the same file.
            service.StartBackgroundDownload(Release);
            Assert.True(service.IsDownloading);

            service.CancelDownload();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gamingos-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }
    }
}

public class HttpUpdateDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamingos-http-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Resumes_FromAPartialFile()
    {
        // The case this exists for: the console was switched off mid-download and the
        // transfer must continue rather than start over.
        Directory.CreateDirectory(_dir);
        var dest = Path.Combine(_dir, "pkg");
        await File.WriteAllBytesAsync(dest + ".part", new byte[400]);

        var handler = new FakeHandler(
            HttpStatusCode.PartialContent,
            content: new byte[600]);

        var downloader = new HttpUpdateDownloader(new HttpClient(handler));

        var ok = await downloader.DownloadAsync(
            new SoftwareRelease { Version = "1.4.0", PackageUrl = "https://example.invalid/pkg", SizeBytes = 1000 },
            dest);

        Assert.True(ok);
        Assert.Equal(1000, new FileInfo(dest).Length);
        Assert.Equal("bytes=400-", handler.LastRangeHeader);
    }

    [Fact]
    public async Task StartsOver_WhenTheServerIgnoresTheRangeRequest()
    {
        // A server answering 200 sends the WHOLE file. Appending that to an existing
        // partial would produce a corrupt package, so the partial must be discarded.
        Directory.CreateDirectory(_dir);
        var dest = Path.Combine(_dir, "pkg");
        await File.WriteAllBytesAsync(dest + ".part", new byte[400]);

        var handler = new FakeHandler(HttpStatusCode.OK, content: new byte[1000]);
        var downloader = new HttpUpdateDownloader(new HttpClient(handler));

        var ok = await downloader.DownloadAsync(
            new SoftwareRelease { Version = "1.4.0", PackageUrl = "https://example.invalid/pkg", SizeBytes = 1000 },
            dest);

        Assert.True(ok);
        Assert.Equal(1000, new FileInfo(dest).Length);
    }

    [Fact]
    public async Task ReturnsFalse_OnNetworkFailure()
    {
        var downloader = new HttpUpdateDownloader(new HttpClient(new ThrowingHandler()));

        var ok = await downloader.DownloadAsync(
            new SoftwareRelease { Version = "1.4.0", PackageUrl = "https://example.invalid/pkg", SizeBytes = 100 },
            Path.Combine(_dir, "pkg"));

        Assert.False(ok);
    }

    [Fact]
    public async Task LeavesNoFinalFile_WhenTheDownloadFails()
    {
        // A .part file is fine to leave behind - it is what resume uses. A file at the
        // FINAL path would be mistaken for a complete package.
        var dest = Path.Combine(_dir, "pkg");
        var downloader = new HttpUpdateDownloader(new HttpClient(new ThrowingHandler()));

        await downloader.DownloadAsync(
            new SoftwareRelease { Version = "1.4.0", PackageUrl = "https://example.invalid/pkg", SizeBytes = 100 },
            dest);

        Assert.False(File.Exists(dest));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _content;

        public string? LastRangeHeader { get; private set; }

        public FakeHandler(HttpStatusCode status, byte[] content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRangeHeader = request.Headers.Range?.ToString();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_content),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("no network");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }
    }
}
