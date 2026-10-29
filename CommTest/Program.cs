using System.Diagnostics;
using System.Text;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace CommTest
{
    /// <summary>
    /// 通信功能测试——网络变量方式（针对 127.0.0.1:502 Modbus TCP + 127.0.0.1:108 西门子 S7 模拟器）：
    ///
    /// 链路：AdvancedCommunicationManager(AddConnection+Connect)
    ///       → VariableFactory.CreateNetwork(建网络变量, 绑定连接名+DeviceAddressBase)
    ///       → var.Value 读=实时读设备 / 写=直通设备（NetworkVariableModel 核心契约）
    ///       → CommunicationVariable 轮询链路（RegisterVariable → PollVariables → ValueChanged）
    ///
    /// 写操作全部"写→回读→比对→清零"，不破坏模拟器状态。
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("========== 网络变量通信测试 ==========");
            Console.WriteLine("Modbus TCP: 127.0.0.1:502 | Siemens S7: 127.0.0.1:108 (S1200)");
            Console.WriteLine();

            // 只跑 EasyNet 基准（Modbus/S7 模拟器不在线时也能单独验证 12939）：
            //   set EASYNET_ONLY=1 && CommTest.exe
            if ((Environment.GetEnvironmentVariable("EASYNET_ONLY") ?? string.Empty).Trim() == "1")
            {
                RunEndianUnitChecks();
                RunEasyNetBench();
                Finish();
                return;
            }

            // 纯单元断言先行：模拟器离线也要能验证字节序转换层（历史 bug 的回归防线）
            RunEndianUnitChecks();

            // EasyNet 只读基准：独立于 Modbus/S7 模拟器，先跑（12939 不在时自动跳过）
            RunEasyNetBench();

            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = false };

            // ============ 建立两条连接（等同 UI 通讯设置里"添加连接"）============
            var modbusCfg = new CommunicationConfig("Test_Modbus", new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502 });
            var s7Cfg = new CommunicationConfig("Test_S7", new SiemensS7Config { IpAddress = "127.0.0.1", Port = 108, S7CpuType = S7CpuType.S1200 });

            try
            {
                manager.AddConnection(modbusCfg);
                manager.AddConnection(s7Cfg);
                Check("建连接 ×2 (Modbus/S7)", true, $"管理器持有 {manager.ConnectionCount} 个");
            }
            catch (Exception ex)
            {
                Check("建连接 ×2", false, ex.Message);
                Console.WriteLine("连接建立失败，无法继续。请确认模拟器已启动。");
                Finish();
                return;
            }

            bool mOk, sOk;
            try
            {
                mOk = manager.Connect("Test_Modbus");
                sOk = manager.Connect("Test_S7");
            }
            catch (Exception ex)
            {
                Check("连接建立", false, ex.Message);
                Finish();
                return;
            }
            Check("Connect(Modbus 502)", mOk, mOk ? "已连接" : "连接失败——检查模拟器");
            Check("Connect(S7 108)", sOk, sOk ? "已连接" : "连接失败——检查模拟器");
            if (!mOk && !sOk) { Finish(); return; }

            // ============ [A] 网络变量：Modbus 保持寄存器（short）============
            if (mOk)
            {
                Console.WriteLine();
                Console.WriteLine("---- [A] Modbus 网络变量（保持寄存器） ----");
                var addr = new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "99", DataType = DataValueType.Int16 };
                Check("ModbusAddress 构建地址", addr.Address == "40100", $"Offset=99 → {addr.Address}");

                var netVar = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    "MB_TestShort", typeof(short), "Test_Modbus", addr, "测试:保持寄存器short");
                InjectManager(netVar, manager);

                netVar.Value = (short)1234;                    // 写设备
                var back = netVar.Value;                        // 读设备
                Check("网络变量 写→读 40100 (short)", back is short s && s == 1234, $"期望 1234 实际 {back}");
                netVar.Value = (short)0;

                // float（占2寄存器）
                var addrF = new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "199", DataType = DataValueType.Float };
                var netVarF = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    "MB_TestFloat", typeof(float), "Test_Modbus", addrF, "测试:float");
                InjectManager(netVarF, manager);
                netVarF.Value = 3.14159f;
                var backF = netVarF.Value;
                Check("网络变量 写→读 40200 (float)", backF is float f && Math.Abs(f - 3.14159f) < 0.001f, $"期望 3.14159 实际 {backF}");
                netVarF.Value = 0f;

                // 线圈 bool
                var addrB = new ModbusAddress { Area = ModbusArea.Coils, Offset = "0", DataType = DataValueType.Boolean };
                var netVarB = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    "MB_TestCoil", typeof(bool), "Test_Modbus", addrB, "测试:线圈bool");
                InjectManager(netVarB, manager);
                netVarB.Value = true;
                var backB = netVarB.Value;
                Check("网络变量 写→读 00001 (bool)", backB is bool b && b, $"期望 True 实际 {backB}");
                netVarB.Value = false;
            }

            // ============ [B] 网络变量：S7 M 区 ============
            if (sOk)
            {
                Console.WriteLine();
                Console.WriteLine("---- [B] S7 网络变量（M 区） ----");
                var addrW = new S7Address { Area = S7Area.M, Offset = "10", DataType = DataValueType.Int16 };
                Check("S7Address 构建地址", addrW.Address == "MW10", $"Offset=10 → {addrW.Address}");

                var netVarS = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    "S7_TestShort", typeof(short), "Test_S7", addrW, "测试:M区short");
                InjectManager(netVarS, manager);
                netVarS.Value = (short)4321;
                var backS = netVarS.Value;
                Check("网络变量 写→读 MW10 (short)", backS is short s && s == 4321, $"期望 4321 实际 {backS}");
                netVarS.Value = (short)0;

                // 位变量 M0.0（修复后位地址无 X 前缀）
                var addrBit = new S7Address { Area = S7Area.M, Offset = "0", DataType = DataValueType.Boolean, BitOffset = 0 };
                Check("S7Address 位地址", addrBit.Address == "M0.0", $"→ {addrBit.Address}");

                // 位地址全格式回归：Q/I/V 区 + DB 位
                var addrQ = new S7Address { Area = S7Area.Q, Offset = "1", DataType = DataValueType.Boolean, BitOffset = 3 };
                Check("S7Address 位地址 Q", addrQ.Address == "Q1.3", $"→ {addrQ.Address}");
                var addrI = new S7Address { Area = S7Area.I, Offset = "2", DataType = DataValueType.Boolean, BitOffset = 5 };
                Check("S7Address 位地址 I", addrI.Address == "I2.5", $"→ {addrI.Address}");
                var addrV = new S7Address { Area = S7Area.V, Offset = "10", DataType = DataValueType.Boolean, BitOffset = 1 };
                Check("S7Address 位地址 V", addrV.Address == "V10.1", $"→ {addrV.Address}");
                var addrDbBit = new S7Address { Area = S7Area.DB, DbNumber = 2, Offset = "4", DataType = DataValueType.Boolean, BitOffset = 7 };
                Check("S7Address 位地址 DB", addrDbBit.Address == "DB2.DBX4.7", $"→ {addrDbBit.Address}");

                // 字地址类型前缀回归
                var addrB = new S7Address { Area = S7Area.M, Offset = "0", DataType = DataValueType.Byte };
                Check("S7Address 字节地址", addrB.Address == "MB0", $"→ {addrB.Address}");
                var addrD = new S7Address { Area = S7Area.M, Offset = "100", DataType = DataValueType.Float };
                Check("S7Address 双字地址", addrD.Address == "MD100", $"→ {addrD.Address}");

                var netVarBit = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    "S7_TestBit", typeof(bool), "Test_S7", addrBit, "测试:M位");
                InjectManager(netVarBit, manager);
                netVarBit.Value = true;
                var backBit = netVarBit.Value;
                Check("网络变量 写→读 M0.0 (bool)", backBit is bool b && b, $"期望 True 实际 {backBit}");
                netVarBit.Value = false;

                // DB 区（模拟器可能限制，失败降级为提示）
                try
                {
                    var addrDb = new S7Address { Area = S7Area.DB, DbNumber = 1, Offset = "0", DataType = DataValueType.Int16 };
                    var netVarDb = (NetworkVariableModel)VariableFactory.CreateNetwork(
                        "S7_TestDB", typeof(short), "Test_S7", addrDb, "测试:DB块");
                    InjectManager(netVarDb, manager);
                    netVarDb.Value = (short)777;
                    var backDb = netVarDb.Value;
                    Check("网络变量 写→读 DB1.DBW0 (short)", backDb is short sv && sv == 777, $"期望 777 实际 {backDb}");
                    netVarDb.Value = (short)0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [跳过] DB 区（模拟器限制）：{ex.Message}");
                }
            }

            // ============ [C] CommunicationVariable 轮询链路 ============
            Console.WriteLine();
            Console.WriteLine("---- [C] CommunicationVariable 轮询链路（RegisterVariable → Poll → ValueChanged） ----");
            if (mOk)
            {
                var pollVar = new CommunicationVariable
                {
                    ConnectionName = "Test_Modbus",
                    VariableName = "Poll_Short",
                    Address = "40300",
                    ValueType = typeof(short).AssemblyQualifiedName,
                    AccessMode = VariableAccessMode.ReadOnly
                };
                short? polled = null;
                int eventCount = 0;
                pollVar.ValueChanged += (_, v) => { polled = (short?)v; eventCount++; };
                manager.RegisterVariable(pollVar);

                // 先写一个已知值
                var rawConn = manager.GetConnection("Test_Modbus");
                rawConn!.Write("40300", (short)555);

                // 轮询定时器默认 ReadCycleMs=1000，等 2.5s 必然触发 ≥1 次
                Thread.Sleep(2500);
                Check("轮询读到设备值", polled == 555, $"事件{eventCount}次, 值={polled}");

                // 设备值变化 → 再次轮询 → ValueChanged 再触发
                rawConn.Write("40300", (short)777);
                Thread.Sleep(2500);
                Check("轮询捕获值变化", polled == 777, $"事件{eventCount}次, 值={polled}");

                manager.Write("Test_Modbus", "40300", (short)0);
                manager.UnregisterVariable("Test_Modbus", "Poll_Short");
            }

            // ============ [G] 字节序交叉验证（第三方视角） ============
            // 历史教训：本测试此前全是"软件自己写、自己读"——小端写+小端读两次颠倒抵消，
            // 字节序 bug 永远测不出来（实测暗号：写 21 设备端变 5376，设备写 1221 软件读成 50436）。
            // 本节用 HSL 标准强类型接口（独立实现，Modbus 大端）交叉核对软件写入的原始寄存器值。
            if (mOk)
            {
                Console.WriteLine();
                Console.WriteLine("---- [G] 字节序交叉验证（软件写 → HSL 标准读） ----");
                try
                {
                    using var probe = new HslCommunication.ModBus.ModbusTcpNet("127.0.0.1", 502);
                    if (!probe.ConnectServer().IsSuccess)
                    {
                        Check("探针连接 502", false, "无法建立第二条 Modbus 连接");
                    }
                    else
                    {
                        // 经软件链路写 short 21（0x0015）到 40100
                        var addrG = new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "99", DataType = DataValueType.Int16 };
                        var netVarG = (NetworkVariableModel)VariableFactory.CreateNetwork(
                            "MB_EndianProbe", typeof(short), "Test_Modbus", addrG, "字节序探针");
                        InjectManager(netVarG, manager);
                        netVarG.Value = (short)21;

                        // HSL 标准大端读同一寄存器：修复后应得 21；旧小端实现会得 0x1500=5376
                        var r16 = probe.ReadInt16("40100");
                        Check("short 字节序：软件写 21 → HSL 标准读", r16.IsSuccess && r16.Content == 21,
                            $"期望 21 实际 {r16.Content}（5376=字节颠倒）");

                        // int 32 位 ABCD 字序：软件写 1221（0x000004C5）→ HSL 标准读
                        var addrG32 = new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "98", DataType = DataValueType.Int32 };
                        var netVarG32 = (NetworkVariableModel)VariableFactory.CreateNetwork(
                            "MB_EndianProbe32", typeof(int), "Test_Modbus", addrG32, "字节序探针32");
                        InjectManager(netVarG32, manager);
                        netVarG32.Value = 1221;
                        var r32 = probe.ReadInt32("40099"); // Offset=98 → 40099，占 40099+40100
                        Check("int 字序(ABCD)：软件写 1221 → HSL 标准读", r32.IsSuccess && r32.Content == 1221,
                            $"期望 1221 实际 {r32.Content}");

                        // 清零复原，不破坏模拟器状态
                        netVarG.Value = (short)0;
                        netVarG32.Value = 0;
                        probe.ConnectClose();
                    }
                }
                catch (Exception ex)
                {
                    Check("字节序交叉验证", false, ex.Message);
                }
            }

            // ============ [D] 清理 ============
            Console.WriteLine();
            try
            {
                manager.DisconnectAll();
                Check("DisconnectAll", manager.ConnectedCount == 0, $"剩余连接 {manager.ConnectedCount}");
                manager.RemoveConnection("Test_Modbus");
                manager.RemoveConnection("Test_S7");
            }
            catch (Exception ex)
            {
                Check("清理", false, ex.Message);
            }

            Finish();
        }

        /// <summary>
        /// [E] EasyNet 批量读实时基准（只读）：把汇川导出的变量表（12.csv）整表接入轮询，
        /// 量"首轮全量刷新耗时 / 实测周期 / 段数"，验证 2400 点规模的时效性。
        /// <para>只读：全程不调用任何 Write，模拟器/PLC 状态不变。</para>
        /// <para>变量表缺失时退回内置小样本，保证没有桌面文件也能跑通链路。</para>
        /// </summary>
        private static void RunEasyNetBench()
        {
            const string host = "127.0.0.1";
            int port = int.TryParse(Environment.GetEnvironmentVariable("EASYNET_PORT"), out int envPort)
                       && envPort > 0 ? envPort : 12939;

            Console.WriteLine();
            Console.WriteLine($"---- [E] EasyNet 批量读基准（{host}:{port}，只读） ----");

            const string csvPath = @"C:\Users\16958\Desktop\12.csv";
            string sample = (Environment.GetEnvironmentVariable("EASYNET_SAMPLE") ?? string.Empty).Trim().ToLowerInvariant();
            List<EasyNetSpec> specs = sample == "classic"
                ? ClassicEasyNetSpecs()
                : File.Exists(csvPath) ? LoadEasyNetSpecs(csvPath) : DefaultEasyNetSpecs();

            bool allowWrite = (Environment.GetEnvironmentVariable("EASYNET_ALLOW_WRITE") ?? string.Empty).Trim() == "1";

            if (specs.Count == 0)
            {
                Console.WriteLine("  [跳过] 没有可用的点位定义");
                return;
            }

            Console.WriteLine($"  点位定义 {specs.Count} 条（{(sample == "classic" ? "classic 样本" : File.Exists(csvPath) ? csvPath : "内置样本")}）" +
                              (allowWrite ? "；含写回读" : "；只读"));

            // 周期可调：EASYNET_CYCLE_MS=50 可量"更快的刷新是否还有余量"（默认 100，与现场变量表一致）
            int cycleMs = int.TryParse(Environment.GetEnvironmentVariable("EASYNET_CYCLE_MS"), out int envCycle)
                          && envCycle > 0 ? envCycle : 100;

            // 多扫描组：EASYNET_GROUPS=100,200,500 → 默认组取第一个值，其余建自定义组 G200/G500；
            // 变量按序号轮转分到各组（验证多周期调度：快组不被慢组拖累、慢组不被快组压垮）
            var groupCycles = new List<int>();
            string groupsText = (Environment.GetEnvironmentVariable("EASYNET_GROUPS") ?? string.Empty).Trim();
            if (groupsText.Length > 0)
            {
                foreach (string part in groupsText.Split(','))
                {
                    if (int.TryParse(part.Trim(), out int g) && g > 0) groupCycles.Add(g);
                }
            }
            if (groupCycles.Count > 0) cycleMs = groupCycles[0];

            bool blockMode = string.Equals(
                (Environment.GetEnvironmentVariable("EASYNET_GROUP_MODE") ?? string.Empty).Trim(),
                "block", StringComparison.OrdinalIgnoreCase);

            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = false };
            var cfg = new CommunicationConfig("EasyNet_Bench", new EasyNetConfig
            {
                // 各 Config 子类都不覆写 Type（默认 0 = ModbusTcp）：手工构造必须显式赋，
                // 否则 CommunicationConfig 构造器会把 Protocol 设成 ModbusTcp，工厂按枚举取错实现
                Type = CommunicationType.EasyNet,
                IpAddress = host,
                Port = port
            })
            {
                ReadCycleMs = cycleMs
            };

            for (int g = 1; g < groupCycles.Count; g++)
                cfg.ScanGroups.Add(new ScanGroupConfig { Name = $"G{groupCycles[g]}", IntervalMs = groupCycles[g] });

            try
            {
                manager.AddConnection(cfg);
            }
            catch (Exception ex)
            {
                Check("EasyNet 建连接", false, ex.Message);
                return;
            }

            var vars = new List<CommunicationVariable>(specs.Count);
            int index = 0;
            foreach (EasyNetSpec spec in specs)
            {
                var address = new EasyNetAddress
                {
                    Area = spec.Area,
                    Offset = spec.Offset,
                    DataType = spec.DataType,
                    Length = spec.Length
                };

                // 多组时分配：默认按序号轮转（slot=0 进默认组，其余进 G<周期>）；
                // EASYNET_GROUP_MODE=block 改为按地址块连续切分——现场真实用法是按区段分组，
                // 轮转会把连续地址打散、段数暴涨（实测 29 段 → 80 段）。
                string groupName = string.Empty;
                if (groupCycles.Count > 1)
                {
                    int slot = blockMode
                        ? Math.Min(groupCycles.Count - 1, index * groupCycles.Count / Math.Max(1, specs.Count))
                        : index % groupCycles.Count;
                    if (slot > 0) groupName = $"G{groupCycles[slot]}";
                }

                vars.Add(new CommunicationVariable
                {
                    ConnectionName = cfg.ConnectionName,
                    // 同名变量在管理器里是按名去重的；加序号保证 CSV 里的重名/重址都各自成点
                    VariableName = $"EZ_{index++}_{spec.Name}",
                    Address = address.Address,
                    AddressConfig = address,
                    ValueType = spec.ClrType.AssemblyQualifiedName,
                    AccessMode = VariableAccessMode.ReadOnly,
                    ScanGroup = groupName
                });
            }

            int published = 0;
            foreach (CommunicationVariable variable in vars)
                variable.ValueChanged += (_, _) => Interlocked.Increment(ref published);

            foreach (CommunicationVariable variable in vars)
                manager.RegisterVariable(variable);

            var sw = Stopwatch.StartNew();
            bool connected = manager.Connect(cfg.ConnectionName);
            Check($"EasyNet Connect({host}:{port})", connected,
                connected ? "已连接" : "连接失败——确认模拟器在 12939");
            if (!connected)
            {
                manager.RemoveConnection(cfg.ConnectionName);
                return;
            }

            // 首次值无论是否变化都会强制推送，故"全部变量各推一次"= 第一轮全量刷新完成
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (Volatile.Read(ref published) < vars.Count && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            sw.Stop();

            int good = vars.Count(v => v.Quality == VariableQuality.Good);
            int uncertain = vars.Count(v => v.Quality == VariableQuality.Uncertain);
            int bad = vars.Count(v => v.Quality == VariableQuality.Bad);

            Check($"首轮全量刷新 {vars.Count} 点", published >= vars.Count,
                $"{published}/{vars.Count}，用时 {sw.ElapsedMilliseconds} ms");
            Check("首轮质量全部 Good", good == vars.Count,
                $"Good={good} Uncertain={uncertain} Bad={bad}");

            // 采样 5 秒看稳态周期（AvgActualMs 是相邻两次开跑的指数滑动平均）
            Thread.Sleep(5000);
            IReadOnlyList<ScanGroupStats> stats = manager.GetScanGroupStats(cfg.ConnectionName);
            Check("扫描组已编译", stats.Count > 0, stats.Count == 0 ? "无扫描组" : "");

            if (stats.Count > 0)
            {
                // 多组时逐组打印：快组与慢组各自的目标/实测/达成率，用来判断"慢组会不会拖累快组"
                foreach (ScanGroupStats group in stats)
                {
                    Console.WriteLine(
                        $"  组={group.GroupName} 变量={group.VariableCount} 段={group.SegmentCount} 兜底={group.FallbackCount} " +
                        $"目标={group.TargetIntervalMs}ms 实测={group.AvgActualMs}ms 达成率={group.AchieveRate:P0} 坏段={group.FaultedSegmentCount}");
                }

                // 判据按各自目标放宽：慢组（500ms）的实测必然 > 500，不能用一个固定上限
                bool allReasonable = stats.All(s => s.AvgActualMs > 0 && s.AvgActualMs < s.TargetIntervalMs * 2 + 100);
                Check("每组稳态实测周期都在目标 2 倍 + 100ms 以内", allReasonable,
                    string.Join("；", stats.Select(s => $"{s.GroupName}:{s.AvgActualMs}/{s.TargetIntervalMs}")));
            }

            if (allowWrite)
                RunEasyNetWriteSmoke(manager, cfg.ConnectionName);

            manager.DisconnectAll();
            manager.RemoveConnection(cfg.ConnectionName);
        }

        /// <summary>
        /// 写入回读（只对可控的参考服务器开）：位写 / 字写 / 字节块写各一条。
        /// 用户现场的模拟器是只读的，故默认不跑；`EASYNET_ALLOW_WRITE=1` 才执行。
        /// </summary>
        private static void RunEasyNetWriteSmoke(AdvancedCommunicationManager manager, string connectionName)
        {
            try
            {
                manager.Write(connectionName, "D100", (short)0x1234);
                short back = manager.Read<short>(connectionName, "D100");
                Check("写回读 D100 (Int16)", back == 0x1234, $"期望 0x1234 实际 0x{back:X4}");

                manager.Write(connectionName, "M0", new[] { true, false, true, true });
                bool[] bits = manager.GetConnection(connectionName)!.ReadBits("M0", 4);
                Check("写回读 M0 (4 位)", bits.Length == 4 && bits[0] && !bits[1] && bits[2] && bits[3],
                    $"[{string.Join(',', bits)}]");

                byte[] payload = Encoding.ASCII.GetBytes("EZ-OK\0\0\0");
                manager.Write(connectionName, "D300", payload);
                byte[] raw = manager.GetConnection(connectionName)!.ReadBytes("D300", (ushort)payload.Length);
                Check("写回读 D300 (8 字节)", Encoding.ASCII.GetString(raw).TrimEnd('\0') == "EZ-OK",
                    $"实际「{Encoding.ASCII.GetString(raw).TrimEnd('\0')}」");
            }
            catch (Exception ex)
            {
                Check("EasyNet 写回读", false, ex.Message);
            }
        }

        private sealed record EasyNetSpec(
            string Name, string Offset, DataValueType DataType, Type ClrType, int Length,
            EasyNetArea Area = EasyNetArea.U);

        /// <summary>
        /// 解析汇川导出的变量表：列 A=名称、C=数据类型、D=长度、F=地址（"UB 2100070" / "UW 21001C0"）。
        /// <para>只取前 6 列——注释列在第 12 列，里面可能有逗号，但不会影响前面的列。</para>
        /// </summary>
        private static List<EasyNetSpec> LoadEasyNetSpecs(string path)
        {
            var list = new List<EasyNetSpec>();

            foreach (string raw in ReadCsvLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                // 汇川导出是**带引号**的 CSV，且变量名里可能含逗号（Sv_CacheArys[0,0]）——
                // 按逗号切分会整体错位（地址列被挤到别的下标，整表解析成 0 条）。改为按列语义逐项匹配。
                var nameMatch = System.Text.RegularExpressions.Regex.Match(line, "^\"([^\"]*)\"");
                var addrMatch = System.Text.RegularExpressions.Regex.Match(line, ",\"(U[BW]? ?[0-9A-Fa-f]+)\"");
                var typeMatch = System.Text.RegularExpressions.Regex.Match(line, ",\"([A-Za-z][A-Za-z0-9]*)\",\"(\\d+)\",\"(\\d+)\"");
                if (!nameMatch.Success || !addrMatch.Success || !typeMatch.Success) continue;

                string name = nameMatch.Groups[1].Value;
                string address = addrMatch.Groups[1].Value.Trim();
                string type = typeMatch.Groups[1].Value.ToLowerInvariant();
                int length = int.TryParse(typeMatch.Groups[2].Value, out int n) ? Math.Max(1, n) : 1;

                string body;
                if (address.StartsWith("UB", StringComparison.OrdinalIgnoreCase)
                    || address.StartsWith("UW", StringComparison.OrdinalIgnoreCase))
                    body = address.Substring(2);
                else
                    body = address.Substring(1);

                string offset = body.Trim();
                if (offset.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    offset = offset.Substring(2);
                if (offset.Length == 0) continue;

                switch (type)
                {
                    case "bool":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.Boolean, typeof(bool), 1));
                        break;
                    case "int16":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.Int16, typeof(short), 1));
                        break;
                    case "uint16":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.UInt16, typeof(ushort), 1));
                        break;
                    case "int32":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.Int32, typeof(int), 1));
                        break;
                    case "uint32":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.UInt32, typeof(uint), 1));
                        break;
                    case "float":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.Float, typeof(float), 1));
                        break;
                    case "double":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.Double, typeof(double), 1));
                        break;
                    case "string":
                        list.Add(new EasyNetSpec(name, offset, DataValueType.String, typeof(string), length));
                        break;
                }
            }

            return list;
        }

        /// <summary>
        /// 汇川导出是**无 BOM 的 GB18030**：按 UTF-8 硬解会把整表中文读成 U+FFFD/错字。
        /// 先按严格 UTF-8 试解（纯 ASCII / 真 UTF-8 走这条），失败回落 GB18030。
        /// </summary>
        private static string[] ReadCsvLines(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes[3..]).Replace("\r\n", "\n").Split('\n');

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).Replace("\r\n", "\n").Split('\n');
            }
            catch
            {
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* 已注册 */ }
                return Encoding.GetEncoding("GB18030").GetString(bytes).Replace("\r\n", "\n").Split('\n');
            }
        }

        /// <summary>没有 12.csv 时的最小样本（覆盖位/字/字符串三种解码路径）。</summary>
        private static List<EasyNetSpec> DefaultEasyNetSpecs() => new()
        {
            new EasyNetSpec("Bit0", "2100070", DataValueType.Boolean, typeof(bool), 1),
            new EasyNetSpec("Bit1", "2100071", DataValueType.Boolean, typeof(bool), 1),
            new EasyNetSpec("Int32", "21001C0", DataValueType.Int32, typeof(int), 1),
            new EasyNetSpec("Float", "501FE0", DataValueType.Float, typeof(float), 1),
            new EasyNetSpec("String", "5044C0", DataValueType.String, typeof(string), 50),
        };

        /// <summary>
        /// 传统区样本（D/M 区）：给参考实现 EasyNetServer 用——它按原版路由，U 区地址
        /// （0x2xxxxxx）会落到 M/S 区并越界报错，不能拿 CSV 那批 U 地址去打它。
        /// </summary>
        private static List<EasyNetSpec> ClassicEasyNetSpecs() => new()
        {
            new EasyNetSpec("D100", "100", DataValueType.Int16, typeof(short), 1, EasyNetArea.D),
            new EasyNetSpec("D200", "200", DataValueType.Int32, typeof(int), 1, EasyNetArea.D),
            new EasyNetSpec("M0", "0", DataValueType.Boolean, typeof(bool), 1, EasyNetArea.M),
            new EasyNetSpec("M1", "1", DataValueType.Boolean, typeof(bool), 1, EasyNetArea.M),
            new EasyNetSpec("D300", "300", DataValueType.String, typeof(string), 8, EasyNetArea.D),
        };

        /// <summary>
        /// 注入通信管理器：NetworkVariableModel.CommunicationManager 是私有字段且全库无赋值点
        /// （UI 链路同样缺失此注入），测试用反射补上；后续应在 UI 建变量处正式注入
        /// </summary>
        private static void InjectManager(NetworkVariableModel netVar, AdvancedCommunicationManager manager)
        {
            var field = typeof(NetworkVariableModel).GetField(
                "CommunicationManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(netVar, manager);
        }

        /// <summary>
        /// [H] 字节序纯单元断言（不依赖模拟器，验证转换层本身）。
        /// HslHelper 是 Communication 程序集的 internal 类，反射调用。
        /// 命名含义：ABCD = 逻辑值字节 A(最高)~D(最低) 在【线路上】的排列顺序，
        /// 故逻辑值 0x12345678 在四种字序下的线路字节依次是：
        /// ABCD=[12,34,56,78]  BADC=[34,12,78,56]  CDAB=[56,78,12,34]  DCBA=[78,56,34,12]。
        /// 16 位自动退化：ABCD≡CDAB、BADC≡DCBA（与 HSL ByteTransDataFormat2 的分支一致）。
        /// 历史暗号：写 21 设备显示 5376、设备写 1221 软件显示 50436 = 小端误解大端。
        /// </summary>
        private static void RunEndianUnitChecks()
        {
            Console.WriteLine("---- [H] 字节序纯单元断言（反射 HslHelper） ----");
            try
            {
                var asm = typeof(AdvancedCommunicationManager).Assembly;
                var helper = asm.GetType("VisionMaster.Communications.HslHelper")
                    ?? throw new Exception("找不到 HslHelper（改名了？）");
                var getValueArray = helper.GetMethod("GetValueArray")!;
                // HslHelper 有 byte[] 与 ReadOnlySpan<byte> 两个 ConvertTo 重载，按名取会 AmbiguousMatch；
                // 显式指定参数类型（byte[] 版是断言用的稳定入口）
                var convertTo = helper.GetMethod("ConvertTo", new[] { typeof(byte[]), typeof(ByteOrderFormat) })!;
                var toDataFormat = helper.GetMethod("ToDataFormat")!;

                // ---- 写侧编码（兜底路径 GetValueArray，恒定大端）----
                var bytes21 = (byte[])getValueArray.Invoke(null, new object[] { (short)21 })!;
                Check("short 21 → 大端字节流", bytes21.Length == 2 && bytes21[0] == 0x00 && bytes21[1] == 0x15,
                    $"实际 [{Hex(bytes21)}]");

                var bytesI = (byte[])getValueArray.Invoke(null, new object[] { 1221 })!;
                Check("int 1221 → 大端字节流(ABCD)", bytesI.Length == 4 && bytesI[0] == 0x00 && bytesI[1] == 0x00
                    && bytesI[2] == 0x04 && bytesI[3] == 0xC5,
                    $"实际 [{Hex(bytesI)}]");

                // ---- 读侧解码：4 种字序 × 16/32/64 位（ABCD/CDAB 在 16 位退化等价）----
                CheckOrder<short>("short", convertTo, ByteOrderFormat.ABCD, 0x1234, 0x12, 0x34);
                CheckOrder<short>("short", convertTo, ByteOrderFormat.CDAB, 0x1234, 0x12, 0x34);
                CheckOrder<short>("short", convertTo, ByteOrderFormat.BADC, 0x1234, 0x34, 0x12);
                CheckOrder<short>("short", convertTo, ByteOrderFormat.DCBA, 0x1234, 0x34, 0x12);

                CheckOrder<int>("int", convertTo, ByteOrderFormat.ABCD, 0x12345678, 0x12, 0x34, 0x56, 0x78);
                CheckOrder<int>("int", convertTo, ByteOrderFormat.BADC, 0x12345678, 0x34, 0x12, 0x78, 0x56);
                CheckOrder<int>("int", convertTo, ByteOrderFormat.CDAB, 0x12345678, 0x56, 0x78, 0x12, 0x34);
                CheckOrder<int>("int", convertTo, ByteOrderFormat.DCBA, 0x12345678, 0x78, 0x56, 0x34, 0x12);

                CheckOrder<long>("long", convertTo, ByteOrderFormat.ABCD, 0x1122334455667788L,
                    0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88);
                CheckOrder<long>("long", convertTo, ByteOrderFormat.BADC, 0x1122334455667788L,
                    0x22, 0x11, 0x44, 0x33, 0x66, 0x55, 0x88, 0x77);
                CheckOrder<long>("long", convertTo, ByteOrderFormat.CDAB, 0x1122334455667788L,
                    0x77, 0x88, 0x55, 0x66, 0x33, 0x44, 0x11, 0x22);
                CheckOrder<long>("long", convertTo, ByteOrderFormat.DCBA, 0x1122334455667788L,
                    0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11);

                // ---- 契约字序 → HSL 字序：必须按【枚举名】映射（两套枚举序号不同，序号强转必错位）----
                foreach (var order in new[]
                         { ByteOrderFormat.ABCD, ByteOrderFormat.BADC, ByteOrderFormat.CDAB, ByteOrderFormat.DCBA })
                {
                    var mapped = toDataFormat.Invoke(null, new object[] { order })!.ToString();
                    Check($"ToDataFormat({order}) → HSL {order}", mapped == order.ToString(), $"实际 {mapped}");
                }
            }
            catch (Exception ex)
            {
                Check("字节序纯单元断言", false, ex.Message);
            }
        }

        /// <summary>按指定字序解码线路字节，断言等于期望逻辑值</summary>
        private static void CheckOrder<T>(string typeName, System.Reflection.MethodInfo convertTo,
            ByteOrderFormat order, T expected, params byte[] wire) where T : struct
        {
            var value = (T)convertTo.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { wire, order })!;
            Check($"{typeName} 线路[{Hex(wire)}] → {order} 解出 {expected}", value.Equals(expected), $"实际 {value}");
        }

        private static string Hex(byte[] bytes) => string.Join(",", bytes.Select(x => "0x" + x.ToString("X2")));

        private static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}  {detail}");
        }

        private static void Finish()
        {
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部测试通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }
    }
}
