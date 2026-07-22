@echo off
REM Suppresses the "Welcome / console + spinning dots" screen shown while Windows
REM signs in and starts the shell.
REM
REM Right-click > Run as administrator.
REM
REM This is NOT the boot animation (bootux) and NOT the sign-in prompt
REM (auto-login). It is the logon UI shown between the two, while the profile
REM loads and the shell starts.
REM
REM Honest expectation: this reduces what is shown, it does not make the gap
REM zero. Windows genuinely is doing work there. The real fix for the remaining
REM gap is the console UI starting fast and painting black immediately.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   Must be run as ADMINISTRATOR - right-click, Run as administrator.
    echo.
    pause
    exit /b 1
)

echo === Removing the animated first-sign-in experience ===
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v EnableFirstLogonAnimation /t REG_DWORD /d 0 /f
echo.

echo === Suppressing logon status messages ===
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableStatusMessages /t REG_DWORD /d 1 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v VerboseStatus /t REG_DWORD /d 0 /f
echo.

echo === Removing the lock screen ===
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization" /v NoLockScreen /t REG_DWORD /d 1 /f
echo.

echo === Skipping user-profile first-run tasks ===
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoAutoUpdate /t REG_DWORD /d 1 /f 2>nul
echo.

echo ============ CURRENT STATE ============
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v EnableFirstLogonAnimation 2>nul
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableStatusMessages 2>nul
echo.

echo ================================================
echo   REBOOT to check.
echo.
echo   NOTE: if UWF is ON these will not survive.
echo   Run 6-DIAGNOSE.cmd to check the filter state.
echo ================================================
echo.
pause
