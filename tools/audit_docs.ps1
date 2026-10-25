<#
.SYNOPSIS
  Audit docs\ structure: top-level whitelist, README presence, orphan detection.

.DESCRIPTION
  Reports (does NOT yet gate) three classes of docs/ structural drift:
    1. TOP      - a directory / root-level .md under docs\ not in the whitelist
    2. NOREADME - a directory containing .md files but lacking README.md
    3. ORPHAN   - a .md file never named by any README.md under docs\

  Phase B2 semantics: REPORT ONLY. Always exits 0 unless the script itself
  failed (2 = docs\ missing). Wiring into tools\run_gates.ps1 as a
  fail-closed gate is phase B6, deliberately NOT done here.

  Encoding (AGENTS.md R1-R5):
    - Script source is pure ASCII. All Chinese comes out of files, never out
      of this script's literals.
    - Every file is read/written through an explicit UTF-8 decoder/encoder.
      Never Get-Content / Set-Content round-trips.
    - Console output is forced to UTF-8 so Chinese path names survive
      (the default console code page on this host is 936, which turned
      every path into mojibake on first run).

.PARAMETER Root
  Repository root. Defaults to the parent directory of this script.

.PARAMETER ReportPath
  Explicit report file. Defaults to
  artifacts\logs\<yyyyMMdd>\audit_docs-<HHmmss>.log

.PARAMETER Top
  Max detail lines printed to the console per category. The report file
  always gets the complete list. Default 40, use 0 for no cap.

.PARAMETER Quiet
  Write the report file only; print just the one-line summary.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit_docs.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit_docs.ps1 -Top 0
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$ReportPath = '',
    [int]$Top = 40,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# --- force UTF-8 console so Chinese paths are readable ---------------------
try {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [Console]::OutputEncoding = $utf8NoBom
    $OutputEncoding = $utf8NoBom
} catch {
    # Non-interactive host: silently keep whatever encoding is in effect.
}

