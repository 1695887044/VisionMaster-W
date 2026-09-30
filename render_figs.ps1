$ErrorActionPreference = 'Continue'
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$svgDir = 'D:\C#\VM\图解'
$srcDir = Join-Path $env:TEMP 'servo_svg'
$outDir = Join-Path $env:TEMP 'servo_figs'
New-Item -ItemType Directory -Force -Path $srcDir, $outDir | Out-Null
Remove-Item (Join-Path $outDir '*.png') -ErrorAction SilentlyContinue

$svgs = Get-ChildItem (Join-Path $svgDir '*.svg') | Sort-Object Name
$i = 0
foreach ($s in $svgs) {
  $i++
  $src = "{0}\fig{1:d2}.svg" -f $srcDir, $i
  $png = "{0}\fig{1:d2}.png" -f $outDir, $i
  Copy-Item $s.FullName $src -Force
  $url = 'file:///' + ($src -replace '\\', '/')
  & $edge --headless --disable-gpu --no-first-run --hide-scrollbars `
    --screenshot=$png --window-size=920,540 `
    --default-background-color=FFFFFFFF --virtual-time-budget=4000 `
    $url 2>$null | Out-Null
  if (Test-Path $png) { Write-Output ("OK  fig{0:d2}.png  {1} bytes" -f $i, (Get-Item $png).Length) }
  else { Write-Output ("FAIL fig{0:d2}  ({1})" -f $i, $s.Name) }
}
Write-Output '--- copy back with Chinese names ---'
$j = 0
foreach ($s in $svgs) {
  $j++
  $png = "{0}\fig{1:d2}.png" -f $outDir, $j
  $dst = Join-Path $svgDir ('png\' + $s.BaseName + '.png')
  if (Test-Path $png) { Copy-Item $png $dst -Force }
}
Get-ChildItem (Join-Path $svgDir 'png') | Select-Object Name, Length | Format-Table -AutoSize
