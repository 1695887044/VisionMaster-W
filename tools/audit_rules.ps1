<#
.SYNOPSIS
  Audit rule ownership across the injection layer (AGENTS.md) and the
  sub-volume files (fen-ce) under docs\<gongcheng jilv>\.

.DESCRIPTION
  Ownership model: a rule ID is OWNED by the file whose markdown table row
  starts with "| R<n>[A-Z]? |". Prose mentions do not count as ownership.

  Checks (any failure exits 1):
    C1 union      - every R1..R187 (+ lettered variants) has an owner.
                    IDs that live in AGENTS.md prose only (no table row)
                    must be listed in $proseOwned AND mentioned in AGENTS.md.
    C2 uniqueness - at most one owner per ID; IDs in $dualAllowed must have
                    exactly 2 owners (A meaning + B meaning, see AGENTS.md 0).
    C3 link       - every fen-ce file on disk is referenced from AGENTS.md.
    C4 dangling   - every fen-ce referenced from AGENTS.md exists on disk.
    C5 size       - AGENTS.md length < 56000 (the file's own hard rule)
                    and <= 65536 (injection cap).
    C6 encoding   - every audited file strictly decodes as UTF-8, no BOM.

  Report (always printed): per-file rule-row counts, and a gate
  classification of every rule row (has gate / manual review / no gate
  declared). The classification is advisory: it drives which rules may be
  demoted from the injection layer (gated rules can move; silent rules
  without a gate must stay).

  This script is ASCII-only (AGENTS.md R1). CJK literals are built from
  code points via the CN helper.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)

try { [Console]::OutputEncoding = $utf8NoBom } catch { }

function CN {
    param([int[]]$CodePoints)
    return (-join ($CodePoints | ForEach-Object { [string][char]$_ }))
}

$cnDiscipline = CN @(0x5DE5, 0x7A0B, 0x7EAA, 0x5F8B)   # gong-cheng-ji-lv
$cnFence = CN @(0x5206, 0x518C)                         # fen-ce
$cnManual = CN @(0x4EBA, 0x5DE5, 0x8BC4, 0x5BA1)        # manual review
$cnGate = CN @(0x5B88, 0x95E8)                          # gate (shou-men)

$docsDir = Join-Path $RepoRoot 'docs'
$fenceDir = Join-Path $docsDir $cnDiscipline
$agentsPath = Join-Path $RepoRoot 'AGENTS.md'

if (-not (Test-Path -LiteralPath $agentsPath)) {
    Write-Host "FATAL: AGENTS.md not found at $agentsPath" -ForegroundColor Red
    exit 2
}
if (-not (Test-Path -LiteralPath $fenceDir)) {
    Write-Host "FATAL: discipline dir not found: $fenceDir" -ForegroundColor Red
    exit 2
}

$fenceFilter = $cnFence + '-*.md'
$fenceFiles = Get-ChildItem -LiteralPath $fenceDir -File -Filter $fenceFilter |
    Sort-Object Name

$audited = New-Object System.Collections.Generic.List[object]
$audited.Add([pscustomobject]@{ Name = 'AGENTS.md'; Path = $agentsPath })
foreach ($f in $fenceFiles) {
    $audited.Add([pscustomobject]@{ Name = $f.Name; Path = $f.FullName })
}

# --- load -------------------------------------------------------------------

$rowRegex = '^\|\s*(R\d{1,3}[A-Z]?)\s*\|'
$owners = @{}          # rule id -> list of file names
$fileStats = @()       # per file: rows, gated, manual, nogate, nogateIds

