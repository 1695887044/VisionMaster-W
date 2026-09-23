Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
$w = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$cbCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
$cb = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)[0]
$cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 1200
foreach ($win in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  $items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  foreach ($it in $items) { "LI | '{0}' | sel={1}" -f $it.Current.Name, $it.GetCurrentPropertyValue([System.Windows.Automation.SelectionItemPattern]::IsSelectedProperty) }
}
