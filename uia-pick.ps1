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
$picked = $null
foreach ($win in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  foreach ($it in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $liCond)) {
    $txt = ($it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tCond) | ForEach-Object { $_.Current.Name }) -join ' | '
    if ($txt -match '显示器 1' -and -not $picked) { $picked = $it }
  }
}
if (-not $picked) { throw '显示器 1 选项没找到' }
$picked.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 800
$s = $cb.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
"SEL_NOW=" + (($s | ForEach-Object { $_.Current.Name }) -join ',')
$bCond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
  (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, '确定')))
$ok = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bCond)
if (-not $ok) { throw '确定 按钮没找到' }
$ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500
"CLICKED_OK"
