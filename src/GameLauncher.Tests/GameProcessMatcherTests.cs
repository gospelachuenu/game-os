using ConsoleLibrary;
using GameLauncher;

namespace GameLauncher.Tests;

public class GameProcessMatcherTests
{
    private static GameEntry MakeEntry(string gameId, string name) => new()
    {
        GameId = gameId,
        GameName = name,
        Provider = GameProvider.Epic,
        AppId = name,
        InstallPath = @"C:\Games\Test",
    };

    [Fact]
    public void FindMatch_ReturnsEntryWhenProcessNameMatchesKnownExecutable()
    {
        var library = new[] { MakeEntry("epic_fortnite", "Fortnite") };
        var map = new Dictionary<string, string> { ["epic_fortnite"] = "FortniteClient-Win64-Shipping.exe" };

        var match = GameProcessMatcher.FindMatch("FortniteClient-Win64-Shipping.exe", library, map);

        Assert.NotNull(match);
        Assert.Equal("epic_fortnite", match!.GameId);
    }

    [Fact]
    public void FindMatch_IsCaseInsensitive()
    {
        var library = new[] { MakeEntry("epic_fortnite", "Fortnite") };
        var map = new Dictionary<string, string> { ["epic_fortnite"] = "FortniteClient-Win64-Shipping.exe" };

        var match = GameProcessMatcher.FindMatch("fortniteclient-win64-shipping.exe", library, map);

        Assert.NotNull(match);
    }

    [Fact]
    public void FindMatch_ReturnsNullWhenNoLibraryEntryMatches()
    {
        var library = new[] { MakeEntry("epic_fortnite", "Fortnite") };
        var map = new Dictionary<string, string> { ["epic_fortnite"] = "FortniteClient-Win64-Shipping.exe" };

        var match = GameProcessMatcher.FindMatch("notepad.exe", library, map);

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_ReturnsNullWhenLibraryEntryHasNoExecutableMapping()
    {
        var library = new[] { MakeEntry("epic_fortnite", "Fortnite") };
        var map = new Dictionary<string, string>();

        var match = GameProcessMatcher.FindMatch("FortniteClient-Win64-Shipping.exe", library, map);

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_PicksCorrectEntryAmongMultipleLibraryGames()
    {
        var library = new[]
        {
            MakeEntry("epic_fortnite", "Fortnite"),
            MakeEntry("steam_730", "Counter-Strike 2"),
        };
        var map = new Dictionary<string, string>
        {
            ["epic_fortnite"] = "FortniteClient-Win64-Shipping.exe",
            ["steam_730"] = "cs2.exe",
        };

        var match = GameProcessMatcher.FindMatch("cs2.exe", library, map);

        Assert.NotNull(match);
        Assert.Equal("steam_730", match!.GameId);
    }
}
