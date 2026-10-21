Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
$all = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($w in $all) {
  $p = $w.Current.ProcessId
  if ($p -eq $proc.Id) { "MINE | '{0}' | {1} | pid={2}" -f $w.Current.Name, $w.Current.ClassName, $p }
}
"TOTAL=$($all.Count)"
# 主窗口里找「运行窗口设置」文字，看弹窗是不是作为子元素挂在主窗下
$w = $null
foreach ($x in $all) { if ($x.Current.ProcessId -eq $proc.Id -and $x.Current.Name -eq 'VisionMaster') { $w = $x; break } }
if ($w) {
  $t = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
  foreach ($i in $t) { if ($i.Current.Name -match '运行窗口|落在哪块屏|跟随主屏|依附主窗口|独立窗口') { "TXT | '{0}'" -f $i.Current.Name } }
}
