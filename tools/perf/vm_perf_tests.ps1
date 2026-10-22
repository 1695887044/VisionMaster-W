$ErrorActionPreference = 'Stop'

$appDir = 'D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows'
$cfg = Join-Path $appDir 'AppConfig.json'
$bak = Join-Path $appDir 'AppConfig.json.zcode-bak'
$sol = 'D:\C#\VM\artifacts\perf-suite.vms'
$img = 'D:\C#\VM\Image\board\board-01.png'
$limitMb = 2048
$outDir = 'D:\C#\VM\artifacts\perf'
$csv = Join-Path $outDir 'perf-results.csv'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Set-Content -Path $csv -Value 'phase,flow,code,seconds,memMB,cpuPct' -Encoding ASCII

$script:lastCpu = $null
$script:lastSample = $null
$script:aborted = $false

function Test-Port([int]$port) {
    try {
        $c = New-Object Net.Sockets.TcpClient
        $iar = $c.BeginConnect('127.0.0.1', $port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne(300)) { $c.EndConnect($iar); $c.Close(); return $true }
        $c.Close(); return $false
    } catch { return $false }
}

function Get-Sample([int]$processId) {
    $p = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($p -eq $null) { return $null }
    $now = Get-Date
    $cpuPct = 0.0
    if ($script:lastCpu -ne $null -and $script:lastSample -ne $null) {
        $elapsed = ($now - $script:lastSample).TotalSeconds
        if ($elapsed -gt 0) {
            $delta = ($p.TotalProcessorTime - $script:lastCpu).TotalSeconds
            $cpuPct = [math]::Round(100.0 * $delta / ($elapsed * [Environment]::ProcessorCount), 1)
        }
    }
    $script:lastCpu = $p.TotalProcessorTime
    $script:lastSample = $now
    return [pscustomobject]@{
        MemMB = [math]::Round($p.WorkingSet64 / 1MB, 1)
        CpuPct = $cpuPct
    }
}

function Add-Row([string]$phase, [string]$flow, [string]$code, [double]$seconds, [double]$mem, [double]$cpu) {
    $line = '{0},{1},{2},{3},{4},{5}' -f $phase, $flow, $code, $seconds, $mem, $cpu
    Add-Content -Path $csv -Value $line -Encoding ASCII
}

function Invoke-Flow([string]$flow) {
    $raw = & curl.exe -s -o NUL -w "%{http_code},%{time_total}" --max-time 180 -X POST `
        -H "Authorization: Bearer visionmaster" -H "X-Image-Name: board-01.png" `
        --data-binary "@$img" "http://127.0.0.1:19000/flow/$flow"
    $text = ($raw | Out-String).Trim()
    $parts = $text -split ','
    if ($parts.Count -lt 2) { return [pscustomobject]@{ Code = 'ERR'; Seconds = 0.0 } }
    $sec = 0.0
    [void][double]::TryParse($parts[1], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$sec)
    return [pscustomobject]@{ Code = $parts[0]; Seconds = $sec }
}

function Record([string]$phase, [string]$flow, [int]$processId) {
    $r = Invoke-Flow $flow
    $s = Get-Sample $processId
    if ($s -eq $null) { return $r }
    Add-Row $phase $flow $r.Code $r.Seconds $s.MemMB $s.CpuPct
    if ($s.MemMB -gt $limitMb) {
        Write-Host "MEMORY LIMIT $limitMb MB -> KILL"
        Stop-Process -Id $processId -Force
        $script:aborted = $true
    }
    return $r
}

function Wait-Ready([int]$processId) {
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { return $false }
        if (Test-Port 19000) { break }
        Start-Sleep -Milliseconds 500
    }
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { return $false }
        $r = Invoke-Flow 'Main'
        if ($r.Code -ne '404') { return $true }
        Start-Sleep -Seconds 3
    }
    return $false
}

