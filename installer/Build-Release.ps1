[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion,

    [ValidateSet('Zip', 'Msix', 'Msi', 'Both', 'All')]
    [string]$Format = 'Both',

    [string]$IdentityName = 'SafeSpeak.App',
    [string]$Publisher = 'CN=SafeSpeak',
    [string]$PublisherDisplayName = 'SafeSpeak',
    [string]$OutputDirectory,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$StoreSubmission,
    [switch]$SkipTests,
    [switch]$KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $versionPropsPath)) {
    throw "The authoritative version file is missing: $versionPropsPath"
}
[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$defaultPackageVersion = [string]($versionProps.Project.PropertyGroup.SafeSpeakVersion | Select-Object -First 1)
if ($defaultPackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props must define SafeSpeakVersion as a four-part numeric version.'
}
if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = $defaultPackageVersion
}
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'PackageVersion must be a four-part numeric version.'
}

$defaultArtifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$OutputDirectory = if ($OutputDirectory) { $OutputDirectory } else { $defaultArtifactRoot }
$appProject = Join-Path $repoRoot 'src\SafeSpeak.App\SafeSpeak.App.csproj'
$testProject = Join-Path $repoRoot 'tests\SafeSpeak.Core.Tests\SafeSpeak.Core.Tests.csproj'
$appContractsTestProject = Join-Path $repoRoot 'tests\SafeSpeak.App.Contracts.Tests\SafeSpeak.App.Contracts.Tests.csproj'
$runtimeIdentifier = "win-$Architecture"
$artifactRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier"
$stagingDirectory = Join-Path $artifactRoot ".staging-$runtimeIdentifier"
$verificationDirectory = Join-Path $artifactRoot ".verify-$runtimeIdentifier"
$zipPath = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier.zip"
$msixPath = Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_${Architecture}.msix"
$msiPath = Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_${Architecture}.msi"
$manifestReportPath = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier.release.json"
$msiBuildScript = Join-Path $PSScriptRoot 'Build-Msi.ps1'

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
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1

    if (-not $selected) {
        throw "$ToolName was not found. Install the Windows 10/11 SDK, including App Certification Kit tools."
    }

    return $selected.FullName
}

function Remove-ReleaseDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $artifactRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the artifact root: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$versionParts = $PackageVersion.Split('.') | ForEach-Object { [int]$_ }
if ($versionParts | Where-Object { $_ -gt 65535 }) {
    throw 'Every MSIX version component must be between 0 and 65535.'
}

if ($StoreSubmission) {
    if ($versionParts[0] -eq 0) {
        throw 'Microsoft Store submissions require a non-zero major version component.'
    }
    if ($versionParts[3] -ne 0) {
        throw 'Microsoft Store submissions require the fourth (revision) version component to be 0.'
    }
    if ($IdentityName -eq 'SafeSpeak.App' -or $Publisher -eq 'CN=SafeSpeak') {
        throw 'StoreSubmission requires the Partner Center Identity Name and Publisher values. The repository defaults are placeholders.'
    }
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$publishesCurrentDesktop = $Architecture -eq 'x64' -and
    $PackageVersion -eq $defaultPackageVersion -and
    [System.String]::Equals($artifactRoot, $defaultArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)
$currentPointerPath = Join-Path $defaultArtifactRoot 'current-win-x64.json'
if ($publishesCurrentDesktop) {
    # Never leave a current pointer referencing a directory while that directory
    # is being replaced. A failed build remains deliberately unlaunchable.
    Remove-Item -LiteralPath $currentPointerPath -Force -ErrorAction SilentlyContinue
}
Remove-ReleaseDirectory -Path $publishDirectory
Remove-ReleaseDirectory -Path $stagingDirectory
Remove-ReleaseDirectory -Path $verificationDirectory
Remove-Item -LiteralPath $zipPath, $msixPath, $msiPath, $manifestReportPath -Force -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', $testProject)
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', $appContractsTestProject)
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', $appProject, '-r', $runtimeIdentifier)

    if (-not $SkipTests) {
        # The TikFinity emulator is a separate development process and must
        # never be built, started, or locked by packaging.
        Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('test', $testProject, '-c', 'Release', '--no-restore')
        Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('test', $appContractsTestProject, '-c', 'Release', '--no-restore')
    }

    $publishProfile = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'publish', $appProject,
        '-c', 'Release',
        '-r', $runtimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        "-p:PublishProfile=$publishProfile",
        "-p:Version=$PackageVersion",
        "-p:AssemblyVersion=$PackageVersion",
        "-p:FileVersion=$PackageVersion",
        "-p:InformationalVersion=$PackageVersion",
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $publishDirectory
    )
}
finally {
    Pop-Location
}

