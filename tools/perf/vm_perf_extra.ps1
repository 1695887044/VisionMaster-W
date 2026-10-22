$ErrorActionPreference = 'Stop'
$appDir = 'D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows'
$cfg = Join-Path $appDir 'AppConfig.json'
$bak = Join-Path $appDir 'AppConfig.json.zcode-bak'
$sol = 'D:\C#\VM\artifacts\perf-suite.vms'
$img = 'D:\C#\VM\Image\board\board-01.png'
$limitMb = 2048

function Test-Port([int]$port) {
    try {
        $c = New-Object Net.Sockets.TcpClient
        $iar = $c.BeginConnect('127.0.0.1', $port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne(300)) { $c.EndConnect($iar); $c.Close(); return $true }
        $c.Close(); return $false
    } catch { return $false }
}

function Invoke-Flow([string]$flow) {
    $raw = & curl.exe -s -o NUL -w "%{http_code},%{time_total}" --max-time 180 -X POST `
        -H "Authorization: Bearer visionmaster" -H "X-Image-Name: board-01.png" `
        --data-binary "@$img" "http://127.0.0.1:19000/flow/$flow"
    return (($raw | Out-String).Trim())
}

Get-Process VisionMaster -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Copy-Item 'D:\C#\VM\artifacts\test-suite.vms' $sol -Force
Copy-Item $cfg $bak -Force
$json = [IO.File]::ReadAllText($cfg, [Text.Encoding]::UTF8)
$obj = $json | ConvertFrom-Json
$obj.StartupSolutionPath = $sol
[IO.File]::WriteAllText($cfg, ($obj | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))

$p = Start-Process -FilePath (Join-Path $appDir 'VisionMaster.exe') -WorkingDirectory $appDir -PassThru
Write-Host "pid=$($p.Id)"
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { Write-Host 'process exited'; break }
    if (Test-Port 19000) { break }
    Start-Sleep -Milliseconds 500
}
$ready = $false
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { break }
    $r = Invoke-Flow 'Main'
    if (($r -split ',')[0] -ne '404') { $ready = $true; break }
    Start-Sleep -Seconds 3
}
Write-Host "ready=$ready"
if (-not $ready) {
    Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
    Copy-Item $bak $cfg -Force
    Remove-Item $bak -Force
    throw 'app not ready'
}

# Duplicate same-flow pairs, synchronized start, slow flow Test_Comm (about 1.3s each).
for ($pair = 1; $pair -le 5; $pair++) {
    $startAt = (Get-Date).AddSeconds(2)
    $jobs = @()
    foreach ($n in 1..2) {
        $jobs += Start-Job -ScriptBlock {
            param($img, $startAt)
            while ((Get-Date) -lt $startAt) { Start-Sleep -Milliseconds 10 }
            & curl.exe -s -o NUL -w "%{http_code}" --max-time 180 -X POST `
                -H "Authorization: Bearer visionmaster" -H "X-Image-Name: board-01.png" `
                --data-binary "@$img" "http://127.0.0.1:19000/flow/Test_Comm"
        } -ArgumentList $img, $startAt
    }
    $codes = Receive-Job -Job $jobs -Wait
    Remove-Job -Job $jobs -Force
    $text = ($codes | ForEach-Object { ($_ | Out-String).Trim() }) -join '/'
    Write-Host "duplicate pair=$pair codes=$text"
    Start-Sleep -Seconds 1
}

Start-Sleep -Seconds 3
$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
$cpu0 = $proc.TotalProcessorTime
$mem0 = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$sw = [Diagnostics.Stopwatch]::StartNew()
$maxMem = $mem0
for ($i = 1; $i -le 150; $i++) {
    $r = Invoke-Flow 'Test_Vision'
    $code = ($r -split ',')[0]
    if ($code -ne '200') { Write-Host "vision#$i code=$code" }
    if ($i % 25 -eq 0) {
        $proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        if ($proc -ne $null) {
            $mem = [math]::Round($proc.WorkingSet64 / 1MB, 1)
            if ($mem -gt $maxMem) { $maxMem = $mem }
            Write-Host "vision $i/150 mem=$mem MB"
            if ($mem -gt $limitMb) {
                Write-Host 'MEMORY LIMIT -> KILL'
                Stop-Process -Id $p.Id -Force
                break
            }
        }
    }
}
$sw.Stop()
$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($proc -ne $null) {
    $cpu1 = $proc.TotalProcessorTime
    $elapsed = $sw.Elapsed.TotalSeconds
    $cpuPct = [math]::Round(100.0 * ($cpu1 - $cpu0).TotalSeconds / ($elapsed * [Environment]::ProcessorCount), 1)
    $mem1 = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    Write-Host ("longrun count=150 seconds=" + [math]::Round($elapsed, 2) + " rps=" + [math]::Round(150 / $elapsed, 1) + " cpu=" + $cpuPct + "% mem0=" + $mem0 + " mem1=" + $mem1 + " maxMem=" + $maxMem)
    Stop-Process -Id $p.Id -Force
}
Start-Sleep -Seconds 2
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Remove-Item $sol -Force
Write-Host 'config restored'
