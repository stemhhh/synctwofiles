# Genera los logos del paquete MSIX (con escalas y tamanos) con el mismo dibujo que assets\make-icon.ps1.
param([string]$OutDir = (Join-Path $PSScriptRoot 'Assets'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'IconDrawing.ps1')
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Lienzo transparente de $cw x $ch con el icono centrado ocupando $ratio del lado menor.
function Save-Logo([string]$name, [int]$cw, [int]$ch, [double]$ratio) {
    $bmp = New-Object Drawing.Bitmap $cw, $ch, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $s = [Math]::Round([Math]::Min($cw, $ch) * $ratio)
    Draw-Icon $g (($cw - $s) / 2) (($ch - $s) / 2) $s
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $name), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }
foreach ($k in $scales.Keys) {
    $f = $scales[$k]
    Save-Logo "Square44x44Logo.scale-$k.png" ([int][Math]::Round(44 * $f)) ([int][Math]::Round(44 * $f)) 1.0
    Save-Logo "Square150x150Logo.scale-$k.png" ([int][Math]::Round(150 * $f)) ([int][Math]::Round(150 * $f)) 0.62
    Save-Logo "Wide310x150Logo.scale-$k.png" ([int][Math]::Round(310 * $f)) ([int][Math]::Round(150 * $f)) 0.62
    Save-Logo "StoreLogo.scale-$k.png" ([int][Math]::Round(50 * $f)) ([int][Math]::Round(50 * $f)) 1.0
}
foreach ($t in 16, 24, 32, 48, 256) {
    Save-Logo "Square44x44Logo.targetsize-$t.png" $t $t 1.0
    Save-Logo "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t 1.0
}
"Logos generados en $OutDir"
