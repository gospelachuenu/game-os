@echo off
REM Final step: freeze the configured system by turning the write filter back on.
REM Right-click > Run as administrator.
REM
REM Run this only once auto-login and the stealth boot are confirmed working,
REM because after this the machine stops accepting permanent changes again.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   Must be run as ADMINISTRATOR - right-click, Run as administrator.
    echo.
    pause
    exit /b 1
)

REM Re-assert the console state exclusion in case it was cleared.
uwfmgr volume protect C:
uwfmgr file add-exclusion C:\GamingOS\State
uwfmgr filter enable

echo.
echo ================================================
echo   REBOOT. System is now frozen except
echo   C:\GamingOS\State
echo ================================================
echo.
pause
