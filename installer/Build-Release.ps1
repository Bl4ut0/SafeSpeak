[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion = '0.1.0.0',

    [ValidateSet('Zip', 'Msix', 'Both')]
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
$OutputDirectory = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repoRoot 'artifacts' }
$appProject = Join-Path $repoRoot 'src\SafeSpeak.App\SafeSpeak.App.csproj'
$solution = Join-Path $repoRoot 'SafeSpeak.sln'
$runtimeIdentifier = "win-$Architecture"
$artifactRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier"
$stagingDirectory = Join-Path $artifactRoot ".staging-$runtimeIdentifier"
$verificationDirectory = Join-Path $artifactRoot ".verify-$runtimeIdentifier"
$zipPath = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier.zip"
$msixPath = Join-Path $artifactRoot "SafeSpeak_${PackageVersion}_${Architecture}.msix"
$manifestReportPath = Join-Path $artifactRoot "SafeSpeak-$PackageVersion-$runtimeIdentifier.release.json"

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

$assemblyVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2])"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Remove-ReleaseDirectory -Path $publishDirectory
Remove-ReleaseDirectory -Path $stagingDirectory
Remove-ReleaseDirectory -Path $verificationDirectory
Remove-Item -LiteralPath $zipPath, $msixPath, $manifestReportPath -Force -ErrorAction SilentlyContinue

Push-Location $repoRoot
try {
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', $solution)
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', $appProject, '-r', $runtimeIdentifier)

    if (-not $SkipTests) {
        Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('test', $solution, '-c', 'Release', '--no-restore')
    }

    $publishProfile = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'publish', $appProject,
        '-c', 'Release',
        '-r', $runtimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        "-p:PublishProfile=$publishProfile",
        "-p:Version=$assemblyVersion",
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

# Import libraries and symbols are build-time diagnostics, not desktop runtime payloads.
Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Where-Object { $_.Extension -in @('.lib', '.pdb') } |
    Remove-Item -Force

if ($Format -in @('Zip', 'Both')) {
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created portable desktop archive: $zipPath"
}

if ($Format -in @('Msix', 'Both')) {
    $makeAppx = Find-WindowsSdkTool -ToolName 'makeappx.exe'

    New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse -Force

    $sourceAssets = Join-Path $PSScriptRoot 'Assets'
    $stagedAssets = Join-Path $stagingDirectory 'Assets'
    $requiredAssets = @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png')
    foreach ($asset in $requiredAssets) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceAssets $asset))) {
            throw "Required MSIX asset is missing: installer\Assets\$asset. Run installer\Generate-Assets.ps1 to restore placeholders."
        }
    }
    New-Item -ItemType Directory -Force -Path $stagedAssets | Out-Null
    Copy-Item -Path (Join-Path $sourceAssets '*') -Destination $stagedAssets -Recurse -Force

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

$artifacts = @($zipPath, $msixPath) |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object {
        $item = Get-Item -LiteralPath $_
        $signature = if ($item.Extension -eq '.msix') { Get-AuthenticodeSignature -LiteralPath $_ } else { $null }
        [ordered]@{
            file = $item.Name
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
            signatureStatus = if ($signature) { $signature.Status.ToString() } else { 'NotApplicable' }
        }
    }

$report = [ordered]@{
    product = 'SafeSpeak'
    packageVersion = $PackageVersion
    runtimeIdentifier = $runtimeIdentifier
    selfContained = $true
    framework = 'net8.0-windows'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    artifacts = @($artifacts)
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestReportPath -Encoding utf8

if (-not $KeepStaging) {
    Remove-ReleaseDirectory -Path $stagingDirectory
    Remove-ReleaseDirectory -Path $verificationDirectory
}

Write-Host "Release report: $manifestReportPath"
