param(
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot '..') 'streamtunes-logs' 'latest-on-device'),
    [string]$RemoteLogDir = '/sdcard/Android/data/net.streamtunes.musicsalesapp.maui/files/logs'
)

$packageName = 'net.streamtunes.musicsalesapp.maui'
$defaultAdb = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe' } else { $null }

function Resolve-AdbPath {
    param([string]$PreferredPath)

    if ($PreferredPath -and (Test-Path $PreferredPath)) {
        return $PreferredPath
    }

    $adbCommand = Get-Command adb -ErrorAction SilentlyContinue
    if ($adbCommand) {
        return $adbCommand.Source
    }

    throw 'ADB was not found. Install Android platform-tools or add adb to PATH.'
}

function Get-ConnectedDeviceSerials {
    param([string]$AdbPath)

    $deviceLines = & $AdbPath devices
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to query connected Android devices with adb.'
    }

    $serials = @($deviceLines | Where-Object { $_ -match "`tdevice$" } | ForEach-Object { ($_ -split "`t")[0].Trim() })
    return ,$serials
}

$adb = Resolve-AdbPath -PreferredPath $defaultAdb
$connectedDevices = @(Get-ConnectedDeviceSerials -AdbPath $adb)

if ($connectedDevices.Count -eq 0) {
    throw 'No connected Android device was found.'
}

if ($connectedDevices.Count -gt 1) {
    throw "Multiple Android devices are connected: $($connectedDevices -join ', '). Disconnect extras or set ANDROID_SERIAL first."
}

$serial = $connectedDevices[0]
Write-Host "=== Pulling latest runtime log from $serial ===" -ForegroundColor Cyan
Write-Host "Package: $packageName" -ForegroundColor DarkGray

$remoteFile = (& $adb -s $serial shell "ls -t $RemoteLogDir/streamtunes-*.log 2>/dev/null | head -n 1" | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteFile)) {
    throw "No runtime log files were found on the device under $RemoteLogDir."
}

$outputDirectoryInfo = New-Item -ItemType Directory -Path $OutputDir -Force
$outputDirectory = $outputDirectoryInfo.FullName
$localFileName = [System.IO.Path]::GetFileName($remoteFile)
$localFilePath = Join-Path $outputDirectory $localFileName
$latestAliasPath = Join-Path $outputDirectory 'streamtunes-latest-device.log'

Write-Host "Remote log: $remoteFile" -ForegroundColor Yellow
Write-Host "Local log:  $localFilePath" -ForegroundColor Yellow

& $adb -s $serial pull $remoteFile $localFilePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $localFilePath)) {
    throw 'adb pull failed before the runtime log could be saved locally.'
}

Copy-Item -Path $localFilePath -Destination $latestAliasPath -Force

Write-Host 'Done!' -ForegroundColor Green
Write-Host "Newest on-device runtime log saved to: $localFilePath" -ForegroundColor Green
Write-Host "Stable latest-log alias updated at: $latestAliasPath" -ForegroundColor Green