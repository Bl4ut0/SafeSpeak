[CmdletBinding()]
param(
    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts'
$stagingRoot = Join-Path $artifactRoot 'com.safespeak.streamdeck.sdPlugin'
$artifactRootFull = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$stagingRootFull = [IO.Path]::GetFullPath($stagingRoot)
if (-not $stagingRootFull.StartsWith($artifactRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to stage outside the artifacts directory: $stagingRootFull"
}

& (Join-Path $PSScriptRoot 'Generate-Assets.ps1')

$schema = (Invoke-WebRequest -Uri 'https://schemas.elgato.com/streamdeck/plugins/manifest.json' -UseBasicParsing).Content
$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
$isValid = Get-Content -Raw -LiteralPath $manifestPath | Test-Json -Schema $schema -ErrorAction Stop
if (-not $isValid) { throw 'Stream Deck manifest validation failed.' }

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot | Out-Null

Copy-Item -LiteralPath $manifestPath, (Join-Path $PSScriptRoot 'app.html'), (Join-Path $PSScriptRoot 'app.js') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $stagingRoot -Recurse

if ($SkipPack) {
    Write-Output "Validated staging directory: $stagingRoot"
    return
}

$streamDeckCli = Get-Command streamdeck -ErrorAction SilentlyContinue
if (-not $streamDeckCli) {
    throw 'Stream Deck CLI is required to create the installer. Install it with: npm install -g @elgato/cli@latest'
}

Push-Location $artifactRoot
try {
    & $streamDeckCli.Source validate $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "Stream Deck validation failed with exit code $LASTEXITCODE." }
    & $streamDeckCli.Source pack $stagingRoot --force
    if ($LASTEXITCODE -ne 0) { throw "Stream Deck packaging failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Output "Created Stream Deck installer in $artifactRoot"
