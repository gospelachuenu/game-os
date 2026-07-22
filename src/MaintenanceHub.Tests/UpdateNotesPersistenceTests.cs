using MaintenanceHub;

namespace MaintenanceHub.Tests;

/// <summary>
/// Patch notes arrive with the download but are shown a boot later, after the install, so
/// they have to survive being written to the state store and read back. These verify that
/// round-trip end to end — the reason it matters is that an update's real "what's new"
/// screen would otherwise fall back to placeholder text.
/// </summary>
public class UpdateNotesPersistenceTests
{
    /// <summary>An in-memory state store, standing in for the file one.</summary>
    private sealed class MemoryState : IConsoleStateStore
    {
        private readonly Dictionary<string, string> _values = new();
        public bool IsAvailable => true;
        public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }

    private static SoftwareRelease ReleaseWithNotes() => new()
    {
        Version = "1.1.0",
        PackageUrl = "https://example.test/x.zip",
        SizeBytes = 100,
        Notes = new[]
        {
            new PatchNoteBlock { Kind = PatchNoteKind.New, Text = "Background music" },
            new PatchNoteBlock { Kind = PatchNoteKind.Fixed, Text = "Sites no longer look like a TV" },
            new PatchNoteBlock { Kind = PatchNoteKind.Image, Text = "The app drawer", ImagePath = "shots/drawer.png" },
        },
    };

    [Fact]
    public void Notes_SurviveDownloadThenInstall()
    {
        var state = new MemoryState();
        var checker = new SoftwareUpdateChecker(new SimulatedUpdateSource(), state);

        // Download saves the notes; install a boot later must still be able to read them.
        checker.RecordDownloaded(ReleaseWithNotes(), "C:\\pkg\\1.1.0.pkg");
        checker.RecordInstalling("1.1.0");

        var restored = checker.InstalledNotes;

        Assert.Equal(3, restored.Count);
        Assert.Equal(PatchNoteKind.New, restored[0].Kind);
        Assert.Equal("Background music", restored[0].Text);
        Assert.Equal(PatchNoteKind.Image, restored[2].Kind);
        Assert.Equal("shots/drawer.png", restored[2].ImagePath);
    }

    [Fact]
    public void InstalledNotes_EmptyWhenNoneStored()
    {
        var state = new MemoryState();
        var checker = new SoftwareUpdateChecker(new SimulatedUpdateSource(), state);

        Assert.Empty(checker.InstalledNotes);
    }

    [Fact]
    public void InstalledNotes_EmptyOnCorruptJson()
    {
        var state = new MemoryState();
        state.Set(ConsoleStateKeys.UpdateNotesJson, "not valid json [");
        var checker = new SoftwareUpdateChecker(new SimulatedUpdateSource(), state);

        // A garbled note store must not throw at boot — it degrades to no notes.
        Assert.Empty(checker.InstalledNotes);
    }
}
