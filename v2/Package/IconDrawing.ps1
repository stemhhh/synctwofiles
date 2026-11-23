# Dibujo del icono de la app (lo usan Make-Logos.ps1 y Store\Make-StoreArt.ps1).
Add-Type -AssemblyName System.Drawing

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

# Dibuja el icono de lado $s con la esquina superior izquierda en ($ox, $oy).
function Draw-Icon($g, [single]$ox, [single]$oy, [single]$s) {
    $pad = if ($s -ge 48) { $s * 0.06 } else { 0 }
    $w = $s - 2 * $pad
    $bg = New-RoundedPath ($ox + $pad) ($oy + $pad) $w $w ($w * 0.225)
    $rect = New-Object Drawing.RectangleF(($ox + $pad), ($oy + $pad), $w, $w)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush($rect,
        [Drawing.Color]::FromArgb(90, 200, 250), [Drawing.Color]::FromArgb(10, 100, 240), [single]90)
    $g.FillPath($grad, $bg)

    $cx = $ox + $s / 2
    $cy = $oy + $s / 2
    if ($s -ge 48) {
        $dw = $w * 0.16; $dh = $w * 0.21; $dr = $w * 0.03
        $back = New-RoundedPath ($cx - $dw * 0.85) ($cy - $dh * 0.62) $dw $dh $dr
        $front = New-RoundedPath ($cx - $dw * 0.15) ($cy - $dh * 0.38) $dw $dh $dr
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(150, 255, 255, 255))), $back)
        $g.FillPath([Drawing.Brushes]::White, $front)
    }
    $r = $w * 0.30
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([Math]::Max(1.6, $w * 0.075))
    $pen.StartCap = 'Round'
    $pen.CustomEndCap = New-Object Drawing.Drawing2D.AdjustableArrowCap(2.4, 2.0, $true)
    $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 205, 120)
    $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 25, 120)
}
