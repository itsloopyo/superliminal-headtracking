#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Automated release workflow for Superliminal Head Tracking.

.DESCRIPTION
    Runs unattended end to end. `pixi run release <version>` is the
    authorization; there is no second gate and nothing here reads stdin. Every
    precondition (on main, clean tree, valid semver, tag absent) fails fast with
    a one-line diagnostic and a non-zero exit.

.PARAMETER Version
    Concrete X.Y.Z, or one of: major | minor | patch | nightly

.PARAMETER Force
    Ship a release even when every commit since the last tag was filtered as
    noise (writes a maintenance changelog entry instead of aborting).

.EXAMPLE
    pixi run release patch
#>
param(
    [Parameter(Position=0)]
    [string]$Version = "",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$csprojPath = Join-Path $projectDir "src\SuperliminalHeadTracking\SuperliminalHeadTracking.csproj"
$changelogPath = Join-Path $projectDir "CHANGELOG.md"
$manifestPath = Join-Path $projectDir "launcher-manifest.json"
$installCmdPath = Join-Path $projectDir "scripts\install.cmd"

Import-Module (Join-Path $projectDir "cameraunlock-core\powershell\ReleaseWorkflow.psm1") -Force

# Mirrors New-ChangelogFromCommits' insertion so a -Force maintenance entry
# lands in the same place with the same shape.
function Add-ChangelogEntry {
    param([string]$Path, [string]$Entry)
    $changelog = Get-Content $Path -Raw
    if ($changelog -match '(?s)(# Changelog.*?)(## \[)') {
        $changelog = $changelog -replace '(?s)(# Changelog.*?\n\n)', "`$1$Entry"
    } else {
        # No version heading yet, so anchor on the H1's own line rather than on
        # the first newline anywhere after it - the lazy form matched inside the
        # H1 and left the entry glued to the title with the file's prose below it.
        $changelog = $changelog -replace '(?s)(# Changelog[^\n]*\n)', "`$1`n$Entry"
    }
    $changelog = $changelog.TrimEnd() + "`n"
    Set-Content $Path $changelog -NoNewline
}

function Add-MaintenanceChangelogEntry {
    param([string]$Path, [string]$NewVersion)
    $date = Get-Date -Format 'yyyy-MM-dd'
    Add-ChangelogEntry -Path $Path -Entry "## [$NewVersion] - $date`n`n### Changed`n`n- Maintenance release (no user-facing changes).`n`n"
}

# A first release has no tags to generate from, and the CHANGELOG is normally
# already carrying the authored entry under the version the repo has been sitting
# at (0.0.0 for a new mod). Bumping to 0.0.1 must therefore RETITLE that entry
# rather than file a "First release." placeholder above it, which would ship a ZIP
# whose CHANGELOG leads with a stub while the real feature list sits under a
# version that was never released.
function Set-FirstReleaseChangelogEntry {
    param([string]$Path, [string]$NewVersion)
    $date = Get-Date -Format 'yyyy-MM-dd'
    $changelog = Get-Content $Path -Raw

    if ($changelog -match [regex]::Escape("## [$NewVersion]")) {
        Write-Host "  First release - CHANGELOG already has a [$NewVersion] entry, leaving it" -ForegroundColor Gray
        return
    }

    $heading = New-Object regex '(?m)^## \[[^\]]+\].*$'
    if ($heading.IsMatch($changelog)) {
        $changelog = $heading.Replace($changelog, "## [$NewVersion] - $date", 1)
        Set-Content $Path ($changelog.TrimEnd() + "`n") -NoNewline
        Write-Host "  First release - retitled the existing CHANGELOG entry to [$NewVersion]" -ForegroundColor Gray
        return
    }

    Add-ChangelogEntry -Path $Path -Entry "## [$NewVersion] - $date`n`nFirst release.`n`n"
    Write-Host "  First release - inserted a [$NewVersion] CHANGELOG entry" -ForegroundColor Gray
}

Write-Host "=== Superliminal Head Tracking Release ===" -ForegroundColor Cyan
Write-Host ""

# THIRD-PARTY-NOTICES.md names the cameraunlock-core commit compiled into the
# release ZIPs, and bumping the submodule does not touch it. Packaging refuses
# to ship that mismatch, so a bump with no notices edit would stop the release
# in CI with the tag already pushed. Re-sync it here and let this release carry
# the correction.
& git -C $projectDir diff --quiet -- THIRD-PARTY-NOTICES.md
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: THIRD-PARTY-NOTICES.md has uncommitted edits. Commit or discard them, then re-run." -ForegroundColor Red
    exit 1
}
& (Join-Path $projectDir 'cameraunlock-core\scripts\sync-core-notices.ps1') -Repo $projectDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: sync-core-notices.ps1 exited $LASTEXITCODE - fix THIRD-PARTY-NOTICES.md before releasing." -ForegroundColor Red
    exit 1
}
& git -C $projectDir diff --quiet -- THIRD-PARTY-NOTICES.md
if ($LASTEXITCODE -ne 0) {
    & git -C $projectDir commit -q -m 'chore: record the cameraunlock-core commit this build compiles' -- THIRD-PARTY-NOTICES.md
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: could not commit the re-synced THIRD-PARTY-NOTICES.md." -ForegroundColor Red
        exit 1
    }
    Write-Host "THIRD-PARTY-NOTICES.md re-synced to the pinned cameraunlock-core commit." -ForegroundColor Yellow
}

