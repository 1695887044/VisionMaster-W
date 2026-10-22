# run_gates.ps1 - single gate entry point (2026-10-22)
#
# Why this exists:
#   * Two assertion hosts (UIThemeSmokeTest, HelpProbe) are NOT in VisionMaster.sln,
#     so `dotnet build VisionMaster.sln` never rebuilds them. A stale exe keeps
#     printing yesterday's verdict - "the gate is green" would be a lie.
#   * The artifact gate (red line 3) and the root-layout gate existed as loose
#     scripts that nothing called. A rule without a gatekeeper is a comment.
#   * Building and running used to be scattered one-liners; nobody aggregated the
#     exit codes, so a red step scrolled away.
#
# This script is the ONLY entry point: it builds each host from its own csproj
# (works for in-sln and out-of-sln alike), runs it, runs the two repo audits, and
# returns a single exit code.
#
# Exit codes: 0 = every step green, 1 = at least one step red (see the summary).
# Logs: artifacts\logs\<yyyyMMdd>\build-<name>.log and gate-<name>.log (UTF-8, no BOM).
#
# ASCII only on purpose (R1). Chinese only appears in captured output, never in
# this file; the pass/fail markers are built from code points.
#
# Examples:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_gates.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_gates.ps1 -SkipBuild
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_gates.ps1 -Only CommChecks,ScadaChecks

