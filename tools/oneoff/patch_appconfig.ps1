param(
    [Parameter(Mandatory = $true)][string]$Config,
    [Parameter(Mandatory = $true)][string]$SolutionPath
)

$text = [IO.File]::ReadAllText($Config, [Text.Encoding]::UTF8)
$json = $SolutionPath.Replace('\', '\\')

$script:count = 0
$eval = {
    param($m)
    $script:count++
    $m.Groups[1].Value + $json + $m.Groups[2].Value
}

$text = [regex]::Replace($text, '("StartupSolutionPath"\s*:\s*")[^"]*(")', $eval)
$text = [regex]::Replace($text, '("Path"\s*:\s*")[^"]*\.vms(")', $eval)

[IO.File]::WriteAllText($Config, $text, (New-Object Text.UTF8Encoding($true)))
Write-Output ('PATCHED replacements=' + $script:count)
