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
# Encoding: child output is redirected to a file as RAW BYTES by Start-Process, so
# nothing is decoded on the way in. On the way out each file is decoded strictly as
# UTF-8 first (dotnet hosts) and falls back to codepage 936 (powershell.exe children).
# The earlier version piped `& $exe 2>&1 | Out-String`, which decoded child stdout
# with the console codepage and then wrote UTF-8: the log came out mojibake AND the
# pass/fail counts never matched, so every host printed "all checks passed".
# A summary line nobody can verify is worse than no summary line (R13).
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
    [string[]]$Only = @(),
    [int]$StepTimeoutSec = 600
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
$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$ansi = [Text.Encoding]::GetEncoding(936)

# Markers used by the hosts' own summary lines, built from code points so this
# file stays ASCII (R1).
$cnPass = [string][char]0x901A + [string][char]0x8FC7
$cnFail = [string][char]0x5931 + [string][char]0x8D25
$countPattern = $cnPass + '\s*:\s*(\d+)\s+' + $cnFail + '\s*:\s*(\d+)'

$script:rows = New-Object System.Collections.Generic.List[object]

function Write-GateLog {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function Read-Captured {
    param([string]$OutFile, [string]$ErrFile)
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in @($OutFile, $ErrFile)) {
        if (-not (Test-Path -LiteralPath $f)) { continue }
        $bytes = [IO.File]::ReadAllBytes($f)
        if ($bytes.Length -eq 0) { continue }
        $text = $null
        try { $text = $utf8Strict.GetString($bytes) } catch { $text = $ansi.GetString($bytes) }
        [void]$sb.Append($text)
        if (-not $text.EndsWith("`n")) { [void]$sb.Append("`r`n") }
    }
    return $sb.ToString()
}

# Runs a child, captures stdout+stderr as raw bytes, returns exit code.
function Invoke-Captured {
    param([string]$FilePath, [string]$Arguments, [string]$Tag)
    # Raw file names carry this gate's PID: two sessions on the same machine and
    # the same day would otherwise open the SAME raw-<tag>.out and truncate it
    # under each other. The symptom is a false red ("NO OUTPUT / host produced
    # nothing") or, worse, a summary line from the other run being counted here
    # (2026-10-26: two concurrent gates, one kept failing with exit=-1).
    $out = Join-Path $logDir ('raw-' + $Tag + '-' + $PID + '.out')
    $err = Join-Path $logDir ('raw-' + $Tag + '-' + $PID + '.err')

    # Why not Start-Process:
    #   1. -ArgumentList '' throws ParameterBindingValidationException, and with
    #      $ErrorActionPreference=Continue that used to fall through to Exit=0 -
    #      a gate that reports green without running anything.
    #   2. -Wait waits on the whole process tree: one lingering descendant blocks
    #      the gate forever (2026-10-22: 11 minutes idle, no child alive, no log).
    #   3. Without -Wait, -PassThru NEVER populates ExitCode in Windows PowerShell
    #      5.1 - measured: raw / after WaitForExit / after Refresh all empty, even
    #      though HasExited is True. A null exit code compares as "!= 0" but
    #      coerces to 0, so every host looked green while printing BUILD FAILED.
    # So: drive System.Diagnostics.Process directly. Raw bytes are copied to the
    # log files through the child's stdout/stderr base streams, so nothing is
    # decoded on the way in (R183) and there is no pipe buffer to deadlock on.
    $exit = 127
    $fsOut = $null
    $fsErr = $null
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $FilePath
        if (-not [string]::IsNullOrWhiteSpace($Arguments)) { $psi.Arguments = $Arguments }
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
            [IO.File]::WriteAllText($err, ('[run_gates] TIMEOUT after ' + $StepTimeoutSec + 's; process tree killed'), $utf8NoBom)
            $exit = 124
        }
        [void]$tOut.Wait(10000)
        [void]$tErr.Wait(10000)
    }
    catch {
        [IO.File]::WriteAllText($err, ('[run_gates] launch failed: ' + $_.Exception.Message), $utf8NoBom)
        $exit = 127
    }
    finally {
        if ($null -ne $fsOut) { $fsOut.Dispose() }
        if ($null -ne $fsErr) { $fsErr.Dispose() }
    }

    $text = Read-Captured -OutFile $out -ErrFile $err
    Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ Exit = $exit; Text = $text }
}

