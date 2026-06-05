param(
    [string]$ProjectDir = (Join-Path (Join-Path $PSScriptRoot '..') 'MusicSalesApp.Maui'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$AppSettingsEnvironment
)

$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
$csproj = Join-Path $ProjectDir 'MusicSalesApp.Maui.csproj'
$packageName = 'net.streamtunes.musicsalesapp.maui'
$binDir = Join-Path (Join-Path (Join-Path $ProjectDir 'bin') $Configuration) 'net10.0-android'
$objDir = Join-Path (Join-Path (Join-Path $ProjectDir 'obj') $Configuration) 'net10.0-android'

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

# --- Step 2: Force rebuild/package ---
Write-Host '=== Building package (clean) ===' -ForegroundColor Cyan
$buildArgs = @(
    'build'
    $csproj
    '-f'
    'net10.0-android'
    '-c'
    $Configuration
    '--no-incremental'
    '/p:EmbedAssembliesIntoApk=true'
    '/p:AndroidPackageFormat=apk'
)

if (-not [string]::IsNullOrWhiteSpace($AppSettingsEnvironment)) {
    $buildArgs += "/p:AppSettingsEnvironment=$AppSettingsEnvironment"
    Write-Host "  Using appsettings environment: $AppSettingsEnvironment" -ForegroundColor Yellow
}

Write-Host "  Build configuration: $Configuration" -ForegroundColor Yellow
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed!' -ForegroundColor Red
    exit 1
}

# --- Step 3: Install the signed APK ---
Write-Host '=== Installing app ===' -ForegroundColor Cyan
$apk = Get-ChildItem -Path $binDir -Recurse -File -Filter "$packageName-Signed.apk" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $apk) {
    $apk = Get-ChildItem -Path $binDir -Recurse -File -Filter '*.apk' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if ($null -eq $apk) {
    Write-Host "No APK was produced under $binDir" -ForegroundColor Red
    exit 1
}

Write-Host "  APK: $($apk.FullName)" -ForegroundColor Yellow
$installOutput = & $adb install -r -d $apk.FullName 2>&1
$installExitCode = $LASTEXITCODE
$installOutput | ForEach-Object { Write-Host "  $_" }

if ($installExitCode -ne 0) {
    $installText = $installOutput -join "`n"
    if ($installText -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE|INSTALL_FAILED_VERSION_DOWNGRADE') {
        Write-Host '  Existing install is incompatible; uninstalling and reinstalling once.' -ForegroundColor Yellow
        & $adb uninstall $packageName | Out-Host
        $installOutput = & $adb install -r -d $apk.FullName 2>&1
        $installExitCode = $LASTEXITCODE
        $installOutput | ForEach-Object { Write-Host "  $_" }
    }
}

if ($installExitCode -ne 0) {
    Write-Host 'Install failed!' -ForegroundColor Red
    exit 1
}

# --- Step 4: Launch the app ---
Write-Host '=== Launching app ===' -ForegroundColor Cyan
& $adb shell monkey -p $packageName -c android.intent.category.LAUNCHER 1

Write-Host 'Done!' -ForegroundColor Green
