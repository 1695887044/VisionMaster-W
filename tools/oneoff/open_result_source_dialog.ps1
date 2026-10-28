param(
    [Parameter(Mandatory = $true)][string]$FlowName,
    [Parameter(Mandatory = $true)][string]$OutFile
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class RsUi {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public struct POINT { public int X; public int Y; }
    public static long PackClient(IntPtr hWnd, int screenX, int screenY) {
        POINT p;
        p.X = screenX;
        p.Y = screenY;
        ScreenToClient(hWnd, ref p);
        uint packed = ((uint)p.Y << 16) | ((uint)p.X & 0xFFFFu);
        return (long)packed;
    }
}
'@
[void][RsUi]::SetProcessDPIAware()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1
$lines = New-Object System.Collections.ArrayList
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Esc([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $c = [int]$ch
        if ($c -lt 32 -or ($c -ge 0xE000 -and $c -le 0xF8FF)) { [void]$sb.Append('\u' + $c.ToString('X4')) }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

function Get-Windows {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $cond)
}

function All-Elements($root) {
    $out = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $stack.Push($root)
    while ($stack.Count -gt 0) {
        $el = $stack.Pop()
        [void]$out.Add($el)
        try {
            $child = $walker.GetFirstChild($el)
            while ($null -ne $child) { $stack.Push($child); $child = $walker.GetNextSibling($child) }
        } catch { }
    }
    return $out
}

$windows = Get-Windows
$main = $null
# 最小化时 UIA 矩形为空（C-31）：先按名字挑一个候选恢复，再重新枚举挑最大窗口
$candidate = $null
foreach ($w in $windows) {
    try { if ($w.Current.Name -like '*VisionMaster*') { $candidate = $w; break } } catch { }
}
if ($null -eq $candidate -and $windows.Count -gt 0) { $candidate = $windows[0] }
if ($null -ne $candidate) {
    try {
        $ch = [IntPtr]$candidate.Current.NativeWindowHandle
        if ($ch -ne [IntPtr]::Zero) { [void][RsUi]::ShowWindow($ch, 9) }
    } catch { }
    Start-Sleep -Milliseconds 300
    $windows = Get-Windows
}
foreach ($w in $windows) {
    try { $r = $w.Current.BoundingRectangle; if ($r.Width -gt 400 -and $r.Height -gt 300) { $main = $w; break } } catch { }
}
if ($null -eq $main) { [IO.File]::WriteAllLines($OutFile, @('NO MAIN WINDOW windows=' + $windows.Count)); exit 1 }

$hwnd = [IntPtr]$main.Current.NativeWindowHandle
[void][RsUi]::ShowWindow($hwnd, 9)
$fg = [RsUi]::GetForegroundWindow()
$fgThread = [uint32]0
if ($fg -ne [IntPtr]::Zero) { [void][RsUi]::GetWindowThreadProcessId($fg, [ref]$fgThread) }
$myThread = [RsUi]::GetCurrentThreadId()
if ($fgThread -ne 0) { [void][RsUi]::AttachThreadInput($myThread, $fgThread, $true) }
[void][RsUi]::SetForegroundWindow($hwnd)
if ($fgThread -ne 0) { [void][RsUi]::AttachThreadInput($myThread, $fgThread, $false) }
Start-Sleep -Milliseconds 400

# 找流程胶囊并右键
$target = $null
foreach ($el in (All-Elements $main)) {
    try {
        if ($el.Current.ControlType.ProgrammaticName -ne 'ControlType.ListItem') { continue }
        foreach ($child in (All-Elements $el)) {
            if ($child.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $child.Current.Name -eq $FlowName) { $target = $el; break }
        }
    } catch { }
    if ($null -ne $target) { break }
}
if ($null -eq $target) { [IO.File]::WriteAllLines($OutFile, @('FLOW ITEM NOT FOUND')); exit 2 }

$rect = $target.Current.BoundingRectangle
$x = [int]($rect.X + $rect.Width / 2)
$y = [int]($rect.Y + $rect.Height / 2)
[void]$lines.Add('ITEM=' + [int]$rect.X + ',' + [int]$rect.Y + ',' + [int]$rect.Width + ',' + [int]$rect.Height + ' CLICK=' + $x + ',' + $y)

[void][RsUi]::SetCursorPos($x, $y)
Start-Sleep -Milliseconds 120

function Find-MenuItem {
    foreach ($w in (Get-Windows)) {
        foreach ($el in (All-Elements $w)) {
            try {
                if ($el.Current.ControlType.ProgrammaticName -eq 'ControlType.MenuItem' -and $el.Current.Name -like '*运行统计*') { return $el }
            } catch { }
        }
    }
    return $null
}

# 先走 PostMessage（不依赖全局输入队列/前台）
$lpValue = [long][RsUi]::PackClient($hwnd, $x, $y)
$lp = [IntPtr]$lpValue
[void]$lines.Add('LP=' + $lpValue)
[void][RsUi]::PostMessage($hwnd, 0x0200, [IntPtr]0, $lp)     # WM_MOUSEMOVE
Start-Sleep -Milliseconds 60
[void][RsUi]::PostMessage($hwnd, 0x0204, [IntPtr]2, $lp)     # WM_RBUTTONDOWN
Start-Sleep -Milliseconds 80
[void][RsUi]::PostMessage($hwnd, 0x0205, [IntPtr]0, $lp)     # WM_RBUTTONUP
Start-Sleep -Milliseconds 700
$menuItem = Find-MenuItem
[void]$lines.Add('POST_CLICK menu=' + ($null -ne $menuItem))

if ($null -eq $menuItem) {
    # 回退：合成鼠标事件（需要目标在前台）
    [RsUi]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [RsUi]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    $menuItem = Find-MenuItem
    [void]$lines.Add('MOUSE_CLICK menu=' + ($null -ne $menuItem))
}

if ($null -eq $menuItem) {
    [void]$lines.Add('MENU ITEM NOT FOUND')
    [IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
    exit 3
}

$menuItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
[void]$lines.Add('MENU INVOKED: ' + (Esc $menuItem.Current.Name))
Start-Sleep -Milliseconds 900

# 弹窗：找标题含「结果源」的顶层窗口并 dump
$dialog = $null
foreach ($w in (Get-Windows)) {
    try { if ($w.Current.Name -like '*结果源*') { $dialog = $w; break } } catch { }
}
if ($null -eq $dialog) {
    [void]$lines.Add('DIALOG NOT FOUND')
    [IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
    exit 4
}

$dr = $dialog.Current.BoundingRectangle
[void]$lines.Add('DIALOG=' + (Esc $dialog.Current.Name) + ' rect=' + [int]$dr.X + ',' + [int]$dr.Y + ',' + [int]$dr.Width + ',' + [int]$dr.Height)
foreach ($el in (All-Elements $dialog)) {
    try {
        $c = $el.Current
        $t = $c.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        if ($t -eq 'Pane' -or $t -eq 'Custom') { continue }
        $r = $c.BoundingRectangle
        [void]$lines.Add($t + ' | ' + (Esc $c.Name) + ' | id=' + $c.AutomationId + ' | rect=' + [int]$r.X + ',' + [int]$r.Y + ',' + [int]$r.Width + ',' + [int]$r.Height + ' | en=' + $c.IsEnabled)
    } catch { }
}

[IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output ('OUT=' + $OutFile + ' lines=' + $lines.Count)
