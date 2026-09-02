[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$InstallerUrl,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$InstallerSha256,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutputDirectory = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $PSScriptRoot 'winget' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$InstallerSha256 = $InstallerSha256.ToUpperInvariant()

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: SafeSpeak.SafeSpeak
PackageVersion: $PackageVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$localeManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: SafeSpeak.SafeSpeak
PackageVersion: $PackageVersion
PackageLocale: en-US
Publisher: SafeSpeak
PublisherUrl: https://github.com/Bl4ut0/SafeSpeak
PublisherSupportUrl: https://github.com/Bl4ut0/SafeSpeak/issues
PackageName: SafeSpeak
PackageUrl: https://github.com/Bl4ut0/SafeSpeak
License: Proprietary
LicenseUrl: https://github.com/Bl4ut0/SafeSpeak/blob/main/LICENSE
ShortDescription: Accessible TTS moderation and playback for livestreamers.
Description: SafeSpeak connects to a local livestream event source, moderates messages, and provides controllable text-to-speech playback for blind, low-vision, and sighted streamers.
Moniker: safespeak
Tags:
  - accessibility
  - livestream
  - moderation
  - tiktok
  - tts
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: SafeSpeak.SafeSpeak
PackageVersion: $PackageVersion
InstallerType: msix
MinimumOSVersion: 10.0.19041.0
Installers:
  - Architecture: x64
    InstallerUrl: $InstallerUrl
    InstallerSha256: $InstallerSha256
ManifestType: installer
ManifestVersion: 1.6.0
"@

$files = [ordered]@{
    'SafeSpeak.SafeSpeak.yaml' = $versionManifest
    'SafeSpeak.SafeSpeak.locale.en-US.yaml' = $localeManifest
    'SafeSpeak.SafeSpeak.installer.yaml' = $installerManifest
}

foreach ($entry in $files.GetEnumerator()) {
    $path = Join-Path $OutputDirectory $entry.Key
    Set-Content -LiteralPath $path -Value $entry.Value -Encoding utf8
    Write-Host "Created $path"
}

Write-Host "Validate with: winget validate --manifest `"$OutputDirectory`""