$executablePath = Join-Path $publishDirectory 'SafeSpeak.App.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Publish completed without the expected desktop executable: $executablePath"
}
$executableInfo = Get-Item -LiteralPath $executablePath
$executableFileVersion = [string]$executableInfo.VersionInfo.FileVersion
if ($executableFileVersion -ne $PackageVersion) {
    throw "Published executable version '$executableFileVersion' does not match package version '$PackageVersion'."
}
$executableSha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
$executableRelativePath = "SafeSpeak-$PackageVersion-$runtimeIdentifier\SafeSpeak.App.exe"
$reportRelativePath = "SafeSpeak-$PackageVersion-$runtimeIdentifier.release.json"
$requiredRuntimeFileNames = @(
    'SafeSpeak.App.dll',
    'SafeSpeak.Core.dll',
    'SafeSpeak.App.deps.json',
    'SafeSpeak.App.runtimeconfig.json'
)
$expandedRuntimeFiles = @(
    foreach ($runtimeFileName in $requiredRuntimeFileNames) {
        $runtimeFilePath = Join-Path $publishDirectory $runtimeFileName
        if (-not (Test-Path -LiteralPath $runtimeFilePath -PathType Leaf)) {
            throw "Publish is incomplete: required SafeSpeak runtime file is missing: $runtimeFileName"
        }
        $runtimeFileInfo = Get-Item -LiteralPath $runtimeFilePath
        $runtimeFileVersion = if ($runtimeFileInfo.Extension -eq '.dll') {
            [string]$runtimeFileInfo.VersionInfo.FileVersion
        }
        else {
            $null
        }
        if ($runtimeFileVersion -and $runtimeFileVersion -ne $PackageVersion) {
            throw "Published runtime file '$runtimeFileName' embeds version '$runtimeFileVersion' instead of '$PackageVersion'."
        }
        [ordered]@{
            relativePath = [System.IO.Path]::Combine("SafeSpeak-$PackageVersion-$runtimeIdentifier", $runtimeFileName)
            bytes = $runtimeFileInfo.Length
            sha256 = (Get-FileHash -LiteralPath $runtimeFilePath -Algorithm SHA256).Hash
            fileVersion = $runtimeFileVersion
        }
    }
)

$publishedVoiceDirectory = Join-Path $publishDirectory 'voices'
$publishedEnglishVoices = @(
    Get-ChildItem -LiteralPath $publishedVoiceDirectory -Filter '*.npy' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -match '^(af|am|bf|bm)_' }
)
if ($publishedEnglishVoices.Count -lt 27) {
    throw "Publish is incomplete: expected at least 27 Kokoro English voice embeddings, found $($publishedEnglishVoices.Count)."
}
foreach ($requiredRuntimeFile in @('KokoroSharp.dll', 'Microsoft.ML.OnnxRuntime.dll', 'onnxruntime.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredRuntimeFile))) {
        throw "Publish is incomplete: required neural voice runtime is missing: $requiredRuntimeFile"
    }
}

$moderationDirectory = Join-Path $publishDirectory 'Models\Moderation'
$moderationModelPath = Join-Path $moderationDirectory 'model.onnx'
$moderationTokenizerPath = Join-Path $moderationDirectory 'tokenizer.json'
$expectedModerationModelHash = '935BA953C9D4478D809DB1A2FA40181F42BF1670D1E69261478B2137C1FBACC5'
$expectedModerationTokenizerHash = '851CA67100D372CA3AE031A6ABD168F53489EEBFD7D89523F35C5C9B4D372C3C'
foreach ($asset in @(
    @{ Path = $moderationModelPath; Hash = $expectedModerationModelHash },
    @{ Path = $moderationTokenizerPath; Hash = $expectedModerationTokenizerHash }
)) {
    if (-not (Test-Path -LiteralPath $asset.Path)) {
        throw "Publish is incomplete: required local moderation asset is missing: $($asset.Path)"
    }
    $actualHash = (Get-FileHash -LiteralPath $asset.Path -Algorithm SHA256).Hash
    if ($actualHash -ne $asset.Hash) {
        throw "Publish contains an unverified local moderation asset: $($asset.Path)"
    }
}
foreach ($requiredNotice in @('MODEL-NOTICE.md', 'LICENSE.apache-2.0.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $moderationDirectory $requiredNotice))) {
        throw "Publish is incomplete: required local moderation notice is missing: $requiredNotice"
    }
}

