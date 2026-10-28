using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VmMcp;

internal static class Paths
{
    public static string RepoRoot { get; private set; } = "";

    public static void Init(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            RepoRoot = Path.GetFullPath(explicitRoot);
            return;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
            {
                RepoRoot = dir.FullName;
                return;
            }
            dir = dir.Parent;
        }

        // Fall back to the working directory; callers will get a clear error when
        // they ask for a path that does not exist.
        RepoRoot = Directory.GetCurrentDirectory();
    }

    public static string VisionMasterBin => Path.Combine(RepoRoot, "VisionMaster", "bin", "Debug", "net9.0-windows");
    public static string VisionMasterExe => Path.Combine(VisionMasterBin, "VisionMaster.exe");
    public static string LogsDir => Path.Combine(VisionMasterBin, "Logs");
    public static string Artifacts => Path.Combine(RepoRoot, "artifacts");
    public static string Today => DateTime.Now.ToString("yyyyMMdd");
    public static string LogDir => Path.Combine(Artifacts, "logs", Today);
    public static string RenderDir => Path.Combine(Artifacts, "render", Today);

    public static string Abs(string path) => Path.IsPathRooted(path) ? path : Path.Combine(RepoRoot, path);

    public static string Rel(string path)
    {
        var full = Path.GetFullPath(path);
        var root = RepoRoot.TrimEnd('\\') + "\\";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full;
    }
}

internal sealed record HostSpec(string Name, string Dir)
{
    public string ExePath => Path.Combine(Paths.RepoRoot, Dir, "bin", "Debug", "net9.0-windows", Name + ".exe");
}

internal static class Hosts
{
    public static readonly HostSpec[] All =
    {
        new("CommChecks", "CommChecks"),
        new("DataPipelineChecks", "DataPipelineChecks"),
        new("DatabaseChecks", "DatabaseChecks"),
        new("ScadaChecks", "ScadaChecks"),
        new("RecipeChecks", "RecipeChecks"),
        new("FlowCanvasChecks", "FlowCanvasChecks"),
        new("UIThemeSmokeTest", "UIThemeSmokeTest"),
        new("HelpProbe", "HelpProbe"),
        new("ScadaWebChecks", @"Web\ScadaWebChecks"),
    };

    public static HostSpec? Find(string name) =>
        All.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
}

internal sealed record RunResult(int ExitCode, bool TimedOut, TimeSpan Duration, string OutPath, string ErrPath, string Text, string Encoding);

internal static class Proc
{
    /// <summary>
    /// Run a child process and capture its output as raw bytes straight to disk.
    /// Deliberately not Start-Process: -Wait waits for the whole process tree (hangs
    /// when a descendant survives) and dropping -Wait leaves ExitCode null forever.
    /// See ledger C-02 / F-02 / R183.
    /// </summary>
    public static RunResult RunCaptured(string exe, string arguments, string workDir, int timeoutMs, string tag)
    {
        Directory.CreateDirectory(Paths.LogDir);
        var stamp = DateTime.Now.ToString("HHmmss-fff");
        var outPath = Path.Combine(Paths.LogDir, $"{tag}-{stamp}.out");
        var errPath = Path.Combine(Paths.LogDir, $"{tag}-{stamp}.err");

        var psi = new ProcessStartInfo(exe)
        {
            Arguments = arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = null,
            StandardErrorEncoding = null,
        };

        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new ToolException($"failed to start '{exe}'");

        var outTask = CopyToFile(proc.StandardOutput.BaseStream, outPath);
        var errTask = CopyToFile(proc.StandardError.BaseStream, errPath);

        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { proc.WaitForExit(5000); } catch { /* ignore */ }
        }
        try { Task.WaitAll(new[] { outTask, errTask }, 10_000); } catch { /* stream copy raced the kill */ }
        sw.Stop();

