$ErrorActionPreference = 'Stop'
$dll = 'C:\Users\16958\.nuget\packages\dirkster.avalondock\4.74.1\lib\net48\AvalonDock.dll'
$a = [Reflection.Assembly]::LoadFrom($dll)
Write-Output ('asm=' + $a.FullName)
$t = $a.GetType('AvalonDock.Layout.ILayoutPreviousContainer')
if ($null -eq $t) {
    Write-Output 'iface=(not found)'
} else {
    Write-Output ('iface public=' + $t.IsPublic + ' nested=' + $t.IsNestedPublic + ' notpublic=' + $t.IsNotPublic)
}
$lc = $a.GetType('AvalonDock.Layout.LayoutContent')
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
foreach ($p in $lc.GetProperties($flags)) {
    if ($p.Name -like 'Previous*') {
        $g = 'null'; $s = 'null'
        if ($p.GetMethod) { $g = $p.GetMethod.Name + ':' + $p.GetMethod.Attributes }
        if ($p.SetMethod) { $s = $p.SetMethod.Name + ':' + $p.SetMethod.Attributes }
        Write-Output ('prop ' + $p.Name + ' | get ' + $g + ' | set ' + $s)
    }
}
foreach ($f in $lc.GetFields($flags)) {
    if ($f.Name -like '*Previous*') { Write-Output ('field ' + $f.Name + ' ' + $f.Attributes) }
}
