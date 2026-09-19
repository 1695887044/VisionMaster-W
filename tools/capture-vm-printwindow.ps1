# Capture the VisionMaster top-level window via PrintWindow (works when occluded).
# Use this instead of CopyFromScreen when the session is locked (LogonUI running),
# because CopyFromScreen returns an all-black bitmap in that case.
param(
    [string]$Out = "$env:TEMP\vm_pw.png"
)

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class PwWin
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public class Info
    {
        public IntPtr Hwnd;
        public string Title;
        public string Cls;
        public int W, H;
        public long Area { get { return (long)W * H; } }
    }

    public static List<Info> List(uint target)
    {
        var list = new List<Info>();
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            var sc = new StringBuilder(512);
            GetClassName(h, sc, sc.Capacity);
            list.Add(new Info
            {
                Hwnd = h,
                Title = sb.ToString(),
                Cls = sc.ToString(),
                W = r.Right - r.Left,
                H = r.Bottom - r.Top
            });
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1

$all = [PwWin]::List([uint32]$proc.Id)
$cand = $all | Where-Object { $_.W -gt 0 -and $_.H -gt 0 } | Sort-Object Area -Descending
if (-not $cand) { throw "no visible VisionMaster window found" }
$win = $cand[0]

"HWND=$($win.Hwnd) TITLE='$($win.Title)' CLASS='$($win.Cls)' SIZE=$($win.W)x$($win.H)"

$bmp = New-Object System.Drawing.Bitmap($win.W, $win.H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT : makes DirectComposition / WPF surfaces render fully
$ok = [PwWin]::PrintWindow($win.Hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not $ok) { throw "PrintWindow failed" }

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

"OUT=$Out SIZE=$((Get-Item $Out).Length)"
