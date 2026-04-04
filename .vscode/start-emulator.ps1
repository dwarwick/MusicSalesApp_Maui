$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
$emulator = Join-Path $env:LOCALAPPDATA "Android\Sdk\emulator\emulator.exe"

$devices = & $adb devices 2>&1 | Out-String
if ($devices -match 'emulator-\d+\s+device') {
    Write-Host 'Emulator already running'
    exit 0
}

Write-Host 'Starting emulator...'
Start-Process $emulator -ArgumentList '-avd', 'Pixel_API_35', '-gpu', 'auto', '-no-snapshot-load' -WindowStyle Normal

$timeout = 120
$elapsed = 0
while ($elapsed -lt $timeout) {
    Start-Sleep -Seconds 5
    $elapsed += 5
    $state = & $adb shell getprop sys.boot_completed 2>&1 | Out-String
    if ($state.Trim() -eq '1') {
        Write-Host "Emulator booted after ${elapsed}s"
        exit 0
    }
}
Write-Host 'WARNING: Emulator boot timed out'
exit 1
