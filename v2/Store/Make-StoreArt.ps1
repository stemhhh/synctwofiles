# Genera las imagenes de la ficha de la Store: poster 9:16, caja 1:1, iconos y capturas enmarcadas a 1920x1080.
# Las capturas sin marco se toman con tools\Ui-Smoke.ps1 y se dejan en screenshots\raw.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\Package\IconDrawing.ps1')
$imgDir = Join-Path $PSScriptRoot 'images'
$rawDir = Join-Path $PSScriptRoot 'screenshots\raw'
$shotDir = Join-Path $PSScriptRoot 'screenshots'
New-Item -ItemType Directory -Force $imgDir | Out-Null
$appName = 'Sync two File'

function New-Canvas([int]$w, [int]$h, [bool]$transparent) {
    $bmp = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    if ($transparent) { $g.Clear([Drawing.Color]::Transparent) }
    else {
        $rect = New-Object Drawing.Rectangle 0, 0, $w, $h
        $bg = New-Object Drawing.Drawing2D.LinearGradientBrush($rect,
            [Drawing.Color]::FromArgb(14, 42, 92), [Drawing.Color]::FromArgb(10, 12, 24), [single]70)
        $g.FillRectangle($bg, $rect)
        $glow = New-Object Drawing.Drawing2D.GraphicsPath
        $glow.AddEllipse([single](-$w * 0.3), [single](-$h * 0.35), [single]($w * 1.6), [single]($h * 0.9))
        $pgb = New-Object Drawing.Drawing2D.PathGradientBrush $glow
        $pgb.CenterColor = [Drawing.Color]::FromArgb(70, 40, 150, 255)
        $pgb.SurroundColors = @([Drawing.Color]::FromArgb(0, 40, 150, 255))
        $g.FillPath($pgb, $glow)
    }
    @{ Bmp = $bmp; G = $g }
}

function Save-Canvas($c, [string]$path) {
    $c.G.Dispose()
    $c.Bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $c.Bmp.Dispose()
}

function Draw-Name($g, [int]$w, [single]$top, [single]$size) {
    $font = New-Object Drawing.Font 'Segoe UI Semibold', $size, ([Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $g.DrawString($appName, $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, $top, $w, ($size * 1.6)), $fmt)
}

# Poster 9:16 y caja 1:1: icono centrado con el nombre debajo.
function New-StoreLogo([int]$w, [int]$h, [string]$file) {
    $c = New-Canvas $w $h $false
    $s = [single]($w * 0.5)
    $top = [single](($h - $s * 1.32) / 2)
    Draw-Icon $c.G (($w - $s) / 2) $top $s
    Draw-Name $c.G $w ($top + $s * 1.06) ([single]($w * 0.085))
    Save-Canvas $c (Join-Path $imgDir $file)
}

New-StoreLogo 720 1080 'poster_720x1080.png'
New-StoreLogo 1440 2160 'poster_1440x2160.png'
New-StoreLogo 1080 1080 'box_1080x1080.png'
New-StoreLogo 2160 2160 'box_2160x2160.png'
foreach ($t in 300, 150, 71) {
    $c = New-Canvas $t $t $true
    Draw-Icon $c.G 0 0 $t
    Save-Canvas $c (Join-Path $imgDir "icon_$t.png")
}

# Capturas: ventana centrada sobre el fondo, con sombra, en 1920x1080.
if (Test-Path $rawDir) {
    foreach ($f in Get-ChildItem $rawDir -Filter *.png) {
        $shot = [Drawing.Image]::FromFile($f.FullName)
        $c = New-Canvas 1920 1080 $false
        $scale = [Math]::Min(1.0, 980.0 / $shot.Height)
        $sw = [single]($shot.Width * $scale); $sh = [single]($shot.Height * $scale)
        $x = [single]((1920 - $sw) / 2); $y = [single]((1080 - $sh) / 2)
        for ($i = 24; $i -ge 1; $i -= 2) {
            $p = New-RoundedPath ($x - $i) ($y - $i + 10) ($sw + 2 * $i) ($sh + 2 * $i) (12 + $i)
            $c.G.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(9, 0, 0, 0))), $p)
        }
        $clip = New-RoundedPath $x $y $sw $sh 10
        $c.G.SetClip($clip)
        $c.G.DrawImage($shot, $x, $y, $sw, $sh)
        $c.G.ResetClip()
        $shot.Dispose()
        Save-Canvas $c (Join-Path $shotDir $f.Name)
    }
}
"Imagenes en $imgDir y $shotDir"
