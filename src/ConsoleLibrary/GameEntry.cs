namespace ConsoleLibrary;

public enum GameStatus
{
    Installed,
    Downloading,
    Unknown,
}

public enum GameProvider
{
    Steam,
    Epic,
}

public sealed class GameEntry
{
    public required string GameId { get; init; }
    public required string GameName { get; init; }
    public required GameProvider Provider { get; init; }
    public required string AppId { get; init; }
    public required string InstallPath { get; init; }
    public GameStatus Status { get; set; } = GameStatus.Unknown;
    public string? ImageUrl { get; init; }
}
