$ErrorActionPreference = 'Stop'
$out = & dotnet run --project 'D:\C#\VM\UIThemeSmokeTest\UIThemeSmokeTest.csproj' --no-build 2>&1 | Out-String
$lines = $out -split "`r?`n"
$failIdx = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '\[FAIL\]') { $failIdx += $i }
}
Write-Output ("TOTAL_FAIL_LINES=" + $failIdx.Count)
foreach ($i in $failIdx) {
    Write-Output ("  " + $lines[$i])
    if ($i + 1 -lt $lines.Count -and $lines[$i + 1].Trim().Length -gt 0) {
        Write-Output ("    -> " + $lines[$i + 1].Trim())
    }
}
Write-Output '---LOG_CONSOLE_BLOCK---'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'LogConsole') { $start = $i; break }
}
if ($start -ge 0) {
    $end = [Math]::Min($lines.Count - 1, $start + 26)
    for ($i = $start; $i -le $end; $i++) { Write-Output $lines[$i] }
}
else {
    Write-Output '(no LogConsole block found)'
}
