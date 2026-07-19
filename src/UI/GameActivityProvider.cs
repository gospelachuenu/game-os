using System.Windows.Media;
using ConsoleLibrary;

namespace UI;

/// <summary>
/// Per-game play activity (last played, play time, achievements) and the accent colour
/// the Spotlight background tints toward for that game.
/// </summary>
public sealed class GameActivity
{
    public required string LastPlayedLabel { get; init; }
    public required string PlayTimeLabel { get; init; }
    public required string InstallSizeLabel { get; init; }
    public required string AchievementsLabel { get; init; }

    /// <summary>Colour the ambient background tints toward while this game is focused.</summary>
    public required Color AmbientTint { get; init; }
}

/// <summary>
/// SIMULATED play-activity data for the Spotlight UI. Nothing here is real yet — the
/// console tracks no play time, achievements or per-game install sizes, and there is no
/// cover-art colour extraction wired up. Values are deterministic per GameId so the UI
/// is stable across restarts rather than flickering between random numbers.
///
/// When real sources exist, replace this wholesale: play time/achievements would come
/// from the provider (Steam/Epic), install size from the real InstallPath on disk, and
/// AmbientTint from sampling the game's actual cover art.
/// </summary>
public static class GameActivityProvider
{
    private static readonly Dictionary<string, GameActivity> Simulated = new()
    {
        ["steam_730"] = new GameActivity
        {
            LastPlayedLabel = "Yesterday",
            PlayTimeLabel = "142 hrs",
            InstallSizeLabel = "38.4 GB",
            AchievementsLabel = "27 / 40",
            AmbientTint = Color.FromRgb(0xF5, 0xB7, 0x2E),
        },
        ["epic_fortnite"] = new GameActivity
        {
            LastPlayedLabel = "4 days ago",
            PlayTimeLabel = "88 hrs",
            InstallSizeLabel = "26.9 GB",
            AchievementsLabel = "—",
            AmbientTint = Color.FromRgb(0x3E, 0x8B, 0xFF),
        },
        ["steam_1091500"] = new GameActivity
        {
            LastPlayedLabel = "3 weeks ago",
            PlayTimeLabel = "61 hrs",
            InstallSizeLabel = "72.1 GB",
            AchievementsLabel = "18 / 44",
            AmbientTint = Color.FromRgb(0xE8, 0x3D, 0x9B),
        },
        ["steam_1245620"] = new GameActivity
        {
            LastPlayedLabel = "Last month",
            PlayTimeLabel = "96 hrs",
            InstallSizeLabel = "54.7 GB",
            AchievementsLabel = "31 / 42",
            AmbientTint = Color.FromRgb(0xC9, 0x9A, 0x3B),
        },
    };

    private static readonly GameActivity Fallback = new()
    {
        LastPlayedLabel = "Never",
        PlayTimeLabel = "0 hrs",
        InstallSizeLabel = "—",
        AchievementsLabel = "—",
        AmbientTint = Color.FromRgb(0xA8, 0xE0, 0x3B),
    };

    public static GameActivity For(GameEntry entry) =>
        Simulated.TryGetValue(entry.GameId, out var activity) ? activity : Fallback;
}
