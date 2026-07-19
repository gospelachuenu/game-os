using System.Diagnostics;

namespace ConsoleSupervisor;

internal sealed class ProcessHandleAdapter : IProcessHandle
{
    private readonly Process _process;

    public ProcessHandleAdapter(Process process) => _process = process;

    public int Id => _process.Id;

    public bool HasExited
    {
        get
        {
            _process.Refresh();
            return _process.HasExited;
        }
    }
}

public sealed class Win32ProcessLauncher : IProcessLauncher
{
    public IProcessHandle? Start(ManagedModule module)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = module.ExecutablePath,
            Arguments = module.Arguments,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        var process = Process.Start(startInfo);
        return process is null ? null : new ProcessHandleAdapter(process);
    }
}