        var text = ReadAllTextAuto(outPath, errPath, out var encoding);
        return new RunResult(exited ? proc.ExitCode : 124, !exited, sw.Elapsed, outPath, errPath, text, encoding);
    }

    private static async Task CopyToFile(Stream source, string path)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await source.CopyToAsync(fs);
        }
        catch
        {
            // Process was killed or the pipe closed early; the bytes we already have
            // are still on disk, which is what the caller reads.
        }
    }

    public static string ReadAllTextAuto(string outPath, string errPath, out string encoding)
    {
        var bytes = new List<byte>();
        foreach (var p in new[] { outPath, errPath })
        {
            if (!File.Exists(p)) continue;
            try { bytes.AddRange(File.ReadAllBytes(p)); } catch { /* unreadable */ }
        }
        return Native.DecodeAuto(bytes.ToArray(), out encoding);
    }

    public static string ReadFileShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        var text = Native.DecodeAuto(ms.ToArray(), out _);
        return text;
    }

    public static IReadOnlyList<Process> VisionMasterProcesses() =>
        Process.GetProcessesByName("VisionMaster").Where(p => !p.HasExited).ToArray();

    public static IReadOnlyList<Process> TestHostProcesses()
    {
        var names = Hosts.All.Select(h => h.Name).ToArray();
        var list = new List<Process>();
        foreach (var n in names)
        {
            foreach (var p in Process.GetProcessesByName(n))
            {
                if (!p.HasExited) list.Add(p);
            }
        }
        return list;
    }

    /// <summary>
    /// Real builds only. VBCSCompiler is the resident Roslyn compiler server: it is
    /// started by the first build and stays for the whole session, so counting it as
    /// "a build is running" would block every later step (ledger A-01).
    /// </summary>
    public static IReadOnlyList<Process> BuildProcesses()
    {
        var list = new List<Process>();
        foreach (var p in Process.GetProcessesByName("MSBuild"))
        {
            if (!p.HasExited) list.Add(p);
        }
        foreach (var p in Process.GetProcessesByName("dotnet"))
        {
            if (p.HasExited) continue;
            var cmd = Native.TryGetCommandLine(p.Id);
            if (cmd is not null && cmd.Contains("VBCSCompiler", StringComparison.OrdinalIgnoreCase)) continue;
            if (cmd is not null && cmd.Contains("vm-mcp", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(p);
        }
        return list;
    }

    public static bool IsCompilerServer(int pid)
    {
        var cmd = Native.TryGetCommandLine(pid);
        return cmd is not null && cmd.Contains("VBCSCompiler", StringComparison.OrdinalIgnoreCase);
    }

    public static string StartTime(Process p)
    {
        try { return p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); } catch { return "?"; }
    }

    public static string MainPath(Process p)
    {
        try { return p.MainModule?.FileName ?? ""; } catch { return ""; }
    }
}

internal sealed record BuildSummary(
    bool Green, int ExitCode, int ErrorCount, int WarningCount,
    string LogPath, string Tail, string[] Errors, TimeSpan Duration, string Encoding);

internal static class Builder
{
    private static readonly Regex ErrorLine = new(@"(?m)^\s*.*?:?\s*error\s+[A-Z]+\d+\s*:.*$", RegexOptions.Compiled);
    private static readonly Regex ErrorCountCn = new(@"(\d+)\s*个错误", RegexOptions.Compiled);
    private static readonly Regex ErrorCountEn = new(@"(\d+)\s+Error\(s\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WarnCountCn = new(@"(\d+)\s*个警告", RegexOptions.Compiled);
    private static readonly Regex WarnCountEn = new(@"(\d+)\s+Warning\(s\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BuildSummary Build(string project, bool noIncremental, bool noDependencies, bool allowRunningHost, int timeoutMs)
    {
        var csproj = Paths.Abs(project);
        if (!File.Exists(csproj)) throw new ToolException($"project not found: {Paths.Rel(csproj)}");

        var running = Proc.VisionMasterProcesses();
        if (running.Count > 0 && !allowRunningHost)
        {
            var pids = string.Join(",", running.Select(p => p.Id));
            throw new ToolException(
                $"VisionMaster is running (pid {pids}). Building now fails at the copy stage with MSB3027/MSB3021 (ledger A-01). " +
                "Stop it with vm_stop, or pass allowRunningHost=true if you only build a project that does not copy into the host bin.");
        }

        var args = new StringBuilder();
        args.Append("build \"").Append(csproj).Append("\" -m:1 -nodeReuse:false -v:m");
        if (noIncremental) args.Append(" --no-incremental");
        if (noDependencies) args.Append(" --no-dependencies");

        var name = Path.GetFileNameWithoutExtension(csproj);
        var r = Proc.RunCaptured("dotnet", args.ToString(), Paths.RepoRoot, timeoutMs, "build-" + name);

        int errors = Count(ErrorCountCn, r.Text) ?? Count(ErrorCountEn, r.Text) ?? -1;
        int warns = Count(WarnCountCn, r.Text) ?? Count(WarnCountEn, r.Text) ?? -1;
        var errorLines = ErrorLine.Matches(r.Text).Select(m => m.Value.Trim()).Distinct().Take(25).ToArray();
        if (errors < 0) errors = errorLines.Length;

        bool failedBanner = r.Text.Contains("生成失败") || r.Text.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);
        bool green = !r.TimedOut && r.ExitCode == 0 && errors == 0 && !failedBanner;

        return new BuildSummary(green, r.ExitCode, errors, warns, r.OutPath, Tail(r.Text, 40), errorLines, r.Duration, r.Encoding);
    }

    private static int? Count(Regex re, string text)
    {
        var m = re.Matches(text);
        if (m.Count == 0) return null;
        return int.TryParse(m[^1].Groups[1].Value, out var v) ? v : null;
    }

    public static string Tail(string text, int lines)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var all = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
    }
}

