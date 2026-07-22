@echo off
rem ============================================================================
rem  Gaming OS - clear the update test toggles
rem
rem  Run as administrator to remove the environment variables vm-setup.cmd set.
rem  Use this when you want the console to behave like the REAL appliance again:
rem  updates finish with a clean reboot rather than relaunching in place, and the
rem  console falls back to the simulated update source with no manifest set.
rem ============================================================================

net session >nul 2>nul
if errorlevel 1 (
  echo   Run this as administrator ^(right-click, Run as administrator^).
  pause
  exit /b 1
)

reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v GAMINGOS_UPDATE_MANIFEST /f >nul 2>nul
reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v GAMINGOS_UPDATE_RELAUNCH /f >nul 2>nul

echo.
echo   Cleared GAMINGOS_UPDATE_MANIFEST and GAMINGOS_UPDATE_RELAUNCH.
echo   Takes effect on the next boot.
echo.
pause
