#!/usr/bin/env pwsh
# Populates src/SuperliminalHeadTracking/libs/ for a game-free build, from REPO
# FILES ONLY - no Superliminal install required:
#   - BepInEx.dll, 0Harmony.dll : extracted from the vendored BepInEx zip
#   - UnityEngine*.dll          : compiled by the shared stub builder in
#                                 cameraunlock-core/csharp/stubs

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot  = Split-Path -Parent $scriptDir
$libsPath     = Join-Path $projectRoot 'src\SuperliminalHeadTracking\libs'
$vendorZip    = Join-Path $projectRoot 'vendor\bepinex\BepInEx_win_x64.zip'
$stubBuilder  = Join-Path $projectRoot 'cameraunlock-core\csharp\stubs\build-unity-stubs.ps1'

if (-not (Test-Path $vendorZip))   { throw "Vendored BepInEx not found at $vendorZip" }
if (-not (Test-Path $stubBuilder)) { throw "Shared stub builder not found at $stubBuilder - is the cameraunlock-core submodule checked out?" }

New-Item -ItemType Directory -Path $libsPath -Force | Out-Null

Write-Host "Bootstrapping build dependencies (no game install required)..." -ForegroundColor Cyan

# Wipe libs/ so stale game DLLs from a past deploy can't mask CI parity. The
# failure is not swallowed: a locked DLL is the ordinary way this goes wrong, and
# discarding the error leaves the exact stale assembly the wipe exists to remove,
# now silently.
Get-ChildItem -Path $libsPath -Force | Remove-Item -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempDir = Join-Path $env:TEMP ("sl-bep-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($vendorZip, $tempDir)
    foreach ($dll in @('BepInEx.dll', '0Harmony.dll')) {
        $src = Join-Path $tempDir "BepInEx\core\$dll"
        if (-not (Test-Path $src)) { throw "$dll not found in vendor zip at BepInEx\core\" }
        Copy-Item $src (Join-Path $libsPath $dll) -Force
        Write-Host "  BepInEx: $dll" -ForegroundColor Gray
    }
} finally {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

& $stubBuilder -OutputPath $libsPath -TargetFramework net472
if ($LASTEXITCODE -ne 0) { throw "Shared Unity stub build failed" }

Write-Host "Build dependencies ready." -ForegroundColor Green
