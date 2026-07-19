namespace MaintenanceHub;

public sealed record SilentInstallCommand(string FileName, string Arguments)
{
    /// <summary>
    /// Always true per §6.3's mandatory masking requirement: install processes must
    /// never flash a visible window or run through ShellExecute, which could surface
    /// the raw installer UI.
    /// </summary>
    public bool CreateNoWindow => true;
    public bool UseShellExecute => false;
}

/// <summary>
/// Builds the exact silent/unattended command lines from plan.md §6.3. Returns a
/// value describing what to run rather than spawning the process itself, so the
/// command-construction logic (correct flags, correct exe path) is testable without
/// actually installing a driver on this dev machine.
/// </summary>
public static class SilentInstallCommandBuilder
{
    public static SilentInstallCommand BuildAmdDriverInstall(string setupExePath) =>
        new(setupExePath, "-install -s -noreboot");

    public static SilentInstallCommand BuildLegacyDriverInstall(string infPath) =>
        new("pnputil.exe", $"/add-driver \"{infPath}\" /install /subdirs");
}