$safeSpeakLicenseSource = Join-Path $repoRoot 'LICENSE'
$thirdPartySummarySource = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
foreach ($requiredLegalFile in @($safeSpeakLicenseSource, $thirdPartySummarySource)) {
    if (-not (Test-Path -LiteralPath $requiredLegalFile -PathType Leaf)) {
        throw "Release is missing a required legal notice: $requiredLegalFile"
    }
}
Copy-Item -LiteralPath $safeSpeakLicenseSource `
    -Destination (Join-Path $publishDirectory 'LICENSE.txt') -Force
Copy-Item -LiteralPath $thirdPartySummarySource `
    -Destination (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md') -Force

$nugetPackagesRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}
else {
    Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}
$thirdPartyNoticeDirectory = Join-Path $publishDirectory 'ThirdPartyNotices'
New-Item -ItemType Directory -Force -Path $thirdPartyNoticeDirectory | Out-Null

function Copy-PackageLegalFile {
    param(
        [Parameter(Mandatory)] [string]$PackageId,
        [Parameter(Mandatory)] [string]$PackageVersion,
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [string]$DestinationName
    )

    $source = Join-Path $nugetPackagesRoot `
        "$($PackageId.ToLowerInvariant())\$PackageVersion\$RelativePath"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required $PackageId $PackageVersion legal file is missing: $source"
    }
    Copy-Item -LiteralPath $source `
        -Destination (Join-Path $thirdPartyNoticeDirectory $DestinationName) -Force
}

Copy-PackageLegalFile -PackageId 'Microsoft.ML.OnnxRuntime' `
    -PackageVersion '1.22.0' -RelativePath 'LICENSE' `
    -DestinationName 'ONNXRuntime-LICENSE.txt'
Copy-PackageLegalFile -PackageId 'Microsoft.ML.OnnxRuntime' `
    -PackageVersion '1.22.0' -RelativePath 'ThirdPartyNotices.txt' `
    -DestinationName 'ONNXRuntime-ThirdPartyNotices.txt'
Copy-PackageLegalFile -PackageId 'NAudio' -PackageVersion '2.2.1' `
    -RelativePath 'license.txt' -DestinationName 'NAudio-LICENSE.txt'
Copy-Item -LiteralPath (Join-Path $moderationDirectory 'LICENSE.apache-2.0.txt') `
    -Destination (Join-Path $thirdPartyNoticeDirectory 'Apache-2.0.txt') -Force

$requiredPackagedLegalFiles = @(
    'LICENSE.txt',
    'THIRD-PARTY-NOTICES.md',
    'ThirdPartyNotices\Apache-2.0.txt',
    'ThirdPartyNotices\NAudio-LICENSE.txt',
    'ThirdPartyNotices\ONNXRuntime-LICENSE.txt',
    'ThirdPartyNotices\ONNXRuntime-ThirdPartyNotices.txt'
)
foreach ($requiredLegalFile in $requiredPackagedLegalFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredLegalFile) -PathType Leaf)) {
        throw "Release is missing a packaged legal notice: $requiredLegalFile"
    }
}

# Import libraries and symbols are build-time diagnostics, not desktop runtime payloads.
Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Where-Object { $_.Extension -in @('.lib', '.pdb') } |
    Remove-Item -Force

if ($Format -in @('Zip', 'Both', 'All')) {
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created portable desktop archive: $zipPath"
}

