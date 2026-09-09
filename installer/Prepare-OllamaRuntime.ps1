[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$CacheRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = '0.33.3'
$releaseTag = 'v0.33.3'
$assetArchitecture = if ($Architecture -eq 'x64') { 'amd64' } else { 'arm64' }
$assetName = "ollama-windows-$assetArchitecture.zip"
$expectedArchiveHash = if ($Architecture -eq 'x64') {
    '52CB36A62E7E501F61514F60212DEC7117B6C098811357585E02FFFE32D2FCD7'
}
else {
    '98B9DDAAB6BAECE0418C6D1231526EB1E4E66944985E0A8EEB7D6171BCD7B6D8'
}
$downloadUrl = "https://github.com/ollama/ollama/releases/download/$releaseTag/$assetName"
$cacheRootFull = [System.IO.Path]::GetFullPath($CacheRoot)
$archiveDirectory = Join-Path $cacheRootFull "downloads\$releaseTag"
$archivePath = Join-Path $archiveDirectory $assetName
$runtimeRoot = Join-Path $cacheRootFull "runtime\$releaseTag\win-$Architecture"
$runtimeExecutable = Join-Path $runtimeRoot 'ollama.exe'
$runtimeMarker = Join-Path $runtimeRoot '.safespeak-runtime.json'

function Test-PreparedRuntime {
    if (-not (Test-Path -LiteralPath $runtimeExecutable -PathType Leaf) -or
        -not (Test-Path -LiteralPath $runtimeMarker -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'lib\ollama\llama-server.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'LICENSE.txt') -PathType Leaf)) {
        return $false
    }

    try {
        $marker = Get-Content -LiteralPath $runtimeMarker -Raw | ConvertFrom-Json
        return $marker.version -eq $version -and
            $marker.architecture -eq $Architecture -and
            $marker.sourceArchiveSha256 -eq $expectedArchiveHash
    }
    catch {
        return $false
    }
}

if (Test-PreparedRuntime) {
    Write-Output $runtimeRoot
    exit 0
}

New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualArchiveHash -ne $expectedArchiveHash) {
        Remove-Item -LiteralPath $archivePath -Force
    }
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    Write-Host "Downloading verified Ollama $version standalone runtime for $Architecture..."
    $temporaryArchive = "$archivePath.$([Guid]::NewGuid().ToString('N')).download"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $temporaryArchive
        $actualArchiveHash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash
        if ($actualArchiveHash -ne $expectedArchiveHash) {
            throw "Ollama archive SHA-256 mismatch for $assetName."
        }
        Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    }
}

$actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualArchiveHash -ne $expectedArchiveHash) {
    throw "Cached Ollama archive SHA-256 mismatch for $assetName."
}

$temporaryRoot = Join-Path $cacheRootFull "runtime\.extract-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
try {
    $tar = Get-Command 'tar.exe' -ErrorAction SilentlyContinue
    if (-not $tar) {
        throw 'Windows tar.exe is required to prepare the private CPU model runtime.'
    }

    & $tar.Source `
        '--exclude=lib/ollama/cuda_v12/*' `
        '--exclude=lib/ollama/cuda_v13/*' `
        '--exclude=lib/ollama/vulkan/*' `
        '-xf' $archivePath '-C' $temporaryRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not extract the verified Ollama archive. tar.exe exited with $LASTEXITCODE."
    }

    foreach ($requiredFile in @('ollama.exe', 'lib\ollama\llama-server.exe', 'lib\ollama\ggml.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $temporaryRoot $requiredFile) -PathType Leaf)) {
            throw "Prepared Ollama runtime is missing required file: $requiredFile"
        }
    }
    if (Get-ChildItem -LiteralPath (Join-Path $temporaryRoot 'lib\ollama') -Directory |
        Where-Object { $_.Name -match '^(cuda|vulkan)' }) {
        throw 'Prepared CPU runtime unexpectedly contains GPU library directories.'
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Ollama\LICENSE.txt') `
        -Destination (Join-Path $temporaryRoot 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Ollama\RUNTIME-NOTICE.md') `
        -Destination (Join-Path $temporaryRoot 'RUNTIME-NOTICE.md') -Force

    $marker = [ordered]@{
        schemaVersion = 1
        product = 'Ollama command-line runtime'
        version = $version
        architecture = $Architecture
        sourceUrl = $downloadUrl
        sourceArchiveSha256 = $expectedArchiveHash
        cpuOnly = $true
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $marker | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath (Join-Path $temporaryRoot '.safespeak-runtime.json') -Encoding utf8

    $runtimeParent = Split-Path $runtimeRoot -Parent
    New-Item -ItemType Directory -Force -Path $runtimeParent | Out-Null
    if (Test-Path -LiteralPath $runtimeRoot) {
        $resolvedRuntime = [System.IO.Path]::GetFullPath($runtimeRoot)
        $allowedRuntimeRoot = ([System.IO.Path]::GetFullPath(
            (Join-Path $cacheRootFull 'runtime'))).TrimEnd('\') + '\'
        if (-not $resolvedRuntime.StartsWith(
                $allowedRuntimeRoot,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to replace a runtime outside the build cache: $resolvedRuntime"
        }
        Remove-Item -LiteralPath $resolvedRuntime -Recurse -Force
    }
    Move-Item -LiteralPath $temporaryRoot -Destination $runtimeRoot
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

if (-not (Test-PreparedRuntime)) {
    throw 'Ollama runtime preparation completed without a valid runtime marker.'
}

Write-Output $runtimeRoot
