using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using VisionMaster;
using VisionMaster.Communications;

namespace CommPerf;

/// <summary>
/// 临时压测台：走 VM 自己的通信代码（ModbusTcpConnection / SiemensS7Connection / AdvancedCommunicationManager）
/// 直连用户正在运行的 DeviceSimulator（127.0.0.1:10000-10019）。
/// 阶段：correct / lat / write / poll / reconn / json，可单独跑或 all。
/// </summary>
internal static class Program
{
    private const string Host = "127.0.0.1";
    private const int GhostPort = 19999;      // 没有监听的端口
    private const int SilentPort = 19998;     // 只 accept 不回包
    private const int RelayPort = 17010;      // relay -> 10010

    private static readonly int[] MbPorts = { 10010, 10011, 10012, 10013, 10014, 10015, 10016, 10017, 10018, 10019 };
    private static readonly ByteOrderFormat[] MbOrders =
    {
        ByteOrderFormat.ABCD, ByteOrderFormat.CDAB, ByteOrderFormat.BADC, ByteOrderFormat.DCBA,
        ByteOrderFormat.ABCD, ByteOrderFormat.CDAB, ByteOrderFormat.BADC, ByteOrderFormat.DCBA,
        ByteOrderFormat.ABCD, ByteOrderFormat.CDAB,
    };
    private static readonly int[] S7Ports = { 10000, 10001, 10002, 10003, 10004, 10005, 10006, 10007, 10008, 10009 };

    private static readonly Process Proc = Process.GetCurrentProcess();
    private static TextWriter _real = Console.Out;
    private static readonly ConsoleSink Sink = new();

    // 轮询事件计数（VariableChanged）
    private static readonly ConcurrentDictionary<string, long> Evt = new();
    private static long TotalEvt;

    private static readonly List<string> Notes = new();

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        _real = Console.Out;
        Console.SetOut(Sink); // 通信库日志吞进 Sink，报告走 _real

        string phase = args.Length > 0 ? args[0] : "all";

        Header();
        try
        {
            if (phase == "all" || phase == "correct") PhaseCorrect();
            if (phase == "all" || phase == "wire") PhaseWire();
            if (phase == "all" || phase == "lat") PhaseLatency();
            if (phase == "all" || phase == "write") PhaseWrite();
            if (phase == "all" || phase == "poll") PhasePoll();
            if (phase == "all" || phase == "reconn") PhaseReconnect();
            if (phase == "all" || phase == "fix") PhaseFixes();
            if (phase == "all" || phase == "json") PhaseJson();
        }
        catch (Exception ex)
        {
            Say($"!! 阶段异常终止: {ex.GetType().Name}: {ex.Message}");
            Say(ex.StackTrace ?? "");
        }

