using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using VisionMaster;
using VisionMaster.Communications;

namespace CommStress;

/// <summary>
/// 通讯库压力测试台。
/// <para>自带 HSL 虚拟靶子服务器（ModbusTcpServer / SiemensS7Server），让 VisionMaster 的
/// AdvancedCommunicationManager 真连真读，从实测数据里找可优化点。</para>
/// <para>日志隔离：通信库内部 4 路日志全部无条件 Console.WriteLine，这里先把 Console.Out 换成
/// 计数器 Sink，压测报告一律走原始 stdout（_real），互不干扰。</para>
/// </summary>
internal static class Program
{
    private const string Host = "127.0.0.1";
    private const int ModbusPort = 1502;
    private const int S7Port = 1102;

    private static ModbusTcpServer? _modbusServer;
    private static SiemensS7Server? _s7Server;
    private static bool _s7Available;

    private static readonly Process Proc = Process.GetCurrentProcess();
    private static TextWriter _real = Console.Out;
    private static readonly CapturingWriter Sink = new();
    private static long _events;
    private static readonly List<string> Findings = new();

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        _real = Console.Out;
        Console.SetOut(Sink);

        int exit = 0;
        try
        {
            PrintHeader();

            if (!StartModbusServer())
            {
                Say("Modbus 靶子服务器启动失败，压测中止。");
                exit = 2;
                return exit;
            }

            StartS7Server();

            RunS1();
            RunS2();
            RunS3();
            RunS4();
            RunS5();
            RunS6();
            RunS7();
            RunS8();

            PrintFindings();
            PrintSinkTail();
        }
        catch (Exception ex)
        {
            Console.SetOut(_real);
            _real.WriteLine();
            _real.WriteLine("压测异常终止：" + ex);
            exit = 1;
        }
        finally
        {
            Console.SetOut(_real);
            try { _modbusServer?.ServerClose(); } catch { /* 关服务器失败无所谓 */ }
            try { _s7Server?.ServerClose(); } catch { /* 同上 */ }
        }

