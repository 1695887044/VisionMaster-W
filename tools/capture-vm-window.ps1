param([string]$OutPath = "$env:TEMP\vm_shot.png")

# Capture the VisionMaster main window into a PNG and print its screen rect.
#
# Why enumerate windows instead of using Process.MainWindowHandle:
#   VisionMaster reports an EMPTY MainWindowTitle (its shell window is created by a
#   navigation host, so .NET never designates it as the "main window"), which makes
#   MainWindowHandle return 0 and CopyFromScreen throw. So we enumerate every
#   top-level window of the process and pick the largest VISIBLE one.
#
# Note: the P/Invoke declarations MUST use CharSet.Unicode. With the default ANSI
# marshaling, the *W entry points return UTF-16 but the buffer is read as bytes, so
# every title/class comes back as a single letter ("VisionMaster..." -> "V").
Add-Type -AssemblyName System.Drawing -ErrorAction Stop

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class VmWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public class WinInfo {
        public IntPtr Handle;
        public bool Visible;
        public string Class = "";
        public string Title = "";
        public int Left, Top, Right, Bottom;
        public int Area { get { return (Right - Left) * (Bottom - Top); } }
    }

    public static List<WinInfo> List(uint target) {
        var list = new List<WinInfo>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target) {
                var t = new StringBuilder(1024); GetWindowText(h, t, 1024);
                var c = new StringBuilder(512); GetClassName(h, c, 512);
                RECT r; GetWindowRect(h, out r);
                var w = new WinInfo();
                w.Handle = h; w.Visible = IsWindowVisible(h);
                w.Class = c.ToString(); w.Title = t.ToString();
                w.Left = r.Left; w.Top = r.Top; w.Right = r.Right; w.Bottom = r.Bottom;
                list.Add(w);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1
$wins = [VmWin]::List($proc.Id)

# Largest visible window that actually has a size = the shell window.
$main = $wins |
    Where-Object { $_.Visible -and $_.Area -gt 0 } |
    Sort-Object Area -Descending |
    Select-Object -First 1

if ($null -eq $main) {
    Write-Output "NO_VISIBLE_WINDOW"
    $wins | ForEach-Object {
        Write-Output ("hwnd={0} visible={1} class='{2}' title='{3}' rect={4},{5},{6},{7}" -f $_.Handle, $_.Visible, $_.Class, $_.Title, $_.Left, $_.Top, $_.Right, $_.Bottom)
    }
    exit 1
}

[VmWin]::ShowWindow($main.Handle, 3) | Out-Null   # SW_MAXIMIZE
[VmWin]::SetForegroundWindow($main.Handle) | Out-Null
Start-Sleep -Milliseconds 900

# Re-read the rect: maximizing changes it.
$rect = New-Object VmWin+RECT
[VmWin]::GetWindowRect($main.Handle, [ref]$rect) | Out-Null

$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose()
$bmp.Dispose()

Write-Output "OUT=$OutPath"
Write-Output "HWND=$($main.Handle)"
Write-Output "TITLE=$($main.Title)"
Write-Output "RECT_LEFT=$($rect.Left)"
Write-Output "RECT_TOP=$($rect.Top)"
Write-Output "RECT_W=$w"
Write-Output "RECT_H=$h"
