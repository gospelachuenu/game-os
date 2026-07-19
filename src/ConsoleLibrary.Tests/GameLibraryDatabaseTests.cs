namespace ConsoleLibrary.Tests;

public class GameLibraryDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameLibraryDatabase _database;

    public GameLibraryDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"console_lib_test_{Guid.NewGuid():N}.db");
        _database = new GameLibraryDatabase(_dbPath);
        _database.EnsureSchema();
    }

    private static GameEntry MakeEntry(string gameId = "steam_730") => new()
    {
        GameId = gameId,
        GameName = "Counter-Strike 2",
        Provider = GameProvider.Steam,
        AppId = "730",
        InstallPath = @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
        Status = GameStatus.Installed,
        ImageUrl = "https://example.test/cs2.jpg",
    };

    [Fact]
    public void Upsert_ThenGetAll_RoundTripsAllFields()
    {
        _database.Upsert(MakeEntry());

        var all = _database.GetAll();

        Assert.Single(all);
        var entry = all[0];
        Assert.Equal("steam_730", entry.GameId);
        Assert.Equal("Counter-Strike 2", entry.GameName);
        Assert.Equal(GameProvider.Steam, entry.Provider);
        Assert.Equal("730", entry.AppId);
        Assert.Equal(GameStatus.Installed, entry.Status);
        Assert.Equal("https://example.test/cs2.jpg", entry.ImageUrl);
    }

    [Fact]
    public void Upsert_WithSameGameId_UpdatesRatherThanDuplicates()
    {
        _database.Upsert(MakeEntry());
        _database.Upsert(MakeEntry());

        Assert.Single(_database.GetAll());
    }

    [Fact]
    public void UpdateStatus_ChangesOnlyTheStatusField()
    {
        _database.Upsert(MakeEntry());

        _database.UpdateStatus("steam_730", GameStatus.Downloading);

        var entry = _database.GetAll().Single();
        Assert.Equal(GameStatus.Downloading, entry.Status);
        Assert.Equal("Counter-Strike 2", entry.GameName);
    }

    [Fact]
    public void GetAll_HandlesNullImageUrl()
    {
        var entry = MakeEntry();
        var withoutImage = new GameEntry
        {
            GameId = entry.GameId,
            GameName = entry.GameName,
            Provider = entry.Provider,
            AppId = entry.AppId,
            InstallPath = entry.InstallPath,
            Status = entry.Status,
            ImageUrl = null,
        };

        _database.Upsert(withoutImage);

        var result = _database.GetAll().Single();
        Assert.Null(result.ImageUrl);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
