using System.Runtime.InteropServices;
using System.Text;

namespace VmMcp;

/// <summary>
/// Win32 / GDI entry points used by the capture, window-enumeration and synthetic
/// input helpers. Everything here is deliberately dependency-free: the box has no
/// guarantee of System.Drawing.Common or any NuGet package, and the whole point of
/// this server is to remove the ad-hoc PowerShell layer that kept failing on
/// encoding and process-tree semantics.
/// </summary>
internal static class Native
{
    public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool HasArea => Width > 0 && Height > 0;
        public override readonly string ToString() => $"{Left},{Top} {Width}x{Height}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);

    [DllImport("kernel32.dll")]
    public static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] bytes, int cbMultiByte, [Out] char[] wideChars, int cchWideChar);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr handle, int infoClass, IntPtr buffer, int bufferLength, out int returnLength);

    /// <summary>
    /// Read another process's command line through ProcessCommandLineInformation (60).
    /// Needed because "a dotnet.exe is running" is NOT the same as "a build is running":
    /// the Roslyn VBCSCompiler server lives for the whole session and the gate must not
    /// treat it as an in-flight build (ledger A-01).
    /// </summary>
    public static string? TryGetCommandLine(int pid)
    {
        IntPtr handle = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            handle = proc.Handle;
            if (handle == IntPtr.Zero) return null;

            const int capacity = 8192;
            buffer = Marshal.AllocHGlobal(capacity);
            int status = NtQueryInformationProcess(handle, 60, buffer, capacity, out _);
            if (status != 0) return null;

            int length = Marshal.ReadInt16(buffer, 0);          // UNICODE_STRING.Length in bytes
            IntPtr stringPtr = Marshal.ReadIntPtr(buffer, 8);   // UNICODE_STRING.Buffer
            if (length <= 0 || stringPtr == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(stringPtr, length / 2);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Largest top-level window of a pid via Win32 (works for minimized windows too).</summary>
    public static (IntPtr Hwnd, RECT Rect, bool Visible) FindMainWindow(uint pid)
    {
        IntPtr best = IntPtr.Zero;
        RECT bestRect = default;
        bool bestVisible = false;
        long bestArea = -1;

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != pid) return true;
            if (!GetWindowRect(h, out var r)) return true;
            long area = (long)Math.Abs(r.Width) * Math.Abs(r.Height);
            if (area > bestArea)
            {
                bestArea = area;
                best = h;
                bestRect = r;
                bestVisible = IsWindowVisible(h);
            }
            return true;
        }, IntPtr.Zero);

        return (best, bestRect, bestVisible);
    }

    public static void EnableDpiAwareness()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)); }
        catch { try { SetProcessDPIAware(); } catch { /* best effort */ } }
    }

    /// <summary>
    /// Decode bytes that may be UTF-8 (dotnet hosts) or the console code page 936
    /// (PowerShell children / WPF hosts that never set Console.OutputEncoding).
    /// This is ledger entry C-01 / C-25 turned into code: strict UTF-8 first, then 936.
    /// </summary>
    public static string DecodeAuto(byte[] bytes, out string encoding)
    {
        if (bytes.Length == 0) { encoding = "empty"; return string.Empty; }
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strict.GetString(bytes);
            encoding = "utf-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = "gbk-936";
            return Decode936(bytes);
        }
    }

    /// <summary>Code page 936 decode via Win32 so no extra NuGet package is required.</summary>
    public static string Decode936(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        int needed = MultiByteToWideChar(936, 0, bytes, bytes.Length, null!, 0);
        if (needed <= 0) return Encoding.Latin1.GetString(bytes);
        var chars = new char[needed];
        MultiByteToWideChar(936, 0, bytes, bytes.Length, chars, needed);
        return new string(chars);
    }
}
