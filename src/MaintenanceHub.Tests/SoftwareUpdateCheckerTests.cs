using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class SoftwareVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.3.2", true)]
    [InlineData("1.3.2", "1.4.0", false)]
    [InlineData("1.4.0", "1.4.0", false)]
    [InlineData("2.0.0", "1.99.99", true)]
    public void IsNewer_ComparesNumerically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, SoftwareVersion.IsNewer(candidate, current));
    }

    [Fact]
    public void IsNewer_HandlesDoubleDigitComponents()
    {
        // The reason this is not string comparison: "1.10.0" sorts BEFORE "1.9.0"
        // alphabetically, so a string compare would silently refuse a real update.
        Assert.True(SoftwareVersion.IsNewer("1.10.0", "1.9.0"));
        Assert.False(SoftwareVersion.IsNewer("1.9.0", "1.10.0"));
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2.3.4.5")]
    public void IsNewer_RefusesUnparseableVersions(string? candidate)
    {
        // Refusing is the safe direction: a missed update beats installing something
        // whose version cannot be established.
        Assert.False(SoftwareVersion.IsNewer(candidate!, "1.0.0"));
    }
}

public class SoftwareUpdateCheckerTests
{
    private static SoftwareUpdateChecker Build(
        IUpdateSource? source = null,
        IConsoleStateStore? state = null)
        => new(source ?? new SimulatedUpdateSource(), state ?? new InMemoryConsoleStateStore());

    [Fact]
    public async Task Check_ReportsUpdateAvailable_WhenSourceHasNewerRelease()
    {
        var result = await Build().CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal("1.4.0", result.Release!.Version);
    }

    [Fact]
    public async Task Check_ReportsUpToDate_WhenInstalledVersionIsCurrent()
    {
        var state = new InMemoryConsoleStateStore();
        state.Set(ConsoleStateKeys.InstalledVersion, "1.4.0");

        var result = await Build(state: state).CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
    }

    [Fact]
    public async Task Check_ReportsReadyToInstall_WhenAnUpdateWasAlreadyDownloaded()
    {
        var state = new InMemoryConsoleStateStore();
        state.Set(ConsoleStateKeys.PendingVersion, "1.4.0");
        state.Set(ConsoleStateKeys.PendingPackagePath, @"C:\GamingOS\State\1.4.0.zip");

        var result = await Build(state: state).CheckAsync();

        Assert.Equal(UpdateCheckOutcome.ReadyToInstall, result.Outcome);
        Assert.Equal(@"C:\GamingOS\State\1.4.0.zip", result.PendingPackagePath);
    }

    [Fact]
    public async Task Check_FailsGracefully_WhenTheSourceThrows()
    {
        // A console that will not start because a server is down is a worse failure
        // than one that checks again tomorrow.
        var result = await Build(new ThrowingUpdateSource()).CheckAsync();

        Assert.Equal(UpdateCheckOutcome.CheckFailed, result.Outcome);
    }

    [Fact]
    public async Task Check_FailsGracefully_WhenTheSourceHangsPastTheTimeout()
    {
        var slow = new SimulatedUpdateSource(
            SimulatedUpdateSource.SampleRelease,
            delay: SoftwareUpdateChecker.CheckTimeout + TimeSpan.FromSeconds(2));

        var result = await Build(slow).CheckAsync();

        Assert.Equal(UpdateCheckOutcome.CheckFailed, result.Outcome);
    }

    [Fact]
    public void RecordInstalling_CapturesPreviousVersionAndOwesNotes()
    {
        var state = new InMemoryConsoleStateStore();
        state.Set(ConsoleStateKeys.InstalledVersion, "1.3.2");
        var checker = Build(state: state);

        checker.RecordInstalling("1.4.0");

        Assert.Equal("1.4.0", checker.InstalledVersion);
        Assert.Equal("1.3.2", checker.PreviousVersion);
        Assert.True(checker.HasUnseenUpdateNotes);
    }

    [Fact]
    public void RecordInstalling_ClearsThePendingDownload()
    {
        var state = new InMemoryConsoleStateStore();
        state.Set(ConsoleStateKeys.PendingVersion, "1.4.0");
        state.Set(ConsoleStateKeys.PendingPackagePath, "pkg.zip");

        Build(state: state).RecordInstalling("1.4.0");

        Assert.Null(state.Get(ConsoleStateKeys.PendingVersion));
        Assert.Null(state.Get(ConsoleStateKeys.PendingPackagePath));
    }

    [Fact]
    public void MarkNotesSeen_ShowsNotesExactlyOncePerVersion()
    {
        var checker = Build();
        checker.RecordInstalling("1.4.0");
        Assert.True(checker.HasUnseenUpdateNotes);

        checker.MarkNotesSeen();

        Assert.False(checker.HasUnseenUpdateNotes);
    }

    private sealed class ThrowingUpdateSource : IUpdateSource
    {
        public Task<SoftwareRelease?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default)
            => throw new HttpRequestException("no network");
    }
}

public class FileConsoleStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamingos-state-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Values_SurviveANewStoreInstance()
    {
        // The whole point of this class: state written now must still be there after a
        // reboot, which a fresh instance stands in for.
        new FileConsoleStateStore(_dir).Set(ConsoleStateKeys.ParentPinHash, "abc123");

        Assert.Equal("abc123", new FileConsoleStateStore(_dir).Get(ConsoleStateKeys.ParentPinHash));
    }

    [Fact]
    public void Get_ReturnsNull_ForUnsetKeys()
    {
        Assert.Null(new FileConsoleStateStore(_dir).Get("never.set"));
    }

    [Fact]
    public void Remove_DeletesAValue()
    {
        var store = new FileConsoleStateStore(_dir);
        store.Set("k", "v");
        store.Remove("k");

        Assert.Null(new FileConsoleStateStore(_dir).Get("k"));
    }

    [Fact]
    public void Set_OverwritesAnExistingValue()
    {
        var store = new FileConsoleStateStore(_dir);
        store.Set("k", "first");
        store.Set("k", "second");

        Assert.Equal("second", new FileConsoleStateStore(_dir).Get("k"));
    }

    [Fact]
    public void CorruptStateFile_DoesNotThrow()
    {
        // A corrupt state file must not stop the console booting. Losing the PIN is
        // recoverable; refusing to start is not.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "console-state.json"), "{ this is not json");

        var store = new FileConsoleStateStore(_dir);

        Assert.Null(store.Get("anything"));
        store.Set("k", "v");
        Assert.Equal("v", store.Get("k"));
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
