# ASCII-only helper: search Chinese terms (from a UTF-8 list file) across repo sources.
# Usage: powershell -File _grep_terms.ps1
$ErrorActionPreference = 'Stop'
$root = 'D:\C#\VM'
$terms = [IO.File]::ReadAllLines((Join-Path $root '_grep_terms.txt'), [Text.Encoding]::UTF8) |
    Where-Object { $_.Trim().Length -gt 0 }
$files = @()
$files += Get-ChildItem -Path (Join-Path $root 'FlowCanvasChecks') -Filter *.cs -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$files += Get-ChildItem -Path (Join-Path $root 'Plugins\Plugin.BeadInspect') -Include *.cs, *.xaml -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$files += Get-Item (Join-Path $root '_BeadViewProbe\Program.cs')
$files += Get-ChildItem -Path (Join-Path $root 'UI\Controls\Themes') -Include *.xaml -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

foreach ($t in $terms) {
    Write-Output "===== TERM: $t ====="
    foreach ($f in $files) {
        $hits = Select-String -Path $f.FullName -SimpleMatch -Pattern $t -Encoding UTF8
        foreach ($h in $hits) {
            Write-Output ("{0}:{1}: {2}" -f $f.Name, $h.LineNumber, $h.Line.Trim())
        }
    }
}
