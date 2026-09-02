[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$IdentityName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Publisher,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublisherDisplayName,

    [string]$OutputDirectory,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseScript = Join-Path $PSScriptRoot 'Build-Release.ps1'
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'

if (-not (Test-Path -LiteralPath $releaseScript -PathType Leaf)) {
    throw "The release entry point is missing: $releaseScript"
}

[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$defaultPackageVersion = [string](
    $versionProps.Project.PropertyGroup.SafeSpeakStoreVersion | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = $defaultPackageVersion
}

if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'PackageVersion must be a four-part numeric version. Directory.Build.props must define SafeSpeakStoreVersion when PackageVersion is omitted.'
}

$versionParts = $PackageVersion.Split('.') | ForEach-Object { [int]$_ }
if ($versionParts | Where-Object { $_ -gt 65535 }) {
    throw 'Every MSIX version component must be between 0 and 65535.'
}
if ($versionParts[0] -eq 0) {
    throw 'Microsoft Store bundle versions require a non-zero major component.'
}
if ($versionParts[3] -ne 0) {
    throw 'Microsoft Store reserves the fourth package version component; it must be 0.'
}
if ($IdentityName -eq 'SafeSpeak.App' -or $Publisher -eq 'CN=SafeSpeak') {
    throw 'Store bundle generation requires the assigned Partner Center identity and publisher.'
}

$artifactRoot = if ($OutputDirectory) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\store'))
}
$bundleInputDirectory = Join-Path $artifactRoot ".bundle-input-$PackageVersion"
$bundleVerifyDirectory = Join-Path $artifactRoot ".bundle-verify-$PackageVersion"
$bundlePath = Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_neutral.msixbundle"
$reportPath = Join-Path $artifactRoot "SafeSpeak-${PackageVersion}.store-bundle.json"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$ArgumentList
    )

    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath"
    }
}

function Find-WindowsSdkTool {
    param([Parameter(Mandatory)] [string]$ToolName)

    $onPath = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $sdkRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $candidates = foreach ($sdkRoot in $sdkRoots) {
        Get-ChildItem -LiteralPath $sdkRoot -Filter $ToolName -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' }
    }

    $selected = $candidates |
        Sort-Object {
            try { [version]$_.Directory.Parent.Name }
            catch { [version]'0.0' }
        } -Descending |
        Select-Object -First 1

    if (-not $selected) {
        throw "$ToolName was not found. Install the Windows 10/11 SDK App Packaging tools."
    }

    return $selected.FullName
}

function Reset-BundleWorkDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $artifactRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith(
        $rootWithSeparator,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the Store artifact root: $resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $resolved | Out-Null
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Reset-BundleWorkDirectory -Path $bundleInputDirectory
Reset-BundleWorkDirectory -Path $bundleVerifyDirectory
Remove-Item -LiteralPath $bundlePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue

# The x64 release invocation owns the one full test pass. ARM64 packages the
# same verified source revision without repeating both suites.
& $releaseScript `
    -Architecture x64 `
    -PackageVersion $PackageVersion `
    -Format Msix `
    -IdentityName $IdentityName `
    -Publisher $Publisher `
    -PublisherDisplayName $PublisherDisplayName `
    -OutputDirectory $artifactRoot `
    -StoreSubmission `
    -SkipTests:$SkipTests

& $releaseScript `
    -Architecture arm64 `
    -PackageVersion $PackageVersion `
    -Format Msix `
    -IdentityName $IdentityName `
    -Publisher $Publisher `
    -PublisherDisplayName $PublisherDisplayName `
    -OutputDirectory $artifactRoot `
    -StoreSubmission `
    -SkipTests

$architecturePackages = @(
    Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_x64.msix"
    Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_arm64.msix"
)
foreach ($package in $architecturePackages) {
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Store architecture package is missing: $package"
    }
    Copy-Item -LiteralPath $package -Destination $bundleInputDirectory -Force
}

$makeAppx = Find-WindowsSdkTool -ToolName 'makeappx.exe'
Invoke-CheckedCommand -FilePath $makeAppx -ArgumentList @(
    'bundle', '/d', $bundleInputDirectory, '/p', $bundlePath, '/o'
)
Invoke-CheckedCommand -FilePath $makeAppx -ArgumentList @(
    'unbundle', '/p', $bundlePath, '/d', $bundleVerifyDirectory, '/o'
)

$verifiedPackages = @(
    Get-ChildItem -LiteralPath $bundleVerifyDirectory -Filter '*.msix' -File)
if ($verifiedPackages.Count -ne 2) {
    throw "Store bundle verification expected 2 architecture packages and found $($verifiedPackages.Count)."
}
foreach ($architecture in @('x64', 'arm64')) {
    if (-not ($verifiedPackages.Name | Where-Object { $_ -match "_${architecture}\.msix$" })) {
        throw "Store bundle verification did not find the $architecture package."
    }
}

$sourceCommit = 'unknown'
try {
    $resolvedCommit = (& git -C $repoRoot rev-parse HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -eq 0 -and $resolvedCommit) {
        $sourceCommit = [string]$resolvedCommit
    }
}
catch {
    $sourceCommit = 'unknown'
}

$bundle = Get-Item -LiteralPath $bundlePath
$report = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    packageVersion = $PackageVersion
    identityName = $IdentityName
    publisher = $Publisher
    publisherDisplayName = $PublisherDisplayName
    sourceCommit = $sourceCommit
    bundle = [ordered]@{
        file = $bundle.Name
        bytes = $bundle.Length
        sha256 = (Get-FileHash -LiteralPath $bundle.FullName -Algorithm SHA256).Hash
        signatureStatus = (Get-AuthenticodeSignature -LiteralPath $bundle.FullName).Status.ToString()
    }
    architecturePackages = @($architecturePackages | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [ordered]@{
            file = $item.Name
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            signatureStatus = (Get-AuthenticodeSignature -LiteralPath $item.FullName).Status.ToString()
        }
    })
}

$report | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host "Created verified Microsoft Store bundle: $bundlePath"
Write-Host "Store bundle report: $reportPath"
