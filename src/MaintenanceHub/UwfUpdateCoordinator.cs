using System.Diagnostics;

namespace MaintenanceHub;

/// <summary>
/// Drives an update across the reboots the Unified Write Filter forces on it.
///
/// THE CONSTRAINT: UWF holds C: in a RAM overlay, so any file written while it is enabled
/// is discarded on reboot. You therefore cannot disable UWF and apply an update in the
/// same boot — the disable only takes effect after a restart. Applying the swap while UWF
/// is still on is exactly why an update "installed" and then vanished on reboot.
///
/// THE DANCE (three boots):
///   Boot A  UWF on   accept update -> disable UWF -> record "resume: apply" -> reboot
///   Boot B  UWF off  see resume marker -> swap files (persists) -> re-enable UWF -> reboot
///   Boot C  UWF on   run the new build, filter re-armed
///
/// The resume marker lives in the console state store, which MUST sit in a UWF
/// write-through exclusion (C:\GamingOS\State) or it would be discarded like everything
/// else and the dance would lose its place. That folder being excluded is a setup
/// precondition; <see cref="EnsureStateExcluded"/> adds it.
///
/// On a machine with no UWF (the dev VM before it is locked down, a plain PC) every step
/// still runs — the uwfmgr calls simply no-op — so a single reboot applies the update and
/// the "dance" collapses to the ordinary path.
/// </summary>
public sealed class UwfUpdateCoordinator
{
    /// <summary>Where an in-progress dance has got to, stored across reboots.</summary>
    public enum Stage
    {
        /// <summary>No update in progress.</summary>
        None,

        /// <summary>UWF has been told to disable; the next boot applies the swap.</summary>
        ApplyAfterReboot,
    }

    private readonly IConsoleStateStore _state;

    public UwfUpdateCoordinator(IConsoleStateStore state) => _state = state;

    /// <summary>True when UWF is installed and its filter is currently ON.</summary>
    public static bool IsUwfActive()
    {
        var (ok, output) = RunUwf("get-config");
        if (!ok)
        {
            return false;
        }

        // "Filter state" / "Current session ... enabled" wording varies by build; match
        // either an explicit enabled line, tolerating case and spacing.
        return output.Contains("enabled", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("filter state: off", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if uwfmgr exists at all — i.e. this machine can have a write filter.</summary>
    public static bool UwfPresent()
    {
        try
        {
            return RunUwf("help").ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The stage a previous boot left the dance at.</summary>
    public Stage CurrentStage =>
        _state.Get(ConsoleStateKeys.UwfUpdateStage) switch
        {
            "apply" => Stage.ApplyAfterReboot,
            _ => Stage.None,
        };

    /// <summary>
    /// Boot A. Prepares the machine to apply an update on the NEXT boot: makes sure the
    /// state folder is write-through-excluded (so the marker survives), disables UWF, and
    /// records the resume stage. The caller reboots after this.
    ///
    /// On a machine without UWF this only records the stage — there is no filter to
    /// disable and no reboot strictly required, but the same stage-driven path is used so
    /// there is one code route, not two.
    /// </summary>
    public void BeginApply(string stateDirectory)
    {
        if (UwfPresent())
        {
            EnsureStateExcluded(stateDirectory);
            RunUwf("filter disable");
        }

        _state.Set(ConsoleStateKeys.UwfUpdateStage, "apply");
    }

    /// <summary>
    /// Boot B, after the swap has been applied. Re-enables the filter and clears the
    /// resume stage. The caller reboots after this to re-arm UWF (Boot C).
    /// </summary>
    public void FinishApply()
    {
        _state.Remove(ConsoleStateKeys.UwfUpdateStage);

        if (UwfPresent())
        {
            RunUwf("filter enable");
        }
    }

    /// <summary>Abandons a dance — filter back on, stage cleared — if an apply cannot proceed.</summary>
    public void Abort()
    {
        _state.Remove(ConsoleStateKeys.UwfUpdateStage);

        if (UwfPresent())
        {
            RunUwf("filter enable");
        }
    }

    /// <summary>
    /// Adds the state directory as a UWF file write-through exclusion, so its contents —
    /// the resume marker, the pending update, the parent PIN — persist across reboots.
    /// Idempotent: adding an existing exclusion is harmless.
    /// </summary>
    public static void EnsureStateExcluded(string stateDirectory)
    {
        if (!UwfPresent())
        {
            return;
        }

        // The volume and path uwfmgr expects are separate arguments.
        RunUwf($"file add-exclusion \"{stateDirectory}\"");
    }

    private static (bool ok, string output) RunUwf(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "uwfmgr.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return (false, string.Empty);
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return (process.ExitCode == 0, output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // uwfmgr not present, or not permitted. Treated as "no UWF" — the caller's
            // no-UWF path is the safe default.
            return (false, string.Empty);
        }
    }
}
