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

& "$projectDir/cameraunlock-core/scripts/package-bepinex-mod.ps1" `
    -ModName "SuperliminalHeadTracking" `
    -CsprojPath "src/SuperliminalHeadTracking/SuperliminalHeadTracking.csproj" `
    -BuildOutputDir "src/SuperliminalHeadTracking/bin/Release/net472" `
    -ModDlls @("SuperliminalHeadTracking.dll", "CameraUnlock.Core.dll", "CameraUnlock.Core.Unity.dll") `
    -ProjectRoot $projectDir `
    -CreateNexusZip
