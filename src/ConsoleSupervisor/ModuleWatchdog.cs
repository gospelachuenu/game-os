using System.Timers;
using Timer = System.Timers.Timer;

namespace ConsoleSupervisor;

public sealed class ModuleWatchdog : IDisposable
{
    private const int PollIntervalMs = 500;

    private readonly IReadOnlyList<ManagedModule> _modules;
    private readonly IProcessLauncher _launcher;
    private readonly Dictionary<string, IProcessHandle> _running = new();
    private readonly Timer _timer;

    public event Action<string, int>? ModuleStarted;
    public event Action<string, string>? ModuleStartFailed;

    public ModuleWatchdog(IReadOnlyList<ManagedModule> modules, IProcessLauncher launcher)
    {
        _modules = modules;
        _launcher = launcher;
        _timer = new Timer(PollIntervalMs);
        _timer.Elapsed += (_, _) => Tick();
        _timer.AutoReset = true;
    }

    public void Start()
    {
        Tick();
        _timer.Start();
    }

    /// <summary>
    /// Evaluates every managed module and (re)launches any that are not currently alive.
    /// Public and side-effect-isolated via IProcessLauncher so it can be driven directly by tests.
    /// </summary>
    public void Tick()
    {
        foreach (var module in _modules)
        {
            if (!IsAlive(module))
            {
                LaunchModule(module);
            }
        }
    }

    private bool IsAlive(ManagedModule module)
    {
        return _running.TryGetValue(module.Name, out var handle) && !handle.HasExited;
    }

    private void LaunchModule(ManagedModule module)
    {
        try
        {
            var handle = _launcher.Start(module);
            if (handle is not null)
            {
                _running[module.Name] = handle;
                ModuleStarted?.Invoke(module.Name, handle.Id);
            }
        }
        catch (Exception ex)
        {
            ModuleStartFailed?.Invoke(module.Name, ex.Message);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
