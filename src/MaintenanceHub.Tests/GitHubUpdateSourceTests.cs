using System.Net;
using System.Net.Http;
using System.Text;
using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class GitHubUpdateSourceTests
{
    /// <summary>
    /// A stand-in HTTP handler that returns a fixed response, so the source can be tested
    /// against exact manifest content without a network or a server.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Func<HttpRequestMessage, Task>? _onRequest;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK,
            Func<HttpRequestMessage, Task>? onRequest = null)
        {
            _body = body;
            _status = status;
            _onRequest = onRequest;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_onRequest is not null)
            {
                await _onRequest(request);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static GitHubUpdateSource Source(string body,
        HttpStatusCode status = HttpStatusCode.OK,
        Func<HttpRequestMessage, Task>? onRequest = null)
        => new("https://example.test/version.json",
               new HttpClient(new StubHandler(body, status, onRequest)));

    [Fact]
    public async Task ReturnsRelease_WhenManifestIsNewer()
    {
        var source = Source("""
            { "version": "1.5.0", "packageUrl": "https://example.test/UI-1.5.0.zip",
              "sizeBytes": 12345 }
            """);

        var release = await source.CheckForUpdateAsync("1.4.0");

        Assert.NotNull(release);
        Assert.Equal("1.5.0", release!.Version);
        Assert.Equal("https://example.test/UI-1.5.0.zip", release.PackageUrl);
        Assert.Equal(12345, release.SizeBytes);
    }

    [Fact]
    public async Task ReturnsNull_WhenManifestIsSameVersion()
    {
        var source = Source("""
            { "version": "1.4.0", "packageUrl": "https://example.test/x.zip" }
            """);

        Assert.Null(await source.CheckForUpdateAsync("1.4.0"));
    }

    [Fact]
    public async Task ReturnsNull_WhenManifestIsOlder()
    {
        var source = Source("""
            { "version": "1.2.0", "packageUrl": "https://example.test/x.zip" }
            """);

        Assert.Null(await source.CheckForUpdateAsync("1.4.0"));
    }

    [Fact]
    public async Task ComparesNumerically_NotAlphabetically()
    {
        // 1.10.0 is newer than 1.9.0, though a string compare would say otherwise.
        var source = Source("""
            { "version": "1.10.0", "packageUrl": "https://example.test/x.zip" }
            """);

        Assert.NotNull(await source.CheckForUpdateAsync("1.9.0"));
    }

    [Fact]
    public async Task ReturnsNull_OnMalformedJson()
    {
        // A garbled manifest must not throw at boot — it resolves to "no update".
        var source = Source("this is not json {");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0"));
    }

    [Fact]
    public async Task ReturnsNull_OnHttpError()
    {
        var source = Source("{}", HttpStatusCode.NotFound);

        Assert.Null(await source.CheckForUpdateAsync("1.0.0"));
    }

    [Fact]
    public async Task ReturnsNull_WhenPackageUrlMissing()
    {
        // A version with nowhere to download from is unusable, so it is no update at all.
        var source = Source("""{ "version": "9.9.9" }""");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0"));
    }

    [Fact]
    public async Task MapsPatchNotes_IncludingImages()
    {
        var source = Source("""
            {
              "version": "1.5.0",
              "packageUrl": "https://example.test/x.zip",
              "notes": [
                { "kind": "New", "text": "Music now plays in the background" },
                { "kind": "Fixed", "text": "Controller no longer drops on YouTube" },
                { "kind": "Image", "text": "The new app drawer", "image": "shots/drawer.png" }
              ]
            }
            """);

        var release = await source.CheckForUpdateAsync("1.4.0");

        Assert.NotNull(release);
        Assert.Equal(3, release!.Notes.Count);
        Assert.Equal(PatchNoteKind.New, release.Notes[0].Kind);
        Assert.Equal(PatchNoteKind.Image, release.Notes[2].Kind);
        Assert.Equal("shots/drawer.png", release.Notes[2].ImagePath);
    }

    [Fact]
    public async Task SkipsUnknownNoteKinds_RatherThanFailing()
    {
        // A newer manifest format must degrade on an older console, not crash it.
        var source = Source("""
            {
              "version": "1.5.0",
              "packageUrl": "https://example.test/x.zip",
              "notes": [
                { "kind": "Sparkle", "text": "from the future" },
                { "kind": "Fixed", "text": "a real one" }
              ]
            }
            """);

        var release = await source.CheckForUpdateAsync("1.4.0");

        Assert.NotNull(release);
        Assert.Single(release!.Notes);
        Assert.Equal(PatchNoteKind.Fixed, release.Notes[0].Kind);
    }

    [Fact]
    public async Task RethrowsOnBootTimeout_SoCheckerRecordsFailure()
    {
        // The boot check cancels via its own token. That must surface as a cancellation,
        // not be swallowed into "up to date", or a timed-out check would look successful.
        var slow = new HttpClient(new StubHandler("{}", onRequest: async _ =>
            await Task.Delay(TimeSpan.FromSeconds(5))));
        var source = new GitHubUpdateSource("https://example.test/version.json", slow);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.CheckForUpdateAsync("1.0.0", cts.Token));
    }

    [Fact]
    public async Task AppendsCacheBuster_SoStaleManifestsAreNotReused()
    {
        string? requested = null;
        var source = Source(
            """{ "version": "1.0.0", "packageUrl": "https://example.test/x.zip" }""",
            onRequest: req =>
            {
                requested = req.RequestUri!.ToString();
                return Task.CompletedTask;
            });

        await source.CheckForUpdateAsync("1.0.0");

        Assert.NotNull(requested);
        Assert.Contains("t=", requested);
    }
}
