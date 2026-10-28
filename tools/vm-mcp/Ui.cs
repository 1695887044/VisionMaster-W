using System.Diagnostics;
using System.Windows.Automation;

namespace VmMcp;

internal sealed record UiNode(
    int Ref, int Depth, int WindowIndex, string Type, string Name, string AutomationId,
    string ClassName, Native.RECT Rect, bool Offscreen, bool Enabled, string Patterns);

internal static class Ui
{
    private static readonly List<AutomationElement> Cache = new();
    private static int _nextRef = 1;

    public static int Remember(AutomationElement el)
    {
        lock (Cache)
        {
            int id = _nextRef++;
            Cache.Add(el);
            // Bound the cache so a long session cannot grow without limit. Older refs
            // then resolve to a clear "stale ref" error instead of silently pointing
            // at a recycled element.
            if (Cache.Count > 20000)
            {
                int drop = Cache.Count - 15000;
                Cache.RemoveRange(0, drop);
            }
            return id;
        }
    }

    public static AutomationElement Resolve(int reference)
    {
        lock (Cache)
        {
            int index = reference - 1;
            if (index < 0 || index >= Cache.Count)
                throw new ToolException($"unknown element ref {reference}. Call vm_ui_tree again to get fresh refs.");
            return Cache[index];
        }
    }

