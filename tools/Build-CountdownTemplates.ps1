param([Parameter(Mandatory=$true)][string]$SampleDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\src\DeltaMarketAssistant.Core\Assets'
$labels = @(
    @{Name='main-11'; Characters=@{0='L';1='0';2='分';3='1';4='1';5='秒'}},
    @{Name='dialog-0'; Characters=@{0='L';1='0';2='分';3='0';4='秒'}},
    @{Name='main-422'; Characters=@{0='L';1='7';2='分';3='2';4='秒'}}
)
foreach ($sample in $labels) {
    $image = [System.Drawing.Bitmap]::new((Join-Path $SampleDirectory ($sample.Name + '.png')))
    try {
        $columns = [bool[]]::new($image.Width)
        for ($x=0; $x -lt $image.Width; $x++) {
            for ($y=0; $y -lt $image.Height; $y++) {
                $p=$image.GetPixel($x,$y)
                $min=[Math]::Min($p.R,[Math]::Min($p.G,$p.B))
                $max=[Math]::Max($p.R,[Math]::Max($p.G,$p.B))
                if ($min -ge 110 -and $max-$min -lt 55) { $columns[$x]=$true; break }
            }
        }
        $index=0
        for ($x=0; $x -lt $image.Width; $x++) {
            if (!$columns[$x]) { continue }
            $left=$x
            while ($x+1 -lt $image.Width -and $columns[$x+1]) { $x++ }
            if ($sample.Characters.ContainsKey($index)) {
                $value=[int][char]$sample.Characters[$index]
                $crop=$image.Clone([System.Drawing.Rectangle]::new($left,0,$x-$left+1,$image.Height),
                    [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
                try { $crop.Save((Join-Path $assets "countdown-$value-$($sample.Name)-$index.png"),
                    [System.Drawing.Imaging.ImageFormat]::Png) }
                finally { $crop.Dispose() }
                Write-Output "$($sample.Name) $index = $($sample.Characters[$index]) x=$left..$x"
            }
            $index++
        }
    } finally { $image.Dispose() }
}
