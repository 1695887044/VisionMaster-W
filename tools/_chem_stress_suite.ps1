# Chemical demo stress suite (ASCII only, per repo R1)
$exe = "D:\C#\VM\Web\ScadaWebChemicalDemo\bin\Release\net9.0-windows\ScadaWebChemicalDemo.exe"
$tiers = @( @(10,10), @(30,10), @(50,10), @(100,10), @(100,20) )

Get-Process ScadaWebChemicalDemo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

foreach ($tier in $tiers) {
    $c = $tier[0]
    $n = $tier[1]
    $log = "D:\C#\VM\_chem_final_${c}x${n}.log"
    Write-Output "=== clients=$c commands=$n"
    & $exe --stress --clients $c --commands $n --page-ms 1200 --holdMs 4000 2>&1 | Out-File -FilePath $log -Encoding UTF8
    Get-Content $log -Encoding UTF8 | Select-String -Pattern "RESULT|LATENCY|MEMORY" | ForEach-Object { $_.Line }
}
