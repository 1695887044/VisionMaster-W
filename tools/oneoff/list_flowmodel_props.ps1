param([string]$Path = 'D:\C#\VM\Core\Models\FlowModel.cs')
$lines = [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^\s*public\s+[\w\.<>,\[\]\?\s]+\s+(\w+)\s*(\{|=>)') {
        Write-Output (($i + 1).ToString() + ': ' + $line.Trim())
    }
}
