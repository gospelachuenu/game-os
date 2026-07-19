using ConsoleLibrary;

namespace GameLauncher;

/// <summary>
/// Pure matching logic for plan.md §3.4's WMI Win32_ProcessStartTrace interception:
/// given a newly observed process image name and the known library, decide whether it
/// corresponds to a tracked game's expected executable. The actual WMI event
/// subscription stays outside this class so the matching rule is testable without a
/// live process-start event.
/// </summary>
public static class GameProcessMatcher
{
    public static GameEntry? FindMatch(string processImageName, IEnumerable<GameEntry> library, IReadOnlyDictionary<string, string> gameIdToExecutableName)
    {
        foreach (var entry in library)
        {
            if (gameIdToExecutableName.TryGetValue(entry.GameId, out var executableName) &&
                string.Equals(executableName, processImageName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}
