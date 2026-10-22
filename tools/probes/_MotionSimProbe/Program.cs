using System.Diagnostics;
using System.Net.Sockets;

using Core.Interfaces;
using Plugin.Motion.Sim;

namespace MotionSimProbe;

/// <summary>
/// 一次性探针：用**真实驱动** SimMotionCard 连**真实仿真器** LineSim，
/// 跑通 P0 出口判据（使能 / 回零 / 绝对定位到位 / 点动 / 停止 / 软限位拒绝）。
/// 用完即删，不进 sln、不进 gate。
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine($"[ok] {name} — {detail}"); }
        else { _fail++; Console.WriteLine($"[FAIL] {name} — {detail}"); }
    }

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);

        // 只认 --linesim <path>；其它参数（run-probe.ps1 会塞 --nologo 之类）一律忽略，
        // 否则把 --nologo 当路径用，报"找不到 LineSim.exe：--nologo"。
        string? explicitExe = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--linesim")
            {
                explicitExe = args[i + 1];
            }
        }

        string exe = explicitExe ?? FindLineSim();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            Console.WriteLine("[FAIL] 找不到 LineSim.exe。仿真器是独立产品，不在本仓库内，请先构建它：");
            Console.WriteLine(@"        cd D:\C#\LineSim && dotnet build LineSim.sln -c Debug -m:1 -nodeReuse:false");
            Console.WriteLine("       然后二选一：--linesim <exe 路径>，或设环境变量 LINESIM_EXE=<exe 路径>");
            return 2;
        }

        Console.WriteLine($"仿真器：{exe}");
        using Process sim = Process.Start(new ProcessStartInfo(exe, "--serve --motion-port 19510 --trace")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false),
            StandardErrorEncoding = new System.Text.UTF8Encoding(false),
        })!;
        sim.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine($"  [sim] {e.Data}"); };
        sim.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine($"  [sim!] {e.Data}"); };
        sim.BeginOutputReadLine();
        sim.BeginErrorReadLine();

        try
        {
            if (!await WaitPortAsync("127.0.0.1", 19510, 10000))
            {
                Console.WriteLine("[FAIL] 仿真器 10s 内没监听 19510");
                return 2;
            }

            var descriptor = new MotionDescriptor
            {
                Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                DriverTypeKey = typeof(SimMotionCard).AssemblyQualifiedName!,
                DisplayName = "P0 出口验证卡",
                Address = "127.0.0.1:19510",
                Axes =
                {
                    new AxisMapping { AxisId = Guid.NewGuid(), LogicalName = "ST1.X", PhysicalIndex = 0, Enabled = true, UnitsPerMm = 1000, SoftLimitMinMm = -50, SoftLimitMaxMm = 600 },
                    new AxisMapping { AxisId = Guid.NewGuid(), LogicalName = "ST1.Z", PhysicalIndex = 1, Enabled = true, UnitsPerMm = 1000, SoftLimitMinMm = -40, SoftLimitMaxMm = 0 },
                    new AxisMapping { AxisId = Guid.NewGuid(), LogicalName = "ST2.Y", PhysicalIndex = 2, Enabled = true, UnitsPerMm = 1000, SoftLimitMinMm = 0, SoftLimitMaxMm = 900 },
                },
            };

            using var card = new SimMotionCard(descriptor);

            // ---- 1. 连接与握手 ----
            bool connected = card.Connect();
            Check("连接+握手成功", connected, $"Connect() = {connected}，Detail = {card.StateDetail}");
            Check("能力来自 hello（44 轴 / 96 进 / 96 出）",
                card.Capabilities.AxisCount == 44 && card.Capabilities.DigitalInputCount == 96 && card.Capabilities.DigitalOutputCount == 96,
                $"axis={card.Capabilities.AxisCount} din={card.Capabilities.DigitalInputCount} dout={card.Capabilities.DigitalOutputCount}");
            Check("八种回零方式全报", card.Capabilities.SupportedHomeModes.Count == 8,
                $"count={card.Capabilities.SupportedHomeModes.Count}");

            // ---- 2. 使能 ----
            var enable = card.Enqueue(new MotionCommand { Kind = MotionCommandKind.Enable, PhysicalAxis = 0 });
            Check("使能被受理", enable == MotionCommandResult.Accepted, $"{enable}");
            bool enabled = await WaitAsync(() => card.GetAxisStatus(0)?.Enabled == true, 3000);
            Check("轴 0 已使能", enabled, $"Enabled={card.GetAxisStatus(0)?.Enabled}");

            // ---- 3. 回零（WaitsForCompletion 路径）----
            var homeCmd = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = 0,
                HomeMode = HomeMode.Origin,
                WaitsForCompletion = true,
                Retryable = false,
            };
            var home = card.Enqueue(homeCmd);
            // 回零是真走一段（轴 0 行程 0..600、开机停中点 300、回零速度 50mm/s → 约 6s），
            // 所以超时必须按"最远行程 ÷ 回零速度"给足，不能按旧的 200ms 固定时长估。
            var homeClock = System.Diagnostics.Stopwatch.StartNew();
            bool homed = await WaitAsync(() => card.IsHomed && card.GetAxisStatus(0)?.Origin == true, 20000);
            homeClock.Stop();
            Check("回零完成（IsHomed + Origin 位）", home == MotionCommandResult.Accepted && homed,
                $"Enqueue={home}，IsHomed={card.IsHomed}，Pos={card.GetAxisStatus(0)?.PositionMm:F3}，耗时={homeClock.ElapsedMilliseconds}ms");

            // ---- 4. 绝对定位到位（下发 ≠ 到位）----
            var move = card.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = 0,
                TargetMm = 100,
                VelocityMmPerS = 200,
            });

            // 下发 ≠ 到位：enqueue 只回答"收下了吗"，返回瞬间轴还没到（设计 §6.1）
            double? atDispatch = card.GetAxisStatus(0)?.PositionMm;
            Check("下发 ≠ 到位（enqueue 返回时轴还在路上）",
                move == MotionCommandResult.Accepted && (atDispatch ?? 100) < 99.5,
                $"Enqueue={move}，下发瞬间 pos={atDispatch:F3}");

            bool arrived = await WaitAsync(() =>
            {
                var s = card.GetAxisStatus(0);
                return s is { Moving: false, InPosition: true } && Math.Abs(s.PositionMm - 100) <= 0.5;
            }, 5000);
            Check("定位到 100mm 且 InPosition",
                move == MotionCommandResult.Accepted && arrived,
                $"Enqueue={move}，下发时={atDispatch:F3}，到位后={card.GetAxisStatus(0)?.PositionMm:F3}");

            // ---- 5. 点动 ----
            double before = card.GetAxisStatus(0)?.PositionMm ?? 0;
            var jog = card.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = 0,
                JogDirection = 1,
                VelocityMmPerS = 50,
            });
            await Task.Delay(200);
            var stop = card.Enqueue(new MotionCommand { Kind = MotionCommandKind.Stop, PhysicalAxis = 0, StopMode = 2 });
            bool stopped = await WaitAsync(() => card.GetAxisStatus(0)?.Moving == false, 3000);
            double after = card.GetAxisStatus(0)?.PositionMm ?? 0;
            Check("点动推进后能停住",
                jog == MotionCommandResult.Accepted && stop == MotionCommandResult.Accepted && stopped && after > before,
                $"before={before:F3} after={after:F3} stop={stop}");

            // ---- 6. 软限位由基类门禁拒绝（驱动看不到这条命令）----
            var outOfLimit = card.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = 0,
                TargetMm = 5000,
                VelocityMmPerS = 200,
            });
            Check("超软限位被基类拒绝",
                outOfLimit == MotionCommandResult.Rejected_InvalidArgument,
                $"{outOfLimit}（期望 Rejected_InvalidArgument）");

            // ---- 7. 未使能轴不许动（轴 1 未 Enable）----
            var notEnabled = card.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = 1,
                TargetMm = -10,
                VelocityMmPerS = 100,
            });
            Check("未使能轴被拒", notEnabled == MotionCommandResult.Rejected_NotConnected, $"{notEnabled}");

            // ---- 8. 输入点来自世界推导 ----
            bool din0 = card.ReadInput(0);
            Check("输入点可读", !din0 || din0, $"DI0={din0}");

            card.Disconnect();
        }
        finally
        {
            try { if (!sim.HasExited) sim.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
        }

        Console.WriteLine($"=== {_pass} ok / {_fail} fail ===");
        Console.WriteLine(_fail == 0 ? "RESULT: ALL_OK" : "RESULT: FAILED");
        return _fail == 0 ? 0 : 1;
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private static async Task<bool> WaitPortAsync(string host, int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port);
                return true;
            }
            catch
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    /// <summary>
    /// 仿真器是独立产品（不在本仓库内）。查找顺序：
    /// 1) 环境变量 LINESIM_EXE 指向的 exe；
    /// 2) 同级目录约定 D:\C#\LineSim\src\bin\Debug\net9.0-windows\LineSim.exe。
    /// 找不到返回空串，由调用方报错——绝不猜一个仓库内路径。
    /// </summary>
    private static string FindLineSim()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("LINESIM_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return Path.GetFullPath(fromEnv);

        foreach (string candidate in ExternalCandidates())
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExternalCandidates()
    {
        const string relative = @"src\bin\Debug\net9.0-windows\LineSim.exe";
        yield return Path.Combine(@"D:\C#\LineSim", relative);

        // 仓库根之外的兄弟目录：从当前目录逐级往上找 C#\LineSim
        string? dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return Path.Combine(dir, "LineSim", relative);
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
    }
}
