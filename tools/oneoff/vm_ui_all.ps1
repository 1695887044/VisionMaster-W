param(
    [ValidateSet('list', 'texts', 'invoke', 'row')][string]$Action = 'list',
    [string]$Name = '',
    [int]$Index = 0,
    [Parameter(Mandatory = $true)][string]$OutFile
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class UiaDpi2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][UiaDpi2]::SetProcessDPIAware()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1

function Esc([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $c = [int]$ch
        if ($c -lt 32 -or ($c -ge 0xE000 -and $c -le 0xF8FF)) {
            [void]$sb.Append('\u' + $c.ToString('X4'))
        } else {
            [void]$sb.Append($ch)
        }
    }
    return $sb.ToString()
}

function Get-Windows {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $cond)
}

$lines = New-Object System.Collections.ArrayList
[void]$lines.Add('PID=' + $proc.Id + ' WINDOWS=' + (Get-Windows).Count)

function Walk($el, [int]$level, [bool]$textOnly) {
    if ($null -eq $el) { return }
    try {
        $c = $el.Current
        $type = $c.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $nm = Esc $c.Name
        if (-not $textOnly -or $type -eq 'Text' -or $type -eq 'Button' -or $type -eq 'ListItem') {
            $r = $c.BoundingRectangle
            [void]$lines.Add(('  ' * $level) + $type + ' | ' + $nm +
                ' | id=' + $c.AutomationId +
                ' | rect=' + [int]$r.X + ',' + [int]$r.Y + ',' + [int]$r.Width + ',' + [int]$r.Height +
                ' | off=' + $c.IsOffscreen + ' | en=' + $c.IsEnabled)
        }
    } catch { }
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    try {
        $child = $walker.GetFirstChild($el)
        while ($null -ne $child) {
            Walk $child ($level + 1) $textOnly
            $child = $walker.GetNextSibling($child)
        }
    } catch { }
}

$windows = Get-Windows

if ($Action -eq 'invoke' -or $Action -eq 'row') {
    $targets = New-Object System.Collections.ArrayList
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    foreach ($w in $windows) {
        Walk $w 0 $true
        $all = New-Object System.Collections.ArrayList
        $stack = New-Object System.Collections.Stack
        $stack.Push($w)
        while ($stack.Count -gt 0) {
            $el = $stack.Pop()
            [void]$all.Add($el)
            $child = $walker.GetFirstChild($el)
            while ($null -ne $child) { $stack.Push($child); $child = $walker.GetNextSibling($child) }
        }
        foreach ($el in $all) {
            try {
                $c = $el.Current
                if ($c.Name -eq $Name -or $c.AutomationId -eq $Name) { [void]$targets.Add($el) }
            } catch { }
        }
    }
    if ($targets.Count -eq 0) {
        [void]$lines.Add('NOT FOUND: ' + (Esc $Name))
    } else {
        $picked = $targets[[Math]::Min($Index, $targets.Count - 1)]
        if ($Action -eq 'row') {
            # Walk up from every match until one has an invokable ancestor (popup row Button).
            # The same flow name also appears in the left flow list (ListItem, not invokable).
            $done = $false
            for ($ti = 0; $ti -lt $targets.Count -and -not $done; $ti++) {
                $cursor = $targets[$ti]
                while ($null -ne $cursor) {
                    try {
                        $pattern = $null
                        if ($cursor.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
                            $pattern.Invoke()
                            [void]$lines.Add('ROW INVOKED ' + (Esc $Name) + ' target=' + $ti + '/' + $targets.Count)
                            $done = $true
                            break
                        }
                    } catch { }
                    try { $cursor = $walker.GetParent($cursor) } catch { $cursor = $null }
                }
            }
            if (-not $done) {
                [void]$lines.Add('ROW INVOKE FAILED: no invokable ancestor for ' + (Esc $Name) + ' targets=' + $targets.Count)
            }
        } else {
            try {
                $pat = $picked.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $pat.Invoke()
                [void]$lines.Add('INVOKED ' + (Esc $Name) + ' index=' + $Index + ' targets=' + $targets.Count)
            } catch {
                [void]$lines.Add('INVOKE FAILED: ' + $_.Exception.Message)
            }
        }
    }
} else {
    $textOnly = ($Action -eq 'texts')
    foreach ($w in $windows) {
        Walk $w 0 $textOnly
    }
}

[IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output ('OUT=' + $OutFile + ' lines=' + $lines.Count)
