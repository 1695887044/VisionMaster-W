# Run ScadaChecks.exe, capture raw bytes to log (no pipeline: R183).
$ErrorActionPreference = 'Continue'
$exe = 'D:\C#\VM\ScadaChecks\bin\Debug\net9.0-windows\ScadaChecks.exe'
$logDir = 'D:\C#\VM\artifacts\logs\20261025'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
$log = Join-Path $logDir 'scadachecks-ax12-run2.log'

if (-not (Test-Path $exe)) { 'EXE MISSING'; exit 127 }
$exeTime = (Get-Item $exe).LastWriteTime
$srcTime = (Get-Item 'D:\C#\VM\ScadaChecks\AxisPanelChecks.cs').LastWriteTime
if ($exeTime -lt $srcTime) { 'STALE EXE: ' + $exeTime + ' < ' + $srcTime; exit 1 }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = 'D:\C#\VM\ScadaChecks\bin\Debug\net9.0-windows'
$proc = [System.Diagnostics.Process]::Start($psi)
$outStream = [System.IO.File]::Create($log)
$outTask = $proc.StandardOutput.BaseStream.CopyToAsync($outStream)
$errTask = $proc.StandardError.ReadToEndAsync()
if (-not $proc.WaitForExit(240000)) { try { $proc.Kill() } catch {} ; 'TIMEOUT'; exit 124 }
$outTask.Wait(10000) | Out-Null
$err = $errTask.Result
$outStream.Close()

'EXE=' + $exeTime.ToString('HH:mm:ss') + ' SRC=' + $srcTime.ToString('HH:mm:ss')
'EXITCODE=' + $proc.ExitCode
'LOG=' + $log + ' BYTES=' + (Get-Item $log).Length
if ($err) { 'STDERR: ' + $err.Substring(0, [Math]::Min(500, $err.Length)) }