foreach ($entry in $audited) {
    $bytes = [IO.File]::ReadAllBytes($entry.Path)

    # C6 encoding: strict decode, no BOM.
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Write-Host ("FAIL: {0} carries a UTF-8 BOM (R3/R4)." -f $entry.Name) -ForegroundColor Red
        exit 1
    }
    try { $text = $utf8Strict.GetString($bytes) }
    catch {
        Write-Host ("FAIL: {0} is not strictly decodable as UTF-8 (R5)." -f $entry.Name) -ForegroundColor Red
        exit 1
    }

    $gated = 0; $manual = 0; $nogate = 0
    $nogateIds = New-Object System.Collections.Generic.List[string]
    $rowBytes = @{ gated = 0; manual = 0; nogate = 0 }

    # The section-0 conflict table lists A/B meanings, not ownership; its rows
    # would otherwise be counted as a second owner in AGENTS.md itself.
    $inSection0 = $false

    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^## 0\.') { $inSection0 = $true; continue }
        if ($line -match '^## ') { $inSection0 = $false }

        if (-not $inSection0 -and $line -match $rowRegex) {
            $id = $Matches[1]
            if (-not $owners.ContainsKey($id)) { $owners[$id] = @() }
            $owners[$id] = $owners[$id] + $entry.Name

            $b = $utf8NoBom.GetByteCount($line)
            if ($line.Contains($cnManual)) { $manual++; $rowBytes.manual += $b }
            elseif ($line.Contains($cnGate)) { $gated++; $rowBytes.gated += $b }
            else { $nogate++; $rowBytes.nogate += $b; $nogateIds.Add($id) }
        }
    }

    $fileStats += [pscustomobject]@{
        Name = $entry.Name; Rows = ($gated + $manual + $nogate)
        Gated = $gated; GatedBytes = $rowBytes.gated
        Manual = $manual; ManualBytes = $rowBytes.manual
        NoGate = $nogate; NoGateBytes = $rowBytes.nogate
        NoGateIds = ($nogateIds -join ',')
    }
}

# --- checks -----------------------------------------------------------------

$failures = New-Object System.Collections.Generic.List[string]
$proseOwned = @(109, 110, 111, 112, 113)   # AGENTS.md 30 range note + three steps
$dualAllowed = @(139, 140, 141)            # AGENTS.md 0 declares A/B meanings

$agentsText = [IO.File]::ReadAllText($agentsPath, $utf8Strict)

# C1 union
$missing = @()
for ($i = 1; $i -le 187; $i++) {
    if (-not $owners.ContainsKey('R' + $i)) { $missing += $i }
}
foreach ($p in $proseOwned) {
    if (-not $owners.ContainsKey('R' + $p)) {
        if (-not $agentsText.Contains('R' + $p)) {
            $failures.Add("C1 rule R$p is neither a table row nor mentioned in AGENTS.md")
        }
    }
}
$real = @($missing | Where-Object { $proseOwned -notcontains $_ })
if ($real.Count -gt 0) {
    $failures.Add(("C1 union incomplete, no owner for: R" + ($real -join ', R')))
}
if (-not $owners.ContainsKey('R132A')) {
    $failures.Add('C1 union incomplete: R132A has no owner')
}

# C2 uniqueness
foreach ($id in ($owners.Keys | Sort-Object)) {
    $n = $owners[$id].Count
    $num = 0
    if ($id -match '^R(\d{1,3})') { $num = [int]$Matches[1] }
    if ($dualAllowed -contains $num) {
        if ($n -ne 2) {
            $failures.Add(("C2 rule {0} is dual-allowed but has {1} owners: {2}" -f $id, $n, ($owners[$id] -join ', ')))
        }
    }
    elseif ($n -gt 1) {
        $failures.Add(("C2 rule {0} has {1} owners (only 0-table conflicts are allowed): {2}" -f $id, $n, ($owners[$id] -join ', ')))
    }
}

# C3 + C4 fen-ce linkage. Captures in AGENTS.md appear both with and without
# the .md extension and always behind the 'fen-ce-' prefix, which the regex
# consumes - so the prefix is prepended again for every comparison.
$referenced = @{}
$refRegex = [regex]::Escape($cnFence) + '-([^``\s>>)()' + [char]0x3011 + [char]0x300B + [char]0xFF09 + [char]0xFF0C + [char]0x3002 + [char]0xFF1B + ']*)'
foreach ($m in [regex]::Matches($agentsText, $refRegex)) {
    $name = $m.Groups[1].Value
    if ($name.Contains('*')) { continue }   # the header's generic 'fen-ce-*.md' pointer
    $referenced[$name] = $true
}
$onDisk = @{}
foreach ($f in $fenceFiles) { $onDisk[$f.Name] = $true; $onDisk[[IO.Path]::GetFileNameWithoutExtension($f.Name)] = $true }

