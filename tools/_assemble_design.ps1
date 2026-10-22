$ErrorActionPreference = 'Stop'
# R1/R2: this script is pure ASCII. All Chinese text comes from UTF-8 manifests.
$root = 'D:\C#\VM\docs\' +
        [string][char]0x0055 + [string][char]0x0049 +
        [string][char]0x8BBE + [string][char]0x8BA1 +
        [string][char]0x57FA + [string][char]0x7EBF
$leaf = [string][char]0x5269 + [string][char]0x4F59 + [string][char]0x89C6 + [string][char]0x56FE +
        [string][char]0x8BBE + [string][char]0x8BA1 + [string][char]0x7A3F + '-2026-10-22'
$dir = Join-Path $root $leaf
$partsDir = Join-Path $dir 'parts'

$groupsPath = Join-Path $dir '_groups.txt'
$groups = [System.IO.File]::ReadAllLines($groupsPath, [System.Text.Encoding]::UTF8) | Where-Object { $_.Trim().Length -gt 0 }

$shellPath = Join-Path $dir '_shell.html'
if (-not (Test-Path -LiteralPath $shellPath)) { throw "missing shell: $shellPath" }
$shell = [System.IO.File]::ReadAllText($shellPath, [System.Text.Encoding]::UTF8)

$files = Get-ChildItem -LiteralPath $partsDir -Filter '*.html' -File | Sort-Object Name
if ($files.Count -eq 0) { throw "no parts in $partsDir" }

$toc = New-Object System.Text.StringBuilder
$body = New-Object System.Text.StringBuilder
$lastGroup = -1

foreach ($f in $files) {
    $html = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)

    $mId = [regex]::Match($html, '<section[^>]*id="([^"]+)"')
    $mH2 = [regex]::Match($html, '<h2>([^<]+)</h2>')
    $num = [regex]::Match($f.Name, '^(\d+)').Groups[1].Value

    $n = 0
    [void][int]::TryParse($num, [ref]$n)
    $gi = 0
    if ($n -le 12) { $gi = 0 }
    elseif ($n -le 20) { $gi = 1 }
    elseif ($n -le 30) { $gi = 2 }
    else { $gi = 3 }
    if ($gi -ne $lastGroup) {
        $lastGroup = $gi
        [void]$toc.AppendLine('  <div class="grp">' + $groups[$gi] + '</div>')
    }

    $title = if ($mH2.Success) { $mH2.Groups[1].Value } else { $f.BaseName }
    $anchor = if ($mId.Success) { $mId.Groups[1].Value } else { 'view-' + $num }
    [void]$toc.AppendLine('  <a href="#' + $anchor + '"><span class="n">' + $num + '</span>' + $title + '</a>')

    [void]$body.AppendLine($html.Trim())
    [void]$body.AppendLine()
}

$out = $shell.Replace('<!--{{TOC}}-->', $toc.ToString().TrimEnd()).Replace('<!--{{PARTS}}-->', $body.ToString().TrimEnd())
$outPath = Join-Path $dir 'design.html'
[System.IO.File]::WriteAllText($outPath, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("assembled " + $files.Count + " parts -> " + $outPath)
Write-Output ("size = " + (Get-Item -LiteralPath $outPath).Length + " bytes")
