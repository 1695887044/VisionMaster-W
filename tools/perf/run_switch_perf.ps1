# run_switch_perf.ps1 - drive --switch-perf one phase per process, N runs each, then aggregate.
#
# ASCII only (R1). Chinese lives in the logs and in the report, never in this file.
#
# Why one phase per process: measured 2026-10-23 - running all five phases in a single
# process gave canvas=238ms ... tree=1601ms with only switch differences between them.
# That is in-process accumulation (WPF caches / GC heap / render queue), not the phase
# difference. Attribution needs a clean process per phase.
#
# Why N independent runs and a median of medians: single-run medians still move 2-3x
# between processes when the machine is busy. Three runs per phase, take the median of
# the three per-flow medians, and report the spread so a reader can see whether a delta
# is real.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_switch_perf.ps1
#   ... -Runs 3 -Phases canvas,tree -Reps 1
# Exit code: 0 = every phase produced a summary line, 1 = at least one did not.

[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [int]$Runs = 3,
    [int]$Reps = 1,
    [string[]]$Phases = @('canvas', 'noorder', 'minimap', 'collapse', 'tree'),
    [int]$StepTimeoutSec = 900
)

$ErrorActionPreference = 'Continue'

$Phases = @($Phases | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    # This script lives in tools\perf\, so the repo root is two levels up (R167: probes/perf
    # tooling stays under tools\, never at the root).
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

$stamp = (Get-Date).ToString('yyyyMMdd')
$logDir = Join-Path $RepoRoot ('artifacts\logs\' + $stamp)
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Set-Location $RepoRoot

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$ansi = [Text.Encoding]::GetEncoding(936)

$exe = Join-Path $RepoRoot 'FlowCanvasChecks\bin\Debug\net9.0-windows\FlowCanvasChecks.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host ('EXE MISSING: ' + $exe + ' (build FlowCanvasChecks first)')
    exit 1
}

function Invoke-Child {
    param([string]$Arguments, [string]$Tag)
    $out = Join-Path $logDir ('raw-' + $Tag + '.out')
    $err = Join-Path $logDir ('raw-' + $Tag + '.err')

    # Same discipline as tools\run_gates.ps1 (R183): drive Diagnostics.Process directly,
    # copy child stdout/stderr as RAW BYTES, bounded WaitForExit. Start-Process -Wait waits
    # on the whole process tree and -PassThru never populates ExitCode in PS 5.1.
    $exit = 127
    $fsOut = $null
    $fsErr = $null
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = $Arguments
        $psi.WorkingDirectory = $RepoRoot
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true

        $p = [System.Diagnostics.Process]::Start($psi)
        $fsOut = [IO.File]::Create($out)
        $fsErr = [IO.File]::Create($err)
        $tOut = $p.StandardOutput.BaseStream.CopyToAsync($fsOut)
        $tErr = $p.StandardError.BaseStream.CopyToAsync($fsErr)

        if ($p.WaitForExit($StepTimeoutSec * 1000)) {
            $exit = $p.ExitCode
        }
        else {
            & taskkill.exe /T /F /PID $p.Id 2>&1 | Out-Null
            [void]$p.WaitForExit(5000)
            $exit = 124
        }
        [void]$tOut.Wait(10000)
        [void]$tErr.Wait(10000)
    }
    catch {
        [IO.File]::WriteAllText($err, ('[run_switch_perf] launch failed: ' + $_.Exception.Message), $utf8NoBom)
        $exit = 127
    }
    finally {
        if ($null -ne $fsOut) { $fsOut.Dispose() }
        if ($null -ne $fsErr) { $fsErr.Dispose() }
    }

    $text = ''
    foreach ($f in @($out, $err)) {
        if (-not (Test-Path -LiteralPath $f)) { continue }
        $bytes = [IO.File]::ReadAllBytes($f)
        if ($bytes.Length -eq 0) { continue }
        try { $text += $utf8Strict.GetString($bytes) } catch { $text += $ansi.GetString($bytes) }
    }
    Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ Exit = $exit; Text = $text }
}

$rows = New-Object System.Collections.Generic.List[object]

