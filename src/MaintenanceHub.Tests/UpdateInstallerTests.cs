using System.IO.Compression;
using MaintenanceHub;

namespace MaintenanceHub.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _work;

    public UpdateInstallerTests()
    {
        _work = Path.Combine(Path.GetTempPath(), "installer-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    private string MakePackage(string name, Action<string> fill)
    {
        var contentDir = Path.Combine(_work, "content-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(contentDir);
        fill(contentDir);

        var zipPath = Path.Combine(_work, name);
        ZipFile.CreateFromDirectory(contentDir, zipPath);
        return zipPath;
    }

    [Fact]
    public void StagePackage_UnpacksAValidBuild()
    {
        var pkg = MakePackage("build.zip", dir =>
        {
            File.WriteAllText(Path.Combine(dir, "UI.exe"), "fake exe");
            File.WriteAllText(Path.Combine(dir, "UI.dll"), "fake dll");
        });

        var installer = new UpdateInstaller(_work);
        var staged = installer.StagePackage(pkg);

        Assert.NotNull(staged);
        Assert.True(File.Exists(Path.Combine(staged!, "UI.exe")));
        Assert.True(File.Exists(Path.Combine(staged, "UI.dll")));
    }

    [Fact]
    public void StagePackage_RejectsBuildWithNoExecutable()
    {
        // A package that would leave the console unable to start must be refused before
        // any swap, not discovered after the old build is already gone.
        var pkg = MakePackage("no-exe.zip", dir =>
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "no exe here"));

        var installer = new UpdateInstaller(_work);

        Assert.Null(installer.StagePackage(pkg));
    }

    [Fact]
    public void StagePackage_RejectsCorruptZip()
    {
        // A truncated download must resolve to "unusable", not throw at install time.
        var pkg = Path.Combine(_work, "corrupt.zip");
        File.WriteAllText(pkg, "this is not a zip file at all");

        var installer = new UpdateInstaller(_work);

        Assert.Null(installer.StagePackage(pkg));
    }

    [Fact]
    public void ApplyStagedAndRelaunch_FailsGracefullyOnMissingStage()
    {
        var installer = new UpdateInstaller(_work);

        // A staging directory that does not exist cannot be applied; the method reports
        // false rather than throwing, so the caller can discard and retry.
        Assert.False(installer.ApplyStagedAndRelaunch(
            Path.Combine(_work, "does-not-exist"),
            Path.Combine(_work, "UI.exe")));
    }
}
