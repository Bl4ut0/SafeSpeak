[CmdletBinding()]
param(
    [switch]$ResolveOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$pointerPath = Join-Path $artifactRoot 'current-win-x64.json'
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'

function Get-RequiredValue {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if (-not $property -or $null -eq $property.Value) {
        throw "$Context is missing required property '$Name'."
    }
    return $property.Value
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Context
    )

    $value = [string](Get-RequiredValue -Object $Object -Name $Name -Context $Context)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Context property '$Name' cannot be blank."
    }
    return $value
}

function Resolve-VerifiedArtifactPath {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [string]$ExpectedRelativePath,
        [Parameter(Mandatory)] [string]$Description
    )

    $normalized = $RelativePath.Replace('/', '\')
    if (-not [System.String]::Equals($normalized, $ExpectedRelativePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description path '$RelativePath' is not the expected current top-level artifact '$ExpectedRelativePath'."
    }
    if ([System.IO.Path]::IsPathRooted($normalized)) {
        throw "$Description path must be relative to the artifact directory."
    }

    $resolved = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $normalized))
    $artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description path resolves outside the artifact directory."
    }
    return $resolved
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ExpectedSha256,
        [Parameter(Mandatory)] [string]$Description
    )

    if ($ExpectedSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw "$Description has an invalid SHA-256 value."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
    $actualSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [System.String]::Equals($actualSha256, $ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description hash does not match the verified current-build pointer."
    }
}

