@echo off
REM Double-click this AFTER rebooting. Reports whether the UWF exclusion worked.
powershell -ExecutionPolicy Bypass -File "%~dp0_uwf-test.ps1" check
echo.
echo ================================================
echo   Screenshot this and send it to Claude
echo ================================================
echo.
pause
