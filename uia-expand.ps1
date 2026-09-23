Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
"TOPWINDOWS=$($wins.Count)"
foreach ($w in $wins) { "W | '{0}' | {1} | {2}" -f $w.Current.Name, $w.Current.ControlType.ProgrammaticName, $w.Current.ClassName }
# 展开左上角菜单
$miCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
$mi = $wins[0].FindFirst([System.Windows.Automation.TreeScope]::Descendants, $miCond)
if ($mi) {
  $pat = $mi.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $pat.Expand()
  Start-Sleep -Milliseconds 1200
  "EXPANDED"
}
$wins2 = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
"TOPWINDOWS2=$($wins2.Count)"
foreach ($w in $wins2) { "W2 | '{0}' | {1} | {2}" -f $w.Current.Name, $w.Current.ControlType.ProgrammaticName, $w.Current.ClassName }
foreach ($w in $wins2) {
  $items = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $miCond)
  foreach ($i in $items) { "  MI | '{0}' | {1}" -f $i.Current.Name, $i.Current.ClassName }
}
