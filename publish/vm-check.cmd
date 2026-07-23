@echo off
rem ============================================================================
rem  Gaming OS - update diagnostic. Run in the VM to see why an update did or
rem  did not apply. Read-only: it checks things, changes nothing.
rem ============================================================================

echo.
echo   ===== Gaming OS update check =====
echo.

echo   [1] Installed build:
dir "C:\GamingOS\UI.exe" | find "UI.exe"
echo.

echo   [2] Update environment variables:
echo       MANIFEST = %GAMINGOS_UPDATE_MANIFEST%
echo       RELAUNCH = %GAMINGOS_UPDATE_RELAUNCH%
echo.

echo   [3] Can the VM reach the manifest on GitHub?
curl -s -L "https://github.com/gospelachuenu/game-os/releases/latest/download/version.json"
echo.
echo.

echo   [4] Downloaded update package waiting to install?
if exist "C:\GamingOS-Updates\*.pkg" (
  dir "C:\GamingOS-Updates\*.pkg" | find ".pkg"
) else (
  echo       none found in C:\GamingOS-Updates
)
echo.

echo   [5] Console state ^(pending version, notes^):
if exist "%TEMP%\console_state_ui_test" (
  dir /b "%TEMP%\console_state_ui_test"
) else (
  echo       no state folder yet
)
echo.

echo   [5b] What the installed build reports as its version:
if exist "C:\GamingOS\UI.dll" (
  powershell -NoProfile -Command "try { (Get-Item 'C:\GamingOS\UI.exe').VersionInfo.FileVersion } catch { 'unknown' }"
)
echo       ^(state file contents:^)
if exist "%TEMP%\console_state_ui_test\console-state.json" type "%TEMP%\console_state_ui_test\console-state.json"
echo.
echo.

echo   [5c] Is there a leftover swap staging folder or .old backup?
if exist "C:\GamingOS.old" echo       C:\GamingOS.old EXISTS ^(swap left the old build behind^)
if exist "C:\GamingOS.bak" echo       C:\GamingOS.bak EXISTS
dir "C:\GamingOS-Updates\staged-*" /b 2>nul
echo.

echo   [5d] UI.dll size in each folder - THIS is what differs between versions.
echo        v1.0.0 dll = 335872 bytes ^| v1.1.0 dll = 344576 bytes
if exist "C:\GamingOS\UI.dll"     for %%F in ("C:\GamingOS\UI.dll")     do echo       GamingOS     %%~zF bytes  %%~tF
if exist "C:\GamingOS.bak\UI.dll" for %%F in ("C:\GamingOS.bak\UI.dll") do echo       GamingOS.bak %%~zF bytes  %%~tF
echo.

echo   [6] Write filter (UWF) state:
where uwfmgr >nul 2>nul
if errorlevel 1 (
  echo       uwfmgr NOT present - UWF not installed on this machine
) else (
  uwfmgr get-config | find /i "filter state"
)
echo.

echo   ===== end =====
echo.
pause
