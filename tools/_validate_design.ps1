$ErrorActionPreference = 'Stop'
# R1/R2: pure ASCII; Chinese path from code points.
$root = 'D:\C#\VM\docs\' +
        [string][char]0x0055 + [string][char]0x0049 +
        [string][char]0x8BBE + [string][char]0x8BA1 +
        [string][char]0x57FA + [string][char]0x7EBF
$leaf = [string][char]0x5269 + [string][char]0x4F59 + [string][char]0x89C6 + [string][char]0x56FE +
        [string][char]0x8BBE + [string][char]0x8BA1 + [string][char]0x7A3F + '-2026-10-22'
$dir = Join-Path $root $leaf
$partsDir = Join-Path $dir 'parts'

$problems = 0
$files = Get-ChildItem -LiteralPath $partsDir -Filter '*.html' -File | Sort-Object Name

foreach ($f in $files) {
    $t = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $issues = @()

    # exactly one section with a view id
    $sec = ([regex]::Matches($t, '<section class="view"')).Count
    if ($sec -ne 1) { $issues += "sections=$sec" }

    # forbidden constructs
    if ($t -match '<style') { $issues += 'has <style>' }
    if ($t -match '<script') { $issues += 'has <script>' }
    if ($t -match '<img') { $issues += 'has <img>' }
    if ($t -match '<link') { $issues += 'has <link>' }
    if ($t -match 'url\(') { $issues += 'has url()' }
    if ($t -match '@import') { $issues += 'has @import' }
    if ($t -match 'https?://') { $issues += 'has http url' }

    # Hardcoded colours that actually paint something: inside <style> blocks or style="..."
    # attributes. Hex that appears as CONTENT (a property value like a colour swatch, a frame
    # number like "#0412", or the notes' replacement checklist) is data, not styling.
    $styleBlocks = [regex]::Matches($t, '<style[^>]*>([\s\S]*?)</style>') | ForEach-Object { $_.Groups[1].Value }
    $styleAttrs = [regex]::Matches($t, 'style="([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
    $paint = ($styleBlocks -join "`n") + "`n" + ($styleAttrs -join "`n")
    $hex = [regex]::Matches($paint, '#[0-9A-Fa-f]{6,8}')
    if ($hex.Count -gt 0) { $issues += ("hex-in-style=" + $hex.Count + " (" + (($hex | ForEach-Object { $_.Value }) -join ',') + ")") }
    $rgb = [regex]::Matches($paint, 'rgba?\(')
    if ($rgb.Count -gt 0) { $issues += ("rgb()-in-style=" + $rgb.Count) }
    if ($styleBlocks.Count -gt 0) { $issues += ("style-blocks=" + $styleBlocks.Count) }

    # notes block required
    if ($t -notmatch 'class="notes"') { $issues += 'no .notes' }

    # tag balance for div / section
    foreach ($tag in @('div', 'section')) {
        $o = ([regex]::Matches($t, '<' + $tag + '[\s>]')).Count
        $c = ([regex]::Matches($t, '</' + $tag + '>')).Count
        if ($o -ne $c) { $issues += ($tag + ' ' + $o + '/' + $c) }
    }

    if ($issues.Count -gt 0) {
        $problems++
        Write-Output ($f.Name + ' -> ' + ($issues -join '; '))
    }
}

Write-Output ('---- checked ' + $files.Count + ' parts, problems = ' + $problems)

# assembled page: exactly one <style>, 33 sections
$designPath = Join-Path $dir 'design.html'
if (Test-Path -LiteralPath $designPath) {
    $d = [System.IO.File]::ReadAllText($designPath, [System.Text.Encoding]::UTF8)
    Write-Output ('assembled: sections=' + ([regex]::Matches($d, '<section class="view"')).Count +
                  ' style=' + ([regex]::Matches($d, '<style')).Count +
                  ' tocLinks=' + ([regex]::Matches($d, '<a href="#view-')).Count)
}

if ($problems -gt 0) { exit 1 }
exit 0
