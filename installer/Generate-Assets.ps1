[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$MasterIconPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$OutputDirectory = if ($OutputDirectory) {
    $OutputDirectory
}
else {
    Join-Path $PSScriptRoot 'Assets'
}

$MasterIconPath = if ($MasterIconPath) {
    $MasterIconPath
}
else {
    Join-Path $PSScriptRoot 'Assets\SafeSpeakIconMaster-v1.png'
}

if (-not (Test-Path -LiteralPath $MasterIconPath -PathType Leaf)) {
    throw "SafeSpeak icon master was not found: $MasterIconPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$master = [System.Drawing.Bitmap]::new($MasterIconPath)

function New-SafeSpeakBitmap {
    param(
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height,
        [Parameter(Mandatory)] [int]$ArtworkSize
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $left = [Math]::Floor(($Width - $ArtworkSize) / 2)
        $top = [Math]::Floor(($Height - $ArtworkSize) / 2)
        $destination = [System.Drawing.Rectangle]::new($left, $top, $ArtworkSize, $ArtworkSize)
        $graphics.DrawImage(
            $master,
            $destination,
            0,
            0,
            $master.Width,
            $master.Height,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function New-SafeSpeakPng {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height,
        [Parameter(Mandatory)] [int]$ArtworkSize
    )

    $path = Join-Path $OutputDirectory $Name
    $bitmap = New-SafeSpeakBitmap -Width $Width -Height $Height -ArtworkSize $ArtworkSize
    try {
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-SafeSpeakPngFrame {
    param([Parameter(Mandatory)] [int]$Size)

    $bitmap = New-SafeSpeakBitmap -Width $Size -Height $Size -ArtworkSize $Size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        # Preserve each PNG as one frame instead of letting PowerShell flatten
        # the byte array into the function's output pipeline.
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

function New-SafeSpeakIco {
    param([Parameter(Mandatory)] [string]$Name)

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = @($sizes | ForEach-Object { Get-SafeSpeakPngFrame -Size $_ })
    $path = Join-Path $OutputDirectory $Name
    $stream = [System.IO.File]::Open(
        $path,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $writer = [System.IO.BinaryWriter]::new($stream)

    try {
        # ICONDIR header: reserved, image type, frame count.
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        for ($index = 0; $index -lt $frames.Count; $index++) {
            $size = $sizes[$index]
            $frame = $frames[$index]
            $encodedSize = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

try {
    New-SafeSpeakPng -Name 'StoreLogo.png' -Width 50 -Height 50 -ArtworkSize 50
    New-SafeSpeakPng -Name 'Square44x44Logo.png' -Width 44 -Height 44 -ArtworkSize 44
    New-SafeSpeakPng -Name 'Square150x150Logo.png' -Width 150 -Height 150 -ArtworkSize 150
    New-SafeSpeakPng -Name 'Wide310x150Logo.png' -Width 310 -Height 150 -ArtworkSize 150
    New-SafeSpeakIco -Name 'SafeSpeak.ico'
}
finally {
    $master.Dispose()
}

Write-Host "Generated branded MSIX and executable assets from $MasterIconPath in $OutputDirectory"
