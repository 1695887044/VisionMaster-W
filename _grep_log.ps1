# ASCII-only helper: per-term hit counts + first N matched lines of a log file.
# usage: powershell -File _grep_log.ps1 <logfile>
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$file = $args[0]
$terms = [IO.File]::ReadAllLines('D:\C#\VM\_grep_terms.txt', [Text.Encoding]::UTF8) |
    Where-Object { $_.Trim().Length -gt 0 }
foreach ($t in $terms) {
    $hits = @(Select-String -Path $file -SimpleMatch -Pattern $t -Encoding UTF8)
    Write-Output ("--- TERM [{0}] hits={1}" -f $t, $hits.Count)
    foreach ($h in ($hits | Select-Object -First 30)) {
        Write-Output ("{0}: {1}" -f $h.LineNumber, $h.Line)
    }
}
