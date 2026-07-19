using ConsoleSupervisor;

Console.WriteLine("ConsoleSupervisor starting...");

var baseDir = AppContext.BaseDirectory;

var modules = new List<ManagedModule>
{
    new()
    {
        Name = "UI",
        ExecutablePath = Path.Combine(baseDir, "stubs", "UI.exe"),
        IsGameOrUiSurface = true,
    },
    new()
    {
        Name = "Input",
        ExecutablePath = Path.Combine(baseDir, "stubs", "Input.exe"),
    },
    new()
    {
        Name = "CEC",
        ExecutablePath = Path.Combine(baseDir, "stubs", "CEC.exe"),
    },
    new()
    {
        Name = "Monitor",
        ExecutablePath = Path.Combine(baseDir, "stubs", "Monitor.exe"),
    },
    new()
    {
        Name = "Hardware",
        ExecutablePath = Path.Combine(baseDir, "stubs", "Hardware.exe"),
    },
};

using var focusGuardian = new FocusGuardian();
focusGuardian.Start();

using var watchdog = new ModuleWatchdog(modules, new Win32ProcessLauncher());
watchdog.ModuleStarted += (name, pid) => Console.WriteLine($"[Watchdog] Started {name} (PID {pid}).");
watchdog.ModuleStartFailed += (name, error) => Console.WriteLine($"[Watchdog] Failed to start {name}: {error}");
watchdog.Start();

Console.WriteLine("ConsoleSupervisor running. Press Ctrl+C to exit.");

var exitSignal = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exitSignal.Set();
};

exitSignal.Wait();
Console.WriteLine("ConsoleSupervisor shutting down.");
