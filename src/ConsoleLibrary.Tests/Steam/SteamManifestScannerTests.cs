using ConsoleLibrary.Steam;

namespace ConsoleLibrary.Tests.Steam;

public class SteamManifestScannerTests
{
    private const string InstalledManifest = """
        "AppState"
        {
            "appid"		"730"
            "name"		"Counter-Strike 2"
            "StateFlags"		"4"
            "installdir"		"Counter-Strike Global Offensive"
        }
        """;

    [Fact]
    public void ParseManifest_BuildsInstallPathFromSteamappsAndInstallDir()
    {
        var entry = SteamManifestScanner.ParseManifest(InstalledManifest, @"D:\SteamLibrary\steamapps");

        Assert.NotNull(entry);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive", entry!.InstallPath);
    }

    [Fact]
    public void ParseManifest_MapsFieldsCorrectly()
    {
        var entry = SteamManifestScanner.ParseManifest(InstalledManifest, @"D:\SteamLibrary\steamapps");

        Assert.NotNull(entry);
        Assert.Equal("steam_730", entry!.GameId);
        Assert.Equal("Counter-Strike 2", entry.GameName);
        Assert.Equal(GameProvider.Steam, entry.Provider);
        Assert.Equal("730", entry.AppId);
    }

    [Fact]
    public void ParseManifest_StateFlagsFourMapsToInstalled()
    {
        var entry = SteamManifestScanner.ParseManifest(InstalledManifest, @"D:\SteamLibrary\steamapps");
        Assert.Equal(GameStatus.Installed, entry!.Status);
    }

    [Fact]
    public void ParseManifest_OtherStateFlagsMapToUnknown_GhostTileTrackerOwnsInProgressState()
    {
        const string downloadingManifest = """
            "AppState"
            {
                "appid"		"440"
                "name"		"Team Fortress 2"
                "StateFlags"		"6"
                "installdir"		"Team Fortress 2"
            }
            """;

        var entry = SteamManifestScanner.ParseManifest(downloadingManifest, @"D:\SteamLibrary\steamapps");
        Assert.Equal(GameStatus.Unknown, entry!.Status);
    }

    [Fact]
    public void ParseManifest_ReturnsNullWhenAppStateBlockMissing()
    {
        const string malformed = """
            "SomethingElse"
            {
                "appid"		"1"
            }
            """;

        var entry = SteamManifestScanner.ParseManifest(malformed, @"D:\SteamLibrary\steamapps");
        Assert.Null(entry);
    }

    [Fact]
    public void ParseManifest_ReturnsNullWhenRequiredFieldMissing()
    {
        const string missingInstallDir = """
            "AppState"
            {
                "appid"		"730"
                "name"		"Counter-Strike 2"
            }
            """;

        var entry = SteamManifestScanner.ParseManifest(missingInstallDir, @"D:\SteamLibrary\steamapps");
        Assert.Null(entry);
    }

    [Fact]
    public void ScanDirectory_ReturnsEmptyWhenDirectoryDoesNotExist()
    {
        var results = SteamManifestScanner.ScanDirectory(@"Z:\does-not-exist\steamapps").ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void ScanDirectory_ParsesAllAcfFilesAndSkipsCorruptOnes()
    {
        var tempDir = Directory.CreateTempSubdirectory("steamapps_test_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "appmanifest_730.acf"), InstalledManifest);
            File.WriteAllText(Path.Combine(tempDir.FullName, "appmanifest_corrupt.acf"), "{ not valid vdf");

            var results = SteamManifestScanner.ScanDirectory(tempDir.FullName).ToList();

            Assert.Single(results);
            Assert.Equal("steam_730", results[0].GameId);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
