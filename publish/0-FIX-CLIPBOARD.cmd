@echo off
REM Double-click to restore clipboard sharing after a reboot.
powershell -ExecutionPolicy Bypass -File "%~dp0_fix-clipboard.ps1"
pause
