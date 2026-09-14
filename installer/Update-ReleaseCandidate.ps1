<#
.SYNOPSIS
    Prepares and synchronizes a new SafeSpeak release candidate across all
    authoritative version files, setup guide versioning, and narrator highlights.

.DESCRIPTION
    Ensures that every release candidate automatically:
    1. Updates SafeSpeakVersion and SafeSpeakStoreVersion in Directory.Build.props.
    2. Increments CurrentGuideVersion and updates CurrentHighlights in ReleaseUpdateInfo.cs
       (which also automatically updates AppSettings.CurrentSetupGuideVersion and the opening
       dialog's narrator announcement).
    3. Synchronizes release documentation and contract test assertions.

.PARAMETER PackageVersion
    Four-part release version (e.g. 1.0.10.0 or 1.1.0.0). The final component must be 0 for Store compliance.

.PARAMETER Highlights
    Array of user-facing release highlight strings describing what's new in this candidate.

.PARAMETER GuideVersion
    Optional setup guide version integer. Defaults to incrementing the current version by 1.

.PARAMETER WhatIf
    Dry-run mode: prints changes without modifying files.

.EXAMPLE
    ./installer/Update-ReleaseCandidate.ps1 `
      -PackageVersion 1.0.10.0 `
      -Highlights @(
        "Platform: Added Discord community connector preview.",
        "Voice: Reduced synthesis latency by 25% on local engine.",
        "Fixes: Resolved audio device switching issue when unplugging headphones."
      )
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Highlights,

    [Parameter(Mandatory = $false)]
    [int]$GuideVersion = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent

# 1. Resolve files
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'
$releaseInfoPath = Join-Path $repoRoot 'src\SafeSpeak.Core\Models\ReleaseUpdateInfo.cs'
$readmePath = Join-Path $repoRoot 'installer\README.md'
$contractsPath = Join-Path $repoRoot 'tests\SafeSpeak.App.Contracts.Tests\ReleaseEntryPointContractTests.cs'

foreach ($file in @($versionPropsPath, $releaseInfoPath, $readmePath, $contractsPath)) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Required release file not found: $file"
    }
}

# 2. Read existing version
$versionPropsContent = Get-Content -LiteralPath $versionPropsPath -Raw
if ($versionPropsContent -match '<SafeSpeakVersion>([^<]+)</SafeSpeakVersion>') {
    $currentVersion = $Matches[1]
} else {
    throw "Could not determine current SafeSpeakVersion in $versionPropsPath"
}

# 3. Read and compute GuideVersion
$releaseInfoContent = Get-Content -LiteralPath $releaseInfoPath -Raw
if ($releaseInfoContent -match 'public const int CurrentGuideVersion = (\d+);') {
    $existingGuideVersion = [int]$Matches[1]
} else {
    throw "Could not determine CurrentGuideVersion in $releaseInfoPath"
}

if ($GuideVersion -le 0) {
    $GuideVersion = $existingGuideVersion + 1
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Preparing SafeSpeak Release Candidate: $PackageVersion" -ForegroundColor Cyan
Write-Host " Previous Version: $currentVersion -> New Version: $PackageVersion" -ForegroundColor Yellow
Write-Host " Setup Guide Version: $existingGuideVersion -> $GuideVersion" -ForegroundColor Yellow
Write-Host " Highlights ($($Highlights.Count) items):" -ForegroundColor Yellow
foreach ($h in $Highlights) {
    Write-Host "   * $h" -ForegroundColor Gray
}
Write-Host "==================================================" -ForegroundColor Cyan

if ($WhatIfPreference) {
    Write-Host "[WhatIf] Changes would be applied to Directory.Build.props, ReleaseUpdateInfo.cs, README.md, and ReleaseEntryPointContractTests.cs." -ForegroundColor Green
    return
}

# 4. Update Directory.Build.props
$newVersionProps = $versionPropsContent `
    -replace '<SafeSpeakVersion>[^<]+</SafeSpeakVersion>', "<SafeSpeakVersion>$PackageVersion</SafeSpeakVersion>" `
    -replace '<SafeSpeakStoreVersion>[^<]+</SafeSpeakStoreVersion>', "<SafeSpeakStoreVersion>$PackageVersion</SafeSpeakStoreVersion>"

Set-Content -LiteralPath $versionPropsPath -Value $newVersionProps -NoNewline
Write-Host "Updated $versionPropsPath" -ForegroundColor Green

# 5. Format Highlights for C# source
$highlightLines = @($Highlights | ForEach-Object {
    $escaped = $_.Replace('"', '\"')
    "        `"$escaped`""
}) -join ",`r`n"

$highlightsBlock = @"
    public static readonly IReadOnlyList<string> CurrentHighlights = new[]
    {
$highlightLines
    };
"@

$newReleaseInfo = $releaseInfoContent `
    -replace 'public const int CurrentGuideVersion = \d+;', "public const int CurrentGuideVersion = $GuideVersion;"

$pattern = '(?s)public static readonly IReadOnlyList<string> CurrentHighlights = new\[\]\s*\{.*?\};'
$newReleaseInfo = [regex]::Replace($newReleaseInfo, $pattern, $highlightsBlock)

Set-Content -LiteralPath $releaseInfoPath -Value $newReleaseInfo -NoNewline
Write-Host "Updated $releaseInfoPath" -ForegroundColor Green

# 6. Update installer/README.md
$readmeContent = Get-Content -LiteralPath $readmePath -Raw
$newReadme = $readmeContent `
    -replace '-PackageVersion \d+\.\d+\.\d+\.\d+ `', ("-PackageVersion " + $PackageVersion + " ``") `
    -replace 'v\d+\.\d+\.\d+\.\d+-rc\.1', "v$PackageVersion-rc.1"

Set-Content -LiteralPath $readmePath -Value $newReadme -NoNewline
Write-Host "Updated $readmePath" -ForegroundColor Green

# 7. Update tests/SafeSpeak.App.Contracts.Tests/ReleaseEntryPointContractTests.cs
$contractsContent = Get-Content -LiteralPath $contractsPath -Raw
$newContracts = $contractsContent `
    -replace '<SafeSpeakStoreVersion>\d+\.\d+\.\d+\.\d+</SafeSpeakStoreVersion>', "<SafeSpeakStoreVersion>$PackageVersion</SafeSpeakStoreVersion>" `
    -replace 'default: \d+\.\d+\.\d+\.\d+', "default: $PackageVersion"

Set-Content -LiteralPath $contractsPath -Value $newContracts -NoNewline
Write-Host "Updated $contractsPath" -ForegroundColor Green

Write-Host "`nRelease candidate files synchronized successfully!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Run 'dotnet test SafeSpeak.sln' to verify contract and core tests."
Write-Host "  2. Commit changes to develop."
Write-Host "  3. Merge develop to main to trigger the release build."
