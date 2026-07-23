@echo off
rem ============================================================================
rem  Gaming OS - one-shot VM install + update setup.
rem
rem  Does the whole first-time setup in one run:
rem    - stops the console
rem    - replaces C:\GamingOS with the fresh v1.0.0 build
rem    - sets the update environment variables (machine-wide)
rem    - verifies each step
rem
rem  MUST run as administrator. In Task Manager: File > Run new task >
rem  type  \\vboxsvr\publish\vm-install.cmd  and TICK "administrative privileges".
rem ============================================================================

net session >nul 2>nul
if errorlevel 1 (
  echo.
  echo   *** NOT running as administrator. ***
  echo   This is why the last setup did not stick.
  echo   Close this, and run it again with the admin box TICKED.
  echo.
  pause
  exit /b 1
)

set "SRC=\\vboxsvr\publish\v1.0.0"
set "DST=C:\GamingOS"
set "REPO=gospelachuenu/game-os"

echo.
echo   ===== Gaming OS VM install =====
echo.

echo   [1] Checking the source build is reachable...
if not exist "%SRC%\UI.exe" (
  echo       CANNOT SEE %SRC%\UI.exe
  echo       The shared folder is not mounted, or v1.0.0 is missing.
  pause
  exit /b 1
)
echo       ok - found %SRC%\UI.exe
echo.

echo   [2] Stopping the console...
taskkill /f /im UI.exe >nul 2>nul
timeout /t 2 /nobreak >nul
echo       done
echo.

echo   [3] Backing up the current build and installing the new one...
if exist "%DST%.bak" rmdir /s /q "%DST%.bak"
if exist "%DST%" move "%DST%" "%DST%.bak" >nul
mkdir "%DST%"
xcopy "%SRC%" "%DST%" /E /I /Y /Q >nul
if not exist "%DST%\UI.exe" (
  echo       COPY FAILED - rolling back
  rmdir /s /q "%DST%"
  move "%DST%.bak" "%DST%" >nul
  pause
  exit /b 1
)
echo       installed to %DST%
echo.

echo   [4] Setting update variables ^(machine-wide^)...
setx /m GAMINGOS_UPDATE_MANIFEST "https://github.com/%REPO%/releases/latest/download/version.json" >nul
setx /m GAMINGOS_UPDATE_RELAUNCH "1" >nul
echo       MANIFEST = https://github.com/%REPO%/releases/latest/download/version.json
echo       RELAUNCH = 1
echo.

echo   [5] Verifying the new build...
dir "%DST%\UI.exe" | find "UI.exe"
echo.

echo   [6] Can the VM reach the manifest?
curl -s -L "https://github.com/%REPO%/releases/latest/download/version.json"
echo.
echo.

echo   ===== DONE =====
echo.
echo   If [5] shows today's/22nd date and [6] printed JSON with "version",
echo   REBOOT the VM. It will find the update on the next boot.
echo.
pause
