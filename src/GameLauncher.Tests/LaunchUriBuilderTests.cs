using ConsoleLibrary;
using GameLauncher;

namespace GameLauncher.Tests;

public class LaunchUriBuilderTests
{
    private static GameEntry MakeEntry(GameProvider provider, string appId) => new()
    {
        GameId = $"{provider}_{appId}",
        GameName = "Test Game",
        Provider = provider,
        AppId = appId,
        InstallPath = @"C:\Games\Test",
    };

    [Fact]
    public void Build_SteamProducesRunUri()
    {
        var uri = LaunchUriBuilder.Build(MakeEntry(GameProvider.Steam, "730"));
        Assert.Equal("steam://run/730", uri);
    }

    [Fact]
    public void Build_EpicProducesLaunchUriWithSilentFlag()
    {
        var uri = LaunchUriBuilder.Build(MakeEntry(GameProvider.Epic, "Fortnite"));
        Assert.Equal("com.epicgames.launcher://apps/Fortnite?action=launch&silent=true", uri);
    }

    [Fact]
    public void Build_EpicEscapesSpecialCharactersInAppId()
    {
        var uri = LaunchUriBuilder.Build(MakeEntry(GameProvider.Epic, "App Name+Extra"));
        Assert.Equal("com.epicgames.launcher://apps/App%20Name%2BExtra?action=launch&silent=true", uri);
    }
}