if ($Format -in @('Msix', 'Both', 'All')) {
    $makeAppx = Find-WindowsSdkTool -ToolName 'makeappx.exe'

    New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse -Force

    $sourceAssets = Join-Path $PSScriptRoot 'Assets'
    $stagedAssets = Join-Path $stagingDirectory 'Assets'
    $requiredAssets = @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png')
    foreach ($asset in $requiredAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceAssets $asset))) {
            throw "Required MSIX asset is missing: installer\Assets\$asset. Run installer\Generate-Assets.ps1 to regenerate the branded package assets."
        }
    }
    New-Item -ItemType Directory -Force -Path $stagedAssets | Out-Null
    foreach ($asset in $requiredAssets) {
        Copy-Item -LiteralPath (Join-Path $sourceAssets $asset) -Destination $stagedAssets -Force
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml')
    $manifest.Package.Identity.Name = $IdentityName
    $manifest.Package.Identity.Publisher = $Publisher
    $manifest.Package.Identity.Version = $PackageVersion
    $manifest.Package.Identity.ProcessorArchitecture = $Architecture
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Save((Join-Path $stagingDirectory 'AppxManifest.xml'))

    Invoke-CheckedCommand -FilePath $makeAppx -ArgumentList @(
        'pack', '/d', $stagingDirectory, '/p', $msixPath, '/o'
    )

    # Unpack the result as a structural verification. This catches malformed package layouts.
    New-Item -ItemType Directory -Force -Path $verificationDirectory | Out-Null
    Invoke-CheckedCommand -FilePath $makeAppx -ArgumentList @(
        'unpack', '/p', $msixPath, '/d', $verificationDirectory, '/o'
    )
    if (-not (Test-Path -LiteralPath (Join-Path $verificationDirectory 'SafeSpeak.App.exe'))) {
        throw 'MSIX verification failed: the application executable is missing from the package.'
    }
    $verifiedVoiceCount = @(Get-ChildItem -LiteralPath (Join-Path $verificationDirectory 'voices') -Filter '*.npy' -File -ErrorAction SilentlyContinue).Count
    if ($verifiedVoiceCount -lt 27) {
        throw "MSIX verification failed: expected at least 27 Kokoro voice embeddings, found $verifiedVoiceCount."
    }
    $verifiedModerationModel = Join-Path $verificationDirectory 'Models\Moderation\model.onnx'
    if (-not (Test-Path -LiteralPath $verifiedModerationModel) -or
        (Get-FileHash -LiteralPath $verifiedModerationModel -Algorithm SHA256).Hash -ne $expectedModerationModelHash) {
        throw 'MSIX verification failed: the pinned local moderation model is missing or altered.'
    }
    $verifiedModerationTokenizer = Join-Path $verificationDirectory 'Models\Moderation\tokenizer.json'
    if (-not (Test-Path -LiteralPath $verifiedModerationTokenizer) -or
        (Get-FileHash -LiteralPath $verifiedModerationTokenizer -Algorithm SHA256).Hash -ne $expectedModerationTokenizerHash) {
        throw 'MSIX verification failed: the pinned local moderation tokenizer is missing or altered.'
    }
    foreach ($requiredNotice in @('MODEL-NOTICE.md', 'LICENSE.apache-2.0.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $verificationDirectory "Models\Moderation\$requiredNotice"))) {
            throw "MSIX verification failed: required local moderation notice is missing: $requiredNotice"
        }
    }
    foreach ($requiredLegalFile in $requiredPackagedLegalFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $verificationDirectory $requiredLegalFile) -PathType Leaf)) {
            throw "MSIX verification failed: packaged legal notice is missing: $requiredLegalFile"
        }
    }

    if ($CertificateThumbprint) {
        $signTool = Find-WindowsSdkTool -ToolName 'signtool.exe'
        Invoke-CheckedCommand -FilePath $signTool -ArgumentList @(
            'sign', '/sha1', $CertificateThumbprint,
            '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256',
            $msixPath
        )
        Invoke-CheckedCommand -FilePath $signTool -ArgumentList @('verify', '/pa', '/v', $msixPath)
    }
    else {
        Write-Warning 'The MSIX is structurally valid but unsigned. Sign it with the Store publisher certificate before sideload distribution.'
    }

    Write-Host "Created MSIX package: $msixPath"
}

