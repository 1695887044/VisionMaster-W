# Batch-convert the control topic pages listed in chm_controls.txt.
# Input file: one "name|htmlfile" per line (UTF-8).
param(
    [Parameter(Mandatory = $true)][string]$Base,
    [Parameter(Mandatory = $true)][string]$List,
    [Parameter(Mandatory = $true)][string]$OutDir
)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$enc = [Text.Encoding]::UTF8
$lines = [IO.File]::ReadAllLines($List, $enc)
$conv = 'D:\C#\VM\tools\oneoff\chm_html2text.ps1'
$n = 0
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line.Split('|')
    if ($parts.Count -lt 2) { continue }
    $html = Join-Path $Base ($parts[1].Trim() + '.html')
    if (-not (Test-Path $html)) { Write-Output ('MISSING ' + $html); continue }
    $out = Join-Path $OutDir ($parts[1].Trim() + '.txt')
    & $conv -In $html -Out $out | Out-Null
    $n++
}
Write-Output ('CONVERTED=' + $n)
