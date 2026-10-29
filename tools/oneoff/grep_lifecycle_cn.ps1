param([string]$Root = 'D:\C#\VM\docs')
$pat = [string][char]0x751F + [char]0x547D + [char]0x5468 + [char]0x671F   # lifecycle in Chinese
Get-ChildItem -LiteralPath $Root -Recurse -Filter *.md -ErrorAction SilentlyContinue | ForEach-Object {
    $lines = [IO.File]::ReadAllLines($_.FullName, [Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Contains($pat)) {
            Write-Output ($_.FullName.Replace('D:\C#\VM\', '') + ':' + ($i + 1) + ': ' + $lines[$i].Trim())
        }
    }
}
