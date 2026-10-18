using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;

namespace DeviceSimulator
{
    /// <summary>
    /// VM 联合仿真用的设备模拟器（纯服务端，无界面）：
    /// 10 台 Modbus TCP（端口 10010-10019，字节序各不相同）+ 10 台西门子 S7（端口 10000-10009）。
    /// 全部预置数据每 1 秒自动刷新；客户端（上位机）的每一次读写请求都会在控制台打印日志，
    /// 便于联调时确认"外部到底写了什么、写到哪、读没读"。
    /// </summary>
    internal static class Program
    {
        private const int ModbusPortBase = 10010;   // Modbus TCP: 10010-10019
        private const int S7PortBase = 10000;       // S7:         10000-10009
        private const int DeviceCount = 10;
        private const int RefreshMs = 1000;

        // 10 台 Modbus 按此表轮转分配字节序：同一 32 位值在不同端口上以不同排列落线，
        // 客户端连接配置里选对应的字序才能读回原值——正好用来验证上位机的字节序处理。
        // 16 位寄存器（short/ushort）不受影响（ABCD≡CDAB、BADC≡DCBA），要验字节序请用 32 位值。
        private static readonly DataFormat[] Orders = { DataFormat.ABCD, DataFormat.CDAB, DataFormat.BADC, DataFormat.DCBA };

        /// <summary>读请求日志开关（写/连接日志恒开）。控制台输入 v 切换。</summary>
        internal static bool VerboseRead;

        private static readonly List<ModbusSimServer> ModbusDevices = new List<ModbusSimServer>();
        private static readonly List<S7SimServer> S7Devices = new List<S7SimServer>();

        private static readonly ManualResetEvent QuitEvent = new ManualResetEvent(false);
        private static readonly DateTime StartAt = DateTime.Now;
        private static Timer _refreshTimer;
        private static int _tick;
        private static readonly Dictionary<int, int> LastOnline = new Dictionary<int, int>();

        /// <summary>设备名（日志前缀），如 M:10010 / S7:10000。</summary>
        internal static string NameOf(int port) => port >= ModbusPortBase && port < ModbusPortBase + DeviceCount
            ? "M:" + port
            : "S7:" + port;

        #region 控制台日志（线程安全 + 颜色）

        private static readonly object LogSync = new object();

        internal static void Log(ConsoleColor color, string format, params object[] args)
        {
            lock (LogSync)
            {
                Console.Write("{0:HH:mm:ss.fff} ", DateTime.Now);
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(format, args);
                Console.ForegroundColor = prev;
            }
        }

        #endregion

        private static void Main()
        {
            try { Console.Title = "VM 联合仿真设备模拟器"; } catch { /* 输出重定向时设标题会抛，忽略 */ }
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; QuitEvent.Set(); };

            Console.WriteLine("==== VM 联合仿真设备模拟器 ====");
            Console.WriteLine("Modbus TCP x{0}: 端口 {1}-{2}（字节序各不相同，见下表）",
                DeviceCount, ModbusPortBase, ModbusPortBase + DeviceCount - 1);
            Console.WriteLine("S7 (S1200) x{0}: 端口 {1}-{2}（S7 报文恒为大端；Rack/Slot 不校验）",
                DeviceCount, S7PortBase, S7PortBase + DeviceCount - 1);
            Console.WriteLine("全部数据每 {0} 秒自动刷新；Modbus 站号不校验（任意站号可通）。", RefreshMs / 1000.0);
            Console.WriteLine("客户端的写请求 / 连接变化实时打印（读请求默认不打，v 开启）。");
            Console.WriteLine();
            Console.WriteLine("命令： list = 设备清单    v = 读日志开关    q = 退出");
            Console.WriteLine();

            StartModbus();
            StartS7();

            _refreshTimer = new Timer(OnTick, null, RefreshMs, RefreshMs);

            RunCommandLoop();

            if (_refreshTimer != null) _refreshTimer.Dispose();
            foreach (var d in ModbusDevices) d.ServerClose();
            foreach (var d in S7Devices) d.ServerClose();
            Console.WriteLine("已全部关闭，退出。");
        }