internal sealed record HostSummary(
    string Name, string ExePath, int ExitCode, bool TimedOut, TimeSpan Duration,
    int PassCount, int FailCount, string? SummaryCounts, string[] FailDetail,
    string LogPath, string Encoding, bool StaleExe, string? StaleSource, string Tail);

internal static class HostRunner
{
    private static readonly Regex BracketPass = new(@"\[PASS\]", RegexOptions.Compiled);
    private static readonly Regex BracketFail = new(@"\[FAIL\]", RegexOptions.Compiled);
    private static readonly Regex CnSummary = new(@"通过\s*[:：]?\s*(\d+)\s*失败\s*[:：]?\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex EnSummary = new(@"RESULT:\s*(\d+)\s*PASS\s*/\s*(\d+)\s*FAIL", RegexOptions.Compiled);
    private static readonly Regex RatioSummary = new(@"(\d+)\s*/\s*(\d+)\s*(?:通过|PASS)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static HostSummary Run(string name, string arguments, int timeoutMs, int failLines)
    {
        var spec = Hosts.Find(name);
        string exe;
        string dir;
        if (spec is not null)
        {
            exe = spec.ExePath;
            dir = Path.GetDirectoryName(exe)!;
        }
        else
        {
            exe = Paths.Abs(name);
            dir = Path.GetDirectoryName(exe) ?? Paths.RepoRoot;
        }

        if (!File.Exists(exe)) throw new ToolException($"host exe not found: {Paths.Rel(exe)} (build it first: vm_build)");

        var (stale, staleSource) = StaleCheck(dir, exe);

        var r = Proc.RunCaptured(exe, arguments ?? "", dir, timeoutMs, "host-" + Path.GetFileNameWithoutExtension(exe));

        if (string.IsNullOrWhiteSpace(r.Text))
        {
            throw new ToolException(
                $"host '{name}' produced no output (exit {(r.TimedOut ? "timeout" : r.ExitCode.ToString())}). " +
                "Zero output is a red result, never a pass (R183②). " +
                $"Raw files: {Paths.Rel(r.OutPath)} / {Paths.Rel(r.ErrPath)}");
        }

        var fails = new List<string>();
        foreach (var line in r.Text.Replace("\r\n", "\n").Split('\n'))
        {
            if (BracketFail.IsMatch(line) || line.Contains("[FAIL]", StringComparison.Ordinal)) fails.Add(line.Trim());
        }

        string? counts = null;
        var cn = CnSummary.Match(r.Text);
        if (cn.Success) counts = $"通过 {cn.Groups[1].Value} / 失败 {cn.Groups[2].Value}";
        else
        {
            var en = EnSummary.Match(r.Text);
            if (en.Success) counts = $"PASS {en.Groups[1].Value} / FAIL {en.Groups[2].Value}";
            else
            {
                var ratio = RatioSummary.Match(r.Text);
                if (ratio.Success) counts = $"{ratio.Groups[1].Value} / {ratio.Groups[2].Value}";
            }
        }

        return new HostSummary(
            Path.GetFileNameWithoutExtension(exe), exe, r.ExitCode, r.TimedOut, r.Duration,
            BracketPass.Matches(r.Text).Count, BracketFail.Matches(r.Text).Count,
            counts, fails.Take(Math.Max(1, failLines)).ToArray(),
            r.OutPath, r.Encoding, stale, staleSource, Builder.Tail(r.Text, 25));
    }

    private static (bool stale, string? source) StaleCheck(string hostDir, string exe)
    {
        try
        {
            var exeTime = File.GetLastWriteTimeUtc(exe);
            var newest = Directory.EnumerateFiles(hostDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
                            && !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null && newest.LastWriteTimeUtc > exeTime)
                return (true, Paths.Rel(newest.FullName));
        }
        catch { /* a locked or disappearing file must not fail the run */ }
        return (false, null);
    }
}
