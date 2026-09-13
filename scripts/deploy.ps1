param([string]$GivenPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = [xml](Get-Content "$projectRoot/src/SuperliminalHeadTracking/SuperliminalHeadTracking.csproj" -Raw)
$version = $project.SelectSingleNode('//Version').InnerText
$package = "$projectRoot/release/SuperliminalHeadTracking-v$version-installer.zip"
$staging = Join-Path $projectRoot ('.lab/deploy-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $package -DestinationPath $staging
$installer = Join-Path $staging 'install.cmd'
if ($GivenPath) {
    & $installer $GivenPath /y
} else {
    & $installer /y
}
if ($LASTEXITCODE -ne 0) { throw "Superliminal installation failed (exit $LASTEXITCODE)." }
