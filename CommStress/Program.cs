using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json;
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

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        _real = Console.Out;
        Console.SetOut(Sink);

        bool diagOnly = args.Length > 0 && args[0] == "diag";

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

            if (diagOnly)
            {
                RunDiag();
                PrintFindings();
                PrintSinkTail();
                return 0;
            }

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
        if (!ok) Find("Modbus 靶子服务器无法完成「写→读」回环，压测数据不可信");
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
            Find("S7 虚拟服务器未跑通（HSL 源码注释为「仅限商业授权」），S7 侧压测本轮无法覆盖");
        Say();
    }

    /// <summary>把"配置 → 连接实现 → Manager"三段链路逐层拆开，定位连接失败发生在哪一层</summary>
    private static void RunDiag()
    {
        Say("== Diagnostic：逐层拆开连接链路 ==");

        // 保存"传进去的那个实例"的引用，后面靠它做引用同一性比对
        var original = new ModbusTcpConfig
        {
            Type = CommunicationType.ModbusTcp,
            IpAddress = Host,
            Port = ModbusPort,
            TimeoutMs = 2000,
            ReadTimeoutMs = 3000,
            RetryCount = 1,
            RetryIntervalMs = 500,
        };
        var cfg = new CommunicationConfig("Diag_Modbus", original) { ReadCycleMs = 1000, AutoReconnect = true };
        var mcfg = cfg.Config as ModbusTcpConfig;
        Say($"  传入的 original ：Type={original.Type}  TimeoutMs={original.TimeoutMs}  " +
            $"ReadTimeoutMs={original.ReadTimeoutMs}  Ip={original.IpAddress}  Port={original.Port}");
        Say($"  cfg.Config 实际 ：Type={mcfg?.Type}  TimeoutMs={mcfg?.TimeoutMs}  " +
            $"ReadTimeoutMs={mcfg?.ReadTimeoutMs}  Ip={mcfg?.IpAddress}  Port={mcfg?.Port}");
        Say($"  引用同一性 ReferenceEquals(cfg.Config, original) = {ReferenceEquals(cfg.Config, original)}" +
            "   ← True 即「2 参构造器把传入配置原样落了进去」（修复前恒为 False）");

        // ① 裸 HSL：无参构造 + 赋 Ip/Port（与 ModbusTcpConnection 完全同构）
        var raw = new ModbusTcpNet
        {
            IpAddress = Host,
            Port = ModbusPort,
            ConnectTimeOut = 2000,
            ReceiveTimeOut = 3000,
        };
        var rc = raw.ConnectServer();
        Say($"  ① HSL 裸连（无参构造 + 赋 Ip/Port）：IsSuccess={rc.IsSuccess}  " +
            $"ErrorCode={rc.ErrorCode}  Msg={rc.Message}  回读Ip/Port={raw.IpAddress}:{raw.Port}");
        if (rc.IsSuccess)
        {
            var rr = raw.Read("x=3;0", 1);
            Say($"     读 x=3;0 → IsSuccess={rr.IsSuccess}  Msg={rr.Message}");
            raw.ConnectClose();
        }

        // ② 裸 HSL：带参构造（对照组）
        var raw2 = new ModbusTcpNet(Host, ModbusPort) { ConnectTimeOut = 2000, ReceiveTimeOut = 3000 };
        var rc2 = raw2.ConnectServer();
        Say($"  ② HSL 带参构造 new ModbusTcpNet(ip, port)：IsSuccess={rc2.IsSuccess}  " +
            $"ErrorCode={rc2.ErrorCode}  Msg={rc2.Message}");
        raw2.ConnectClose();

        // ③ 产品连接实现（用 cfg.Config —— 即"构造之后实际留在配置里的那一份"）
        using (var conn = new ModbusTcpConnection((ModbusTcpConfig)cfg.Config!))
        {
            bool ok3 = conn.Connect();
            Say($"  ③ ModbusTcpConnection.Connect()（用 cfg.Config）：{ok3}   实际目标={conn.ConnectionName}");
            if (ok3) conn.Disconnect();
        }

        // ③c 产品连接实现（用 original —— 我本意要连的那一份 1502）
        using (var conn = new ModbusTcpConnection(original))
        {
            bool ok3c = conn.Connect();
            Say($"  ③c ModbusTcpConnection.Connect()（用 original）：{ok3c}   实际目标={conn.ConnectionName}");
            if (ok3c) conn.Disconnect();
        }

        // ★ ③b 结论：CommunicationConfig(name, config) 是否把传进来的 config 原样保留
        Say($"  ③b 引用同一性 ReferenceEquals(cfg.Config, 传入的 original) = {ReferenceEquals(cfg.Config, original)}" +
            "（True = 传入配置被原样保留；修复前恒为 False，即被默认配置顶掉）");

        // ④ 走 Manager 全景
        using (var mgr = NewManager())
        {
            try { mgr.AddConnection(cfg); }
            catch (Exception ex) { Say($"  ④ AddConnection 抛异常：{ex.Message}"); return; }

            Say($"  ④ AddConnection 后：cfg.Config?.TimeoutMs={cfg.Config?.TimeoutMs}  cfg.State={cfg.State}");
            bool ok4 = mgr.Connect(cfg.ConnectionName);
            var st = mgr.GetAllConnections().FirstOrDefault(c => c.ConnectionName == cfg.ConnectionName);
            var impl = mgr.GetConnection(cfg.ConnectionName);
            Say($"  ④ Manager.Connect()：{ok4}   最终 State={st?.State}   " +
                $"LastError={(st?.HasError == true ? st.LastError : "无")}");
            Say($"  ④ GetConnection 实现：{(impl == null ? "null" : impl.GetType().Name)}  IsConnected={impl?.IsConnected}");
        }

        // ⑤ 回归：不再需要任何二次赋值，2 参构造器直接把 original 落进去，连接就应当成功
        using (var mgr = NewManager())
        {
            var cfg2 = new CommunicationConfig("Diag_Modbus2", original) { ReadCycleMs = 1000, AutoReconnect = true };
            mgr.AddConnection(cfg2);
            bool ok5 = mgr.Connect(cfg2.ConnectionName);
            var impl2 = mgr.GetConnection(cfg2.ConnectionName);
            Say($"  ⑤ 只用 2 参构造器（不二次赋 Config）→ Manager.Connect()：{ok5}   IsConnected={impl2?.IsConnected}" +
                "（True = 构造器已修；修复前这里会失败，必须靠构造后再赋一次 Config 才能连上）");
        }

        // ⑥ JSON 往返：验证"存盘 → 读盘"这条路同样安全
        var cfg3 = new CommunicationConfig("Diag_Modbus3", original) { ReadCycleMs = 1000, AutoReconnect = true };
        string json = JsonConvert.SerializeObject(cfg3, Formatting.None);
        var back = JsonConvert.DeserializeObject<CommunicationConfig>(json);
        var backCfg = back?.Config as ModbusTcpConfig;
        Say($"  ⑥ JSON 往返后：Port={backCfg?.Port}  TimeoutMs={backCfg?.TimeoutMs}  " +
            $"（1502 / 2000 即「存盘读盘安全」，本坑只在 2 参构造这条 API 上）");
        Say($"     JSON 里 Protocol 与 Config 的先后：Protocol@{json.IndexOf("\"Protocol\"", StringComparison.Ordinal)}  " +
            $"Config@{json.IndexOf("\"Config\"", StringComparison.Ordinal)}（Protocol 在前 → 反序列化先建默认、后被真值覆盖）");

        // ⑦ 无 WPF 宿主下 config.State 是否可用：SafeDispatch.BeginInvoke 在 Application.Current == null
        //    时直接 return，而 OnWorkerStateChanged 把 `config.State = newState` 正放在这条会被丢弃的路径里。
        using (var mgr = NewManager())
        {
            var cfg4 = NewModbusConfig("Diag_State", 1000);
            bool added = mgr.AddConnection(cfg4);
            bool conn4 = mgr.Connect(cfg4.ConnectionName);
            Thread.Sleep(1500); // 留足时间让 Worker 状态机走到 Connected 并尝试回写 config.State

            var snap = mgr.GetAllConnections().FirstOrDefault(c => c.ConnectionName == cfg4.ConnectionName);
            bool implConnected = mgr.GetConnection(cfg4.ConnectionName)?.IsConnected ?? false;
            var newApiState = mgr.GetConnectionState(cfg4.ConnectionName);
            Say($"  ⑦ AddConnection={added}  Manager.Connect={conn4}  等待 1500ms 后：");
            Say($"     config.State（GetAllConnections 返回的 UI 绑定字段）= {snap?.State}");
            Say($"     GetConnection(name).IsConnected（连接实现，真实值）  = {implConnected}");
            Say($"     GetConnectionState(name)（新增：读 Worker，不经 Dispatcher） = {newApiState}" +
                "   ← 非 WPF 宿主下应当用这一条");
            Say($"     Application.Current = {(System.Windows.Application.Current == null ? "null（无 WPF 宿主）" : "非空")}" +
                "   ← null 即 SafeDispatch.BeginInvoke 直接丢弃，config.State 永远是 Disconnected");
            if (implConnected && snap?.State != ConnectionState.Connected)
                Find("【Headless 状态失真】无 WPF 宿主时 GetAllConnections()[i].State 恒为 Disconnected（SafeDispatch 丢弃回写），" +
                     "控制台/Windows 服务/单元测试里查询连接状态会得到错误结果；已新增 GetConnectionState(name) / GetConnectionError(name) 读 Worker 绕开该限制");
        }

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
        => NewModbusConfigCore(name, cycleMs, ModbusPort, groups);

    /// <summary>可指定目标端口的版本（S8 要连 TCP 继电器，故不能固定 ModbusPort）</summary>
    private static CommunicationConfig NewModbusConfigCore(
        string name, int cycleMs, int port, ScanGroupConfig[] groups)
    {
        var link = new ModbusTcpConfig
        {
            // ⚠ 必写：Type 默认值是 0(=ModbusTcp)，但 S7/串口配置漏写会被 Validate 判"协议类型不匹配"
            Type = CommunicationType.ModbusTcp,
            IpAddress = Host,
            Port = port,
            TimeoutMs = 2000,
            ReadTimeoutMs = 3000,
            RetryCount = 1,
            RetryIntervalMs = 500,
        };
        // 2 参构造器内部按 Protocol 先、Config 后的顺序赋值，传入的 link 会原样落进去（曾经不是，
        // 修好后这里不再需要"构造之后再赋一次 Config"的绕行；diag 模式 ③/③b/⑤ 是这条结论的回归探针）。
        var cfg = new CommunicationConfig(name, link)
        {
            ReadCycleMs = cycleMs,
            AutoReconnect = true,
        };
        if (groups.Length > 0) cfg.ScanGroups = groups.ToList();
        return cfg;
    }

    private static CommunicationConfig NewS7Config(string name, int cycleMs, int retryMs = 500)
    {
        var link = new SiemensS7Config
        {
            Type = CommunicationType.SiemensS7,
            IpAddress = Host,
            Port = S7Port,
            TimeoutMs = 2000,
            ReadTimeoutMs = 3000,
            RetryCount = 1,
            RetryIntervalMs = retryMs,
        };
        // 同上：2 参构造器已修，无需再二次赋 Config
        return new CommunicationConfig(name, link)
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
        Say("== S3 稀疏地址 1000 点 step=10（相邻间隔 9）==");
        const int points = 1000;
        var cfg = NewModbusConfig("S3_Modbus", 1000);
        using var mgr = NewManager();
        if (!mgr.AddConnection(cfg)) { Say("  AddConnection 失败"); return; }
        RegModbusBlock(mgr, cfg.ConnectionName, 0, points, 10);

        var preview = mgr.GetScanGroupPreview(cfg.ConnectionName).FirstOrDefault();
        Say($"  静态画像：段数 {preview.SegmentCount}，点/段 {(preview.SegmentCount > 0 ? (double)preview.VariableCount / preview.SegmentCount : 0):F2}");
        // 空档容忍按传输分档（见 PollTransportKind）：
        //   以太网 = 单请求上限 120 → 一段装 12 个点（10×11+1 = 111 ≤ 120）→ 段数 ≈ 84、点/段 ≈ 11.9
        //   串口   = 保守常数 8     → 9 > 8 → 每点自成一段 → 段数 ≈ 1000、点/段 = 1.0
        // 本机靶子是 ModbusTcp，故期望 84 段；修复前不论传输都按 8 走，拿到的是 1000 段。
        Say($"  期望（以太网档）：段数 ≈ 84、点/段 ≈ 11.9；若走串口档则仍是 ≈1000 段 / 1.0");

        if (!mgr.Connect(cfg.ConnectionName)) { Say("  连接失败"); return; }
        var m = Measure(mgr, cfg.ConnectionName, $"{points} 点 / 步长 10", 2000, 5000, points);

        if (m.Stats.FirstOrDefault().SegmentCount > 500)
            Find("S3 稀疏点位仍产生近千个单点段（1000 请求/轮）：说明该连接走的是串口档（阈值 8）或点表步长超过单请求上限，无法靠放宽空档合并");
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
        const int relayPort = 1602;
        Say($"== S8 断线重连：经 TCP 继电器 {relayPort} → {ModbusPort}（RetryIntervalMs=500）==");

        using var relay = new TcpRelay(relayPort, ModbusPort);
        relay.Start();

        // 继电器自检：不通就说明压测台自身有问题，结论不可信，直接收摊
        using (var probe = new ModbusTcpNet(Host, relayPort) { ConnectTimeOut = 2000, ReceiveTimeOut = 2000 })
        {
            var pr = probe.Read("x=3;0", 1);
            Say($"  继电器自检：读 x=3;0 IsSuccess={pr.IsSuccess}  Msg={pr.Message}");
            if (!pr.IsSuccess)
            {
                Find("S8 TCP 继电器不通，断线重连结论不可信");
                Say();
                return;
            }
        }

        var cfg = NewModbusConfigCore("S8_Modbus", 200, relayPort, Array.Empty<ScanGroupConfig>());
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
        long warnBefore = Sink.WarnLines;
        long errBefore = Sink.ErrorLines;

        // ── 阶段 A：掐断继电器（客户端立刻收到连接关闭，等价于拔网线）→ 量掉线检测耗时
        Say("  [A] 掐断继电器（连接被关闭）…");
        relay.Stop();
        long lostAt = WaitState(mgr, cfg.ConnectionName, wantConnected: false, timeoutMs: 15000);
        Say($"      掉线检测耗时：{(lostAt < 0 ? "超时未检测到" : lostAt + " ms")}（轮询周期 200ms + 读超时 3000ms）");
        Thread.Sleep(4000); // 保持断开：确保每个段都真失败过，排除 HSL 在段间自愈把故障吞掉的可能

        // ── 阶段 B：恢复继电器 → 量重连耗时
        Say("  [B] 恢复继电器…");
        relay.Start();
        long backAt = WaitState(mgr, cfg.ConnectionName, wantConnected: true, timeoutMs: 30000);
        Say($"      重连恢复耗时：{(backAt < 0 ? "超时未恢复" : backAt + " ms")}（退避基准 500ms）");
        Thread.Sleep(1000);

        // ── 阶段 C：静默（TCP 还在、就是不回包）→ 只能靠读超时兜底，验证"半开"场景
        Say("  [C] 继电器静默（连接保持着，字节不再搬运）…");
        relay.Pause();
        long silentAt = WaitState(mgr, cfg.ConnectionName, wantConnected: false, timeoutMs: 20000);
        Say($"      静默掉线检测耗时：{(silentAt < 0 ? "超时未检测到" : silentAt + " ms")}（下限应为 ReadTimeoutMs=3000）");
        Thread.Sleep(2000);

        // ── 阶段 D：解除静默 → 量恢复
        Say("  [D] 解除静默…");
        relay.Resume();
        long silentBackAt = WaitState(mgr, cfg.ConnectionName, wantConnected: true, timeoutMs: 30000);
        Say($"      静默后恢复耗时：{(silentBackAt < 0 ? "超时未恢复" : silentBackAt + " ms")}");

        Thread.Sleep(1500);
        var vars = mgr.GetScanGroupStats(cfg.ConnectionName).FirstOrDefault();
        var st = mgr.GetAllConnections().FirstOrDefault(c => c.ConnectionName == cfg.ConnectionName);
        bool live = mgr.GetConnection(cfg.ConnectionName)?.IsConnected ?? false;
        // ★ 两个状态源对照：控制台（无 WPF 宿主）下 config.State 会被 SafeDispatch 静默丢弃，恒为 Disconnected
        Say($"  状态源对照：config.State={st?.State}（UI 绑定字段）  连接实现 IsConnected={live}（真实值）");
        Say($"  恢复后：坏段 {vars.FaultedSegmentCount}  最后错误 {(st?.HasError == true ? st.LastError : "无")}");
        Say($"  本轮日志增量：WARNING {Sink.WarnLines - warnBefore} 行 / ERROR {Sink.ErrorLines - errBefore} 行");
        Say($"  状态迁移轨迹：{string.Join(" → ", transitions.Select(x => $"{x.from}→{x.to}@{x.ms}ms"))}");
        Say();

        long warnDelta = Sink.WarnLines - warnBefore;
        if (lostAt < 0) Find("S8[A] 掐线后 15s 内未检测到掉线，掉线检测偏慢");
        else if (lostAt > 5000) Find($"S8[A] 掐线掉线检测耗时 {lostAt} ms 偏长：单次读超时（3000ms）决定了下限，可考虑按轮询周期自适应收紧读超时");
        if (backAt < 0) Find("S8[B] 继电器恢复后 30s 内未重连成功，重连链路有问题");
        else if (backAt > 3000) Find($"S8[B] 重连耗时 {backAt} ms（退避基准 500ms）：断线恢复偏慢");
        if (silentAt < 0) Find("S8[C] 服务器静默 20s 内未判掉线：ReadTimeoutMs=3000 未兜住半开连接，属于严重漏检");
        else if (silentAt > 6000) Find($"S8[C] 静默掉线检测 {silentAt} ms（ReadTimeoutMs=3000）：连续多次超时才判掉线，恢复及时性受影响");
        if (silentBackAt < 0) Find("S8[D] 静默解除后 30s 内未恢复");
        if (warnDelta > 50)
            Find($"S8 一次断线刷了 {warnDelta} 行 WARNING 日志：按「每次失败都打」产生日志风暴，建议按连接+地址限流");
    }

    /// <summary>
    /// 等到连接进入/离开 Connected。
    /// <para><b>为什么不用 config.State</b>：Manager 的 OnWorkerStateChanged 把 `config.State = newState`
    /// 包在 SafeDispatch.BeginInvoke 里，而 SafeDispatch 在 `Application.Current == null`（控制台/服务等
    /// 无 WPF 宿主）时**直接 return、动作永不执行**——config.State 会永久停在 Disconnected。
    /// 故这里改读连接实现自身的 IsConnected（Connect/Disconnect 直接置位，不经 Dispatcher）。</para>
    /// </summary>
    private static long WaitState(
        AdvancedCommunicationManager mgr, string conn, bool wantConnected, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if ((mgr.GetConnection(conn)?.IsConnected ?? false) == wantConnected)
                return sw.ElapsedMilliseconds;
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

    #region TCP 继电器（S8 掐线用）

    /// <summary>
    /// 可随时掐断 / 恢复的 TCP 继电器：监听 <see cref="_listenPort"/>，把字节原样转发到 127.0.0.1:<see cref="_upstreamPort"/>。
    ///
    /// <para><b>为什么要它（踩过的坑）</b>：HSL 的 <c>CommunicationTcpServer.ServerClose()</c> 在"客户端正连着"时
    /// 会直接关掉监听 socket，而它的接受循环（<c>AsyncAcceptCallback</c>）在连续 3 次 <c>BeginAccept</c> 失败后
    /// 会 <c>throw new Exception</c>（见 CommunicationTcpServer.cs:243-247）。这个异常在线程池线程上抛出，
    /// 调用方的 try/catch 拦不到，**整个进程被带走**——本轮首次完整压测就是这么崩的（崩在 S8）。
    /// 那是 HSL **服务端**代码的缺陷；本产品的通信库是纯客户端，不受影响，但压测台必须绕开它。</para>
    ///
    /// <para><b>做法</b>：靶子服务器全程不动，改在中间加一跳继电器。掐断继电器 = 客户端侧看到连接被关闭（等价于断网 / 交换机拔线），
    /// 恢复继电器 = 客户端重连成功。既真实，又不碰 HSL 那个服务端地雷。</para>
    ///
    /// <para>本类自身也刻意做到"任何异常都不外抛"：接受循环、转发线程全部兜底 catch，
    /// 绝不让自己变成第二个把进程干掉的元凶。</para>
    /// </summary>
    private sealed class TcpRelay : IDisposable
    {
        private readonly int _listenPort;
        private readonly int _upstreamPort;
        private readonly List<Socket> _sockets = new();
        private Socket? _listener;
        private volatile bool _running;

        /// <summary>静默模式：TCP 连接照旧保持着，但不再搬运任何字节（模拟"服务器进程还在、就是不回包"的半开场景）</summary>
        private volatile bool _paused;

        public TcpRelay(int listenPort, int upstreamPort)
        {
            _listenPort = listenPort;
            _upstreamPort = upstreamPort;
        }

        /// <summary>静默：连接不断，但客户端发的请求被吞掉、服务端的应答也回不去 → 只能靠读超时兜底</summary>
        public void Pause() => _paused = true;

        /// <summary>恢复搬运</summary>
        public void Resume() => _paused = false;

        public void Start()
        {
            if (_running) return;
            _running = true;
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // 允许"掐断后立刻重开"复用同一个监听端口（否则 TIME_WAIT 会让第二次 Bind 失败）
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, _listenPort));
            listener.Listen(64);
            _listener = listener;
            new Thread(AcceptLoop) { IsBackground = true, Name = "relay-accept" }.Start();
        }

        /// <summary>掐线：关监听 + 关掉所有已建立的转发连接（客户端会立刻收到连接关闭）</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _listener?.Close(); } catch { /* 已关或未开，忽略 */ }
            _listener = null;
            lock (_sockets)
            {
                foreach (var s in _sockets) TryClose(s);
                _sockets.Clear();
            }
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                Socket client;
                try
                {
                    client = _listener!.Accept();
                }
                catch (ObjectDisposedException) { break; }            // 正常掐线
                catch (SocketException) { if (!_running) break; continue; }
                catch { break; }                                     // 兜底：继电器绝不外抛

                var upstream = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    upstream.Connect(IPAddress.Loopback, _upstreamPort);
                }
                catch
                {
                    // 靶子服务器没在听：关掉这条，继续接下一根
                    TryClose(client);
                    TryClose(upstream);
                    continue;
                }

                lock (_sockets)
                {
                    _sockets.Add(client);
                    _sockets.Add(upstream);
                }
                Pump(client, upstream);
                Pump(upstream, client);
            }
        }

        /// <summary>单向搬运字节；任一方向断开就把这对 socket 都关掉（Modbus 是请求/应答式，不需要半关闭语义）</summary>
        private void Pump(Socket from, Socket to)
        {
            new Thread(() =>
            {
                var buffer = new byte[16 * 1024];
                try
                {
                    while (true)
                    {
                        if (_paused) { Thread.Sleep(20); continue; }   // 静默期：把字节留在缓冲里，谁也不发
                        int n = from.Receive(buffer);
                        if (n <= 0) break;
                        if (_paused) continue;                          // 收下但不转发
                        to.Send(buffer, 0, n, SocketFlags.None);
                    }
                }
                catch { /* 掐线时必然抛，属预期 */ }
                finally
                {
                    TryClose(from);
                    TryClose(to);
                }
            })
            { IsBackground = true, Name = "relay-pump" }.Start();
        }

        private static void TryClose(Socket? s)
        {
            if (s == null) return;
            try { s.Shutdown(SocketShutdown.Both); } catch { /* 未连接 / 已关闭 */ }
            try { s.Close(); } catch { /* 已关闭 */ }
        }
    }

    #endregion
}