foreach ($name in $referenced.Keys) {
    $candidate = $cnFence + '-' + $name
    if (-not $candidate.EndsWith('.md')) { $candidate = $candidate + '.md' }
    if (-not $onDisk.ContainsKey($candidate)) {
        $failures.Add(("C4 AGENTS.md references fen-ce '{0}' but the file does not exist" -f $candidate))
    }
}
foreach ($f in $fenceFiles) {
    $stem = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    $stem = $stem.Substring($cnFence.Length + 1)   # strip 'fen-ce-'
    if (-not $referenced.ContainsKey($stem) -and -not $referenced.ContainsKey($stem + '.md')) {
        $failures.Add(("C3 fen-ce '{0}' is not referenced from AGENTS.md (orphan sub-volume)" -f $f.Name))
    }
}

# C5 size
$agentsLen = (Get-Item -LiteralPath $agentsPath).Length
if ($agentsLen -ge 56000) {
    $failures.Add(("C5 AGENTS.md is {0} bytes, must stay under 56000 (see AGENTS.md header)" -f $agentsLen))
}
if ($agentsLen -gt 65536) {
    $failures.Add(("C5 AGENTS.md is {0} bytes, above the 65536 injection cap" -f $agentsLen))
}

# --- report -----------------------------------------------------------------

$stamp = (Get-Date).ToString('yyyyMMdd')
$logDir = Join-Path $RepoRoot ('artifacts\logs\' + $stamp)
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ('audit_rules-' + (Get-Date).ToString('HHmmss') + '.log')

$report = New-Object System.Collections.Generic.List[string]
$report.Add('[audit_rules] rule ownership and gate classification')
$report.Add(('  AGENTS.md bytes : ' + $agentsLen + ' (limit 56000)'))
$report.Add(('  fen-ce files    : ' + $fenceFiles.Count))
$report.Add('')
$report.Add('  file | rows | gated | manual | no-gate | no-gate bytes')
foreach ($s in $fileStats) {
    $report.Add(('  ' + $s.Name + ' | ' + $s.Rows + ' | ' + $s.Gated + ' (' + $s.GatedBytes + 'B) | ' +
        $s.Manual + ' (' + $s.ManualBytes + 'B) | ' + $s.NoGate + ' (' + $s.NoGateBytes + 'B)'))
}
$report.Add('')
$nogateAll = @()
foreach ($s in $fileStats) { if ($s.NoGateIds) { $nogateAll += ($s.NoGateIds -split ',') } }
$report.Add('  rules with NO gate declared (silent - must stay in AGENTS.md or get a gate):')
$report.Add('    ' + ($nogateAll -join ', '))
$report.Add('')
$report.Add('  owners per rule:')
foreach ($id in ($owners.Keys | Sort-Object { [regex]::Replace($_, '\D', '') }, { $_ })) {
    $report.Add(('    ' + $id + ' <- ' + ($owners[$id] -join ', ')))
}

$out = ($report -join "`r`n")
[IO.File]::WriteAllText($logPath, $out, $utf8NoBom)

$out | ForEach-Object { Write-Host $_ }

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host ('RULE LAYOUT VIOLATIONS: ' + $failures.Count) -ForegroundColor Red
    $failures | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Red }
    Write-Host ('report : ' + $logPath)
    exit 1
}

$totalRows = ($fileStats | Measure-Object Rows -Sum).Sum
Write-Host ''
Write-Host ('RULE LAYOUT OK: ' + $totalRows + ' rule rows / ' + $owners.Count + ' distinct ids / ' + $fenceFiles.Count + ' fen-ce / AGENTS.md ' + $agentsLen + ' bytes')
Write-Host ('report : ' + $logPath)
exit 0