if ($Format -in @('Msi', 'All')) {
    if (-not (Test-Path -LiteralPath $msiBuildScript -PathType Leaf)) {
        throw "The MSI build entry point is missing: $msiBuildScript"
    }

    & $msiBuildScript `
        -Architecture $Architecture `
        -PackageVersion $PackageVersion `
        -PayloadDirectory $publishDirectory `
        -OutputPath $msiPath `
        -CertificateThumbprint $CertificateThumbprint `
        -TimestampUrl $TimestampUrl `
        -KeepStaging:$KeepStaging
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
        throw "MSI build completed without the expected package: $msiPath"
    }
}

$artifacts = @($zipPath, $msixPath, $msiPath) |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object {
        $item = Get-Item -LiteralPath $_
        $signature = if ($item.Extension -in @('.msix', '.msi')) { Get-AuthenticodeSignature -LiteralPath $_ } else { $null }
        [ordered]@{
            file = $item.Name
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
            signatureStatus = if ($signature) { $signature.Status.ToString() } else { 'NotApplicable' }
        }
    }

$sourceCommit = $null
$sourceDirty = $null
$gitCommand = Get-Command 'git' -ErrorAction SilentlyContinue
if ($gitCommand) {
    try {
        $commitOutput = @(& $gitCommand.Source -C $repoRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $commitOutput.Count -gt 0) {
            $sourceCommit = ([string]$commitOutput[0]).Trim()
            $statusOutput = @(& $gitCommand.Source -C $repoRoot status --porcelain --untracked-files=all 2>$null)
            if ($LASTEXITCODE -eq 0) {
                $sourceDirty = $statusOutput.Count -gt 0
            }
        }
    }
    catch {
        # Source provenance is diagnostic metadata. Packaging also works from a
        # source archive that has no Git executable or repository metadata.
        $sourceCommit = $null
        $sourceDirty = $null
    }
}

$generatedUtc = [DateTime]::UtcNow.ToString('o')
$report = [ordered]@{
    product = 'SafeSpeak'
    packageVersion = $PackageVersion
    runtimeIdentifier = $runtimeIdentifier
    selfContained = $true
    framework = 'net8.0-windows'
    expandedApplication = [ordered]@{
        path = $executablePath
        relativePath = $executableRelativePath
        bytes = $executableInfo.Length
        sha256 = $executableSha256
        fileVersion = $executableFileVersion
    }
    expandedRuntimeFiles = $expandedRuntimeFiles
    source = [ordered]@{
        commit = $sourceCommit
        dirty = $sourceDirty
    }
    localModeration = [ordered]@{
        model = 'navodPeiris/minilm-toxic-classifier'
        revision = '4831179af569756699fdd6132a520dcdbfe07f03'
        modelSha256 = $expectedModerationModelHash
        tokenizerSha256 = $expectedModerationTokenizerHash
    }
    generatedUtc = $generatedUtc
    artifacts = @($artifacts)
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestReportPath -Encoding utf8

if (-not $KeepStaging) {
    Remove-ReleaseDirectory -Path $stagingDirectory
    Remove-ReleaseDirectory -Path $verificationDirectory
}

if ($publishesCurrentDesktop) {
    $pointer = [ordered]@{
        schemaVersion = 2
        product = 'SafeSpeak'
        packageVersion = $PackageVersion
        runtimeIdentifier = $runtimeIdentifier
        generatedUtc = $generatedUtc
        releaseReport = [ordered]@{
            path = $reportRelativePath
            sha256 = (Get-FileHash -LiteralPath $manifestReportPath -Algorithm SHA256).Hash
        }
        executable = [ordered]@{
            path = $executableRelativePath
            sha256 = $executableSha256
            fileVersion = $executableFileVersion
        }
    }

    $temporaryPointerPath = "$currentPointerPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $pointer | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryPointerPath -Encoding utf8
        if (Test-Path -LiteralPath $currentPointerPath) {
            [System.IO.File]::Replace($temporaryPointerPath, $currentPointerPath, $null)
        }
        else {
            [System.IO.File]::Move($temporaryPointerPath, $currentPointerPath)
        }
    }
    finally {
        Remove-Item -LiteralPath $temporaryPointerPath -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Updated verified current desktop pointer: $currentPointerPath"
}

Write-Host "Release report: $manifestReportPath"
