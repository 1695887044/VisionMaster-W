param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Dest,
    [Parameter(Mandatory = $true)][string]$Map
)

$text = [IO.File]::ReadAllText($Source, [Text.Encoding]::UTF8)
$mapping = [IO.File]::ReadAllText($Map, [Text.Encoding]::UTF8) | ConvertFrom-Json

$miss = 0
foreach ($p in $mapping.PSObject.Properties) {
    $flow = $p.Name
    $var = [string]$p.Value
    $needle = '"FlowName": "' + $flow + '",'
    $idx = $text.IndexOf($needle, [StringComparison]::Ordinal)
    if ($idx -lt 0) {
        Write-Output ('MISS ' + $flow)
        $miss++
        continue
    }

    $lineStart = $text.LastIndexOf("`n", $idx) + 1
    $indent = $text.Substring($lineStart, $idx - $lineStart)
    $eol = $text.IndexOf("`n", $idx)
    if ($eol -lt 0) { Write-Output ('NO-EOL ' + $flow); $miss++; continue }

    $block = $indent + '"ResultSource": 2,' + "`r`n" +
             $indent + '"ResultVariableName": "' + $var + '",' + "`r`n"
    $text = $text.Insert($eol + 1, $block)
    Write-Output ('OK ' + $flow + ' -> ' + $var)
}

[IO.File]::WriteAllText($Dest, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output ('WROTE ' + $Dest + ' bytes=' + (Get-Item -LiteralPath $Dest).Length + ' miss=' + $miss)
