[CmdletBinding()]
param([string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
$OutputDirectory = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $PSScriptRoot 'Assets' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-SafeSpeakAsset {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $path = Join-Path $OutputDirectory $Name
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(18, 18, 20))

        $padding = [Math]::Max(3, [Math]::Round([Math]::Min($Width, $Height) * 0.16))
        $diameter = [Math]::Max(8, [Math]::Min($Width, $Height) - (2 * $padding))
        $circleX = [Math]::Round(($Width - $diameter) / 2)
        $circleY = [Math]::Round(($Height - $diameter) / 2)

        $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(38, 208, 187))
        $ink = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(18, 18, 20), [Math]::Max(2, $diameter * 0.07))
        $ink.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $ink.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        try {
            $graphics.FillEllipse($accent, $circleX, $circleY, $diameter, $diameter)

            # A simple speech-wave mark that remains recognizable at 44 pixels.
            $centerX = $Width / 2
            $centerY = $Height / 2
            $waveWidth = $diameter * 0.48
            $waveHeight = $diameter * 0.42
            $left = $centerX - ($waveWidth / 2)
            $step = $waveWidth / 4
            $heights = @(0.28, 0.72, 1.0, 0.72, 0.28)

            for ($index = 0; $index -lt $heights.Count; $index++) {
                $lineHeight = $waveHeight * $heights[$index]
                $x = $left + ($step * $index)
                $graphics.DrawLine($ink, $x, $centerY - ($lineHeight / 2), $x, $centerY + ($lineHeight / 2))
            }
        }
        finally {
            $accent.Dispose()
            $ink.Dispose()
        }

        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-SafeSpeakAsset -Name 'StoreLogo.png' -Width 50 -Height 50
New-SafeSpeakAsset -Name 'Square44x44Logo.png' -Width 44 -Height 44
New-SafeSpeakAsset -Name 'Square150x150Logo.png' -Width 150 -Height 150
New-SafeSpeakAsset -Name 'Wide310x150Logo.png' -Width 310 -Height 150

Write-Host "Generated MSIX assets in $OutputDirectory"
