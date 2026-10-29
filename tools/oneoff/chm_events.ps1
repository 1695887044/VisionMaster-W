# Extract the trailing "events" section of each converted control page.
# Usage: powershell -ExecutionPolicy Bypass -File chm_events.ps1 -Dir <txtdir> -List <list> -Out <report>
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [Parameter(Mandatory = $true)][string]$List,
    [Parameter(Mandatory = $true)][string]$Out
)

$lines = [IO.File]::ReadAllLines($List, [Text.Encoding]::UTF8)
$sb = New-Object System.Text.StringBuilder

foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line.Split('|')
    if ($parts.Count -lt 2) { continue }
    $title = $parts[0].Trim()
    $id = $parts[1].Trim()
    $f = Join-Path $Dir ($id + '.txt')
    if (-not (Test-Path $f)) { continue }
    $body = [IO.File]::ReadAllLines($f, [Text.Encoding]::UTF8)

    # Locate the last paragraph that introduces the events dialog.
    $start = -1
    for ($i = 0; $i -lt $body.Count; $i++) {
        if ($body[$i] -match '^4\.' -and $body[$i] -match '\u4e8b\u4ef6') { $start = $i; break }
    }
    if ($start -lt 0) {
        for ($i = 0; $i -lt $body.Count; $i++) {
            if ($body[$i] -match '\u4e8b\u4ef6\u300d\u5bf9\u8bdd\u6846') { $start = $i; break }
        }
    }

    [void]$sb.AppendLine('===== ' + $title + ' (' + $id + ') =====')
    if ($start -lt 0) {
        [void]$sb.AppendLine('(no explicit event dialog section)')
    }
    else {
        for ($i = $start; $i -lt $body.Count; $i++) {
            $t = $body[$i]
            if ([string]::IsNullOrWhiteSpace($t)) { continue }
            if ($t.Trim() -eq '|') { continue }
            [void]$sb.AppendLine($t)
        }
    }
    [void]$sb.AppendLine('')
}

[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Output ('OUT=' + $Out)
