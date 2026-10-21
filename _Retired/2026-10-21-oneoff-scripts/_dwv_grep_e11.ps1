# Extract [E11] section from after-run output + baseline summary tail. ASCII-only per repo R1.
$ErrorActionPreference = 'Stop'
$after = [IO.File]::ReadAllLines('D:\C#\VM\_dwv_checks_after.out', [Text.Encoding]::UTF8)
$base = [IO.File]::ReadAllLines('D:\C#\VM\_dwv_checks_baseline.out', [Text.Encoding]::UTF8)
$out = New-Object System.Collections.Generic.List[string]
$in = $false
for ($i = 0; $i -lt $after.Length; $i++) {
    if ($after[$i].StartsWith('----')) { $in = $after[$i].Contains('[E11]') }
    if ($in) { $out.Add(([string]($i + 1)) + ' | ' + $after[$i]) }
}
$out.Add('')
$out.Add('--- baseline summary tail ---')
$s = [Math]::Max(0, $base.Length - 6)
for ($i = $s; $i -lt $base.Length; $i++) { $out.Add($base[$i]) }
[IO.File]::WriteAllLines('D:\C#\VM\_dwv_e11_lines.txt', $out, (New-Object Text.UTF8Encoding($false)))
Write-Output ('rows=' + $out.Count)
