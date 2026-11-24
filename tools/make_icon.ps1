# Generates src\NitroCustom.App\Assets\nitrocustom.ico (PNG-compressed, 16..256 px) and a 512 px preview PNG.
# The mark: an italic, faceted "N" with an orange-to-red face and a dark side face (original design, Nitro-like mood).
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\NitroCustom.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# Same outline as the window emblem (MainWindow.xaml), in a 100x100 box.
$outline = @(@(12,96), @(24,4), @(42,20), @(64,62), @(70,18), @(90,4), @(80,96), @(62,82), @(38,40), @(30,86))

function New-Points([double]$scale, [double]$dx, [double]$dy) {
    [System.Drawing.PointF[]]($outline | ForEach-Object { New-Object System.Drawing.PointF(($_[0] * $scale + $dx), ($_[1] * $scale + $dy)) })
}

function New-Mark([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $size / 100.0
    $small = $size -le 24
    $pad = if ($small) { 0 } else { -2 * $scale }

    if (-not $small) {
        # Side face: darker copy shifted right/down gives the extruded look.
        $side = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x6E, 0x0E, 0x06))
        $g.FillPolygon($side, (New-Points $scale ($pad + 5 * $scale) ($pad + 3 * $scale)))
        $side.Dispose()
    }
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $face = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 0xFF, 0xB8, 0x1C), [System.Drawing.Color]::FromArgb(255, 0xE3, 0x1E, 0x24), 60)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 0xFF, 0xC2, 0x2A), [System.Drawing.Color]::FromArgb(255, 0xFF, 0x6A, 0x12), [System.Drawing.Color]::FromArgb(255, 0xE3, 0x1E, 0x24))
    $blend.Positions = @(0.0, 0.5, 1.0)
    $face.InterpolationColors = $blend
    $g.FillPolygon($face, (New-Points $scale $pad $pad))
    if (-not $small) {
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, 0xFF, 0xE2, 0x9A)), ([Math]::Max(1, $size / 128))
        $g.DrawPolygon($edge, (New-Points $scale $pad $pad))
        $edge.Dispose()
    }
    $face.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Mark $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$ico = Join-Path $assets 'nitrocustom.ico'
$fs = [System.IO.File]::Create($ico)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

$preview = New-Mark 512
$preview.Save((Join-Path $assets 'nitrocustom_512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
"Icon: $ico"
