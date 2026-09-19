# Minimal UI Automation driver for VisionMaster (works while the display is not rendering).
#
# Why UIA: CopyFromScreen returns pure black when the session has no active monitor,
# and PrintWindow only yields a blank surface for WPF windows. UIA, by contrast, is
# answered in-process by the app's automation providers, so it keeps working.
#
# Usage:
#   powershell -File tools/vm-uia.ps1 -Action list
#   powershell -File tools/vm-uia.ps1 -Action invoke -Name "運行画面"
#   powershell -File tools/vm-uia.ps1 -Action texts
param(
    [ValidateSet('list', 'invoke', 'texts', 'click')]
    [string]$Action = 'list',
    [string]$Name = '',
    [string]$Class = '',
    [int]$X = 0,
    [int]$Y = 0
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$script:proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1

function Get-Root {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:proc.Id)
    $r = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -eq $r) { throw "no top-level window for pid $($script:proc.Id)" }
    return $r
}

function Get-All($root) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $out = New-Object System.Collections.ArrayList
    function Walk($el, $level) {
        if ($null -eq $el) { return }
        [void]$out.Add([pscustomobject]@{ El = $el; Level = $level })
        $c = $walker.GetFirstChild($el)
        while ($null -ne $c) { Walk $c ($level + 1); $c = $walker.GetNextSibling($c) }
    }
    Walk $root 0
    return $out
}

$root = Get-Root
$all = Get-All $root

switch ($Action) {
    'list' {
        foreach ($n in $all) {
            $c = $n.El.Current
            $r = $c.BoundingRectangle
            "{0}{1} | '{2}' | {3} | {4},{5} {6}x{7}" -f ("  " * $n.Level),
                $c.ControlType.ProgrammaticName.Replace("ControlType.", ""), $c.Name, $c.ClassName,
                [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
        }
        "NODES=$($all.Count)"
    }
    'texts' {
        foreach ($n in $all) {
            $c = $n.El.Current
            if ($c.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $c.Name) {
                "TEXT='{0}' @ {1},{2}" -f $c.Name, [int]$c.BoundingRectangle.X, [int]$c.BoundingRectangle.Y
            }
        }
    }
    'invoke' {
        $hit = $all | Where-Object {
            $_.El.Current.Name -eq $Name -and
            ($_.El.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button)
        }
        if (-not $hit) { throw "button '$Name' not found" }
        $el = $hit[0].El
        $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pat.Invoke()
        "INVOKED '$Name'"
    }
    'click' {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Mouse {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
'@
        [void][Mouse]::SetProcessDPIAware()
        [void][Mouse]::SetCursorPos($X, $Y)
        Start-Sleep -Milliseconds 120
        [Mouse]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)  # LEFTDOWN
        Start-Sleep -Milliseconds 60
        [Mouse]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)  # LEFTUP
        "CLICKED $X,$Y"
    }
}
