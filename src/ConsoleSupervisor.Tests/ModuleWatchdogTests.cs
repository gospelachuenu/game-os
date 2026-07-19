using ConsoleSupervisor;

namespace ConsoleSupervisor.Tests;

public class ModuleWatchdogTests
{
    private static ManagedModule MakeModule(string name) => new()
    {
        Name = name,
        ExecutablePath = $@"C:\fake\{name}.exe",
    };

    [Fact]
    public void Start_LaunchesEveryConfiguredModuleExactlyOnce()
    {
        var launcher = new FakeProcessLauncher();
        var modules = new List<ManagedModule> { MakeModule("UI"), MakeModule("Input") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        watchdog.Tick();

        Assert.Equal(new[] { "UI", "Input" }, launcher.LaunchedModuleNames);
    }

    [Fact]
    public void Tick_DoesNotRestartAModuleThatIsStillAlive()
    {
        var launcher = new FakeProcessLauncher();
        var modules = new List<ManagedModule> { MakeModule("UI") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        watchdog.Tick();
        watchdog.Tick();
        watchdog.Tick();

        Assert.Single(launcher.LaunchedModuleNames);
    }

    [Fact]
    public void Tick_RestartsAModuleAfterItExits()
    {
        var launcher = new FakeProcessLauncher();
        var modules = new List<ManagedModule> { MakeModule("UI") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        watchdog.Tick();
        launcher.HandlesByModule["UI"].HasExited = true;
        watchdog.Tick();

        Assert.Equal(2, launcher.LaunchedModuleNames.Count(name => name == "UI"));
    }

    [Fact]
    public void Tick_RaisesModuleStartedWithReportedProcessId()
    {
        var launcher = new FakeProcessLauncher();
        var modules = new List<ManagedModule> { MakeModule("UI") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        (string Name, int Pid)? captured = null;
        watchdog.ModuleStarted += (name, pid) => captured = (name, pid);

        watchdog.Tick();

        Assert.NotNull(captured);
        Assert.Equal("UI", captured!.Value.Name);
        Assert.Equal(launcher.HandlesByModule["UI"].Id, captured.Value.Pid);
    }

    [Fact]
    public void Tick_RaisesModuleStartFailedAndKeepsRetryingOnNextTick()
    {
        var launcher = new FakeProcessLauncher { FailNextStart = true };
        var modules = new List<ManagedModule> { MakeModule("UI") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        string? failedName = null;
        watchdog.ModuleStartFailed += (name, _) => failedName = name;

        watchdog.Tick();
        Assert.Equal("UI", failedName);
        Assert.False(launcher.HandlesByModule.ContainsKey("UI"));

        watchdog.Tick();
        Assert.True(launcher.HandlesByModule.ContainsKey("UI"));
    }

    [Fact]
    public void Tick_TreatsEachModuleIndependently_OneFailureDoesNotBlockOthers()
    {
        var launcher = new FakeProcessLauncher();
        var modules = new List<ManagedModule> { MakeModule("UI"), MakeModule("Input") };
        using var watchdog = new ModuleWatchdog(modules, launcher);

        launcher.FailNextStart = true;
        watchdog.Tick();

        Assert.False(launcher.HandlesByModule.ContainsKey("UI"));
        Assert.True(launcher.HandlesByModule.ContainsKey("Input"));
    }
}
