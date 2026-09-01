[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [string]$PayloadDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$msiProject = Join-Path $PSScriptRoot 'SafeSpeak.Installer\SafeSpeak.Installer.wixproj'
$iconPath = Join-Path $PSScriptRoot 'Assets\SafeSpeak.ico'
$licensePath = Join-Path $repoRoot 'LICENSE'
$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
$msiPath = [System.IO.Path]::GetFullPath($OutputPath)
$artifactRoot = Split-Path $msiPath -Parent
$stagingDirectory = Join-Path $artifactRoot ".msi-build-$Architecture-$([Guid]::NewGuid().ToString('N'))"
$licenseRtfPath = Join-Path $stagingDirectory 'SafeSpeak-License.rtf'
$msiOutputName = [System.IO.Path]::GetFileNameWithoutExtension($msiPath)

$upgradeCode = switch ($Architecture) {
    'x64' { '9DFCB478-F482-435F-ADA2-027D86C7BE3E' }
    'arm64' { 'D4B40CCD-56E6-49D2-AA60-2287EA54809C' }
}

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
        throw "$ToolName was not found. Install the Windows 10/11 SDK signing tools."
    }

    return $selected.FullName
}

function Get-MsiScalar {
    param(
        [Parameter(Mandatory)] $Database,
        [Parameter(Mandatory)] [string]$Query,
        [switch]$Integer
    )

    $view = $null
    $record = $null
    try {
        $view = $Database.OpenView($Query)
        [void]$view.Execute()
        $record = $view.Fetch()
        if (-not $record) {
            return $null
        }
        if ($Integer) {
            return [int]$record.IntegerData(1)
        }
        return ([string]$record.StringData(1)).Trim()
    }
    finally {
        if ($record) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        if ($view) {
            [void]$view.Close()
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
    }
}

function Get-MsiRowCount {
    param(
        [Parameter(Mandatory)] $Database,
        [Parameter(Mandatory)] [string]$Query
    )

    $view = $null
    $record = $null
    $count = 0
    try {
        $view = $Database.OpenView($Query)
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            $count++
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
            $record = $null
        }
        return $count
    }
    finally {
        if ($record) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        if ($view) {
            [void]$view.Close()
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
    }
}

function Remove-MsiStaging {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedStaging = [System.IO.Path]::GetFullPath($Path)
    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    if (-not $resolvedStaging.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove MSI staging outside the artifact directory: $resolvedStaging"
    }

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 20) {
                Write-Warning "MSI staging could not be removed after packaging: $resolvedStaging"
                return
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

foreach ($requiredPath in @($msiProject, $iconPath, $licensePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required MSI source is missing: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "MSI payload directory is missing: $payloadRoot"
}
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'SafeSpeak.App.exe') -PathType Leaf)) {
    throw 'MSI payload does not contain SafeSpeak.App.exe.'
}
if ([System.IO.Path]::GetExtension($msiPath) -ne '.msi') {
    throw 'OutputPath must end in .msi.'
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
Remove-Item -LiteralPath $msiPath -Force -ErrorAction SilentlyContinue

$licenseText = Get-Content -LiteralPath $licensePath -Raw
$escapedLicense = $licenseText.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
$escapedLicense = $escapedLicense.Replace("`r`n", "\par`r`n").Replace("`n", "\par`r`n")
$licenseRtf = "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18 $escapedLicense}"
[System.IO.File]::WriteAllText(
    $licenseRtfPath,
    $licenseRtf,
    [System.Text.UTF8Encoding]::new($false))

try {
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'build', $msiProject,
        '-c', 'Release',
        '--nologo',
        "-p:InstallerArchitecture=$Architecture",
        "-p:PackageVersion=$PackageVersion",
        "-p:PayloadDirectory=$payloadRoot",
        "-p:UpgradeCode=$upgradeCode",
        "-p:IconPath=$iconPath",
        "-p:LicenseRtfPath=$licenseRtfPath",
        "-p:MsiOutputName=$msiOutputName",
        "-p:MsiOutputDirectory=$stagingDirectory",
        "-p:MsiIntermediateOutputPath=$(Join-Path $stagingDirectory 'obj')"
    )

    $localizedMsiPath = Join-Path $stagingDirectory "en-US\$([System.IO.Path]::GetFileName($msiPath))"
    $unlocalizedMsiPath = Join-Path $stagingDirectory ([System.IO.Path]::GetFileName($msiPath))
    $builtMsiPath = @($localizedMsiPath, $unlocalizedMsiPath) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if ($builtMsiPath) {
        Move-Item -LiteralPath $builtMsiPath -Destination $msiPath -Force
    }

    if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
        throw "WiX completed without the expected MSI: $msiPath"
    }

    $windowsInstaller = $null
    $database = $null
    try {
        $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
        $database = $windowsInstaller.OpenDatabase($msiPath, 0)
        $productName = Get-MsiScalar -Database $database `
            -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductName'"
        $manufacturer = Get-MsiScalar -Database $database `
            -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='Manufacturer'"
        $productVersion = Get-MsiScalar -Database $database `
            -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'"
        $fileCount = Get-MsiRowCount -Database $database -Query 'SELECT `File` FROM `File`'
        $shortcutCount = Get-MsiRowCount -Database $database `
            -Query 'SELECT `Shortcut` FROM `Shortcut`'
        $upgradeCount = Get-MsiRowCount -Database $database `
            -Query 'SELECT `UpgradeCode` FROM `Upgrade`'
        $repairResetCount = Get-MsiRowCount -Database $database `
            -Query 'SELECT `RemoveFolderEx` FROM `Wix4RemoveFolderEx`'
        $repairCondition = Get-MsiScalar -Database $database `
            -Query "SELECT ``Condition`` FROM ``Component`` WHERE ``Component``='RepairResetComponent'"
        $appDataProperty = Get-MsiScalar -Database $database `
            -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='SAFESPEAKAPPDATA'"
    }
    finally {
        if ($database) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        }
        if ($windowsInstaller) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($windowsInstaller)
        }
        $database = $null
        $windowsInstaller = $null
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }

    if ($productName -ne 'SafeSpeak') {
        throw "MSI verification found unexpected ProductName '$productName'."
    }
    if ($manufacturer -ne 'The Project Hub') {
        throw "MSI verification found unexpected Manufacturer '$manufacturer'."
    }
    if ($productVersion -ne $PackageVersion) {
        throw "MSI version '$productVersion' does not match '$PackageVersion'."
    }
    if ($fileCount -lt 5) {
        throw "MSI verification found only $fileCount payload files."
    }
    if ($shortcutCount -lt 1) {
        throw 'MSI verification did not find the SafeSpeak Start-menu shortcut.'
    }
    if ($upgradeCount -lt 1) {
        throw 'MSI verification did not find major-upgrade metadata.'
    }
    if ($repairResetCount -lt 1) {
        throw 'MSI verification did not find the Local AppData repair-reset action.'
    }
    if ($repairCondition -ne 'REINSTALL AND NOT REMOVE="ALL"') {
        throw "MSI verification found an unexpected repair-reset condition '$repairCondition'."
    }
    if ($appDataProperty -ne '%LOCALAPPDATA%\SafeSpeak') {
        throw "MSI verification found an unexpected Local AppData target '$appDataProperty'."
    }

    if ($CertificateThumbprint) {
        $signTool = Find-WindowsSdkTool -ToolName 'signtool.exe'
        Invoke-CheckedCommand -FilePath $signTool -ArgumentList @(
            'sign', '/sha1', $CertificateThumbprint,
            '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256',
            $msiPath
        )
        Invoke-CheckedCommand -FilePath $signTool -ArgumentList @('verify', '/pa', '/v', $msiPath)
    }
    else {
        Write-Warning 'The MSI is valid but unsigned. Users can install it, but Windows will show an unknown-publisher warning until a code-signing certificate is configured.'
    }

    $msiInfo = Get-Item -LiteralPath $msiPath
    Write-Host "Created MSI package: $msiPath"
    Write-Host "MSI payload files: $fileCount"
    Write-Host 'MSI repair reset: %LOCALAPPDATA%\SafeSpeak'
    Write-Host "MSI SHA-256: $((Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash)"
    Write-Output $msiInfo
}
finally {
    if (-not $KeepStaging -and (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-MsiStaging -Path $stagingDirectory
    }
}
