# Genera SyncTwoFiles.ico (multi-resolucion) y SyncTwoFiles.png (256px)
Add-Type -AssemblyName System.Drawing

$OUT_DIR = $PSScriptRoot

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    $pad = if ($s -ge 48) { $s * 0.06 } else { 0 }
    $w = $s - 2 * $pad
    $bg = New-RoundedPath $pad $pad $w $w ($w * 0.225)
    $rect = New-Object Drawing.RectangleF($pad, $pad, $w, $w)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush($rect,
        [Drawing.Color]::FromArgb(90, 200, 250), [Drawing.Color]::FromArgb(10, 100, 240), [single]90)
    $g.FillPath($grad, $bg)

    $c = $s / 2
    if ($s -ge 48) {
        $dw = $w * 0.16; $dh = $w * 0.21; $dr = $w * 0.03
        $back = New-RoundedPath ($c - $dw * 0.85) ($c - $dh * 0.62) $dw $dh $dr
        $front = New-RoundedPath ($c - $dw * 0.15) ($c - $dh * 0.38) $dw $dh $dr
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(150, 255, 255, 255))), $back)
        $g.FillPath([Drawing.Brushes]::White, $front)
    }

    $r = $w * 0.30
    $penW = [Math]::Max(1.6, $w * 0.075)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), $penW
    $pen.StartCap = 'Round'
    $pen.CustomEndCap = New-Object Drawing.Drawing2D.AdjustableArrowCap(2.4, 2.0, $true)
    $g.DrawArc($pen, $c - $r, $c - $r, 2 * $r, 2 * $r, 205, 120)
    $g.DrawArc($pen, $c - $r, $c - $r, 2 * $r, 2 * $r, 25, 120)

    $g.Dispose()
    $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save("$OUT_DIR\SyncTwoFiles.png", [Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [IO.File]::Create("$OUT_DIR\SyncTwoFiles.ico")
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"Icono generado."