    public static uint ResolvePid(int? pid)
    {
        if (pid is > 0)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid.Value);
                return (uint)pid.Value;
            }
            catch (ArgumentException)
            {
                throw new ToolException($"process {pid} is not running (it exited since the last call). Re-check with vm_status.");
            }
        }
        var procs = Proc.VisionMasterProcesses();
        if (procs.Count == 0) throw new ToolException("VisionMaster is not running (no process named VisionMaster). Use vm_start first.");
        return (uint)procs[0].Id;
    }

    public static IReadOnlyList<(AutomationElement Element, Native.RECT Rect, string Title)> TopLevelWindows(uint pid)
    {
        var result = new List<(AutomationElement, Native.RECT, string)>();
        var root = AutomationElement.RootElement;
        var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, (int)pid);
        AutomationElementCollection wins;
        try { wins = root.FindAll(TreeScope.Children, cond); }
        catch { return result; }

        foreach (AutomationElement w in wins)
        {
            try
            {
                var r = ToRect(w.Current.BoundingRectangle);
                if (r.Width <= 0 || r.Height <= 0) continue;
                result.Add((w, r, w.Current.Name ?? ""));
            }
            catch { /* window vanished between enumeration and read */ }
        }
        return result;
    }

    /// <summary>
    /// Pick the main window by LARGEST AREA, never "first top-level window".
    /// Tooltip popups and native dialogs are also top-level windows of the process
    /// and they frequently sort first (ledger C-20).
    /// A minimized window reports an empty UIA rectangle, so it is invisible to the
    /// walk: restore it through Win32 once and retry instead of reporting "no window".
    /// </summary>
    public static (AutomationElement Element, Native.RECT Rect, string Title) MainWindow(uint pid)
    {
        var wins = TopLevelWindows(pid);
        if (wins.Count == 0)
        {
            var (hwnd, _, visible) = Native.FindMainWindow(pid);
            if (hwnd == IntPtr.Zero)
                throw new ToolException($"pid {pid} has no top-level window at all (still starting, or the window was closed). Re-check with vm_status.");

            Native.ShowWindow(hwnd, Native.SW_RESTORE);
            Thread.Sleep(500);
            wins = TopLevelWindows(pid);
            if (wins.Count == 0)
                throw new ToolException(
                    $"pid {pid} has a window (hwnd {hwnd}, visible={visible}) but UI Automation reports no rectangle. " +
                    "It is probably minimized to tray or on a disconnected session; restore it manually and retry.");
        }

        // A splash screen is a real top-level window with an EMPTY title. While it is up
        // it can be the largest window, so "largest wins" alone hands back the splash
        // (observed on 2026-10-27: vm_start returned a 520x360 untitled window at 1.4 s
        // while the shell was still loading). Prefer titled windows whenever one exists.
        var titled = wins.Where(w => !string.IsNullOrWhiteSpace(w.Title)).ToList();
        var pool = titled.Count > 0 ? titled : wins;
        return pool.OrderByDescending(w => (long)w.Rect.Width * w.Rect.Height).First();
    }

    /// <summary>
    /// The window that behaves like the application shell: a title plus a plausible
    /// size. Used by vm_start so it does not report "started" for a splash screen.
    /// </summary>
    public static (AutomationElement Element, Native.RECT Rect, string Title)? FindShellWindow(uint pid, int minWidth, int minHeight)
    {
        var wins = TopLevelWindows(pid);
        return wins
            .Where(w => !string.IsNullOrWhiteSpace(w.Title) && w.Rect.Width >= minWidth && w.Rect.Height >= minHeight)
            .OrderByDescending(w => (long)w.Rect.Width * w.Rect.Height)
            .Cast<(AutomationElement, Native.RECT, string)?>()
            .FirstOrDefault();
    }

    public static string DescribeWindows(uint pid)
    {
        var wins = TopLevelWindows(pid);
        if (wins.Count == 0) return "  (no top-level windows)";
        return string.Join(Environment.NewLine, wins.Select(w =>
            $"  '{w.Title}' {w.Rect}{(string.IsNullOrWhiteSpace(w.Title) ? "   <- no title (splash / popup)" : "")}"));
    }

    public static Native.RECT ToRect(System.Windows.Rect r)
    {
        if (double.IsInfinity(r.X) || double.IsNaN(r.X) || double.IsInfinity(r.Width) || double.IsNaN(r.Width))
            return default;
        return new Native.RECT
        {
            Left = (int)Math.Round(r.X),
            Top = (int)Math.Round(r.Y),
            Right = (int)Math.Round(r.X + r.Width),
            Bottom = (int)Math.Round(r.Y + r.Height),
        };
    }

    public static string TypeName(AutomationElement el)
    {
        try { return el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
        catch { return "?"; }
    }

    public static string SafeName(AutomationElement el)
    {
        try { return el.Current.Name ?? ""; } catch { return ""; }
    }

    public static string SafeAutomationId(AutomationElement el)
    {
        try { return el.Current.AutomationId ?? ""; } catch { return ""; }
    }

    public static string SafeClassName(AutomationElement el)
    {
        try { return el.Current.ClassName ?? ""; } catch { return ""; }
    }

    private static readonly (string Name, AutomationPattern Pattern)[] KnownPatterns =
    {
        ("Invoke", InvokePattern.Pattern),
        ("ExpandCollapse", ExpandCollapsePattern.Pattern),
        ("SelectionItem", SelectionItemPattern.Pattern),
        ("Selection", SelectionPattern.Pattern),
        ("Value", ValuePattern.Pattern),
        ("Toggle", TogglePattern.Pattern),
        ("RangeValue", RangeValuePattern.Pattern),
        ("ScrollItem", ScrollItemPattern.Pattern),
        ("Scroll", ScrollPattern.Pattern),
        ("Window", WindowPattern.Pattern),
        ("Text", TextPattern.Pattern),
        ("Transform", TransformPattern.Pattern),
        ("Grid", GridPattern.Pattern),
        ("Table", TablePattern.Pattern),
    };

    public static string PatternList(AutomationElement el)
    {
        var names = new List<string>();
        foreach (var (name, pattern) in KnownPatterns)
        {
            try
            {
                if (el.TryGetCurrentPattern(pattern, out _)) names.Add(name);
            }
            catch { /* element went away */ }
        }
        return string.Join(",", names);
    }

    public static (List<UiNode> Nodes, int Total, bool Truncated) Walk(
        uint pid,
        string scope,
        int maxDepth,
        int maxRows,
        string? controlType,
        string? nameContains,
        string? automationId,
        bool includeOffscreen,
        bool includePatterns)
    {
        var nodes = new List<UiNode>();
        int total = 0;
        bool truncated = false;

        var roots = new List<(AutomationElement Element, int WindowIndex)>();
        if (string.Equals(scope, "process", StringComparison.OrdinalIgnoreCase))
        {
            var wins = TopLevelWindows(pid);
            for (int i = 0; i < wins.Count; i++) roots.Add((wins[i].Element, i));
        }
        else
        {
            roots.Add((MainWindow(pid).Element, 0));
        }

        var walker = TreeWalker.ControlViewWalker;

        void Visit(AutomationElement el, int depth, int windowIndex)
        {
            if (nodes.Count >= maxRows) { truncated = true; return; }

            string type = TypeName(el);
            string name = SafeName(el);
            string autoId = SafeAutomationId(el);
            bool match = true;
            if (!string.IsNullOrEmpty(controlType) && !type.Equals(controlType, StringComparison.OrdinalIgnoreCase)) match = false;
            if (match && !string.IsNullOrEmpty(nameContains) && name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0) match = false;
            if (match && !string.IsNullOrEmpty(automationId) && !autoId.Equals(automationId, StringComparison.OrdinalIgnoreCase)) match = false;

            bool offscreen = false, enabled = true;
            Native.RECT rect = default;
            try
            {
                offscreen = el.Current.IsOffscreen;
                enabled = el.Current.IsEnabled;
                rect = ToRect(el.Current.BoundingRectangle);
            }
            catch { /* element disappeared mid-walk */ }

            if (match && (includeOffscreen || !offscreen))
            {
                total++;
                nodes.Add(new UiNode(
                    Remember(el), depth, windowIndex, type, name, autoId, SafeClassName(el),
                    rect, offscreen, enabled, includePatterns ? PatternList(el) : ""));
            }

            if (depth >= maxDepth) return;
            AutomationElement? child;
            try { child = walker.GetFirstChild(el); }
            catch { return; }
            while (child is not null)
            {
                Visit(child, depth + 1, windowIndex);
                try { child = walker.GetNextSibling(child); }
                catch { return; }
            }
        }

        foreach (var (root, index) in roots)
        {
            Visit(root, 0, index);
            if (nodes.Count >= maxRows) { truncated = true; break; }
        }

        return (nodes, total, truncated);
    }

    public static (string Used, string Detail) Act(AutomationElement el, string action, string? value)
    {
        string a = (action ?? "").Trim().ToLowerInvariant();
        switch (a)
        {
            case "invoke":
                return Use<InvokePattern>(el, InvokePattern.Pattern, "Invoke", p => { p.Invoke(); return "InvokePattern.Invoke()"; });
            case "expand":
                return Use<ExpandCollapsePattern>(el, ExpandCollapsePattern.Pattern, "ExpandCollapse", p => { p.Expand(); return "ExpandCollapsePattern.Expand()"; });
            case "collapse":
                return Use<ExpandCollapsePattern>(el, ExpandCollapsePattern.Pattern, "ExpandCollapse", p => { p.Collapse(); return "ExpandCollapsePattern.Collapse()"; });
            case "select":
                return Use<SelectionItemPattern>(el, SelectionItemPattern.Pattern, "SelectionItem", p => { p.Select(); return "SelectionItemPattern.Select()"; });
            case "toggle":
                return Use<TogglePattern>(el, TogglePattern.Pattern, "Toggle", p => { p.Toggle(); return "TogglePattern.Toggle()"; });
            case "setvalue":
                return Use<ValuePattern>(el, ValuePattern.Pattern, "Value", p => { p.SetValue(value ?? ""); return $"ValuePattern.SetValue('{value}')"; });
            case "scrollintoview":
                return Use<ScrollItemPattern>(el, ScrollItemPattern.Pattern, "ScrollItem", p => { p.ScrollIntoView(); return "ScrollItemPattern.ScrollIntoView()"; });
            case "close":
                return Use<WindowPattern>(el, WindowPattern.Pattern, "Window", p =>
                {
                    // Native Win32 dialog buttons expose no InvokePattern (ledger C-21);
                    // closing the dialog window is the reliable equivalent of "cancel".
                    p.Close();
                    return "WindowPattern.Close()";
                });
            case "focus":
                el.SetFocus();
                return ("Element.SetFocus()", "focus requested");
            default:
                throw new ToolException($"unknown action '{action}'. Use one of: invoke, expand, collapse, select, toggle, setValue, scrollIntoView, close, focus.");
        }
    }

    /// <summary>
    /// Whether the element can perform the requested action. Used to pick the right
    /// element when the caller targets by name instead of by ref: a name filter also
    /// matches the Text child sitting next to the Button, and the Text has no patterns.
    /// </summary>
    public static bool Supports(AutomationElement el, string action)
    {
        AutomationPattern? pattern = (action ?? "").Trim().ToLowerInvariant() switch
        {
            "invoke" => InvokePattern.Pattern,
            "expand" or "collapse" => ExpandCollapsePattern.Pattern,
            "select" => SelectionItemPattern.Pattern,
            "toggle" => TogglePattern.Pattern,
            "setvalue" => ValuePattern.Pattern,
            "scrollintoview" => ScrollItemPattern.Pattern,
            "close" => WindowPattern.Pattern,
            "focus" => null, // focus needs no pattern
            _ => null,
        };
        if (pattern is null) return true;
        try { return el.TryGetCurrentPattern(pattern, out _); }
        catch { return false; }
    }

    private static (string Used, string Detail) Use<T>(AutomationElement el, AutomationPattern pattern, string label, Func<T, string> body) where T : class
    {
        if (!el.TryGetCurrentPattern(pattern, out var obj) || obj is not T typed)
        {
            string available = PatternList(el);
            string what = $"{TypeName(el)} '{SafeName(el)}'";
            throw new ToolException(
                $"{what} does not support {label}Pattern. Available patterns: {(string.IsNullOrEmpty(available) ? "(none)" : available)}. " +
                "A Text element next to a Button often matches the same name but exposes nothing — target the Button " +
                "(controlType=Button, or read the ref from vm_ui_tree). Native Win32 dialog buttons are Panes without " +
                "InvokePattern: close the dialog with action=close instead (ledger C-21).");
        }
        return (label + "Pattern", body(typed));
    }

    public static bool TryFind(
        AutomationElement root,
        Func<AutomationElement, bool> predicate,
        int maxDepth,
        out AutomationElement? found)
    {
        found = null;
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<(AutomationElement El, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (el, depth) = queue.Dequeue();
            try
            {
                if (predicate(el)) { found = el; return true; }
            }
            catch { continue; }
            if (depth >= maxDepth) continue;
            AutomationElement? child;
            try { child = walker.GetFirstChild(el); }
            catch { continue; }
            while (child is not null)
            {
                queue.Enqueue((child, depth + 1));
                try { child = walker.GetNextSibling(child); }
                catch { break; }
            }
        }
        return false;
    }

    public static bool Exists(uint pid, string scope, string? nameContains, string? automationId, string? controlType, int maxDepth)
    {
        return Query(pid, scope, nameContains, automationId, controlType, maxDepth, 1).Count > 0;
    }

    /// <summary>
    /// All elements matching the filters, across one or all top-level windows.
    /// Used by vm_assert (count / text / enabled) and by vm_ui_type to resolve a target.
    /// </summary>
    public static List<AutomationElement> Query(
        uint pid, string scope, string? nameContains, string? automationId, string? controlType, int maxDepth, int max)
    {
        var roots = string.Equals(scope, "process", StringComparison.OrdinalIgnoreCase)
            ? TopLevelWindows(pid).Select(w => w.Element).ToArray()
            : new[] { MainWindow(pid).Element };

        var found = new List<AutomationElement>();
        var walker = TreeWalker.ControlViewWalker;

        foreach (var root in roots)
        {
            var queue = new Queue<(AutomationElement El, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0 && found.Count < max)
            {
                var (el, depth) = queue.Dequeue();
                bool match;
                try
                {
                    match = (string.IsNullOrEmpty(controlType) || TypeName(el).Equals(controlType, StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrEmpty(automationId) || SafeAutomationId(el).Equals(automationId, StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrEmpty(nameContains) || SafeName(el).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch { continue; }

                if (match) found.Add(el);
                if (depth >= maxDepth) continue;

                AutomationElement? child;
                try { child = walker.GetFirstChild(el); }
                catch { continue; }
                while (child is not null)
                {
                    queue.Enqueue((child, depth + 1));
                    try { child = walker.GetNextSibling(child); }
                    catch { break; }
                }
            }
            if (found.Count >= max) break;
        }
        return found;
    }

    /// <summary>
    /// What a user would read off the control: the ValuePattern value for editable
    /// fields, otherwise the Name. WPF TextBoxes often have an empty Name and carry the
    /// text only in ValuePattern, so checking Name alone gives false negatives.
    /// </summary>
    public static string ElementText(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var v) && v is ValuePattern vp)
                return vp.Current.Value ?? "";
        }
        catch { /* pattern raced with the UI thread */ }
        return SafeName(el);
    }

    public static bool IsEnabled(AutomationElement el)
    {
        try { return el.Current.IsEnabled; } catch { return false; }
    }

    public static (uint ForegroundPid, string ForegroundTitle) Foreground()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return (0, "");
        Native.GetWindowThreadProcessId(h, out uint pid);
        var sb = new System.Text.StringBuilder(512);
        Native.GetWindowText(h, sb, sb.Capacity);
        return (pid, sb.ToString());
    }

    public static void Click(int x, int y, string button, bool doubleClick)
    {
        uint down = button.ToLowerInvariant() switch
        {
            "right" => Native.MOUSEEVENTF_RIGHTDOWN,
            "middle" => Native.MOUSEEVENTF_MIDDLEDOWN,
            _ => Native.MOUSEEVENTF_LEFTDOWN,
        };
        uint up = button.ToLowerInvariant() switch
        {
            "right" => Native.MOUSEEVENTF_RIGHTUP,
            "middle" => Native.MOUSEEVENTF_MIDDLEUP,
            _ => Native.MOUSEEVENTF_LEFTUP,
        };

        Native.SetCursorPos(x, y);
        Thread.Sleep(60);
        int repeats = doubleClick ? 2 : 1;
        for (int i = 0; i < repeats; i++)
        {
            Native.mouse_event(down, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(40);
            Native.mouse_event(up, 0, 0, 0, IntPtr.Zero);
            if (doubleClick) Thread.Sleep(60);
        }
    }

    public static void Wait(int ms) => Thread.Sleep(ms);
}
