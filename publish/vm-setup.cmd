@echo off
rem ============================================================================
rem  Gaming OS - VM update setup
rem
rem  Run this ONCE inside the VM, after copying the v1.0.0 build over the folder
rem  the console already runs from. It sets the two environment variables the
rem  console reads at boot:
rem
rem    GAMINGOS_UPDATE_MANIFEST  - where the console looks for updates
rem    GAMINGOS_UPDATE_RELAUNCH  - relaunch in place instead of rebooting (test aid)
rem
rem  Sets them MACHINE-WIDE (setx /m), so they survive reboots and are in place
rem  no matter which account the console shell runs under. That needs an elevated
rem  prompt -- right-click this file and "Run as administrator".
rem ============================================================================

net session >nul 2>nul
if errorlevel 1 (
  echo.
  echo   This needs to run as administrator.
  echo   Right-click vm-setup.cmd and choose "Run as administrator".
  echo.
  pause
  exit /b 1
)

echo.
echo   Gaming OS - VM update setup
echo   ---------------------------
echo.
echo   Enter your GitHub repo as  user/repo  (e.g. gospel/gaming-os).
echo   The manifest URL is built from it and always points at the LATEST release,
echo   so you never have to change it again.
echo.

set "REPO="
set /p "REPO=  user/repo: "

if "%REPO%"=="" (
  echo.
  echo   No repo entered - nothing changed.
  echo.
  pause
  exit /b 1
)

set "MANIFEST=https://github.com/%REPO%/releases/latest/download/version.json"

echo.
echo   Setting update manifest to:
echo     %MANIFEST%
echo.

setx /m GAMINGOS_UPDATE_MANIFEST "%MANIFEST%" >nul

rem Relaunch-in-place, so testing an update does not reboot the whole VM each
rem time. Remove this on a real console so updates finish with a clean reboot.
setx /m GAMINGOS_UPDATE_RELAUNCH "1" >nul

echo   Done.
echo.
echo   Set:
echo     GAMINGOS_UPDATE_MANIFEST = %MANIFEST%
echo     GAMINGOS_UPDATE_RELAUNCH = 1   (relaunch in place; remove on real console)
echo.
echo   These take effect on the NEXT boot. Reboot the VM to pick them up.
echo.
pause
