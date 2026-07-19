using ConsoleLibrary.Epic;

namespace ConsoleLibrary.Tests.Epic;

public class EpicManifestScannerTests
{
    private const string InstalledManifestJson = """
        {
            "DisplayName": "Fortnite",
            "CatalogItemId": "fn-catalog-id",
            "AppName": "Fortnite",
            "InstallLocation": "C:\\Games\\Fortnite",
            "bIsIncompleteInstall": false
        }
        """;

    [Fact]
    public void ParseManifest_MapsFieldsCorrectly()
    {
        var entry = EpicManifestScanner.ParseManifest(InstalledManifestJson);

        Assert.NotNull(entry);
        Assert.Equal("epic_fn-catalog-id", entry!.GameId);
        Assert.Equal("Fortnite", entry.GameName);
        Assert.Equal(GameProvider.Epic, entry.Provider);
        Assert.Equal("Fortnite", entry.AppId);
        Assert.Equal(@"C:\Games\Fortnite", entry.InstallPath);
    }

    [Fact]
    public void ParseManifest_CompleteInstallMapsToInstalled()
    {
        var entry = EpicManifestScanner.ParseManifest(InstalledManifestJson);
        Assert.Equal(GameStatus.Installed, entry!.Status);
    }

    [Fact]
    public void ParseManifest_IncompleteInstallMapsToDownloading()
    {
        const string incomplete = """
            {
                "DisplayName": "Fortnite",
                "CatalogItemId": "fn-catalog-id",
                "AppName": "Fortnite",
                "InstallLocation": "C:\\Games\\Fortnite",
                "bIsIncompleteInstall": true
            }
            """;

        var entry = EpicManifestScanner.ParseManifest(incomplete);
        Assert.Equal(GameStatus.Downloading, entry!.Status);
    }

    [Fact]
    public void ParseManifest_ReturnsNullOnInvalidJson()
    {
        var entry = EpicManifestScanner.ParseManifest("{ not valid json");
        Assert.Null(entry);
    }

    [Fact]
    public void ParseManifest_ReturnsNullWhenRequiredFieldMissing()
    {
        const string missingInstallLocation = """
            {
                "DisplayName": "Fortnite",
                "CatalogItemId": "fn-catalog-id"
            }
            """;

        var entry = EpicManifestScanner.ParseManifest(missingInstallLocation);
        Assert.Null(entry);
    }

    [Fact]
    public void ParseManifest_FallsBackToCatalogItemIdWhenAppNameMissing()
    {
        const string noAppName = """
            {
                "DisplayName": "Fortnite",
                "CatalogItemId": "fn-catalog-id",
                "InstallLocation": "C:\\Games\\Fortnite"
            }
            """;

        var entry = EpicManifestScanner.ParseManifest(noAppName);
        Assert.Equal("fn-catalog-id", entry!.AppId);
    }

    [Fact]
    public void ScanDirectory_ReturnsEmptyWhenDirectoryDoesNotExist()
    {
        var results = EpicManifestScanner.ScanDirectory(@"Z:\does-not-exist\Manifests").ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void ScanDirectory_ParsesAllItemFilesAndSkipsCorruptOnes()
    {
        var tempDir = Directory.CreateTempSubdirectory("epic_manifests_test_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "fortnite.item"), InstalledManifestJson);
            File.WriteAllText(Path.Combine(tempDir.FullName, "corrupt.item"), "{ not valid json");

            var results = EpicManifestScanner.ScanDirectory(tempDir.FullName).ToList();

            Assert.Single(results);
            Assert.Equal("epic_fn-catalog-id", results[0].GameId);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
