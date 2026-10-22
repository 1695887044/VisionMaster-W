$ErrorActionPreference = 'Stop'
# R1/R2: pure ASCII. Chinese folder name from code points; all Chinese text lives in _fixes.json.
$root = 'D:\C#\VM\docs\' +
        [string][char]0x0055 + [string][char]0x0049 +
        [string][char]0x8BBE + [string][char]0x8BA1 +
        [string][char]0x57FA + [string][char]0x7EBF
$leaf = [string][char]0x5269 + [string][char]0x4F59 + [string][char]0x89C6 + [string][char]0x56FE +
        [string][char]0x8BBE + [string][char]0x8BA1 + [string][char]0x7A3F + '-2026-10-22'
$dir = Join-Path $root $leaf
$partsDir = Join-Path $dir 'parts'
$manifest = Join-Path $dir '_fixes.json'

$json = [System.IO.File]::ReadAllText($manifest, [System.Text.Encoding]::UTF8)
$spec = $json | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding($false)

$applied = 0
$skipped = 0

# group replacements by file so each file is read/written once
$byFile = @{}
foreach ($r in $spec.replace) {
    if (-not $byFile.ContainsKey($r.file)) { $byFile[$r.file] = New-Object System.Collections.ArrayList }
    [void]$byFile[$r.file].Add($r)
}

foreach ($file in $byFile.Keys) {
    $path = Join-Path $partsDir $file
    if (-not (Test-Path -LiteralPath $path)) { Write-Output "MISSING $file"; $skipped++; continue }
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $before = $text
    foreach ($r in $byFile[$file]) {
        if ($text.Contains($r.from)) {
            $text = $text.Replace($r.from, $r.to)
            $applied++
        }
        else {
            Write-Output ("  no-match in " + $file + " : " + $r.from.Substring(0, [Math]::Min(40, $r.from.Length)))
            $skipped++
        }
    }
    if (-not [object]::ReferenceEquals($text, $before)) {
        [System.IO.File]::WriteAllText($path, $text, $utf8)
    }
}

foreach ($op in $spec.removeLineContaining) {
    $path = Join-Path $partsDir $op.file
    if (-not (Test-Path -LiteralPath $path)) { Write-Output "MISSING $op.file"; continue }
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    $kept = @()
    $removed = 0
    foreach ($line in $lines) {
        $hit = $true
        foreach ($needle in $op.all) {
            if (-not $line.Contains($needle)) { $hit = $false; break }
        }
        if ($hit) { $removed++ } else { $kept += $line }
    }
    [System.IO.File]::WriteAllLines($path, $kept, $utf8)
    Write-Output ("removed " + $removed + " line(s) from " + $op.file)
    $applied += $removed
}

Write-Output ("---- applied=" + $applied + " skipped=" + $skipped)
