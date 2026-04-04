param(
    [string]$ProjectDir = (Join-Path (Join-Path $PSScriptRoot '..') 'MusicSalesApp.Maui')
)

$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
$csproj = Join-Path $ProjectDir 'MusicSalesApp.Maui.csproj'
$apk = Join-Path $ProjectDir 'bin' 'Debug' 'net10.0-android' 'com.companyname.musicsalesapp.maui-Signed.apk'

Write-Host '=== Building and Installing ===' -ForegroundColor Cyan
# Uninstall first to avoid stale resource ID crashes from incremental installs
& $adb -s emulator-5554 uninstall com.companyname.musicsalesapp.maui 2>$null
# Use dotnet build -t:Install to handle Fast Deployment properly
# (pushes assemblies separately in Debug builds, unlike plain 'adb install')
dotnet build $csproj -f net10.0-android -c Debug -t:Install -p:AdbTarget=-s%20emulator-5554
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build/install failed!' -ForegroundColor Red
    exit 1
}

Write-Host '=== Launching app ===' -ForegroundColor Cyan
& $adb -s emulator-5554 shell am start -n com.companyname.musicsalesapp.maui/crc64e03c33350aba05b9.MainActivity

Write-Host 'Done!' -ForegroundColor Green
