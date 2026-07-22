# Run inside the VM to copy the console build to a real local path.
#
# The build must live somewhere local: the shell registry value is read at
# logon, before network shares are reliably available, so a \\vboxsvr path
# would fail exactly when it matters.
#
# ASCII only in this file. Non-ASCII characters get mangled crossing into the
# guest and break the script with a "string is missing the terminator" error.

$target = 'C:\GamingOS'
$source = '\\vboxsvr\publish'

Write-Host "Copying console build to $target ..." -ForegroundColor Cyan

if (-not (Test-Path $source)) {
    Write-Host "Shared folder not found at $source" -ForegroundColor Red
    Write-Host "Add it via Devices > Shared Folders (name it 'publish', tick auto-mount)." -ForegroundColor Yellow
    return
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item "$source\*" $target -Recurse -Force

$count = (Get-ChildItem $target -Recurse -File).Count
Write-Host "Copied $count files." -ForegroundColor Green

if (Test-Path "$target\UI.exe") {
    Write-Host ""
    Write-Host "Starting the console UI..." -ForegroundColor Cyan
    Start-Process "$target\UI.exe"
} else {
    Write-Host "UI.exe missing from $target - copy may have failed." -ForegroundColor Red
}
