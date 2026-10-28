# One-off: count pixels whose gray difference exceeds a threshold between two same-size images.
# ASCII-only source per R1. Args: <img1> <img2> <threshold>
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$a = [System.Drawing.Bitmap]::FromFile($args[0])
$b = [System.Drawing.Bitmap]::FromFile($args[1])
$thr = [int]$args[2]
Write-Output ("a=" + $a.Width + "x" + $a.Height + "  b=" + $b.Width + "x" + $b.Height)
if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) { Write-Output "SIZE MISMATCH"; exit 2 }

$count = 0
$max = 0
$sum = 0L
for ($y = 0; $y -lt $a.Height; $y++) {
    for ($x = 0; $x -lt $a.Width; $x++) {
        $ca = $a.GetPixel($x, $y); $cb = $b.GetPixel($x, $y)
        $ga = [int](($ca.R * 299 + $ca.G * 587 + $ca.B * 114) / 1000)
        $gb = [int](($cb.R * 299 + $cb.G * 587 + $cb.B * 114) / 1000)
        $d = [Math]::Abs($ga - $gb)
        if ($d -gt $thr) { $count++ }
        if ($d -gt $max) { $max = $d }
        $sum += $d
    }
}
$total = $a.Width * $a.Height
Write-Output ("thr=" + $thr + "  over=" + $count + "  of=" + $total + "  pct=" + [Math]::Round(100.0 * $count / $total, 2) + "  max=" + $max + "  mean=" + [Math]::Round($sum / $total, 2))
$a.Dispose(); $b.Dispose()