function Get-Counts {
    param([string]$Text, [string]$Pattern)
    # Most hosts print a Chinese summary line; ScadaWebChecks prints the English
    # one (RESULT: N PASS / M FAIL). Callers pass Pattern for the latter.
    if ([string]::IsNullOrWhiteSpace($Pattern)) { $Pattern = $countPattern }
    $m = [regex]::Matches($Text, $Pattern)
    if ($m.Count -eq 0) { return '' }
    $last = $m[$m.Count - 1]
    return ('pass=' + $last.Groups[1].Value + ' fail=' + $last.Groups[2].Value)
}

function Add-Row {
    param([string]$Name, $Exit, [string]$Note)
    # An unknown exit code is RED, never green. A null used to coerce to 0, which
    # is how "BUILD FAILED" rows once printed exit=0 and the gate said all green.
    $code = 1
    if ($null -ne $Exit) {
        $code = [int]$Exit
    }
    elseif ([string]::IsNullOrWhiteSpace($Note)) {
        $Note = 'exit code unknown'
    }
    $script:rows.Add([pscustomobject]@{ Name = $Name; Exit = $code; Note = $Note })
    $flag = if ($code -eq 0) { 'OK  ' } else { 'FAIL' }
    Write-Host ('[{0}] {1,-20} exit={2,-3} {3}' -f $flag, $Name, $code, $Note)
}

# --- Step 0: docs\ journal index freshness (B4) ------------------------------
# A code change that is not in docs\code-changes\ never happened (R177), and an
# entry that is not in INDEX.md cannot be found. The generator is idempotent, so
# "regenerating changes nothing" is exactly the property we gate on: if it has to
# write, somebody added journal entries and forgot to refresh the index.
if (-not ($Only.Count -gt 0 -and $Only -notcontains 'docs_index')) {
    $genPath = Join-Path $RepoRoot 'tools\gen_docs_index.ps1'
    if (-not (Test-Path -LiteralPath $genPath)) {
        Add-Row -Name 'docs_index' -Exit 1 -Note ('MISSING ' + $genPath)
    }
    else {
        $preReport = Join-Path $logDir 'docs_index_pre.log'
        $preText = ''
        if (Test-Path -LiteralPath $preReport) { $preText = [IO.File]::ReadAllText($preReport, [Text.Encoding]::UTF8) }

        $g = Invoke-Captured -FilePath 'powershell.exe' -Tag 'docs_index_pre' `
            -Arguments ('-NoProfile -ExecutionPolicy Bypass -File "' + $genPath + '"')
        Write-GateLog -Path $preReport -Text $g.Text

        $stale = @()
        foreach ($line in ($g.Text -split "`r?`n")) {
            if ($line -match '^\s*docs/\S+\s+entries=\s*\d+\s+written') {
                $stale += ($line -replace '\s+', ' ').Trim()
            }
        }
        if ($g.Exit -ne 0) {
            Add-Row -Name 'docs_index' -Exit $g.Exit -Note 'generator failed, see docs_index_pre.log'
        }
        elseif ($stale.Count -gt 0) {
            # fail closed and leave the index correct on disk
            $null = Invoke-Captured -FilePath 'powershell.exe' -Tag 'docs_index_fix' `
                -Arguments ('-NoProfile -ExecutionPolicy Bypass -File "' + $genPath + '"')
            Add-Row -Name 'docs_index' -Exit 1 -Note ('STALE INDEX (' + ($stale -join '; ') + ') - regenerated, re-run')
        }
        else {
            Add-Row -Name 'docs_index' -Exit 0 -Note 'up to date'
        }
    }
}

# --- Step 1: repo audits (loose scripts, now actually called) -----------------

$audits = @(
    @{ N = 'audit_root';        P = 'tools\audit_root.ps1' }
    @{ N = 'audit_artifacts';   P = 'tools\audit_artifacts.ps1' }
    @{ N = 'audit_docs';        P = 'tools\audit_docs.ps1' }
    @{ N = 'audit_rules';       P = 'tools\audit_rules.ps1' }
    @{ N = 'sync_constraints';  P = 'tools\sync_constraints.ps1'; Args = '-Check' }
)

