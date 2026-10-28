param(
    [Parameter(Mandatory = $true)][string]$FlowName,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [ValidateSet('click', 'key')][string]$Mode = 'key'
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class RtClick {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
'@
[void][RtClick]::SetProcessDPIAware()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1
$lines = New-Object System.Collections.ArrayList

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

$windows = Get-Windows
$main = $null
foreach ($w in $windows) {
    try {
        $r = $w.Current.BoundingRectangle
        if ($r.Width -gt 400 -and $r.Height -gt 300) { $main = $w; break }
    } catch { }
}
if ($null -eq $main) { [IO.File]::WriteAllLines($OutFile, @('NO MAIN WINDOW')); exit 1 }

$hwnd = [IntPtr]$main.Current.NativeWindowHandle
[void][RtClick]::ShowWindow($hwnd, 9)   # SW_RESTORE
# 前台锁：直接 SetForegroundWindow 会被系统拒绝（用户正在用别的窗口）。
# 先 AttachThreadInput 到当前前台线程，再抢，抢完解绑。
$fgBefore = [RtClick]::GetForegroundWindow()
$fgThread = [uint32]0
[void][RtClick]::GetWindowThreadProcessId($fgBefore, [ref]$fgThread)
$myThread = [RtClick]::GetCurrentThreadId()
[void][RtClick]::AttachThreadInput($myThread, $fgThread, $true)
[void][RtClick]::SetForegroundWindow($hwnd)
[void][RtClick]::AttachThreadInput($myThread, $fgThread, $false)
Start-Sleep -Milliseconds 400

$fg = [RtClick]::GetForegroundWindow()
$fgPid = [uint32]0
[void][RtClick]::GetWindowThreadProcessId($fg, [ref]$fgPid)
[void]$lines.Add('FOREGROUND_PID=' + $fgPid + ' VM_PID=' + $proc.Id + ' MATCH=' + ($fgPid -eq $proc.Id))

# 找流程胶囊：ControlType.ListItem 且子树里有 Text = FlowName
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$stack = New-Object System.Collections.Stack
$stack.Push($main)
$target = $null
while ($stack.Count -gt 0 -and $null -eq $target) {
    $el = $stack.Pop()
    try {
        $c = $el.Current
        if ($c.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and $c.Name -eq 'VisionMaster.Models.FlowModel') {
            $child = $walker.GetFirstChild($el)
            while ($null -ne $child) {
                try {
                    if ($child.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $child.Current.Name -eq $FlowName) { $target = $el; break }
                } catch { }
                $child = $walker.GetNextSibling($child)
            }
        }
    } catch { }
    if ($null -ne $target) { break }
    try {
        $child = $walker.GetFirstChild($el)
        while ($null -ne $child) { $stack.Push($child); $child = $walker.GetNextSibling($child) }
    } catch { }
}

if ($null -eq $target) {
    [void]$lines.Add('FLOW ITEM NOT FOUND: ' + (Esc $FlowName))
    [IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
    exit 2
}

$rect = $target.Current.BoundingRectangle
$x = [int]($rect.X + $rect.Width / 2)
$y = [int]($rect.Y + $rect.Height / 2)
[void]$lines.Add('ITEM_RECT=' + [int]$rect.X + ',' + [int]$rect.Y + ',' + [int]$rect.Width + ',' + [int]$rect.Height + ' CLICK=' + $x + ',' + $y)

if ($Mode -eq 'key') {
    # 先选中并聚焦，再用 Shift+F10 打开上下文菜单（不依赖鼠标命中）
    try {
        $sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
    } catch { [void]$lines.Add('SELECT FAILED: ' + $_.Exception.Message) }
    try { $target.SetFocus() } catch { [void]$lines.Add('FOCUS FAILED: ' + $_.Exception.Message) }
    Start-Sleep -Milliseconds 200
    [RtClick]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)        # Shift down
    [RtClick]::keybd_event(0x79, 0, 0, [UIntPtr]::Zero)        # F10 down
    Start-Sleep -Milliseconds 40
    [RtClick]::keybd_event(0x79, 0, 2, [UIntPtr]::Zero)        # F10 up
    [RtClick]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)        # Shift up
} else {
    [void][RtClick]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 120
    [RtClick]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTDOWN
    Start-Sleep -Milliseconds 60
    [RtClick]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTUP
}
Start-Sleep -Milliseconds 800

# 菜单是独立 Popup 窗口：重新枚举所有顶层窗口，打印菜单项
$after = Get-Windows
if ($after.Count -eq 0) { Start-Sleep -Milliseconds 300; $after = Get-Windows }
if ($after.Count -eq 0) { Start-Sleep -Milliseconds 600; $after = Get-Windows }
[void]$lines.Add('WINDOWS_AFTER=' + $after.Count)
foreach ($w in $after) {
    try {
        $c = $w.Current
        $r = $c.BoundingRectangle
        [void]$lines.Add('WIN ' + (Esc $c.Name) + ' | rect=' + [int]$r.X + ',' + [int]$r.Y + ',' + [int]$r.Width + ',' + [int]$r.Height)
        $stack2 = New-Object System.Collections.Stack
        $stack2.Push($w)
        while ($stack2.Count -gt 0) {
            $el = $stack2.Pop()
            try {
                $cc = $el.Current
                if ($cc.ControlType.ProgrammaticName -eq 'ControlType.MenuItem' -or $cc.ControlType.ProgrammaticName -eq 'ControlType.Menu') {
                    $rr = $cc.BoundingRectangle
                    [void]$lines.Add('  MENU ' + (Esc $cc.Name) + ' | id=' + $cc.AutomationId + ' | rect=' + [int]$rr.X + ',' + [int]$rr.Y + ',' + [int]$rr.Width + ',' + [int]$rr.Height + ' | en=' + $cc.IsEnabled)
                }
            } catch { }
            try {
                $child = $walker.GetFirstChild($el)
                while ($null -ne $child) { $stack2.Push($child); $child = $walker.GetNextSibling($child) }
            } catch { }
        }
    } catch { }
}

[IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output ('OUT=' + $OutFile + ' lines=' + $lines.Count)
