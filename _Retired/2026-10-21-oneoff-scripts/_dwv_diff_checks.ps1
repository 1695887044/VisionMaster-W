# Multiset diff of check lines between baseline and after runs. ASCII-only per repo R1.
$ErrorActionPreference = 'Stop'
$n1 = '[' + [char]0x221A   # '[' + check mark
$n2 = '[' + [char]0xD7     # '[' + multiply sign (fail marker)
function Get-Checks($path) {
    $r = New-Object System.Collections.Generic.List[string]
    foreach ($l in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $t = $l.Trim()
        if ($t.StartsWith($n1) -or $t.StartsWith($n2)) { [void]$r.Add($t) }
    }
    , $r
}
$base = Get-Checks 'D:\C#\VM\_dwv_checks_baseline.out'
$af = Get-Checks 'D:\C#\VM\_dwv_checks_after.out'
$bc = @{}
foreach ($x in $base) { if ($bc.ContainsKey($x)) { $bc[$x]++ } else { $bc[$x] = 1 } }
$ac = @{}
foreach ($x in $af) { if ($ac.ContainsKey($x)) { $ac[$x]++ } else { $ac[$x] = 1 } }
$out = New-Object System.Collections.Generic.List[string]
$out.Add('base checks=' + $base.Count + '  after checks=' + $af.Count)
$out.Add('--- added in AFTER (not in BASELINE) ---')
foreach ($k in $ac.Keys) {
    $b = 0; if ($bc.ContainsKey($k)) { $b = $bc[$k] }
    if ($ac[$k] -gt $b) { $out.Add('+' + ($ac[$k] - $b) + ' | ' + $k) }
}
$out.Add('--- removed from BASELINE (not in AFTER) ---')
foreach ($k in $bc.Keys) {
    $a2 = 0; if ($ac.ContainsKey($k)) { $a2 = $ac[$k] }
    if ($bc[$k] -gt $a2) { $out.Add('-' + ($bc[$k] - $a2) + ' | ' + $k) }
}
[IO.File]::WriteAllLines('D:\C#\VM\_dwv_check_diff.txt', $out, (New-Object Text.UTF8Encoding($false)))
Write-Output ('wrote rows=' + $out.Count)
