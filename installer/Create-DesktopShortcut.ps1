[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ShortcutName = 'SafeSpeak',
    [switch]$PublicDesktop,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$iconPath = Join-Path $PSScriptRoot 'Assets\SafeSpeak.ico'

function Resolve-SafeSpeakExecutable {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequestedPath = [System.IO.Path]::GetFullPath($RequestedPath)
        if (-not (Test-Path -LiteralPath $resolvedRequestedPath -PathType Leaf)) {
            throw "The requested SafeSpeak executable does not exist: $resolvedRequestedPath"
        }
        return $resolvedRequestedPath
    }

    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    $programFilesDirectory = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)
    if (-not [string]::IsNullOrWhiteSpace($programFilesDirectory)) {
        $candidatePaths.Add((Join-Path $programFilesDirectory 'The Project Hub\SafeSpeak\SafeSpeak.App.exe'))
        $candidatePaths.Add((Join-Path $programFilesDirectory 'SafeSpeak\SafeSpeak.App.exe'))
    }

    $verifiedBuildLauncher = Join-Path $PSScriptRoot 'Start-LatestBuild.ps1'
    if (Test-Path -LiteralPath $verifiedBuildLauncher -PathType Leaf) {
        try {
            $verifiedPath = [string](& $verifiedBuildLauncher -ResolveOnly 2>$null)
            if (-not [string]::IsNullOrWhiteSpace($verifiedPath)) {
                $candidatePaths.Add($verifiedPath.Trim())
            }
        }
        catch {
            # A missing or stale verified-build pointer is expected during
            # ordinary development. The local build candidates remain valid.
        }
    }

    $candidatePaths.Add((Join-Path $repoRoot 'src\SafeSpeak.App\bin\Release\net8.0-windows\SafeSpeak.App.exe'))
    $candidatePaths.Add((Join-Path $repoRoot 'src\SafeSpeak.App\bin\Debug\net8.0-windows\SafeSpeak.App.exe'))

    foreach ($candidatePath in $candidatePaths) {
        $resolvedCandidatePath = [System.IO.Path]::GetFullPath($candidatePath)
        if (Test-Path -LiteralPath $resolvedCandidatePath -PathType Leaf) {
            return $resolvedCandidatePath
        }
    }

    throw 'SafeSpeak is not installed and no local desktop build was found. Build SafeSpeak or pass -ExecutablePath.'
}

if ([string]::IsNullOrWhiteSpace($ShortcutName) -or
    $ShortcutName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw 'ShortcutName must be a valid Windows file name.'
}

if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    throw "The SafeSpeak icon is missing: $iconPath"
}

$resolvedExecutable = Resolve-SafeSpeakExecutable -RequestedPath $ExecutablePath
$desktopFolder = if ($PublicDesktop) {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
}
else {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
}
if ([string]::IsNullOrWhiteSpace($desktopFolder) -or
    -not (Test-Path -LiteralPath $desktopFolder -PathType Container)) {
    throw "Windows did not return an available desktop folder: $desktopFolder"
}

$shortcutPath = Join-Path $desktopFolder ($ShortcutName + '.lnk')
$shell = New-Object -ComObject WScript.Shell
try {
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        $existingShortcut = $shell.CreateShortcut($shortcutPath)
        $existingTarget = [string]$existingShortcut.TargetPath
        if (-not $Force -and
            -not [System.String]::Equals(
                [System.IO.Path]::GetFullPath($existingTarget),
                $resolvedExecutable,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A different shortcut already exists at $shortcutPath. Use -Force to replace it."
        }
    }

    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $resolvedExecutable
    $shortcut.WorkingDirectory = Split-Path $resolvedExecutable -Parent
    $shortcut.Description = 'Accessible livestream text-to-speech and moderation'
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.WindowStyle = 1
    $shortcut.Save()

    $savedShortcut = $shell.CreateShortcut($shortcutPath)
    if (-not [System.String]::Equals(
            [System.IO.Path]::GetFullPath([string]$savedShortcut.TargetPath),
            $resolvedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The desktop shortcut target could not be verified: $shortcutPath"
    }

    [pscustomobject]@{
        ShortcutPath = $shortcutPath
        TargetPath = [string]$savedShortcut.TargetPath
        WorkingDirectory = [string]$savedShortcut.WorkingDirectory
        IconLocation = [string]$savedShortcut.IconLocation
        Description = [string]$savedShortcut.Description
    }
}
finally {
    if ($null -ne $shell) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}
