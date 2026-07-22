@echo off
REM Double-click this in the VM. Writes the UWF test files, then tells you to reboot.
powershell -ExecutionPolicy Bypass -File "%~dp0_uwf-test.ps1" mark
echo.
echo ================================================
echo   NOW REBOOT THE VM, THEN RUN 2-CHECK.cmd
echo ================================================
echo.
pause