        return exit;
    }

    #region 输出与日志隔离

    private static void Say(string s = "") => _real.WriteLine(s);

    /// <summary>把通信库的四路 Console 日志吞进计数器，只留尾部若干行供人工核对</summary>
    private sealed class CapturingWriter : TextWriter
    {
        private readonly object _lock = new();
        private readonly Queue<string> _tail = new();
        private readonly StringBuilder _line = new();
        private const int TailLimit = 40;

        public long TotalLines;
        public long ErrorLines;
        public long WarnLines;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_lock)
            {
                if (value == '\n') { FlushLine(); return; }
                if (value == '\r') return;
                _line.Append(value);
                if (_line.Length > 512) FlushLine();
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            foreach (var c in value) Write(c);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        private void FlushLine()
        {
            var s = _line.ToString();
            _line.Clear();
            TotalLines++;
            if (s.Contains("[ERROR]")) ErrorLines++;
            else if (s.Contains("[WARNING]")) WarnLines++;
            _tail.Enqueue(s);
            while (_tail.Count > TailLimit) _tail.Dequeue();
        }

        public string[] Tail()
        {
            lock (_lock) return _tail.ToArray();
        }
    }

    #endregion

    #region 靶子服务器

    private static bool StartModbusServer()
    {
        Say("== S0a Modbus TCP 靶子服务器 ==");
        try
        {
            _modbusServer = new ModbusTcpServer
            {
                Port = ModbusPort,
                Station = 1,
                // 必须关：否则客户端站号对不上会被服务端直接拒答
                StationCheck = false,
                UseModbusRtuOverTcp = false,
                RequestDelayTime = 0,
            };
            _modbusServer.ServerStart();
        }
        catch (Exception ex)
        {
            Say($"  ServerStart 抛异常：{ex.Message}");
            return false;
        }

        using var probe = new ModbusTcpNet(Host, ModbusPort)
        {
            ConnectTimeOut = 2000,
            ReceiveTimeOut = 2000,
        };
        var w = probe.Write("x=3;0", (short)0x1234);
        var r = probe.Read("x=3;0", 1);
        bool ok = w.IsSuccess && r.IsSuccess && r.Content.Length == 2;

        Say($"  IsStarted={_modbusServer.IsStarted}  端口={_modbusServer.Port}  " +
            $"真连真读={ok}（写 {(w.IsSuccess ? "OK" : w.Message)} / 读 {(r.IsSuccess ? "OK" : r.Message)}）");
        if (!ok) Find("Modbus 靶子服务器无法完成"写→读"回环，压测数据不可信");
        Say();
        return ok;
    }

    private static void StartS7Server()
    {
        Say("== S0b Siemens S7 靶子服务器 ==");
        try
        {
            _s7Server = new SiemensS7Server { Port = S7Port };
            _s7Server.ServerStart();

            using var probe = new SiemensS7Net(SiemensPLCS.S1200, Host)
            {
                Port = S7Port,
                ConnectTimeOut = 2000,
                ReceiveTimeOut = 2000,
            };
            var w = probe.Write("DB1.DBW0", (short)0x1234);
            var r = probe.Read("DB1.DBW0", 2);
            _s7Available = w.IsSuccess && r.IsSuccess && r.Content.Length == 2;

            Say($"  IsStarted={_s7Server.IsStarted}  端口={_s7Server.Port}  " +
                $"真连真读={_s7Available}（写 {(w.IsSuccess ? "OK" : w.Message)} / 读 {(r.IsSuccess ? "OK" : r.Message)}）");
        }
        catch (Exception ex)
        {
            _s7Available = false;
            Say($"  ServerStart 抛异常：{ex.Message}");
        }

        if (!_s7Available)
            Find("S7 虚拟服务器未跑通（HSL 源码注释为"仅限商业授权"），S7 侧压测本轮无法覆盖");
        Say();
    }

    #endregion

    #region 配置与变量构造

    private static AdvancedCommunicationManager NewManager() => new()
    {
        AutoReconnectEnabled = true,
        GlobalReconnectIntervalMs = 1000,
        MaxReconnectAttempts = 0, // 0 = 不限次数
    };

    private static CommunicationConfig NewModbusConfig(
        string name, int cycleMs, params ScanGroupConfig[] groups)
    {
        var cfg = new CommunicationConfig(name, new ModbusTcpConfig
        {
            // ⚠ 必写：Type 默认值是 0(=ModbusTcp)，但 S7/串口配置漏写会被 Validate 判"协议类型不匹配"
            Type = CommunicationType.ModbusTcp,
            IpAddress = Host,
            Port = ModbusPort,
            TimeoutMs = 2000,
            ReadTimeoutMs = 3000,
            RetryCount = 1,
            RetryIntervalMs = 500,
        })
        {
            ReadCycleMs = cycleMs,
            AutoReconnect = true,
        };
        if (groups.Length > 0) cfg.ScanGroups = groups.ToList();
        return cfg;
    }

    private static CommunicationConfig NewS7Config(string name, int cycleMs, int retryMs = 500)
    {
        return new CommunicationConfig(name, new SiemensS7Config
        {
            Type = CommunicationType.SiemensS7,
            IpAddress = Host,
            Port = S7Port,
            TimeoutMs = 2000,
            ReadTimeoutMs = 3000,
            RetryCount = 1,
            RetryIntervalMs = retryMs,
        })
        {
            ReadCycleMs = cycleMs,
            AutoReconnect = true,
        };
    }

    private static void OnVarChanged(object? sender, object? e) => Interlocked.Increment(ref _events);

    private static void Reg(AdvancedCommunicationManager mgr, string conn, string name, string addr, string group = "")
    {
        var v = new CommunicationVariable
        {
            ConnectionName = conn,
            VariableName = name,
            Address = addr,
            ValueType = typeof(short).AssemblyQualifiedName!,
            ScanGroup = group,
            AccessMode = VariableAccessMode.ReadOnly,
        };
        v.ValueChanged += OnVarChanged;
        mgr.RegisterVariable(v);
    }

    /// <summary>Modbus 连续寄存器区注册：地址 x=3;(startReg + i*step)</summary>
    private static void RegModbusBlock(
        AdvancedCommunicationManager mgr, string conn, int startReg, int count, int step, string group = "")
    {
        for (int i = 0; i < count; i++)
            Reg(mgr, conn, $"{group}V{i}", "x=3;" + (startReg + i * step), group);
    }

    /// <summary>S7 连续字区注册：地址 DB1.DBW(i*2)</summary>
    private static void RegS7Block(AdvancedCommunicationManager mgr, string conn, int count)
    {
        for (int i = 0; i < count; i++)
            Reg(mgr, conn, "V" + i, "DB1.DBW" + (i * 2));
    }

    #endregion

    #region 度量

    private readonly record struct Metrics(
        long AllocBytes, int Gen0, int Gen1, int Gen2,
        TimeSpan Cpu, int Threads, long WorkingSet);

    private static Metrics Capture() => new(
        GC.GetTotalAllocatedBytes(false),
        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
        Proc.TotalProcessorTime, Proc.Threads.Count, Proc.WorkingSet64);

    private sealed class Measurement
    {
        public string Conn = "";
        public int PointCount;
        public double WindowSec;
        public long Events;
        public double AllocBytesPerPointPerSec;
        public double AllocMbPerSec;
        public double CpuPercent;
        public int Gen0;
        public int Gen1;
        public int Gen2;
        public int Threads;
        public double WorkingSetMb;
        public IReadOnlyList<ScanGroupStats> Stats = Array.Empty<ScanGroupStats>();
        public double TotalReqPerSec;
        public double WorstAchieve = 1.0;
        public int TotalFallbacks;
    }

    private static Measurement Measure(
        AdvancedCommunicationManager mgr, string conn, string note,
        int warmupMs, int windowMs, int pointCount)
    {
        Thread.Sleep(warmupMs);
        Interlocked.Exchange(ref _events, 0);
        var before = Capture();

        var sw = Stopwatch.StartNew();
        Thread.Sleep(windowMs);
        sw.Stop();

        var after = Capture();
        var stats = mgr.GetScanGroupStats(conn);

        var m = new Measurement
        {
            Conn = conn,
            PointCount = pointCount,
            WindowSec = sw.Elapsed.TotalSeconds,
            Events = Interlocked.Read(ref _events),
            AllocMbPerSec = (after.AllocBytes - before.AllocBytes) / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds,
            CpuPercent = (after.Cpu - before.Cpu).TotalMilliseconds
                         / sw.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100.0,
            Gen0 = after.Gen0 - before.Gen0,
            Gen1 = after.Gen1 - before.Gen1,
            Gen2 = after.Gen2 - before.Gen2,
            Threads = after.Threads,
            WorkingSetMb = after.WorkingSet / 1024.0 / 1024.0,
            Stats = stats,
        };

        if (pointCount > 0 && sw.Elapsed.TotalSeconds > 0)
            m.AllocBytesPerPointPerSec =
                (after.AllocBytes - before.AllocBytes) / sw.Elapsed.TotalSeconds / pointCount;

        foreach (var s in stats)
        {
            int cycle = s.AvgActualMs > 0 ? s.AvgActualMs : s.TargetIntervalMs;
            m.TotalReqPerSec += s.SegmentCount * 1000.0 / Math.Max(1, cycle);
            m.TotalFallbacks += s.FallbackCount;
            if (s.AvgActualMs > 0 && s.AchieveRate < m.WorstAchieve) m.WorstAchieve = s.AchieveRate;
        }

        Say($"  [{conn}] {note}");
        PrintGroupTable(stats);
        Say($"    合计：{m.TotalReqPerSec:F0} 请求/s   值变化 {m.Events} 次   兜底 {m.TotalFallbacks} 次   " +
            $"CPU {m.CpuPercent:F1}%   分配 {m.AllocMbPerSec:F2} MB/s   线程 {m.Threads}   内存 {m.WorkingSetMb:F0} MB");
        Say();
        return m;
    }

    private static void PrintGroupTable(IReadOnlyList<ScanGroupStats> stats)
    {
        if (stats.Count == 0)
        {
            Say("    （无扫描组数据）");
            return;
        }

        Say($"    {"组名",-12}{"变量",7}{"段数",7}{"兜底",7}{"目标ms",9}{"实测ms",9}{"达成率",9}{"坏段",6}{"点/段",8}{"请求/s",9}");
        foreach (var s in stats)
        {
            int cycle = s.AvgActualMs > 0 ? s.AvgActualMs : s.TargetIntervalMs;
            double reqPerSec = s.SegmentCount * 1000.0 / Math.Max(1, cycle);
            double perSeg = s.SegmentCount > 0 ? (double)s.VariableCount / s.SegmentCount : 0;
            Say($"    {s.GroupName,-12}{s.VariableCount,7}{s.SegmentCount,7}{s.FallbackCount,7}" +
                $"{s.TargetIntervalMs,9}{s.AvgActualMs,9}{s.AchieveRate * 100,8:F0}%{s.FaultedSegmentCount,6}" +
                $"{perSeg,8:F1}{reqPerSec,9:F0}");
            if (s.FaultDetail != null) Say($"      └ 坏段：{s.FaultDetail}");
        }
    }

    #endregion

    #region 场景

    private static void RunS1()
    {
        Say("== S1 单连接 5000 点连续区 @1000ms（批量合并效率基线）==");
        const int points = 5000;
        var cfg = NewModbusConfig("S1_Modbus", 1000);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, points, 1);

        var preview = mgr.GetScanGroupPreview(cfg.ConnectionName);
        Say($"  静态画像（未连接即可算）：段数 {preview.FirstOrDefault().SegmentCount}，兜底 {preview.FirstOrDefault().FallbackCount}");
        Say($"  期望段数 ≈ ceil(5000/120) = 42");

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, $"{points} 点 / 连续 / 1 寄存器步长", 2000, 5000, points);

        int seg = m.Stats.FirstOrDefault().SegmentCount;
        if (seg > 45) Find($"S1 段数 {seg} 高于期望 42，段合并阈值可能偏保守（Modbus GapUnitsRegister=8）");
    }

    private static void RunS2()
    {
        Say("== S2 单连接 20000 点 @100ms（吞吐上限）==");
        const int points = 20000;
        var cfg = NewModbusConfig("S2_Modbus", 100);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, points, 1);

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, $"{points} 点 / 10Hz 目标", 3000, 8000, points);

        Say($"  期望段数 ≈ ceil(20000/120) = 167 → 约 1670 请求/s");
        if (m.WorstAchieve < 0.9)
            Find($"S2 实测周期未达成目标（最差达成率 {m.WorstAchieve * 100:F0}%）：单连接串行 + 单 socket 往返是物理上限");
        if (m.AllocBytesPerPointPerSec > 200)
            Find($"S2 每点每秒分配 {m.AllocBytesPerPointPerSec:F0} 字节（20000 点 @10Hz ≈ " +
                 $"{m.AllocBytesPerPointPerSec * points / 1024.0 / 1024.0:F1} MB/s）：ApplyBytes 每变量每次轮询都 new byte[]，热路径可零分配化");
    }

    private static void RunS3()
    {
        Say("== S3 稀疏地址 1000 点 step=10（间隔 9 > 合并阈值 8）==");
        const int points = 1000;
        var cfg = NewModbusConfig("S3_Modbus", 1000);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, points, 10);

        var preview = mgr.GetScanGroupPreview(cfg.ConnectionName).FirstOrDefault();
        Say($"  静态画像：段数 {preview.SegmentCount}，点/段 {(preview.SegmentCount > 0 ? (double)preview.VariableCount / preview.SegmentCount : 0):F2}");
        Say($"  期望：每点独立成段（段数 ≈1000、点/段 = 1.0）");

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, $"{points} 点 / 步长 10", 2000, 5000, points);

        if (m.Stats.FirstOrDefault().SegmentCount > 500)
            Find("S3 稀疏点位产生近千个单点段（1000 请求/轮）：建议增加"段内允许空档"或点位密度自适应，把稀疏区按更大跨度整体读回后本地裁剪");
    }

    private static void RunS4()
    {
        Say("== S4 单管理器 8 连接 × 1000 点 @100ms（多连接并发）==");
        const int conns = 8;
        const int points = 1000;

        using var mgr = NewManager();
        var names = new List<string>();
        for (int c = 0; c < conns; c++)
        {
            var cfg = NewModbusConfig($"S4_Conn{c}", 100);
            if (!mgr.AddConnection(cfg)) { Say($"  AddConnection 失败：{cfg.ConnectionName}"); return; }
            // 每条连接独占一段寄存器区，避免互相覆盖
            RegModbusBlock(mgr, cfg.ConnectionName, c * 2000, points, 1);
            names.Add(cfg.ConnectionName);
        }

        var sw = Stopwatch.StartNew();
        foreach (var n in names)
            if (!mgr.Connect(n)) { Say($"  连接失败：{n}"); return; }
        sw.Stop();
        Say($"  8 条连接全部就绪耗时 {sw.ElapsedMilliseconds} ms（串行 Connect）");

        var before = Capture();
        var w = Stopwatch.StartNew();
        Thread.Sleep(3000);
        Thread.Sleep(8000);
        w.Stop();
        var after = Capture();

        var all = mgr.GetAllConnections();
        double totalReq = 0;
        int totalSeg = 0;
        foreach (var n in names)
        {
            var stats = mgr.GetScanGroupStats(n);
            var s = stats.FirstOrDefault();
            totalSeg += s.SegmentCount;
            int cycle = s.AvgActualMs > 0 ? s.AvgActualMs : s.TargetIntervalMs;
            totalReq += s.SegmentCount * 1000.0 / Math.Max(1, cycle);
            Say($"    {n,-10} 段 {s.SegmentCount,4}  实测 {s.AvgActualMs,5} ms  达成率 {s.AchieveRate * 100,3:F0}%  坏段 {s.FaultedSegmentCount}");
        }

        Say($"  合计：{conns} 连接 / {totalSeg} 段/轮 / {totalReq:F0} 请求/s   " +
            $"CPU {(after.Cpu - before.Cpu).TotalMilliseconds / 11000.0 / Environment.ProcessorCount * 100:F1}%   " +
            $"线程 {after.Threads}   内存 {after.WorkingSet / 1024.0 / 1024.0:F0} MB   " +
            $"已连接 {mgr.ConnectedCount}/{all.Count}");
        Say();

        if (after.Threads - before.Threads > conns + 6)
            Find($"S4 线程数从 {before.Threads} 涨到 {after.Threads}：一条连接一个专用线程（N3），连接数上到几百条时线程与栈内存会成主要开销");
    }

    private static void RunS5()
    {
        Say("== S5 单连接 4 组混合周期（快组是否被慢组拖慢）==");
        var cfg = NewModbusConfig("S5_Modbus", 500,
            new ScanGroupConfig("G_100ms", 100),
            new ScanGroupConfig("G_1000ms", 1000),
            new ScanGroupConfig("G_10000ms", 10000));

        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, 500, 1);                       // 默认组 500ms
        RegModbusBlock(mgr, cfg.ConnectionName, 1000, 500, 1, "G_100ms");
        RegModbusBlock(mgr, cfg.ConnectionName, 2000, 500, 1, "G_1000ms");
        RegModbusBlock(mgr, cfg.ConnectionName, 3000, 500, 1, "G_10000ms");

        var names = mgr.GetScanGroupNames(cfg.ConnectionName);
        Say($"  组表：{string.Join(" / ", names)}");

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, "4 组 × 500 点，周期 100/500/1000/10000 ms", 3000, 20000, 2000);

        var fast = m.Stats.FirstOrDefault(s => s.GroupName == "G_100ms");
        var slow = m.Stats.FirstOrDefault(s => s.GroupName == "G_10000ms");
        if (fast.AvgActualMs > 0 && slow.AvgActualMs > 0)
        {
            Say($"  快组(100ms) 实测 {fast.AvgActualMs}ms 达成率 {fast.AchieveRate * 100:F0}%；" +
                $"慢组(10s) 实测 {slow.AvgActualMs}ms 达成率 {slow.AchieveRate * 100:F0}%");
            if (fast.AchieveRate < 0.8)
                Find($"S5 100ms 快组达成率仅 {fast.AchieveRate * 100:F0}%：PollScheduler.Run 严格按周期升序串行，慢组那一拍会顶住整条链");
        }
    }

    private static void RunS6()
    {
        Say("== S6 写命令风暴（写优先 N1 + 真异步 B7）==");
        var cfg = NewModbusConfig("S6_Modbus", 1000);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        // 故意不注册变量：本场景只测写链路，避免轮询干扰

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        Thread.Sleep(500);
        Say();

        // ① 单线程同步写基线
        var (avg1, p99_1, ok1) = RunSyncWrites(mgr, cfg.ConnectionName, 0, 200, 1);
        Say($"  ① 单线程同步写 200 次：平均 {avg1:F2} ms，p99 {p99_1:F2} ms，成功 {ok1}/200");

        // ② TriggerWrite 入队速率
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20000; i++)
            mgr.TriggerWrite(cfg.ConnectionName, "x=3;" + (i % 500), (short)i, typeof(short));
        sw.Stop();
        Say($"  ② TriggerWrite 20000 次入队耗时 {sw.ElapsedMilliseconds} ms（{20000.0 / Math.Max(1, sw.ElapsedMilliseconds) * 1000:F0} 次/s）");

        // ③ 积压下立刻同步写：看写队列是否真的优先
        for (int i = 0; i < 4000; i++)
            mgr.TriggerWrite(cfg.ConnectionName, "x=3;" + (i % 500), (short)i, typeof(short));
        var (avg3, p99_3, ok3) = RunSyncWrites(mgr, cfg.ConnectionName, 0, 100, 1);
        Say($"  ③ 积压 4000 条后同步写 100 次：平均 {avg3:F2} ms，p99 {p99_3:F2} ms，成功 {ok3}/100");

        Thread.Sleep(3000); // 等积压消化

        // ④ 8 线程并发同步写
        var (avg4, p99_4, ok4) = RunSyncWrites(mgr, cfg.ConnectionName, 0, 500, 8);
        Say($"  ④ 8 线程 × 500 次同步写：平均 {avg4:F2} ms，p99 {p99_4:F2} ms，成功 {ok4}/4000");

        // ⑤ 回读校验
        int verified = 0;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                mgr.Write(cfg.ConnectionName, "x=3;" + i, (short)(1000 + i));
                var back = mgr.Read<short>(cfg.ConnectionName, "x=3;" + i);
                if (back == 1000 + i) verified++;
            }
            catch { /* 单点失败只计入失败数 */ }
        }
        Say($"  ⑤ 写后回读校验：{verified}/20 一致");
        Say();

        if (p99_1 > 50)
            Find($"S6 单线程同步写 p99 {p99_1:F0} ms：同步 Write 走 InvokeWrite(...).GetAwaiter().GetResult()，写队列满时会阻塞调用线程");
        if (p99_3 > p99_1 * 3 && p99_3 > 20)
            Find($"S6 积压 4000 条后同步写 p99 从 {p99_1:F0}ms 涨到 {p99_3:F0}ms：写队列虽有优先权，但仍按 FIFO 排在新命令前面，突发写入会顶住后续同步写");
        if (verified < 20)
            Find($"S6 写后回读有 {20 - verified} 点不一致，需排查端序/写链路");
        if (p99_4 > 500)
            Find($"S6 8 线程并发写 p99 {p99_4:F0} ms：所有写命令串行到单 Worker 线程，并发写只是排队");
    }

    private static (double avg, double p99, int ok) RunSyncWrites(
        AdvancedCommunicationManager mgr, string conn, int baseReg, int perThread, int threads)
    {
        int total = perThread * threads;
        var lat = new double[total];
        int ok = 0;

        if (threads <= 1)
        {
            for (int i = 0; i < perThread; i++)
            {
                var t0 = Stopwatch.GetTimestamp();
                try
                {
                    mgr.Write(conn, "x=3;" + (baseReg + i % 100), (short)i);
                    Interlocked.Increment(ref ok);
                }
                catch { /* 失败只计数 */ }
                lat[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }
        }
        else
        {
            Parallel.For(0, threads, t =>
            {
                for (int i = 0; i < perThread; i++)
                {
                    int idx = t * perThread + i;
                    var t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        mgr.Write(conn, "x=3;" + (baseReg + i % 100), (short)i);
                        Interlocked.Increment(ref ok);
                    }
                    catch { /* 失败只计数 */ }
                    lat[idx] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                }
            });
        }

        Array.Sort(lat);
        double avg = lat.Average();
        int p99Idx = Math.Min(lat.Length - 1, (int)Math.Floor(lat.Length * 0.99));
        return (avg, lat[p99Idx], ok);
    }

    private static void RunS7()
    {
        Say("== S7 西门子 S7 协议对照：5000 个 DB1.DBW @1000ms ==");
        if (!_s7Available)
        {
            Say("  S7 靶子服务器不可用，跳过。");
            Say();
            return;
        }

        const int points = 5000;
        var cfg = NewS7Config("S7_Conn", 1000);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegS7Block(mgr, cfg.ConnectionName, points);

        var preview = mgr.GetScanGroupPreview(cfg.ConnectionName).FirstOrDefault();
        Say($"  静态画像：段数 {preview.SegmentCount}，兜底 {preview.FallbackCount}");
        Say($"  期望段数 ≈ ceil(10000 字节 / 110) = 91");

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, $"{points} 字 / DB1 连续 / 2 字节步长", 2000, 5000, points);

        if (m.Stats.FirstOrDefault().SegmentCount > 100)
            Find($"S7 段数 {m.Stats.FirstOrDefault().SegmentCount} 高于期望 91，MaxSegmentUnits(S7=110) 与 GapUnitsS7=16 的组合可再调");
    }

    private static void RunS8()
    {
        Say("== S8 断线重连：靶子服务器关停→恢复（RetryIntervalMs=500）==");
        var cfg = NewModbusConfig("S8_Modbus", 200);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, 500, 1);

        var transitions = new List<(string from, string to, long ms)>();
        var t0 = Stopwatch.StartNew();
        mgr.ConnectionStateChanged += (_, e) =>
        {
            lock (transitions) transitions.Add((e.OldState.ToString(), e.NewState.ToString(), t0.ElapsedMilliseconds));
        };

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        Thread.Sleep(2000);
        long errBefore = Sink.ErrorLines;
        Say($"  已连接，开始关停靶子服务器…");

        // 关服务器 → 量掉线检测耗时
        _modbusServer!.ServerClose();
        long lostAt = WaitState(mgr, cfg.ConnectionName, ConnectionState.Connected, want: false, timeoutMs: 15000);
        Say($"  掉线检测耗时：{(lostAt < 0 ? "超时未检测到" : lostAt + " ms")}（轮询周期 200ms + 读超时 3000ms）");

        // 恢复服务器 → 量重连耗时
        _modbusServer.ServerStart();
        long backAt = WaitState(mgr, cfg.ConnectionName, ConnectionState.Connected, want: true, timeoutMs: 30000);
        Say($"  重连恢复耗时：{(backAt < 0 ? "超时未恢复" : backAt + " ms")}（退避基准 500ms）");

        Thread.Sleep(1500);
        var vars = mgr.GetScanGroupStats(cfg.ConnectionName).FirstOrDefault();
        var conns = mgr.GetAllConnections();
        var st = conns.FirstOrDefault(c => c.ConnectionName == cfg.ConnectionName);
        Say($"  恢复后状态：{st?.State}  坏段 {vars.FaultedSegmentCount}  最后错误 {(st?.HasError == true ? st.LastError : "无")}");
        Say($"  通信库错误日志行数：{Sink.ErrorLines - errBefore}（这段时间内）");
        Say($"  状态迁移轨迹：{string.Join(" → ", transitions.Select(x => $"{x.from}→{x.to}@{x.ms}ms"))}");
        Say();

        if (lostAt < 0) Find("S8 服务器关停后 15s 内未检测到掉线，掉线检测偏慢");
        else if (lostAt > 5000) Find($"S8 掉线检测耗时 {lostAt} ms（轮询周期 200ms + ReceiveTimeOut 3000ms）：单次读超时决定了下限，可考虑按周期自适应收紧读超时");
        if (backAt < 0) Find("S8 服务器恢复后 30s 内未重连成功，重连链路有问题");
        if (Sink.ErrorLines - errBefore > 50)
            Find($"S8 一次断线刷了 {Sink.ErrorLines - errBefore} 行错误日志：按"每次失败都打"产生日志风暴，建议按连接+地址限流");
    }

    private static long WaitState(
        AdvancedCommunicationManager mgr, string conn, ConnectionState target, bool want, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var st = mgr.GetAllConnections().FirstOrDefault(c => c.ConnectionName == conn);
            bool isTarget = st != null && st.State == target;
            if (isTarget == want) return sw.ElapsedMilliseconds;
            Thread.Sleep(50);
        }
        return -1;
    }

    #endregion

    #region 报告

    private static void PrintHeader()
    {
        Say("================================================================");
        Say(" VisionMaster 通讯库压力测试（HSL 虚拟靶子服务器）");
        Say("================================================================");
        Say($" 时间     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Say($" 进程     : {Proc.ProcessName} (PID {Proc.Id})");
        Say($" 运行时   : {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Say($" 逻辑核数 : {Environment.ProcessorCount}");
        Say($" 靶子     : ModbusTCP {Host}:{ModbusPort}   S7 {Host}:{S7Port}");
        Say();
    }

    private static void PrintFindings()
    {
        Say("================================================================");
        Say(" 可优化点清单（由实测数据生成）");
        Say("================================================================");
        if (Findings.Count == 0)
        {
            Say(" 本轮未触发任何阈值告警。");
        }
        else
        {
            for (int i = 0; i < Findings.Count; i++)
                Say($" {i + 1}. {Findings[i]}");
        }
        Say();
    }

    private static void PrintSinkTail()
    {
        Say("================================================================");
        Say($" 通信库日志统计：共 {Sink.TotalLines} 行，其中 ERROR {Sink.ErrorLines} 行 / WARNING {Sink.WarnLines} 行");
        Say(" 尾部 40 行：");
        Say("----------------------------------------------------------------");
        foreach (var line in Sink.Tail())
            Say(" " + line);
        Say("================================================================");
    }

    private static void Find(string text) => Findings.Add(text);

    #endregion
}