        #region 启动

        private static void StartModbus()
        {
            for (int i = 0; i < DeviceCount; i++)
            {
                int port = ModbusPortBase + i;
                bool free = TryBind(port);   // 先确认端口空闲（HSL 的 ServerStart 失败是静默的，必须自己查）
                var server = new ModbusSimServer(port, Orders[i % Orders.Length])
                {
                    StationCheck = false   // 任意站号都能通，联调时不被站号配置卡住
                };
                server.ServerStart(port);
                ModbusDevices.Add(server);
                PortFree[port] = free;
            }

            Console.WriteLine("---- Modbus TCP 设备（端口 → 字节序）----");
            foreach (var d in ModbusDevices)
                Console.WriteLine("  端口 {0}   字节序 {1}   {2}", d.Port, d.Order, PortFree[d.Port] ? "[监听中]" : "[!! 端口被占]");
            Console.WriteLine();
        }

        private static void StartS7()
        {
            for (int i = 0; i < DeviceCount; i++)
            {
                int port = S7PortBase + i;
                bool free = TryBind(port);
                var server = new S7SimServer(port);
                server.ServerStart(port);
                S7Devices.Add(server);
                PortFree[port] = free;
            }

            Console.WriteLine("---- S7 设备（端口 → 监听状态）----");
            foreach (var d in S7Devices)
                Console.WriteLine("  端口 {0}   大端(DB)   {1}", d.Port, PortFree[d.Port] ? "[监听中]" : "[!! 端口被占]");
            Console.WriteLine();
        }

        private static readonly Dictionary<int, bool> PortFree = new Dictionary<int, bool>();