foreach ($a in $audits) {
    if ($Only.Count -gt 0 -and $Only -notcontains $a.N) { continue }
    $path = Join-Path $RepoRoot $a.P
    if (-not (Test-Path -LiteralPath $path)) {
        # A registered gate whose script disappeared is RED, not skipped: the
        # rule it enforces would otherwise silently stop being enforced.
        Add-Row -Name $a.N -Exit 1 -Note ('MISSING ' + $a.P)
        continue
    }
    $argLine = '-NoProfile -ExecutionPolicy Bypass -File "' + $path + '"'
    if ($a.ContainsKey('Args') -and $a.Args) { $argLine = $argLine + ' ' + $a.Args }
    $r = Invoke-Captured -FilePath 'powershell.exe' -Tag $a.N -Arguments $argLine
    Write-GateLog -Path (Join-Path $logDir ('gate-' + $a.N + '.log')) -Text $r.Text
    $note = 'clean'
    if ($r.Exit -ne 0) { $note = 'see gate-' + $a.N + '.log' }
    Add-Row -Name $a.N -Exit $r.Exit -Note $note
}

# --- Step 1.5: solution layout verification (R187) ---------------------------
# The generator lives under tools\ and is NOT in VisionMaster.sln, so it has to
# be built here (R182: a registered gate whose tool vanished is RED, not
# skipped). The --verify mode is read-only: it never rewrites a .vms.
# Judgement is field-scoped, not "no drive letter anywhere": asset references
# are resolved against the solution root by SolutionAssetService.TryResolve, so
# an absolute path there breaks the moment the folder is moved (ERROR). Step
# level FilePath/FolderPath have no root anchor in IExecutionContext at all -
# that is a documented boundary (WARN) until the contract gains one.
if (-not ($Only.Count -gt 0 -and $Only -notcontains 'vms_verify')) {
    $genProj = Join-Path $RepoRoot 'tools\vms-generator\GenSolution.csproj'
    $cnSol = [string][char]0x89E3 + [string][char]0x51B3 + [string][char]0x65B9 + [string][char]0x6848
    $solDir = Join-Path $RepoRoot $cnSol
    $genExe = Join-Path $RepoRoot 'tools\vms-generator\bin\Debug\net9.0\GenSolution.exe'

    if (-not (Test-Path -LiteralPath $genProj)) {
        Add-Row -Name 'vms_verify' -Exit 1 -Note ('MISSING ' + $genProj)
    }
    elseif (-not (Test-Path -LiteralPath $solDir)) {
        Add-Row -Name 'vms_verify' -Exit 1 -Note ('MISSING ' + $solDir)
    }
    else {
        $vb = Invoke-Captured -FilePath 'dotnet' -Tag 'build-vms_verify' `
            -Arguments ('build "' + $genProj + '" -m:1 -nodeReuse:false')
        Write-GateLog -Path (Join-Path $logDir 'build-vms_verify.log') -Text $vb.Text
        if ($vb.Exit -ne 0) {
            Add-Row -Name 'vms_verify' -Exit $vb.Exit -Note 'BUILD FAILED, see build-vms_verify.log'
        }
        elseif (-not (Test-Path -LiteralPath $genExe)) {
            Add-Row -Name 'vms_verify' -Exit 1 -Note ('EXE MISSING ' + $genExe)
        }
        else {
            $v = Invoke-Captured -FilePath $genExe -Tag 'vms_verify' `
                -Arguments ('--verify "' + $solDir + '"')
            Write-GateLog -Path (Join-Path $logDir 'gate-vms_verify.log') -Text $v.Text
            if ([string]::IsNullOrWhiteSpace($v.Text)) {
                Add-Row -Name 'vms_verify' -Exit 1 -Note 'NO OUTPUT (did not run)'
            }
            elseif ($v.Exit -ne 0) {
                Add-Row -Name 'vms_verify' -Exit $v.Exit -Note 'see gate-vms_verify.log'
            }
            else {
                Add-Row -Name 'vms_verify' -Exit 0 -Note 'clean'
            }
        }
    }
}

# --- Step 2: assertion hosts --------------------------------------------------

