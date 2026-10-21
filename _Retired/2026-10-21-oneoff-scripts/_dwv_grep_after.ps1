# Extract failures + [E13] section + summary from checks output (UTF-8). ASCII-only script per repo R1.
$ErrorActionPreference = 'Stop'
$src = 'D:\C#\VM\_dwv_checks_after.out'
$failDst = 'D:\C#\VM\_dwv_fail_lines_after.txt'
$e13Dst = 'D:\C#\VM\_dwv_e13_lines.txt'
$failNeedle = [string]([char]0x5931 + [char]0x8D25 + [char]0x5D)
$lines = [IO.File]::ReadAllLines($src, [Text.Encoding]::UTF8)
$failCount = 0
$fails = New-Object System.Collections.Generic.List[string]
$e13 = New-Object System.Collections.Generic.List[string]
$inE13 = $false
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains($failNeedle)) {
        $failCount++
        $fails.Add(([string]($i + 1)) + ' | ' + $lines[$i].Trim())
    }
    if ($lines[$i].StartsWith('----')) { $inE13 = $lines[$i].Contains('[E13]') }
    if ($inE13) { $e13.Add(([string]($i + 1)) + ' | ' + $lines[$i]) }
}
$fails.Add('')
$fails.Add('TOTAL_LINES=' + $lines.Length)
$fails.Add('FAIL_COUNT=' + $failCount)
[IO.File]::WriteAllLines($failDst, $fails, (New-Object Text.UTF8Encoding($false)))
$tail = New-Object System.Collections.Generic.List[string]
$tail.Add('')
$tail.Add('--- summary tail ---')
$start = [Math]::Max(0, $lines.Length - 8)
for ($i = $start; $i -lt $lines.Length; $i++) { $tail.Add($lines[$i]) }
[IO.File]::WriteAllLines($e13Dst, ($e13 + $tail), (New-Object Text.UTF8Encoding($false)))
Write-Output ('fail=' + $failCount + ' e13lines=' + $e13.Count + ' total=' + $lines.Length)
