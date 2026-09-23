Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
$w = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$cbCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
$cbs = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
"COMBOCOUNT=$($cbs.Count)"
$i = 0
foreach ($cb in $cbs) {
  $i++
  $sel = ''
  try { $s = $cb.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection(); if ($s.Length -gt 0) { $sel = $s[0].Current.Name } } catch {}
  "COMBO#$i name='{0}' cls={1} selection='{2}'" -f $cb.Current.Name, $cb.Current.ClassName, $sel
  $items = $cb.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  "  items=$($items.Count)"
  foreach ($it in $items) { "    LI | '{0}' | selected={1}" -f $it.Current.Name, $it.GetCurrentPropertyValue([System.Windows.Automation.SelectionItemPattern]::IsSelectedProperty) }
}
