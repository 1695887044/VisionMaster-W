<#
.SYNOPSIS
  Generate INDEX.md for every journal directory under docs\ (phase B4).

.DESCRIPTION
  Journal directories (docs\code-changes, docs\meetings, docs\performance)
  hold hundreds of dated entries. A hand-written README for them rots on the
  next commit, so tools\audit_docs.ps1 exempts them from the README rule and
  the right artifact is a GENERATED index instead.

  This script writes docs\<dir>\INDEX.md:
    - one table per month, newest month first, newest entry first within a
      month (ordinal sort on the file name, which starts with yyyy-MM-dd)
    - first heading line of the entry becomes the description column
    - links are relative (just the file name), so the file is portable

  Idempotent: writes only when the generated text differs from what is on
  disk. Re-run after adding entries.

  Encoding (AGENTS.md R1-R5): source is pure ASCII, every file is read and
  written through an explicit UTF-8 encoder. Never Get-Content / Set-Content.

.PARAMETER Root
  Repository root. Defaults to the parent directory of this script.

.PARAMETER Dir
  One or more journal directory names (relative to docs\). Defaults to the
  readmePolicy.exemptDirs list in tools\docs-layout.json, falling back to
  code-changes, meetings, performance.

.PARAMETER Quiet
  Print only the one-line summary per directory.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\gen_docs_index.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\gen_docs_index.ps1 -Dir code-changes
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string[]]$Dir = @(),
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# --- force UTF-8 console so Chinese paths are readable --------------------
try {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [Console]::OutputEncoding = $utf8NoBom
    $OutputEncoding = $utf8NoBom
} catch {
    # Non-interactive host: keep whatever encoding is in effect.
}

function Read-Utf8All {
    param([string]$Path)
    return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
}

