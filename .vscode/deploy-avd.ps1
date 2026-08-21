# Builds the Debug app and installs it on a NAMED AVD, booting it first if needed.
#
# Why not deploy-android.ps1: that one hardcodes `emulator-5554`, which is whichever emulator
# booted first. With a phone and a tablet AVD both defined, the one you want is often 5556 and the
# install silently lands on the wrong device. This resolves the serial from the AVD name instead.
param(
    [string]$AvdName = 'Tablet_API_35',
    [string]$ProjectDir = (Join-Path (Join-Path $PSScriptRoot '..') 'MusicSalesApp.Maui'),
    [int]$BootTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

# MSBuild keeps worker processes alive between builds for speed, and they hold the APK packaging
# temp file. After several back-to-back Android builds that surfaces as
# "XABAA7000: Renaming temporary file failed: Permission denied". A deploy that boots an emulator
# is not in a tight edit-build loop, so the startup that node reuse saves is worth less than the
# lock it risks.
$env:MSBUILDDISABLENODEREUSE = '1'

$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$emulator = Join-Path $sdk 'emulator\emulator.exe'
$package = 'net.streamtunes.musicsalesapp.maui'
$csproj = Join-Path $ProjectDir 'MusicSalesApp.Maui.csproj'

# Map a running emulator serial back to its AVD name. `adb devices` only gives serials, and the
# AVD name is the one thing that tells a tablet from a phone.
function Get-SerialForAvd([string]$name) {
    $lines = & $adb devices | Select-Object -Skip 1
    foreach ($line in $lines) {
        if ($line -notmatch '^(emulator-\d+)\s+device') { continue }
        $serial = $Matches[1]
        $running = (& $adb -s $serial emu avd name 2>$null | Select-Object -First 1)
        if ($running) { $running = $running.Trim() }
        if ($running -eq $name) { return $serial }
    }
    return $null
}

$serial = Get-SerialForAvd $AvdName

if (-not $serial) {
    Write-Host "=== Booting $AvdName ===" -ForegroundColor Cyan
    Start-Process -FilePath $emulator -ArgumentList @('-avd', $AvdName) -WindowStyle Normal | Out-Null

    $deadline = (Get-Date).AddSeconds($BootTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        $serial = Get-SerialForAvd $AvdName
        if (-not $serial) { continue }
        # Present is not the same as ready: installing before sys.boot_completed fails or hangs.
        $booted = (& $adb -s $serial shell getprop sys.boot_completed 2>$null)
        if ($booted -and $booted.Trim() -eq '1') { break }
        $serial = $null
    }

    if (-not $serial) {
        Write-Host "$AvdName did not finish booting within $BootTimeoutSeconds seconds." -ForegroundColor Red
        exit 1
    }
}

Write-Host "=== $AvdName is $serial ===" -ForegroundColor Cyan

# Uninstall first: incremental installs over a changed resource set crash at startup.
& $adb -s $serial uninstall $package 2>$null | Out-Null

Write-Host '=== Building and installing ===' -ForegroundColor Cyan
# EmbedAssembliesIntoApk is not optional here. Debug builds default to Fast Deployment, which keeps
# the managed assemblies OUT of the APK and pushes them to .__override__/<abi> separately. The
# uninstall above wipes that directory, and an incremental build then judges the push step up to
# date and skips it - so the APK installs, finds no assemblies and aborts in monodroid before a
# line of managed code runs. No stack trace, no app log, just a process that vanishes:
#     F monodroid: No assemblies found in '.../files/.__override__/arm64-v8a'. Exiting...
# Embedding them costs a slower install and removes the failure entirely.
dotnet build $csproj -f net10.0-android -c Debug -t:Install -p:EmbedAssembliesIntoApk=true -p:AdbTarget="-s%20$serial"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build/install failed!' -ForegroundColor Red
    exit 1
}

Write-Host '=== Launching ===' -ForegroundColor Cyan
& $adb -s $serial shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null

Write-Host ''
Write-Host "Running on $AvdName ($serial)." -ForegroundColor Green
Write-Host 'Rotate with Ctrl+Left / Ctrl+Right, or the rotate buttons on the emulator toolbar.' -ForegroundColor Green
Write-Host 'Medium Tablet is 800x1280dp: portrait is 800 (single column), landscape 1280 (two column).' -ForegroundColor Green
