param([Parameter(Mandatory=$true)][string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot '..\countdown-samples'
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$samples = @(
    @{ File='codex-clipboard-3e7ca8a6-e4e0-4126-abdb-b69fae4cbf52.png'; Name='main-11'; X=1640; Y=927; W=240; H=34 },
    @{ File='codex-clipboard-7d23c02a-585d-4805-8261-c1503361baee.png'; Name='dialog-0'; X=1230; Y=698; W=300; H=44 },
    @{ File='codex-clipboard-4faf05f0-3b5a-4cab-a9bb-07932f1e1aac.png'; Name='main-422'; X=1640; Y=927; W=240; H=34 }
)
foreach ($item in $samples) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $SourceDirectory $item.File))
    $crop = [System.Drawing.Bitmap]::new($item.W * 2, $item.H * 2)
    $graphics = [System.Drawing.Graphics]::FromImage($crop)
    try {
        $rect = [System.Drawing.RectangleF]::new(
            $item.X * $image.Width / 2048.0, $item.Y * $image.Height / 1152.0,
            $item.W * $image.Width / 2048.0, $item.H * $image.Height / 1152.0)
        $graphics.DrawImage($image, [System.Drawing.RectangleF]::new(0,0,$crop.Width,$crop.Height),
            $rect, [System.Drawing.GraphicsUnit]::Pixel)
        $crop.Save((Join-Path $output ($item.Name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $crop.Dispose(); $image.Dispose() }
}
