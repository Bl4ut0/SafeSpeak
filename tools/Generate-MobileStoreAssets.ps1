[CmdletBinding()]
param(
    [string]$SourceIcon = (Join-Path $PSScriptRoot '..\installer\Assets\SafeSpeakIconMaster-v1.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\mobile-store-assets')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

$sourcePath = [IO.Path]::GetFullPath($SourceIcon)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.File]::Exists($sourcePath)) {
    throw "SafeSpeak icon source not found: $sourcePath"
}

[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$source = [Drawing.Image]::FromFile($sourcePath)

function Save-SquareAsset {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [int]$Size,
        [Parameter(Mandatory)] [Drawing.Color]$Background
    )

    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear($Background)
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $padding = [Math]::Round($Size * 0.08)
        $graphics.DrawImage($source, $padding, $padding, $Size - (2 * $padding), $Size - (2 * $padding))
        $destination = Join-Path $outputPath $Name
        $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
        return $destination
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-GoogleFeatureGraphic {
    $width = 1024
    $height = 500
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $titleFont = [Drawing.Font]::new('Segoe UI', 64, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $subtitleFont = [Drawing.Font]::new('Segoe UI', 28, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $titleBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245, 247, 255))
    $subtitleBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(185, 196, 223))
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(16, 20, 38))
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($source, 72, 90, 320, 320)
        $graphics.DrawString('SafeSpeak', $titleFont, $titleBrush, 440, 155)
        $graphics.DrawString('Safer livestream speech', $subtitleFont, $subtitleBrush, 445, 250)
        $destination = Join-Path $outputPath 'GooglePlay-FeatureGraphic-1024x500.png'
        $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
        return $destination
    }
    finally {
        $subtitleBrush.Dispose()
        $titleBrush.Dispose()
        $subtitleFont.Dispose()
        $titleFont.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    $background = [Drawing.Color]::FromArgb(16, 20, 38)
    $assets = @(
        Save-SquareAsset -Name 'Apple-AppStore-Icon-1024.png' -Size 1024 -Background $background
        Save-SquareAsset -Name 'GooglePlay-Icon-512.png' -Size 512 -Background $background
        Save-GoogleFeatureGraphic
    )

    $report = foreach ($asset in $assets) {
        $image = [Drawing.Image]::FromFile($asset)
        try {
            [pscustomobject]@{
                file = [IO.Path]::GetFileName($asset)
                width = $image.Width
                height = $image.Height
                sha256 = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
            }
        }
        finally {
            $image.Dispose()
        }
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'assets.json') -Encoding utf8
    $report | Format-Table -AutoSize
}
finally {
    $source.Dispose()
}
