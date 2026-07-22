# Restores clipboard sharing in the VM and makes it stick across reboots.
#
# ASCII only in this file.
#
# Why this is needed: VBoxTray.exe is what implements clipboard sharing on the
# guest side, and Windows normally starts it via the SHELL. Once the console UI
# replaces explorer.exe as the shell, nothing starts it, so copy/paste silently
# stops working even though VirtualBox reports the clipboard as enabled.
#
# Fix: locate VBoxTray wherever Guest Additions put it, start it now, and add it
# to the Run key - which Winlogon processes independently of the shell.

Write-Host "Locating VBoxTray.exe ..." -ForegroundColor Cyan

$candidates = @(
    "$env:SystemRoot\System32\VBoxTray.exe",
    "${env:ProgramFiles}\Oracle\VirtualBox Guest Additions\VBoxTray.exe",
    "${env:ProgramFiles(x86)}\Oracle\VirtualBox Guest Additions\VBoxTray.exe"
)

$tray = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $tray) {
    Write-Host "Not in the usual places - searching C:\ (this takes a moment) ..." -ForegroundColor Yellow
    $found = Get-ChildItem C:\ -Filter VBoxTray.exe -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { $tray = $found.FullName }
}

if (-not $tray) {
    Write-Host "VBoxTray.exe not found. Guest Additions may not be fully installed." -ForegroundColor Red
    Write-Host "Reinstall via Devices > Insert Guest Additions CD image." -ForegroundColor Yellow
    return
}

Write-Host "Found: $tray" -ForegroundColor Green

# Start it now so the clipboard works in this session.
Get-Process VBoxTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process $tray
Write-Host "Started - clipboard should work now." -ForegroundColor Green

# And make it survive reboots. The Run key is read by Winlogon, not by the shell,
# so it still fires when the console UI is the shell.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty $runKey -Name VBoxTray -Value $tray
Write-Host "Added to startup - will run on every boot." -ForegroundColor Green

Write-Host ""
Write-Host "NOTE: with UWF enabled, this registry write may not survive a reboot," -ForegroundColor Yellow
Write-Host "since the registry lives on the protected volume. If clipboard breaks" -ForegroundColor Yellow
Write-Host "again after rebooting, that is a useful finding - tell Claude." -ForegroundColor Yellow
