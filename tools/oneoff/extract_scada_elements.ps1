# Extract TypeKey / DisplayName / Category / Events from BuiltInElements.cs.
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Out
)

$text = [IO.File]::ReadAllText($Src, [Text.Encoding]::UTF8)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('TypeKey|DisplayName|Category|Events|Description')

$blocks = [regex]::Split($text, '\r?\n\s*new\(\)\s*\r?\n\s*\{')
for ($i = 1; $i -lt $blocks.Count; $i++) {
    $b = $blocks[$i]
    $key = [regex]::Match($b, 'TypeKey\s*=\s*"([^"]*)"')
    if (-not $key.Success) { continue }
    $name = [regex]::Match($b, 'DisplayName\s*=\s*"([^"]*)"')
    $cat = [regex]::Match($b, 'Category\s*=\s*"([^"]*)"')
    $desc = [regex]::Match($b, 'Description\s*=\s*"([^"]*)"')
    $ev = [regex]::Match($b, '(?s)Events\s*=\s*(.{0,400}?),\s*\r?\n\s*(?:Description|ReadOnlyKeys|ShowInToolbox|Properties)')
    $evText = ''
    if ($ev.Success) {
        $raw = $ev.Groups[1].Value
        $names = [regex]::Matches($raw, 'ScadaEventType\.(\w+)') | ForEach-Object { $_.Groups[1].Value }
        if ($names.Count -gt 0) { $evText = ($names -join ',') }
        elseif ($raw -match 'new\[\]|Array\.Empty|new ScadaEventType\[0\]') { $evText = '' }
        else { $evText = 'EXPR:' + ($raw -replace '\s+', ' ') }
    }
    $line = @(
        $key.Groups[1].Value,
        $(if ($name.Success) { $name.Groups[1].Value } else { '' }),
        $(if ($cat.Success) { $cat.Groups[1].Value } else { '' }),
        $evText,
        $(if ($desc.Success) { $desc.Groups[1].Value } else { '' })
    ) -join '|'
    [void]$sb.AppendLine($line)
}

[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Output ('ROWS=' + ($blocks.Count - 1))