function Write-Utf8IfChanged {
    param([string]$Path, [string]$Text)
    $old = $null
    if (Test-Path -LiteralPath $Path) {
        $old = Read-Utf8All $Path
        if ($old -ceq $Text) { return $false }
    }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
    return $true
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

# --- journal dir list -----------------------------------------------------
$journalDirs = @($Dir)
if ($journalDirs.Count -eq 0) {
    $layoutPath = Join-Path $PSScriptRoot 'docs-layout.json'
    if (Test-Path -LiteralPath $layoutPath) {
        $layout = (Read-Utf8All $layoutPath) | ConvertFrom-Json
        if ($layout.PSObject.Properties.Name -contains 'readmePolicy') {
            $rp = $layout.readmePolicy
            if ($rp.PSObject.Properties.Name -contains 'exemptDirs') {
                $journalDirs = @($rp.exemptDirs)
            }
        }
    }
    if ($journalDirs.Count -eq 0) {
        $journalDirs = @('code-changes', 'meetings', 'performance')
    }
}

# --- the first markdown heading of a file, cleaned for a table cell -------
function Get-EntryTitle {
    param([string]$Path)
    $text = Read-Utf8All $Path
    $title = ''
    $h1 = ''
    $h2 = ''
    foreach ($raw in ($text -split "`r?`n")) {
        $line = $raw.Trim()
        if ($line -eq '') { continue }
        if ($line.StartsWith('### ')) { continue }
        if ($line.StartsWith('## ')) {
            if ($h2 -eq '') { $h2 = $line.TrimStart('#').Trim() }
            break
        }
        if ($line.StartsWith('# ')) {
            if ($h1 -eq '') { $h1 = $line.TrimStart('#').Trim() }
            break
        }
    }
    # Journals usually carry no H1; the first H2 is the real subject. Prefer an
    # H1 when one exists above it, otherwise fall back to the H2.
    if ($h1 -ne '') { $title = $h1 }
    elseif ($h2 -ne '') { $title = $h2 }
    if ($title -eq '') { $title = [IO.Path]::GetFileNameWithoutExtension($Path) }
    # table cell hygiene: pipes break columns, brackets break links
    $title = $title -replace '\|', '/'
    $title = $title -replace '\[', '('
    $title = $title -replace '\]', ')'
    if ($title.Length -gt 90) { $title = $title.Substring(0, 90) + '...' }
    return $title
}

function Get-EntryDate {
    param([string]$Name)
    $m = [regex]::Match($Name, '^(\d{4})-(\d{2})-(\d{2})')
    if ($m.Success) { return ($m.Groups[1].Value + '-' + $m.Groups[2].Value + '-' + $m.Groups[3].Value) }
    return ''
}

$summaryLines = New-Object System.Collections.Generic.List[string]
$grandTotal = 0

foreach ($d in $journalDirs) {
    $full = Join-Path $docsRoot $d
    if (-not (Test-Path -LiteralPath $full)) {
        $summaryLines.Add('SKIP  docs/' + $d + '  (directory missing)')
        continue
    }

    $files = New-Object System.Collections.Generic.List[object]
    foreach ($f in (Get-ChildItem -LiteralPath $full -File -Filter '*.md')) {
        if ($f.Name -eq 'README.md' -or $f.Name -eq 'INDEX.md') { continue }
        $files.Add([pscustomobject]@{
            Name  = $f.Name
            Date  = (Get-EntryDate $f.Name)
            Title = (Get-EntryTitle $f.FullName)
        })
    }
    $files.Sort([System.Comparison[object]]{
        param($a, $b)
        $c = [string]::CompareOrdinal($b.Date, $a.Date)
        if ($c -ne 0) { return $c }
        return [string]::CompareOrdinal($b.Name, $a.Name)
    })

    $total = $files.Count
    $grandTotal += $total

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# ' + $d + ' - entry index')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('> **GENERATED FILE - do not hand-edit.** Regenerate with:')
    [void]$sb.AppendLine('> `powershell -NoProfile -ExecutionPolicy Bypass -File tools\gen_docs_index.ps1`')
    [void]$sb.AppendLine('>')
    [void]$sb.AppendLine('> Source of truth is the directory listing itself. Newest first; the')
    [void]$sb.AppendLine('> description column is the first markdown heading of each entry.')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Entries: **' + $total + '**')
    [void]$sb.AppendLine('')

    if ($total -eq 0) {
        [void]$sb.AppendLine('_(empty)_')
    } else {
        $month = ''
        foreach ($f in $files) {
            $thisMonth = 'undated'
            if ($f.Date -ne '') { $thisMonth = $f.Date.Substring(0, 7) }
            if ($thisMonth -ne $month) {
                if ($month -ne '') { [void]$sb.AppendLine('') }
                $month = $thisMonth
                [void]$sb.AppendLine('## ' + $month)
                [void]$sb.AppendLine('')
                [void]$sb.AppendLine('| Date | Entry |')
                [void]$sb.AppendLine('|---|---|')
            }
            $dateCell = $f.Date
            if ($dateCell -eq '') { $dateCell = '-' }
            [void]$sb.AppendLine('| ' + $dateCell + ' | [' + $f.Title + '](' + $f.Name + ') |')
        }
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Generated by `tools\gen_docs_index.ps1`. See [`tools\audit_docs.ps1`](../../tools/audit_docs.ps1)')
    [void]$sb.AppendLine('for the docs\ structure audit that exempts journal dirs from the README rule.')

    $target = Join-Path $full 'INDEX.md'
    $changed = Write-Utf8IfChanged -Path $target -Text $sb.ToString()
    $state = 'unchanged'
    if ($changed) { $state = 'written' }
    # Alignment: 'docs/' (5) + name padded to 22 keeps the columns aligned for the
    # longest journal dir today (code-changes, 12) with room to spare. Padding used
    # to be 28 on the bare name, which pushed every column right by 5.
    $summaryLines.Add(('docs/' + $d).PadRight(27) + ' entries=' + $total.ToString().PadLeft(4) + '  ' + $state)
}

Write-Host ('JOURNAL_ENTRIES=' + $grandTotal)
foreach ($line in $summaryLines) { Write-Host $line }
if (-not $Quiet) {
    Write-Host 'INDEX.md files are generated; run this again after adding entries.'
}

exit 0
