# run_gates.ps1 - build each assertion host, then run it, capturing full log.
# ASCII only (R1). Chinese lives in the logs, not in this file.
$ErrorActionPreference = 'Continue'
$root = 'D:\C#\VM'
$log  = Join-Path $root 'artifacts\logs\20261022'
New-Item -ItemType Directory -Force -Path $log | Out-Null
Set-Location $root

$hosts = @(
  @{ N = 'CommChecks';         P = 'CommChecks\CommChecks.csproj' }
  @{ N = 'DataPipelineChecks'; P = 'DataPipelineChecks\DataPipelineChecks.csproj' }
  @{ N = 'ScadaChecks';        P = 'ScadaChecks\ScadaChecks.csproj' }
  @{ N = 'UIThemeSmokeTest';   P = 'UIThemeSmokeTest\UIThemeSmokeTest.csproj' }
  @{ N = 'HelpProbe';          P = 'HelpProbe\HelpProbe.csproj' }
  @{ N = 'FlowCanvasChecks';   P = 'FlowCanvasChecks\FlowCanvasChecks.csproj' }
)

foreach ($h in $hosts) {
  Write-Host ('=== ' + $h.N + ' : build ===')
  $bout = Join-Path $log ('build-' + $h.N + '.log')
  dotnet build $h.P -m:1 -nodeReuse:false *> $bout
  $buildExit = $LASTEXITCODE
  if ($buildExit -ne 0) {
    Write-Host ('BUILD FAILED exit=' + $buildExit + ' log=' + $bout)
    continue
  }

  $exe = Join-Path $root ($h.N + '\bin\Debug\net9.0-windows\' + $h.N + '.exe')
  if (-not (Test-Path $exe)) {
    Write-Host ('EXE MISSING: ' + $exe)
    continue
  }
  $stamp = (Get-Item $exe).LastWriteTime.ToString('HH:mm:ss')

  Write-Host ('=== ' + $h.N + ' : run (exe ' + $stamp + ') ===')
  $out = Join-Path $log ('gate-' + $h.N + '.log')
  & $exe *> $out
  Write-Host ('exit=' + $LASTEXITCODE + '  log=' + $out)
}
Write-Host 'ALL DONE'
