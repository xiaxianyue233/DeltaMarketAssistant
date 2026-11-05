param(
    [string]$QueueImage,
    [string]$RollImage,
    [string]$SuccessImage,
    [string]$ListingImage
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot '..\src\DeltaMarketAssistant.Core\Assets'
[System.IO.Directory]::CreateDirectory($output) | Out-Null

function Export-Crop([string]$sourcePath, [string]$name, [int]$x, [int]$y, [int]$width, [int]$height) {
    $source = [System.Drawing.Image]::FromFile($sourcePath)
    $crop = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($crop)
    try {
        $target = [System.Drawing.RectangleF]::new(0, 0, $width, $height)
        $rect = [System.Drawing.RectangleF]::new(
            $x * $source.Width / 2048.0, $y * $source.Height / 1152.0,
            $width * $source.Width / 2048.0, $height * $source.Height / 1152.0)
        $graphics.DrawImage($source, $target, $rect, [System.Drawing.GraphicsUnit]::Pixel)
        $crop.Save((Join-Path $output $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $crop.Dispose()
        $source.Dispose()
    }
}

if ($QueueImage) {
    Export-Crop $QueueImage 'queue-full.png' 914 169 222 24
    Export-Crop $QueueImage 'dialog-back.png' 970 982 92 47
}
if ($RollImage) {
    Export-Crop $RollImage 'roll-failed.png' 711 169 211 24
}
if ($SuccessImage) {
    Export-Crop $SuccessImage 'success-title.png' 130 160 260 48
    Export-Crop $SuccessImage 'success-skip.png' 1768 1001 54 29
}
if ($ListingImage) {
    Export-Crop $ListingImage 'listing-duration-prefix.png' 1218 677 78 26
    Export-Crop $ListingImage 'dialog-back-720.png' 970 982 92 47
}
