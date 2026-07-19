using Microsoft.Data.Sqlite;

namespace ConsoleLibrary;

/// <summary>
/// Owns the console_lib.db schema described in plan.md §4.1: GameID (PK), GameName,
/// Provider, AppID, InstallPath, Status, ImageURL.
/// </summary>
public sealed class GameLibraryDatabase
{
    private readonly string _connectionString;

    public GameLibraryDatabase(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
    }

    public void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Games (
                GameId       TEXT PRIMARY KEY,
                GameName     TEXT NOT NULL,
                Provider     TEXT NOT NULL,
                AppId        TEXT NOT NULL,
                InstallPath  TEXT NOT NULL,
                Status       TEXT NOT NULL,
                ImageUrl     TEXT
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Upsert(GameEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Games (GameId, GameName, Provider, AppId, InstallPath, Status, ImageUrl)
            VALUES ($gameId, $gameName, $provider, $appId, $installPath, $status, $imageUrl)
            ON CONFLICT(GameId) DO UPDATE SET
                GameName    = excluded.GameName,
                Provider    = excluded.Provider,
                AppId       = excluded.AppId,
                InstallPath = excluded.InstallPath,
                Status      = excluded.Status,
                ImageUrl    = excluded.ImageUrl;
            """;

        command.Parameters.AddWithValue("$gameId", entry.GameId);
        command.Parameters.AddWithValue("$gameName", entry.GameName);
        command.Parameters.AddWithValue("$provider", entry.Provider.ToString());
        command.Parameters.AddWithValue("$appId", entry.AppId);
        command.Parameters.AddWithValue("$installPath", entry.InstallPath);
        command.Parameters.AddWithValue("$status", entry.Status.ToString());
        command.Parameters.AddWithValue("$imageUrl", (object?)entry.ImageUrl ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    public void UpdateStatus(string gameId, GameStatus status)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET Status = $status WHERE GameId = $gameId;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$gameId", gameId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<GameEntry> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GameId, GameName, Provider, AppId, InstallPath, Status, ImageUrl FROM Games;";

        using var reader = command.ExecuteReader();
        var results = new List<GameEntry>();

        while (reader.Read())
        {
            results.Add(new GameEntry
            {
                GameId = reader.GetString(0),
                GameName = reader.GetString(1),
                Provider = Enum.Parse<GameProvider>(reader.GetString(2)),
                AppId = reader.GetString(3),
                InstallPath = reader.GetString(4),
                Status = Enum.Parse<GameStatus>(reader.GetString(5)),
                ImageUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }

        return results;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
