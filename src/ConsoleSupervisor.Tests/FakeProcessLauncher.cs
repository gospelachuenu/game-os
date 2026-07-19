using ConsoleSupervisor;

namespace ConsoleSupervisor.Tests;

internal sealed class FakeProcessHandle : IProcessHandle
{
    public int Id { get; init; }
    public bool HasExited { get; set; }
}

internal sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<string> LaunchedModuleNames { get; } = new();
    public Dictionary<string, FakeProcessHandle> HandlesByModule { get; } = new();

    private int _nextPid = 1000;
    public bool FailNextStart { get; set; }

    public IProcessHandle? Start(ManagedModule module)
    {
        LaunchedModuleNames.Add(module.Name);

        if (FailNextStart)
        {
            FailNextStart = false;
            throw new InvalidOperationException($"Simulated failure launching {module.Name}");
        }

        var handle = new FakeProcessHandle { Id = _nextPid++, HasExited = false };
        HandlesByModule[module.Name] = handle;
        return handle;
    }
}