        Say("== NOTES ==");
        foreach (var n in Notes) Say(" * " + n);
        Say($"== LIB LOG == lines={Sink.TotalLines} error={Sink.ErrorLines} warn={Sink.WarnLines}");
        return 0;
    }

    private static void Say(string s = "") => _real.WriteLine(s);
    private static void Note(string s) { Notes.Add(s); Say("  [NOTE] " + s); }

    private static void Header()
    {
        Say("================================================================");
        Say(" CommPerf — VisionMaster 通信层 × DeviceSimulator 压测台");
        Say($" 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}   PID={Proc.Id}   核数={Environment.ProcessorCount}");
        Say($" 目标: Modbus {Host}:{MbPorts[0]}-{MbPorts[^1]}   S7 {Host}:{S7Ports[0]}-{S7Ports[^1]}");
        Say("================================================================");
    }

    #region 配置/连接构造

    private static ModbusTcpConfig NewMbLink(int port, ByteOrderFormat order) => new()
    {
        Type = CommunicationType.ModbusTcp,
        IpAddress = Host,
        Port = port,
        TimeoutMs = 2000,
        ReadTimeoutMs = 3000,
        RetryCount = 1,
        RetryIntervalMs = 500,
        ByteOrder = order,
    };

    private static SiemensS7Config NewS7Link(int port) => new()
    {
        Type = CommunicationType.SiemensS7,
        IpAddress = Host,
        Port = port,
        TimeoutMs = 2000,
        ReadTimeoutMs = 3000,
        RetryCount = 1,
        RetryIntervalMs = 500,
        S7CpuType = S7CpuType.S1200,
        Rack = 0,
        Slot = 0,
    };

    /// <summary>经 ConnectionFactoryManager 造连接（VM 的等价工厂路径）</summary>
    private static ICommunicationConnection NewConn(CommunicationConfig cfg)
        => ConnectionFactoryManager.Instance.CreateConnection(cfg);

    private static CommunicationConfig NewMbCfg(string name, int port, ByteOrderFormat order, int cycle = 1000)
        => new(name, NewMbLink(port, order)) { ReadCycleMs = cycle, AutoReconnect = true };

    private static CommunicationConfig NewS7Cfg(string name, int port, int cycle = 1000)
        => new(name, NewS7Link(port)) { ReadCycleMs = cycle, AutoReconnect = true };

    #endregion

    #region 度量工具

    private static List<double> TimeLoop(int n, Action act)
    {
        var lat = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            act();
            lat.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        }
        return lat;
    }

    private static string StatLine(string label, List<double> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        double avg = a.Average();
        double p50 = a[(int)(a.Length * 0.50)];
        double p95 = a[Math.Min(a.Length - 1, (int)(a.Length * 0.95))];
        return $"{label,-34} n={a.Length,5}  avg={avg,8:F3}  P50={p50,8:F3}  P95={p95,8:F3}  max={a[^1],9:F3}  min={a[0],7:F3} (ms)";
    }

    private sealed record Metrics(long Alloc, TimeSpan Cpu, int Threads, long Ws);
    private static Metrics Capture() => new(
        GC.GetTotalAllocatedBytes(false), Proc.TotalProcessorTime, Proc.Threads.Count, Proc.WorkingSet64);

    private sealed class ConsoleSink : TextWriter
    {
        private readonly object _lock = new();
        private readonly Queue<string> _tail = new();
        private readonly StringBuilder _line = new();
        public long TotalLines, ErrorLines, WarnLines;
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_lock)
            {
                if (value == '\n') { Flush(); return; }
                if (value == '\r') return;
                _line.Append(value);
                if (_line.Length > 800) Flush();
            }
        }
        public override void Write(string? v) { if (v != null) foreach (var c in v) Write(c); }
        public override void WriteLine(string? v) { Write(v); Write('\n'); }

        private void Flush()
        {
            var s = _line.ToString(); _line.Clear();
            TotalLines++;
            if (s.Contains("[ERROR]")) ErrorLines++;
            else if (s.Contains("[WARNING]")) WarnLines++;
            _tail.Enqueue(s);
            while (_tail.Count > 60) _tail.Dequeue();
        }

        public string[] Tail() { lock (_lock) return _tail.ToArray(); }
        public (long lines, long err, long warn) Snapshot() { lock (_lock) return (TotalLines, ErrorLines, WarnLines); }
    }

    /// <summary>PhaseJson 用的日志收集器（挂到 <see cref="AdvancedCommunicationManager.LogSink"/>，留逐条隔离的 ERROR 原文）</summary>
    private sealed class ListSink : Core.Interfaces.ILogService
    {
        private readonly List<string> _lines;
        public ListSink(List<string> lines) => _lines = lines;

        public void Success(params string[] messages) => _lines.Add("[SUCCESS] " + string.Join(" ", messages));
        public void Error(params string[] messages) => _lines.Add("[ERROR] " + string.Join(" ", messages));
        public void Error(params Exception[] messages) => _lines.Add("[ERROR] " + string.Join(" ", messages.Select(e => e.Message)));
        public void Info(params string[] messages) => _lines.Add("[INFO] " + string.Join(" ", messages));
        public void Warn(params string[] messages) => _lines.Add("[WARNING] " + string.Join(" ", messages));
    }

    #endregion

    #region Phase 0 线路字节转储

    private static string Hex(byte[] b) => BitConverter.ToString(b);

    private static void PhaseWire()
    {
        Say("== C0 线路字节转储（ReadBytes 原始字节，不重排）：HR40 float 特征值 / HR30 int32 / HR10 ushort ==");
        foreach (var port in MbPorts)
        {
            try
            {
                using var conn = NewConn(NewMbCfg($"WIRE_{port}", port, ByteOrderFormat.ABCD));
                bool c = conn.Connect();
                var b40 = conn.ReadBytes("x=3;40", 4);
                var b30 = conn.ReadBytes("x=3;30", 4);
                var b10 = conn.ReadBytes("x=3;10", 2);
                Say($"  MB {port}: conn={c}  HR40(4B)={Hex(b40)}  HR30(4B)={Hex(b30)}  HR10(2B)={Hex(b10)}");
            }
            catch (Exception ex) { Say($"  MB {port}: EXCEPTION {ex.Message}"); }
        }
        Say();
        Say("  交叉验证：全部 10 端口统一按 CDAB 配置 → HR40 / HR0 温度 ==");
        int okCdab = 0;
        foreach (var port in MbPorts)
        {
            try
            {
                using var conn = NewConn(NewMbCfg($"WIRE2_{port}", port, ByteOrderFormat.CDAB));
                bool c = conn.Connect();
                float f40 = conn.Read<float>("x=3;40");
                float t0 = conn.Read<float>("x=3;0");
                bool ok = Math.Abs(f40 - 123.5f) < 0.05f;
                if (ok) okCdab++;
                Say($"  MB {port} [CDAB]: HR40={f40,8:F3} {(ok ? "OK" : "**MISMATCH**")}  HR0温度={t0,8:F3}");
            }
            catch (Exception ex) { Say($"  MB {port}: EXCEPTION {ex.Message}"); }
        }
        Say($"  >>> 统一 CDAB 口径：{okCdab}/10 端口 HR40=123.5");
        Say();
    }

    #endregion

    #region Phase 1 正确性

    /// <summary>等一个位翻转（消除"1.1s 采样间隔 vs 1s 翻转周期"的混叠误判）</summary>
    private static (bool flipped, double ms) WaitForFlip(Func<bool> read, int timeoutMs = 1700)
    {
        bool v0 = read();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(50);
            if (read() != v0) return (true, sw.ElapsedMilliseconds);
        }
        return (false, sw.ElapsedMilliseconds);
    }

    private static void PhaseCorrect()
    {
        Say("== C1 Modbus 正确性：HR40 特征值 123.5（按各端口字节序解码） ==");
        int pass = 0, fail = 0;
        for (int i = 0; i < MbPorts.Length; i++)
        {
            int port = MbPorts[i]; var order = MbOrders[i];
            try
            {
                using var conn = NewConn(NewMbCfg($"C_MB{port}", port, order));
                bool connected = conn.Connect();
                float feat = conn.Read<float>("x=3;40");
                ushort cnt = conn.Read<ushort>("x=3;10");
                int cnt32 = conn.Read<int>("x=3;30");
                bool okFeat = Math.Abs(feat - 123.5f) < 0.05f;
                if (connected && okFeat) pass++; else fail++;

                // 心跳：线圈0 与 离散输入0，轮询等待翻转（1s 周期内应出现）
                var (coilFlip, coilMs) = WaitForFlip(() => conn.ReadBits("0", 1)[0]);
                var (discFlip, discMs) = WaitForFlip(() => conn.ReadBits("x=2;0", 1)[0]);

                // 写回环（float @100 / int16 @300）
                conn.Write("x=3;100", 123.5f);
                float wback = conn.Read<float>("x=3;100");
                conn.Write("x=3;300", (short)-123);
                short sback = conn.Read<short>("x=3;300");

                Say($"  MB {port} [{order,-4}] conn={connected} HR40={feat,8:F3} {(okFeat ? "OK" : "**MISMATCH**")} " +
                    $"HR10={cnt} HR30={cnt32} | 心跳线圈 flip={coilFlip}({coilMs:F0}ms) {(coilFlip ? "OK" : "**NO-FLIP**")} " +
                    $"离散 flip={discFlip}({discMs:F0}ms) {(discFlip ? "OK" : "**NO-FLIP**")} | " +
                    $"写回环 float@100={wback:F3} {(Math.Abs(wback - 123.5f) < 0.05f ? "OK" : "**BAD**")} " +
                    $"int16@300={sback} {(sback == -123 ? "OK" : "**BAD**")}");
            }
            catch (Exception ex)
            {
                fail++;
                Say($"  MB {port} [{order}] EXCEPTION: {ex.Message}");
            }
        }
        Say($"  >>> Modbus 正确性：{pass}/{MbPorts.Length} 通过（特征值口径），失败 {fail}");
        Say();

        Say("== C2 S7 正确性：DB1.DBW30 特征字 0x4242 ==");
        int pass7 = 0, fail7 = 0;
        for (int i = 0; i < S7Ports.Length; i++)
        {
            int port = S7Ports[i];
            try
            {
                using var conn = NewConn(NewS7Cfg($"C_S7{port}", port));
                bool connected = conn.Connect();
                ushort feat = conn.Read<ushort>("DB1.DBW30");
                short w0 = conn.Read<short>("DB1.DBW0");
                float temp = conn.Read<float>("DB1.DBD10");
                int d20 = conn.Read<int>("DB1.DBD20");
                byte m100 = conn.Read<byte>("MB100");
                bool okFeat = feat == 0x4242;
                if (connected && okFeat) pass7++; else fail7++;

                byte h1 = conn.Read<byte>("MB101");
                var (hbFlip, hbMs) = WaitForFlip(() => conn.Read<byte>("MB101") != h1);

                conn.Write("DB1.DBD100", 123.5f);
                float wback = conn.Read<float>("DB1.DBD100");
                conn.Write("MB20", (byte)0x5A);
                byte bback = conn.Read<byte>("MB20");
                conn.Write("M10.3", true);
                bool bitset = (conn.Read<byte>("MB10") & 0x08) != 0;

                Say($"  S7 {port} conn={connected} DBW30=0x{feat:X4} {(okFeat ? "OK" : "**MISMATCH**")} " +
                    $"DBW0={w0} DBD10={temp,7:F3} DBD20={d20} MB100={m100} | 心跳M101 flip={hbFlip}({hbMs:F0}ms) {(hbFlip ? "OK" : "**NO-FLIP**")} | " +
                    $"写回环 float@DBD100={wback:F3} {(Math.Abs(wback - 123.5f) < 0.05f ? "OK" : "**BAD**")} " +
                    $"byte@M20=0x{bback:X2} {(bback == 0x5A ? "OK" : "**BAD**")} bit@M10.3={bitset} {(bitset ? "OK" : "**BAD**")}");
            }
            catch (Exception ex)
            {
                fail7++;
                Say($"  S7 {port} EXCEPTION: {ex.Message}");
            }
        }
        Say($"  >>> S7 正确性：{pass7}/{S7Ports.Length} 通过（特征字口径），失败 {fail7}");
        Say();
    }

    #endregion

    #region Phase 2 延迟

    private static void PhaseLatency()
    {
        Say("== L1 单连接顺序读延迟（VM 连接类 vs 裸 HSL 基线） ==");

        // VM ModbusTcpConnection
        using (var conn = NewConn(NewMbCfg("L_MB10010", 10010, ByteOrderFormat.ABCD)))
        {
            bool c = conn.Connect();
            for (int i = 0; i < 50; i++) conn.Read<float>("x=3;40");            // 预热
            var vm = TimeLoop(600, () => conn.Read<float>("x=3;40"));
            var bulk = TimeLoop(600, () => conn.ReadBytes("x=3;10", 32));        // 批量段读形态
            Say("  [Modbus VM] Connect=" + c);
            Say("  " + StatLine("Read<float> x=3;40 (2 reg)", vm));
            Say("  " + StatLine("ReadBytes x=3;10 len32 (段读形态)", bulk));
        }

        // 裸 HSL 基线
        using (var raw = new ModbusTcpNet(Host, 10010) { ConnectTimeOut = 2000, ReceiveTimeOut = 3000 })
        {
            var rc = raw.ConnectServer();
            for (int i = 0; i < 50; i++) raw.Read("x=3;40", 2);
            var b = TimeLoop(600, () => raw.Read("x=3;40", 2));
            Say("  [Modbus 裸HSL] ConnectServer=" + rc.IsSuccess);
            Say("  " + StatLine("raw.Read x=3;40 (2 reg)", b));
            raw.ConnectClose();
        }

        // VM S7
        using (var conn7 = NewConn(NewS7Cfg("L_S710000", 10000)))
        {
            bool c = conn7.Connect();
            for (int i = 0; i < 50; i++) conn7.Read<ushort>("DB1.DBW30");
            var vm7 = TimeLoop(600, () => conn7.Read<ushort>("DB1.DBW30"));
            Say("  [S7 VM] Connect=" + c);
            Say("  " + StatLine("Read<ushort> DB1.DBW30 (2 byte)", vm7));
        }

        // 裸 HSL S7
        using (var raw7 = new SiemensS7Net(SiemensPLCS.S1200, Host)
        {
            Port = 10000,
            ConnectTimeOut = 2000,
            ReceiveTimeOut = 3000,
        })
        {
            var rc7 = raw7.ConnectServer();
            for (int i = 0; i < 50; i++) raw7.Read("DB1.DBW30", 2);
            var b7 = TimeLoop(600, () => raw7.Read("DB1.DBW30", 2));
            Say("  [S7 裸HSL] ConnectServer=" + rc7.IsSuccess);
            Say("  " + StatLine("raw.Read DB1.DBW30 (2 byte)", b7));
            raw7.ConnectClose();
        }
        Say();
    }

    #endregion

    #region Phase 3 写延迟

    private static void PhaseWrite()
    {
        Say("== W1 写操作延迟（VM 连接类） ==");

        using (var conn = NewConn(NewMbCfg("W_MB10010", 10010, ByteOrderFormat.ABCD)))
        {
            bool c = conn.Connect();
            for (int i = 0; i < 10; i++) conn.Write("x=3;100", 123.5f);
            var w = TimeLoop(250, () => conn.Write("x=3;100", 123.5f));
            float back = conn.Read<float>("x=3;100");
            Say($"  [Modbus VM] Connect={c}  写 float HR100 ×250");
            Say("  " + StatLine("Write(\"x=3;100\", float)", w));
            Say($"  末次回读 HR100 = {back:F3}（≈123.5 即 {(Math.Abs(back - 123.5f) < 0.05f ? "OK" : "**BAD**")}）");
        }

        using (var conn7 = NewConn(NewS7Cfg("W_S710000", 10000)))
        {
            bool c = conn7.Connect();
            for (int i = 0; i < 10; i++) conn7.Write("DB1.DBD100", 123.5f);
            var w7 = TimeLoop(250, () => conn7.Write("DB1.DBD100", 123.5f));
            float back7 = conn7.Read<float>("DB1.DBD100");
            Say($"  [S7 VM] Connect={c}  写 float DB1.DBD100 ×250");
            Say("  " + StatLine("Write(\"DB1.DBD100\", float)", w7));
            Say($"  末次回读 DBD100 = {back7:F3}（≈123.5 即 {(Math.Abs(back7 - 123.5f) < 0.05f ? "OK" : "**BAD**")}）");
        }
        Say();
    }

    #endregion

    #region Phase 4 并发轮询 60s

    private static void OnVarChanged(object? sender, object? e)
    {
        if (sender is CommunicationVariable v) Evt.AddOrUpdate(v.VariableName, 1, (_, c) => c + 1);
        Interlocked.Increment(ref TotalEvt);
    }

    private static void RegVar(AdvancedCommunicationManager mgr, string conn, string name, string addr, string type)
    {
        var v = new CommunicationVariable
        {
            ConnectionName = conn,
            VariableName = name,
            Address = addr,
            ValueType = type,
            AccessMode = VariableAccessMode.ReadOnly,
        };
        v.ValueChanged += OnVarChanged;
        mgr.RegisterVariable(v);
    }

    private static void PhasePoll()
    {
        Say("== P1 并发轮询：Modbus×10 + S7×10 @200ms 默认组，持续 60s ==");
        Evt.Clear();
        Interlocked.Exchange(ref TotalEvt, 0);
        var (l0, e0, w0) = Sink.Snapshot();

        using var mgr = new AdvancedCommunicationManager
        {
            AutoReconnectEnabled = true,
            GlobalReconnectIntervalMs = 1000,
        };

        var names = new List<string>();
        for (int i = 0; i < MbPorts.Length; i++)
        {
            string n = $"P_MB{i:D2}_{MbPorts[i]}";
            var cfg = NewMbCfg(n, MbPorts[i], MbOrders[i], cycle: 200);
            mgr.AddConnection(cfg);
            string t16 = typeof(ushort).AssemblyQualifiedName!;
            string tf4 = typeof(float).AssemblyQualifiedName!;
            RegVar(mgr, n, n + ".cnt", "x=3;10", t16);
            RegVar(mgr, n, n + ".temp", "x=3;40", tf4);
            RegVar(mgr, n, n + ".status", "x=3;20", t16);
            names.Add(n);
        }
        for (int i = 0; i < S7Ports.Length; i++)
        {
            string n = $"P_S7{i:D2}_{S7Ports[i]}";
            var cfg = NewS7Cfg(n, S7Ports[i], cycle: 200);
            mgr.AddConnection(cfg);
            string ts2 = typeof(short).AssemblyQualifiedName!;
            string tu2 = typeof(ushort).AssemblyQualifiedName!;
            string tf4 = typeof(float).AssemblyQualifiedName!;
            string ti4 = typeof(int).AssemblyQualifiedName!;
            RegVar(mgr, n, n + ".cnt", "DB1.DBW0", ts2);
            RegVar(mgr, n, n + ".temp", "DB1.DBD10", tf4);
            RegVar(mgr, n, n + ".dint", "DB1.DBD20", ti4);
            RegVar(mgr, n, n + ".feat", "DB1.DBW30", tu2);
            names.Add(n);
        }

        var swAll = Stopwatch.StartNew();
        foreach (var n in names) mgr.RequestConnect(n);
        var swConn = Stopwatch.StartNew();
        while (swConn.ElapsedMilliseconds < 15000 && mgr.ConnectedCount < names.Count) Thread.Sleep(50);
        Say($"  20 连接并发建连（RequestConnect 非阻塞）：{mgr.ConnectedCount}/{names.Count} 就绪，用时 {swConn.ElapsedMilliseconds} ms");
        Thread.Sleep(2000); // 首轮轮询稳定

        var before = Capture();
        long evtBefore = Interlocked.Read(ref TotalEvt);
        var sw = Stopwatch.StartNew();
        const int windowMs = 60_000;
        long lastReport = 0;
        while (sw.ElapsedMilliseconds < windowMs)
        {
            Thread.Sleep(1000);
            if (sw.ElapsedMilliseconds - lastReport >= 15000)
            {
                lastReport = sw.ElapsedMilliseconds;
                Say($"  … {sw.ElapsedMilliseconds / 1000}s: Connected={mgr.ConnectedCount}/20  事件累计={Interlocked.Read(ref TotalEvt)}  " +
                    $"WS={Proc.WorkingSet64 / 1048576}MB");
            }
        }
        sw.Stop();

        var after = Capture();
        long evtAfter = Interlocked.Read(ref TotalEvt);
        Say();
        Say("  每连接扫描组实测（轮询吞吐）:");
        double totalQps = 0;
        foreach (var n in names)
        {
            var stats = mgr.GetScanGroupStats(n);
            var s = stats.FirstOrDefault();
            int cycle = s.AvgActualMs > 0 ? s.AvgActualMs : s.TargetIntervalMs;
            double qps = s.SegmentCount * 1000.0 / Math.Max(1, cycle);
            totalQps += qps;
            Say($"    {n,-16} 变量{s.VariableCount,3} 段{s.SegmentCount,2} 目标{s.TargetIntervalMs,5}ms 实测{s.AvgActualMs,5}ms " +
                $"达成{s.AchieveRate * 100,3:F0}% 坏段{s.FaultedSegmentCount} 请求/s={qps,6:F1}");
        }
        double sec = sw.Elapsed.TotalSeconds;
        Say($"  合计: QPS={totalQps:F1}  实测窗口 {sec:F1}s  值变化事件 {evtAfter - evtBefore}（{(evtAfter - evtBefore) / sec:F0}/s）  " +
            $"CPU={(after.Cpu - before.Cpu).TotalMilliseconds / (sec * 1000) / Environment.ProcessorCount * 100:F1}%  " +
            $"分配={(after.Alloc - before.Alloc) / 1048576.0 / sec:F2} MB/s  WS {before.Ws / 1048576}→{after.Ws / 1048576}MB（Δ{(after.Ws - before.Ws) / 1048576:+0;-0}MB）  " +
            $"线程 {before.Threads}→{after.Threads}");
        var (l1, e1, w1) = Sink.Snapshot();
        Say($"  期间通信库日志: {l1 - l0} 行 / ERROR {e1 - e0} / WARNING {w1 - w0}");
        if (e1 - e0 > 0 || w1 - w0 > 0)
        {
            Say("  日志尾部:");
            foreach (var line in Sink.Tail().TakeLast(8)) Say("    " + line);
        }
        if ((after.Ws - before.Ws) > 64 * 1048576)
            Note($"P1 工作集增长 {(after.Ws - before.Ws) / 1048576}MB/60s，需二次确认是否持续增长（泄漏嫌疑）");

        foreach (var n in names) mgr.Disconnect(n);
        Say();
    }

    #endregion

    #region Phase 5 断线重连

    private static void PhaseReconnect()
    {
        Say("== R1 幽灵端口（127.0.0.1:19999 无监听）：失败耗时与退避重连节奏 ==");
        var transitions = new List<(long t, string from, string to)>();
        var t0 = Stopwatch.StartNew();
        {
            using var mgr = new AdvancedCommunicationManager { AutoReconnectEnabled = true, GlobalReconnectIntervalMs = 1000 };
            var cfg = new CommunicationConfig("R_Ghost", NewMbLink(GhostPort, ByteOrderFormat.ABCD))
            {
                ReadCycleMs = 200,
                AutoReconnect = true,
            };
            cfg.Config!.RetryIntervalMs = 1000; // 退避基准 1s
            mgr.ConnectionStateChanged += (_, e) => transitions.Add((t0.ElapsedMilliseconds, e.OldState.ToString(), e.NewState.ToString()));
            mgr.AddConnection(cfg);

            var swC = Stopwatch.StartNew();
            bool ok = mgr.Connect("R_Ghost");
            swC.Stop();
            Say($"  Manager.Connect（同步等待首建，TimeoutMs=2000）→ {ok}，耗时 {swC.ElapsedMilliseconds} ms");
            Say($"  LastError(Worker) = {mgr.GetConnectionError("R_Ghost")}");
            Thread.Sleep(8000); // 观察退避节奏
            Say($"  8s 观察期状态迁移: {string.Join(" → ", transitions.Select(x => $"{x.from}→{x.to}@{x.t}ms"))}");
            mgr.Disconnect("R_Ghost");
        }
        Say();

        Say("== R2 静默对端（accept 后不回包 19998）：半开场景的读/建连超时行为 ==");
        using (var silent = new SilentServer(SilentPort)) { silent.Start(); Thread.Sleep(200);
            // Modbus：TCP 建连成功（无握手），读应烧满 ReceiveTimeOut
            using (var mb = new ModbusTcpConnection(NewMbLink(SilentPort, ByteOrderFormat.ABCD)))
            {
                var swB = Stopwatch.StartNew();
                bool c = mb.Connect();
                swB.Stop();
                Say($"  Modbus Connect → {c}，耗时 {swB.ElapsedMilliseconds} ms（无协议握手，TCP 通即成）");
                var swR = Stopwatch.StartNew();
                bool readFailed = false; string msg = "";
                try { mb.Read<float>("x=3;40"); } catch (Exception ex) { readFailed = true; msg = ex.Message; }
                swR.Stop();
                Say($"  Read<float> → 失败={readFailed}，耗时 {swR.ElapsedMilliseconds} ms（ReadTimeoutMs=3000 应兜底）  msg={Trunc(msg)}");
            }
            // S7：COTP 握手要读回包 → Connect 应在 ReceiveTimeOut 失败
            using (var s7 = new SiemensS7Connection(NewS7Link(SilentPort)))
            {
                var swB = Stopwatch.StartNew();
                bool c = s7.Connect();
                swB.Stop();
                Say($"  S7 Connect → {c}，耗时 {swB.ElapsedMilliseconds} ms（COTP 握手等回包，≈ReadTimeoutMs=3000）  LastError≈{Trunc(s7.ToString())}");
            }
            // 幽灵端口直连（VM 连接类裸用）：TCP 拒绝应快速失败
            using (var gh = new ModbusTcpConnection(NewMbLink(GhostPort, ByteOrderFormat.ABCD)))
            {
                var swB = Stopwatch.StartNew();
                bool c = gh.Connect();
                swB.Stop();
                Say($"  Modbus Connect(幽灵端口) → {c}，耗时 {swB.ElapsedMilliseconds} ms（TCP 拒绝，应秒败）");
            }
        }
        Say();

        Say("== R3 掐线/恢复（relay 17010 → 10010，轮询中连接） ==");
        {
            using var relay = new TcpRelay(RelayPort, 10010);
            relay.Start();
            using var mgr = new AdvancedCommunicationManager { AutoReconnectEnabled = true, GlobalReconnectIntervalMs = 1000 };
            var transitions3 = new List<(long t, string from, string to)>();
            var t3 = Stopwatch.StartNew();
            mgr.ConnectionStateChanged += (_, e) => transitions3.Add((t3.ElapsedMilliseconds, e.OldState.ToString(), e.NewState.ToString()));

            var cfg = NewMbCfg("R_Relay", RelayPort, ByteOrderFormat.ABCD, cycle: 200);
            mgr.AddConnection(cfg);
            RegVar(mgr, "R_Relay", "R_Relay.cnt", "x=3;10", typeof(ushort).AssemblyQualifiedName!);
            bool ok = mgr.Connect("R_Relay");
            Say($"  经 relay 建连 → {ok}");
            Thread.Sleep(2000);

            var swL = Stopwatch.StartNew();
            relay.Stop(); // 掐线：客户端立刻收到 RST/FIN
            long lost = WaitConn(mgr, "R_Relay", false, 15000);
            Say($"  [A] 掐线 → 掉线检测 {lost} ms（轮询 200ms + 读超时 3000ms 量级）");
            Thread.Sleep(3000);

            relay.Start();
            long back = WaitConn(mgr, "R_Relay", true, 30000);
            Say($"  [B] 恢复 relay → 重连成功 {back} ms（退避基准 1000ms）");

            relay.Pause();
            long silentLost = WaitConn(mgr, "R_Relay", false, 20000);
            Say($"  [C] relay 静默（半开） → 掉线检测 {silentLost} ms（下限=ReadTimeoutMs 3000）");
            relay.Resume();
            long silentBack = WaitConn(mgr, "R_Relay", true, 30000);
            Say($"  [D] 解除静默 → 恢复 {silentBack} ms");
            Thread.Sleep(1000);
            Say($"  状态迁移轨迹: {string.Join(" → ", transitions3.Select(x => $"{x.from}→{x.to}@{x.t}ms"))}");
            var stats = mgr.GetScanGroupStats("R_Relay").FirstOrDefault();
            Say($"  恢复后坏段={stats.FaultedSegmentCount}  最后错误={mgr.GetConnectionError("R_Relay") ?? "无"}");
            mgr.Disconnect("R_Relay");
        }
        Say();
    }

    private static string Trunc(string s) => s.Length <= 80 ? s : s[..80] + "…";

    private static long WaitConn(AdvancedCommunicationManager mgr, string name, bool want, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool now = mgr.GetConnection(name)?.IsConnected ?? false;
            if (now == want) return sw.ElapsedMilliseconds;
            Thread.Sleep(20);
        }
        return -1;
    }

    /// <summary>只 accept 不回包的靶子（半开场景）</summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly int _port;
        private Socket? _listener;
        private Thread? _t;
        private volatile bool _run;
        private readonly List<Socket> _held = new();

        public SilentServer(int port) => _port = port;
        public void Start()
        {
            _run = true;
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, _port));
            _listener.Listen(8);
            _t = new Thread(() =>
            {
                while (_run)
                {
                    try
                    {
                        var s = _listener!.Accept();
                        lock (_held) _held.Add(s); // 收下，不读不写
                    }
                    catch { if (!_run) break; }
                }
            }) { IsBackground = true };
            _t.Start();
        }
        public void Dispose()
        {
            _run = false;
            try { _listener?.Close(); } catch { }
            lock (_held) { foreach (var s in _held) try { s.Close(); } catch { } _held.Clear(); }
        }
    }

    #endregion

    #region Phase 6 communications.json 容错

    private static void PhaseJson()
    {
        Say("== J1 communications.json 反序列化容错 ==");
        string dir = Path.Combine(Path.GetTempPath(), "CommPerfJson");
        Directory.CreateDirectory(dir);

        // 挂接通信库日志入口，把逐条隔离的 ERROR 原文留证（Manager 的 Console 输出已被 Sink 吞掉）
        var libLogs = new List<string>();
        AdvancedCommunicationManager.LogSink = new ListSink(libLogs);

        void Case(string title, string json)
        {
            libLogs.Clear();
            string file = Path.Combine(dir, "case.json");
            File.WriteAllText(file, json, new UTF8Encoding(false));
            using var mgr = new AdvancedCommunicationManager { ConfigFilePath = file };
            int loaded = -1;
            string err = "";
            try
            {
                mgr.LoadConfig();
                loaded = mgr.GetAllConnections().Count;
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name + ": " + Trunc(ex.Message.Replace("\r", " ").Replace("\n", " "));
            }
            var first = mgr.GetAllConnections().FirstOrDefault();
            string detail = first?.Config is ModbusTcpConfig m ? $"Port={m.Port} TO={m.TimeoutMs}" : "";
            Say($"  [{title}] 加载 {loaded} 条  {(err == "" ? detail : "抛出→ " + err)}");
            foreach (var line in libLogs.Where(l => l.Contains("ERROR")))
                Say($"        {Trunc(line)}");
        }

        Case("J-ok       ",
            "[{\"connectionName\":\"J_A\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0,\"timeoutMs\":2500},\"readCycleMs\":500,\"autoStart\":false}]");

        Case("J-未实现协议",
            "[{\"connectionName\":\"J_A\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0},\"autoStart\":false}," +
            "{\"connectionName\":\"J_B\",\"protocol\":3,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":9600,\"type\":0}}]");

        Case("J-截断坏JSON",
            "[{\"connectionName\":\"J_A\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0},\"autoStart\":false}," +
            "{\"connectionName\":\"J_B\",\"protocol\":0,\"config\":{\"ipAddr");

        Case("J-缺字段    ",
            "[{\"connectionName\":\"J_C\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\"}}]");

        Case("J-协议=7越界",
            "[{\"connectionName\":\"J_A\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0},\"autoStart\":false}," +
            "{\"connectionName\":\"J_D\",\"protocol\":7,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":1,\"type\":0}}]");

        Case("J-S7 $type  ",
            "[{\"connectionName\":\"J_S7\",\"protocol\":2,\"config\":{\"$type\":\"VisionMaster.Communications.SiemensS7Config, VM.Core\",\"ipAddress\":\"127.0.0.1\",\"port\":10005,\"type\":2,\"timeoutMs\":2200},\"autoStart\":false}]");

        Case("J-$type 越权",
            "[{\"connectionName\":\"J_E\",\"protocol\":0,\"config\":{\"$type\":\"System.DateTime, System.Private.CoreLib\"},\"autoStart\":false}]");

        // 逐条隔离的核心证据（本轮修复点）：坏条在前、好条在后 → 好条必须照常加载（旧实现整份连坐为 0 条）
        Case("J-$type越权+好条",
            "[{\"connectionName\":\"J_E\",\"protocol\":0,\"config\":{\"$type\":\"System.DateTime, System.Private.CoreLib\"},\"autoStart\":false}," +
            "{\"connectionName\":\"J_A\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0,\"timeoutMs\":2500},\"autoStart\":false}]");

        Say();
    }

    #endregion

    #region Phase 7 修复点验证（refused 分类 / KeepAlive 落地，零模拟器依赖）

    private static void PhaseFixes()
    {
        Say("== F1 KeepAlive 落地运行时验证（本机 TcpListener 靶子） ==");
        int kaPort = 17890;
        using (var listener = new TcpListener(IPAddress.Loopback, kaPort))
        {
            listener.Start();
            using (var on = new ModbusTcpConnection(new ModbusTcpConfig
            { IpAddress = Host, Port = kaPort, TimeoutMs = 2000, ReadTimeoutMs = 3000, EnableKeepAlive = true, KeepAliveIntervalMs = 5000 }))
            {
                bool c = on.Connect();
                var ka = ReadSocketKeepAlive(on);
                Say($"  [开启] Connect={c}  SO_KEEPALIVE={ka.Enabled}  {ka.Detail}（期望 True，间隔 5000ms 由 HSL IOControl 下发）");
            }
            using (var off = new ModbusTcpConnection(new ModbusTcpConfig
            { IpAddress = Host, Port = kaPort, TimeoutMs = 2000, ReadTimeoutMs = 3000, EnableKeepAlive = false }))
            {
                bool c = off.Connect();
                var ka = ReadSocketKeepAlive(off);
                Say($"  [关闭] Connect={c}  SO_KEEPALIVE={ka.Enabled}  {ka.Detail}（期望 False，未启用时与落地前零变化）");
            }
            listener.Stop();
        }
        Say();

        Say("== F2 ConnectionRefused 识别（127.0.0.1:1 无监听；本机 OS/代理层拒绝延迟 ~2s，裸 socket 实测 2030ms） ==");
        using (var mb = new ModbusTcpConnection(new ModbusTcpConfig { IpAddress = Host, Port = 1, TimeoutMs = 3000, ReadTimeoutMs = 3000 }))
        {
            var sw = Stopwatch.StartNew();
            bool c = mb.Connect();
            sw.Stop();
            var d = (IConnectionFailureDetail)mb;
            Say($"  Modbus Connect(幽灵端口) → {c}，耗时 {sw.ElapsedMilliseconds} ms  分类Refused={d.IsLastConnectFailureRefused}  LastError={Trunc(d.LastConnectError ?? "null")}");
        }
        using (var s7 = new SiemensS7Connection(new SiemensS7Config { IpAddress = Host, Port = 1, TimeoutMs = 3000, ReadTimeoutMs = 3000 }))
        {
            var sw = Stopwatch.StartNew();
            bool c = s7.Connect();
            sw.Stop();
            var d = (IConnectionFailureDetail)s7;
            Say($"  S7 Connect(幽灵端口) → {c}，耗时 {sw.ElapsedMilliseconds} ms  分类Refused={d.IsLastConnectFailureRefused}  LastError={Trunc(d.LastConnectError ?? "null")}");
        }
        using (var mgr = new AdvancedCommunicationManager { AutoReconnectEnabled = true })
        {
            var cfg = new CommunicationConfig("F2_Ghost", NewMbLink(GhostPort, ByteOrderFormat.ABCD)) { AutoReconnect = true };
            cfg.Config!.TimeoutMs = 3000; // 默认档：refused 失败即返回（旧实现陪等满 3000ms）
            mgr.AddConnection(cfg);
            var sw = Stopwatch.StartNew();
            bool ok = mgr.Connect("F2_Ghost");
            sw.Stop();
            Say($"  Manager.Connect(19999, TimeoutMs=3000) → {ok}，耗时 {sw.ElapsedMilliseconds} ms（旧实现陪等满 TimeoutMs）  LastError={Trunc(mgr.GetConnectionError("F2_Ghost") ?? "null")}");
            mgr.RemoveConnection("F2_Ghost");
        }
        Say();
    }

    /// <summary>
    /// 读连接底层 socket 的 KeepAlive 状态（反射取 HSL 设备 → CommunicationPipe → Socket，与 CommChecks C7/C8 同口径）。
    /// SO_KEEPALIVE 是硬证据；IOControl KeepAliveValues 的三参数（on/time/interval）为尽力读取（Windows 不保证可 GET）。
    /// </summary>
    private static (bool Enabled, string Detail) ReadSocketKeepAlive(ICommunicationConnection conn)
    {
        try
        {
            var device = conn.GetType().GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(conn)!;
            var pipe = device.GetType().GetProperty("CommunicationPipe", BindingFlags.Public | BindingFlags.Instance)?.GetValue(device);
            var sock = pipe?.GetType().GetProperty("Socket", BindingFlags.Public | BindingFlags.Instance)?.GetValue(pipe) as Socket;
            if (sock == null) return (false, "socket 不可得");
            bool enabled = Convert.ToInt32(sock.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)) != 0;
            string detail;
            try
            {
                var outValue = new byte[12];
                sock.IOControl(IOControlCode.KeepAliveValues, null, outValue);
                detail = $"on={BitConverter.ToInt32(outValue, 0)} time={BitConverter.ToInt32(outValue, 4)}ms interval={BitConverter.ToInt32(outValue, 8)}ms";
            }
            catch (Exception ex)
            {
                detail = "IOControl-GET 不支持(" + ex.GetType().Name + ")，以 SO_KEEPALIVE 为准";
            }
            return (enabled, detail);
        }
        catch (Exception ex)
        {
            return (false, "读取失败: " + ex.Message);
        }
    }

    #endregion

    #region TcpRelay

    /// <summary>可掐断/恢复/静默的 TCP 继电器（与 CommStress 同思路）</summary>
    private sealed class TcpRelay : IDisposable
    {
        private readonly int _listenPort, _upstreamPort;
        private readonly List<Socket> _sockets = new();
        private Socket? _listener;
        private volatile bool _running;
        private volatile bool _paused;

        public TcpRelay(int listenPort, int upstreamPort) { _listenPort = listenPort; _upstreamPort = upstreamPort; }
        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, _listenPort));
            _listener.Listen(16);
            new Thread(AcceptLoop) { IsBackground = true }.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Close(); } catch { }
            lock (_sockets) { foreach (var s in _sockets) TryClose(s); _sockets.Clear(); }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                Socket client;
                try { client = _listener!.Accept(); }
                catch { break; }

                var up = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { up.Connect(IPAddress.Loopback, _upstreamPort); }
                catch { TryClose(client); TryClose(up); continue; }

                lock (_sockets) { _sockets.Add(client); _sockets.Add(up); }
                Pump(client, up);
                Pump(up, client);
            }
        }

        private void Pump(Socket from, Socket to)
        {
            new Thread(() =>
            {
                var buf = new byte[32 * 1024];
                try
                {
                    while (true)
                    {
                        int n = from.Receive(buf);
                        if (n <= 0) break;
                        if (_paused) continue;
                        to.Send(buf, 0, n, SocketFlags.None);
                    }
                }
                catch { }
                finally { TryClose(from); TryClose(to); }
            }) { IsBackground = true }.Start();
        }

        private static void TryClose(Socket? s)
        {
            if (s == null) return;
            try { s.Shutdown(SocketShutdown.Both); } catch { }
            try { s.Close(); } catch { }
        }

        public void Dispose() => Stop();
    }

    #endregion
}
