@echo off
REM Double-click in the VM. Prompts for the account password, then removes the
REM Windows boot animation and the login screen.
REM
REM Must run elevated - right-click this file and Run as administrator.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   This must be run as ADMINISTRATOR.
    echo   Right-click 3-STEALTH-BOOT.cmd and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

set /p PW=Enter the Windows password for user "%USERNAME%":

powershell -ExecutionPolicy Bypass -File "%~dp0_stealth-boot.ps1" -Username "%USERNAME%" -Password "%PW%"

echo.
echo ================================================
echo   REBOOT to see the stealth boot
echo ================================================
echo.
pause