function Write-Utf8File {
    param([string]$Path, [string[]]$Lines)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $text = ($Lines -join "`r`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$Root = [IO.Path]::GetFullPath($Root)

$docsRoot = Join-Path $Root 'docs'
if (-not (Test-Path -LiteralPath $docsRoot)) {
    Write-Host ('docs directory not found: ' + $docsRoot)
    exit 2
}

function Get-RelPath {
    param([string]$Full)
    return (($Full.Substring($Root.Length).TrimStart('\', '/')) -replace '\\', '/')
}

# --- whitelist ------------------------------------------------------------
$layoutPath = Join-Path $PSScriptRoot 'docs-layout.json'
$allowedTop = New-Object 'System.Collections.Generic.HashSet[string]'
$allowedRootFiles = New-Object 'System.Collections.Generic.HashSet[string]'
$layoutState = 'MISSING (no tools\docs-layout.json -> whitelist checks skipped)'
if (Test-Path -LiteralPath $layoutPath) {
    $layout = ([IO.File]::ReadAllText($layoutPath, [Text.Encoding]::UTF8)) | ConvertFrom-Json
    foreach ($d in $layout.topDirs) { [void]$allowedTop.Add([string]$d) }
    if ($layout.PSObject.Properties.Name -contains 'rootFiles') {
        foreach ($f in $layout.rootFiles) { [void]$allowedRootFiles.Add([string]$f) }
    }
    $layoutState = 'OK (' + $allowedTop.Count + ' topDirs / ' + $allowedRootFiles.Count + ' rootFiles)'
}

# --- journal policy (must be defined before first use) --------------------
# Two kinds of .md live under docs\:
#   REFERENCE - design / architecture / guide docs; must be reachable from
#               some README.md, otherwise nobody will ever find them.
#   JOURNAL   - dated change records, meeting minutes, perf runs; indexed by
#               date, not linked from a README. Counting these as orphans made
#               the first run report ORPHAN=386 (392 code-changes entries
#               alone) which is never actionable and buries the real signal.
$journalDirs = @()
$journalPatterns = @()
if (Test-Path -LiteralPath $layoutPath) {
    if ($layout.PSObject.Properties.Name -contains 'orphanPolicy') {
        $op = $layout.orphanPolicy
        if ($op.PSObject.Properties.Name -contains 'journalDirs') { $journalDirs = @($op.journalDirs) }
        if ($op.PSObject.Properties.Name -contains 'journalFilePatterns') { $journalPatterns = @($op.journalFilePatterns) }
    }
}

function Test-JournalDoc {
    param([string]$RelPath)
    foreach ($jd in $journalDirs) {
        $prefix = 'docs/' + $jd
        if ($RelPath -eq $prefix -or $RelPath.StartsWith($prefix + '/')) { return $true }
    }
    $base = $RelPath.Substring($RelPath.LastIndexOf('/') + 1)
    foreach ($p in $journalPatterns) {
        if ($base -match $p) { return $true }
    }
    return $false
}

# --- collect --------------------------------------------------------------
$topViolations = New-Object System.Collections.Generic.List[string]
$noReadme      = New-Object System.Collections.Generic.List[string]
$orphans       = New-Object System.Collections.Generic.List[string]
$allMd         = New-Object System.Collections.Generic.List[string]

foreach ($f in (Get-ChildItem -LiteralPath $docsRoot -Recurse -File -Filter '*.md')) {
    $allMd.Add((Get-RelPath $f.FullName))
}
$allMd.Sort([StringComparer]::Ordinal)

if ($allowedTop.Count -gt 0) {
    foreach ($d in (Get-ChildItem -LiteralPath $docsRoot -Directory)) {
        if (-not $allowedTop.Contains($d.Name)) {
            $topViolations.Add('dir  docs/' + $d.Name)
        }
    }
    foreach ($f in (Get-ChildItem -LiteralPath $docsRoot -File -Filter '*.md')) {
        if (-not $allowedRootFiles.Contains($f.Name)) {
            $topViolations.Add('file docs/' + $f.Name)
        }
    }
}

# --- second-level whitelist (docs\代码\<domain>\<plugin>) ------------------
# Without this a new plugin doc directory could appear at the second level and
# stay invisible: only the first level of docs\ is compared against topDirs.
# Domain directories themselves are pinned by topDirs through "代码"; here we
# pin what may live ONE level deeper.
$codeRootRel = 'docs/代码'
$codeRootAbs = Join-Path $docsRoot ('代码')
$codeDirsState = 'NOT CONFIGURED (second-level check skipped)'
$codeDomains = @{}
if (Test-Path -LiteralPath $layoutPath) {
    if ($layout.PSObject.Properties.Name -contains 'codeDirs') {
        $cd = $layout.codeDirs
        if ($cd.PSObject.Properties.Name -contains 'domains') {
            foreach ($p in $cd.domains.PSObject.Properties) {
                $codeDomains[$p.Name] = @($p.Value)
            }
            $codeDirsState = 'OK (' + $codeDomains.Count + ' domains / ' + (@($codeDomains.Values | ForEach-Object { $_.Count }) | Measure-Object -Sum).Sum + ' dirs)'
        }
    }
}

if ($codeDomains.Count -gt 0 -and (Test-Path -LiteralPath $codeRootAbs)) {
    foreach ($f in (Get-ChildItem -LiteralPath $codeRootAbs -File -Filter '*.md')) {
        if ($f.Name -ne 'README.md') {
            $topViolations.Add('file ' + $codeRootRel + '/' + $f.Name)
        }
    }
    foreach ($dom in (Get-ChildItem -LiteralPath $codeRootAbs -Directory)) {
        if (-not $codeDomains.ContainsKey($dom.Name)) {
            $topViolations.Add('dir  ' + $codeRootRel + '/' + $dom.Name)
            continue
        }
        $allowed = $codeDomains[$dom.Name]
        foreach ($sub in (Get-ChildItem -LiteralPath $dom.FullName -Directory)) {
            if ($allowed -notcontains $sub.Name) {
                $topViolations.Add('dir  ' + $codeRootRel + '/' + $dom.Name + '/' + $sub.Name)
            }
        }
        foreach ($f in (Get-ChildItem -LiteralPath $dom.FullName -File -Filter '*.md')) {
            if ($f.Name -ne 'README.md') {
                $topViolations.Add('file ' + $codeRootRel + '/' + $dom.Name + '/' + $f.Name)
            }
        }
    }
}

# which directories have a README?
$dirsWithMd = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($m in $allMd) {
    $i = $m.LastIndexOf('/')
    if ($i -gt 0) {
        $dir = $m.Substring(0, $i)
        while ($dir.Length -ge 4 -and $dir -ne 'docs') {
            [void]$dirsWithMd.Add($dir)
            $j = $dir.LastIndexOf('/')
            if ($j -lt 0) { break }
            $dir = $dir.Substring(0, $j)
        }
        if ($dir -eq 'docs') { [void]$dirsWithMd.Add('docs') }
    }
}
foreach ($dir in $dirsWithMd) {
    if ($dir -eq 'docs') { continue }
    $readme = Join-Path (Join-Path $Root ($dir -replace '/', '\')) 'README.md'
    if (Test-Path -LiteralPath $readme) { continue }
    # Journal dirs are exempt: a README is a human-written map; for dated
    # entry lists the right artifact is a generated index (phase B4).
    if (Test-JournalDoc $dir) { continue }
    $noReadme.Add($dir)
}
$noReadme.Sort([StringComparer]::Ordinal)

# --- orphan scan: filename must appear somewhere in a docs README ---------
$blob = New-Object System.Text.StringBuilder
foreach ($r in (Get-ChildItem -LiteralPath $docsRoot -Recurse -File -Filter 'README.md')) {
    [void]$blob.AppendLine([IO.File]::ReadAllText($r.FullName, [Text.Encoding]::UTF8))
}
$readmeBlob = $blob.ToString()

$journalSkipped = 0
foreach ($m in $allMd) {
    $base = $m.Substring($m.LastIndexOf('/') + 1)
    if ($base -eq 'README.md') { continue }
    if (Test-JournalDoc $m) { $journalSkipped++; continue }
    if ($readmeBlob.IndexOf($base, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        $orphans.Add($m)
    }
}

# --- broken relative links (added 2026-10-24, docs reorg round 3) ----------
# Why a gate and not a one-off scan: moving a directory rewrites relative
# links, and nothing else notices. After the 2026-10-24 reorg the first scan
# found 300 broken targets; a re-run silently re-broke them every time a
# subdirectory moved. Two categories are deliberately NOT reported:
#   - targets reached through a "#fragment" (anchors are not files)
#   - targets containing a space (regex scraped half of a prose sentence)
# Journal files (code-changes etc.) are in scope: they link to reference docs.
$linkPat = [regex]'\]\(([^)\s]+)\)'
$linkBroken = New-Object System.Collections.Generic.List[string]
$linkChecked = 0
foreach ($m in $allMd) {
    $srcAbs = Join-Path $Root ($m -replace '/', '\\')
    $srcDir = Split-Path -Parent $srcAbs
    $text = [IO.File]::ReadAllText($srcAbs, [Text.Encoding]::UTF8)
    # Blank out fenced code blocks first: a "](x.md)" inside ``` is an EXAMPLE,
    # not a link (2026-10-24: two such examples in the PowerShell encoding log
    # taught the tooling how to rewrite links and are meant to stay as-is).
    $scan = [regex]::Replace($text, '(?s)```.*?```', '')
    foreach ($mm in $linkPat.Matches($scan)) {
        $t = $mm.Groups[1].Value
        if ($t.StartsWith('http://') -or $t.StartsWith('https://') -or $t.StartsWith('mailto:') -or $t.StartsWith('#')) { continue }
        if ($t.StartsWith('file:')) { continue }
        # Images are exempt: the repo keeps docs prose text-only (YAML front
        # matter and inline examples reference assets that live in Help\ or
        # nowhere at all), and a missing inline image never breaks navigation.
        # Only link targets are gated.
        if ($t -match '\.(png|jpg|jpeg|gif|svg|webp|bmp|ico)$') { continue }
        if ($t.Contains('#')) { continue }
        if ($t.Contains(' ')) { continue }
        $linkChecked++
        $cand = $t
        if ($cand.StartsWith('/')) { $cand = $cand.Substring(1) } else { $cand = Join-Path $srcDir $cand }
        $cand = $cand -replace '/', '\\'
        if (-not (Test-Path -LiteralPath $cand)) {
            $linkBroken.Add($m + ' -> ' + $t)
        }
    }
}

# --- build report ---------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd'
$clock = Get-Date -Format 'HHmmss'
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $Root ('artifacts\logs\' + $stamp + '\audit_docs-' + $clock + '.log')
}

$out = New-Object System.Collections.Generic.List[string]
$out.Add('=== docs structure audit (REPORT ONLY, phase B2) ===')
$out.Add('when        : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$out.Add('root        : ' + $Root)
$out.Add('docs        : ' + $docsRoot)
$out.Add('whitelist   : ' + $layoutState)
$out.Add('codeDirs    : ' + $codeDirsState)
$out.Add('md total    : ' + $allMd.Count)
$out.Add('')

function Add-Section {
    param([string]$Title, $Items, [string]$Hint)
    $out.Add('[' + $Title + '] ' + $Items.Count)
    if ($Items.Count -gt 0 -and $Hint) { $out.Add('    (' + $Hint + ')') }
    foreach ($i in $Items) { $out.Add('    ' + $i) }
    $out.Add('')
}

$out.Add('[TOP] directories / root files not in whitelist: ' + $topViolations.Count)
$out.Add('    (policy: register in tools\docs-layout.json, or move the item)')
foreach ($v in $topViolations) { $out.Add('    ' + $v) }
$out.Add('')
$out.Add('[NOREADME] reference directories with .md but no README.md: ' + $noReadme.Count)
$out.Add('    (policy: human-written map expected; journal dirs exempt)')
foreach ($v in $noReadme) { $out.Add('    ' + $v) }
$out.Add('')
$out.Add('[ORPHAN] reference .md never named by any docs\**\README.md: ' + $orphans.Count)
$out.Add('    (policy: journal docs exempt - ' + $journalSkipped + ' skipped by orphanPolicy)')
foreach ($v in $orphans) { $out.Add('    ' + $v) }
$out.Add('')
$out.Add('[LINK] relative markdown links whose target file does not exist: ' + $linkBroken.Count)
$out.Add('    (policy: fix the path; fragments, spaces and absolute URLs are not reported. checked=' + $linkChecked + ')')
foreach ($v in $linkBroken) { $out.Add('    ' + $v) }
$out.Add('')
$out.Add('--- summary ---')
$out.Add('TOP=' + $topViolations.Count + ' NOREADME=' + $noReadme.Count + ' ORPHAN=' + $orphans.Count + ' LINK=' + $linkBroken.Count + ' MD=' + $allMd.Count + ' JOURNAL_SKIPPED=' + $journalSkipped)

$reportOk = $true
try {
    Write-Utf8File -Path $ReportPath -Lines $out.ToArray()
} catch {
    $reportOk = $false
    Write-Host ('WARN report write failed: ' + $_.Exception.Message)
}

# --- console --------------------------------------------------------------
$summary = 'TOP=' + $topViolations.Count + ' NOREADME=' + $noReadme.Count + ' ORPHAN=' + $orphans.Count + ' LINK=' + $linkBroken.Count + ' MD=' + $allMd.Count + ' JOURNAL_SKIPPED=' + $journalSkipped
Write-Host $summary
if ($reportOk) { Write-Host ('report : ' + $ReportPath) } else { Write-Host 'report : NOT WRITTEN' }

if (-not $Quiet) {
    $printed = 0
    $capped = $false
    foreach ($line in $out) {
        if ($line -match '^\s{4}') {
            if ($Top -gt 0 -and $printed -ge $Top) {
                if (-not $capped) {
                    Write-Host ('    ... (detail capped at ' + $Top + ' lines, full list in report)')
                    $capped = $true
                }
                continue
            }
            $printed++
        }
        if ($line -match '^\[' -or $line -match '^\s{4}') {
            Write-Host $line
        }
    }
}

exit 0