foreach ($phase in $Phases) {
    for ($run = 1; $run -le $Runs; $run++) {
        $tag = 'switchperf-' + $phase + '-r' + $run
        $r = Invoke-Child -Tag $tag -Arguments ('--switch-perf "" ' + $Reps + ' ' + $phase)
        $gateLog = Join-Path $logDir ('gate-' + $tag + '.log')
        [IO.File]::WriteAllText($gateLog, $r.Text, $utf8NoBom)

        $summary = ($r.Text -split "`r?`n" | Where-Object { $_ -match '\[switchperf\] summary' } | Select-Object -Last 1)
        $heavy = ''
        if ($summary -match 'heavy_vm=([\d.]+) heavy_layout=([\d.]+) heavy_deferred=([\d.]+) heavy_total=([\d.]+)') {
            $heavy = 'heavy_vm=' + $matches[1] + ' heavy_layout=' + $matches[2] + ' heavy_deferred=' + $matches[3] + ' heavy_total=' + $matches[4]
        }
        elseif ($summary -match 'phase=floor.*layout=([\d.]+) layout_max=([\d.]+) deferred=([\d.]+)') {
            # floor has no per-flow rows: it is a single no-switch measurement of the same
            # UpdateLayout+drain path. Keep the field names so the aggregate code stays uniform.
            $heavy = 'heavy_layout=' + $matches[1] + ' heavy_deferred=' + $matches[3]
        }
        elseif (-not [string]::IsNullOrWhiteSpace($summary)) {
            # diag prints no summary at all; any other phase that produced one but no heavy_*
            # fields still counts as "ran".
            $heavy = 'summary_no_heavy_fields'
        }
        if ([string]::IsNullOrWhiteSpace($summary)) {
            Write-Host ('[' + $tag + '] NO SUMMARY exit=' + $r.Exit + ' log=' + $gateLog)
            $rows.Add([pscustomobject]@{ Phase = $phase; Run = $run; Exit = $r.Exit; Heavy = ''; Ok = $false })
            continue
        }
        Write-Host ('[' + $tag + '] ' + $heavy)
        $rows.Add([pscustomobject]@{ Phase = $phase; Run = $run; Exit = $r.Exit; Heavy = $heavy; Ok = ($r.Exit -eq 0) })
    }
}

# ---- aggregate: median across runs, per phase ----
Write-Host ''
Write-Host '=== AGGREGATE (median of per-run medians) ==='
$bad = 0
foreach ($phase in $Phases) {
    $vals = @{}
    foreach ($k in @('heavy_vm', 'heavy_layout', 'heavy_deferred', 'heavy_total')) { $vals[$k] = @() }
    foreach ($row in $rows | Where-Object { $_.Phase -eq $phase -and $_.Ok }) {
        foreach ($k in @('heavy_vm', 'heavy_layout', 'heavy_deferred', 'heavy_total')) {
            if ($row.Heavy -match ($k + '=([\d.]+)')) { $vals[$k] += [double]$matches[1] }
        }
    }
    if ($vals['heavy_layout'].Count -eq 0) {
        Write-Host ('  ' + $phase.PadRight(10) + ' NO DATA')
        $bad++
        continue
    }
    $parts = @()
    foreach ($k in @('heavy_vm', 'heavy_layout', 'heavy_deferred', 'heavy_total')) {
        if ($vals[$k].Count -eq 0) { continue }
        $s = @($vals[$k] | Sort-Object)
        $med = $s[[int][Math]::Floor($s.Count / 2)]
        $min = $s[0]
        $max = $s[$s.Count - 1]
        $parts += ('{0}={1:0.0}[{2:0.0}-{3:0.0}]' -f $k, $med, $min, $max)
    }
    Write-Host ('  ' + $phase.PadRight(10) + ' ' + ($parts -join ' '))
}

Write-Host ''
Write-Host ('  logs: ' + $logDir)
if ($bad -gt 0) {
    Write-Host ('[run_switch_perf] FAIL - ' + $bad + ' phase(s) produced no summary')
    exit 1
}
Write-Host '[run_switch_perf] OK - every phase produced a summary'
exit 0
