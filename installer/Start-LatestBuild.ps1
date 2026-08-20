[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repoRoot 'artifacts'
$application = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter 'SafeSpeak-*-win-x64' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    ForEach-Object { Get-Item -LiteralPath (Join-Path $_.FullName 'SafeSpeak.App.exe') -ErrorAction SilentlyContinue } |
    Select-Object -First 1

if (-not $application) {
    Write-Error 'No verified x64 desktop build exists. Run installer\Build-Release.ps1 from the repository root first.'
    exit 1
}

Write-Host "Starting $($application.FullName)"
Start-Process -FilePath $application.FullName
