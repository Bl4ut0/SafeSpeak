param()

Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path $PSScriptRoot '..\com.bl4ut0.safespeak.sdPlugin\imgs\plugin'
$outputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-SafeSpeakIcon {
    param(
        [Parameter(Mandatory)]
        [int] $Size,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(23, 35, 61))

    $scale = $Size / 256.0
    $shieldPoints = [System.Drawing.PointF[]] @(
        [System.Drawing.PointF]::new(128 * $scale, 24 * $scale),
        [System.Drawing.PointF]::new(50 * $scale, 56 * $scale),
        [System.Drawing.PointF]::new(50 * $scale, 116 * $scale),
        [System.Drawing.PointF]::new(70 * $scale, 176 * $scale),
        [System.Drawing.PointF]::new(128 * $scale, 224 * $scale),
        [System.Drawing.PointF]::new(186 * $scale, 176 * $scale),
        [System.Drawing.PointF]::new(206 * $scale, 116 * $scale),
        [System.Drawing.PointF]::new(206 * $scale, 56 * $scale)
    )

    $shieldPen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 13 * $scale)
    $shieldPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPolygon($shieldPen, $shieldPoints)

    $speechPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(83, 214, 183), 15 * $scale)
    $speechPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $speechPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($speechPen, 92 * $scale, 101 * $scale, 164 * $scale, 101 * $scale)
    $graphics.DrawLine($speechPen, 92 * $scale, 137 * $scale, 148 * $scale, 137 * $scale)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $speechPen.Dispose()
    $shieldPen.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

New-SafeSpeakIcon -Size 256 -Path (Join-Path $outputDirectory 'marketplace.png')
New-SafeSpeakIcon -Size 512 -Path (Join-Path $outputDirectory 'marketplace@2x.png')
