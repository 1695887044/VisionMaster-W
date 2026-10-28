# One-off: bake HMI navigation icons (64x64 white line art, transparent) and a 1920x1080
# dark tech background into the solution Image folder. ASCII only (R1); Chinese target
# path comes from a UTF-8 manifest (R2).
Add-Type -AssemblyName System.Drawing

$paths = [IO.File]::ReadAllLines('D:\C#\VM\tools\oneoff\asset_paths.txt', [Text.Encoding]::UTF8)
$outDir = $paths[0]
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$SIZE = 64
$WHITE = [System.Drawing.Color]::FromArgb(255, 55, 65, 81)   # #FF374151 dark slate for light backgrounds

function New-IconCanvas {
    $bmp = New-Object System.Drawing.Bitmap($SIZE, $SIZE)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $pen = New-Object System.Drawing.Pen($WHITE, 4.0)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return @{ Bmp = $bmp; G = $g; Pen = $pen }
}

function Save-Icon($c, [string]$name) {
    $c.Pen.Dispose()
    $c.G.Dispose()
    $target = Join-Path $outDir ('icon_' + $name + '.png')
    $c.Bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.Bmp.Dispose()
    Write-Output ('  icon: ' + $name)
}

# ---- home ----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawLine($p, 10, 30, 32, 12)
$g.DrawLine($p, 32, 12, 54, 30)
$g.DrawLine($p, 15, 30, 15, 52)
$g.DrawLine($p, 15, 52, 49, 52)
$g.DrawLine($p, 49, 52, 49, 30)
$g.DrawRectangle($p, 27, 38, 10, 14)
Save-Icon $c 'home'

# ---- run（循环箭头）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawArc($p, 12, 12, 40, 40, 45, 275)
$g.DrawLine($p, 44, 12, 52, 22)
$g.DrawLine($p, 52, 22, 40, 25)
Save-Icon $c 'run'

# ---- manual（手）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawArc($p, 18, 26, 28, 26, 180, 180)
$g.DrawLine($p, 18, 39, 18, 26)
$g.DrawLine($p, 46, 39, 46, 26)
$g.DrawLine($p, 24, 26, 24, 12)
$g.DrawLine($p, 32, 26, 32, 8)
$g.DrawLine($p, 40, 26, 40, 12)
$g.DrawLine($p, 18, 32, 12, 26)
Save-Icon $c 'manual'

# ---- param（齿轮）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawEllipse($p, 22, 22, 20, 20)
for ($i = 0; $i -lt 8; $i++) {
    $ang = $i * [Math]::PI / 4.0
    $x1 = [float](32 + [Math]::Cos($ang) * 14)
    $y1 = [float](32 + [Math]::Sin($ang) * 14)
    $x2 = [float](32 + [Math]::Cos($ang) * 24)
    $y2 = [float](32 + [Math]::Sin($ang) * 24)
    $g.DrawLine($p, $x1, $y1, $x2, $y2)
}
Save-Icon $c 'param'

# ---- monitor（波形）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawRectangle($p, 8, 14, 48, 36)
$g.DrawLine($p, 13, 38, 22, 38)
$g.DrawLine($p, 22, 38, 27, 22)
$g.DrawLine($p, 27, 22, 33, 44)
$g.DrawLine($p, 33, 44, 39, 30)
$g.DrawLine($p, 39, 30, 51, 30)
Save-Icon $c 'monitor'

# ---- chart（柱状）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawLine($p, 10, 52, 54, 52)
$g.DrawRectangle($p, 16, 34, 8, 18)
$g.DrawRectangle($p, 28, 22, 8, 30)
$g.DrawRectangle($p, 40, 28, 8, 24)
Save-Icon $c 'chart'

# ---- log（文档）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawRectangle($p, 14, 10, 36, 44)
$g.DrawLine($p, 22, 22, 42, 22)
$g.DrawLine($p, 22, 32, 42, 32)
$g.DrawLine($p, 22, 42, 36, 42)
Save-Icon $c 'log'

# ---- alarm（三角感叹号）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawLine($p, 32, 10, 54, 50)
$g.DrawLine($p, 54, 50, 10, 50)
$g.DrawLine($p, 10, 50, 32, 10)
$g.DrawLine($p, 32, 24, 32, 36)
$g.DrawEllipse($p, 30, 40, 4, 4)
Save-Icon $c 'alarm'

# ---- back（左箭头）----
$c = New-IconCanvas; $g = $c.G; $p = $c.Pen
$g.DrawLine($p, 50, 32, 16, 32)
$g.DrawLine($p, 16, 32, 28, 20)
$g.DrawLine($p, 16, 32, 28, 44)
Save-Icon $c 'back'

# ==================================================================
#  背景图 1920x1080
# ==================================================================
$W = 1920
$H = 1080
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

$rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
# Light "web dashboard" canvas: near-white base with two very soft tints (no grid).
$colTop = [System.Drawing.Color]::FromArgb(255, 250, 251, 253)
$colMid = [System.Drawing.Color]::FromArgb(255, 247, 248, 250)
$colBot = [System.Drawing.Color]::FromArgb(255, 243, 245, 249)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $colTop, $colBot, 90.0)
$blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
$blend.Colors = @($colTop, $colMid, $colBot)
$blend.Positions = @(0.0, 0.55, 1.0)
$brush.InterpolationColors = $blend
$g.FillRectangle($brush, $rect)
$brush.Dispose()

$glowPath1 = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath1.AddEllipse(1080, -420, 1100, 1100)
$gb1 = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath1)
$gb1.CenterColor = [System.Drawing.Color]::FromArgb(26, 37, 99, 235)
$gb1.SurroundColors = @([System.Drawing.Color]::Transparent)
$g.FillEllipse($gb1, 1080, -420, 1100, 1100)
$gb1.Dispose()
$glowPath1.Dispose()

$glowPath2 = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath2.AddEllipse(-420, 560, 1180, 1180)
$gb2 = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath2)
$gb2.CenterColor = [System.Drawing.Color]::FromArgb(20, 124, 58, 237)
$gb2.SurroundColors = @([System.Drawing.Color]::Transparent)
$g.FillEllipse($gb2, -420, 560, 1180, 1180)
$gb2.Dispose()
$glowPath2.Dispose()

$g.Dispose()
$bgPath = Join-Path $outDir 'bg_main.png'
$bmp.Save($bgPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ('  background: ' + $bgPath)
Write-Output 'DONE'
