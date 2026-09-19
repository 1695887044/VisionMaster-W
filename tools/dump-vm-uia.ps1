# Dump the UI Automation tree of the VisionMaster main window.
# Works even when the workstation is locked, because UIA queries are answered
# in-process by the app's automation providers (no screen pixels involved).
param(
    [int]$Depth = 6,
    [string]$Filter = ""
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $win) { throw "UIA: no top-level window for pid $($proc.Id)" }

"WINDOW name='$($win.Current.Name)' class='$($win.Current.ClassName)' rect=$($win.Current.BoundingRectangle)"

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$script:count = 0

function Walk($el, $level) {
    if ($null -eq $el -or $level -gt $Depth) { return }
    $script:count++
    $c = $el.Current
    $pad = "  " * $level
    $r = $c.BoundingRectangle
    $line = "{0}{1} | name='{2}' | cls={3} | rect={4},{5} {6}x{7} | vis={8}" -f `
        $pad, $c.ControlType.ProgrammaticName.Replace("ControlType.",""), $c.Name, $c.ClassName,
        [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $c.IsOffscreen
    if ([string]::IsNullOrEmpty($Filter) -or $line -match $Filter) { $line }

    $child = $walker.GetFirstChild($el)
    while ($null -ne $child) {
        Walk $child ($level + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Walk $win 0
"NODES=$($script:count)"
