using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class SilentInstallCommandBuilderTests
{
    [Fact]
    public void BuildAmdDriverInstall_UsesExactSilentSwitchesFromPlanMd()
    {
        var command = SilentInstallCommandBuilder.BuildAmdDriverInstall(@"C:\ConsoleDrivers\AMD_Setup.exe");

        Assert.Equal(@"C:\ConsoleDrivers\AMD_Setup.exe", command.FileName);
        Assert.Equal("-install -s -noreboot", command.Arguments);
    }

    [Fact]
    public void BuildAmdDriverInstall_AlwaysMasksWindowSpawning()
    {
        var command = SilentInstallCommandBuilder.BuildAmdDriverInstall(@"C:\ConsoleDrivers\AMD_Setup.exe");

        Assert.True(command.CreateNoWindow);
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void BuildLegacyDriverInstall_UsesPnputilWithCorrectFlags()
    {
        var command = SilentInstallCommandBuilder.BuildLegacyDriverInstall(@"C:\Drivers\legacy.inf");

        Assert.Equal("pnputil.exe", command.FileName);
        Assert.Equal("/add-driver \"C:\\Drivers\\legacy.inf\" /install /subdirs", command.Arguments);
    }

    [Fact]
    public void BuildLegacyDriverInstall_AlwaysMasksWindowSpawning()
    {
        var command = SilentInstallCommandBuilder.BuildLegacyDriverInstall(@"C:\Drivers\legacy.inf");

        Assert.True(command.CreateNoWindow);
        Assert.False(command.UseShellExecute);
    }
}
