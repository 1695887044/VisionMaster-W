$lines = [IO.File]::ReadAllLines('D:\C#\VM\_render_out\gate2.log', [Text.Encoding]::UTF8)
$pass = ($lines | Select-String -Pattern '通过').Count
$fail = $lines | Select-String -Pattern '✗|未通过|\[FAIL\]'
Write-Host "pass-lines=$pass"
if ($fail) { Write-Host "FAIL=$($fail.Count)"; $fail | Select-Object -First 5 | ForEach-Object { $_.Line } } else { Write-Host 'no fail lines' }
Write-Host '--- around MotionZMotion ---'
$idx = ($lines | Select-String -Pattern 'MotionZMotion').LineNumber
if ($idx) { Write-Host "MotionZMotion first at line $idx / $($lines.Count)" }
