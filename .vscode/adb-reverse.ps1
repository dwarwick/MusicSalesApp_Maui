# Sets up ADB reverse port forwarding so a USB-connected Android device
# can reach the PC's localhost (dev web server + SignalR).
# Re-run this each time you reconnect the USB cable.

$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
if (-not (Test-Path $adb)) {
    Write-Host "adb not found at $adb" -ForegroundColor Red
    exit 1
}

$devices = & $adb devices | Select-String "device$"
if (-not $devices) {
    Write-Host "No Android device connected. Plug in your phone with USB debugging enabled." -ForegroundColor Yellow
    exit 1
}

Write-Host "Setting up ADB reverse port forwarding..." -ForegroundColor Cyan
& $adb reverse tcp:5162 tcp:5162
& $adb reverse tcp:7173 tcp:7173
Write-Host ""
Write-Host "Active reverse forwards:" -ForegroundColor Green
& $adb reverse --list
Write-Host ""
Write-Host "Done! Your phone's localhost now routes to this PC." -ForegroundColor Green
