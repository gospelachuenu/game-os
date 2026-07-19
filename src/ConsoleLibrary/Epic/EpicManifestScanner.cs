using System.Text.Json;

namespace ConsoleLibrary.Epic;

public static class EpicManifestScanner
{
    public static GameEntry? ParseManifest(string jsonContents)
    {
        EpicManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EpicManifest>(jsonContents);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null ||
            manifest.CatalogItemId is null ||
            manifest.DisplayName is null ||
            manifest.InstallLocation is null)
        {
            return null;
        }

        return new GameEntry
        {
            GameId = $"epic_{manifest.CatalogItemId}",
            GameName = manifest.DisplayName,
            Provider = GameProvider.Epic,
            AppId = manifest.AppName ?? manifest.CatalogItemId,
            InstallPath = manifest.InstallLocation,
            Status = manifest.IsIncompleteInstall ? GameStatus.Downloading : GameStatus.Installed,
        };
    }

    public static IEnumerable<GameEntry> ScanDirectory(string manifestsPath)
    {
        if (!Directory.Exists(manifestsPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(manifestsPath, "*.item"))
        {
            GameEntry? entry = null;
            try
            {
                var contents = File.ReadAllText(file);
                entry = ParseManifest(contents);
            }
            catch (Exception)
            {
                // Skip unreadable/corrupt manifests; one bad file should not abort the whole scan.
            }

            if (entry is not null)
            {
                yield return entry;
            }
        }
    }
}
