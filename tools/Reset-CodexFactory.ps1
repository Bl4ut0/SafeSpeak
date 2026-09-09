[CmdletBinding()]
param(
    [ValidateRange(30, 1800)]
    [int] $WaitSeconds = 600,

    [switch] $ConfirmPermanentReset,

    [switch] $ListTargets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedUserRoot = 'C:\Users\bl4ut'
$packageDataRoot = 'C:\Users\bl4ut\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0'
$logPath = 'C:\Users\bl4ut\AppData\Local\Temp\CodexFactoryReset.log'
$completePath = 'C:\Users\bl4ut\AppData\Local\Temp\CodexFactoryReset.complete'
$directoryTargets = @(
    'C:\Users\bl4ut\.codex',
    'C:\Users\bl4ut\AppData\Local\Codex',
    'C:\Users\bl4ut\AppData\Local\OpenAI\Codex',
    'C:\Users\bl4ut\AppData\Roaming\Codex',
    'C:\Users\bl4ut\AppData\Roaming\OpenAI\Codex'
)
$codexProcessNames = @(
    'ChatGPT',
    'codex',
    'codex-code-mode-host'
)

function Write-ResetLog {
    param([Parameter(Mandatory)] [string] $Message)

    $line = '{0:o} {1}' -f [DateTime]::UtcNow, $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
}

function Resolve-ValidatedExactTarget {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $AllowedPaths
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $isAllowed = @($AllowedPaths).Where({
        [string]::Equals(
            $fullPath,
            [IO.Path]::GetFullPath($_),
            [StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0
    if (-not $isAllowed) {
        throw "Reset target is not in the fixed allowlist: $fullPath"
    }

    $userPrefix = $expectedUserRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($userPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Reset target is outside the expected user profile: $fullPath"
    }
    if ([string]::Equals($fullPath, $expectedUserRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to target the user profile root.'
    }

    return $fullPath
}

function Remove-ExactDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = Resolve-ValidatedExactTarget -Path $Path -AllowedPaths $directoryTargets
    if (-not (Test-Path -LiteralPath $fullPath)) {
        Write-ResetLog "Already absent: $fullPath"
        return
    }

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction Stop
            Write-ResetLog "Permanently removed: $fullPath"
            return
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }
            Start-Sleep -Seconds 2
        }
    }
}

if ($ListTargets) {
    @($directoryTargets) + @("$packageDataRoot\*") | ForEach-Object { Write-Output $_ }
    exit 0
}
if (-not $ConfirmPermanentReset) {
    throw 'Permanent reset is not armed. Review the script, then run it with -ConfirmPermanentReset.'
}

Remove-Item -LiteralPath $logPath, $completePath -Force -ErrorAction SilentlyContinue
Write-ResetLog 'Codex factory reset helper started. No backup will be created.'

$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
do {
    $running = @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $codexProcessNames -contains $_.ProcessName }
    )
    if ($running.Count -eq 0) {
        break
    }
    if ([DateTime]::UtcNow -ge $deadline) {
        Write-ResetLog 'Timed out waiting for Codex to close. No data was deleted.'
        exit 2
    }
    Start-Sleep -Milliseconds 500
} while ($true)

# Let WebView and helper processes release their file handles after the main app exits.
Start-Sleep -Seconds 3

foreach ($target in $directoryTargets) {
    Remove-ExactDirectory -Path $target
}

$resolvedPackageRoot = Resolve-ValidatedExactTarget -Path $packageDataRoot -AllowedPaths @($packageDataRoot)
if (Test-Path -LiteralPath $resolvedPackageRoot) {
    $packagePrefix = $resolvedPackageRoot.TrimEnd('\') + '\'
    foreach ($child in @(Get-ChildItem -LiteralPath $resolvedPackageRoot -Force)) {
        $childPath = [IO.Path]::GetFullPath($child.FullName)
        if (-not $childPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Package-data child resolved outside the package root: $childPath"
        }
        Remove-Item -LiteralPath $childPath -Recurse -Force -ErrorAction Stop
        Write-ResetLog "Permanently removed package data: $childPath"
    }
}
else {
    Write-ResetLog "Package data root already absent: $resolvedPackageRoot"
}

$completion = @(
    'Codex factory reset completed successfully.',
    "Completed UTC: $([DateTime]::UtcNow.ToString('o'))",
    "Log: $logPath"
) -join [Environment]::NewLine
Set-Content -LiteralPath $completePath -Value $completion -Encoding utf8
Write-ResetLog 'Codex factory reset completed successfully.'

# The helper is disposable and is outside every deletion target.
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