if (-not (Test-Path -LiteralPath $versionPropsPath -PathType Leaf)) {
    throw "The authoritative version file is missing: $versionPropsPath"
}
[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$currentVersion = [string]($versionProps.Project.PropertyGroup.SafeSpeakVersion | Select-Object -First 1)
if ($currentVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props must define SafeSpeakVersion as a four-part numeric version.'
}

if (-not (Test-Path -LiteralPath $pointerPath -PathType Leaf)) {
    throw "No verified current x64 desktop build exists. Run installer\Build-Release.ps1 from the repository root first."
}

try {
    $pointer = Get-Content -LiteralPath $pointerPath -Raw | ConvertFrom-Json
}
catch {
    throw "The current desktop pointer is not valid JSON: $pointerPath"
}

$schemaVersion = [string](Get-RequiredValue -Object $pointer -Name 'schemaVersion' -Context 'Current desktop pointer')
$pointerVersion = Get-RequiredString -Object $pointer -Name 'packageVersion' -Context 'Current desktop pointer'
$pointerRuntime = Get-RequiredString -Object $pointer -Name 'runtimeIdentifier' -Context 'Current desktop pointer'
if ($schemaVersion -ne '2') {
    throw "Unsupported current desktop pointer schema '$schemaVersion'."
}
if ($pointerVersion -ne $currentVersion) {
    throw "The verified build is version '$pointerVersion', but the current source version is '$currentVersion'. Rebuild SafeSpeak."
}
if ($pointerRuntime -ne 'win-x64') {
    throw "The current desktop pointer has unsupported runtime '$pointerRuntime'."
}

$reportPointer = Get-RequiredValue -Object $pointer -Name 'releaseReport' -Context 'Current desktop pointer'
$executablePointer = Get-RequiredValue -Object $pointer -Name 'executable' -Context 'Current desktop pointer'
$expectedReportRelativePath = "SafeSpeak-$currentVersion-win-x64.release.json"
$expectedExecutableRelativePath = "SafeSpeak-$currentVersion-win-x64\SafeSpeak.App.exe"
$reportRelativePath = Get-RequiredString -Object $reportPointer -Name 'path' -Context 'Release report pointer'
$executableRelativePath = Get-RequiredString -Object $executablePointer -Name 'path' -Context 'Executable pointer'
$reportPath = Resolve-VerifiedArtifactPath -RelativePath $reportRelativePath -ExpectedRelativePath $expectedReportRelativePath -Description 'Release report'
$executablePath = Resolve-VerifiedArtifactPath -RelativePath $executableRelativePath -ExpectedRelativePath $expectedExecutableRelativePath -Description 'SafeSpeak executable'

$reportSha256 = Get-RequiredString -Object $reportPointer -Name 'sha256' -Context 'Release report pointer'
$executableSha256 = Get-RequiredString -Object $executablePointer -Name 'sha256' -Context 'Executable pointer'
$pointerFileVersion = Get-RequiredString -Object $executablePointer -Name 'fileVersion' -Context 'Executable pointer'
Assert-FileHash -Path $reportPath -ExpectedSha256 $reportSha256 -Description 'Release report'
Assert-FileHash -Path $executablePath -ExpectedSha256 $executableSha256 -Description 'SafeSpeak executable'

try {
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
}
catch {
    throw "The verified release report is not valid JSON: $reportPath"
}

$reportVersion = Get-RequiredString -Object $report -Name 'packageVersion' -Context 'Release report'
$reportRuntime = Get-RequiredString -Object $report -Name 'runtimeIdentifier' -Context 'Release report'
if ($reportVersion -ne $currentVersion -or $reportRuntime -ne 'win-x64') {
    throw 'The release report does not describe the current win-x64 build.'
}

$expandedApplication = Get-RequiredValue -Object $report -Name 'expandedApplication' -Context 'Release report'
$reportedAbsolutePath = Get-RequiredString -Object $expandedApplication -Name 'path' -Context 'Expanded application report'
$reportedRelativePath = Get-RequiredString -Object $expandedApplication -Name 'relativePath' -Context 'Expanded application report'
$reportedSha256 = Get-RequiredString -Object $expandedApplication -Name 'sha256' -Context 'Expanded application report'
$reportedFileVersion = Get-RequiredString -Object $expandedApplication -Name 'fileVersion' -Context 'Expanded application report'
if (-not [System.String]::Equals([System.IO.Path]::GetFullPath($reportedAbsolutePath), $executablePath, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [System.String]::Equals($reportedRelativePath.Replace('/', '\'), $expectedExecutableRelativePath, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [System.String]::Equals($reportedSha256, $executableSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    $reportedFileVersion -ne $currentVersion -or
    $pointerFileVersion -ne $currentVersion) {
    throw 'The release report, pointer, and executable metadata do not identify the same current build.'
}

$embeddedFileVersion = [string](Get-Item -LiteralPath $executablePath).VersionInfo.FileVersion
if ($embeddedFileVersion -ne $currentVersion) {
    throw "SafeSpeak.App.exe embeds version '$embeddedFileVersion' instead of '$currentVersion'."
}

$runtimeFileReports = @(Get-RequiredValue -Object $report -Name 'expandedRuntimeFiles' -Context 'Release report')
$expectedRuntimeFileNames = @(
    'SafeSpeak.App.dll',
    'SafeSpeak.Core.dll',
    'SafeSpeak.App.deps.json',
    'SafeSpeak.App.runtimeconfig.json'
)
if ($runtimeFileReports.Count -ne $expectedRuntimeFileNames.Count) {
    throw 'The release report does not contain the exact required SafeSpeak runtime file set.'
}
foreach ($runtimeFileName in $expectedRuntimeFileNames) {
    $expectedRuntimeRelativePath = [System.IO.Path]::Combine("SafeSpeak-$currentVersion-win-x64", $runtimeFileName)
    $matches = @($runtimeFileReports | Where-Object {
        $candidatePath = Get-RequiredString -Object $_ -Name 'relativePath' -Context 'Runtime file report'
        [System.String]::Equals(
            $candidatePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar),
            $expectedRuntimeRelativePath,
            [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -ne 1) {
        throw "The release report must contain exactly one '$runtimeFileName' entry."
    }

    $runtimeFileReport = $matches[0]
    $runtimeFilePath = Resolve-VerifiedArtifactPath -RelativePath (Get-RequiredString -Object $runtimeFileReport -Name 'relativePath' -Context 'Runtime file report') -ExpectedRelativePath $expectedRuntimeRelativePath -Description $runtimeFileName
    $runtimeFileSha256 = Get-RequiredString -Object $runtimeFileReport -Name 'sha256' -Context "$runtimeFileName report"
    Assert-FileHash -Path $runtimeFilePath -ExpectedSha256 $runtimeFileSha256 -Description $runtimeFileName

    $reportedBytes = [long](Get-RequiredValue -Object $runtimeFileReport -Name 'bytes' -Context "$runtimeFileName report")
    if ((Get-Item -LiteralPath $runtimeFilePath).Length -ne $reportedBytes) {
        throw "$runtimeFileName size does not match the verified release report."
    }
    if ([System.IO.Path]::GetExtension($runtimeFileName) -eq '.dll') {
        $reportedRuntimeVersion = Get-RequiredString -Object $runtimeFileReport -Name 'fileVersion' -Context "$runtimeFileName report"
        $embeddedRuntimeVersion = [string](Get-Item -LiteralPath $runtimeFilePath).VersionInfo.FileVersion
        if ($reportedRuntimeVersion -ne $currentVersion -or $embeddedRuntimeVersion -ne $currentVersion) {
            throw "$runtimeFileName does not embed the current SafeSpeak version '$currentVersion'."
        }
    }
}

$localModeration = Get-RequiredValue -Object $report -Name 'localModeration' -Context 'Release report'
$moderationModelHash = Get-RequiredString -Object $localModeration -Name 'modelSha256' -Context 'Local moderation report'
$moderationTokenizerHash = Get-RequiredString -Object $localModeration -Name 'tokenizerSha256' -Context 'Local moderation report'
$moderationRoot = [System.IO.Path]::Combine("SafeSpeak-$currentVersion-win-x64", 'Models', 'Moderation')
$moderationModelRelativePath = Join-Path $moderationRoot 'model.onnx'
$moderationTokenizerRelativePath = Join-Path $moderationRoot 'tokenizer.json'
$moderationModelPath = Resolve-VerifiedArtifactPath -RelativePath $moderationModelRelativePath -ExpectedRelativePath $moderationModelRelativePath -Description 'Local moderation model'
$moderationTokenizerPath = Resolve-VerifiedArtifactPath -RelativePath $moderationTokenizerRelativePath -ExpectedRelativePath $moderationTokenizerRelativePath -Description 'Local moderation tokenizer'
Assert-FileHash -Path $moderationModelPath -ExpectedSha256 $moderationModelHash -Description 'Local moderation model'
Assert-FileHash -Path $moderationTokenizerPath -ExpectedSha256 $moderationTokenizerHash -Description 'Local moderation tokenizer'

if ($ResolveOnly) {
    Write-Output $executablePath
    exit 0
}

$runningProcesses = @(Get-Process -Name 'SafeSpeak.App' -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    $runningIds = ($runningProcesses | ForEach-Object { $_.Id }) -join ', '
    throw "SafeSpeak is already running (PID(s): $runningIds). Close it before launching the verified current build."
}

Write-Host "Starting verified SafeSpeak $currentVersion from $executablePath"
Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path $executablePath -Parent)
