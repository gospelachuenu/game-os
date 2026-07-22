using System.Diagnostics;
using System.IO.Compression;

namespace MaintenanceHub;

/// <summary>
/// Applies a downloaded update by swapping the console's own files for the new build.
///
/// THE PROBLEM THIS SOLVES: a running program cannot overwrite its own exe and DLLs —
/// they are locked while the process lives. So the install cannot be done from inside the
/// console. Instead this stages the new files and writes a small script that runs AFTER
/// the console exits: it waits for the process to end, swaps the folder, and relaunches.
///
/// The swap is made as close to atomic as a file system allows — the live install is
/// renamed aside, the new build moved into place, and the old one deleted only once the
/// new one is confirmed there. A power cut mid-swap therefore leaves either the old
/// install or the new one, never a half-written mixture that will not start.
///
/// On the real appliance the install directory sits behind the Unified Write Filter, so
/// the script also toggles UWF around the swap (the "three-boot dance"). That is gated on
/// UWF actually being present, so the same script works on the dev VM where it is not.
/// </summary>
public sealed class UpdateInstaller
{
    private readonly string _installDir;

    /// <param name="installDir">
    /// The folder the console runs from — the one to be replaced. Defaults to the
    /// directory the current executable lives in.
    /// </param>
    public UpdateInstaller(string? installDir = null)
    {
        _installDir = installDir ?? AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Unpacks the package next to the install so the files are ready before any swap.
    ///
    /// Done as a SEPARATE step from applying, and BEFORE the console exits, because
    /// unzipping tens of megabytes is the slow part and the risky part (a corrupt zip is
    /// caught here, with the console still running, rather than after it has quit and
    /// cannot report anything). Returns the staged directory, or null if the package is
    /// unusable — in which case the caller discards it and re-downloads.
    /// </summary>
    public string? StagePackage(string packagePath)
    {
        var stageDir = Path.Combine(
            Path.GetDirectoryName(packagePath) ?? _installDir,
            "staged-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (Directory.Exists(stageDir))
            {
                Directory.Delete(stageDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(packagePath, stageDir);

            // A build must at least contain the console executable, or the swap would
            // leave the machine unable to start. Cheap sanity check before committing.
            if (!ContainsExecutable(stageDir))
            {
                Directory.Delete(stageDir, recursive: true);
                return null;
            }

            return stageDir;
        }
        catch (Exception e) when (e is IOException
                                   or InvalidDataException
                                   or UnauthorizedAccessException)
        {
            // Corrupt or truncated zip, or nowhere to write it. Unusable; the caller
            // re-downloads rather than trying to install a broken package.
            TryDeleteDirectory(stageDir);
            return null;
        }
    }

    /// <summary>
    /// Writes and launches the swap script, then asks the console to exit.
    ///
    /// The script does the actual replacement, because it must outlive the process whose
    /// files it is replacing. This method returns having handed off — the caller shuts
    /// the console down, and the script relaunches it from the new build.
    /// </summary>
    /// <param name="stageDir">The directory returned by <see cref="StagePackage"/>.</param>
    /// <param name="relaunchExe">The console executable — started after the swap if not rebooting.</param>
    /// <param name="reboot">
    /// When true, the machine restarts after the swap instead of relaunching the app.
    ///
    /// This is the better default on the real console and quietly nicer everywhere: the
    /// file swap then happens during the reboot's own black screen, so the user never sees
    /// the Windows shell between the old build closing and the new one starting — it simply
    /// reads as "restarting to update". It is also REQUIRED on the appliance, where the
    /// write filter only re-arms across a reboot (the third boot of the "three-boot
    /// dance"). Relaunching in place is kept for the dev VM, where rebooting the whole
    /// machine to test an update would be tedious.
    /// </param>
    /// <returns>True if the swap process was launched; false if it could not be started.</returns>
    public bool ApplyStagedAndRelaunch(string stageDir, string relaunchExe, bool reboot = false)
    {
        if (!Directory.Exists(stageDir))
        {
            return false;
        }

        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"gamingos-update-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(scriptPath, BuildSwapScript(stageDir, relaunchExe, reboot));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // /c runs the script and exits. The script's own first act is to wait for
                // THIS console to close, so starting it now and quitting immediately after
                // is the intended sequence.
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private bool ContainsExecutable(string dir) =>
        Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Any();

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort — a leftover staging folder is clutter, not a failure.
        }
    }

    /// <summary>
    /// The batch script that performs the swap after the console has exited.
    ///
    /// A .cmd rather than a second managed process: it has to run while NO console files
    /// are locked, which means nothing from the install directory may be loaded — so it
    /// cannot itself be one of the console's assemblies. A plain script depends on nothing
    /// but Windows.
    ///
    /// UWF is toggled only if present, so the same script serves the dev VM (no filter)
    /// and the real appliance (filter on) without branching in the console.
    /// </summary>
    private string BuildSwapScript(string stageDir, string relaunchExe, bool reboot)
    {
        var install = _installDir;
        var backup = install + ".old";
        var pid = Environment.ProcessId;

        // Reboot hides the swap inside the machine's own restart; relaunch brings the app
        // straight back in place (dev VM only, where a full reboot per test is tedious).
        var finish = reboot
            ? "shutdown /r /t 0"
            : $"start \"\" \"{install}\\{Path.GetFileName(relaunchExe)}\"";

        // Every path is quoted: install folders and temp paths routinely contain spaces.
        return $"""
            @echo off
            rem --- Gaming OS update swap. Generated; safe to delete after it runs. ---

            rem 1. Wait for the console to actually exit, so its files unlock. Poll its PID
            rem    rather than a fixed sleep, which would race a slow shutdown.
            :waitloop
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul
            if not errorlevel 1 (
              timeout /t 1 /nobreak >nul
              goto waitloop
            )

            rem 2. Disable the write filter if this machine has one, so the swap survives a
            rem    reboot. Absent (the dev VM), this line simply fails and is ignored.
            where uwfmgr >nul 2>nul && uwfmgr filter disable >nul 2>nul

            rem 3. Swap. Move the live install aside, put the new build in its place, and
            rem    only delete the old one once the new one is confirmed present. A crash
            rem    at any point leaves a startable install.
            if exist "{backup}" rmdir /s /q "{backup}"
            move "{install}" "{backup}" >nul
            move "{stageDir}" "{install}" >nul

            if exist "{install}\{Path.GetFileName(relaunchExe)}" (
              rmdir /s /q "{backup}"
            ) else (
              rem New build did not land — roll back to the old one so the console still runs.
              if exist "{install}" rmdir /s /q "{install}"
              move "{backup}" "{install}" >nul
            )

            rem 4. Re-enable the write filter if we disabled it. On the appliance this
            rem    takes effect on the reboot below — the third boot of the "three-boot
            rem    dance".
            where uwfmgr >nul 2>nul && uwfmgr filter enable >nul 2>nul

            rem 5. Delete this script, THEN finish. (Done before the finish line because a
            rem    reboot would never reach a line after it.)
            del "%~f0"

            rem 6. Reboot, or relaunch in place — the swap is now done either way. A reboot
            rem    hides the whole swap inside the machine's own restart, so the user never
            rem    sees the shell between the old build and the new one.
            {finish}
            """;
    }
}
