# One-off: approximate HALCON sobel_amp('sum_abs') mean on a grayscale image.
# Used only to pick a sensible FocusRef/FocusMin for the quality-gate sample flow.
# ASCII-only source per R1. Args: <image>
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$bmp = [System.Drawing.Bitmap]::FromFile($args[0])
$w = $bmp.Width; $h = $bmp.Height
$gray = New-Object 'int[,]' $w, $h
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $gray[($x), ($y)] = [int](($c.R * 299 + $c.G * 587 + $c.B * 114) / 1000)
    }
}
$sum = 0.0; $n = 0
for ($y = 1; $y -lt $h - 1; $y++) {
    for ($x = 1; $x -lt $w - 1; $x++) {
        $xr = $x + 1; $xl = $x - 1; $yd = $y - 1; $yu = $y + 1
        $gx = ($gray[($xr), ($yd)] + 2 * $gray[($xr), ($y)] + $gray[($xr), ($yu)]) - ($gray[($xl), ($yd)] + 2 * $gray[($xl), ($y)] + $gray[($xl), ($yu)])
        $gy = ($gray[($xl), ($yu)] + 2 * $gray[($x), ($yu)] + $gray[($xr), ($yu)]) - ($gray[($xl), ($yd)] + 2 * $gray[($x), ($yd)] + $gray[($xr), ($yd)])
        $sum += [Math]::Abs($gx) + [Math]::Abs($gy)
        $n++
    }
}
Write-Output ((Split-Path -Leaf $args[0]) + "  size=" + $w + "x" + $h + "  sobel_sum_abs_mean=" + [Math]::Round($sum / $n, 2))
$bmp.Dispose()