$currentVersion = Get-CsprojVersion $csprojPath

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Current version: $currentVersion" -ForegroundColor White
    Write-Host ""
    Write-Host "Usage:   pixi run release <major|minor|patch|nightly|X.Y.Z>" -ForegroundColor Yellow
    Write-Host "Example: pixi run release patch" -ForegroundColor Yellow
    exit 0
}

if ($Version -eq 'nightly') {
    & (Join-Path $PSScriptRoot 'release-nightly.ps1')
    exit $LASTEXITCODE
}

try {
    $Version = Resolve-ReleaseVersion -Argument $Version -CurrentVersion $currentVersion
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "Error: '$Version' is not valid semver (X.Y.Z)" -ForegroundColor Red
    exit 1
}

$tagName = "v$Version"

$currentBranch = git rev-parse --abbrev-ref HEAD
if ($currentBranch -ne "main") {
    Write-Host "Error: Must be on 'main' branch to release (currently on '$currentBranch')" -ForegroundColor Red
    exit 1
}

$status = git status --porcelain
if ($status) {
    Write-Host "Error: Working directory has uncommitted changes" -ForegroundColor Red
    Write-Host $status -ForegroundColor Gray
    exit 1
}

if (git tag -l $tagName) {
    Write-Host "Error: Tag '$tagName' already exists" -ForegroundColor Red
    exit 1
}

Write-Host "Current version: $currentVersion" -ForegroundColor Gray
Write-Host "New version:     $Version" -ForegroundColor Green
Write-Host ""

# Step 1: generate CHANGELOG from commits since the last tag. This is the gate
# that aborts when every commit was filtered as noise, so run it BEFORE
# mutating any version file - a failure here then leaves a clean tree instead of
# stranding a half-applied bump with no tag.
Write-Host "Generating CHANGELOG from commits..." -ForegroundColor Cyan
if (-not (git tag -l)) {
    # Never written over the top: the authored entry is the only description of the
    # release that exists, and it ships inside the ZIP as well as sitting in the repo.
    Set-FirstReleaseChangelogEntry -Path $changelogPath -NewVersion $Version
} else {
    try {
        New-ChangelogFromCommits `
            -ChangelogPath $changelogPath `
            -Version $Version `
            -ArtifactPaths @(
                "src/SuperliminalHeadTracking/",
                "cameraunlock-core",
                "scripts/install.cmd",
                "scripts/uninstall.cmd"
            )
    } catch {
        if (-not $Force) {
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "No user-facing changes to release. Re-run with -Force for a maintenance release." -ForegroundColor Yellow
            exit 1
        }
        Write-Host "No user-facing commits since last tag - writing maintenance entry (-Force)." -ForegroundColor Yellow
        Add-MaintenanceChangelogEntry -Path $changelogPath -NewVersion $Version
    }
}

# Step 2: bump the version everywhere it is recorded. The csproj is canonical;
# the manifest is what the launcher deploys from, and install.cmd's MOD_VERSION
# is what the install writes into the state file the launcher reads to spot a
# stale install.
Write-Host "Updating version to $Version..." -ForegroundColor Cyan
Set-CsprojVersion $csprojPath $Version

$manifestJson = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifestJson.mod_info.version = $Version
# Set-Content -Encoding UTF8 on Windows PowerShell 5.1 writes a BOM, which
# serde_json rejects. Write through the .NET API with a no-BOM encoder.
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, (($manifestJson | ConvertTo-Json -Depth 10) + "`n"), $utf8NoBom)

$installCmdText = Get-Content $installCmdPath -Raw
if ($installCmdText -notmatch 'set "MOD_VERSION=[^"]+"') { throw "MOD_VERSION line not found in $installCmdPath" }
$installCmdText = $installCmdText -replace 'set "MOD_VERSION=[^"]+"', "set `"MOD_VERSION=$Version`""
Set-Content -Path $installCmdPath -Value $installCmdText -NoNewline

# Step 3: build at the new version.
Write-Host "Building release..." -ForegroundColor Cyan
pixi run build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "Committing changes..." -ForegroundColor Cyan
git add $csprojPath $changelogPath $manifestPath $installCmdPath
git commit -m "Release v$Version"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Commit failed" -ForegroundColor Red
    exit 1
}

Write-Host "Creating tag $tagName..." -ForegroundColor Cyan
git tag -a $tagName -m "Release $tagName"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tag creation failed - nothing pushed." -ForegroundColor Red
    exit 1
}

# Checked one at a time, and main goes first. The tag push is what triggers
# release.yml, so pushing it after a failed branch push would build a release
# from a commit that is not on main - and the release would look successful.
Write-Host "Pushing to GitHub..." -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Push of main failed - the tag $tagName exists locally but was NOT pushed." -ForegroundColor Red
    Write-Host "Fix the push, then run: git push origin main; git push origin $tagName" -ForegroundColor Yellow
    exit 1
}

git push origin $tagName
if ($LASTEXITCODE -ne 0) {
    Write-Host "Push of tag $tagName failed - main is pushed but no release was triggered." -ForegroundColor Red
    Write-Host "Re-run: git push origin $tagName" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Release $tagName initiated." -ForegroundColor Green
Write-Host "GitHub Actions will build and publish the release." -ForegroundColor Yellow
