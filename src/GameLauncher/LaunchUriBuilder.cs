using ConsoleLibrary;

namespace GameLauncher;

/// <summary>
/// Implements plan.md §3.2's direct URI launch protocols, bypassing launcher storefront UI.
/// </summary>
public static class LaunchUriBuilder
{
    public static string Build(GameEntry entry)
    {
        return entry.Provider switch
        {
            GameProvider.Steam => $"steam://run/{entry.AppId}",
            GameProvider.Epic => $"com.epicgames.launcher://apps/{Uri.EscapeDataString(entry.AppId)}?action=launch&silent=true",
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Provider, "Unsupported provider for URI launch."),
        };
    }
}
