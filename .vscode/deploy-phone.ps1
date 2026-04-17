param(
    [string]$ProjectDir = (Join-Path (Join-Path $PSScriptRoot '..') 'MusicSalesApp.Maui')
)

$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
$csproj = Join-Path $ProjectDir 'MusicSalesApp.Maui.csproj'
$packageName = 'net.streamtunes.musicsalesapp.maui'
$binDir = Join-Path $ProjectDir 'bin' 'Debug' 'net10.0-android'
$objDir = Join-Path $ProjectDir 'obj' 'Debug' 'net10.0-android'

# --- Step 1: Clean build caches ---
Write-Host '=== Cleaning build caches ===' -ForegroundColor Cyan
foreach ($dir in @($binDir, $objDir)) {
    if (Test-Path $dir) {
        try {
            Remove-Item -Recurse -Force $dir -ErrorAction Stop
            Write-Host "  Removed $dir" -ForegroundColor Yellow
        } catch {
            Write-Host "  Could not fully remove $dir (files may be locked). Continuing..." -ForegroundColor Yellow
        }
    }
}

# --- Step 2: Uninstall old app from phone ---
Write-Host '=== Uninstalling old app ===' -ForegroundColor Cyan
& $adb uninstall $packageName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  Uninstalled $packageName" -ForegroundColor Yellow
} else {
    Write-Host "  App was not installed (OK)" -ForegroundColor Gray
}

# --- Step 3: Force rebuild and install ---
Write-Host '=== Building and installing (clean) ===' -ForegroundColor Cyan
dotnet build $csproj -f net10.0-android -c Debug -t:Install --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build/install failed!' -ForegroundColor Red
    exit 1
}

# --- Step 4: Launch the app ---
Write-Host '=== Launching app ===' -ForegroundColor Cyan
& $adb shell monkey -p $packageName -c android.intent.category.LAUNCHER 1

Write-Host 'Done!' -ForegroundColor Green