[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [switch]$SkipBuild,
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Continue'

# `-File tools\run_gates.ps1 -Only a,b` reaches us as ONE string "a,b" (the
# -File binder does not split on commas), so normalize it here.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

$stamp = (Get-Date).ToString('yyyyMMdd')
$logDir = Join-Path $RepoRoot ('artifacts\logs\' + $stamp)
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Set-Location $RepoRoot

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Markers used by the hosts' own summary lines, built from code points so this
# file stays ASCII (R1): the Chinese passed/failed marker pair is assembled
# from code points below.
$cnPass = [string][char]0x901A + [string][char]0x8FC7
$cnFail = [string][char]0x5931 + [string][char]0x8D25
$countPattern = $cnPass + '\s*:\s*(\d+)\s+' + $cnFail + '\s*:\s*(\d+)'

$script:rows = New-Object System.Collections.Generic.List[object]

function Write-GateLog {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function Get-Counts {
    param([string]$Text)
    $m = [regex]::Matches($Text, $countPattern)
    if ($m.Count -eq 0) { return '' }
    $last = $m[$m.Count - 1]
    return ('pass=' + $last.Groups[1].Value + ' fail=' + $last.Groups[2].Value)
}

function Add-Row {
    param([string]$Name, [int]$Exit, [string]$Note)
    $script:rows.Add([pscustomobject]@{ Name = $Name; Exit = $Exit; Note = $Note })
    $flag = if ($Exit -eq 0) { 'OK  ' } else { 'FAIL' }
    Write-Host ('[{0}] {1,-20} exit={2,-3} {3}' -f $flag, $Name, $Exit, $Note)
}

# --- Step 1: repo audits (loose scripts, now actually called) -----------------

$audits = @(
    @{ N = 'audit_root';      P = 'tools\audit_root.ps1' }
    @{ N = 'audit_artifacts'; P = 'tools\audit_artifacts.ps1' }
)

foreach ($a in $audits) {
    if ($Only.Count -gt 0 -and $Only -notcontains $a.N) { continue }
    $path = Join-Path $RepoRoot $a.P
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Row -Name $a.N -Exit 1 -Note ('MISSING ' + $a.P)
        continue
    }
    $text = (& powershell -NoProfile -ExecutionPolicy Bypass -File $path 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-GateLog -Path (Join-Path $logDir ('gate-' + $a.N + '.log')) -Text $text
    $note = 'clean'
    if ($code -ne 0) { $note = 'see gate-' + $a.N + '.log' }
    Add-Row -Name $a.N -Exit $code -Note $note
}

# --- Step 2: assertion hosts --------------------------------------------------

$hosts = @(
    @{ N = 'CommChecks';         P = 'CommChecks\CommChecks.csproj' }
    @{ N = 'DataPipelineChecks'; P = 'DataPipelineChecks\DataPipelineChecks.csproj' }
    @{ N = 'ScadaChecks';        P = 'ScadaChecks\ScadaChecks.csproj' }
    @{ N = 'FlowCanvasChecks';   P = 'FlowCanvasChecks\FlowCanvasChecks.csproj' }
    @{ N = 'UIThemeSmokeTest';   P = 'UIThemeSmokeTest\UIThemeSmokeTest.csproj' }
    @{ N = 'HelpProbe';          P = 'HelpProbe\HelpProbe.csproj' }
)

foreach ($h in $hosts) {
    if ($Only.Count -gt 0 -and $Only -notcontains $h.N) { continue }

    $proj = Join-Path $RepoRoot $h.P
    if (-not (Test-Path -LiteralPath $proj)) {
        Add-Row -Name $h.N -Exit 1 -Note ('MISSING ' + $h.P)
        continue
    }

    if (-not $SkipBuild) {
        $buildLog = Join-Path $logDir ('build-' + $h.N + '.log')
        $bout = (& dotnet build $proj -m:1 -nodeReuse:false 2>&1 | Out-String)
        $bcode = $LASTEXITCODE
        Write-GateLog -Path $buildLog -Text $bout
        if ($bcode -ne 0) {
            Add-Row -Name $h.N -Exit $bcode -Note ('BUILD FAILED, see build-' + $h.N + '.log')
            continue
        }
    }

    $exe = Join-Path $RepoRoot ($h.N + '\bin\Debug\net9.0-windows\' + $h.N + '.exe')
    if (-not (Test-Path -LiteralPath $exe)) {
        Add-Row -Name $h.N -Exit 1 -Note ('EXE MISSING ' + $exe)
        continue
    }

    # R65: never run a stale exe. The exe must be at least as new as the host's
    # own sources, otherwise the verdict belongs to an older build.
    $exeTime = (Get-Item -LiteralPath $exe).LastWriteTimeUtc
    $srcDir = Join-Path $RepoRoot $h.N
    $newestSrc = Get-ChildItem -LiteralPath $srcDir -Recurse -File -Include *.cs,*.xaml,*.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\obj\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $newestSrc -and $newestSrc.LastWriteTimeUtc -gt $exeTime) {
        Add-Row -Name $h.N -Exit 1 -Note ('STALE EXE (source newer: ' + $newestSrc.Name + '); rebuild')
        continue
    }

    $text = (& $exe 2>&1 | Out-String)
    $code = $LASTEXITCODE
    Write-GateLog -Path (Join-Path $logDir ('gate-' + $h.N + '.log')) -Text $text
    $note = Get-Counts -Text $text
    if ([string]::IsNullOrWhiteSpace($note)) {
        $note = if ($code -eq 0) { 'all checks passed' } else { 'see gate-' + $h.N + '.log' }
    }
    Add-Row -Name $h.N -Exit $code -Note $note
}

# --- Summary ------------------------------------------------------------------

Write-Host ''
Write-Host '=== GATE SUMMARY ==='
$failed = 0
foreach ($r in $script:rows) {
    if ($r.Exit -ne 0) { $failed++ }
    Write-Host ('  {0,-20} exit={1,-3} {2}' -f $r.Name, $r.Exit, $r.Note)
}
Write-Host ('  logs: ' + $logDir)
Write-Host ''

if ($script:rows.Count -eq 0) {
    Write-Host '[run_gates] FAIL - no step ran (bad -Only filter?)'
    exit 1
}
if ($failed -gt 0) {
    Write-Host ('[run_gates] FAIL - ' + $failed + ' of ' + $script:rows.Count + ' steps red')
    exit 1
}
Write-Host ('[run_gates] OK - ' + $script:rows.Count + ' steps green')
exit 0
