# ASCII-only helper: crop + upscale regions of the user's screenshot for close inspection.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = 'C:\Users\16958\.zcode\cli\image-cache\sess_40cea40c-f0c1-4ca4-93eb-cfd6886b4ac6\image-d67158034f0106e4b4b01923c9de68d0.png'
$outDir = 'D:\C#\VM\_BeadViewProbe'

$img = [System.Drawing.Image]::FromFile($src)
Write-Output ("source size = {0} x {1}" -f $img.Width, $img.Height)

function Crop-Zoom {
    param([string]$Name, [int]$X, [int]$Y, [int]$W, [int]$H, [int]$Zoom)
    $x2 = [Math]::Min($X, $img.Width - 1)
    $y2 = [Math]::Min($Y, $img.Height - 1)
    $w2 = [Math]::Min($W, $img.Width - $x2)
    $h2 = [Math]::Min($H, $img.Height - $y2)
    $bmp = New-Object System.Drawing.Bitmap (($w2 * $Zoom), ($h2 * $Zoom))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, ($w2 * $Zoom), ($h2 * $Zoom)), (New-Object System.Drawing.Rectangle $x2, $y2, $w2, $h2), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $p = Join-Path $outDir $Name
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("saved {0}  (crop {1},{2} {3}x{4} zoom x{5})" -f $p, $x2, $y2, $w2, $h2, $Zoom)
}

# point list card (left column, bottom) + ref path row + full left/right columns
Crop-Zoom 'user_zoom_points.png' 110 880 660 400 3
Crop-Zoom 'user_zoom_load.png'   90 590 680 190 3
Crop-Zoom 'user_zoom_left.png'   60 240 700 1120 1
$img.Dispose()
