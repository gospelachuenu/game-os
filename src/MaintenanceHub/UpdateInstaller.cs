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
        // A backup name unique to THIS swap, so it cannot collide with a leftover .bak/.old
        // from a manual bootstrap or a previous update — a collision there was silently
        // failing the move and rolling back to the old build.
        var backup = install + ".prev-" + Guid.NewGuid().ToString("N")[..8];
        var pid = Environment.ProcessId;
        var exeName = Path.GetFileName(relaunchExe);

        // A log the swap writes as it runs, so a failed update can be diagnosed from what
        // it ACTUALLY did rather than inferred from the aftermath. Beside the install, not
        // in temp, so it survives even if temp is cleared.
        var log = Path.Combine(
            Path.GetDirectoryName(install.TrimEnd('\\')) ?? "C:\\", "gamingos-update.log");

        var finish = reboot
            ? "shutdown /r /t 0"
            : $"start \"\" \"{install}\\{exeName}\"";

        // Every path is quoted: install folders and temp paths routinely contain spaces.
        return $$"""
            @echo off
            set LOG="{{log}}"
            echo ================================================= > %LOG%
            echo Gaming OS update swap  %DATE% %TIME% >> %LOG%
            echo install = {{install}} >> %LOG%
            echo stage   = {{stageDir}} >> %LOG%
            echo backup  = {{backup}} >> %LOG%

            rem 1. Wait for the console to fully exit so its files unlock.
            :waitloop
            tasklist /fi "PID eq {{pid}}" 2>nul | find "{{pid}}" >nul
            if not errorlevel 1 (
              timeout /t 1 /nobreak >nul
              goto waitloop
            )
            echo [1] console exited >> %LOG%

            rem 2. Write filter off if present. On the appliance this needs the reboot below
            rem    to take full effect, but disabling here still lets the swap write through
            rem    for this session.
            where uwfmgr >nul 2>nul && (uwfmgr filter disable >> %LOG% 2>&1 & echo [2] uwf disable attempted >> %LOG%)

            rem 3. Verify the staged build is real BEFORE touching the live install. If the
            rem    stage is missing we must not move the install aside, or we would be left
            rem    with nothing.
            if not exist "{{stageDir}}\{{exeName}}" (
              echo [3] ABORT: staged build missing, install untouched >> %LOG%
              goto finish
            )
            echo [3] staged build present >> %LOG%

            rem 4. Swap: install -> backup, stage -> install.
            move "{{install}}" "{{backup}}" >> %LOG% 2>&1
            echo [4a] moved install to backup >> %LOG%
            move "{{stageDir}}" "{{install}}" >> %LOG% 2>&1
            echo [4b] moved stage to install >> %LOG%

            rem 5. Confirm the NEW build landed. If it did not, roll the old one back so the
            rem    console still starts.
            if exist "{{install}}\{{exeName}}" (
              echo [5] SUCCESS: new build in place >> %LOG%
              rmdir /s /q "{{backup}}" >nul 2>nul
            ) else (
              echo [5] FAILED: new build not in place, rolling back >> %LOG%
              if exist "{{install}}" rmdir /s /q "{{install}}" >nul 2>nul
              move "{{backup}}" "{{install}}" >> %LOG% 2>&1
            )

            rem 6. Write filter back on if we turned it off.
            where uwfmgr >nul 2>nul && (uwfmgr filter enable >> %LOG% 2>&1 & echo [6] uwf enable attempted >> %LOG%)

            :finish
            echo [7] finishing: {{finish}} >> %LOG%
            del "%~f0"
            {{finish}}
            """;
    }
}
