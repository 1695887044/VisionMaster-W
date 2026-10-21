[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$u = 'https://raw.githubusercontent.com/ScottPlot/ScottPlot/v5.1.59/src/ScottPlot5/ScottPlot5/Interactivity/UserInputProcessor.cs'
$c = (Invoke-WebRequest -Uri $u -UseBasicParsing).Content
[IO.File]::WriteAllText('D:\C#\VM\_ApiDump\uip.cs', $c, [Text.Encoding]::UTF8)
Write-Output 'written'
