namespace ConsoleLibrary.Steam;

public static class SteamManifestScanner
{
    /// <summary>
    /// Parses a single appmanifest_*.acf file's contents into a GameEntry.
    /// steamappsPath is the "steamapps" directory the manifest lives in, used to build InstallPath.
    /// </summary>
    public static GameEntry? ParseManifest(string acfContents, string steamappsPath)
    {
        var root = VdfParser.Parse(acfContents);
        var appState = root["AppState"];
        if (appState is null)
        {
            return null;
        }

        var appId = appState.GetString("appid");
        var name = appState.GetString("name");
        var installDir = appState.GetString("installdir");
        var stateFlags = appState.GetString("StateFlags");

        if (appId is null || name is null || installDir is null)
        {
            return null;
        }

        var installPath = Path.Combine(steamappsPath, "common", installDir);

        return new GameEntry
        {
            GameId = $"steam_{appId}",
            GameName = name,
            Provider = GameProvider.Steam,
            AppId = appId,
            InstallPath = installPath,
            Status = InterpretStateFlags(stateFlags),
        };
    }

    /// <summary>
    /// StateFlags is a bitmask; 4 ("fully installed") is Valve's documented steady-state value.
    /// Any other observed value is treated as in-progress/unknown rather than guessed at,
    /// since the ghost-tile FileSystemWatcher (see GhostTileTracker) is the authoritative
    /// signal for download progress, not this field.
    /// </summary>
    private static GameStatus InterpretStateFlags(string? stateFlags)
    {
        if (stateFlags is not null && int.TryParse(stateFlags, out var flags))
        {
            const int fullyInstalled = 4;
            if (flags == fullyInstalled)
            {
                return GameStatus.Installed;
            }
        }

        return GameStatus.Unknown;
    }

    public static IEnumerable<GameEntry> ScanDirectory(string steamappsPath)
    {
        if (!Directory.Exists(steamappsPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(steamappsPath, "appmanifest_*.acf"))
        {
            GameEntry? entry = null;
            try
            {
                var contents = File.ReadAllText(file);
                entry = ParseManifest(contents, steamappsPath);
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
