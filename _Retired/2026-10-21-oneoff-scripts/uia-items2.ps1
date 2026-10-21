Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$proc = Get-Process VisionMaster | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
$w = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$cbCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
$cb = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)[0]
try { $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() } catch {}
Start-Sleep -Milliseconds 1200
$liCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$tCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$n = 0
foreach ($win in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  foreach ($it in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $liCond)) {
    $txt = ($it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tCond) | ForEach-Object { $_.Current.Name }) -join ' | '
    if ($txt -match '跟随主屏|显示器') { $n++; "ITEM#$n sel={0} :: {1}" -f $it.GetCurrentPropertyValue([System.Windows.Automation.SelectionItemPattern]::IsSelectedProperty), $txt }
  }
}
