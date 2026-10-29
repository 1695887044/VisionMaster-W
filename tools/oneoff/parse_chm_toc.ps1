# Parse a CHM .hhc table of contents into an indented text file.
# Usage: powershell -ExecutionPolicy Bypass -File parse_chm_toc.ps1 -Hhc <path> -Out <path>
param(
    [Parameter(Mandatory = $true)][string]$Hhc,
    [Parameter(Mandatory = $true)][string]$Out
)

$enc = [Text.Encoding]::GetEncoding(936)
$text = [IO.File]::ReadAllText($Hhc, $enc)

$pattern = '(?is)<LI>\s*<OBJECT.*?</OBJECT>|<UL>|</UL>'
$depth = 0
$lines = New-Object System.Collections.Generic.List[string]

foreach ($m in [regex]::Matches($text, $pattern)) {
    $v = $m.Value
    if ($v -match '(?is)^<UL>') { $depth++; continue }
    if ($v -match '(?is)^</UL>') { $depth--; continue }

    $name = ''
    $local = ''
    $n = [regex]::Match($v, 'name="Name"\s+value="([^"]*)"')
    if ($n.Success) { $name = $n.Groups[1].Value }
    $l = [regex]::Match($v, 'name="Local"\s+value="([^"]*)"')
    if ($l.Success) { $local = $l.Groups[1].Value }

    if ($depth -lt 0) { $depth = 0 }
    $lines.Add(('  ' * $depth) + $name + ' || ' + $local)
}

[IO.File]::WriteAllLines($Out, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output ('LINES=' + $lines.Count)
