# Stealth boot: no Windows logo, no login screen, straight into the console.
# Run inside the VM as ADMIN. ASCII only in this file.
#
# plan.md 1.3. Two separate things:
#
#   1. bootux disabled  - removes the Windows boot animation (the spinning dots)
#   2. auto-login       - removes the welcome/sign-in screen
#
# Combined with shell replacement, the whole visible boot becomes:
#   black -> black -> console boot screen -> dashboard
#
# SECURITY NOTE: this writes the password to the registry in PLAINTEXT under
# Winlogon\DefaultPassword. Anyone with disk access can read it. That is
# acceptable for a VM test and for a console with no meaningful local secrets,
# but the REAL machine should use Sysinternals Autologon.exe instead, which
# stores it encrypted in the LSA. Same effect, no plaintext.

param(
    [string]$Username = 'console',
    [string]$Password
)

if (-not $Password) {
    Write-Host "Usage: _stealth-boot.ps1 -Username <name> -Password <pass>" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Current Windows user is: $env:USERNAME" -ForegroundColor Cyan
    Write-Host "Pass that account's password so Windows can log itself in." -ForegroundColor Cyan
    return
}

$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

Write-Host "Disabling the Windows boot animation ..." -ForegroundColor Cyan
bcdedit /set "{globalsettings}" bootux disabled | Out-Null
bcdedit /set "{current}" quietboot on | Out-Null

Write-Host "Configuring auto-login for '$Username' ..." -ForegroundColor Cyan
Set-ItemProperty $winlogon -Name AutoAdminLogon    -Value "1"        -Type String
Set-ItemProperty $winlogon -Name DefaultUserName   -Value $Username  -Type String
Set-ItemProperty $winlogon -Name DefaultPassword   -Value $Password  -Type String
Set-ItemProperty $winlogon -Name DefaultDomainName -Value $env:COMPUTERNAME -Type String

# Windows 10 shows a lock screen before the sign-in prompt even when auto-login
# is on; this suppresses it so nothing flashes up on the way through.
$personal = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
New-Item -Path $personal -Force | Out-Null
Set-ItemProperty $personal -Name NoLockScreen -Value 1 -Type DWord

Write-Host ""
Write-Host "Done. Reboot to see it." -ForegroundColor Green
Write-Host ""
Write-Host "Expected boot: black -> black -> console boot screen -> dashboard" -ForegroundColor Cyan
Write-Host ""
Write-Host "If UWF is enabled these registry writes may not survive a reboot." -ForegroundColor Yellow
Write-Host "If auto-login stops working after one boot, that is why - the settings" -ForegroundColor Yellow
Write-Host "need applying with the filter disabled, or via a registry exclusion." -ForegroundColor Yellow
