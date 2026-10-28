# One-off: dump a vertical pixel profile from a PNG (pick ROI + expected color sequence).
# ASCII-only source per R1. Args: <image> <x> <y0> <y1> <step>
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$img = $args[0]; $x = [int]$args[1]; $y0 = [int]$args[2]; $y1 = [int]$args[3]; $step = [int]$args[4]
$bmp = [System.Drawing.Bitmap]::FromFile($img)
Write-Output ("size=" + $bmp.Width + "x" + $bmp.Height + "  x=" + $x)
for ($y = $y0; $y -le $y1; $y += $step) {
    $c = $bmp.GetPixel($x, $y)
    $mx = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))
    $mn = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
    $d = $mx - $mn
    $s = if ($mx -le 0) { 0 } else { [Math]::Round($d / $mx, 2) }
    Write-Output ("y=" + $y + "  R=" + $c.R + " G=" + $c.G + " B=" + $c.B + "  s=" + $s)
}
$bmp.Dispose()
