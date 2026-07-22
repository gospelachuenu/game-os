@echo off
REM Reports what is actually configured, so we stop guessing.
REM Right-click > Run as administrator.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   Must be run as ADMINISTRATOR - right-click, Run as administrator.
    echo.
    pause
    exit /b 1
)

echo ============ UWF STATE ============
uwfmgr get-config | findstr /i "Filter state Current Next"
echo.

echo ============ AUTO-LOGIN ============
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon 2>nul
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName 2>nul
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword 2>nul
echo.

echo ============ SHELL ============
reg query "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell 2>nul
echo.

echo ============ BOOT ANIMATION ============
bcdedit /enum "{globalsettings}" | findstr /i bootux
echo.

echo ============ WHAT THIS MEANS ============
echo If AutoAdminLogon is missing or 0, the stealth-boot script did not stick.
echo If UWF "Filter state" is ON, that is why - changes go to the RAM overlay
echo and vanish on reboot. Disable it, REBOOT, then reconfigure.
echo.
pause