$hosts = @(
    @{ N = 'CommChecks';         P = 'CommChecks\CommChecks.csproj' }
    @{ N = 'DataPipelineChecks'; P = 'DataPipelineChecks\DataPipelineChecks.csproj' }
    @{ N = 'DatabaseChecks';     P = 'DatabaseChecks\DatabaseChecks.csproj' }
    @{ N = 'ScadaChecks';        P = 'ScadaChecks\ScadaChecks.csproj' }
    @{ N = 'RecipeChecks';       P = 'RecipeChecks\RecipeChecks.csproj' }
    @{ N = 'FlowCanvasChecks';   P = 'FlowCanvasChecks\FlowCanvasChecks.csproj' }
    # VarScopeAudit lives under tools\probes (registered in probes.json) and prints
    # the English summary line "=== RESULT: ALL_OK ===" — own dir + count pattern.
    @{ N = 'VarScopeAudit';      P = 'tools\probes\_VarScopeAudit\VarScopeAudit.csproj'; D = 'tools\probes\_VarScopeAudit'; M = 'ASSERT:\s*(\d+)\s*PASS\s*/\s*(\d+)\s*FAIL' }
    @{ N = 'UIThemeSmokeTest';   P = 'UIThemeSmokeTest\UIThemeSmokeTest.csproj' }
    @{ N = 'HelpProbe';          P = 'HelpProbe\HelpProbe.csproj' }
    # ScadaWebChecks lives under Web\ (not next to the other hosts) and prints the
    # English summary line, so it carries its own directory + count pattern.
    @{ N = 'ScadaWebChecks';     P = 'Web\ScadaWebChecks\ScadaWebChecks.csproj'; D = 'Web\ScadaWebChecks'; M = 'RESULT:\s*(\d+)\s*PASS\s*/\s*(\d+)\s*FAIL' }
)

foreach ($h in $hosts) {
    if ($Only.Count -gt 0 -and $Only -notcontains $h.N) { continue }

    $proj = Join-Path $RepoRoot $h.P
    if (-not (Test-Path -LiteralPath $proj)) {
        Add-Row -Name $h.N -Exit 1 -Note ('MISSING ' + $h.P)
        continue
    }

    if (-not $SkipBuild) {
        $b = Invoke-Captured -FilePath 'dotnet' -Tag ('build-' + $h.N) `
            -Arguments ('build "' + $proj + '" -m:1 -nodeReuse:false')
        Write-GateLog -Path (Join-Path $logDir ('build-' + $h.N + '.log')) -Text $b.Text
        if ($b.Exit -ne 0) {
            Add-Row -Name $h.N -Exit $b.Exit -Note ('BUILD FAILED, see build-' + $h.N + '.log')
            continue
        }
    }

    $dir = $h.N
    if ($h.ContainsKey('D') -and -not [string]::IsNullOrWhiteSpace($h.D)) { $dir = $h.D }

    $exe = Join-Path $RepoRoot ($dir + '\bin\Debug\net9.0-windows\' + $h.N + '.exe')
    if (-not (Test-Path -LiteralPath $exe)) {
        Add-Row -Name $h.N -Exit 1 -Note ('EXE MISSING ' + $exe)
        continue
    }

    # R65: never run a stale exe. The exe must be at least as new as the host's
    # own sources, otherwise the verdict belongs to an older build.
    $exeTime = (Get-Item -LiteralPath $exe).LastWriteTimeUtc
    $srcDir = Join-Path $RepoRoot $dir
    $newestSrc = Get-ChildItem -LiteralPath $srcDir -Recurse -File -Include *.cs,*.xaml,*.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\obj\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $newestSrc -and $newestSrc.LastWriteTimeUtc -gt $exeTime) {
        Add-Row -Name $h.N -Exit 1 -Note ('STALE EXE (source newer: ' + $newestSrc.Name + '); rebuild')
        continue
    }

    $r = Invoke-Captured -FilePath $exe -Tag $h.N -Arguments ''
    Write-GateLog -Path (Join-Path $logDir ('gate-' + $h.N + '.log')) -Text $r.Text
    # A host that produced no output did not run. Never let that pass as green.
    if ([string]::IsNullOrWhiteSpace($r.Text)) {
        Add-Row -Name $h.N -Exit 1 -Note 'NO OUTPUT (host produced nothing)'
        continue
    }
    $note = ''
    if ($h.ContainsKey('M')) { $note = Get-Counts -Text $r.Text -Pattern $h.M }
    else { $note = Get-Counts -Text $r.Text }
    if ($r.Exit -eq 124) {
        $note = 'TIMEOUT after ' + $StepTimeoutSec + 's (killed)'
    }
    elseif ([string]::IsNullOrWhiteSpace($note)) {
        $note = if ($r.Exit -eq 0) { 'exit 0 (no summary line)' } else { 'see gate-' + $h.N + '.log' }
    }
    Add-Row -Name $h.N -Exit $r.Exit -Note $note
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
