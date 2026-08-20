[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetRoot = Join-Path $PSScriptRoot 'assets'
$actionRoot = Join-Path $assetRoot 'actions'
$stateRoot = Join-Path $assetRoot 'states'
New-Item -ItemType Directory -Force -Path $actionRoot, $stateRoot | Out-Null

function New-IconFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $Size,
        [Parameter(Mandatory)] [string] $Glyph,
        [Parameter(Mandatory)] [System.Drawing.Color] $Foreground,
        [System.Drawing.Color] $Background = [System.Drawing.Color]::Transparent,
        [double] $FontScale = 0.42
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $font = $null
    $foregroundBrush = $null
    $backgroundBrush = $null
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.Color]::Transparent)

        if ($Background.A -gt 0) {
            $backgroundBrush = [System.Drawing.SolidBrush]::new($Background)
            $graphics.FillRectangle($backgroundBrush, 0, 0, $Size, $Size)
        }

        $font = [System.Drawing.Font]::new('Segoe UI', [single]($Size * $FontScale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $foregroundBrush = [System.Drawing.SolidBrush]::new($Foreground)
        $format = [System.Drawing.StringFormat]::new()
        try {
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString($Glyph, $font, $foregroundBrush, [System.Drawing.RectangleF]::new(0, 0, $Size, $Size), $format)
        }
        finally {
            $format.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($backgroundBrush) { $backgroundBrush.Dispose() }
        if ($foregroundBrush) { $foregroundBrush.Dispose() }
        if ($font) { $font.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$white = [System.Drawing.Color]::White
$cyan = [System.Drawing.Color]::FromArgb(255, 0, 231, 249)
$green = [System.Drawing.Color]::FromArgb(255, 0, 255, 102)
$amber = [System.Drawing.Color]::FromArgb(255, 255, 170, 0)
$red = [System.Drawing.Color]::FromArgb(255, 255, 51, 51)
$gray = [System.Drawing.Color]::FromArgb(255, 150, 150, 158)
$dark = [System.Drawing.Color]::FromArgb(255, 18, 18, 20)

$actionGlyphs = [ordered]@{
    arm = 'A'; autoplay = 'P'; pause = 'II'; english = 'E'; usernames = 'U';
    context = 'C'; audience = 'F'; strictness = 'S'; panic = '!'; skip = '>>';
    next = '>'; status = 'i'; clear = 'X'
}

foreach ($entry in $actionGlyphs.GetEnumerator()) {
    New-IconFile -Path (Join-Path $actionRoot "$($entry.Key).png") -Size 20 -Glyph $entry.Value -Foreground $white -FontScale 0.52
    New-IconFile -Path (Join-Path $actionRoot "$($entry.Key)@2x.png") -Size 40 -Glyph $entry.Value -Foreground $white -FontScale 0.52
}

$states = @(
    @{ Name = 'disarmed'; Glyph = 'D'; Color = $gray },
    @{ Name = 'armed'; Glyph = 'A'; Color = $green },
    @{ Name = 'manual'; Glyph = 'M'; Color = $amber },
    @{ Name = 'auto'; Glyph = 'A'; Color = $cyan },
    @{ Name = 'play'; Glyph = '>'; Color = $green },
    @{ Name = 'pause'; Glyph = 'II'; Color = $amber },
    @{ Name = 'multilingual'; Glyph = 'ML'; Color = $gray },
    @{ Name = 'english'; Glyph = 'EN'; Color = $cyan },
    @{ Name = 'usernames_off'; Glyph = 'U-'; Color = $gray },
    @{ Name = 'usernames_on'; Glyph = 'U+'; Color = $cyan },
    @{ Name = 'context_off'; Glyph = 'C-'; Color = $gray },
    @{ Name = 'context_on'; Glyph = 'C+'; Color = $cyan },
    @{ Name = 'audience'; Glyph = 'F'; Color = $white },
    @{ Name = 'strictness'; Glyph = 'S'; Color = $white },
    @{ Name = 'panic'; Glyph = '!'; Color = $red },
    @{ Name = 'skip'; Glyph = '>>'; Color = $white },
    @{ Name = 'next'; Glyph = '>'; Color = $green },
    @{ Name = 'status'; Glyph = 'i'; Color = $cyan },
    @{ Name = 'clear'; Glyph = 'X'; Color = $amber }
)

foreach ($state in $states) {
    New-IconFile -Path (Join-Path $stateRoot "$($state.Name).png") -Size 72 -Glyph $state.Glyph -Foreground $state.Color -Background $dark
    New-IconFile -Path (Join-Path $stateRoot "$($state.Name)@2x.png") -Size 144 -Glyph $state.Glyph -Foreground $state.Color -Background $dark
}

New-IconFile -Path (Join-Path $assetRoot 'category.png') -Size 28 -Glyph 'S' -Foreground $white -FontScale 0.58
New-IconFile -Path (Join-Path $assetRoot 'category@2x.png') -Size 56 -Glyph 'S' -Foreground $white -FontScale 0.58
New-IconFile -Path (Join-Path $assetRoot 'plugin.png') -Size 256 -Glyph 'SS' -Foreground $cyan -Background $dark -FontScale 0.35
New-IconFile -Path (Join-Path $assetRoot 'plugin@2x.png') -Size 512 -Glyph 'SS' -Foreground $cyan -Background $dark -FontScale 0.35

Write-Output "Generated Stream Deck assets in $assetRoot"
