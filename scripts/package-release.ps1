#!/usr/bin/env pwsh
#Requires -Version 5.1
# Thin wrapper - packaging lives in
# cameraunlock-core/scripts/package-bepinex-mod.ps1, which stages the installer
# ZIP (install.cmd + shared/ + plugins/ + vendor/bepinex/ + docs +
# launcher-manifest.json) and the Nexus ZIP (BepInEx/plugins/ + notices).
#
# The vendored BepInEx copy is consumed exactly as committed. Refreshing it is
# `pixi run update-deps`, a manual action with a commit attached.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

$monoProject = [xml](Get-Content "$projectDir/src/SuperliminalHeadTracking/SuperliminalHeadTracking.csproj" -Raw)
$il2cppProject = [xml](Get-Content "$projectDir/src/SuperliminalHeadTracking.Il2Cpp/SuperliminalHeadTracking.Il2Cpp.csproj" -Raw)
if ($monoProject.SelectSingleNode('//Version').InnerText -ne $il2cppProject.SelectSingleNode('//Version').InnerText) {
    throw 'Mono and IL2CPP project versions must match.'
}

$zips = & "$projectDir/cameraunlock-core/scripts/package-bepinex-mod.ps1" `
    -ModName "SuperliminalHeadTracking" `
    -CsprojPath "src/SuperliminalHeadTracking/SuperliminalHeadTracking.csproj" `
    -BuildOutputDir "src/SuperliminalHeadTracking/bin/Release/net472" `
    -ModDlls @("SuperliminalHeadTracking.dll", "CameraUnlock.Core.dll", "CameraUnlock.Core.Unity.dll") `
    -ProjectRoot $projectDir `
    -CreateNexusZip

Add-Type -AssemblyName System.IO.Compression.FileSystem
$extra = [ordered]@{
    'plugins-il2cpp/SuperliminalHeadTracking.dll' = 'src/SuperliminalHeadTracking.Il2Cpp/bin/Release/net6.0/SuperliminalHeadTracking.dll'
    'plugins-il2cpp/CameraUnlock.Core.dll' = 'src/SuperliminalHeadTracking.Il2Cpp/bin/Release/net6.0/CameraUnlock.Core.dll'
    'vendor/bepinex-il2cpp/BepInEx_UnityIL2CPP_x64.zip' = 'vendor/bepinex-il2cpp/BepInEx_UnityIL2CPP_x64.zip'
    'vendor/bepinex-il2cpp/LICENSE' = 'vendor/bepinex-il2cpp/LICENSE'
    'vendor/bepinex-il2cpp/README.md' = 'vendor/bepinex-il2cpp/README.md'
}
foreach ($name in @('Il2CppInterop', 'Cpp2IL', 'Disarm', 'AsmResolver',
        'AssetRipper.CIL', 'AssetRipper.Primitives', 'Iced', 'Capstone.NET',
        'Dobby', 'SemanticVersioning', 'dotnet-runtime')) {
    $extra["licenses/$name-LICENSE.txt"] = "licenses/$name-LICENSE.txt"
}

$archive = [IO.Compression.ZipFile]::Open($zips.GithubZip, 'Update')
try {
    foreach ($entry in $extra.GetEnumerator()) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $projectDir $entry.Value), $entry.Key,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
}

& node "$projectDir/cameraunlock-core/scripts/validate-manifest.mjs" $zips.GithubZip
if ($LASTEXITCODE -ne 0) { throw 'Installer manifest validation failed.' }
$zips
