[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = 'https://raw.githubusercontent.com/ScottPlot/ScottPlot/main/src/ScottPlot5/ScottPlot5/Plottables/'
foreach ($f in 'Pie.cs', 'BarPlot.cs') {
    try {
        $c = (Invoke-WebRequest -Uri ($base + $f) -UseBasicParsing).Content
        [IO.File]::WriteAllText(('D:\C#\VM\_ApiDump\' + $f), $c, [Text.Encoding]::UTF8)
        Write-Output ('OK ' + $f + ' (' + $c.Length + ' chars)')
    } catch {
        Write-Output ('FAIL ' + $f + ' ' + $_.Exception.Message)
    }
}
