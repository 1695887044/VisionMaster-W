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

            // 纯单元断言先行：模拟器离线也要能验证字节序转换层（历史 bug 的回归防线）
            RunEndianUnitChecks();

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
        /// Modbus PDU 寄存器数据为大端：short 21 → 字节流必须是 [0x00,0x15]，
        /// 大端字节流 [0x04,0xC5] → 必须解释为 1221（旧小端实现分别得 [0x15,0x00] / 50436——
        /// 正是现场"写 21 设备显示 5376、设备写 1221 软件显示 50436"的暗号）
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
                var convertTo = helper.GetMethod("ConvertTo")!;

                var bytes21 = (byte[])getValueArray.Invoke(null, new object[] { (short)21 })!;
                Check("short 21 → 大端字节流", bytes21.Length == 2 && bytes21[0] == 0x00 && bytes21[1] == 0x15,
                    $"实际 [{string.Join(",", bytes21.Select(x => "0x" + x.ToString("X2")))}]");

                var read1221 = convertTo.MakeGenericMethod(typeof(short))
                    .Invoke(null, new object[] { new byte[] { 0x04, 0xC5 } });
                Check("大端字节流 → short 1221", (short)read1221! == 1221, $"实际 {read1221}（50436=旧小端特征值）");

                var bytesI = (byte[])getValueArray.Invoke(null, new object[] { 1221 })!;
                Check("int 1221 → 大端字节流(ABCD)", bytesI.Length == 4 && bytesI[0] == 0x00 && bytesI[1] == 0x00
                    && bytesI[2] == 0x04 && bytesI[3] == 0xC5,
                    $"实际 [{string.Join(",", bytesI.Select(x => "0x" + x.ToString("X2")))}]");

                var readI = convertTo.MakeGenericMethod(typeof(int))
                    .Invoke(null, new object[] { new byte[] { 0x00, 0x00, 0x04, 0xC5 } });
                Check("大端字节流 → int 1221", (int)readI! == 1221, $"实际 {readI}");
            }
            catch (Exception ex)
            {
                Check("字节序纯单元断言", false, ex.Message);
            }
        }

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
