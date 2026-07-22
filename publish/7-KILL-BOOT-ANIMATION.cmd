@echo off
REM Removes the Windows boot animation (the "welcome" spinning dots).
REM Right-click > Run as administrator.
REM
REM This is separate from auto-login: bootux controls the ANIMATION, auto-login
REM controls the SIGN-IN SCREEN. Different settings, different symptoms.
REM
REM Output is shown rather than suppressed - the earlier script hid errors behind
REM Out-Null and reported success regardless.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   Must be run as ADMINISTRATOR - right-click, Run as administrator.
    echo.
    pause
    exit /b 1
)

echo === Applying to {current} ===
bcdedit /set {current} bootux disabled
bcdedit /set {current} quietboot on
echo.

echo === Applying to {globalsettings} ===
bcdedit /set {globalsettings} bootux disabled
echo.

echo === Applying to {bootmgr} ===
bcdedit /set {bootmgr} nointegritychecks off
bcdedit /timeout 0
echo.

echo === RESULT: current ===
bcdedit /enum {current} | findstr /i "bootux quietboot"
echo.
echo === RESULT: globalsettings ===
bcdedit /enum {globalsettings} | findstr /i "bootux"
echo.

echo ================================================
echo   If you see "bootux Disabled" above, it applied.
echo   REBOOT to check.
echo ================================================
echo.
pause
