# ASCII-only helper: diff the set of passing check names between two check logs.
# usage: powershell -File _diff_checks.ps1 <baselineLog> <newLog>
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$marker = ([IO.File]::ReadAllLines('D:\C#\VM\_grep_terms.txt', [Text.Encoding]::UTF8) |
    Where-Object { $_.Trim().Length -gt 0 })[0]

function Get-Names([string]$f) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Get-Content $f -Encoding UTF8)) {
        if ($l.Contains($marker)) {
            $i = $l.IndexOf($marker)
            $out.Add($l.Substring($i + $marker.Length).Trim())
        }
    }
    return $out
}

$b = Get-Names $args[0]
$a = Get-Names $args[1]
Write-Output ("baseline={0} lines, now={1} lines" -f $b.Count, $a.Count)
$d = Compare-Object $b $a
Write-Output ("diff entries = {0}   (Left=only in baseline, Right=only in now)" -f @($d).Count)
foreach ($x in $d) { Write-Output ("{0}  {1}" -f $x.SideIndicator, $x.InputObject) }
