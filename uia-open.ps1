Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
function Top($cond) { $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) }
function MenuItem($root, $name) {
  $c = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)),
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
$w = Top $cond | Where-Object { $_.Current.Name -eq 'VisionMaster' } | Select-Object -First 1
$top = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
$top.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 1000
$sys = $null
foreach ($win in Top $cond) { $sys = MenuItem $win '系统(S)'; if ($sys) { break } }
if (-not $sys) { throw '系统(S) not found' }
$sys.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 1000
$target = $null
foreach ($win in Top $cond) { $target = MenuItem $win '运行窗口设置'; if ($target) { break } }
if (-not $target) {
  "MISS: 系统(S) 下没找到「运行窗口设置」，现有项："
  foreach ($win in Top $cond) { foreach ($i in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))) { "  '{0}'" -f $i.Current.Name } }
  exit 1
}
$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500
"INVOKED 运行窗口设置"
$cond2 = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
foreach ($win in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond2)) {
  "WIN | '{0}' | {1}" -f $win.Current.Name, $win.Current.ClassName
}
