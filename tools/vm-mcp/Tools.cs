using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace VmMcp;

internal sealed class ToolException : Exception
{
    public ToolException(string message) : base(message) { }
}

internal sealed record ToolDef(string Name, string Description, JsonObject InputSchema, Func<JsonObject, string> Handler);

internal static class Tools
{
    public static readonly IReadOnlyList<ToolDef> All = Build();

    // ------------------------------------------------------------------ helpers

    private static JsonObject Schema(JsonObject props, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
        };
        if (required.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var r in required) arr.Add(r);
            schema["required"] = arr;
        }
        return schema;
    }

    private static JsonObject P(string type, string description, string[]? enumValues = null, JsonNode? @default = null)
    {
        var o = new JsonObject { ["type"] = type, ["description"] = description };
        if (enumValues is not null)
        {
            var arr = new JsonArray();
            foreach (var v in enumValues) arr.Add(v);
            o["enum"] = arr;
        }
        if (@default is not null) o["default"] = @default;
        return o;
    }

    private static string? Str(JsonObject a, string key)
    {
        var n = a[key];
        if (n is null) return null;
        try { return n.GetValue<string>(); } catch { return n.ToString(); }
    }

    private static int Int(JsonObject a, string key, int fallback)
    {
        var n = a[key];
        if (n is null) return fallback;
        try { return n.GetValue<int>(); }
        catch
        {
            return int.TryParse(n.ToString(), out var v) ? v : fallback;
        }
    }

    private static int? IntOrNull(JsonObject a, string key)
    {
        var n = a[key];
        if (n is null) return null;
        try { return n.GetValue<int>(); }
        catch { return int.TryParse(n.ToString(), out var v) ? v : null; }
    }

    private static bool Bool(JsonObject a, string key, bool fallback)
    {
        var n = a[key];
        if (n is null) return fallback;
        try { return n.GetValue<bool>(); }
        catch { return bool.TryParse(n.ToString(), out var v) ? v : fallback; }
    }

    private static string[] StrArray(JsonObject a, string key)
    {
        var n = a[key];
        if (n is not JsonArray arr) return Array.Empty<string>();
        return arr.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToArray();
    }

    private static string Line(string label, string value) => $"{label}: {value}";

    private static string PidList(IEnumerable<Process> procs) =>
        procs.Any()
            ? string.Join(", ", procs.Select(p => $"{p.ProcessName}(pid {p.Id}, start {Proc.StartTime(p)})"))
            : "(none)";

    // ------------------------------------------------------------------ registry

    private static IReadOnlyList<ToolDef> Build() => new List<ToolDef>
    {
        new(
            "vm_status",
            "Preflight for on-machine testing: VisionMaster processes, assertion hosts already running, dotnet/MSBuild builds in flight, " +
            "the four TCP ports (19000/19100/8080/19510), Layout.xml timestamps and the newest host log. " +
            "Call this before any build or GUI run — a running host locks the build (ledger A-01) and a second instance kicks the first (A-02).",
            Schema(new JsonObject()),
            _ => Status()),

        new(
            "vm_start",
            "Start VisionMaster.exe from the Debug bin and wait until the SHELL window exists (a title plus a plausible size), " +
            "not merely any window: a splash screen is an untitled top-level window and would otherwise be reported as 'started'. " +
            "Refuses when an instance is already running because SingleInstanceCheck would kill one of them (ledger A-02). " +
            "On timeout it lists every window the process has, which is how a stuck modal gets identified.",
            Schema(new JsonObject
            {
                ["exe"] = P("string", "Exe path. Default: VisionMaster\\bin\\Debug\\net9.0-windows\\VisionMaster.exe"),
                ["arguments"] = P("string", "Command-line arguments (rarely needed)."),
                ["waitMs"] = P("integer", "How long to wait for the shell window.", @default: 60000),
                ["maximize"] = P("boolean", "Maximize the window after it appears.", @default: false),
                ["minWidth"] = P("integer", "Minimum shell width; smaller windows are treated as splash/popup.", @default: 640),
                ["minHeight"] = P("integer", "Minimum shell height; smaller windows are treated as splash/popup.", @default: 480),
            }),
            a => Start(a)),

        new(
            "vm_windows",
            "List every top-level window of the target process: title, class, rect and whether it looks like a splash/popup. " +
            "This is the tool for 'a dialog is stuck' — vm_ui_tree picks ONE window, this shows all of them.",
            Schema(new JsonObject
            {
                ["pid"] = P("integer", "Target process id. Default: the running VisionMaster."),
            }),
            a => Windows(a)),

        new(
            "vm_stop",
            "Close VisionMaster gracefully (CloseMainWindow first, never /F unless force=true) and wait until the process really exits. " +
            "A graceful close lets the exit chain save drafts and layout (ledger A-01/A-03).",
            Schema(new JsonObject
            {
                ["pid"] = P("integer", "Only stop this pid. Default: every VisionMaster process."),
                ["waitMs"] = P("integer", "Graceful wait before giving up.", @default: 15000),
                ["force"] = P("boolean", "Kill the process tree if the graceful close timed out.", @default: false),
            }),
            a => Stop(a)),

        new(
            "vm_backup_layout",
            "Copy Layout.xml and DefaultLayout.xml into artifacts\\backup\\<yyyyMMdd>-<topic>\\ before running the real exe. " +
            "Starting the host overwrites the user's docking layout (ledger A-03, R176).",
            Schema(new JsonObject
            {
                ["topic"] = P("string", "Folder suffix, e.g. 'ui-test' or 'scada-panel'.", @default: "manual"),
            }, "topic"),
            a => BackupLayout(a)),

        new(
            "vm_build",
            "Build one project with the mandatory '-m:1 -nodeReuse:false' flags and judge the result by CONTENT, not the exit code alone: " +
            "green means exit 0 AND zero errors AND no failure banner (R179). Refuses while VisionMaster runs unless allowRunningHost=true.",
            Schema(new JsonObject
            {
                ["project"] = P("string", "Project path relative to the repo root, e.g. 'FlowCanvasChecks\\FlowCanvasChecks.csproj'."),
                ["noIncremental"] = P("boolean", "Pass --no-incremental (required after changing embedded Help resources).", @default: false),
                ["noDependencies"] = P("boolean", "Pass --no-dependencies (build only this project).", @default: false),
                ["allowRunningHost"] = P("boolean", "Build even when VisionMaster is running (only safe for projects that do not copy into the host bin).", @default: false),
                ["timeoutMs"] = P("integer", "Hard timeout.", @default: 600000),
            }, "project"),
            a => Build(a)),

        new(
            "vm_run_host",
            "Run one assertion host with a bounded timeout, capture RAW BYTES to artifacts\\logs, decode UTF-8 first then fall back to GBK(936), " +
            "and return parsed pass/fail counts plus the [FAIL] lines. Zero output is reported as an error, never as a pass (R183, C-01/C-25). " +
            "Also flags a stale exe (source newer than the binary) before you trust the verdict (R65).",
            Schema(new JsonObject
            {
                ["name"] = P("string", "Registered host name (CommChecks, DataPipelineChecks, DatabaseChecks, ScadaChecks, RecipeChecks, FlowCanvasChecks, UIThemeSmokeTest, HelpProbe, ScadaWebChecks) or an absolute exe path."),
                ["args"] = P("string", "Arguments, e.g. '--sample-flows' or '--deformable'.", @default: ""),
                ["timeoutMs"] = P("integer", "Hard timeout; on expiry the process tree is killed and the result is red.", @default: 600000),
                ["failLines"] = P("integer", "How many [FAIL] lines to include in the reply.", @default: 20),
            }, "name"),
            a => RunHost(a)),

        new(
            "vm_gates",
            "Run tools\\run_gates.ps1 (the single closing gate) and return the parsed GATE SUMMARY rows plus the log tail. " +
            "Use -Only / -SkipBuild through this tool instead of hand-rolling a host loop.",
            Schema(new JsonObject
            {
                ["only"] = P("array", "Only run these steps, e.g. ['FlowCanvasChecks','HelpProbe']."),
                ["skipBuild"] = P("boolean", "Pass -SkipBuild (only valid right after a full build).", @default: false),
                ["timeoutMs"] = P("integer", "Hard timeout for the whole gate.", @default: 1800000),
            }),
            a => Gates(a)),

        new(
            "vm_ui_tree",
            "Dump the UI Automation tree of the VisionMaster main window (picked by LARGEST AREA, not 'first top-level window' — ledger C-20). " +
            "Returns stable integer refs usable by vm_ui_act / vm_ui_click / vm_ui_wait. scope='process' also walks native dialogs and tooltip popups.",
            Schema(new JsonObject
            {
                ["pid"] = P("integer", "Target process id. Default: the running VisionMaster."),
                ["scope"] = P("string", "'main' = largest window only; 'process' = every top-level window (dialogs, popups).", new[] { "main", "process" }, "main"),
                ["maxDepth"] = P("integer", "Tree depth limit.", @default: 12),
                ["maxRows"] = P("integer", "Row cap; the reply says truncated=true when hit.", @default: 400),
                ["controlType"] = P("string", "Filter, e.g. 'Button', 'Text', 'MenuItem', 'Edit'."),
                ["nameContains"] = P("string", "Case-insensitive substring of the element name."),
                ["automationId"] = P("string", "Exact AutomationId filter."),
                ["includeOffscreen"] = P("boolean", "Include elements reported as offscreen.", @default: true),
                ["includePatterns"] = P("boolean", "Also list the supported UIA patterns per element (slower, but tells you which action will work).", @default: false),
                ["format"] = P("string", "'text' for a compact table, 'json' for machine-readable rows.", new[] { "text", "json" }, "text"),
            }),
            a => UiTree(a)),

        new(
            "vm_ui_act",
            "Act on an element through a UIA pattern (invoke / expand / collapse / select / toggle / setValue / scrollIntoView / close / focus). " +
            "Target it either by ref or by name/id/type — with a query it picks the first match that can actually perform the action, " +
            "because a name filter also matches the Text child next to a Button and that Text supports no patterns. " +
            "If the element lacks the pattern the error lists the patterns it DOES support. " +
            "Use action=close on native Win32 dialogs: their buttons are Panes without InvokePattern (ledger C-21).",
            Schema(new JsonObject
            {
                ["ref"] = P("integer", "Element ref from vm_ui_tree. Omit to target by nameContains/automationId/controlType."),
                ["pid"] = P("integer", "Target process id for query targeting. Default: the running VisionMaster."),
                ["scope"] = P("string", "'main' or 'process' for query targeting.", new[] { "main", "process" }, "main"),
                ["nameContains"] = P("string", "Element name substring (query targeting)."),
                ["automationId"] = P("string", "Exact AutomationId (query targeting)."),
                ["controlType"] = P("string", "Control type, e.g. 'Button' (query targeting)."),
                ["maxDepth"] = P("integer", "Search depth for query targeting.", @default: 14),
                ["action"] = P("string", "Pattern action.", new[]
                {
                    "invoke", "expand", "collapse", "select", "toggle", "setValue", "scrollIntoView", "close", "focus",
                }),
                ["value"] = P("string", "Text for action=setValue."),
                ["settleMs"] = P("integer", "Wait after the action before returning.", @default: 200),
            }, "action"),
            a => UiAct(a)),

        new(
            "vm_ui_click",
            "Click at the centre of an element (or explicit screen coordinates). Checks that the target process really owns the foreground " +
            "window first — under remote desktop / another app holding focus, synthetic clicks land elsewhere and look like 'the button did nothing' (ledger C-26).",
            Schema(new JsonObject
            {
                ["ref"] = P("integer", "Element ref from vm_ui_tree. The click point is the centre of its bounding rectangle."),
                ["x"] = P("integer", "Absolute screen X (physical pixels), used when ref is omitted."),
                ["y"] = P("integer", "Absolute screen Y (physical pixels), used when ref is omitted."),
                ["button"] = P("string", "left | right | middle.", new[] { "left", "right", "middle" }, "left"),
                ["doubleClick"] = P("boolean", "Send two down/up pairs.", @default: false),
                ["allowForegroundMismatch"] = P("boolean", "Click even when the target does not own the foreground window.", @default: false),
                ["settleMs"] = P("integer", "Wait after the click.", @default: 250),
            }),
            a => UiClick(a)),

        new(
            "vm_ui_wait",
            "Poll until an element appears/disappears or becomes enabled. Use this instead of fixed sleeps when driving the UI.",
            Schema(new JsonObject
            {
                ["pid"] = P("integer", "Target process id. Default: the running VisionMaster."),
                ["scope"] = P("string", "'main' or 'process'.", new[] { "main", "process" }, "main"),
                ["nameContains"] = P("string", "Case-insensitive substring of the element name."),
                ["automationId"] = P("string", "Exact AutomationId."),
                ["controlType"] = P("string", "Control type filter, e.g. 'Window'."),
                ["state"] = P("string", "'appear' | 'disappear'.", new[] { "appear", "disappear" }, "appear"),
                ["timeoutMs"] = P("integer", "Give up after this long.", @default: 10000),
                ["pollMs"] = P("integer", "Poll interval.", @default: 250),
                ["maxDepth"] = P("integer", "Search depth.", @default: 14),
            }),
            a => UiWait(a)),

        new(
            "vm_shot",
            "Screenshot the VisionMaster window into artifacts\\render\\<yyyyMMdd>\\ and return the exact window rect, image size and DPI scale, " +
            "so screen coordinates can be derived instead of eyeballed (ledger C-07). Supports crop and integer zoom for measuring small buttons. " +
            "mode=window captures only the window's own surface: WPF menus, dropdowns, tooltips and native dialogs are SEPARATE windows and will be missing. " +
            "Use mode=screen when a popup must appear in the image.",
            Schema(new JsonObject
            {
                ["pid"] = P("integer", "Target process id. Default: the running VisionMaster."),
                ["mode"] = P("string", "'window' (PrintWindow, works while occluded, no popups), 'screen' (BitBlt of the window rect, includes popups drawn over it) or 'element' (screen crop of an element rect).",
                    new[] { "window", "screen", "element" }, "window"),
                ["ref"] = P("integer", "Element ref for mode=element."),
                ["out"] = P("string", "Output file name or path. Default: artifacts\\render\\<yyyyMMdd>\\shot-<HHmmss>.png"),
                ["cropX"] = P("integer", "Crop origin X in captured-image pixels."),
                ["cropY"] = P("integer", "Crop origin Y in captured-image pixels."),
                ["cropW"] = P("integer", "Crop width."),
                ["cropH"] = P("integer", "Crop height."),
                ["zoom"] = P("integer", "Nearest-neighbour upscale factor (1-8).", @default: 1),
            }),
            a => Shot(a)),

        new(
            "vm_log_tail",
            "Read the VisionMaster log (or any file) while the host is still writing it, using FileShare.ReadWrite (ledger C-09). " +
            "Returns the last N matching lines with line numbers.",
            Schema(new JsonObject
            {
                ["path"] = P("string", "File to read. Default: the newest file in VisionMaster\\bin\\Debug\\net9.0-windows\\Logs."),
                ["pattern"] = P("string", "Optional .NET regex; only matching lines are returned."),
                ["tail"] = P("integer", "How many trailing lines to scan when no pattern is given.", @default: 80),
                ["maxMatches"] = P("integer", "Cap on returned lines.", @default: 200),
            }),
            a => LogTail(a)),

        new(
            "vm_ui_type",
            "Put text into an element: ValuePattern first (no focus needed), falling back to focusing the control and typing unicode key events. " +
            "Also sends key chords such as Ctrl+S / Enter. Reads the value back so you can see what actually landed. " +
            "IMPORTANT: ValuePattern.SetValue raises no text-input events, so a control that keys off PreviewTextInput or 'edited by the user' " +
            "intent (R35) will not react to it — use mode=keys for those, it is the only path that looks like real typing.",
            Schema(new JsonObject
            {
                ["ref"] = P("integer", "Element ref from vm_ui_tree. Required for text input; optional for a pure key chord."),
                ["text"] = P("string", "Text to enter."),
                ["keys"] = P("string", "Key chord instead of text, e.g. 'Ctrl+S', 'Enter', 'Ctrl+Shift+A', 'F5'."),
                ["mode"] = P("string", "'auto' (ValuePattern when available, else keystrokes), 'value' (ValuePattern only), 'keys' (keystrokes only).",
                    new[] { "auto", "value", "keys" }, "auto"),
                ["clearFirst"] = P("boolean", "Replace the current content instead of appending.", @default: true),
                ["enter"] = P("boolean", "Send Enter after the text.", @default: false),
                ["settleMs"] = P("integer", "Wait after typing.", @default: 250),
            }),
            a => UiType(a)),

        new(
            "vm_ui_drag",
            "Drag with a real mouse down/move/up between two points (element centres or explicit screen coordinates). " +
            "Needed for canvas wiring, SCADA element placement and slider handles, which no UIA pattern can drive.",
            Schema(new JsonObject
            {
                ["fromRef"] = P("integer", "Element ref to start from (its centre)."),
                ["toRef"] = P("integer", "Element ref to drop on (its centre)."),
                ["fromX"] = P("integer", "Absolute screen X to start from when fromRef is omitted."),
                ["fromY"] = P("integer", "Absolute screen Y to start from when fromRef is omitted."),
                ["toX"] = P("integer", "Absolute screen X to drop at when toRef is omitted."),
                ["toY"] = P("integer", "Absolute screen Y to drop at when toRef is omitted."),
                ["pid"] = P("integer", "Target process id for the foreground check (optional with explicit coordinates)."),
                ["button"] = P("string", "left | right | middle.", new[] { "left", "right", "middle" }, "left"),
                ["steps"] = P("integer", "Intermediate move events (2-200).", @default: 16),
                ["durationMs"] = P("integer", "Total drag duration.", @default: 400),
                ["allowForegroundMismatch"] = P("boolean", "Drag even when the target does not own the foreground window.", @default: false),
                ["settleMs"] = P("integer", "Wait after the drop.", @default: 250),
            }),
            a => UiDrag(a)),

        new(
            "vm_ui_scroll",
            "Send mouse wheel events at an element centre or explicit coordinates. Positive delta scrolls up.",
            Schema(new JsonObject
            {
                ["ref"] = P("integer", "Element ref to scroll over (its centre)."),
                ["x"] = P("integer", "Absolute screen X when ref is omitted."),
                ["y"] = P("integer", "Absolute screen Y when ref is omitted."),
                ["pid"] = P("integer", "Target process id for the foreground check (optional with explicit coordinates)."),
                ["delta"] = P("integer", "Wheel notches per step; positive = up.", @default: -1),
                ["times"] = P("integer", "How many notches to send.", @default: 3),
                ["allowForegroundMismatch"] = P("boolean", "Scroll even when the target does not own the foreground window.", @default: false),
                ["settleMs"] = P("integer", "Wait after scrolling.", @default: 250),
            }),
            a => UiScroll(a)),

        new(
            "vm_assert",
            "Assert UI state or log content instead of eyeballing a screenshot. kind=element checks exists/absent/count/text/enabled/disabled " +
            "with polling; kind=log checks that a regex appears (or does not appear) in the log within a time window. " +
            "A failed assertion comes back with isError=true so it cannot be mistaken for success.",
            Schema(new JsonObject
            {
                ["kind"] = P("string", "'element' or 'log'.", new[] { "element", "log" }, "element"),
                ["pid"] = P("integer", "Target process id. Default: the running VisionMaster."),
                ["scope"] = P("string", "'main' or 'process'.", new[] { "main", "process" }, "main"),
                ["nameContains"] = P("string", "Element name substring."),
                ["automationId"] = P("string", "Exact AutomationId."),
                ["controlType"] = P("string", "Control type, e.g. 'Text', 'Button'."),
                ["expect"] = P("string", "exists | absent | count | text | enabled | disabled.", new[]
                {
                    "exists", "absent", "count", "text", "enabled", "disabled",
                }, "exists"),
                ["value"] = P("string", "Expected count (count) or expected text (text)."),
                ["path"] = P("string", "Log file for kind=log. Default: the newest VisionMaster log."),
                ["pattern"] = P("string", "Regex for kind=log."),
                ["withinMinutes"] = P("integer", "Only consider lines whose timestamp is within this window (0 = whole tail).", @default: 10),
                ["timeoutMs"] = P("integer", "Poll until satisfied or this elapses.", @default: 5000),
                ["pollMs"] = P("integer", "Poll interval.", @default: 250),
                ["maxDepth"] = P("integer", "Search depth for kind=element.", @default: 14),
            }),
            a => Assert(a)),

        new(
            "image_info",
            "Decode an image and report size, pixel format, bit depth, per-channel min/max/mean/std and how much of the image sits at " +
            "the black/white end. Catches the fixture bugs that look like plugin bugs: clipped byte fixtures (G-08), constant images, wrong bit depth.",
            Schema(new JsonObject
            {
                ["path"] = P("string", "Image path (relative to the repo root or absolute)."),
                ["perChannel"] = P("boolean", "Report each colour channel separately instead of luminance.", @default: false),
            }, "path"),
            a => ImageInfo(a)),

        new(
            "image_histogram",
            "Histogram over luminance (or the first channel for gray images), with the lowest and highest occupied bin. " +
            "The occupied range is the quickest way to see that a fixture only uses 0..255 of a 16-bit range (G-07/G-08).",
            Schema(new JsonObject
            {
                ["path"] = P("string", "Image path."),
                ["bins"] = P("integer", "Number of bins (2-256).", @default: 16),
            }, "path"),
            a => ImageHistogram(a)),

        new(
            "image_diff",
            "Compare two images of the same size: how many pixels differ beyond a tolerance, by how much, and where (bounding box). " +
            "Optionally writes a red-overlay difference PNG. Use it for before/after acceptance evidence instead of eyeballing two screenshots.",
            Schema(new JsonObject
            {
                ["pathA"] = P("string", "Reference image."),
                ["pathB"] = P("string", "Image to compare."),
                ["tolerance"] = P("double", "Per-pixel tolerance on the 0-255 luminance scale.", @default: 2.0),
                ["saveDiff"] = P("boolean", "Write a red-overlay difference image into artifacts\\render.", @default: false),
            }, "pathA", "pathB"),
            a => ImageDiff(a)),

        new(
            "image_crop",
            "Crop (and optionally integer-zoom) an image into artifacts\\render, preserving the original pixel format. " +
            "For fixtures and for reading a small region at 4x when measuring a control.",
            Schema(new JsonObject
            {
                ["path"] = P("string", "Image path."),
                ["x"] = P("integer", "Crop origin X.", @default: 0),
                ["y"] = P("integer", "Crop origin Y.", @default: 0),
                ["width"] = P("integer", "Crop width."),
                ["height"] = P("integer", "Crop height."),
                ["zoom"] = P("integer", "Nearest-neighbour upscale factor (1-8).", @default: 1),
                ["out"] = P("string", "Output path. Default: artifacts\\render\\<yyyyMMdd>\\crop-<HHmmss>.png"),
            }, "path", "width", "height"),
            a => ImageCrop(a)),

        new(
            "dataset_scan",
            "Scan an image folder and print one line per file (size, format, min-max, mean, std) plus a consistency summary: " +
            "which files deviate in size/format and which are statistical outliers. Use before writing acceptance criteria on a new dataset.",
            Schema(new JsonObject
            {
                ["dir"] = P("string", "Folder to scan (relative to the repo root or absolute)."),
                ["pattern"] = P("string", "File search pattern.", @default: "*.png"),
                ["maxFiles"] = P("integer", "Cap on files inspected.", @default: 60),
                ["recursive"] = P("boolean", "Include subfolders.", @default: false),
            }, "dir"),
            a => DatasetScan(a)),
    };

    // ------------------------------------------------------------------ host / process

    private static string Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Line("repo", Paths.RepoRoot));
        sb.AppendLine(Line("visionmaster", PidList(Proc.VisionMasterProcesses())));
        sb.AppendLine(Line("test-hosts", PidList(Proc.TestHostProcesses())));
        sb.AppendLine(Line("builds", PidList(Proc.BuildProcesses())));
        sb.AppendLine(Line("roslyn-server", RoslynServer()));
        sb.AppendLine(Line("ports", Ports()));

        foreach (var f in new[] { "Layout.xml", "DefaultLayout.xml" })
        {
            var p = Path.Combine(Paths.VisionMasterBin, f);
            sb.AppendLine(Line(f, File.Exists(p)
                ? $"{File.GetLastWriteTime(p):yyyy-MM-dd HH:mm:ss} ({new FileInfo(p).Length} B)"
                : "missing"));
        }

        var newest = NewestLog();
        sb.AppendLine(Line("newest-log", newest is null ? "(none)" : $"{Paths.Rel(newest.FullName)} ({newest.LastWriteTime:yyyy-MM-dd HH:mm:ss}, {newest.Length} B)"));
        sb.AppendLine(Line("host-exe", File.Exists(Paths.VisionMasterExe) ? "present" : "MISSING (build VisionMaster first)"));
        return sb.ToString().TrimEnd();
    }

    private static string RoslynServer()
    {
        var servers = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("dotnet"))
        {
            try
            {
                if (!p.HasExited && Proc.IsCompilerServer(p.Id)) servers.Add($"pid {p.Id}");
            }
            catch { /* exited while enumerating */ }
        }
        return servers.Count == 0
            ? "(none) — every dotnet process above is a real build"
            : string.Join(", ", servers) + " (VBCSCompiler is resident and does NOT lock builds)";
    }

    private static string Ports()
    {
        var busy = new HashSet<int>();
        try
        {
            foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                busy.Add(ep.Port);
        }
        catch { /* best effort */ }

        var ports = new[] { 19000, 19100, 8080, 19510 };
        return string.Join("  ", ports.Select(p => $"{p}={(busy.Contains(p) ? "BUSY" : "free")}"));
    }

    private static FileInfo? NewestLog()
    {
        if (!Directory.Exists(Paths.LogsDir)) return null;
        return new DirectoryInfo(Paths.LogsDir)
            .GetFiles("*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string Windows(JsonObject a)
    {
        uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
        var wins = Ui.TopLevelWindows(pid);
        if (wins.Count == 0)
            throw new ToolException($"pid {pid} has no top-level window right now (still starting, or all windows closed).");

        var sb = new StringBuilder();
        sb.AppendLine(Line("pid", pid.ToString()));
        sb.AppendLine(Line("count", wins.Count.ToString()));
        var ordered = wins.OrderByDescending(w => (long)w.Rect.Width * w.Rect.Height).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var w = ordered[i];
            string note = string.IsNullOrWhiteSpace(w.Title)
                ? "  <- no title: splash or a native popup"
                : (i == 0 ? "  <- largest titled window (what vm_ui_tree treats as main)" : "");
            sb.AppendLine($"  [{i}] '{w.Title}' cls={Ui.SafeClassName(w.Element)} {w.Rect}{note}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Start(JsonObject a)
    {
        var running = Proc.VisionMasterProcesses();
        if (running.Count > 0)
        {
            throw new ToolException(
                $"VisionMaster is already running (pid {string.Join(",", running.Select(p => p.Id))}). " +
                "SingleInstanceCheck would kill one of the two instances (ledger A-02). Stop it first with vm_stop.");
        }

        var exe = Str(a, "exe") is { Length: > 0 } custom ? Paths.Abs(custom) : Paths.VisionMasterExe;
        if (!File.Exists(exe)) throw new ToolException($"exe not found: {exe}");

        var args = Str(a, "arguments") ?? "";
        var waitMs = Int(a, "waitMs", 60000);
        var maximize = Bool(a, "maximize", false);
        var minWidth = Int(a, "minWidth", 640);
        var minHeight = Int(a, "minHeight", 480);

        var psi = new ProcessStartInfo(exe)
        {
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        };
        var sw = Stopwatch.StartNew();
        var proc = Process.Start(psi) ?? throw new ToolException("Process.Start returned null");
        uint pid = (uint)proc.Id;

        // Wait for the SHELL window, not merely "a window": a splash screen is a real
        // top-level window with an empty title and it wins a pure largest-area race
        // (2026-10-27: vm_start returned a 520x360 untitled window at 1.4 s).
        (AutomationElement Element, Native.RECT Rect, string Title)? shell = null;
        while (sw.ElapsedMilliseconds < waitMs)
        {
            if (proc.HasExited)
                throw new ToolException($"VisionMaster exited during startup with code {proc.ExitCode} after {sw.ElapsedMilliseconds} ms. Check the log via vm_log_tail.");

            try
            {
                shell = Ui.FindShellWindow(pid, minWidth, minHeight);
                if (shell is not null) break;
            }
            catch { /* not ready yet */ }
            Thread.Sleep(300);
        }

        if (shell is null)
            throw new ToolException(
                $"no shell window (title + at least {minWidth}x{minHeight}) for pid {pid} within {waitMs} ms. " +
                "Startup checks may be blocked, or a modal dialog is holding the process. Windows currently present:" +
                Environment.NewLine + Ui.DescribeWindows(pid) +
                Environment.NewLine + "Check the log with vm_log_tail, and if a dialog is listed above inspect it with vm_ui_tree scope=process.");

        var window = shell.Value.Element;
        var rect = shell.Value.Rect;

        if (maximize)
        {
            try
            {
                int hwnd = window.Current.NativeWindowHandle;
                if (hwnd != 0) { Native.ShowWindow(new IntPtr(hwnd), Native.SW_MAXIMIZE); Thread.Sleep(600); }
            }
            catch { /* ignore */ }
            rect = Ui.MainWindow(pid).Rect;
        }

        var title = shell.Value.Title;
        sw.Stop();
        var extra = Ui.TopLevelWindows(pid).Count > 1
            ? Environment.NewLine + Line("other-windows", "") + Environment.NewLine + Ui.DescribeWindows(pid)
            : "";
        return string.Join(Environment.NewLine,
            Line("started", $"pid {pid}"),
            Line("window", $"'{title}' {rect}"),
            Line("elapsed", $"{sw.ElapsedMilliseconds} ms"),
            Line("exe", exe)) + extra;
    }

    private static string Stop(JsonObject a)
    {
        var pid = IntOrNull(a, "pid");
        var waitMs = Int(a, "waitMs", 15000);
        var force = Bool(a, "force", false);

        var targets = pid is > 0
            ? Process.GetProcesses().Where(p => p.Id == pid.Value).ToArray()
            : Proc.VisionMasterProcesses().ToArray();

        if (targets.Length == 0) return "nothing to stop (no matching process)";

        var sb = new StringBuilder();
        foreach (var p in targets)
        {
            string name = p.ProcessName;
            int id = p.Id;
            bool closed = false;
            try { closed = p.CloseMainWindow(); } catch { }
            if (!closed)
            {
                sb.AppendLine($"{name}(pid {id}): CloseMainWindow returned false (no window or already exiting)");
            }

            var sw = Stopwatch.StartNew();
            bool exited = false;
            while (sw.ElapsedMilliseconds < waitMs)
            {
                try { if (p.HasExited) { exited = true; break; } } catch { exited = true; break; }
                Thread.Sleep(200);
            }

            if (!exited && force)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                try { exited = p.WaitForExit(5000); } catch { }
                sb.AppendLine($"{name}(pid {id}): killed after {sw.ElapsedMilliseconds} ms (force=true)");
            }
            else
            {
                sb.AppendLine($"{name}(pid {id}): {(exited ? "exited gracefully" : "STILL RUNNING")} after {sw.ElapsedMilliseconds} ms");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string BackupLayout(JsonObject a)
    {
        var topic = Str(a, "topic") is { Length: > 0 } t ? Sanitize(t) : "manual";
        var dir = Path.Combine(Paths.Artifacts, "backup", $"{Paths.Today}-{topic}");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        int copied = 0;
        foreach (var f in new[] { "Layout.xml", "DefaultLayout.xml" })
        {
            var src = Path.Combine(Paths.VisionMasterBin, f);
            if (!File.Exists(src)) { sb.AppendLine($"{f}: missing (skipped)"); continue; }
            var dst = Path.Combine(dir, f);
            File.Copy(src, dst, overwrite: true);
            copied++;
            sb.AppendLine($"{f}: -> {Paths.Rel(dst)} ({new FileInfo(dst).Length} B)");
        }
        sb.AppendLine(Line("folder", Paths.Rel(dir)));
        sb.AppendLine(Line("copied", copied.ToString()));
        return sb.ToString().TrimEnd();
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Trim();
    }

    // ------------------------------------------------------------------ build / hosts

    private static string Build(JsonObject a)
    {
        var project = Str(a, "project");
        if (string.IsNullOrWhiteSpace(project)) throw new ToolException("project is required");
        var r = Builder.Build(
            project,
            Bool(a, "noIncremental", false),
            Bool(a, "noDependencies", false),
            Bool(a, "allowRunningHost", false),
            Int(a, "timeoutMs", 600000));

        var sb = new StringBuilder();
        sb.AppendLine(Line("verdict", r.Green ? "GREEN" : "RED"));
        sb.AppendLine(Line("exit", r.ExitCode.ToString()));
        sb.AppendLine(Line("errors", r.ErrorCount < 0 ? "?" : r.ErrorCount.ToString()));
        sb.AppendLine(Line("warnings", r.WarningCount < 0 ? "?" : r.WarningCount.ToString()));
        sb.AppendLine(Line("duration", $"{r.Duration.TotalSeconds:F1}s"));
        sb.AppendLine(Line("log", Paths.Rel(r.LogPath)));
        if (r.Errors.Length > 0)
        {
            sb.AppendLine("--- error lines ---");
            foreach (var e in r.Errors) sb.AppendLine(e);
        }
        sb.AppendLine("--- tail ---");
        sb.AppendLine(r.Tail);
        return sb.ToString().TrimEnd();
    }

    private static string RunHost(JsonObject a)
    {
        var name = Str(a, "name");
        if (string.IsNullOrWhiteSpace(name)) throw new ToolException("name is required");
        var r = HostRunner.Run(name, Str(a, "args") ?? "", Int(a, "timeoutMs", 600000), Int(a, "failLines", 20));

        bool red = r.TimedOut || r.ExitCode != 0 || r.FailCount > 0;
        var sb = new StringBuilder();
        sb.AppendLine(Line("host", r.Name));
        sb.AppendLine(Line("verdict", red ? "RED" : "GREEN"));
        sb.AppendLine(Line("exit", r.TimedOut ? "TIMEOUT (killed)" : r.ExitCode.ToString()));
        sb.AppendLine(Line("duration", $"{r.Duration.TotalSeconds:F1}s"));
        sb.AppendLine(Line("encoding", r.Encoding));
        sb.AppendLine(Line("summary", r.SummaryCounts ?? "(no summary line)"));
        sb.AppendLine(Line("[PASS]/[FAIL]", $"{r.PassCount} / {r.FailCount}"));
        if (r.StaleExe)
            sb.AppendLine(Line("WARNING", $"stale exe: {r.StaleSource} is newer than the binary. Rebuild before trusting this verdict (R65)."));
        sb.AppendLine(Line("log", Paths.Rel(r.LogPath)));
        if (r.FailDetail.Length > 0)
        {
            sb.AppendLine("--- [FAIL] lines ---");
            foreach (var f in r.FailDetail) sb.AppendLine(f);
        }
        sb.AppendLine("--- tail ---");
        sb.AppendLine(r.Tail);
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex GateRow = new(@"^\s{2}(\S+)\s+exit=(\S+)\s*(.*)$", RegexOptions.Compiled);

    private static string Gates(JsonObject a)
    {
        var only = StrArray(a, "only");
        var skipBuild = Bool(a, "skipBuild", false);
        var timeoutMs = Int(a, "timeoutMs", 1800000);

        var args = new StringBuilder("-NoProfile -ExecutionPolicy Bypass -File \"")
            .Append(Path.Combine(Paths.RepoRoot, "tools", "run_gates.ps1")).Append('"');
        if (only.Length > 0) args.Append(" -Only ").Append(string.Join(",", only));
        if (skipBuild) args.Append(" -SkipBuild");

        var r = Proc.RunCaptured("powershell.exe", args.ToString(), Paths.RepoRoot, timeoutMs, "gates");

        var rows = new List<string>();
        foreach (var line in r.Text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = GateRow.Match(line);
            if (m.Success) rows.Add($"{m.Groups[1].Value,-20} exit={m.Groups[2].Value,-4} {m.Groups[3].Value}".TrimEnd());
        }

        var sb = new StringBuilder();
        sb.AppendLine(Line("verdict", r.TimedOut ? "TIMEOUT" : (r.ExitCode == 0 ? "GREEN" : "RED")));
        sb.AppendLine(Line("exit", r.TimedOut ? "124" : r.ExitCode.ToString()));
        sb.AppendLine(Line("encoding", r.Encoding));
        if (rows.Count > 0)
        {
            sb.AppendLine("--- gate summary ---");
            foreach (var row in rows) sb.AppendLine(row);
        }
        else
        {
            sb.AppendLine("--- gate summary ---");
            sb.AppendLine("(no summary rows parsed; see tail)");
        }
        sb.AppendLine(Line("log", Paths.Rel(r.OutPath)));
        sb.AppendLine("--- tail ---");
        sb.AppendLine(Builder.Tail(r.Text, 30));
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ UI

    private static string UiTree(JsonObject a)
    {
        uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
        var scope = Str(a, "scope") ?? "main";
        var (nodes, total, truncated) = Ui.Walk(
            pid, scope,
            Int(a, "maxDepth", 12),
            Int(a, "maxRows", 400),
            Str(a, "controlType"),
            Str(a, "nameContains"),
            Str(a, "automationId"),
            Bool(a, "includeOffscreen", true),
            Bool(a, "includePatterns", false));

        var format = (Str(a, "format") ?? "text").ToLowerInvariant();
        if (format == "json")
        {
            var arr = new JsonArray();
            foreach (var n in nodes)
            {
                arr.Add(new JsonObject
                {
                    ["ref"] = n.Ref,
                    ["depth"] = n.Depth,
                    ["window"] = n.WindowIndex,
                    ["type"] = n.Type,
                    ["name"] = n.Name,
                    ["automationId"] = n.AutomationId,
                    ["className"] = n.ClassName,
                    ["rect"] = $"{n.Rect.Left},{n.Rect.Top} {n.Rect.Width}x{n.Rect.Height}",
                    ["offscreen"] = n.Offscreen,
                    ["enabled"] = n.Enabled,
                    ["patterns"] = n.Patterns,
                });
            }
            return JsonSerializer.Serialize(new JsonObject
            {
                ["pid"] = pid,
                ["scope"] = scope,
                ["total"] = total,
                ["shown"] = nodes.Count,
                ["truncated"] = truncated,
                ["nodes"] = arr,
            }, Json.Options);
        }

        var sb = new StringBuilder();
        var main = Ui.MainWindow(pid);
        sb.AppendLine($"MAIN '{Escape(main.Title)}' cls={Ui.SafeClassName(main.Element)} rect={main.Rect} pid={pid}");
        foreach (var n in nodes)
        {
            string name = Escape(n.Name);
            if (name.Length > 80) name = name[..80] + "...";
            var row = new StringBuilder();
            row.Append(' ', Math.Min(n.Depth, 20) * 2);
            row.Append('#').Append(n.Ref).Append(' ').Append(n.Type);
            if (name.Length > 0) row.Append(" '").Append(name).Append('\'');
            if (n.AutomationId.Length > 0) row.Append(" id=").Append(Escape(n.AutomationId));
            row.Append(' ').Append(n.Rect.ToString());
            if (n.Offscreen) row.Append(" off");
            if (!n.Enabled) row.Append(" disabled");
            if (n.Patterns.Length > 0) row.Append(" [").Append(n.Patterns).Append(']');
            sb.AppendLine(row.ToString());
        }
        sb.AppendLine($"NODES total={total} shown={nodes.Count} truncated={(truncated ? "true" : "false")}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Icon-font buttons carry private-use code points (U+E000..U+F8FF) that render as
    /// '?' in any console. Print the code point instead, otherwise every glyph button
    /// looks like the same unnamed '?' and cannot be targeted.
    /// </summary>
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool odd = char.IsControl(c) || (c >= '\uE000' && c <= '\uF8FF') || c == '\uFFFD';
            if (!odd)
            {
                sb?.Append(c);
                continue;
            }
            sb ??= new StringBuilder(s[..i]);
            sb.Append("\\u").Append(((int)c).ToString("X4"));
        }
        return sb?.ToString() ?? s;
    }

    private static string UiAct(JsonObject a)
    {
        var action = Str(a, "action") ?? throw new ToolException("action is required");
        var value = Str(a, "value");
        var settle = Int(a, "settleMs", 200);
        var reference = IntOrNull(a, "ref");

        AutomationElement el;
        string how;

        if (reference is not null)
        {
            el = Ui.Resolve(reference.Value);
            how = $"#{reference}";
        }
        else
        {
            // Target by name/id/type. A name filter also matches the Text child that
            // sits next to the Button, and that Text supports no patterns — so pick the
            // first match that can actually perform the action.
            uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
            var scope = Str(a, "scope") ?? "main";
            var name = Str(a, "nameContains");
            var autoId = Str(a, "automationId");
            var type = Str(a, "controlType");
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(autoId) && string.IsNullOrEmpty(type))
                throw new ToolException("pass ref, or at least one of nameContains / automationId / controlType");

            var matches = Ui.Query(pid, scope, name, autoId, type, Int(a, "maxDepth", 14), 40);
            if (matches.Count == 0)
                throw new ToolException($"no element matches {Describe(name, autoId, type)} in scope={scope}.");

            el = matches.FirstOrDefault(m => Ui.Supports(m, action)) ?? matches[0];
            how = $"query {Describe(name, autoId, type)} ({matches.Count} match(es))";
        }

        string typeName, elName, autoIdValue;
        Native.RECT rect;
        try
        {
            typeName = Ui.TypeName(el);
            elName = Ui.SafeName(el);
            autoIdValue = Ui.SafeAutomationId(el);
            rect = Ui.ToRect(el.Current.BoundingRectangle);
        }
        catch (ElementNotAvailableException)
        {
            throw new ToolException($"{how} is no longer available. Call vm_ui_tree again (the window may have been re-created).");
        }

        var (used, detail) = Ui.Act(el, action, value);
        if (settle > 0) Ui.Wait(settle);

        return string.Join(Environment.NewLine,
            Line("target", $"{how} -> {typeName} '{elName}' id={autoIdValue} {rect}"),
            Line("pattern", used),
            Line("detail", detail),
            Line("ok", "true"));
    }

    private static string UiClick(JsonObject a)
    {
        var reference = IntOrNull(a, "ref");
        int x, y;
        string target;
        uint targetPid;

        if (reference is not null)
        {
            var el = Ui.Resolve(reference.Value);
            Native.RECT rect;
            string type, name;
            try
            {
                rect = Ui.ToRect(el.Current.BoundingRectangle);
                type = Ui.TypeName(el);
                name = Ui.SafeName(el);
                targetPid = (uint)el.Current.ProcessId;
            }
            catch (ElementNotAvailableException)
            {
                throw new ToolException($"element ref {reference} is no longer available. Call vm_ui_tree again.");
            }
            if (!rect.HasArea) throw new ToolException($"element #{reference} has an empty bounding rectangle; it cannot be clicked.");
            x = rect.Left + rect.Width / 2;
            y = rect.Top + rect.Height / 2;
            target = $"#{reference} {type} '{name}' centre";
        }
        else
        {
            var px = IntOrNull(a, "x") ?? throw new ToolException("pass ref, or both x and y");
            var py = IntOrNull(a, "y") ?? throw new ToolException("pass ref, or both x and y");
            x = px;
            y = py;
            targetPid = Ui.ResolvePid(IntOrNull(a, "pid"));
            target = "explicit screen coordinates";
        }

        var (fgPid, fgTitle) = Ui.Foreground();
        bool mismatch = fgPid != 0 && fgPid != targetPid;
        if (mismatch && !Bool(a, "allowForegroundMismatch", false))
        {
            throw new ToolException(
                $"foreground window belongs to pid {fgPid} ('{fgTitle}'), not the target process. " +
                "Synthetic clicks would land in that window and look like 'the button did nothing' (ledger C-26). " +
                "Prefer vm_ui_act (InvokePattern needs no focus). Pass allowForegroundMismatch=true to click anyway.");
        }

        var button = Str(a, "button") ?? "left";
        Ui.Click(x, y, button, Bool(a, "doubleClick", false));
        var settle = Int(a, "settleMs", 250);
        if (settle > 0) Ui.Wait(settle);

        return string.Join(Environment.NewLine,
            Line("clicked", $"{target} at {x},{y} button={button}"),
            Line("foreground", $"pid {fgPid} '{fgTitle}'{(mismatch ? " (MISMATCH - click may not land)" : "")}"),
            Line("hint", "verify the effect by re-reading the element state or a screenshot, not by the fact that a click was sent"));
    }

    private static string UiWait(JsonObject a)
    {
        uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
        var scope = Str(a, "scope") ?? "main";
        var name = Str(a, "nameContains");
        var autoId = Str(a, "automationId");
        var type = Str(a, "controlType");
        var state = (Str(a, "state") ?? "appear").ToLowerInvariant();
        var timeout = Int(a, "timeoutMs", 10000);
        var poll = Math.Max(50, Int(a, "pollMs", 250));
        var depth = Int(a, "maxDepth", 14);

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(autoId) && string.IsNullOrEmpty(type))
            throw new ToolException("pass at least one of nameContains / automationId / controlType");

        var sw = Stopwatch.StartNew();
        bool last = false;
        while (sw.ElapsedMilliseconds < timeout)
        {
            bool exists;
            try { exists = Ui.Exists(pid, scope, name, autoId, type, depth); }
            catch (ToolException) { exists = false; }
            last = exists;

            bool satisfied = state == "disappear" ? !exists : exists;
            if (satisfied)
            {
                return string.Join(Environment.NewLine,
                    Line("state", state),
                    Line("satisfied", "true"),
                    Line("elapsed", $"{sw.ElapsedMilliseconds} ms"),
                    Line("query", Describe(name, autoId, type)));
            }
            Thread.Sleep(poll);
        }

        return string.Join(Environment.NewLine,
            Line("state", state),
            Line("satisfied", "false"),
            Line("elapsed", $"{sw.ElapsedMilliseconds} ms (timeout)"),
            Line("query", Describe(name, autoId, type)),
            Line("lastSeen", last ? "present" : "absent"));
    }

    private static string Describe(string? name, string? autoId, string? type) =>
        $"name~'{name}' id='{autoId}' type='{type}'";

    private static string Shot(JsonObject a)
    {
        uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
        var mode = (Str(a, "mode") ?? "window").ToLowerInvariant();
        var zoom = Math.Clamp(Int(a, "zoom", 1), 1, 8);

        Native.RECT winRect;
        CapturedImage img;
        string method;
        string what;

        if (mode == "element")
        {
            var reference = IntOrNull(a, "ref") ?? throw new ToolException("mode=element needs ref");
            var el = Ui.Resolve(reference);
            Native.RECT rect;
            try { rect = Ui.ToRect(el.Current.BoundingRectangle); }
            catch (ElementNotAvailableException) { throw new ToolException($"element ref {reference} is no longer available."); }
            if (!rect.HasArea) throw new ToolException("element has an empty bounding rectangle");
            winRect = Ui.MainWindow(pid).Rect;
            img = Capture.GrabScreenRect(rect.Left, rect.Top, rect.Width, rect.Height);
            method = "BitBlt(screen, element rect)";
            what = $"element #{reference} {Ui.TypeName(el)} '{Ui.SafeName(el)}'";
        }
        else
        {
            var main = Ui.MainWindow(pid);
            winRect = main.Rect;
            if (mode == "screen")
            {
                img = Capture.GrabScreenRect(winRect.Left, winRect.Top, winRect.Width, winRect.Height);
                method = "BitBlt(screen, window rect)";
            }
            else
            {
                int hwnd = 0;
                try { hwnd = main.Element.Current.NativeWindowHandle; } catch { }
                img = Capture.GrabWindow(new IntPtr(hwnd), winRect, out method);
            }
            what = $"main window '{main.Title}'";
        }

        var cropX = IntOrNull(a, "cropX");
        var cropY = IntOrNull(a, "cropY");
        var cropW = IntOrNull(a, "cropW");
        var cropH = IntOrNull(a, "cropH");
        bool cropped = false;
        if (cropX is not null && cropY is not null && cropW is not null && cropH is not null)
        {
            img = Capture.Crop(img, cropX.Value, cropY.Value, cropW.Value, cropH.Value);
            cropped = true;
        }

        var originalW = img.Width;
        var originalH = img.Height;
        if (zoom > 1) img = Capture.Zoom(img, zoom);

        var outPath = Str(a, "out") is { Length: > 0 } o
            ? Paths.Abs(o)
            : Path.Combine(Paths.RenderDir, $"shot-{DateTime.Now:HHmmss-fff}.png");
        if (!Path.IsPathRooted(outPath)) outPath = Paths.Abs(outPath);
        Capture.SavePng(img, outPath);

        var (fgPid, fgTitle) = Ui.Foreground();
        uint dpi = 96;
        try
        {
            var main = Ui.MainWindow(pid);
            int hwnd = main.Element.Current.NativeWindowHandle;
            if (hwnd != 0) dpi = Native.GetDpiForWindow(new IntPtr(hwnd));
        }
        catch { }

        return string.Join(Environment.NewLine,
            Line("file", Paths.Rel(outPath)),
            Line("target", what),
            Line("method", method),
            Line("window-rect", $"{winRect.Left},{winRect.Top} {winRect.Width}x{winRect.Height}"),
            Line("image", $"{img.Width}x{img.Height} bytes={new FileInfo(outPath).Length}"),
            Line("crop", cropped ? $"{cropX},{cropY} {originalW}x{originalH} -> zoom {zoom}x" : $"none (zoom {zoom}x)"),
            Line("dpi", $"{dpi} (scale {dpi / 96.0:F2})"),
            Line("foreground", $"pid {fgPid} '{fgTitle}'"),
            Line("coordinate-note", "image pixel (0,0) corresponds to window-rect top-left; screen = window-rect + image pixel / zoom"),
            Line("popup-note", mode == "window"
                ? "menus, dropdowns, tooltips and native dialogs are separate windows and are NOT in this image; use mode=screen to include them"
                : "screen capture includes popups that overlap the rect, but anything outside the rect is cut off"));
    }

    // ------------------------------------------------------------------ logs

    private static string LogTail(JsonObject a)
    {
        string path;
        var requested = Str(a, "path");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            path = Paths.Abs(requested);
        }
        else
        {
            var newest = NewestLog() ?? throw new ToolException($"no .log file under {Paths.LogsDir} (has the host run yet?)");
            path = newest.FullName;
        }

        if (!File.Exists(path)) throw new ToolException($"file not found: {path}");

        var text = Proc.ReadFileShared(path);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var pattern = Str(a, "pattern");
        var tail = Int(a, "tail", 80);
        var max = Int(a, "maxMatches", 200);

        var picked = new List<string>();
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            Regex re;
            try { re = new Regex(pattern, RegexOptions.Compiled); }
            catch (Exception ex) { throw new ToolException($"invalid regex: {ex.Message}"); }

            // "tail" must mean the LAST matches: walking forward and stopping at the cap
            // returns lines from the beginning of the log, which is never what an
            // investigation of "what just happened" needs.
            var all = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (re.IsMatch(lines[i])) all.Add($"{i + 1,6}: {lines[i]}");
            }
            int from = Math.Max(0, all.Count - max);
            for (int i = from; i < all.Count; i++) picked.Add(all[i]);
        }
        else
        {
            int start = Math.Max(0, lines.Length - tail);
            for (int i = start; i < lines.Length; i++) picked.Add($"{i + 1,6}: {lines[i]}");
        }

        var sb = new StringBuilder();
        sb.AppendLine(Line("file", Paths.Rel(path)));
        sb.AppendLine(Line("size", $"{new FileInfo(path).Length} B"));
        sb.AppendLine(Line("lines", lines.Length.ToString()));
        sb.AppendLine(Line("matches", picked.Count.ToString()));
        sb.AppendLine("---");
        foreach (var l in picked) sb.AppendLine(l);
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ input

    private static (int X, int Y, uint Pid, string What) ResolvePoint(JsonObject a, string refKey, string xKey, string yKey)
    {
        var reference = IntOrNull(a, refKey);
        if (reference is not null)
        {
            var el = Ui.Resolve(reference.Value);
            try
            {
                var rect = Ui.ToRect(el.Current.BoundingRectangle);
                if (!rect.HasArea) throw new ToolException($"element #{reference} has an empty bounding rectangle (offscreen or collapsed)");
                return (rect.Left + rect.Width / 2, rect.Top + rect.Height / 2,
                    (uint)el.Current.ProcessId, $"#{reference} {Ui.TypeName(el)} '{Ui.SafeName(el)}' centre");
            }
            catch (ElementNotAvailableException)
            {
                throw new ToolException($"element ref {reference} is no longer available. Call vm_ui_tree again.");
            }
        }

        var px = IntOrNull(a, xKey) ?? throw new ToolException($"pass {refKey}, or both {xKey} and {yKey}");
        var py = IntOrNull(a, yKey) ?? throw new ToolException($"pass {refKey}, or both {xKey} and {yKey}");
        var pidArg = IntOrNull(a, "pid");
        return (px, py, pidArg is > 0 ? (uint)pidArg.Value : 0u, "explicit screen coordinates");
    }

    private static void RequireForeground(uint targetPid, JsonObject a, string action)
    {
        // targetPid == 0 means "coordinates only, no process known" — the caller is
        // driving whatever is in front, so there is nothing to compare against.
        if (targetPid == 0) return;

        var (fgPid, fgTitle) = Ui.Foreground();
        if (fgPid == 0 || fgPid == targetPid) return;
        if (Bool(a, "allowForegroundMismatch", false)) return;

        throw new ToolException(
            $"{action} needs the target window in the foreground, but pid {fgPid} ('{fgTitle}') owns it. " +
            "Synthetic input would land in that window (ledger C-26). Prefer a UIA pattern action, or pass allowForegroundMismatch=true.");
    }

    private static string UiType(JsonObject a)
    {
        var keys = Str(a, "keys");
        var text = Str(a, "text");
        var mode = (Str(a, "mode") ?? "auto").ToLowerInvariant();
        var clearFirst = Bool(a, "clearFirst", true);
        var settle = Int(a, "settleMs", 250);
        var reference = IntOrNull(a, "ref");

        if (!string.IsNullOrEmpty(keys) && text is null)
        {
            AutomationElement? el = null;
            if (reference is not null)
            {
                el = Ui.Resolve(reference.Value);
                RequireForeground((uint)el.Current.ProcessId, a, "vm_ui_type");
                try { el.SetFocus(); Thread.Sleep(120); } catch { /* some elements cannot take focus */ }
            }
            var chord = Input.SendChord(keys);
            if (settle > 0) Ui.Wait(settle);
            return string.Join(Environment.NewLine,
                Line("sent", chord),
                Line("target", el is null ? "current foreground window" : $"#{reference} {Ui.TypeName(el)} '{Ui.SafeName(el)}'"),
                Line("ok", "true"));
        }

        if (text is null) throw new ToolException("pass text, or keys (e.g. 'Ctrl+S')");
        if (reference is null) throw new ToolException("text input needs ref (call vm_ui_tree first)");

        var target = Ui.Resolve(reference.Value);
        string type, name;
        uint pid;
        try
        {
            type = Ui.TypeName(target);
            name = Ui.SafeName(target);
            pid = (uint)target.Current.ProcessId;
        }
        catch (ElementNotAvailableException)
        {
            throw new ToolException($"element ref {reference} is no longer available. Call vm_ui_tree again.");
        }

        bool hasValue = false;
        ValuePattern? valuePattern = null;
        try
        {
            if (target.TryGetCurrentPattern(ValuePattern.Pattern, out var obj) && obj is ValuePattern vp)
            {
                hasValue = true;
                valuePattern = vp;
            }
        }
        catch { /* pattern query raced */ }

        string path;
        if (mode == "value" || (mode == "auto" && hasValue))
        {
            if (!hasValue)
                throw new ToolException($"#{reference} does not support ValuePattern. Use mode=keys, or pick a different element.");
            string newValue = text;
            if (!clearFirst)
            {
                try { newValue = (valuePattern!.Current.Value ?? "") + text; } catch { newValue = text; }
            }
            valuePattern!.SetValue(newValue);
            path = "ValuePattern.SetValue";
        }
        else
        {
            RequireForeground(pid, a, "vm_ui_type");
            try { target.SetFocus(); } catch { /* fall through: keystrokes still go to the foreground window */ }
            Ui.Wait(120);
            if (clearFirst) Input.SendChord("Ctrl+A");
            if (clearFirst) Input.SendChord("Delete");
            Input.TypeText(text);
            path = "SetFocus + unicode keystrokes";
        }

        if (Bool(a, "enter", false)) Input.SendChord("Enter");
        if (settle > 0) Ui.Wait(settle);

        string readBack = "";
        try { readBack = Ui.ElementText(target); } catch { }

        var sb = new StringBuilder();
        sb.AppendLine(Line("target", $"#{reference} {type} '{name}'"));
        sb.AppendLine(Line("path", path));
        sb.AppendLine(Line("requested", clearFirst ? text : "+" + text));
        if (readBack.Length > 0) sb.AppendLine(Line("read-back", readBack));
        sb.AppendLine(Line("ok", "true"));
        return sb.ToString().TrimEnd();
    }

    private static string UiDrag(JsonObject a)
    {
        var from = ResolvePoint(a, "fromRef", "fromX", "fromY");
        var to = ResolvePoint(a, "toRef", "toX", "toY");
        uint pid = from.Pid != 0 ? from.Pid : to.Pid;
        RequireForeground(pid, a, "vm_ui_drag");

        var button = Str(a, "button") ?? "left";
        var steps = Int(a, "steps", 16);
        var duration = Int(a, "durationMs", 400);
        Input.Drag(from.X, from.Y, to.X, to.Y, button, steps, duration);

        var settle = Int(a, "settleMs", 250);
        if (settle > 0) Ui.Wait(settle);

        return string.Join(Environment.NewLine,
            Line("drag", $"{from.What} ({from.X},{from.Y}) -> {to.What} ({to.X},{to.Y})"),
            Line("button", $"{button}, {steps} steps over {duration} ms"),
            Line("hint", "verify the result by re-reading the element tree or a screenshot, not by the fact that a drag was sent"));
    }

    private static string UiScroll(JsonObject a)
    {
        var point = ResolvePoint(a, "ref", "x", "y");
        RequireForeground(point.Pid, a, "vm_ui_scroll");

        var delta = Int(a, "delta", -1);
        var times = Int(a, "times", 3);
        Native.SetCursorPos(point.X, point.Y);
        Thread.Sleep(60);
        Input.Wheel(delta, times);

        var settle = Int(a, "settleMs", 250);
        if (settle > 0) Ui.Wait(settle);

        return string.Join(Environment.NewLine,
            Line("scrolled", $"{point.What} ({point.X},{point.Y}) delta={delta} x{times}"),
            Line("ok", "true"));
    }

    // ------------------------------------------------------------------ assert

    private static string Assert(JsonObject a)
    {
        var kind = (Str(a, "kind") ?? "element").ToLowerInvariant();
        return kind == "log" ? AssertLog(a) : AssertElement(a);
    }

    private static string AssertElement(JsonObject a)
    {
        uint pid = Ui.ResolvePid(IntOrNull(a, "pid"));
        var scope = Str(a, "scope") ?? "main";
        var name = Str(a, "nameContains");
        var autoId = Str(a, "automationId");
        var type = Str(a, "controlType");
        var expect = (Str(a, "expect") ?? "exists").ToLowerInvariant();
        var value = Str(a, "value");
        var timeout = Int(a, "timeoutMs", 5000);
        var poll = Math.Max(50, Int(a, "pollMs", 250));
        var depth = Int(a, "maxDepth", 14);

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(autoId) && string.IsNullOrEmpty(type))
            throw new ToolException("pass at least one of nameContains / automationId / controlType");

        int expectedCount = 1;
        if (expect == "count")
        {
            if (!int.TryParse(value, out expectedCount))
                throw new ToolException("expect=count needs a numeric value");
        }
        if (expect == "text" && value is null)
            throw new ToolException("expect=text needs value");

        var sw = Stopwatch.StartNew();
        List<AutomationElement> matches = new();
        bool ok = false;
        string detail = "";

        while (true)
        {
            try { matches = Ui.Query(pid, scope, name, autoId, type, depth, 200); }
            catch (ToolException) { matches = new(); }

            switch (expect)
            {
                case "exists": ok = matches.Count > 0; detail = $"matches={matches.Count}"; break;
                case "absent": ok = matches.Count == 0; detail = $"matches={matches.Count}"; break;
                case "count": ok = matches.Count == expectedCount; detail = $"matches={matches.Count}, expected {expectedCount}"; break;
                case "text":
                    var actual = matches.Count > 0 ? Ui.ElementText(matches[0]) : "";
                    ok = matches.Count > 0 && actual.Contains(value!, StringComparison.OrdinalIgnoreCase);
                    detail = $"text='{actual}' expected to contain '{value}'";
                    break;
                case "enabled": ok = matches.Count > 0 && Ui.IsEnabled(matches[0]); detail = $"enabled={ok}"; break;
                case "disabled": ok = matches.Count > 0 && !Ui.IsEnabled(matches[0]); detail = $"enabled={!ok}"; break;
                default:
                    throw new ToolException($"unknown expect '{expect}'. Use exists, absent, count, text, enabled or disabled.");
            }

            if (ok || sw.ElapsedMilliseconds >= timeout) break;
            Thread.Sleep(poll);
        }

        var sb = new StringBuilder();
        sb.AppendLine(Line("verdict", ok ? "PASS" : "FAIL"));
        sb.AppendLine(Line("kind", $"element {expect}"));
        sb.AppendLine(Line("query", $"{Describe(name, autoId, type)} scope={scope}"));
        sb.AppendLine(Line("detail", detail));
        sb.AppendLine(Line("elapsed", $"{sw.ElapsedMilliseconds} ms"));
        if (matches.Count > 0)
        {
            sb.AppendLine("--- first matches ---");
            foreach (var el in matches.Take(5))
            {
                try
                {
                    sb.AppendLine($"  {Ui.TypeName(el)} '{Ui.SafeName(el)}' id={Ui.SafeAutomationId(el)} {Ui.ToRect(el.Current.BoundingRectangle)} enabled={Ui.IsEnabled(el)}");
                }
                catch { /* vanished */ }
            }
        }
        var text = sb.ToString().TrimEnd();
        if (!ok) throw new AssertionFailed(text);
        return text;
    }

    private static string AssertLog(JsonObject a)
    {
        var pattern = Str(a, "pattern") ?? throw new ToolException("kind=log needs pattern");
        Regex re;
        try { re = new Regex(pattern, RegexOptions.Compiled); }
        catch (Exception ex) { throw new ToolException($"invalid regex: {ex.Message}"); }

        var expect = (Str(a, "expect") ?? "exists").ToLowerInvariant();
        if (expect is not ("exists" or "absent"))
            throw new ToolException("kind=log supports expect=exists or absent");
        var expectPresent = expect == "exists";

        var path = Str(a, "path") is { Length: > 0 } p
            ? Paths.Abs(p)
            : (NewestLog()?.FullName ?? throw new ToolException($"no .log file under {Paths.LogsDir}"));
        if (!File.Exists(path)) throw new ToolException($"file not found: {path}");

        var timeout = Int(a, "timeoutMs", 5000);
        var poll = Math.Max(50, Int(a, "pollMs", 250));
        var within = Int(a, "withinMinutes", 10);
        var sw = Stopwatch.StartNew();
        var hits = new List<string>();
        bool ok = false;

        while (true)
        {
            hits.Clear();
            var text = Proc.ReadFileShared(path);
            var cutoff = within > 0 ? DateTime.Now.AddMinutes(-within) : DateTime.MinValue;

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (!re.IsMatch(line)) continue;
                if (cutoff > DateTime.MinValue && TryTimestamp(line, out var ts) && ts < cutoff) continue;
                hits.Add(line.Trim());
            }

            ok = expectPresent ? hits.Count > 0 : hits.Count == 0;
            if (ok || sw.ElapsedMilliseconds >= timeout) break;
            Thread.Sleep(poll);
        }

        var sb = new StringBuilder();
        sb.AppendLine(Line("verdict", ok ? "PASS" : "FAIL"));
        sb.AppendLine(Line("kind", $"log {expect}"));
        sb.AppendLine(Line("file", Paths.Rel(path)));
        sb.AppendLine(Line("pattern", pattern));
        sb.AppendLine(Line("window", within > 0 ? $"last {within} min" : "whole file"));
        sb.AppendLine(Line("matches", hits.Count.ToString()));
        sb.AppendLine(Line("elapsed", $"{sw.ElapsedMilliseconds} ms"));
        if (hits.Count > 0)
        {
            sb.AppendLine("--- first matches ---");
            foreach (var h in hits.Take(10)) sb.AppendLine("  " + h);
        }
        var result = sb.ToString().TrimEnd();
        if (!ok) throw new AssertionFailed(result);
        return result;
    }

    private static bool TryTimestamp(string line, out DateTime ts)
    {
        ts = default;
        // Log lines start with "yyyy-MM-dd HH:mm:ss.fff"; parse the first 19 chars only.
        if (line.Length < 19) return false;
        return DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", null,
            System.Globalization.DateTimeStyles.None, out ts);
    }

    // ------------------------------------------------------------------ image

    private static string ImageInfo(JsonObject a)
    {
        var path = ImageTools.ResolvePath(Str(a, "path"), "path");
        var img = ImageTools.Load(path);
        var sb = new StringBuilder();
        sb.AppendLine(Line("file", Paths.Rel(path)));
        sb.AppendLine(Line("bytes", new FileInfo(path).Length.ToString()));
        sb.Append(ImageTools.Describe(img, Bool(a, "perChannel", false)));
        return sb.ToString().TrimEnd();
    }

    private static string ImageHistogram(JsonObject a)
    {
        var path = ImageTools.ResolvePath(Str(a, "path"), "path");
        var bins = Math.Clamp(Int(a, "bins", 16), 2, 256);
        var img = ImageTools.Load(path);
        var hist = ImageTools.Histogram(img, bins, out double maxValue);

        long total = hist.Sum();
        int low = Array.FindIndex(hist, h => h > 0);
        int high = Array.FindLastIndex(hist, h => h > 0);

        var sb = new StringBuilder();
        sb.AppendLine(Line("file", Paths.Rel(path)));
        sb.AppendLine(Line("format", $"{img.FormatName} {img.Width}x{img.Height}"));
        sb.AppendLine(Line("bins", $"{bins} over 0..{maxValue:F0}"));
        sb.AppendLine(Line("occupied", $"bin {low} .. {high} of 0..{bins - 1}"));
        if (img.BytesPerSample == 2 && high < bins / 4)
            sb.AppendLine("warning: a 16-bit image whose occupied bins stop in the low quarter usually means the fixture was built by scaling a byte image (ledger G-08)");
        sb.AppendLine("---");
        for (int i = 0; i < bins; i++)
        {
            if (hist[i] == 0) continue;
            double from = i * (maxValue + 1) / bins;
            double to = (i + 1) * (maxValue + 1) / bins - 1;
            sb.AppendLine($"[{i,3}] {from,8:F0}..{to,-8:F0} {hist[i],9} {hist[i] * 100.0 / total,6:F2}%");
        }
        return sb.ToString().TrimEnd();
    }

    private static string ImageDiff(JsonObject a)
    {
        var pathA = ImageTools.ResolvePath(Str(a, "pathA"), "pathA");
        var pathB = ImageTools.ResolvePath(Str(a, "pathB"), "pathB");
        var imgA = ImageTools.Load(pathA);
        var imgB = ImageTools.Load(pathB);

        if (imgA.Width != imgB.Width || imgA.Height != imgB.Height)
            throw new ToolException($"size mismatch: {imgA.Width}x{imgA.Height} vs {imgB.Width}x{imgB.Height}. Crop or resize first; a diff across sizes would be meaningless.");

        double tolerance = 2.0;
        try { tolerance = a["tolerance"]?.GetValue<double>() ?? 2.0; } catch { }
        tolerance = Math.Clamp(tolerance, 0, 255);

        long differing = 0;
        double maxDiff = 0, sumDiff = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var overlay = Bool(a, "saveDiff", false) ? new byte[imgA.Width * imgA.Height * 4] : null;

        for (int y = 0; y < imgA.Height; y++)
        {
            for (int x = 0; x < imgA.Width; x++)
            {
                double d = Math.Abs(imgA.Luma(x, y) - imgB.Luma(x, y)) * 255.0;
                sumDiff += d;
                if (d > maxDiff) maxDiff = d;
                bool over = d > tolerance;
                if (over)
                {
                    differing++;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

                if (overlay is not null)
                {
                    int i = (y * imgA.Width + x) * 4;
                    byte baseValue = (byte)Math.Clamp(imgB.Luma(x, y) * 255.0, 0, 255);
                    overlay[i] = over ? (byte)0 : baseValue;       // B
                    overlay[i + 1] = over ? (byte)0 : baseValue;   // G
                    overlay[i + 2] = over ? (byte)255 : baseValue; // R
                    overlay[i + 3] = 255;
                }
            }
        }

        long total = (long)imgA.Width * imgA.Height;
        var sb = new StringBuilder();
        sb.AppendLine(Line("A", $"{Paths.Rel(pathA)} ({imgA.FormatName} {imgA.Width}x{imgA.Height})"));
        sb.AppendLine(Line("B", $"{Paths.Rel(pathB)} ({imgB.FormatName} {imgB.Width}x{imgB.Height})"));
        sb.AppendLine(Line("tolerance", $"{tolerance:F1} (0-255 luminance)"));
        sb.AppendLine(Line("differing", $"{differing} / {total} ({differing * 100.0 / total:F3}%)"));
        sb.AppendLine(Line("max-diff", $"{maxDiff:F1}"));
        sb.AppendLine(Line("mean-diff", $"{sumDiff / total:F3}"));
        sb.AppendLine(Line("bbox", differing > 0 ? $"{minX},{minY} .. {maxX},{maxY}" : "(none)"));

        if (overlay is not null)
        {
            var outPath = Path.Combine(Paths.RenderDir, $"diff-{DateTime.Now:HHmmss-fff}.png");
            var image = new CapturedImage(imgA.Width, imgA.Height, overlay);
            Capture.SavePng(image, outPath);
            sb.AppendLine(Line("diff-image", Paths.Rel(outPath)));
        }
        return sb.ToString().TrimEnd();
    }

    private static string ImageCrop(JsonObject a)
    {
        var path = ImageTools.ResolvePath(Str(a, "path"), "path");
        var img = ImageTools.Load(path);
        var x = Int(a, "x", 0);
        var y = Int(a, "y", 0);
        var w = IntOrNull(a, "width") ?? throw new ToolException("width is required");
        var h = IntOrNull(a, "height") ?? throw new ToolException("height is required");
        var zoom = Math.Clamp(Int(a, "zoom", 1), 1, 8);

        var cropped = ImageTools.Crop(img, x, y, w, h);
        string outPath = Str(a, "out") is { Length: > 0 } o
            ? Paths.Abs(o)
            : Path.Combine(Paths.RenderDir, $"crop-{DateTime.Now:HHmmss-fff}.png");

        if (zoom == 1)
        {
            ImageTools.Save(cropped, outPath);
        }
        else
        {
            // Zoom through the Bgra32 view so 8/16-bit sources both end up in a PNG the
            // screenshot tooling can read; the crop itself keeps the source format.
            var image = new CapturedImage(cropped.Width, cropped.Height, ToBgra32(cropped));
            Capture.SavePng(Capture.Zoom(image, zoom), outPath);
        }

        return string.Join(Environment.NewLine,
            Line("file", Paths.Rel(outPath)),
            Line("source", $"{Paths.Rel(path)} {img.Width}x{img.Height} {img.FormatName}"),
            Line("crop", $"{x},{y} {cropped.Width}x{cropped.Height} -> zoom {zoom}x"),
            Line("bytes", new FileInfo(outPath).Length.ToString()));
    }

    private static byte[] ToBgra32(LoadedImage img)
    {
        var dst = new byte[img.Width * img.Height * 4];
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int i = (y * img.Width + x) * 4;
                if (img.IsGray)
                {
                    byte v = (byte)Math.Clamp(img.Sample(x, y, 0) * 255.0 / img.MaxValue, 0, 255);
                    dst[i] = dst[i + 1] = dst[i + 2] = v;
                }
                else
                {
                    dst[i] = (byte)Math.Clamp(img.Sample(x, y, 0) * 255.0 / img.MaxValue, 0, 255);
                    dst[i + 1] = (byte)Math.Clamp(img.Sample(x, y, 1) * 255.0 / img.MaxValue, 0, 255);
                    dst[i + 2] = (byte)Math.Clamp(img.Sample(x, y, 2) * 255.0 / img.MaxValue, 0, 255);
                }
                dst[i + 3] = 255;
            }
        }
        return dst;
    }

    private static string DatasetScan(JsonObject a)
    {
        var dir = Paths.Abs(Str(a, "dir") ?? throw new ToolException("dir is required"));
        if (!Directory.Exists(dir)) throw new ToolException($"folder not found: {Paths.Rel(dir)}");

        var pattern = Str(a, "pattern") ?? "*.png";
        var maxFiles = Math.Clamp(Int(a, "maxFiles", 60), 1, 500);
        var recursive = Bool(a, "recursive", false);

        var files = Directory.EnumerateFiles(dir, pattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToArray();

        if (files.Length == 0) throw new ToolException($"no files matching '{pattern}' under {Paths.Rel(dir)}");

        var rows = new List<(string Name, int W, int H, string Format, double Min, double Max, double Mean, double Std, double Sharp, string Flags)>();
        var failures = new List<string>();

        foreach (var f in files)
        {
            try
            {
                var img = ImageTools.Load(f);
                var stats = ImageTools.Stats(img, perChannel: false);
                var s = stats[0];
                long total = (long)img.Width * img.Height;
                var flags = new List<string>();
                if (s.Min == s.Max) flags.Add("constant");
                if (s.Max == img.MaxValue && s.AtMax * 100.0 / total > 1) flags.Add("clip-high");
                if (s.Min == 0 && s.AtMin * 100.0 / total > 5) flags.Add("clip-low");
                if (img.BytesPerSample == 2 && s.Max < img.MaxValue / 4) flags.Add("16bit-low-max");
                rows.Add((Path.GetFileName(f), img.Width, img.Height, img.FormatName, s.Min, s.Max, s.Mean, s.Std,
                    ImageTools.Sharpness(img), string.Join(",", flags)));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(f)}: {ex.Message}");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(Line("dir", Paths.Rel(dir)));
        sb.AppendLine(Line("pattern", pattern));
        sb.AppendLine(Line("scanned", $"{rows.Count} image(s), {failures.Count} unreadable"));
        if (failures.Count > 0)
        {
            sb.AppendLine("--- unreadable ---");
            foreach (var f in failures.Take(10)) sb.AppendLine("  " + f);
        }

        sb.AppendLine("--- files ---");
        foreach (var r in rows)
        {
            sb.AppendLine($"{r.Name,-40} {r.W,5}x{r.H,-5} {r.Format,-10} {r.Min,6:F0}..{r.Max,-6:F0} mean={r.Mean,7:F1} std={r.Std,6:F2} sharp={r.Sharp,9:F1} {r.Flags}".TrimEnd());
        }

        var groups = rows.GroupBy(r => $"{r.W}x{r.H} {r.Format}").OrderByDescending(g => g.Count()).ToArray();
        sb.AppendLine("--- consistency ---");
        foreach (var g in groups) sb.AppendLine($"{g.Count(),4}x {g.Key}");

        if (rows.Count > 2)
        {
            double meanOfMeans = rows.Average(r => r.Mean);
            double sd = Math.Sqrt(rows.Average(r => (r.Mean - meanOfMeans) * (r.Mean - meanOfMeans)));
            if (sd > 0.01)
            {
                var outliers = rows.Where(r => Math.Abs(r.Mean - meanOfMeans) > 3 * sd).ToArray();
                if (outliers.Length > 0)
                {
                    sb.AppendLine(Line("outliers", $"{outliers.Length} file(s) with mean beyond 3 sigma ({meanOfMeans:F1} ± {sd:F1})"));
                    foreach (var o in outliers.Take(10)) sb.AppendLine($"  {o.Name} mean={o.Mean:F1}");
                }
            }

            // A large spread in sharpness means the frames are not interchangeable
            // samples: a through-focus sweep peaks once and decays, and only the frames
            // near the peak are usable for matching. A strict "monotonic" test does not
            // survive real sensor noise, so the criterion is the peak-to-median ratio.
            var sharps = rows.Select(r => r.Sharp).ToArray();
            double peak = sharps.Max();
            int peakIndex = Array.IndexOf(sharps, peak);
            var sorted = sharps.OrderBy(v => v).ToArray();
            double median = sorted[sorted.Length / 2];

            if (sharps.Length >= 5 && peak > median * 3)
            {
                sb.AppendLine(Line("sharpness-trend",
                    $"peak {peak:F1} at '{rows[peakIndex].Name}' vs median {median:F1} — " +
                    "sharpness varies more than 3x across the set, so the frames are NOT interchangeable samples " +
                    "(typical of a focus/exposure sweep; only frames near the peak are usable)"));
            }
            else
            {
                sb.AppendLine(Line("sharpness-trend", $"uniform enough (peak {peak:F1} vs median {median:F1})"));
            }
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Assertion failures travel as isError=true so a client cannot read "the check ran"
/// as "the check passed".
/// </summary>
internal sealed class AssertionFailed : Exception
{
    public AssertionFailed(string report) : base(report) { }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
