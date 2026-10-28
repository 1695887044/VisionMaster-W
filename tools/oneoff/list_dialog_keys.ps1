param([string]$Dir = 'D:\C#\VM\UI\Controls\Themes\Dialog')
$quote = [string][char]0x22
Get-ChildItem -LiteralPath $Dir -Filter *.xaml | ForEach-Object {
    $file = $_.Name
    $lines = [IO.File]::ReadAllLines($_.FullName, [Text.Encoding]::UTF8)
    foreach ($line in $lines) {
        $m = [regex]::Match($line, 'x:Key=' + $quote + '([^' + $quote + ']+)' + $quote)
        if ($m.Success) { Write-Output ($file + ' :: ' + $m.Groups[1].Value) }
    }
}
