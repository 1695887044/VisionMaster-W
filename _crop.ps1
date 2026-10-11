# ASCII-only helper: generic crop + nearest-neighbour zoom.
# usage: powershell -File _crop.ps1 src out x y w h zoom
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = $args[0]; $out = $args[1]
[int]$x = $args[2]; [int]$y = $args[3]; [int]$w = $args[4]; [int]$h = $args[5]; [int]$zoom = $args[6]
$img = [System.Drawing.Image]::FromFile($src)
$x2 = [Math]::Max(0, [Math]::Min($x, $img.Width - 1))
$y2 = [Math]::Max(0, [Math]::Min($y, $img.Height - 1))
$w2 = [Math]::Min($w, $img.Width - $x2)
$h2 = [Math]::Min($h, $img.Height - $y2)
$bmp = New-Object System.Drawing.Bitmap (($w2 * $zoom), ($h2 * $zoom))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, ($w2 * $zoom), ($h2 * $zoom)), (New-Object System.Drawing.Rectangle $x2, $y2, $w2, $h2), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose(); $img.Dispose()
Write-Output ("saved {0}  src={1}x{2} crop={3},{4} {5}x{6} zoom=x{7}" -f $out, $img.Width, $img.Height, $x2, $y2, $w2, $h2, $zoom)