# Ensure no stale instance owns port 19000.
Get-Process VisionMaster -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Copy-Item 'D:\C#\VM\artifacts\test-suite.vms' $sol -Force
Copy-Item $cfg $bak -Force
$json = [IO.File]::ReadAllText($cfg, [Text.Encoding]::UTF8)
$obj = $json | ConvertFrom-Json
$obj.StartupSolutionPath = $sol
[IO.File]::WriteAllText($cfg, ($obj | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath (Join-Path $appDir 'VisionMaster.exe') -WorkingDirectory $appDir -PassThru
Write-Host "pid=$($p.Id)"
$ready = Wait-Ready $p.Id
$sw.Stop()
Write-Host ("startup_ready_seconds=" + [math]::Round($sw.Elapsed.TotalSeconds, 2) + " ready=" + $ready)
if (-not $ready) {
    Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
    Copy-Item $bak $cfg -Force
    Remove-Item $bak -Force
    throw 'app did not become ready'
}

$s0 = Get-Sample $p.Id
Write-Host ("baseline_mem=" + $s0.MemMB + " MB")

# Warmup
foreach ($flow in @('Test_Comm', 'Test_Logic', 'Test_Data', 'Test_Vision')) {
    for ($i = 1; $i -le 2; $i++) { [void](Invoke-Flow $flow) }
}

# Phase 1: single-run latency, 20 samples each
foreach ($flow in @('Test_Comm', 'Test_Logic', 'Test_Data', 'Test_Vision')) {
    for ($i = 1; $i -le 10; $i++) {
        if ($script:aborted) { break }
        $r = Record 'latency' $flow $p.Id
        if ($r.Code -ne '200') { Write-Host "latency $flow#$i code=$($r.Code)" }
    }
}

# Phase 2: sequential throughput
$seq = @(
    @{ Flow = 'Test_Logic'; Count = 60 },
    @{ Flow = 'Test_Comm'; Count = 100 },
    @{ Flow = 'Test_Data'; Count = 50 },
    @{ Flow = 'Test_Vision'; Count = 100 }
)
foreach ($item in $seq) {
    if ($script:aborted) { break }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 1; $i -le $item.Count; $i++) {
        if ($script:aborted) { break }
        $r = Record 'sequential' $item.Flow $p.Id
        if ($r.Code -ne '200') { Write-Host "sequential $($item.Flow)#$i code=$($r.Code)" }
        if ($i % 25 -eq 0) {
            $s = Get-Sample $p.Id
            if ($s -ne $null) { Write-Host ("seq {0} {1}/{2} mem={3} MB cpu={4}%" -f $item.Flow, $i, $item.Count, $s.MemMB, $s.CpuPct) }
        }
    }
    $sw.Stop()
    $rps = if ($sw.Elapsed.TotalSeconds -gt 0) { [math]::Round($item.Count / $sw.Elapsed.TotalSeconds, 1) } else { 0 }
    Write-Host ("sequential {0} count={1} seconds={2} rps={3}" -f $item.Flow, $item.Count, [math]::Round($sw.Elapsed.TotalSeconds, 2), $rps)
}

# Phase 3: concurrent, 4 workers x 100 requests, different flows
if (-not $script:aborted) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $jobs = @()
    $workerFlows = @('Test_Comm', 'Test_Logic', 'Test_Data', 'Test_Vision')
    foreach ($flow in $workerFlows) {
        $jobs += Start-Job -ScriptBlock {
            param($flow, $img, $count)
            $codes = @()
            for ($i = 1; $i -le $count; $i++) {
                $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 180 -X POST `
                    -H "Authorization: Bearer visionmaster" -H "X-Image-Name: board-01.png" `
                    --data-binary "@$img" "http://127.0.0.1:19000/flow/$flow"
                $codes += ($code | Out-String).Trim()
            }
            return ($codes -join ',')
        } -ArgumentList $flow, $img, 25
    }
    $results = Receive-Job -Job $jobs -Wait
    Remove-Job -Job $jobs -Force
    $sw.Stop()
    $allCodes = @()
    foreach ($r in $results) { $allCodes += ($r -split ',') }
    $ok = ($allCodes | Where-Object { $_ -eq '200' }).Count
    $bad = $allCodes.Count - $ok
    Write-Host ("concurrent total={0} ok={1} bad={2} seconds={3} rps={4}" -f $allCodes.Count, $ok, $bad, [math]::Round($sw.Elapsed.TotalSeconds, 2), [math]::Round($allCodes.Count / $sw.Elapsed.TotalSeconds, 1))
    $s = Get-Sample $p.Id
    if ($s -ne $null) { Write-Host ("concurrent mem=" + $s.MemMB + " MB cpu=" + $s.CpuPct + "%") }
}

# Phase 4: duplicate same-flow pairs, expect one 200 and one 409
if (-not $script:aborted) {
    for ($pair = 1; $pair -le 3; $pair++) {
        $jobs = @()
        foreach ($n in 1..2) {
            $jobs += Start-Job -ScriptBlock {
                param($img)
                & curl.exe -s -o NUL -w "%{http_code}" --max-time 180 -X POST `
                    -H "Authorization: Bearer visionmaster" -H "X-Image-Name: board-01.png" `
                    --data-binary "@$img" "http://127.0.0.1:19000/flow/Test_Vision"
            } -ArgumentList $img
        }
        $codes = Receive-Job -Job $jobs -Wait
        Remove-Job -Job $jobs -Force
        Write-Host ("duplicate pair={0} codes={1}" -f $pair, (($codes | ForEach-Object { ($_ | Out-String).Trim() }) -join '/'))
        Start-Sleep -Seconds 1
    }
}

# Phase 5: long-run stability, 300 Vision
if (-not $script:aborted) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 1; $i -le 150; $i++) {
        if ($script:aborted) { break }
        $r = Record 'longrun' 'Test_Vision' $p.Id
        if ($r.Code -ne '200') { Write-Host "longrun#$i code=$($r.Code)" }
        if ($i % 25 -eq 0) {
            $s = Get-Sample $p.Id
            if ($s -ne $null) { Write-Host ("longrun {0}/150 mem={1} MB cpu={2}%" -f $i, $s.MemMB, $s.CpuPct) }
        }
    }
    $sw.Stop()
    Write-Host ("longrun seconds=" + [math]::Round($sw.Elapsed.TotalSeconds, 2) + " rps=" + [math]::Round(150 / $sw.Elapsed.TotalSeconds, 1))
}

# Phase 6: mixed all HTTP flows, 5 rounds
if (-not $script:aborted) {
    $flows = @('Test_Comm','Test_Motion','Test_Vision','Test_Logic','Test_Data',
               'Test_CommEdge','Test_MotionEdge','Test_VisionEdge','Test_FlowEdge','Test_DataEdge','Test_Report')
    for ($round = 1; $round -le 2; $round++) {
        $bad = 0
        foreach ($flow in $flows) {
            if ($script:aborted) { break }
            $r = Record 'mixed' $flow $p.Id
            if ($r.Code -ne '200') { $bad++; Write-Host "mixed round=$round flow=$flow code=$($r.Code)" }
        }
        Write-Host ("mixed round={0} bad={1}" -f $round, $bad)
    }
}

$sEnd = Get-Sample $p.Id
if ($sEnd -ne $null) { Write-Host ("final_mem=" + $sEnd.MemMB + " MB cpu=" + $sEnd.CpuPct + "%") }
$p = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($p -ne $null) { Stop-Process -Id $p.Id -Force }
Start-Sleep -Seconds 2
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Remove-Item $sol -Force
Write-Host 'config restored'
Write-Host "csv=$csv"
