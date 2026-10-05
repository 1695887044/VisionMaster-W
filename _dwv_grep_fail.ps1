# Extract failed assertion lines from a checks output file (UTF-8). ASCII-only script per repo R1.
# Needle = U+5931 U+8D25 + "]" (i.e. the failure marker suffix "失败]"), built from code points so this file stays pure ASCII.
$ErrorActionPreference = 'Stop'
$src = 'D:\C#\VM\_dwv_checks_baseline.out'
$dst = 'D:\C#\VM\_dwv_fail_lines_baseline.txt'
$needle = [string]([char]0x5931 + [char]0x8D25 + [char]0x5D)
$lines = [IO.File]::ReadAllLines($src, [Text.Encoding]::UTF8)
$failCount = 0
$hits = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].Contains($needle)) {
        $failCount++
        $hits.Add(([string]($i + 1)) + ' | ' + $lines[$i].Trim())
    }
}
$hits.Add('')
$hits.Add('TOTAL_LINES=' + $lines.Length)
$hits.Add('FAIL_COUNT=' + $failCount)
[IO.File]::WriteAllLines($dst, $hits, (New-Object Text.UTF8Encoding($false)))
Write-Output ('WROTE rows=' + $hits.Count + ' fail=' + $failCount)