        /// <summary>
        /// 端口空闲性检查：只做一次 Bind+Close（不建连，不给服务器制造假连接信号）。
        /// 必须在 ServerStart 之前调用；HSL 的 ServerStart 绑定失败不抛异常，端口被占只能在这里暴露。
        /// </summary>
        private static bool TryBind(int port)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                    return true;
                }
            }
            catch { return false; }
        }

        #endregion

        #region 连接事件轮询 + 数据刷新

        /// <summary>1 秒一拍：心跳 / 计数 / 温度刷新 + 连接数变化上报。</summary>
        private static void OnTick(object state)
        {
            int tick = Interlocked.Increment(ref _tick);
            bool heartbeat = tick % 2 == 0;

            for (int i = 0; i < ModbusDevices.Count; i++)
            {
                var d = ModbusDevices[i];
                try
                {
                    ushort counter = (ushort)((i + 1) * 1000 + tick);
                    int bigCounter = (i + 1) * 100000 + tick;
                    float temp = (float)(25.0 + 10.0 * Math.Sin(tick * 2.0 * Math.PI / 60.0 + i * Math.PI / 10.0));

                    d.Write("0", temp);                        // 4x HR0    float 温度（字节序敏感）
                    d.Write("10", counter);                    // 4x HR10   ushort 计数
                    d.Write("20", (short)1);                   // 4x HR20   状态字 = 运行中
                    d.Write("30", bigCounter);                 // 4x HR30   int 计数（字节序敏感）
                    d.Write("40", 123.5f);                     // 4x HR40   float 特征值（字节序校准用）
                    d.Write("x=4;0", counter);                 // 3x IR0    输入寄存器计数
                    d.Write("x=4;10", temp);                   // 3x IR10   输入寄存器温度

                    d.Write("0", heartbeat);                   // 0x 线圈 0 心跳
                    d.Write("x=2;0", heartbeat);               // 1x 离散输入 0 心跳
                    d.Write("x=2;1", true);                    // 1x 离散输入 1 常真
                }
                catch { /* 单台刷新失败不拖垮整体 */ }

                ReportOnline(d);
            }

            for (int i = 0; i < S7Devices.Count; i++)
            {
                var d = S7Devices[i];
                try
                {
                    ushort counter = (ushort)((i + 1) * 1000 + tick);
                    int bigCounter = (i + 1) * 100000 + tick;
                    float temp = (float)(25.0 + 10.0 * Math.Sin(tick * 2.0 * Math.PI / 60.0 + (i + 10) * Math.PI / 10.0));

                    d.Write("DB1.0", counter);                 // DB1.DBW0  WORD 计数
                    d.Write("DB1.10", temp);                   // DB1.DBD10 REAL 温度
                    d.Write("DB1.20", bigCounter);             // DB1.DBD20 DINT 计数
                    d.Write("DB1.30", (ushort)0x4242);         // DB1.DBW30 特征字
                    d.Write("M100", (byte)1);                  // MB100     状态 = 运行中
                    d.Write("M101", (byte)(heartbeat ? 1 : 0)); // MB101    心跳
                    d.Write("I0", (byte)(heartbeat ? 1 : 0));  // IB0       心跳
                }
                catch { /* 单台刷新失败不拖垮整体 */ }

                ReportOnline(d);
            }
        }

        /// <summary>连接数变化才打印：0→1 建连、1→0 掉线都能看到。</summary>
        private static void ReportOnline(DeviceServer server)
        {
            int now;
            try { now = server.OnlineCount; } catch { return; }

            int last;
            bool known = LastOnline.TryGetValue(server.Port, out last);
            LastOnline[server.Port] = now;
            if (!known || now != last)
                Log(ConsoleColor.DarkCyan, "[{0}] 连接数 {1} -> {2}", NameOf(server.Port), known ? last.ToString() : "?", now);
        }

        #endregion

        #region 命令循环

        private static void RunCommandLoop()
        {
            while (true)
            {
                if (QuitEvent.WaitOne(0)) break;

                string line = Console.ReadLine();
                if (line == null)
                {
                    // stdin 结束（后台 / 重定向运行时）：不退出，转入纯等待模式，靠 Ctrl+C 或杀进程结束
                    QuitEvent.WaitOne();
                    break;
                }

                line = line.Trim().ToLowerInvariant();
                if (line.Length == 0) continue;
                if (line == "q" || line == "quit" || line == "exit") break;
                if (line == "v")
                {
                    VerboseRead = !VerboseRead;
                    Log(ConsoleColor.DarkCyan, "读请求日志 {0}。", VerboseRead ? "已开启" : "已关闭");
                    continue;
                }
                if (line == "list") { PrintDevices(); continue; }
                Console.WriteLine("未知命令。支持： list = 设备清单    v = 读日志开关    q = 退出");
            }
        }

        private static void PrintDevices()
        {
            Console.WriteLine("---- 设备清单（运行 {0:hh\\:mm\\:ss}，数据刷新 tick={1}）----", DateTime.Now - StartAt, _tick);
            foreach (var d in ModbusDevices)
                Console.WriteLine("  Modbus  端口 {0}   字节序 {1}   连接数 {2}", d.Port, d.Order, TryOnline(d));
            foreach (var d in S7Devices)
                Console.WriteLine("  S7      端口 {0}   大端(DB)    连接数 {1}", d.Port, TryOnline(d));
            Console.WriteLine();
            Console.WriteLine("Modbus 预置（4x 保持寄存器为主）：");
            Console.WriteLine("  HR0     float   温度 25±10 正弦波动（字节序敏感，各端口相位不同）");
            Console.WriteLine("  HR10    ushort  计数，每秒 +1（第 N 台基准 = N*1000）");
            Console.WriteLine("  HR20    short   状态字 = 1（运行中）");
            Console.WriteLine("  HR30    int     计数，每秒 +1（第 N 台基准 = N*100000）");
            Console.WriteLine("  HR40    float   恒定特征值 123.5（字节序校准用：读回 123.5 即字序配对正确）");
            Console.WriteLine("  3x 输入寄存器（地址写 x=4;...）：IR0 计数、IR10 温度，与 HR 同步");
            Console.WriteLine("  线圈 0 / 离散输入 0 = 心跳（1 秒翻转）；离散输入 1 = 常真");
            Console.WriteLine();
            Console.WriteLine("S7 预置：");
            Console.WriteLine("  DB1.DBW0  WORD  计数，每秒 +1      DB1.DBD10  REAL  温度（正弦波动）");
            Console.WriteLine("  DB1.DBD20 DINT  计数，每秒 +1      DB1.DBW30  0x4242 特征字");
            Console.WriteLine("  MB100 = 1（状态）                  M101 / IB0 = 心跳（0/1 翻转）");
            Console.WriteLine();
        }

        private static string TryOnline(DeviceServer server)
        {
            try { return server.OnlineCount.ToString(); } catch { return "?"; }
        }

        #endregion
    }

    #region 公共小工具

    internal static class Dump
    {
        public static string Hex(byte[] data, int offset, int count)
        {
            var sb = new StringBuilder(count * 3);
            for (int i = 0; i < count && offset + i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[offset + i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>4 字节按指定 DataFormat 解码 float / int（Modbus 客户端写的寄存器字节按本机字序还原）。</summary>
        public static string Decode32(byte[] data, int offset, DataFormat order)
        {
            if (offset + 4 > data.Length) return string.Empty;
            var bt = new RegularByteTransform(order);
            float f = bt.TransSingle(data, offset);
            int n = bt.TransInt32(data, offset);
            return string.Format("float({0})={1:R} int={2}", order, f, n);
        }

        /// <summary>4 字节按 S7 大端解码 float / int。</summary>
        public static string Decode32BigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return string.Empty;
            var tmp = new byte[4];
            for (int i = 0; i < 4; i++) tmp[3 - i] = data[offset + i];
            float f = BitConverter.ToSingle(tmp, 0);
            int n = BitConverter.ToInt32(tmp, 0);
            return string.Format("float={0:R} int={1}", f, n);
        }

        public static string Decode16BigEndian(byte[] data, int offset)
        {
            if (offset + 2 > data.Length) return string.Empty;
            return ((data[offset] << 8) | data[offset + 1]).ToString();
        }
    }

    #endregion

    /// <summary>
    /// Modbus TCP 仿真设备：在报文入口（ReadFromCoreServer）拦截客户端请求打日志。
    /// 模拟器自身的预置刷新走 Write/WriteCoil 等服务器侧方法，不经过报文入口，不会被误记。
    /// </summary>
    internal class ModbusSimServer : ModbusTcpServer
    {
        /// <summary>
        /// 本机寄存器字节序（客户端按同名 DataFormat 配置即可读回原值）。
        /// <para>⚠ P-Q3 实测澄清：本属性<b>只用于日志解码展示</b>，从未下发到 HSL 服务端。
        /// HSL 的 <c>ModbusTcpServer.DataFormat</c> setter 只改 <c>base.ByteTransform</c>，
        /// 而请求帧解析全走私有字段 <c>byteTransformSelf</c>（构造时恒为 <c>DataFormat.CDAB</c>、从不更新）。
        /// 因此 10 台模拟设备的线上字节序<b>实际全部是 CDAB</b>——"每台字节序不同"的宣称名不副实。
        /// 客户端按端口宣称的字节序去读，只有 CDAB 与"16 位退化等价"的 ABCD 两台会碰巧对上，
        /// 这是<b>靶子特性，不是产品缺陷</b>；要验多字节序请让客户端统一按 CDAB 配置。</para>
        /// </summary>
        public DataFormat Order { get; }

        private readonly string _name;

        public ModbusSimServer(int port, DataFormat order)
        {
            Port = port;
            Order = order;
            _name = Program.NameOf(port);
        }

        protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
        {
            try { AnalyzeRequest(receive); }
            catch { /* 日志解析失败不影响正常处理 */ }

            var result = base.ReadFromCoreServer(session, receive);
            if (result != null && !result.IsSuccess)
                Program.Log(ConsoleColor.Red, "[{0}] 请求处理失败: {1}", _name, result.Message);
            return result;
        }

        /// <summary>
        /// 手解 Modbus-TCP 帧，打印客户端的读/写意图与写入数据。
        /// MBAP 头 7 字节：事务(2) + 协议号(2，恒 0x0000) + 长度(2) + 单元标识(1)，PDU 从第 8 字节起。
        /// </summary>
        private void AnalyzeRequest(byte[] receive)
        {
            if (receive == null || receive.Length < 12 || receive[2] != 0 || receive[3] != 0) return;

            int fc = receive[7];
            int addr = (receive[8] << 8) | receive[9];

            switch (fc)
            {
                case 1: // 读线圈
                case 2: // 读离散输入
                case 3: // 读保持寄存器
                case 4: // 读输入寄存器
                    {
                        if (!Program.VerboseRead) return;
                        int count = (receive[10] << 8) | receive[11];
                        string area = fc == 1 ? "线圈" : fc == 2 ? "离散输入" : fc == 3 ? "保持寄存器" : "输入寄存器";
                        Program.Log(ConsoleColor.DarkGray, "[{0}] 客户端读 {1} 地址={2} 数量={3}", _name, area, addr, count);
                        return;
                    }
                case 5: // 写单线圈
                    {
                        bool on = receive[10] != 0;
                        Program.Log(ConsoleColor.Yellow, "[{0}] 客户端写 线圈 地址={1} = {2}", _name, addr, on ? "ON" : "OFF");
                        return;
                    }
                case 6: // 写单寄存器
                    {
                        int value = (receive[10] << 8) | receive[11];
                        Program.Log(ConsoleColor.Yellow, "[{0}] 客户端写 寄存器 地址={1} = {2} (0x{3:X4})", _name, addr, value, value);
                        return;
                    }
                case 15: // 写多线圈
                case 16: // 写多寄存器
                    {
                        int count = (receive[10] << 8) | receive[11];
                        int byteCount = receive[12];
                        if (fc == 15)
                        {
                            var bits = new StringBuilder();
                            for (int i = 0; i < count && 13 + i / 8 < receive.Length; i++)
                            {
                                if (i > 0) bits.Append(',');
                                bits.Append((receive[13 + i / 8] >> (i % 8)) & 1);
                            }
                            Program.Log(ConsoleColor.Yellow, "[{0}] 客户端写 线圈 地址={1} 数量={2}: {3}", _name, addr, count, bits);
                        }
                        else
                        {
                            Program.Log(ConsoleColor.Yellow, "[{0}] 客户端写 寄存器 地址={1} 数量={2}: {3}{4}",
                                _name, addr, count, Dump.Hex(receive, 13, byteCount),
                                byteCount == 4 ? " → " + Dump.Decode32(receive, 13, Order) : string.Empty);
                        }
                        return;
                    }
            }
        }
    }

    /// <summary>
    /// S7 仿真设备：在报文入口（ReadFromCoreServer）拦截客户端请求打日志。
    /// S7 客户端写请求在本库内部直接落数据池（不经过 public Write 汇聚点），所以必须在报文层解析。
    /// </summary>
    internal class S7SimServer : SiemensS7Server
    {
        private readonly string _name;

        public S7SimServer(int port)
        {
            Port = port;
            _name = Program.NameOf(port);
        }

        protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
        {
            try { AnalyzeRequest(receive); }
            catch { /* 日志解析失败不影响正常处理 */ }

            var result = base.ReadFromCoreServer(session, receive);
            if (result != null && !result.IsSuccess)
                Program.Log(ConsoleColor.Red, "[{0}] 请求处理失败: {1}", _name, result.Message);
            return result;
        }

        /// <summary>
        /// 粗解 S7 请求流：TPKT(4) + COTP(1+LI) + S7 PDU（0x32 头 + 参数区 + 数据区），
        /// 识别读(0x04)/写(0x05)请求里的 Any-Pointer 与写入数据。
        /// 只求联调时"写了什么、写到哪"看得见，不追求完整协议校验；解不动就静默放弃。
        /// </summary>
        private void AnalyzeRequest(byte[] receive)
        {
            if (receive == null || receive.Length < 20 || receive[0] != 0x03) return;

            // TPKT 4 字节 + COTP（首字节为 LI，COTP 总长 = 1 + LI），S7 PDU 从其后开始
            int pdu = 5 + receive[4];
            if (pdu + 10 > receive.Length || receive[pdu] != 0x32) return;

            int msgType = receive[pdu + 1];
            if (msgType != 0x01) return; // 只关心客户端发来的 Job 请求（Ack_Data 是响应方向）

            int paramLen = (receive[pdu + 6] << 8) | receive[pdu + 7];
            int paramStart = pdu + 10;
            if (paramStart + paramLen > receive.Length) return;

            int function = receive[paramStart];
            int itemCount = paramLen >= 2 ? receive[paramStart + 1] : 0;

            if (function == 0x05) // 写请求
            {
                // 逐 item 解析参数区 Any-Pointer，数据区依次跟在每个 item 的 4 字节数据头之后
                int p = paramStart + 2;     // 跳过功能码 + item 数
                int d = paramStart + paramLen; // 数据区起点
                for (int i = 0; i < itemCount && p + 12 <= paramStart + paramLen; i++)
                {
                    if (receive[p] != 0x12 || receive[p + 1] != 0x0a || receive[p + 2] != 0x10) return; // 非常规 item，放弃

                    int area = receive[p + 8];
                    int db = (receive[p + 6] << 8) | receive[p + 7];
                    int addrBits = (receive[p + 9] << 16) | (receive[p + 10] << 8) | receive[p + 11];
                    int addrByte = addrBits >> 3;
                    string where = AreaName(area, db, addrByte, addrBits & 7);
                    p += 12;

                    if (d + 4 > receive.Length) return;
                    int transport = receive[d + 1];
                    int declared = (receive[d + 2] << 8) | receive[d + 3];
                    // S7 数据头的长度单位：transport=0x09（字符串）按字节，其余（0x01 位 / 0x03,0x04 字节字双字）按"位"
                    int byteLen = transport == 0x09 ? declared : (declared + 7) / 8;
                    if (d + 4 + byteLen > receive.Length) return;

                    Program.Log(ConsoleColor.Yellow, "[{0}] 客户端写 {1} x{2}字节: {3}{4}{5}{6}",
                        _name, where, byteLen, Dump.Hex(receive, d + 4, byteLen),
                        byteLen == 1 ? " → " + receive[d + 4] : string.Empty,
                        byteLen == 2 ? " → " + Dump.Decode16BigEndian(receive, d + 4) : string.Empty,
                        byteLen == 4 ? " → " + Dump.Decode32BigEndian(receive, d + 4) : string.Empty);

                    d += 4 + byteLen;
                }
            }
            else if (function == 0x04) // 读请求
            {
                if (!Program.VerboseRead) return;
                int p = paramStart + 2;
                for (int i = 0; i < itemCount && p + 12 <= paramStart + paramLen; i++)
                {
                    if (receive[p] != 0x12 || receive[p + 1] != 0x0a || receive[p + 2] != 0x10) return;
                    int area = receive[p + 8];
                    int db = (receive[p + 6] << 8) | receive[p + 7];
                    int addrBits = (receive[p + 9] << 16) | (receive[p + 10] << 8) | receive[p + 11];
                    int length = (receive[p + 4] << 8) | receive[p + 5];
                    Program.Log(ConsoleColor.DarkGray, "[{0}] 客户端读 {1} x{2}", _name,
                        AreaName(area, db, addrBits >> 3, addrBits & 7), length);
                    p += 12;
                }
            }
        }

        private static string AreaName(int area, int db, int addrByte, int bit)
        {
            switch (area)
            {
                case 0x81: return string.Format("I{0}.{1}", addrByte, bit);
                case 0x82: return string.Format("Q{0}.{1}", addrByte, bit);
                case 0x83: return bit != 0 ? string.Format("M{0}.{1}", addrByte, bit) : string.Format("M{0}", addrByte);
                case 0x84: return bit != 0 ? string.Format("DB{0}.DBX{1}.{2}", db, addrByte, bit) : string.Format("DB{0}.DB{1}", db, addrByte);
                case 0x85: return string.Format("DI{0}", addrByte);
                case 0x86: return string.Format("LC{0}", addrByte);
                case 0x1C: return string.Format("CNT{0}", addrByte);
                case 0x1D: return string.Format("TIMER{0}", addrByte);
                default: return string.Format("Area0x{0:X2} 偏移{1}", area, addrByte);
            }
        }
    }
}
