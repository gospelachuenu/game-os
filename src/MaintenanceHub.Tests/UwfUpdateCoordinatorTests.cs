using MaintenanceHub;

namespace MaintenanceHub.Tests;

/// <summary>
/// The coordinator drives an update across the reboots UWF forces. These test the
/// stage bookkeeping — the part that must survive reboots and never strand a machine
/// mid-dance. The actual uwfmgr calls no-op on a machine without a write filter (the CI
/// box, the dev laptop), which is the path exercised here.
/// </summary>
public class UwfUpdateCoordinatorTests
{
    private sealed class MemoryState : IConsoleStateStore
    {
        private readonly Dictionary<string, string> _v = new();
        public bool IsAvailable => true;
        public string? Get(string key) => _v.TryGetValue(key, out var x) ? x : null;
        public void Set(string key, string value) => _v[key] = value;
        public void Remove(string key) => _v.Remove(key);
    }

    [Fact]
    public void StartsWithNoDanceInProgress()
    {
        var c = new UwfUpdateCoordinator(new MemoryState());
        Assert.Equal(UwfUpdateCoordinator.Stage.None, c.CurrentStage);
    }

    [Fact]
    public void BeginApply_RecordsResumeStage()
    {
        var state = new MemoryState();
        var c = new UwfUpdateCoordinator(state);

        c.BeginApply(@"C:\GamingOS\State");

        // The next boot must be able to see it should apply the swap — this is the marker
        // that has to survive the reboot in between.
        Assert.Equal(UwfUpdateCoordinator.Stage.ApplyAfterReboot, c.CurrentStage);
    }

    [Fact]
    public void BeginApply_ThenNewCoordinator_StillSeesStage()
    {
        // A fresh coordinator on the next boot reads the same store — the reboot is
        // stood in for by constructing a new instance over the same state.
        var state = new MemoryState();
        new UwfUpdateCoordinator(state).BeginApply(@"C:\GamingOS\State");

        var afterReboot = new UwfUpdateCoordinator(state);
        Assert.Equal(UwfUpdateCoordinator.Stage.ApplyAfterReboot, afterReboot.CurrentStage);
    }

    [Fact]
    public void FinishApply_ClearsTheStage()
    {
        var state = new MemoryState();
        var c = new UwfUpdateCoordinator(state);
        c.BeginApply(@"C:\GamingOS\State");

        c.FinishApply();

        // Cleared, or the dance would loop forever, re-applying every boot.
        Assert.Equal(UwfUpdateCoordinator.Stage.None, c.CurrentStage);
    }

    [Fact]
    public void Abort_ClearsTheStage()
    {
        var state = new MemoryState();
        var c = new UwfUpdateCoordinator(state);
        c.BeginApply(@"C:\GamingOS\State");

        c.Abort();

        Assert.Equal(UwfUpdateCoordinator.Stage.None, c.CurrentStage);
    }
}
