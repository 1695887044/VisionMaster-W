$ErrorActionPreference = 'Stop'
# R1/R2: this script is pure ASCII. The Chinese folder name is built from code points
# so the file stays 7-bit clean under Windows PowerShell 5.1 (ANSI) parsing.
$designRoot = 'D:\C#\VM\docs\' +
              [string][char]0x0055 + [string][char]0x0049 +
              [string][char]0x8BBE + [string][char]0x8BA1 +
              [string][char]0x57FA + [string][char]0x7EBF

$colorsPath = 'D:\C#\VM\UI\Controls\Themes\Colors.xaml'
$tokensPath = 'D:\C#\VM\Scada.Controls\Themes\ScadaTokens.xaml'

function Expand-Hex([string]$h) {
    $s = $h.TrimStart('#').ToUpperInvariant()
    if ($s.Length -eq 3) {
        $s = '' + $s[0] + $s[0] + $s[1] + $s[1] + $s[2] + $s[2]
    }
    elseif ($s.Length -eq 4) {
        $s = '' + $s[0] + $s[0] + $s[1] + $s[1] + $s[2] + $s[2] + $s[3] + $s[3]
    }
    return '#' + $s
}

# ---------------------------------------------------------------- token table
$allowed = @{}
foreach ($p in @($colorsPath, $tokensPath)) {
    if (-not (Test-Path -LiteralPath $p)) { continue }
    $lines = [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
    foreach ($line in $lines) {
        $m = [regex]::Match($line, 'x:Key="([^"]+)"[^>]*Color="(#[0-9A-Fa-f]{3,8})"')
        if ($m.Success) {
            $v = Expand-Hex $m.Groups[2].Value
            if (-not $allowed.ContainsKey($v)) { $allowed[$v] = $m.Groups[1].Value }
        }
    }
}

# RGB-only view for rgba() comparison (alpha channel ignored).
$tokenRgb = @{}
foreach ($k in $allowed.Keys) { $tokenRgb[$k.Substring(0, 7)] = $allowed[$k] }

# ---------------------------------------------------------------- scan pages
$report = New-Object System.Collections.ArrayList
$failed = 0

foreach ($page in @('log-console', 'watch-panel', 'flow-list', 'run-cockpit')) {
    $file = Join-Path (Join-Path $designRoot $page) 'design.html'
    if (-not (Test-Path -LiteralPath $file)) {
        [void]$report.Add([pscustomobject]@{ Page = $page; Hex = 0; Chrome = 0; Rgba = 0; Unmapped = 'FILE MISSING'; Status = 'MISSING' })
        $failed++
        continue
    }

    $text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

    # Scan surface: every <style> block plus every inline style="..." attribute.
    # (v1 only read the first <style> block, so inline styles were invisible.)
    $cssParts = New-Object System.Collections.ArrayList
    foreach ($m in [regex]::Matches($text, '<style[^>]*>(.*?)</style>', 'Singleline')) {
        [void]$cssParts.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($text, 'style="([^"]*)"')) {
        [void]$cssParts.Add($m.Groups[1].Value)
    }
    $css = ($cssParts -join "`n")

    # Strip comments so commented-out values are not counted as real colours.
    $cssNoComments = [regex]::Replace($css, '/\*.*?\*/', '', 'Singleline')

    # Hex literals: 3, 4, 6 and 8 digits. (v1 ignored 3/4-digit shorthand.)
    $found = @()
    foreach ($m in [regex]::Matches($cssNoComments, '#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b')) {
        $found += (Expand-Hex $m.Value)
    }
    $found = $found | Sort-Object -Unique

    # rgba()/rgb() triplets: each RGB triple must correspond to a token colour.
    # (v1 did not look at rgba at all.)
    # Collected as (raw text, rgb hex) so the chrome exemption can be applied later.
    $rgbaHits = @()
    $rgbaCount = 0
    foreach ($m in [regex]::Matches($cssNoComments, 'rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})')) {
        $rgbaCount++
        $hex = ('#{0:X2}{1:X2}{2:X2}' -f [int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value)
        $rgbaHits += [pscustomobject]@{ Raw = $m.Value; Hex = $hex }
    }

    # Declared design-canvas chrome: hex values and rgba() triplets listed on a NOT-A-TOKEN
    # line that also contains a colon. Line-scoped, so a bare NOT-A-TOKEN comment cannot
    # swallow the next declaration's colour. (v1 used [^:] which crossed newlines.)
    $chrome = @{}
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match 'NOT-A-TOKEN' -and $line -match ':') {
            foreach ($hm in [regex]::Matches($line, '#[0-9A-Fa-f]{3,8}')) {
                $chrome[(Expand-Hex $hm.Value)] = $true
            }
            foreach ($rm in [regex]::Matches($line, 'rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*[,)]')) {
                $rgb = ('#{0:X2}{1:X2}{2:X2}' -f [int]$rm.Groups[1].Value, [int]$rm.Groups[2].Value, [int]$rm.Groups[3].Value)
                $chrome[$rgb] = $true
            }
        }
    }

    $bad = @()
    foreach ($c in $found) {
        if (-not $allowed.ContainsKey($c) -and -not $chrome.ContainsKey($c)) { $bad += $c }
    }
    # rgba exemption: allowed when the triple matches a token OR was declared as chrome.
    foreach ($hit in $rgbaHits) {
        if (-not $tokenRgb.ContainsKey($hit.Hex) -and -not $chrome.ContainsKey($hit.Hex)) {
            $bad += ('rgba ' + $hit.Raw + ' -> ' + $hit.Hex)
        }
    }
    $bad = $bad | Sort-Object -Unique

    $status = 'OK'
    if ($bad.Count -gt 0) { $status = 'UNMAPPED'; $failed++ }

    [void]$report.Add([pscustomobject]@{
        Page     = $page
        Hex      = $found.Count
        Chrome   = $chrome.Count
        Rgba     = $rgbaCount
        Unmapped = ($bad -join ' | ')
        Status   = $status
    })
}

$report | Format-Table -AutoSize | Out-String -Width 220

if ($failed -gt 0) {
    Write-Output "FAILED: $failed page(s) contain colours that are not in the token dictionaries."
    exit 1
}
Write-Output 'OK: every colour maps to a token in Colors.xaml / ScadaTokens.xaml (or a declared NOT-A-TOKEN chrome value).'
exit 0
