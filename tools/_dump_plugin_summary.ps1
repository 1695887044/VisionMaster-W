param([string]$In = "D:\C#\VM\artifacts\plugins.json", [string]$Out = "D:\C#\VM\artifacts\plugins-summary.txt")
$raw = [IO.File]::ReadAllText($In, [Text.Encoding]::UTF8)
$list = $raw | ConvertFrom-Json
$sb = New-Object Text.StringBuilder
foreach ($p in $list) {
    if ($null -eq $p.type) { [void]$sb.AppendLine("DLL-ERR " + $p.dll + " " + $p.loadError); continue }
    [void]$sb.AppendLine("### " + $p.display + " | " + $p.group + " | " + $p.type)
    [void]$sb.AppendLine("  asm: " + $p.assemblyQualifiedName)
    if ($p.instantiateError) { [void]$sb.AppendLine("  INST-ERR: " + $p.instantiateError) }
    $ins = @()
    foreach ($i in $p.inputs) { $ins += ($i.name + ":" + $i.type + "=" + $i.value) }
    [void]$sb.AppendLine("  IN: " + ($ins -join "; "))
    $outs = @()
    foreach ($o in $p.outputs) { $outs += ($o.name + ":" + $o.type) }
    [void]$sb.AppendLine("  OUT: " + ($outs -join "; "))
    $cfg = @()
    foreach ($c in $p.configs) { $cfg += ($c.name + ":" + $c.type + "=" + $c.defaultValue + $(if ($c.options) { "[" + $c.options + "]" } else { "" })) }
    [void]$sb.AppendLine("  CFG: " + ($cfg -join "; "))
    [void]$sb.AppendLine("")
}
[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Host ("WROTE " + $Out)
