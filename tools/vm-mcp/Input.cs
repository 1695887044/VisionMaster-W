using System.Runtime.InteropServices;

namespace VmMcp;

/// <summary>
/// Keyboard and pointer synthesis. Text goes through KEYEVENTF_UNICODE so it does not
/// depend on the active keyboard layout, which matters because the fixtures here mix
/// Chinese names, file paths and numbers in the same field.
/// </summary>
internal static class Input
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    /// <summary>Type arbitrary text as unicode key events. Returns the number of characters sent.</summary>
    public static int TypeText(string text, int perCharDelayMs = 12)
    {
        int sent = 0;
        foreach (char c in text)
        {
            var batch = new[] { Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) };
            if (SendInput((uint)batch.Length, batch, Marshal.SizeOf<INPUT>()) == 0)
                throw new ToolException($"SendInput failed (Win32 error {Marshal.GetLastWin32Error()}). Is the target window running at a higher integrity level?");

            sent++;
            if (perCharDelayMs > 0) Thread.Sleep(perCharDelayMs);
        }
        return sent;
    }

    /// <summary>Send a chord such as "Ctrl+S", "Enter", "Ctrl+Shift+A", "F5".</summary>
    public static string SendChord(string chord)
    {
        var parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ToolException("empty key chord");

        var modifiers = new List<ushort>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers.Add(0x11); break;
                case "shift": modifiers.Add(0x10); break;
                case "alt": modifiers.Add(0x12); break;
                case "win" or "meta": modifiers.Add(0x5B); break;
                default: throw new ToolException($"unknown modifier '{parts[i]}' in '{chord}'");
            }
        }

        string keyName = parts[^1];
        if (!TryVk(keyName, out ushort vk))
            throw new ToolException($"unknown key '{keyName}'. Supported: A-Z, 0-9, F1-F24, Enter, Tab, Esc, Space, Back, Delete, Insert, Home, End, PageUp, PageDown, Left, Right, Up, Down.");

        var batch = new List<INPUT>();
        foreach (var m in modifiers) batch.Add(Key(m, 0, 0));
        batch.Add(Key(vk, 0, 0));
        batch.Add(Key(vk, 0, KEYEVENTF_KEYUP));
        for (int i = modifiers.Count - 1; i >= 0; i--) batch.Add(Key(modifiers[i], 0, KEYEVENTF_KEYUP));

        if (SendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<INPUT>()) == 0)
            throw new ToolException($"SendInput failed (Win32 error {Marshal.GetLastWin32Error()}).");

        return chord;
    }

    private static bool TryVk(string name, out ushort vk)
    {
        vk = 0;
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
        {
            vk = (ushort)(0x6F + fn); // VK_F1 = 0x70
            return true;
        }

        vk = name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "space" => 0x20,
            "back" or "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            _ => 0,
        };
        return vk != 0;
    }

    public static void Wheel(int delta, int times)
    {
        for (int i = 0; i < Math.Max(1, times); i++)
        {
            Native.mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)(delta * 120)), IntPtr.Zero);
            Thread.Sleep(40);
        }
    }

    public static void Drag(int x1, int y1, int x2, int y2, string button, int steps, int durationMs)
    {
        (uint down, uint up) = button.ToLowerInvariant() switch
        {
            "right" => (Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_RIGHTUP),
            "middle" => (Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_MIDDLEUP),
            _ => (Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_LEFTUP),
        };

        steps = Math.Clamp(steps, 2, 200);
        int pause = Math.Max(1, durationMs / steps);

        Native.SetCursorPos(x1, y1);
        Thread.Sleep(80);
        Native.mouse_event(down, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(60);

        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            Native.SetCursorPos(x, y);
            Thread.Sleep(pause);
        }

        Thread.Sleep(60);
        Native.mouse_event(up, 0, 0, 0, IntPtr.Zero);
    }
}
