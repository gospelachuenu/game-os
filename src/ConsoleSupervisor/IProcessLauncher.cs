namespace ConsoleSupervisor;

public interface IProcessHandle
{
    int Id { get; }
    bool HasExited { get; }
}

public interface IProcessLauncher
{
    IProcessHandle? Start(ManagedModule module);
}
