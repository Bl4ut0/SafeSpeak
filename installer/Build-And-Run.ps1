[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningProcesses = @(Get-Process -Name 'SafeSpeak.App' -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    $runningIds = ($runningProcesses | ForEach-Object { $_.Id }) -join ', '
    throw "SafeSpeak is already running (PID(s): $runningIds). Close it before building the current test version."
}

$buildScript = Join-Path $PSScriptRoot 'Build-Release.ps1'
$launchScript = Join-Path $PSScriptRoot 'Start-LatestBuild.ps1'

# The package version comes from Directory.Build.props. Tests intentionally target
# only SafeSpeak.Core.Tests; this workflow never starts or owns the TikFinity emulator.
& $buildScript -Architecture x64 -Format Zip
& $launchScript
