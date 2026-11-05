param(
    [string]$SourceImage = (Join-Path $PSScriptRoot "..\src\DeltaMarketAssistant\Assets\icon-source.jpg"),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\src\DeltaMarketAssistant\Assets")
)

Add-Type -AssemblyName System.Drawing

function Add-IcoEntry {
    param(
        [System.IO.BinaryWriter]$Writer,
        [int]$Size,
        [byte[]]$Data,
        [int]$Offset
    )

    $dimension = if ($Size -ge 256) { 0 } else { $Size }
    $Writer.Write([byte]$dimension)
    $Writer.Write([byte]$dimension)
    $Writer.Write([byte]0)
    $Writer.Write([byte]0)
    $Writer.Write([uint16]1)
    $Writer.Write([uint16]32)
    $Writer.Write([uint32]$Data.Length)
    $Writer.Write([uint32]$Offset)
}

$ErrorActionPreference = "Stop"
if (-not [System.IO.File]::Exists($SourceImage)) {
    throw "Icon source image was not found: $SourceImage"
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$pngPath = Join-Path $OutputDirectory "cartoon-assistant.png"
$icoPath = Join-Path $OutputDirectory "cartoon-assistant.ico"

$source = [System.Drawing.Image]::FromFile($SourceImage)
$usableHeight = $source.Height - [Math]::Round($source.Height * 0.04)
$cropSize = [Math]::Min($source.Width, $usableHeight)
$cropX = [Math]::Round(($source.Width - $cropSize) / 2)
$crop = [System.Drawing.Rectangle]::new($cropX, 0, $cropSize, $cropSize)

$canvas = [System.Drawing.Bitmap]::new(1024, 1024, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$destination = [System.Drawing.Rectangle]::new(0, 0, 1024, 1024)
$graphics.DrawImage($source, $destination, $crop.X, $crop.Y, $crop.Width, $crop.Height, [System.Drawing.GraphicsUnit]::Pixel)
$canvas.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $scaled = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaled)
    $scaledGraphics.Clear([System.Drawing.Color]::Transparent)
    $scaledGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaledGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scaledGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scaledGraphics.DrawImage($canvas, 0, 0, $size, $size)
    $stream = [System.IO.MemoryStream]::new()
    $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose()
    $scaledGraphics.Dispose()
    $scaled.Dispose()
}

$file = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)
for ($index = 0; $index -lt $images.Count; $index++) {
    Add-IcoEntry $writer $sizes[$index] $images[$index] $offset
    $offset += $images[$index].Length
}
foreach ($image in $images) {
    $writer.Write($image)
}

$writer.Dispose()
$file.Dispose()
$graphics.Dispose()
$canvas.Dispose()
$source.Dispose()

Write-Output $pngPath
Write-Output $icoPath
