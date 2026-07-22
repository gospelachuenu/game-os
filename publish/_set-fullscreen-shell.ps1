# Run inside the VM. Updates the console build and points the Windows shell at it
# in FULLSCREEN mode.
#
# ASCII only in this file - non-ASCII gets mangled crossing into the guest.
#
# The --fullscreen flag is what makes the window borderless and maximised. Without
# it the same exe runs as an ordinary window, which is how it runs on the dev
# laptop. Use --kiosk instead for always-on-top as well.
#
# ESCAPE HATCH: press F12 in the running app to drop back to a normal window.

$target = 'C:\GamingOS'
$source = '\\vboxsvr\publish'

Write-Host "Updating console build in $target ..." -ForegroundColor Cyan

if (-not (Test-Path $source)) {
    Write-Host "Shared folder not found at $source" -ForegroundColor Red
    return
}

# The app may be running as the current shell; stop it before overwriting.
Get-Process UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item "$source\*" $target -Recurse -Force
Write-Host "Build updated." -ForegroundColor Green

# Winlogon reads this at logon. The quotes matter: without them the argument
# would be treated as part of the executable path and the shell would fail to
# start, leaving a black screen with nothing on it.
$shell = '"C:\GamingOS\UI.exe" --fullscreen'
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name Shell -Value $shell

$current = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name Shell).Shell
Write-Host ""
Write-Host "Shell set to: $current" -ForegroundColor Green
Write-Host ""
Write-Host "Reboot the VM to see it. Press F12 in the app to leave fullscreen." -ForegroundColor Yellow
Write-Host "If it fails to start: Ctrl+Shift+Esc, or restore a snapshot." -ForegroundColor Yellow
