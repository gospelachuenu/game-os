using ConsoleLibrary;

namespace UI;

/// <summary>
/// Seeds console_lib.db with sample entries for this windowed test build, since this
/// dev laptop has no real Steam/Epic installs for the manifest scanners (ConsoleLibrary.Steam/
/// ConsoleLibrary.Epic) to find. On the real target hardware, GameLibraryDatabase would
/// instead be populated by SteamManifestScanner/EpicManifestScanner against real installs.
/// </summary>
public static class SampleLibrarySeeder
{
    public static void SeedIfEmpty(GameLibraryDatabase database)
    {
        if (database.GetAll().Count > 0)
        {
            return;
        }

        database.Upsert(new GameEntry
        {
            GameId = "steam_730",
            GameName = "Counter-Strike 2",
            Provider = GameProvider.Steam,
            AppId = "730",
            InstallPath = @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
            Status = GameStatus.Installed,
            ImageUrl = "cs2.png",
        });

        database.Upsert(new GameEntry
        {
            GameId = "epic_fortnite",
            GameName = "Fortnite",
            Provider = GameProvider.Epic,
            AppId = "Fortnite",
            InstallPath = @"C:\Games\Fortnite",
            Status = GameStatus.Installed,
            ImageUrl = "fortnite.png",
        });

        database.Upsert(new GameEntry
        {
            GameId = "steam_1091500",
            GameName = "Cyberpunk 2077",
            Provider = GameProvider.Steam,
            AppId = "1091500",
            InstallPath = @"D:\SteamLibrary\steamapps\common\Cyberpunk 2077",
            Status = GameStatus.Downloading,
            ImageUrl = "cyberpunk2077.png",
        });

        database.Upsert(new GameEntry
        {
            GameId = "steam_1245620",
            GameName = "Elden Ring",
            Provider = GameProvider.Steam,
            AppId = "1245620",
            InstallPath = @"D:\SteamLibrary\steamapps\common\ELDEN RING",
            Status = GameStatus.Installed,
            ImageUrl = "eldenring.png",
        });
    }
}
