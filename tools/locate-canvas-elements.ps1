param(
  [string]$ShotPath = "$env:TEMP\vm_locate.png"
)

# Capture the VisionMaster shell window at native resolution and locate
# blue-ish element clusters (canvas HMI controls use a blue accent color).
# Prints the window rect plus cluster bounding boxes in WINDOW (PNG) coordinates,
# i.e. screen point = (RECT_LEFT + x, RECT_TOP + y) in this process's DPI space.
#
# Window lookup goes through EnumWindows: VisionMaster's shell window has an empty
# MainWindowTitle, so Process.MainWindowHandle is 0 and would throw here.
Add-Type -AssemblyName System.Drawing -ErrorAction Stop

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class WinApiLocate {
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr FindLargestVisible(uint target) {
        IntPtr best = IntPtr.Zero; int bestArea = 0;
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h)) {
                RECT r; GetWindowRect(h, out r);
                int a = (r.Right - r.Left) * (r.Bottom - r.Top);
                if (a > bestArea) { bestArea = a; best = h; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }
}
"@

$proc = Get-Process VisionMaster -ErrorAction Stop | Select-Object -First 1
$hwnd = [WinApiLocate]::FindLargestVisible($proc.Id)
if ($hwnd -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; exit 1 }

[WinApiLocate]::ShowWindow($hwnd, 3) | Out-Null
[WinApiLocate]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 900

$rect = New-Object WinApiLocate+RECT
[WinApiLocate]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$gfx.Dispose()
$bmp.Save($ShotPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Output "RECT=$($rect.Left),$($rect.Top),$w,$h"
Write-Output "SHOT=$ShotPath"

$cell = 8
$cols = [int][Math]::Ceiling($w / $cell)
$rows = [int][Math]::Ceiling($h / $cell)
$occ  = New-Object 'bool[,]' $cols, $rows

for ($y = 0; $y -lt $h; $y += 2) {
  for ($x = 0; $x -lt $w; $x += 2) {
    $c = $bmp.GetPixel($x, $y)
    if ($c.B -gt 120 -and ($c.B - $c.R) -gt 40 -and ($c.B - $c.G) -gt 25) {
      $occ[[int]($x / $cell), [int]($y / $cell)] = $true
    }
  }
}

$seen = New-Object 'bool[,]' $cols, $rows
$clusters = @()
for ($cy = 0; $cy -lt $rows; $cy++) {
  for ($cx = 0; $cx -lt $cols; $cx++) {
    if (-not $occ[$cx, $cy] -or $seen[$cx, $cy]) { continue }
    $q = New-Object System.Collections.Queue
    $q.Enqueue(@($cx, $cy))
    $seen[$cx, $cy] = $true
    $minx = $cx; $maxx = $cx; $miny = $cy; $maxy = $cy; $n = 0
    while ($q.Count -gt 0) {
      $p = $q.Dequeue()
      $px = $p[0]; $py = $p[1]
      $n++
      if ($px -lt $minx) { $minx = $px }; if ($px -gt $maxx) { $maxx = $px }
      if ($py -lt $miny) { $miny = $py }; if ($py -gt $maxy) { $maxy = $py }
      for ($dx = -1; $dx -le 1; $dx++) {
        for ($dy = -1; $dy -le 1; $dy++) {
          $nx = $px + $dx; $ny = $py + $dy
          if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $cols -or $ny -ge $rows) { continue }
          if ($occ[$nx, $ny] -and -not $seen[$nx, $ny]) {
            $seen[$nx, $ny] = $true
            $q.Enqueue(@($nx, $ny))
          }
        }
      }
    }
    $clusters += [pscustomobject]@{
      Left   = $minx * $cell
      Top    = $miny * $cell
      Right  = ([Math]::Min(($maxx + 1) * $cell, $w) - 1)
      Bottom = ([Math]::Min(($maxy + 1) * $cell, $h) - 1)
      Cells  = $n
    }
  }
}

Write-Output "CLUSTER_COUNT=$($clusters.Count)"
$i = 0
foreach ($cl in ($clusters | Sort-Object Cells -Descending | Select-Object -First 25)) {
  $cxp = [int](($cl.Left + $cl.Right) / 2)
  $cyp = [int](($cl.Top + $cl.Bottom) / 2)
  $c = $bmp.GetPixel($cxp, $cyp)
  Write-Output ("CL{0} box={1},{2},{3},{4} cells={5} center={6},{7} color=#{8:X2}{9:X2}{10:X2}" -f `
    $i, $cl.Left, $cl.Top, $cl.Right, $cl.Bottom, $cl.Cells, $cxp, $cyp, $c.R, $c.G, $c.B)
  $i++
}

$bmp.Dispose()
