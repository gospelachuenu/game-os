# UWF test harness. Run inside the VM, as admin.
#
# ASCII only in this file - non-ASCII gets mangled crossing into the guest.
#
# Answers the question three console features depend on: can the parent PIN, the
# setup-complete marker, and downloaded-update state survive a reboot while the
# write filter is protecting C:?
#
# Usage:
#   _uwf-test.ps1 status    - report current UWF state
#   _uwf-test.ps1 enable    - turn the filter on + add the console state exclusion
#   _uwf-test.ps1 mark      - write test files (run this, then REBOOT)
#   _uwf-test.ps1 check     - after reboot: report which files survived
#   _uwf-test.ps1 disable   - turn the filter off

param([string]$Action = 'status')

$stateDir  = 'C:\GamingOS\State'      # intended to survive (excluded)
$volatile  = 'C:\UWF-Volatile'        # intended to be wiped (not excluded)

function Show-Status {
    Write-Host "=== UWF configuration ===" -ForegroundColor Cyan
    uwfmgr get-config
    Write-Host ""
    Write-Host "=== Overlay consumption ===" -ForegroundColor Cyan
    uwfmgr overlay get-consumption
}

switch ($Action.ToLower()) {

    'enable' {
        Write-Host "Adding write-through exclusion for $stateDir ..." -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
        uwfmgr volume protect C:
        uwfmgr file add-exclusion $stateDir
        uwfmgr filter enable
        Write-Host ""
        Write-Host "Done. REBOOT for the filter to take effect." -ForegroundColor Yellow
    }

    'mark' {
        $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
        New-Item -ItemType Directory -Path $volatile -Force | Out-Null

        Set-Content "$stateDir\survives.txt" "written at $stamp"
        Set-Content "$volatile\wiped.txt"    "written at $stamp"

        Write-Host "Wrote test files at $stamp" -ForegroundColor Green
        Write-Host "  $stateDir\survives.txt  (excluded - SHOULD survive)"
        Write-Host "  $volatile\wiped.txt     (not excluded - SHOULD vanish)"
        Write-Host ""
        Write-Host "Now REBOOT, then run: _uwf-test.ps1 check" -ForegroundColor Yellow
    }

    'check' {
        Write-Host "=== Results after reboot ===" -ForegroundColor Cyan
        Write-Host ""

        $survived = Test-Path "$stateDir\survives.txt"
        $wiped    = -not (Test-Path "$volatile\wiped.txt")

        if ($survived) {
            $c = Get-Content "$stateDir\survives.txt"
            Write-Host "PASS  excluded file survived: $c" -ForegroundColor Green
        } else {
            Write-Host "FAIL  excluded file did NOT survive - exclusion is not working" -ForegroundColor Red
        }

        if ($wiped) {
            Write-Host "PASS  unexcluded file was wiped - filter is active" -ForegroundColor Green
        } else {
            Write-Host "WARN  unexcluded file still present - is the filter actually on?" -ForegroundColor Yellow
        }

        Write-Host ""
        if ($survived -and $wiped) {
            Write-Host "UWF write-through exclusions WORK. Console state can live in $stateDir" -ForegroundColor Green
        }

        Write-Host ""
        Show-Status
    }

    'disable' {
        uwfmgr filter disable
        Write-Host "Filter disabled. REBOOT to take effect." -ForegroundColor Yellow
    }

    default { Show-Status }
}
