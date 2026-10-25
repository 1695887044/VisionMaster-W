# One-off: composite baseline/WPF screenshot pairs side by side.
# ASCII-only per R1; the pair list (contains CJK paths) is read from a UTF-8 manifest per R2.
param(
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$text = [IO.File]::ReadAllLines($Manifest, [Text.Encoding]::UTF8)
$rows = $text | Where-Object { $_.Trim().Length -gt 0 -and -not $_.StartsWith('#') }

foreach ($row in $rows) {
    $parts = $row -split '\|'
    if ($parts.Count -lt 3) { Write-Output "SKIP malformed: $($parts[0])"; continue }
    $leftPath = $parts[0].Trim()
    $rightPath = $parts[1].Trim()
    $outName = $parts[2].Trim()

    if (-not (Test-Path $leftPath)) { Write-Output "MISS left: $leftPath"; continue }
    if (-not (Test-Path $rightPath)) { Write-Output "MISS right: $rightPath"; continue }

    $left = [System.Drawing.Image]::FromFile($leftPath)
    $right = [System.Drawing.Image]::FromFile($rightPath)

    $headH = 26
    $gap = 12
    $w = $left.Width + $right.Width + $gap
    $h = [Math]::Max($left.Height, $right.Height) + $headH

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)

    $font = New-Object System.Drawing.Font('Segoe UI', 11)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 60, 60, 60))

    $g.DrawString('BASELINE (web)', $font, $brush, 4, 4)
    $g.DrawString('WPF REPLICATION', $font, $brush, ($left.Width + $gap + 4), 4)

    $g.DrawImage($left, 0, $headH, $left.Width, $left.Height)
    $g.DrawImage($right, ($left.Width + $gap), $headH, $right.Width, $right.Height)

    $g.Dispose()
    $outPath = Join-Path $OutDir $outName
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $left.Dispose()
    $right.Dispose()

    Write-Output "OK $outName  ${w}x${h}"
}
