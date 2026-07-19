namespace ConsoleSupervisor;

public sealed class ManagedModule
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public bool IsGameOrUiSurface { get; init; }
}
