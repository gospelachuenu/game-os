@echo off
REM Step 1 of the reconfigure dance: turn the write filter OFF so settings stick.
REM Right-click > Run as administrator.
REM
REM This is the same three-boot sequence the console's own self-update needs:
REM   filter off -> reboot -> make changes -> reboot -> filter on -> reboot

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   Must be run as ADMINISTRATOR - right-click, Run as administrator.
    echo.
    pause
    exit /b 1
)

uwfmgr filter disable

echo.
echo ================================================
echo   REBOOT, then run 3-STEALTH-BOOT.cmd as admin
echo ================================================
echo.
pause
