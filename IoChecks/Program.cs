using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Io;
using VisionMaster.Communications;
using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Services.Io;
using VisionMaster.ViewModels.DialogViewModels;

namespace IoChecks
{
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("=== IO Checks ===");

            TestDeviceContract();
            TestVariableCatalog();
            TestDeterministicId();
            TestInputVariableReadOnly();
            TestOutputVariableWrite();
            TestSystemVariableCoexistence();
            TestDescriptorValidation();
            TestIoConfigViewModel();
            TestPointSearchAndAudit();
            TestSolutionRoundTrip();
            TestDriverRegistry();
            TestModbusTcpIntegration();
            TestSiemensS7Integration();
            TestModbusRtu();
            TestSystemVariableHost();
            TestPointIsolation();
            TestLeaseRegistry();
            TestStatusVariables();
            TestWriteScope();
            TestVariableSubscription();
            TestAuditRecording();
            TestBranchScope();
            TestHealthMonitor();
            TestSchemaVersion();
            TestBusLinkSwitching();
            TestDriverConfigSwitching();
            TestHealthViewModel();
            TestCommunicationBinder();
            TestConfigMigrator();
            TestBoundaryAndExceptionCases();

            Console.WriteLine("=== IO 视图渲染冒烟 ===");
            IoViewSmoke.Run((name, ok, detail) => CurrentSection.Check(name, ok, detail));

            TestCase.PrintSummary();
            return TestCase.FailedCount == 0 ? 0 : 1;
        }

        /// <summary>
        /// 当前测试段落。用例 ID 由"段落号.序号"组成（如 D1.3 / M2.5），
        /// 报告里能直接定位到是哪一组、哪一条。
        /// </summary>
        private static SectionScope CurrentSection = SectionScope.None;

        private static void BeginSection(string section)
        {
            CurrentSection = new SectionScope(section);
            Console.WriteLine();
            Console.WriteLine($"--- [{section}] ---");
        }

        private sealed class SectionScope
        {
            public static readonly SectionScope None = new("X");
            private readonly string _section;
            private int _index;

            public SectionScope(string section) => _section = section;

            public void Check(string name, bool condition, string? detail = null)
                => TestCase.Check($"{_section}.{++_index}", name, name, condition, detail);

            public void Skip(string name, string reason)
                => TestCase.Skip($"{_section}.{++_index}", name, reason);
        }

        private static void Check(string name, bool condition, string? detail = null)
            => CurrentSection.Check(name, condition, detail);

        private static void Skip(string name, string reason)
            => CurrentSection.Skip(name, reason);


        private static void TestDeviceContract()
        {
            BeginSection("D");
            var bus = new IoBusDescriptor
            {
                BusId = "BUS_1",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "127.0.0.1", Port = 502 }
            };

            var descriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1",
                BusId = bus.BusId,
                DriverId = "test",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" },
                    new() { PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "2" },
                    new() { PointId = "AO_1", Kind = IoPointKind.AnalogOutput, DataType = IoDataType.Int16, Address = "3", Scale = 0.1 }
                },
                Params = new IoParams
                {
                    PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 20 } }
                }
            };

            using var device = new TestDevice(descriptor, bus);
            device.StartWorker();

            Check("连接成功", device.Connect());
            Check("连接后状态 Online", device.State == IoDeviceState.Online, device.State.ToString());

            WaitUntil(() => device.TryRead("DI_1", out _), 1000);
            Check("轮询缓存可读 DI_1", device.TryRead("DI_1", out var di) && !di.Boolean);

            device.SetInput("DI_1", true);
            WaitUntil(() => device.TryRead("DI_1", out var v) && v.Boolean, 1000);
            Check("DI 变化可读", device.TryRead("DI_1", out var di2) && di2.Boolean);

            Check("写输出成功", device.WriteAndWait("DO_1", IoPointValue.FromBool(true), 1000, CancellationToken.None, out var writeError), writeError);
            Check("写后输出为 true", device.TryRead("DO_1", out var doValue) && doValue.Boolean);

            var now = device.ReadNow("AO_1", 1000, CancellationToken.None, out var readError);
            Check("ReadNow 成功", readError == null, readError);
            Check("ReadNow 应用缩放", Math.Abs(now.Numeric - 1.0) < 0.001, now.Numeric.ToString("F3"));

            device.SlowWriteMs = 300;
            bool timedOut = device.WriteAndWait("DO_1", IoPointValue.FromBool(false), 30,
                CancellationToken.None, out var timeoutError);
            Check("写超时返回 false 且错误可读",
                !timedOut && timeoutError != null && timeoutError.Contains("超时"), timeoutError);
            device.SlowWriteMs = 0;

            device.Disconnect();
            Check("断开后状态 Closed", device.State == IoDeviceState.Closed, device.State.ToString());
        }

        private static void TestVariableCatalog()
        {
            BeginSection("V");
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionId = Guid.NewGuid() };
            var bus = new IoBusDescriptor { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp };
            var pointIn = new IoPointDescriptor
            {
                PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1"
            };
            var pointOut = new IoPointDescriptor
            {
                PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "2"
            };
            var deviceDescriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = bus.BusId, DriverId = "test",
                Points = new List<IoPointDescriptor> { pointIn, pointOut }
            };
            solution.IoBuses = new List<IoBusDescriptor> { bus };
            solution.IoDevices = new List<IoDeviceDescriptor> { deviceDescriptor };
            workspace.SwitchSolution(solution);

            var fakeDevice = new FakeDevice(deviceDescriptor, bus);
            var provider = new FakeProvider(new[] { fakeDevice });
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            catalog.Rebuild();

            var input = workspace.SystemVariables.OfType<IoInputVariableModel>()
                .FirstOrDefault(v => v.Name == "System.Io.IO_1.DI_1");
            var output = workspace.SystemVariables.OfType<IoOutputVariableModel>()
                .FirstOrDefault(v => v.Name == "System.Io.IO_1.DO_1");

            Check("IO 输入变量已投影", input != null);
            Check("IO 输出变量已投影", output != null);
            Check("输入变量只读", !((object?)input is IWritableVariable));
            Check("输出变量可写", output is IWritableVariable);
            Check("变量类型为 Io", input?.VariableType == VariableType.Io);
        }

        private static void TestDeterministicId()
        {
            BeginSection("I");
            var solutionId = Guid.NewGuid();
            var a = IoVariableCatalog.ComputeVariableId(solutionId, "IO_1", "DI_1");
            var b = IoVariableCatalog.ComputeVariableId(solutionId, "IO_1", "DI_1");
            var c = IoVariableCatalog.ComputeVariableId(solutionId, "IO_1", "DI_2");
            Check("确定性 Id 同输入同输出", a == b);
            Check("确定性 Id 不同点不同", a != c);
        }

        private static void TestInputVariableReadOnly()
        {
            BeginSection("R");
            var descriptor = new IoPointDescriptor
            {
                PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1"
            };
            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test", Points = new List<IoPointDescriptor> { descriptor }
            }, new IoBusDescriptor { BusId = "BUS_1" });
            var provider = new FakeProvider(new[] { device });
            var model = new IoInputVariableModel(
                Guid.NewGuid(), "System.Io.IO_1.DI_1", typeof(bool),
                provider, "IO_1", "DI_1", descriptor, "test");

            bool threw = false;
            try
            {
                ((Core.Interfaces.IPort)model).Value = true;
            }
            catch (NotSupportedException)
            {
                threw = true;
            }
            Check("输入变量 setter 抛 NotSupportedException", threw);
        }

        private static void TestOutputVariableWrite()
        {
            BeginSection("W");
            var descriptor = new IoPointDescriptor
            {
                PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "2"
            };
            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test", Points = new List<IoPointDescriptor> { descriptor }
            }, new IoBusDescriptor { BusId = "BUS_1" });
            var provider = new FakeProvider(new[] { device });
            var model = new IoOutputVariableModel(
                Guid.NewGuid(), "System.Io.IO_1.DO_1", typeof(bool),
                provider, "IO_1", "DO_1", descriptor, "test");

            bool ok = model.TryWrite(true, out var error);
            Check("输出变量写入成功", ok, error);
            Check("输出变量写后镜像为 true", model.Value is bool b && b);
            Check("设备收到写入", device.LastWrite?.PointId == "DO_1" && device.LastWrite.Value.Value.Boolean);
        }

        private static void TestModbusTcpIntegration()
        {
            BeginSection("M");
            int port = GetFreeTcpPort();
            var server = new ModbusTcpServer();
            try
            {
                server.ServerStart(port);
                Thread.Sleep(200);

                var bus = new IoBusDescriptor
                {
                    BusId = "BUS_1",
                    TransportKind = IoTransportKind.Tcp,
                    Tcp = new TcpTransportConfig { Host = "127.0.0.1", Port = port, ReadTimeoutMs = 1000 }
                };
                var descriptor = new IoDeviceDescriptor
                {
                    DeviceId = "IO_1",
                    BusId = "BUS_1",
                    DriverId = "modbus.tcp",
                    DriverConfigJson = "{\"UnitId\":1,\"ByteOrder\":\"CDAB\"}",
                    Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "00001" },
                        new() { PointId = "AO_1", Kind = IoPointKind.AnalogOutput, DataType = IoDataType.Int16, Address = "40001", Scale = 1 }
                    },
                    Params = new IoParams
                    {
                        PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 50 } }
                    }
                };

                using var device = new ModbusTcpIoDevice(descriptor, bus, new IoRuntimeContext(new NullLog()));
                device.StartWorker();
                Check("Modbus TCP 连接", device.Connect(), device.StateDetail);

                bool wroteCoil = device.WriteAndWait("DO_1", IoPointValue.FromBool(true), 1000,
                    CancellationToken.None, out var coilError);
                Check("Modbus TCP 写线圈", wroteCoil, coilError);

                var coil = device.ReadNow("DO_1", 1000, CancellationToken.None, out var coilReadError);
                Check("Modbus TCP 回读线圈", coilReadError == null && coil.Boolean, coilReadError);

                bool wroteRegister = device.WriteAndWait("AO_1",
                    IoPointValue.FromRaw(IoDataType.Int16, 123, 123), 1000,
                    CancellationToken.None, out var registerError);
                Check("Modbus TCP 写寄存器", wroteRegister, registerError);

                var analog = device.ReadNow("AO_1", 1000, CancellationToken.None, out var analogError);
                Check("Modbus TCP 回读寄存器", analogError == null && Math.Abs(analog.Numeric - 123) < 0.001,
                    analogError ?? analog.Numeric.ToString("F3"));

                device.Disconnect();

                // ---- 地址基数开关：同一从站、同一地址串 "40001"，1 基与 0 基必须落到不同寄存器 ----
                // 若 UI 上加了开关但驱动没接线，两个设备会读到同一个值——这条就会红。
                IoDeviceDescriptor MakeBaseDevice(string id, bool startWithZero) => new()
                {
                    DeviceId = id,
                    BusId = "BUS_1",
                    DriverId = "modbus.tcp",
                    DriverConfigJson =
                        $"{{\"UnitId\":1,\"ByteOrder\":\"CDAB\",\"AddressStartWithZero\":{(startWithZero ? "true" : "false")}}}",
                    Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "AI", Kind = IoPointKind.AnalogOutput, DataType = IoDataType.Int16, Address = "40001" }
                    },
                    Params = new IoParams
                    {
                        ConnectTimeoutMs = 1000,
                        PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 100 } }
                    }
                };

                using (var oneBased = new ModbusTcpIoDevice(MakeBaseDevice("IO_1BASED", false), bus,
                           new IoRuntimeContext(new NullLog())))
                using (var zeroBased = new ModbusTcpIoDevice(MakeBaseDevice("IO_0BASED", true), bus,
                           new IoRuntimeContext(new NullLog())))
                {
                    oneBased.StartWorker();
                    zeroBased.StartWorker();

                    Check("地址基数：1 基设备连接成功", oneBased.Connect(), oneBased.StateDetail);
                    Check("地址基数：0 基设备连接成功", zeroBased.Connect(), zeroBased.StateDetail);

                    bool baseWrote = oneBased.WriteAndWait("AI",
                        IoPointValue.FromRaw(IoDataType.Int16, 111, 111), 1000,
                        CancellationToken.None, out var baseWriteError);
                    Check("地址基数：1 基设备写 40001", baseWrote, baseWriteError);

                    var oneValue = oneBased.ReadNow("AI", 1000, CancellationToken.None, out var oneError);
                    Check("地址基数：1 基设备回读 40001 = 111",
                        oneError == null && Math.Abs(oneValue.Numeric - 111) < 0.001,
                        oneError ?? oneValue.Numeric.ToString("F0"));

                    // 本方法前面用默认（0 基）设备往 "40001" 写过 123：
                    // 0 基设备读同一地址串会读到那一个寄存器，1 基设备读的是相邻的那个（111）。
                    var zeroValue = zeroBased.ReadNow("AI", 1000, CancellationToken.None, out var zeroError);
                    Check("地址基数：0 基设备读同一地址串落到相邻寄存器（123，而不是 111）",
                        zeroError == null && Math.Abs(zeroValue.Numeric - 123) < 0.001,
                        zeroError ?? zeroValue.Numeric.ToString("F0"));

                    oneBased.Disconnect();
                    zeroBased.Disconnect();
                }
            }
            finally
            {
                // 刻意不调 ServerClose：HSL 的异步 accept 回调在关闭瞬间会抛
                // 「重新异步接受传入的连接尝试」到线程池（进程级未处理异常）。
                // 这是测试夹具的关闭竞态，不是被测代码的问题；进程退出时端口随之释放。
                _ = server;
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// S7 真实帧联调：进程内 <see cref="SiemensS7Server"/> 当从站，走完整 HSL S7 协议栈
        /// （建连 → 读 I/M/DB 区 → 写 Q/DB 区 → 后台批量轮询），验证驱动不是"只编译得过"。
        ///
        /// 为什么用进程内从站而不是 <c>tools\DeviceSimulator</c>：后者是控制台进程，
        /// 断言要么依赖"用户先手工启动"，要么起子进程再收输出——失败时无法区分
        /// "环境没起来"和"驱动有问题"（R38：跳过型断言必须能被反证）。
        /// </summary>
        private static void TestSiemensS7Integration()
        {
            BeginSection("S7");
            int port = GetFreeTcpPort();
            var server = new SiemensS7Server();

            try
            {
                // 预置从站数据。地址语义：I/Q/M 是「字节.位」，DB 是「块.字节.位」；
                // 字/双字读写的地址是字节偏移（DB1.0 / DB1.2）。
                server.Write("I0.0", true);          // 数字输入为真
                server.Write("DB1.0", (short)1234);  // 模拟输入原始值
                server.Write("DB1.2", (short)55);    // 用于验证缩放
                server.ServerStart(port);
                Thread.Sleep(200);

                var bus = new IoBusDescriptor
                {
                    BusId = "S7_BUS",
                    TransportKind = IoTransportKind.Tcp,
                    Tcp = new TcpTransportConfig { Host = "127.0.0.1", Port = port, ReadTimeoutMs = 1000 }
                };
                var descriptor = new IoDeviceDescriptor
                {
                    DeviceId = "S7_1",
                    BusId = "S7_BUS",
                    DriverId = "siemens.s7",
                    DriverConfigJson = "{\"PlcSeries\":\"S1200\",\"Rack\":0,\"Slot\":1}",
                    Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "DI_ON",     Kind = IoPointKind.DigitalInput,  DataType = IoDataType.Bool,  Address = "I0.0" },
                        new() { PointId = "DI_OFF",    Kind = IoPointKind.DigitalInput,  DataType = IoDataType.Bool,  Address = "M0.0" },
                        new() { PointId = "AI_RAW",    Kind = IoPointKind.AnalogInput,   DataType = IoDataType.Int16, Address = "DB1.0" },
                        new() { PointId = "AI_SCALED", Kind = IoPointKind.AnalogInput,   DataType = IoDataType.Int16, Address = "DB1.2", Scale = 0.1 },
                        new() { PointId = "DO_1",      Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool,  Address = "Q0.0" },
                        new() { PointId = "AO_1",      Kind = IoPointKind.AnalogOutput,  DataType = IoDataType.Int16, Address = "DB1.10" }
                    },
                    Params = new IoParams
                    {
                        ConnectTimeoutMs = 1000,
                        PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 50 } }
                    }
                };

                using var device = new SiemensS7IoDevice(descriptor, bus, new IoRuntimeContext(new NullLog()));

                // 轮询路径的观测点：后台线程把输入点读进来后会发 PointsChanged，
                // 收集被发布的点位 id（输出点按约定不轮询，不应出现）。
                var published = new HashSet<string>(StringComparer.Ordinal);
                device.PointsChanged += (_, args) =>
                {
                    lock (published)
                        foreach (var change in args.Changes) published.Add(change.PointId);
                };

                device.StartWorker();

                Check("连接成功（总线端口来自配置，不是 HSL 写死的 102）", device.Connect(), device.StateDetail);

                // 轮询覆盖：放在任何 ReadNow 之前，这样集合里的点位只可能来自后台轮询。
                string[] expected = { "DI_ON", "DI_OFF", "AI_RAW", "AI_SCALED" };
                int covered = 0;
                for (int i = 0; i < 60 && covered < expected.Length; i++)
                {
                    Thread.Sleep(50);
                    lock (published) covered = expected.Count(published.Contains);
                }
                Check("后台轮询覆盖全部输入点（4 个）", covered == expected.Length, $"实际 {covered} 个");

                device.TryRead("AI_SCALED", out var snapshot);
                Check("轮询快照里能取到缩放后的值（5.5）",
                    Math.Abs(snapshot.Numeric - 5.5) < 0.001, snapshot.Numeric.ToString("F3"));

                var on = device.ReadNow("DI_ON", 1000, CancellationToken.None, out var e1);
                Check("读 I 区：从站预置 I0.0 = true", e1 == null && on.Boolean,
                    e1 ?? on.Boolean.ToString());

                var off = device.ReadNow("DI_OFF", 1000, CancellationToken.None, out var e2);
                Check("读 M 区：未预置的 M0.0 = false", e2 == null && !off.Boolean,
                    e2 ?? off.Boolean.ToString());

                var raw = device.ReadNow("AI_RAW", 1000, CancellationToken.None, out var e3);
                Check("读 DB 区 16 位整数（1234）", e3 == null && Math.Abs(raw.Numeric - 1234) < 0.001,
                    e3 ?? raw.Numeric.ToString("F3"));

                var scaled = device.ReadNow("AI_SCALED", 1000, CancellationToken.None, out var e4);
                Check("真实帧上应用缩放（55 × 0.1 = 5.5）",
                    e4 == null && Math.Abs(scaled.Numeric - 5.5) < 0.001,
                    e4 ?? scaled.Numeric.ToString("F3"));

                bool wroteCoil = device.WriteAndWait("DO_1", IoPointValue.FromBool(true),
                    1000, CancellationToken.None, out var e5);
                Check("写 Q 区线圈", wroteCoil, e5);
                var serverCoil = server.ReadBool("Q0.0");
                Check("从站侧确实收到线圈写入（真帧，不是本地镜像）",
                    serverCoil.IsSuccess && serverCoil.Content, DescribeOp(serverCoil));

                bool wroteReg = device.WriteAndWait("AO_1",
                    IoPointValue.FromRaw(IoDataType.Int16, 4321, 4321), 1000, CancellationToken.None, out var e6);
                Check("写 DB 区寄存器", wroteReg, e6);
                var serverReg = server.ReadInt16("DB1.10");
                Check("从站侧确实收到寄存器写入（4321）",
                    serverReg.IsSuccess && serverReg.Content == 4321, DescribeOp(serverReg));

                var back = device.ReadNow("AO_1", 1000, CancellationToken.None, out var e7);
                Check("写入值可回读", e7 == null && Math.Abs(back.Numeric - 4321) < 0.001,
                    e7 ?? back.Numeric.ToString("F3"));

                // "输出点不轮询"的判据要避开写入回显：写入本身也会发 PointsChanged。
                // 所以先清空集合、再改一个输入值逼出一次新的轮询发布，
                // 然后看新集合里有没有输出点。前半句是正对照——它证明"集合会增长"，
                // 否则"没看到输出点"可能只是根本没事件（假通过）。
                lock (published) published.Clear();
                server.Write("M0.0", true);
                int republished = 0;
                for (int i = 0; i < 40 && republished == 0; i++)
                {
                    Thread.Sleep(50);
                    lock (published) republished = published.Count;
                }
                Check("输入变化能触发新的轮询发布（判据正对照）", republished > 0, $"新发布 {republished} 个点");

                bool sawOutput = false;
                lock (published) sawOutput = published.Contains("DO_1") || published.Contains("AO_1");
                Check("轮询不读输出点（只有 ReadNow/回读才读输出）", !sawOutput,
                    sawOutput ? "轮询里出现了输出点" : "符合约定");

                Check("实测轮询周期 > 0（健康面板『实测周期』的真实来源）",
                    device.AveragePollMs > 0, $"{device.AveragePollMs:F2} ms");

                device.Disconnect();
                Check("断开后状态不再是在线", device.State != IoDeviceState.Online, device.State.ToString());

            // ---- 边界：连不上 / 配置损坏 / 未知 PLC 系列 ----
            // 这三条替代"真机 PLC"能覆盖的部分：失败路径与配置兜底不需要硬件。
            var deadBus = new IoBusDescriptor
            {
                BusId = "S7_DEAD",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "127.0.0.1", Port = GetFreeTcpPort(), ReadTimeoutMs = 300 }
            };
            using (var dead = new SiemensS7IoDevice(MakeS7Device("S7_DEAD_1", deadBus.BusId, descriptor.DriverConfigJson),
                       deadBus, new IoRuntimeContext(new NullLog())))
            {
                dead.StartWorker();

                bool threw = false;
                bool connected = true;
                try { connected = dead.Connect(); }
                catch { threw = true; }

                Check("S7 端口无人监听：Connect 不抛异常", !threw, threw ? "抛了异常" : "未抛");
                Check("S7 端口无人监听：Connect 返回 false", !connected, dead.StateDetail);

                // Connect() 等到 ConnectTimeoutMs 就返回，而底层建连可能刚好在同一时刻失败：
                // 直接读 FaultCount 有竞态，这里给它 2 秒落账。
                for (int i = 0; i < 40 && dead.FaultCount == 0; i++)
                    Thread.Sleep(50);
                Check("S7 端口无人监听：计入故障计数（不是静默失败）",
                    dead.FaultCount > 0, $"FaultCount={dead.FaultCount}");
                Check("S7 端口无人监听：状态不是 Online",
                    dead.State != IoDeviceState.Online, dead.State.ToString());

                dead.Disconnect();
            }

            // 配置损坏：JObject.Parse 抛异常 → 回落默认值（S1200/0/1），仍应连上
            using (var badJson = new SiemensS7IoDevice(
                       MakeS7Device("S7_BADJSON", bus.BusId, "{这不是合法 JSON"),
                       bus, new IoRuntimeContext(new NullLog())))
            {
                badJson.StartWorker();
                Check("S7 驱动配置损坏：回落默认值后仍能连接", badJson.Connect(), badJson.StateDetail);
                var value = badJson.ReadNow("DI_ON", 1000, CancellationToken.None, out var badJsonError);
                Check("S7 驱动配置损坏：读写照常（默认值可用）",
                    badJsonError == null && value.Boolean, badJsonError ?? value.Boolean.ToString());
                badJson.Disconnect();
            }

            // 未知 PLC 系列：Enum.TryParse 失败 → 保留默认 S1200，不崩
            using (var unknownSeries = new SiemensS7IoDevice(
                       MakeS7Device("S7_UNKNOWN", bus.BusId,
                           "{\"PlcSeries\":\"S9999\",\"Rack\":0,\"Slot\":1}"),
                       bus, new IoRuntimeContext(new NullLog())))
            {
                unknownSeries.StartWorker();
                Check("S7 未知 PLC 系列：回落默认系列后仍能连接",
                    unknownSeries.Connect(), unknownSeries.StateDetail);
                unknownSeries.Disconnect();
            }
        }
        finally
            {
                // 与 ModbusTcpServer 同样的关闭竞态（见 TestModbusTcpIntegration 的说明）：
                // 不调 ServerClose，进程退出时端口自然释放。
                _ = server;
            }
        }

        /// <summary>HSL 结果转可读文本（失败时带错误码，便于判读是链路还是协议错）。</summary>
        private static string DescribeOp(OperateResult result)
            => result.IsSuccess ? "success" : $"{result.ErrorCode}: {result.Message}";

        /// <summary>造一个 S7 设备描述符（边界用例复用：只换 DeviceId / BusId / 驱动 JSON）。</summary>
        private static IoDeviceDescriptor MakeS7Device(string deviceId, string busId, string driverConfigJson) => new()
        {
            DeviceId = deviceId,
            BusId = busId,
            DriverId = "siemens.s7",
            DriverConfigJson = driverConfigJson,
            Points = new List<IoPointDescriptor>
            {
                new() { PointId = "DI_ON", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "I0.0" }
            },
            Params = new IoParams
            {
                ConnectTimeoutMs = 500,
                PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 100 } }
            }
        };

        /// <summary>
        /// Modbus RTU：编解码（无条件）、链路负向路径（无条件）、真实帧（需一对可互通串口）。
        ///
        /// <para>为什么要拆三段：RTU 的"真实帧"必须有一对能互通的串口，本机没有
        /// （无 com0com，Win32_SerialPort 只有蓝牙虚拟口且要已配对的对端设备）。
        /// 但"帧编得对不对""端口不存在时会不会挂死"这两件事与环境无关，
        /// 所以前者用纯函数测、后者用真串口打开失败来测，只有最后一段需要环境。</para>
        /// </summary>
        private static void TestModbusRtu()
        {
            BeginSection("RT");

            // ---------- 一、从站编解码（纯函数） ----------
            var slave = new RtuSlave(7);

            // 外部锚点：01 03 00 00 00 0A 的 CRC 是 C5 CD（Modbus 规范的经典示例帧），
            // 用它证明 CRC 实现不是"自洽但错"。
            var anchor = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
            ushort anchorCrc = RtuSlave.Crc16(anchor, 0, anchor.Length);
            Check("CRC16 对规范示例帧算出 0xCDC5",
                anchorCrc == 0xCDC5, $"0x{anchorCrc:X4}");

            slave.HoldingRegisters[0] = 0x1234;
            slave.HoldingRegisters[1] = 0x5678;
            var readRegs = slave.Handle(RtuSlave.Request(7, 0x03, 0x00, 0x00, 0x00, 0x02));
            Check("FC03 读保持寄存器：字节数与内容正确",
                readRegs != null && readRegs.Length == 9 &&
                readRegs[2] == 4 && readRegs[3] == 0x12 && readRegs[4] == 0x34 &&
                readRegs[5] == 0x56 && readRegs[6] == 0x78,
                readRegs == null ? "(无应答)" : BitConverter.ToString(readRegs));
            Check("FC03 响应帧 CRC 自洽", readRegs != null && RtuSlave.IsCrcValid(readRegs),
                readRegs == null ? "(无应答)" : "见上帧");

            slave.Coils[0] = true;
            slave.Coils[2] = true;
            var readCoils = slave.Handle(RtuSlave.Request(7, 0x01, 0x00, 0x00, 0x00, 0x03));
            Check("FC01 读线圈：位按低位先打包（0/2 位为真 → 0x05）",
                readCoils != null && readCoils.Length == 6 && readCoils[2] == 1 && readCoils[3] == 0x05,
                readCoils == null ? "(无应答)" : BitConverter.ToString(readCoils));

            var writeCoil = slave.Handle(RtuSlave.Request(7, 0x05, 0x00, 0x01, 0xFF, 0x00));
            Check("FC05 写单线圈：从站状态改变且原样回显",
                slave.Coils[1] && writeCoil != null && writeCoil.Length == 8 && writeCoil[4] == 0xFF,
                writeCoil == null ? "(无应答)" : BitConverter.ToString(writeCoil));

            var writeReg = slave.Handle(RtuSlave.Request(7, 0x06, 0x00, 0x05, 0x00, 0xFF));
            Check("FC06 写单寄存器：值落地且原样回显",
                slave.HoldingRegisters[5] == 0x00FF && writeReg != null && writeReg.Length == 8,
                $"HR5={slave.HoldingRegisters[5]:X4}");

            var writeRegs = slave.Handle(RtuSlave.Request(7, 0x10,
                0x00, 0x0A, 0x00, 0x02, 0x04, 0x00, 0x01, 0x00, 0x02));
            Check("FC10 写多寄存器：两个值都落地，响应回显起始地址与数量",
                slave.HoldingRegisters[10] == 1 && slave.HoldingRegisters[11] == 2 &&
                writeRegs != null && writeRegs.Length == 8 &&
                writeRegs[2] == 0x00 && writeRegs[3] == 0x0A &&      // 起始地址 10
                writeRegs[4] == 0x00 && writeRegs[5] == 0x02,        // 数量 2
                writeRegs == null ? "(无应答)" : BitConverter.ToString(writeRegs));

            var writeCoils = slave.Handle(RtuSlave.Request(7, 0x0F,
                0x00, 0x10, 0x00, 0x0A, 0x02, 0b0000_0101, 0b0000_0001));
            Check("FC0F 写多线圈：位展开正确",
                slave.Coils[16] && !slave.Coils[17] && slave.Coils[18] && slave.Coils[24],
                $"16={slave.Coils[16]} 17={slave.Coils[17]} 18={slave.Coils[18]} 24={slave.Coils[24]}");

            int crcBefore = slave.CrcErrorCount;
            var badCrc = (byte[])RtuSlave.Request(7, 0x03, 0x00, 0x00, 0x00, 0x01).Clone();
            badCrc[^1] ^= 0xFF;
            Check("CRC 错误：不应答（主站会超时，而不是拿到脏数据）",
                slave.Handle(badCrc) == null && slave.CrcErrorCount == crcBefore + 1,
                $"CRC 错误计数 {slave.CrcErrorCount}");

            Check("站号不匹配：不应答",
                slave.Handle(RtuSlave.Request(8, 0x03, 0x00, 0x00, 0x00, 0x01)) == null, "无应答");

            var illegalFunction = slave.Handle(RtuSlave.Request(7, 0x2B, 0x00, 0x00));
            Check("不支持的功能码：回异常响应（功能码 | 0x80，异常码 01）",
                illegalFunction != null && illegalFunction[1] == 0xAB && illegalFunction[2] == 0x01,
                illegalFunction == null ? "(无应答)" : BitConverter.ToString(illegalFunction));

            // 65528 + 16 = 65544 > 65536，真正越界（注意别用 0xFFF0+16：那正好落在最后 16 个寄存器，
            // 是合法地址——第一版就写错过，被断言自己抓出来）。
            var illegalAddress = slave.Handle(RtuSlave.Request(7, 0x03, 0xFF, 0xF8, 0x00, 0x10));
            Check("地址越界：回异常响应（异常码 02）",
                illegalAddress != null && illegalAddress[2] == 0x02,
                illegalAddress == null ? "(无应答)" : BitConverter.ToString(illegalAddress));

            var illegalQuantity = slave.Handle(RtuSlave.Request(7, 0x03, 0x00, 0x00, 0x00, 0x00));
            Check("数量为 0：回异常响应（异常码 03）",
                illegalQuantity != null && illegalQuantity[2] == 0x03,
                illegalQuantity == null ? "(无应答)" : BitConverter.ToString(illegalQuantity));

            // ---------- 二、链路负向路径（不需要真实从站） ----------
            string missingPort = FindUnusedSerialPort();
            var rtuBus = new IoBusDescriptor
            {
                BusId = "RTU_BUS",
                TransportKind = IoTransportKind.Serial,
                Serial = new SerialTransportConfig
                {
                    PortName = missingPort,
                    BaudRate = 9600,
                    DataBits = 8,
                    Parity = IoParity.None,
                    StopBits = IoStopBits.One,
                    ReadTimeoutMs = 300
                }
            };
            var rtuDevice = new IoDeviceDescriptor
            {
                DeviceId = "RTU_1",
                BusId = "RTU_BUS",
                DriverId = "modbus.rtu",
                DriverConfigJson = "{\"UnitId\":1,\"ByteOrder\":\"CDAB\"}",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "00001" },
                    new() { PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "00002" },
                    new() { PointId = "AI_1", Kind = IoPointKind.AnalogInput, DataType = IoDataType.Int16, Address = "40001" }
                },
                Params = new IoParams
                {
                    ConnectTimeoutMs = 500,
                    PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 100 } }
                }
            };

            using (var device = new ModbusRtuIoDevice(rtuDevice, rtuBus, new IoRuntimeContext(new NullLog())))
            {
                device.StartWorker();

                bool connectThrew = false;
                bool connected = true;
                try { connected = device.Connect(); }
                catch { connectThrew = true; }

                Check("端口不存在：Connect 不抛异常", !connectThrew, connectThrew ? "抛了异常" : "未抛");
                Check("端口不存在：Connect 返回 false", !connected, device.StateDetail);
                Check("端口不存在：状态说明可读（不是空白/不是类型名）",
                    !string.IsNullOrWhiteSpace(device.StateDetail) && !device.StateDetail.Contains("Exception"),
                    device.StateDetail);
                Check("端口不存在：状态不是 Online",
                    device.State != IoDeviceState.Online, device.State.ToString());

                var stopwatch = Stopwatch.StartNew();
                bool wrote = device.WriteAndWait("DO_1", IoPointValue.FromBool(true), 500,
                    CancellationToken.None, out var writeError);
                stopwatch.Stop();
                Check("未连接时写入：明确失败且带原因", !wrote && writeError != null, writeError);
                Check("未连接时写入：在超时内返回（不挂死）", stopwatch.ElapsedMilliseconds < 2000,
                    $"{stopwatch.ElapsedMilliseconds}ms");

                var value = device.ReadNow("AI_1", 300, CancellationToken.None, out var readError);
                Check("未连接时即时读：明确失败且带原因",
                    value.Quality != IoQuality.Good && readError != null, readError);

                device.Disconnect();
            }

            // ---------- 三、多从站挂在同一条串口总线（配置层） ----------
            IoDeviceDescriptor MakeRtuDevice(string id, byte unitId) => new()
            {
                DeviceId = id,
                BusId = "RTU_BUS",
                DriverId = "modbus.rtu",
                DriverConfigJson = $"{{\"UnitId\":{unitId},\"ByteOrder\":\"CDAB\"}}",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "00001" }
                },
                Params = new IoParams { PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 100 } } }
            };

            var provider = new IoProvider(new IoDriverRegistry(), new NullLog());
            try
            {
                provider.Apply(new[] { rtuBus },
                    new[] { MakeRtuDevice("RTU_S1", 1), MakeRtuDevice("RTU_S2", 2) },
                    autoStart: false);

                Check("同一条总线可挂多个从站（两个设备都被创建）",
                    provider.Devices.Count == 2, $"实际 {provider.Devices.Count} 个");
                Check("多从站共享同一条总线",
                    provider.Devices.All(d => d.Bus.BusId == "RTU_BUS"),
                    string.Join(",", provider.Devices.Select(d => d.Bus.BusId)));
                var unitIds = provider.Devices
                    .Select(d => Newtonsoft.Json.Linq.JObject.Parse(d.Descriptor.DriverConfigJson)["UnitId"]!.ToObject<int>())
                    .OrderBy(x => x).ToArray();
                Check("每个从站的站号各自独立（1 / 2）",
                    unitIds.SequenceEqual(new[] { 1, 2 }), string.Join(",", unitIds));
            }
            finally
            {
                provider.ReleaseAll();
            }

            // ---------- 四、真实 RTU 帧（承载层换成 TCP，无需 com0com） ----------
            // 帧仍是标准 RTU 帧（站号 + 功能码 + CRC），只是走 socket。
            // 这证明"我们的从站编解码与真实 Modbus 客户端能互通"，而不是只跟自己的测试自洽。
            var interopSlave = new RtuSlave(7);
            interopSlave.HoldingRegisters[1] = 777;
            interopSlave.Coils[1] = true;

            int interopPort = GetFreeTcpPort();
            using (var server = new RtuOverTcpServer(interopSlave, interopPort))
            using (var client = new ModbusRtuOverTcp("127.0.0.1", interopPort, 7)
            {
                ConnectTimeOut = 1000,
                ReceiveTimeOut = 1000
                // 用 HSL 默认（地址从 0 起）：地址串就是协议地址本身，
                // 不是 PLC 的 4xxxx 编号——"40001" 会被当成第 40001 号寄存器。
                // 这里用 "1"/"2" 这种原始地址，避免把地址约定混进帧层验证。
            })
            {
                var connect = client.ConnectServer();
                Check("真实 RTU 帧：客户端连接成功", connect.IsSuccess, DescribeOp(connect));

                var readRegister = client.ReadInt16("1");
                Check("真实 RTU 帧：读保持寄存器（777）",
                    readRegister.IsSuccess && readRegister.Content == 777,
                    (readRegister.IsSuccess ? readRegister.Content.ToString() : DescribeOp(readRegister))
                    + "；帧：" + server.FrameLog);

                var readCoil = client.ReadBool("1");
                Check("真实 RTU 帧：读线圈（预置为 true）",
                    readCoil.IsSuccess && readCoil.Content,
                    readCoil.IsSuccess ? readCoil.Content.ToString() : DescribeOp(readCoil));

                var interopWriteCoil = client.Write("2", true);
                Check("真实 RTU 帧：写线圈穿透到从站",
                    interopWriteCoil.IsSuccess && interopSlave.Coils[2],
                    interopWriteCoil.IsSuccess ? "从站 Coils[2]=True" : DescribeOp(interopWriteCoil));

                var writeRegister = client.Write("2", (short)4321);
                Check("真实 RTU 帧：写寄存器穿透到从站",
                    writeRegister.IsSuccess && interopSlave.HoldingRegisters[2] == 4321,
                    writeRegister.IsSuccess
                        ? interopSlave.HoldingRegisters[2].ToString()
                        : DescribeOp(writeRegister));

                Check("真实 RTU 帧：多帧交互且 CRC 全对（证明真的解出了帧）",
                    server.Frames >= 4 && interopSlave.CrcErrorCount == 0,
                    $"收到 {server.Frames} 帧 / CRC 错误 {interopSlave.CrcErrorCount}；帧：{server.FrameLog}");
            }

            // ---------- 五、真实帧（需要一对可互通的串口） ----------
            string? pair = Environment.GetEnvironmentVariable("VM_IO_RTU_PAIR");
            if (string.IsNullOrWhiteSpace(pair) || !pair.Contains(':'))
            {
                Skip("真实帧：RTU 读写穿透到从站",
                    "未设置 VM_IO_RTU_PAIR（本机无 com0com，Win32_SerialPort 只有蓝牙虚拟口且需已配对的对端设备）。" +
                    "反证方式：装 com0com 后设 VM_IO_RTU_PAIR=COM20:COM21（格式：主站口:从站口）再跑本用例即可执行。");
            }
            else
            {
                var ports = pair.Split(':');
                var liveSlave = new RtuSlave(7);
                liveSlave.HoldingRegisters[0] = 777;
                liveSlave.Coils[0] = true;
                try
                {
                    liveSlave.Start(ports[1].Trim());

                    var liveBus = new IoBusDescriptor
                    {
                        BusId = "RTU_LIVE",
                        TransportKind = IoTransportKind.Serial,
                        Serial = new SerialTransportConfig
                        {
                            PortName = ports[0].Trim(),
                            BaudRate = 9600,
                            ReadTimeoutMs = 500
                        }
                    };
                    var liveDevice = MakeRtuDevice("RTU_LIVE_1", 7);
                    liveDevice.BusId = "RTU_LIVE";
                    liveDevice.Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "00001" },
                        new() { PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "00002" },
                        new() { PointId = "AI_1", Kind = IoPointKind.AnalogInput, DataType = IoDataType.Int16, Address = "40001" },
                        new() { PointId = "AO_1", Kind = IoPointKind.AnalogOutput, DataType = IoDataType.Int16, Address = "40002" }
                    };

                    using var live = new ModbusRtuIoDevice(liveDevice, liveBus, new IoRuntimeContext(new NullLog()));
                    live.StartWorker();
                    Check("真实串口：连接成功", live.Connect(), live.StateDetail);

                    var readAnalog = live.ReadNow("AI_1", 1000, CancellationToken.None, out var liveReadError);
                    Check("真实串口：读保持寄存器（777）",
                        liveReadError == null && Math.Abs(readAnalog.Numeric - 777) < 0.001,
                        liveReadError ?? readAnalog.Numeric.ToString("F3"));

                    bool liveWrote = live.WriteAndWait("DO_1", IoPointValue.FromBool(true),
                        1000, CancellationToken.None, out var liveWriteError);
                    Check("真实串口：写线圈穿透到从站",
                        liveWrote && liveSlave.Coils[1], liveWriteError);

                    live.Disconnect();
                }
                finally
                {
                    liveSlave.Dispose();
                }
            }
        }

        /// <summary>找一个本机不存在的串口名（避免"COM99 恰好存在"这种偶发假通过）。</summary>
        private static string FindUnusedSerialPort()
        {
            var used = new HashSet<string>(
                System.IO.Ports.SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= 250; i++)
            {
                string name = "COM" + i;
                if (!used.Contains(name)) return name;
            }
            return "COM250";
        }

        /// <summary>
        /// 点位表搜索（IO 配置弹窗）+ 写入审计页签的筛选。
        ///
        /// 两件事的共同点：都是"显示集合 ≠ 数据集合"。判据必须同时验
        /// ① 显示被过滤；② **保存/落库仍是全量**——只验 ① 会把"搜索把数据也删了"这种错当成通过。
        /// </summary>
        private static void TestPointSearchAndAudit()
        {
            // ---------- 一、点位搜索 ----------
            BeginSection("PF");

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionId = Guid.NewGuid() };
            workspace.SwitchSolution(solution);

            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLog());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            var vm = new IoConfigViewModel(workspace, provider, catalog, new NullNotifier(), registry);

            vm.OnDialogOpened(new DialogParameters());
            vm.AddBusCommand.Execute();
            vm.AddDeviceCommand.Execute();
            vm.SelectedBus!.BusId = "BUS_1";
            vm.SelectedDevice!.DeviceId = "IO_1";
            vm.SelectedDevice.BusId = "BUS_1";
            vm.SelectedDevice.DriverId = "simulated";

            vm.AddPointCommand.Execute();
            vm.SelectedPoint!.PointId = "DI_1";
            vm.SelectedPoint.Address = "00001";
            vm.SelectedPoint.Description = "启动按钮";

            vm.AddPointCommand.Execute();
            vm.SelectedPoint!.PointId = "DO_1";
            vm.SelectedPoint.Kind = IoPointKind.DigitalOutput;
            vm.SelectedPoint.Address = "00002";
            vm.SelectedPoint.Description = "气缸输出";

            vm.AddPointCommand.Execute();
            vm.SelectedPoint!.PointId = "AI_1";
            vm.SelectedPoint.Kind = IoPointKind.AnalogInput;
            vm.SelectedPoint.DataType = IoDataType.Int16;
            vm.SelectedPoint.Address = "40001";

            Check("无搜索时显示集合 == 全部点位",
                vm.FilteredPoints.Count == 3 && vm.Points.Count == 3,
                $"{vm.FilteredPoints.Count} / {vm.Points.Count}");

            vm.PointSearch = "DI_";
            Check("按点位标识过滤", vm.FilteredPoints.Count == 1 && vm.FilteredPoints[0].PointId == "DI_1",
                string.Join(",", vm.FilteredPoints.Select(p => p.PointId)));

            vm.PointSearch = "00002";
            Check("按地址过滤", vm.FilteredPoints.Count == 1 && vm.FilteredPoints[0].PointId == "DO_1",
                string.Join(",", vm.FilteredPoints.Select(p => p.PointId)));

            vm.PointSearch = "气缸";
            Check("按说明过滤（中文）", vm.FilteredPoints.Count == 1 && vm.FilteredPoints[0].PointId == "DO_1",
                string.Join(",", vm.FilteredPoints.Select(p => p.PointId)));

            vm.PointSearch = "不存在的点位";
            Check("无命中时显示 0 条", vm.FilteredPoints.Count == 0, vm.FilteredPoints.Count.ToString());
            Check("计数提示写成「命中 / 总数」", vm.PointsSummary == "0 / 3 个点位", vm.PointsSummary);

            // 选中被过滤掉 → 必须清空（否则属性网格在编辑一个列表里看不见的点）
            vm.PointSearch = string.Empty;
            vm.SelectedPoint = vm.Points.First(p => p.PointId == "DO_1");
            vm.PointSearch = "DI_";
            Check("选中的点位被搜索过滤掉时清空选中", vm.SelectedPoint == null,
                vm.SelectedPoint?.PointId ?? "(已清空)");

            // 搜索状态下新增：清搜索、选中新点（否则"建了但看不见"）
            vm.AddPointCommand.Execute();
            Check("搜索中新增点位会先清搜索（新建的点立即可见）",
                string.IsNullOrEmpty(vm.PointSearch) && vm.FilteredPoints.Count == 4,
                $"搜索='{vm.PointSearch}' 显示 {vm.FilteredPoints.Count} 条");
            Check("新增后选中新点位", vm.SelectedPoint != null && vm.SelectedPoint.PointId.StartsWith("P_"),
                vm.SelectedPoint?.PointId ?? "(无)");

            // 保存仍走全量：搜索不得影响写回
            vm.PointSearch = "DI_";
            vm.ApplyCommand.Execute();
            Check("搜索只影响显示，不影响写回方案（4 个点位全部落盘）",
                solution.IoDevices[0].Points.Count == 4,
                $"落盘 {solution.IoDevices[0].Points.Count} 个");

            vm.PointSearch = string.Empty;
            vm.SelectedPoint = vm.Points.First(p => p.PointId == "AI_1");
            vm.RemoveCommand.Execute();
            Check("删除后显示集合同步收缩",
                vm.FilteredPoints.Count == 3 && vm.FilteredPoints.All(p => p.PointId != "AI_1"),
                string.Join(",", vm.FilteredPoints.Select(p => p.PointId)));

            provider.ReleaseAll();

            // ---------- 二、写入审计筛选 ----------
            BeginSection("AU");

            var now = DateTime.Now;
            var entries = new List<VisionMaster.Core.AuditLogEntry>
            {
                new() { Timestamp = now.AddSeconds(-1), OperationType = VisionMaster.Core.AuditOperationType.DeviceControl,
                        OperationName = "IO 输出写入", UserName = "flow:主流程", Details = "点位=IO_1.DO_1；旧值=False；新值=True；结果=成功", Result = true },
                new() { Timestamp = now.AddSeconds(-2), OperationType = VisionMaster.Core.AuditOperationType.DeviceControl,
                        OperationName = "IO 输出写入失败", UserName = "manual", Details = "点位=IO_1.DO_2；旧值=False；新值=True；结果=失败（设备离线）", Result = false },
                new() { Timestamp = now.AddSeconds(-3), OperationType = VisionMaster.Core.AuditOperationType.ConfigModify,
                        OperationName = "报警改名", UserName = "李工", Details = "报警_1 → 冷却水温度", Result = true },
                new() { Timestamp = now.AddSeconds(-4), OperationType = VisionMaster.Core.AuditOperationType.DeviceControl,
                        OperationName = "运动回零", UserName = "manual", Details = "轴=X", Result = true }
            };

            var ioOnly = IoHealthViewModel.SelectIoWriteEntries(entries, null, failuresOnly: false);
            Check("只挑 IO 写入：排除报警改名与运动回零", ioOnly.Count == 2, $"实际 {ioOnly.Count} 条");
            Check("保持输入顺序（新→旧）",
                ioOnly.Count == 2 && ioOnly[0].Details.Contains("DO_1") && ioOnly[1].Details.Contains("DO_2"),
                ioOnly.Count == 2 ? ioOnly[0].Details : "(不足 2 条)");

            var failures = IoHealthViewModel.SelectIoWriteEntries(entries, null, failuresOnly: true);
            Check("只看失败：只留 Result=false 的那条",
                failures.Count == 1 && !failures[0].Result, $"实际 {failures.Count} 条");

            var byPoint = IoHealthViewModel.SelectIoWriteEntries(entries, "DO_2", failuresOnly: false);
            Check("按点位搜索（详情里含 DO_2）", byPoint.Count == 1, $"实际 {byPoint.Count} 条");

            var byUser = IoHealthViewModel.SelectIoWriteEntries(entries, "flow:", failuresOnly: false);
            Check("按操作者搜索（流程 owner）", byUser.Count == 1, $"实际 {byUser.Count} 条");

            var limited = IoHealthViewModel.SelectIoWriteEntries(entries, null, failuresOnly: false, max: 1);
            Check("条数上限生效", limited.Count == 1, $"实际 {limited.Count} 条");

            Check("空输入返回空集合（不抛）",
                IoHealthViewModel.SelectIoWriteEntries(null, null, false).Count == 0, "0 条");

            var row = new IoAuditRow(ioOnly[1]);
            Check("审计行从详情里抽出点位", row.PointText == "IO_1.DO_2", row.PointText);
            Check("审计行结果灯为失败档", row.ResultLevel == "error", row.ResultLevel);
            Check("审计行操作者取 UserName", row.Operator == "manual", row.Operator);

            var okRow = new IoAuditRow(ioOnly[0]);
            Check("成功条目结果灯为 ok 档", okRow.ResultLevel == "ok", okRow.ResultLevel);
        }

        private static void TestDriverRegistry()
        {
            BeginSection("G");
            var registry = new IoDriverRegistry();
            Check("内置驱动 modbus.tcp 已注册", registry.TryGet("modbus.tcp", out _));
            Check("内置驱动 modbus.rtu 已注册", registry.TryGet("modbus.rtu", out _));
            Check("内置驱动 siemens.s7 已注册", registry.TryGet("siemens.s7", out _));
            Check("内置驱动 simulated 已注册", registry.TryGet("simulated", out _));

            bool duplicateRejected = false;
            try { registry.Register(new SimulatedIoDriverFactory()); }
            catch (InvalidOperationException) { duplicateRejected = true; }
            Check("重复 driverId 被拒绝", duplicateRejected);
        }

        private static void TestSolutionRoundTrip()
        {
            BeginSection("S");
            var solution = new SolutionModel { SolutionId = Guid.NewGuid(), SolutionName = "IO 方案" };
            solution.IoBuses.Add(new IoBusDescriptor
            {
                BusId = "BUS_1",
                DisplayName = "总线 1",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "192.168.1.10", Port = 502 }
            });
            solution.IoDevices.Add(new IoDeviceDescriptor
            {
                DeviceId = "IO_1",
                BusId = "BUS_1",
                DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            });

            var json = JsonConvert.SerializeObject(solution, SolutionService.SerializationSettings);
            var roundTrip = JsonConvert.DeserializeObject<SolutionModel>(json, SolutionService.SerializationSettings);

            Check("方案往返保留 SolutionId", roundTrip?.SolutionId == solution.SolutionId);
            Check("方案往返保留 IO 总线", roundTrip?.IoBuses.Count == 1 && roundTrip.IoBuses[0].BusId == "BUS_1");
            Check("方案往返保留 IO 设备/点位",
                roundTrip?.IoDevices.Count == 1 &&
                roundTrip.IoDevices[0].Points.Count == 1 &&
                roundTrip.IoDevices[0].Points[0].PointId == "DI_1");
        }

        private static void TestIoConfigViewModel()
        {
            BeginSection("U");
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionId = Guid.NewGuid() };
            workspace.SwitchSolution(solution);

            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLog());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            var vm = new IoConfigViewModel(workspace, provider, catalog, new NullNotifier(), registry);

            vm.OnDialogOpened(new DialogParameters());
            vm.AddBusCommand.Execute();
            vm.AddDeviceCommand.Execute();
            vm.AddPointCommand.Execute();

            vm.SelectedBus!.BusId = "BUS_1";
            vm.SelectedDevice!.DeviceId = "IO_1";
            vm.SelectedDevice.DriverId = "simulated";
            vm.SelectedDevice.BusId = "BUS_1";
            vm.SelectedPoint!.PointId = "DI_1";
            vm.SelectedPoint.Kind = IoPointKind.DigitalInput;
            vm.SelectedPoint.DataType = IoDataType.Bool;
            vm.SelectedPoint.Address = "1";

            vm.ApplyCommand.Execute();

            Check("IO 配置应用到方案：总线 1 条", solution.IoBuses.Count == 1);
            Check("IO 配置应用到方案：设备 1 台", solution.IoDevices.Count == 1);
            Check("IO 配置应用到方案：点位 1 个", solution.IoDevices[0].Points.Count == 1);
            Check("IO 配置应用后创建设备", provider.Devices.Count == 1);
            Check("IO 配置应用后投影变量", workspace.SystemVariables.OfType<IoVariableModel>()
                .Any(v => v.Name == "System.Io.IO_1.DI_1"));

            provider.ReleaseAll();
        }

        private static void TestDescriptorValidation()
        {
            BeginSection("K");
            var bus = new IoBusDescriptor { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp };
            Check("TCP 总线配置有效", bus.Validate(out _));
            bus.Tcp.Host = "";
            Check("TCP 主机为空被拒绝", !bus.Validate(out var busError) && busError.Contains("主机"));

            var device = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "P1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" },
                    new() { PointId = "P1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "2" }
                }
            };
            Check("重复点位标识被拒绝", !device.Validate(out var dupError) && dupError.Contains("重复"));

            var badScale = new IoPointDescriptor
            {
                PointId = "AI", Kind = IoPointKind.AnalogInput, DataType = IoDataType.Int16, Address = "1", Scale = 0
            };
            Check("缩放为 0 被拒绝", !badScale.Validate(out var scaleError) && scaleError.Contains("缩放"));

            var badType = new IoPointDescriptor
            {
                PointId = "DI", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Int16, Address = "1"
            };
            Check("数字量类型不是 Bool 被拒绝", !badType.Validate(out var typeError) && typeError.Contains("Bool"));
        }

        private static void TestSystemVariableCoexistence()
        {
            BeginSection("C");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var io = new IoInputVariableModel(
                Guid.NewGuid(), "System.Io.IO_1.DI_1", typeof(bool),
                new FakeProvider(Array.Empty<IIoDevice>()), "IO_1", "DI_1",
                new IoPointDescriptor { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool },
                "io");
            var motion = new SystemVariableModel(Guid.NewGuid(), "System.Motion.X.Position", typeof(double), "motion");
            workspace.SystemVariables.Add(io);
            workspace.SystemVariables.Add(motion);

            var catalog = new SystemVariableCatalog(workspace);
            catalog.Rebuild(Array.Empty<Core.Interfaces.MotionAxisBinding>());

            Check("运动目录重建不删除 IO 变量", workspace.SystemVariables.Contains(io));
            Check("运动目录重建会移除旧运动变量", !workspace.SystemVariables.Contains(motion));
            Check("System. 前缀整体保留", SystemVariableCatalog.IsReservedName("System.Io.IO_1.DI_1"));
            Check("Motion. 旧前缀不再单独保留", !SystemVariableCatalog.IsReservedName("Motion.X.Position"));
        }

        private static void TestSystemVariableHost()
        {
            BeginSection("H");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var host = new SystemVariableHost(workspace.SystemVariables);
            var motionProvider = new FakeProvider(Array.Empty<IIoDevice>());
            var ioProvider = new FakeProvider(Array.Empty<IIoDevice>());

            var motion = new FakeVariableProvider("Motion", new[]
            {
                new SystemVariableModel(Guid.NewGuid(), "System.Motion.X.Position", typeof(double), "m")
            });
            var io = new IoVariableCatalog(workspace, ioProvider, new NullLog());
            io.AttachToHost(host);
            host.Register(motion);

            // IO 点位
            var solution = workspace.CurrentSolution;
            solution.IoBuses = new List<IoBusDescriptor>
            {
                new() { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp }
            };
            solution.IoDevices = new List<IoDeviceDescriptor>
            {
                new()
                {
                    DeviceId = "IO_1", BusId = "BUS_1", DriverId = "simulated",
                    Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                    }
                }
            };
            io.Rebuild();
            host.Refresh();

            Check("宿主合并运动 + IO 变量", workspace.SystemVariables.Count == 2,
                string.Join("、", workspace.SystemVariables.Select(v => v.Name)));
            Check("宿主保留运动变量",
                workspace.SystemVariables.Any(v => v.Name == "System.Motion.X.Position"));
            Check("宿主保留 IO 变量",
                workspace.SystemVariables.Any(v => v.Name == "System.Io.IO_1.DI_1"));

            // 运动提供方清空 → IO 变量必须还在
            motion.Reset(Array.Empty<IVariable>());
            host.Refresh();
            Check("运动提供方清空后 IO 变量仍在",
                workspace.SystemVariables.Count == 1 &&
                workspace.SystemVariables.Any(v => v.Name == "System.Io.IO_1.DI_1"),
                string.Join("、", workspace.SystemVariables.Select(v => v.Name)));

            // 顺序稳定：重复刷新不改变顺序
            var before = workspace.SystemVariables.Select(v => v.Name).ToList();
            host.Refresh();
            var after = workspace.SystemVariables.Select(v => v.Name).ToList();
            Check("重复刷新顺序稳定", before.SequenceEqual(after));

            TestRealMotionCatalogThroughHost(host);
        }

        /// <summary>
        /// 生产接线路径：真实的 SystemVariableCatalog 接到 Host 上之后，
        /// 运动变量必须仍然出现在集合里（AttachToHost 之后目录自己不写集合）。
        /// </summary>
        private static void TestRealMotionCatalogThroughHost(SystemVariableHost host)
        {
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });
            var localHost = new SystemVariableHost(workspace.SystemVariables);

            var card = new MotionDescriptor { Id = Guid.NewGuid(), DisplayName = "卡A" };
            var axisId = Guid.NewGuid();
            card.Axes.Add(new AxisMapping { AxisId = axisId, LogicalName = "X", PhysicalIndex = 0 });
            var registry = new MotionAxisRegistry(() => new[] { card });

            var catalog = new SystemVariableCatalog(workspace);
            catalog.AttachToHost(localHost);
            catalog.Rebuild(registry.Snapshot());

            Check("接到宿主的运动目录仍能把变量写进集合",
                workspace.SystemVariables.Count == 12,
                $"{workspace.SystemVariables.Count} 个");
            Check("接到宿主的运动变量名仍是 System.Motion.*",
                workspace.SystemVariables.Any(v => v.Name == "System.Motion.X.Position"));
        }

        private static void TestPointIsolation()
        {
            BeginSection("P");
            var bus = new IoBusDescriptor { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp };
            var descriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1",
                BusId = "BUS_1",
                DriverId = "test",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_OK", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" },
                    new() { PointId = "DI_BAD", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "2" }
                },
                Params = new IoParams
                {
                    PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 10 } }
                }
            };

            using var device = new PartialFailureDevice(descriptor, bus);
            device.StartWorker();
            device.Connect();

            WaitUntil(() => device.IsolatedPoints.Count > 0, 3000);
            // 隔离是在“这一轮结束后”生效的：再等一轮，确认坏点已不参与正常轮询。
            WaitUntil(() => device.LastPollPointCount <= 1, 2000);
            Check("坏点连续失败后被隔离",
                device.IsolatedPoints.Contains("DI_BAD", StringComparer.OrdinalIgnoreCase),
                string.Join("、", device.IsolatedPoints));
            Check("好点不受影响", device.TryRead("DI_OK", out var ok) && ok.Boolean);
            Check("坏点不再出现在正常轮询里", device.LastPollPointCount <= 1,
                device.LastPollPointCount.ToString());

            device.Disconnect();
        }

        private static void TestLeaseRegistry()
        {
            BeginSection("L");
            var registry = new IoOutputLeaseRegistry();

            Check("首个 owner 能拿到租约",
                registry.TryAcquire("DO_1", "flow-A", 30000, allowShared: false, out _));
            Check("第二个 owner 被拒绝",
                !registry.TryAcquire("DO_1", "flow-B", 30000, allowShared: false, out var conflict)
                && conflict != null && conflict.Contains("flow-A"), conflict);
            Check("当前 owner 可见", registry.GetOwner("DO_1") == "flow-A");

            registry.Release("DO_1", "flow-B");
            Check("非 owner 释放无效", registry.GetOwner("DO_1") == "flow-A");

            registry.Release("DO_1", "flow-A");
            Check("owner 释放后为空", registry.GetOwner("DO_1") == null);
            Check("释放后可被他人获取",
                registry.TryAcquire("DO_1", "flow-B", 30000, allowShared: false, out _));

            registry.ReleaseAll("flow-B");
            Check("ReleaseAll 清空该 owner 的全部租约", registry.GetOwner("DO_1") == null);

            Check("AllowSharedWrite 时不互斥",
                registry.TryAcquire("DO_2", "flow-A", 30000, allowShared: true, out _) &&
                registry.TryAcquire("DO_2", "flow-B", 30000, allowShared: true, out _));

            // 输出变量层：流程持有时手动写被拒绝
            var descriptor = new IoPointDescriptor
            {
                PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool,
                Address = "1", AllowManualWrite = true
            };
            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor> { descriptor }
            }, new IoBusDescriptor { BusId = "BUS_1" });
            var provider = new FakeProvider(new[] { device });
            var lease = new IoOutputLeaseRegistry();
            lease.TryAcquire("DO_1", "flow-A", 30000, allowShared: false, out _);

            var model = new IoOutputVariableModel(
                Guid.NewGuid(), "System.Io.IO_1.DO_1", typeof(bool),
                provider, "IO_1", "DO_1", descriptor, "test", lease);

            bool written = model.TryWrite(true, out var writeError);
            Check("流程持有点位时手动写被拒绝",
                !written && writeError != null && writeError.Contains("flow-A"), writeError);
            Check("被拒绝时设备未收到写入", device.LastWrite == null);

            lease.Release("DO_1", "flow-A");
            Check("租约释放后手动写成功", model.TryWrite(true, out var retryError), retryError);
        }

        private static void TestStatusVariables()
        {
            BeginSection("T");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            }, new IoBusDescriptor { BusId = "BUS_1" });

            var provider = new FakeProvider(new[] { device });
            var host = new SystemVariableHost(workspace.SystemVariables);
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            catalog.AttachToHost(host);

            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor> { device.Descriptor };
            catalog.Rebuild();
            host.Refresh();

            var names = workspace.SystemVariables.Select(v => v.Name).ToList();
            Check("站点状态变量：Online", names.Contains("System.Io.IO_1.Online"));
            Check("站点状态变量：State", names.Contains("System.Io.IO_1.State"));
            Check("站点状态变量：LastError", names.Contains("System.Io.IO_1.LastError"));
            Check("点位变量仍在", names.Contains("System.Io.IO_1.DI_1"));

            // 实例复用：再投影一次必须是同一批对象（否则订阅方失联）
            var onlineBefore = workspace.SystemVariables.First(v => v.Name == "System.Io.IO_1.Online");
            host.Refresh();
            var onlineAfter = workspace.SystemVariables.FirstOrDefault(v => v.Name == "System.Io.IO_1.Online");
            Check("状态变量实例复用", ReferenceEquals(onlineBefore, onlineAfter));

            // 设备从 provider 消失 → 状态变量一起摘掉
            var emptyProvider = new FakeProvider(Array.Empty<IIoDevice>());
            var catalog2 = new IoVariableCatalog(workspace, emptyProvider, new NullLog());
            Check("设备不在仓库时不投影状态变量",
                !catalog2.Snapshot().Any(v => v.Name.EndsWith(".Online", StringComparison.Ordinal)));
        }

        private static void TestWriteScope()
        {
            BeginSection("B");
            var descriptor = new IoPointDescriptor
            {
                PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "1"
            };
            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor> { descriptor }
            }, new IoBusDescriptor { BusId = "BUS_1" });
            var provider = new FakeProvider(new[] { device });
            var leases = new IoOutputLeaseRegistry();
            var model = new IoOutputVariableModel(
                Guid.NewGuid(), "System.IO.IO_1.DO_1", typeof(bool),
                provider, "IO_1", "DO_1", descriptor, "test", leases);

            Check("流程外 owner 为空", IoWriteScope.CurrentOwner == null);

            string? observedOwner = null;
            using (IoWriteScope.Begin("flow:Main", owner => leases.ReleaseAll(owner)))
            {
                observedOwner = IoWriteScope.CurrentOwner;
                Check("作用域内 owner 正确", observedOwner == "flow:Main", observedOwner);

                // 流程写：以 flow:Main 身份取得租约
                Check("流程内写入成功", model.TryWrite(true, out var flowError), flowError);
                Check("租约归流程 owner", leases.GetOwner("DO_1") == "flow:Main", leases.GetOwner("DO_1"));
            }

            Check("作用域结束后 owner 恢复", IoWriteScope.CurrentOwner == null);
            Check("作用域结束后租约已释放", leases.GetOwner("DO_1") == null);

            // 流程外隐式写：manual owner
            Check("流程外写入成功", model.TryWrite(false, out var manualError), manualError);

            // 嵌套作用域：内层恢复为外层 owner
            using (IoWriteScope.Begin("flow:A", null))
            {
                using (IoWriteScope.Begin("flow:B", null))
                    Check("嵌套作用域内层 owner", IoWriteScope.CurrentOwner == "flow:B");
                Check("嵌套作用域退出后恢复外层", IoWriteScope.CurrentOwner == "flow:A");
            }

            // 两个流程争同一输出：后到的被拒
            // 两个流程争同一输出：后到的被拒（A 的作用域要一直开到这里结束）
            var leases2 = new IoOutputLeaseRegistry();
            using (IoWriteScope.Begin("flow:A", owner => leases2.ReleaseAll(owner)))
            {
                Check("流程 A 取得租约", leases2.TryAcquire("DO_1", "flow:A", 30000, false, out _));

                using (IoWriteScope.Begin("flow:B", owner => leases2.ReleaseAll(owner)))
                {
                    var model2 = new IoOutputVariableModel(
                        Guid.NewGuid(), "System.IO.IO_1.DO_1", typeof(bool),
                        provider, "IO_1", "DO_1", descriptor, "test", leases2);
                    bool written = model2.TryWrite(true, out var conflict);
                    Check("流程 B 被流程 A 拒绝",
                        !written && conflict != null && conflict.Contains("flow:A"), conflict);
                }
            }
        }

        private static void TestVariableSubscription()
        {
            BeginSection("N");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            }, new IoBusDescriptor { BusId = "BUS_1" });

            var provider = new FakeProvider(new[] { device });
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor> { device.Descriptor };
            catalog.Rebuild();

            var variable = catalog.Snapshot().First(v => v.Name == "System.Io.IO_1.DI_1");
            Check("订阅测试找到 IO 变量", variable != null);
            if (variable == null) return;

            var published = new List<(IVariable Variable, object? Value)>();
            using var subscription = new VariableSubscription(20, batch => published.AddRange(batch));
            subscription.Subscribe(variable);

            // 通过设备事件驱动值变化（Publish 是 internal，测试走公开入口）
            var ioDevice = (IoVariableModel)variable;
            var model = ioDevice;
            var publishMethod = typeof(IoVariableModel).GetMethod("Publish",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check("反射拿到 Publish（用于模拟设备推送）", publishMethod != null);

            void Fire(bool value) => publishMethod!.Invoke(model,
                new object?[] { value, VisionMaster.VariableQuality.Good, DateTime.Now });

            Fire(true);
            Fire(false);
            Fire(true);

            Thread.Sleep(40);
            subscription.Pump();

            Check("发布周期内合并为一次回调", published.Count == 1, published.Count.ToString());
            Check("发布的是最新值", published.Count == 1 && Equals(published[0].Value, true));
            Check("中间更新计入丢弃", subscription.DroppedCount == 2, subscription.DroppedCount.ToString());

            // 无变化时不该重复发布
            Thread.Sleep(40);
            int before = published.Count;
            subscription.Pump();
            Check("无变化不重复发布", published.Count == before, published.Count.ToString());

            // 退订后不再收到
            subscription.Unsubscribe(variable);
            Fire(false);
            Thread.Sleep(40);
            int afterUnsubscribe = published.Count;
            subscription.Pump();
            Check("退订后不再发布", published.Count == afterUnsubscribe);

            Check("退订后计数归零", subscription.Count == 0);
        }

        private static void TestAuditRecording()
        {
            BeginSection("A");
            VisionMaster.Core.AuditLogger.ClearRecentForTests();

            var descriptor = new IoPointDescriptor
            {
                PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "1"
            };
            var device = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor> { descriptor }
            }, new IoBusDescriptor { BusId = "BUS_1" });
            var provider = new FakeProvider(new[] { device });
            var leases = new IoOutputLeaseRegistry();

            var model = new IoOutputVariableModel(
                Guid.NewGuid(), "System.Io.IO_1.DO_1", typeof(bool),
                provider, "IO_1", "DO_1", descriptor, "test", leases);

            model.TryWrite(true, out _);
            var entries = VisionMaster.Core.AuditLogger.RecentEntries;
            Check("成功写输出落一条审计", entries.Count == 1, entries.Count.ToString());
            Check("审计记到点位", entries.Count == 1 && entries[0].Details.Contains("IO_1.DO_1"),
                entries.Count == 1 ? entries[0].Details : "");
            Check("审计 owner 为 manual", entries.Count == 1 && entries[0].UserId == "manual",
                entries.Count == 1 ? entries[0].UserId : "");
            Check("审计结果为成功", entries.Count == 1 && entries[0].Result);

            // 流程内写入 → owner 是流程
            using (IoWriteScope.Begin("flow:Main", null))
                model.TryWrite(false, out _);

            entries = VisionMaster.Core.AuditLogger.RecentEntries;
            Check("流程内写入审计 owner 为流程",
                entries.Count >= 2 && entries[0].UserId == "flow:Main",
                entries.Count > 0 ? entries[0].UserId : "");

            // 失败也要记：离线设备
            var offlineDevice = new FakeDevice(new IoDeviceDescriptor
            {
                DeviceId = "IO_2", BusId = "BUS_1", DriverId = "test", Points = new List<IoPointDescriptor>()
            }, new IoBusDescriptor { BusId = "BUS_1" }, online: false);
            var offlineModel = new IoOutputVariableModel(
                Guid.NewGuid(), "System.Io.IO_2.DO_1", typeof(bool),
                new FakeProvider(new[] { offlineDevice }), "IO_2", "DO_1", descriptor, "test", null);
            offlineModel.TryWrite(true, out _);

            entries = VisionMaster.Core.AuditLogger.RecentEntries;
            Check("失败写入也落审计",
                entries.Count >= 3 && !entries[0].Result && entries[0].OperationName.Contains("失败"),
                entries.Count > 0 ? entries[0].OperationName : "");
        }

        private static void TestBranchScope()
        {
            BeginSection("F");
            // 分支 owner = 流程 owner + /branch:N，两个分支互不干扰
            var leases = new IoOutputLeaseRegistry();
            IoWriteScope.AttachLeaseRegistry(leases);

            using (IoWriteScope.Begin("flow:Main", null))
            {
                string? outer = IoWriteScope.CurrentOwner;
                using (IoWriteScope.Begin($"{outer}/branch:0", null))
                {
                    Check("分支 0 owner 带分支后缀",
                        IoWriteScope.CurrentOwner == "flow:Main/branch:0", IoWriteScope.CurrentOwner);
                    Check("分支 0 取得 DO 租约",
                        leases.TryAcquire("DO_1", IoWriteScope.CurrentOwner!, 30000, false, out _));
                }

                Check("分支 0 退出后租约释放（静态挂接生效）", leases.GetOwner("DO_1") == null);
                Check("分支退出后恢复流程 owner", IoWriteScope.CurrentOwner == "flow:Main", IoWriteScope.CurrentOwner);

                using (IoWriteScope.Begin($"{outer}/branch:1", null))
                {
                    Check("分支 1 拿到的是自己的 owner",
                        IoWriteScope.CurrentOwner == "flow:Main/branch:1", IoWriteScope.CurrentOwner);
                }
            }

            Check("流程退出后 owner 清空", IoWriteScope.CurrentOwner == null);
            IoWriteScope.AttachLeaseRegistry(null);
        }

        private static void TestHealthMonitor()
        {
            BeginSection("E");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var bus = new IoBusDescriptor { BusId = "BUS_1", DisplayName = "总线1", TransportKind = IoTransportKind.Tcp };
            var descriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", DisplayName = "站1", BusId = "BUS_1", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            };
            workspace.CurrentSolution.IoBuses = new List<IoBusDescriptor> { bus };
            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor> { descriptor };

            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLog());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            provider.Apply(new[] { bus }, new[] { descriptor }, autoStart: false);
            catalog.Rebuild();

            var monitor = new IoHealthMonitor(provider, catalog);
            var snapshot = monitor.Capture();

            Check("健康快照：总线 1 条", snapshot.Buses.Count == 1, snapshot.Buses.Count.ToString());
            Check("健康快照：设备 1 台", snapshot.TotalDevices == 1, snapshot.TotalDevices.ToString());
            Check("健康快照：点位 1 个", snapshot.Points.Count == 1, snapshot.Points.Count.ToString());
            Check("健康快照：设备连接前非在线", snapshot.OnlineDevices == 0,
                snapshot.OnlineDevices.ToString());
            Check("健康快照摘要含在线计数", snapshot.Summary.Contains("0/1"), snapshot.Summary);
            Check("健康快照明细含设备标识", snapshot.ToDetailText().Contains("IO_1"), snapshot.ToDetailText());
            Check("无轮询样本时总线实测周期为 0（界面显示 —，不假装 0ms）",
                snapshot.Buses[0].AveragePollMs == 0, snapshot.Buses[0].AveragePollMs.ToString("F1"));

            // 连上设备跑几轮轮询，再抓一次：实测周期必须变成正数。
            var device = provider.Devices[0];
            device.Connect();
            Thread.Sleep(400);
            var live = monitor.Capture();
            Check("轮询后总线实测周期 > 0", live.Buses[0].AveragePollMs > 0,
                live.Buses[0].AveragePollMs.ToString("F1") + " ms");

            provider.ReleaseAll();
        }

        private static void TestSchemaVersion()
        {
            BeginSection("Y");
            var bus = new IoBusDescriptor { BusId = "BUS_1" };
            Check("总线默认版本为当前版本",
                bus.SchemaVersion == IoBusDescriptor.CurrentSchemaVersion, bus.SchemaVersion.ToString());
            Check("新总线配置校验通过", bus.Validate(out _));

            bus.SchemaVersion = IoBusDescriptor.CurrentSchemaVersion + 1;
            Check("总线版本高于软件支持时被拒绝",
                !bus.Validate(out var busError) && busError.Contains("高于当前软件支持"), busError);

            var device = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "simulated"
            };
            Check("新设备配置校验通过", device.Validate(out _));

            device.SchemaVersion = IoDeviceDescriptor.CurrentSchemaVersion + 1;
            Check("设备版本高于软件支持时被拒绝",
                !device.Validate(out var deviceError) && deviceError.Contains("高于当前软件支持"), deviceError);
        }

        private static void TestBusLinkSwitching()
        {
            BeginSection("X");
            var bus = new IoBusDescriptor
            {
                BusId = "BUS_1",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "10.0.0.5", Port = 502 },
                Serial = new SerialTransportConfig { PortName = "COM7", BaudRate = 19200 }
            };

            var model = IoBusEditModel.From(bus);

            Check("TCP 总线：链路模型是 TcpLinkEditModel",
                model.Transport is TcpLinkEditModel, model.Transport.GetType().Name);
            Check("TCP 总线：链路模型里读得到 IP",
                ((TcpLinkEditModel)model.Transport).Host == "10.0.0.5",
                ((TcpLinkEditModel)model.Transport).Host);

            // 只有 TcpLinkEditModel 带 [SuperDisplay] 的 TCP 字段；串口字段不属于当前对象
            var visibleBefore = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(model.Transport)
                .Select(p => p.Name).ToList();
            Check("TCP 链路只暴露 TCP 字段",
                visibleBefore.Contains(nameof(TcpLinkEditModel.Host)) &&
                visibleBefore.Contains(nameof(TcpLinkEditModel.Port)),
                string.Join("、", visibleBefore));
            Check("TCP 链路上没有串口字段",
                !visibleBefore.Contains(nameof(SerialLinkEditModel.PortName)),
                string.Join("、", visibleBefore));

            // 切到串口：模型换成 SerialLinkEditModel，且保留已填的串口草稿
            model.TransportKind = IoTransportKind.Serial;
            Check("切换到串口后链路模型是 SerialLinkEditModel",
                model.Transport is SerialLinkEditModel, model.Transport.GetType().Name);

            var visibleAfter = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(model.Transport)
                .Select(p => p.Name).ToList();
            Check("串口链路只暴露串口字段",
                visibleAfter.Contains(nameof(SerialLinkEditModel.PortName)) &&
                visibleAfter.Contains(nameof(SerialLinkEditModel.BaudRate)),
                string.Join("、", visibleAfter));
            Check("串口链路上没有 TCP 字段",
                !visibleAfter.Contains(nameof(TcpLinkEditModel.Host)),
                string.Join("、", visibleAfter));
            Check("切到串口后保留原 COM 口",
                ((SerialLinkEditModel)model.Transport).PortName == "COM7",
                ((SerialLinkEditModel)model.Transport).PortName);

            // 切回 TCP：TCP 值仍在
            model.TransportKind = IoTransportKind.Tcp;
            Check("切回 TCP 后保留原 IP",
                ((TcpLinkEditModel)model.Transport).Host == "10.0.0.5",
                ((TcpLinkEditModel)model.Transport).Host);

            // 描述符写回：两种链路都保留（运行期只看 TransportKind）
            var round = model.ToDescriptor(Guid.NewGuid());
            Check("写回描述符：TransportKind=TCP", round.TransportKind == IoTransportKind.Tcp);
            Check("写回描述符：TCP 主机正确", round.Tcp.Host == "10.0.0.5", round.Tcp.Host);
            Check("写回描述符：串口草稿保留", round.Serial.PortName == "COM7", round.Serial.PortName);
        }

        private static void TestDriverConfigSwitching()
        {
            BeginSection("Z");
            var modbusDevice = new IoDeviceDescriptor
            {
                DeviceId = "IO_M", BusId = "BUS_1", DriverId = "modbus.tcp",
                DriverConfigJson = "{\"UnitId\":7,\"ByteOrder\":\"ABCD\"}"
            };
            var modbus = IoDeviceEditModel.From(modbusDevice);
            Check("Modbus 设备：驱动模型是 ModbusDriverEditModel",
                modbus.DriverConfig is ModbusDriverEditModel, modbus.DriverConfig.GetType().Name);
            Check("Modbus 设备：读回站号",
                ((ModbusDriverEditModel)modbus.DriverConfig).UnitId == 7,
                ((ModbusDriverEditModel)modbus.DriverConfig).UnitId.ToString());
            Check("Modbus 设备：读回字序",
                ((ModbusDriverEditModel)modbus.DriverConfig).ByteOrder == IoByteOrder.ABCD);

            var modbusVisible = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(modbus.DriverConfig)
                .Select(p => p.Name).ToList();
            Check("Modbus 驱动只暴露 Modbus 字段",
                modbusVisible.Contains(nameof(ModbusDriverEditModel.UnitId)) &&
                !modbusVisible.Contains(nameof(S7DriverEditModel.Rack)),
                string.Join("、", modbusVisible));

            // 切到 S7：模型换类型，只暴露 S7 字段
            modbus.DriverId = "siemens.s7";
            Check("切换到 S7 后驱动模型是 S7DriverEditModel",
                modbus.DriverConfig is S7DriverEditModel, modbus.DriverConfig.GetType().Name);

            var s7Visible = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(modbus.DriverConfig)
                .Select(p => p.Name).ToList();
            Check("S7 驱动只暴露 S7 字段",
                s7Visible.Contains(nameof(S7DriverEditModel.Rack)) &&
                s7Visible.Contains(nameof(S7DriverEditModel.Slot)) &&
                !s7Visible.Contains(nameof(ModbusDriverEditModel.UnitId)),
                string.Join("、", s7Visible));

            // 写回 JSON 按当前驱动生成
            ((S7DriverEditModel)modbus.DriverConfig).Rack = 1;
            var descriptor = modbus.ToDescriptor(Guid.NewGuid(), Array.Empty<IoPointDescriptor>());
            Check("写回描述符：驱动 Id 为 S7", descriptor.DriverId == "siemens.s7");
            Check("写回描述符：S7 JSON 含 Rack",
                descriptor.DriverConfigJson.Contains("\"Rack\":1"), descriptor.DriverConfigJson);
            Check("写回描述符：S7 JSON 不含 UnitId",
                !descriptor.DriverConfigJson.Contains("UnitId"), descriptor.DriverConfigJson);

            // 模拟驱动：无私有字段
            var simulated = new IoDeviceEditModel { DriverId = "simulated" };
            Check("模拟驱动使用 SimulatedDriverEditModel",
                simulated.DriverConfig is SimulatedDriverEditModel, simulated.DriverConfig.GetType().Name);
            Check("模拟驱动 JSON 为空对象",
                simulated.ToDescriptor(Guid.NewGuid(), Array.Empty<IoPointDescriptor>()).DriverConfigJson == "{}");
        }

        private static void TestHealthViewModel()
        {
            BeginSection("O");
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var bus = new IoBusDescriptor
            {
                BusId = "BUS_1", DisplayName = "总线1", TransportKind = IoTransportKind.Tcp
            };
            var descriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", DisplayName = "站1", BusId = "BUS_1", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            };
            workspace.CurrentSolution.IoBuses = new List<IoBusDescriptor> { bus };
            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor> { descriptor };

            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLog());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLog());
            provider.Apply(new[] { bus }, new[] { descriptor }, autoStart: false);
            catalog.Rebuild();

            var vm = new IoHealthViewModel(new IoHealthMonitor(provider, catalog));
            vm.OnDialogOpened(new Prism.Dialogs.DialogParameters());

            Check("健康面板：设备行已填充", vm.Devices.Count == 1, vm.Devices.Count.ToString());
            Check("健康面板：总线行已填充", vm.Buses.Count == 1, vm.Buses.Count.ToString());
            Check("健康面板：点位行已填充", vm.Points.Count == 1, vm.Points.Count.ToString());
            Check("健康面板：摘要非空", !string.IsNullOrWhiteSpace(vm.Summary), vm.Summary);

            var deviceRow = vm.Devices[0];
            Check("设备行：状态文案为“未连接”", deviceRow.StateText == "未连接", deviceRow.StateText);
            Check("设备行：状态级别为 idle", deviceRow.StateLevel == "idle", deviceRow.StateLevel);
            Check("设备行：从未成功文案", deviceRow.LastSuccessText == "从未成功", deviceRow.LastSuccessText);

            var pointRow = vm.Points[0];
            Check("点位行：类型文案 DI", pointRow.KindText == "DI", pointRow.KindText);
            Check("点位行：启用文案", pointRow.EnabledText == "启用", pointRow.EnabledText);

            // 第二次刷新：行对象必须复用（否则每秒刷新会重置选中/滚动）
            var beforeRow = vm.Devices[0];
            vm.RefreshCommand.Execute();
            Check("刷新后复用同一设备行实例", ReferenceEquals(beforeRow, vm.Devices[0]));

            // 设备从 provider 移除后再刷新：行应消失
            provider.ReleaseAll();
            vm.RefreshCommand.Execute();
            Check("设备移除后行被清理", vm.Devices.Count == 0, vm.Devices.Count.ToString());
            Check("无设备时给出空态提示", !string.IsNullOrWhiteSpace(vm.Hint), vm.Hint);
        }

        private static void TestCommunicationBinder()
        {
            BeginSection("J");
            var manager = new AdvancedCommunicationManager();
            var binder = new CommunicationSolutionBinder(manager, new NullLog());

            // 方案 A：两条连接，一条 TCP 一条串口
            var solutionA = new SolutionModel { SolutionId = Guid.NewGuid(), SolutionName = "A" };
            solutionA.CommunicationConfigs = new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>
            {
                new("CONN_TCP", new ModbusTcpConfig { Type = CommunicationType.ModbusTcp, IpAddress = "127.0.0.1", Port = 15020 }),
                new("CONN_RTU", new SerialConfig { Type = CommunicationType.ModbusRtu, PortName = "COM9" })
            };

            var (loadedA, failedA) = binder.Apply(solutionA, autoStart: false);
            Check("方案 A：装配 2 条连接", loadedA == 2 && failedA == 0, $"{loadedA}/{failedA}");
            Check("方案 A：管理器里有 2 条",
                manager.GetAllConnections().Count == 2, manager.GetAllConnections().Count.ToString());

            // 方案 B：只有一条，且名字不同 → 旧连接必须整体消失
            var solutionB = new SolutionModel { SolutionId = Guid.NewGuid(), SolutionName = "B" };
            solutionB.CommunicationConfigs = new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>
            {
                new("CONN_B", new ModbusTcpConfig { Type = CommunicationType.ModbusTcp, IpAddress = "127.0.0.2", Port = 15021 })
            };

            var (loadedB, _) = binder.Apply(solutionB, autoStart: false);
            var names = manager.GetAllConnections().Select(c => c.ConnectionName).OrderBy(n => n).ToList();
            Check("方案 B：只装配 1 条", loadedB == 1, loadedB.ToString());
            Check("方案 B：旧方案连接已清除",
                names.Count == 1 && names[0] == "CONN_B", string.Join("、", names));

            // 空方案（无打开方案）→ 清空全部
            var (loadedNull, _) = binder.Apply(null, autoStart: false);
            Check("无方案：连接全部清空",
                loadedNull == 0 && manager.GetAllConnections().Count == 0,
                manager.GetAllConnections().Count.ToString());

            // 非法配置：跳过并继续，不阻断整份方案。
            // 注意判据用 Validate() 真正检查的字段（TimeoutMs/ReadTimeoutMs/RetryCount）——
            // [Required]/[RegexValidation] 只被属性面板的校验处理器消费，不进 Validate（既有口径）。
            var bad = new SolutionModel { SolutionId = Guid.NewGuid() };
            bad.CommunicationConfigs = new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>
            {
                new("BAD", new ModbusTcpConfig { Type = CommunicationType.ModbusTcp, TimeoutMs = 0 }),
                new("GOOD", new ModbusTcpConfig { Type = CommunicationType.ModbusTcp })
            };
            var (loadedBad, failedBad) = binder.Apply(bad, autoStart: false);
            Check("非法配置被跳过、合法配置仍装配",
                loadedBad == 1 && failedBad == 1, $"{loadedBad}/{failedBad}");
            Check("非法配置没进管理器",
                manager.GetAllConnections().All(c => c.ConnectionName != "BAD"));

            // Capture：管理器 → 方案
            var captured = new SolutionModel { SolutionId = Guid.NewGuid() };
            binder.Capture(captured);
            Check("Capture 把管理器连接写回方案",
                captured.CommunicationConfigs.Count == 1 &&
                captured.CommunicationConfigs[0].ConnectionName == "GOOD",
                string.Join("、", captured.CommunicationConfigs.Select(c => c.ConnectionName)));

            try { manager.Dispose(); } catch { }
        }

        private static void TestConfigMigrator()
        {
            BeginSection("Q");

            // 1) 当前版本：不迁移、不拒绝、幂等
            var bus = new IoBusDescriptor { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp };
            var device = new IoDeviceDescriptor { DeviceId = "IO_1", BusId = "BUS_1", DriverId = "simulated" };
            var r1 = IoConfigMigrator.Migrate(new[] { bus }, new[] { device });

            Check("当前版本不迁移", !r1.AnyMigrated && !r1.AnyRejected,
                $"migrated={r1.BusesMigrated}/{r1.DevicesMigrated} rejected={r1.BusesRejected}/{r1.DevicesRejected}");
            Check("当前版本无任何说明", r1.Messages.Count == 0, string.Join("；", r1.Messages));

            // 2) 版本 0（旧文件缺字段）：归一化为当前版本，不报错
            var legacy = new IoDeviceDescriptor
            {
                DeviceId = "IO_OLD",
                BusId = "BUS_1",
                DriverId = "modbus.tcp",
                SchemaVersion = 0,
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "P1", Kind = IoPointKind.AnalogInput, DataType = IoDataType.Int16,
                            Address = "40001", Scale = 0, Deadband = -1, DebounceMs = -1 }
                }
            };
            var r2 = IoConfigMigrator.Migrate(Array.Empty<IoBusDescriptor>(), new[] { legacy });

            Check("版本 0 被迁移到当前版本",
                legacy.SchemaVersion == IoDeviceDescriptor.CurrentSchemaVersion,
                legacy.SchemaVersion.ToString());
            Check("版本 0 不计入拒绝", r2.DevicesRejected == 0, r2.DevicesRejected.ToString());
            Check("归一化补齐 Scale=0 为 1",
                Math.Abs(legacy.Points[0].Scale - 1.0) < 1e-9, legacy.Points[0].Scale.ToString("F3"));
            Check("归一化把负死区置 0", legacy.Points[0].Deadband == 0, legacy.Points[0].Deadband.ToString());
            Check("归一化把负防抖置 0", legacy.Points[0].DebounceMs == 0, legacy.Points[0].DebounceMs.ToString());

            // 3) 版本高于当前：明确拒绝，且不改动配置
            var future = new IoBusDescriptor
            {
                BusId = "BUS_FUTURE",
                SchemaVersion = IoBusDescriptor.CurrentSchemaVersion + 1,
                TransportKind = IoTransportKind.Serial
            };
            var r3 = IoConfigMigrator.Migrate(new[] { future }, null);

            Check("未来版本被拒绝", r3.BusesRejected == 1, r3.BusesRejected.ToString());
            Check("被拒条目的版本号原样保留",
                future.SchemaVersion == IoBusDescriptor.CurrentSchemaVersion + 1,
                future.SchemaVersion.ToString());
            Check("拒绝时给出可读原因",
                r3.Messages.Any(m => m.Contains("高于当前软件支持")),
                string.Join("；", r3.Messages));

            // 4) null：不抛异常
            var r4 = IoConfigMigrator.Migrate(null, null);
            Check("null 集合不抛异常且零计数",
                r4.BusesMigrated == 0 && r4.DevicesMigrated == 0 && r4.Messages.Count == 0);

            // 5) 缺省字段全部补齐
            var sparse = new IoBusDescriptor { BusId = "BUS_SPARSE", Tcp = null!, Serial = null!, Reconnect = null! };
            var sparseDevice = new IoDeviceDescriptor
            {
                DeviceId = "IO_SPARSE", BusId = "BUS_SPARSE", DriverId = "simulated",
                Points = null!, Params = null!, DriverConfigJson = null!
            };
            IoConfigMigrator.Migrate(new[] { sparse }, new[] { sparseDevice });

            Check("缺失的 Tcp/Serial/Reconnect 被补齐",
                sparse.Tcp != null && sparse.Serial != null && sparse.Reconnect != null);
            Check("缺失的 Points/Params 被补齐",
                sparseDevice.Points != null && sparseDevice.Params != null);
            Check("空 DriverConfigJson 归一化为 {}",
                sparseDevice.DriverConfigJson == "{}", sparseDevice.DriverConfigJson);
            Check("归一化后配置可通过校验", sparseDevice.Validate(out var sparseError), sparseError);

            // 6) 迁移链本身（纯逻辑）：顺序、中止、步数不足、降级、非法版本
            var trace = new List<int>();
            var twoSteps = new Action<List<int>>[]
            {
                list => list.Add(0),
                list => list.Add(1)
            };

            bool okChain = IoConfigMigrator.TryMigrate(0, 2, twoSteps, trace, out var chainError);
            Check("迁移链按版本顺序执行（0→1→2）",
                okChain && trace.Count == 2 && trace[0] == 0 && trace[1] == 1,
                okChain ? string.Join(",", trace) : chainError);

            trace.Clear();
            bool sameVersion = IoConfigMigrator.TryMigrate(1, 1, twoSteps, trace, out _);
            Check("版本相同：一步不跑且成功", sameVersion && trace.Count == 0, trace.Count.ToString());

            var throwTrace = new List<int>();
            var throwing = new Action<List<int>>[]
            {
                list => { list.Add(0); throw new InvalidOperationException("结构不认识"); },
                list => list.Add(1)
            };
            bool failed = IoConfigMigrator.TryMigrate(0, 2, throwing, throwTrace, out var throwError);
            Check("某一步抛异常：整链失败并带版本区间",
                !failed && throwError != null && throwError.Contains("版本 0 → 1") && throwError.Contains("结构不认识"),
                throwError);
            Check("某一步失败后不再执行后续步骤（不把结构改坏）",
                throwTrace.Count == 1 && throwTrace[0] == 0, string.Join(",", throwTrace));

            bool shortChain = IoConfigMigrator.TryMigrate(0, 3, twoSteps, new List<int>(), out var shortError);
            Check("步骤表不够长：明确失败（不静默跳过剩下的）",
                !shortChain && shortError != null && shortError.Contains("缺少迁移步骤"), shortError);

            bool downgrade = IoConfigMigrator.TryMigrate(2, 1, twoSteps, new List<int>(), out var downError);
            Check("降级被拒绝", !downgrade && downError != null && downError.Contains("降级"), downError);

            bool negative = IoConfigMigrator.TryMigrate(-1, 1, twoSteps, new List<int>(), out var negError);
            Check("负版本号被拒绝", !negative && negError != null && negError.Contains("非法"), negError);

            bool nullSteps = IoConfigMigrator.TryMigrate(0, 1, null, new List<int>(), out var nullError);
            Check("步骤表为 null：明确失败", !nullSteps && nullError != null, nullError);

            // 7) 迁移消息里的"从版本"必须是迁移前的值（旧实现先改版本再拼消息 → 恒显示 1→1）
            var legacyBus = new IoBusDescriptor
            {
                BusId = "BUS_V0",
                SchemaVersion = 0,
                TransportKind = IoTransportKind.Tcp
            };
            var rMsg = IoConfigMigrator.Migrate(new[] { legacyBus }, null);
            Check("迁移消息记录的是迁移前的版本（0 → 1，而不是 1 → 1）",
                rMsg.Messages.Any(m => m.Contains("已从版本 0 迁移到 1")),
                string.Join("；", rMsg.Messages));
        }

        private static void TestBoundaryAndExceptionCases()
        {
            BeginSection("AE");

            // ---- 设备：未连接时的读写行为 ----
            var bus = new IoBusDescriptor { BusId = "BUS_1", TransportKind = IoTransportKind.Tcp };
            var descriptor = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", BusId = "BUS_1", DriverId = "test",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" },
                    new() { PointId = "DO_1", Kind = IoPointKind.DigitalOutput, DataType = IoDataType.Bool, Address = "2" }
                },
                Params = new IoParams { PollGroups = new List<IoPollGroup> { new() { Name = "fast", PeriodMs = 20 } } }
            };

            using (var device = new TestDevice(descriptor, bus))
            {
                device.StartWorker();

                Check("未连接：TryRead 返回 false",
                    !device.TryRead("DI_1", out var notConnected), notConnected.Quality.ToString());
                Check("未连接：ReadNow 明确失败",
                    device.ReadNow("DI_1", 200, CancellationToken.None, out var readErr).Quality != IoQuality.Good
                    && readErr != null, readErr);
                Check("未连接：WriteAndWait 明确失败",
                    !device.WriteAndWait("DO_1", IoPointValue.FromBool(true), 200,
                        CancellationToken.None, out var writeErr) && writeErr != null, writeErr);

                Check("不存在的点位：写入被拒并说明",
                    !device.WriteAndWait("NOT_EXIST", IoPointValue.FromBool(true), 100,
                        CancellationToken.None, out var missingErr)
                    && missingErr != null && missingErr.Contains("NOT_EXIST"), missingErr);

                Check("只读点：写入被拒",
                    !device.WriteAndWait("DI_1", IoPointValue.FromBool(true), 100,
                        CancellationToken.None, out var roErr) && roErr != null && roErr.Contains("只读"), roErr);
            }

            // ---- 取消：等待期间被取消应返回失败而不是抛 ----
            using (var device = new TestDevice(descriptor, bus))
            {
                device.StartWorker();
                device.Connect();

                using var cts = new CancellationTokenSource();
                cts.Cancel();
                Check("已取消的令牌：ReadNow 不抛异常",
                    TryNoThrow(() => device.ReadNow("DI_1", 500, cts.Token, out _)), "见实现");
            }

            // ---- 描述符：边界值 ----
            var zeroBridge = new IoDeviceDescriptor
            {
                DeviceId = "IO_Z", BusId = "BUS_1", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool,
                            Address = "1", DebounceMs = 0, Deadband = 0 }
                }
            };
            Check("防抖=0、死区=0 是合法配置", zeroBridge.Validate(out var zeroErr), zeroErr);

            var emptyAddress = new IoPointDescriptor
            {
                PointId = "P", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = ""
            };
            Check("空地址被拒绝",
                !emptyAddress.Validate(out var addrErr) && addrErr.Contains("地址"), addrErr);

            var analogBool = new IoPointDescriptor
            {
                PointId = "P", Kind = IoPointKind.AnalogInput, DataType = IoDataType.Bool, Address = "1"
            };
            Check("模拟量用 Bool 类型被拒",
                !analogBool.Validate(out var abErr) && abErr.Contains("Bool"), abErr);

            var negativeDebounce = new IoPointDescriptor
            {
                PointId = "P", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool,
                Address = "1", DebounceMs = -5
            };
            Check("负防抖被拒",
                !negativeDebounce.Validate(out var ndErr) && ndErr.Contains("防抖"), ndErr);

            // ---- Provider：坏配置不影响其它设备 ----
            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLog());
            var goodBus = new IoBusDescriptor { BusId = "BUS_OK", TransportKind = IoTransportKind.Tcp };
            var badDevice = new IoDeviceDescriptor
            {
                DeviceId = "IO_BAD", BusId = "BUS_OK", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "P1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" },
                    new() { PointId = "P1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "2" }
                }
            };
            var goodDevice = new IoDeviceDescriptor
            {
                DeviceId = "IO_GOOD", BusId = "BUS_OK", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "P1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            };

            provider.Apply(new[] { goodBus }, new[] { badDevice, goodDevice }, autoStart: false);
            Check("重复点位的设备被跳过、其它设备照常创建",
                provider.Devices.Count == 1 &&
                provider.Devices[0].Descriptor.DeviceId == "IO_GOOD",
                string.Join("、", provider.Devices.Select(d => d.Descriptor.DeviceId)));

            var orphan = new IoDeviceDescriptor
            {
                DeviceId = "IO_ORPHAN", BusId = "BUS_MISSING", DriverId = "simulated",
                Points = new List<IoPointDescriptor>()
            };
            provider.Apply(new[] { goodBus }, new[] { orphan }, autoStart: false);
            Check("找不到总线的设备被跳过", provider.Devices.Count == 0, provider.Devices.Count.ToString());

            var unknownDriver = new IoDeviceDescriptor
            {
                DeviceId = "IO_UNKNOWN", BusId = "BUS_OK", DriverId = "no.such.driver",
                Points = new List<IoPointDescriptor>()
            };
            provider.Apply(new[] { goodBus }, new[] { unknownDriver }, autoStart: false);
            Check("未知驱动不抛异常、设备数为 0", provider.Devices.Count == 0, provider.Devices.Count.ToString());

            var futureBus = new IoBusDescriptor
            {
                BusId = "BUS_OK", TransportKind = IoTransportKind.Tcp,
                SchemaVersion = IoBusDescriptor.CurrentSchemaVersion + 1
            };
            provider.Apply(new[] { futureBus }, new[] { goodDevice }, autoStart: false);
            Check("未来版本总线导致其下设备被跳过", provider.Devices.Count == 0, provider.Devices.Count.ToString());

            provider.ReleaseAll();

            // ---- 租约：过期回收 ----
            var leases = new IoOutputLeaseRegistry();
            // 租约超时有 1000ms 下限（防止抖动导致反复失效），所以这里不能用 50ms 去测
            leases.TryAcquire("DO_X", "owner-A", timeoutMs: 1000, allowShared: false, out _);
            Check("租约立即持有", leases.GetOwner("DO_X") == "owner-A", leases.GetOwner("DO_X"));
            Thread.Sleep(1200);
            Check("租约超时后被自动回收", leases.GetOwner("DO_X") == null, leases.GetOwner("DO_X"));

            // ---- 订阅：回调抛异常不影响刷新 ----
            int callbackCount = 0;
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });
            var ioProvider = new FakeProvider(Array.Empty<IIoDevice>());
            var catalog = new IoVariableCatalog(workspace, ioProvider, new NullLog());
            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor>
            {
                new()
                {
                    DeviceId = "IO_S", BusId = "BUS_1", DriverId = "simulated",
                    Points = new List<IoPointDescriptor>
                    {
                        new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                    }
                }
            };
            catalog.Rebuild();
            var variable = catalog.Snapshot().First(v => v.Name == "System.Io.IO_S.DI_1");
            using (var sub = new VariableSubscription(10, _ =>
                   {
                       callbackCount++;
                       throw new InvalidOperationException("订阅方故意抛异常");
                   }))
            {
                sub.Subscribe(variable);
                var publish = typeof(IoVariableModel).GetMethod("Publish",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                publish.Invoke(variable, new object?[] { true, VisionMaster.VariableQuality.Good, DateTime.Now });
                Thread.Sleep(30);
                Check("订阅回调抛异常被吞掉、不中断刷新",
                    TryNoThrow(() => sub.Pump()) && callbackCount == 1, callbackCount.ToString());
            }
        }

        private static bool TryNoThrow(Action action)
        {
            try { action(); return true; }
            catch { return false; }
        }

        private static void WaitUntil(Func<bool> predicate, int timeoutMs)

        {
            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                if (predicate()) return;
                Thread.Sleep(20);
            }
        }

        private sealed class TestDevice : IoDeviceBase
        {
            private readonly Dictionary<string, bool> _bits = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, double> _analogs = new(StringComparer.OrdinalIgnoreCase);

            public TestDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus)
                : base(descriptor, bus, new IoRuntimeContext(new NullLog()))
            {
                foreach (var point in descriptor.Points)
                {
                    if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
                        _bits[point.PointId] = false;
                    else
                        _analogs[point.PointId] = 10;
                }
            }

            public int SlowWriteMs { get; set; }

            public void SetInput(string pointId, bool value) => _bits[pointId] = value;

            protected override bool ConnectCore(out string? error)
            {
                error = null;
                return true;
            }

            protected override void DisconnectCore() { }

            protected override bool PollCore(IReadOnlyList<IoPointDescriptor> points,
                                             IDictionary<string, IoPointValue> values,
                                             out string? error)
            {
                foreach (var point in points)
                {
                    if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
                        values[point.PointId] = IoPointValue.FromBool(_bits.TryGetValue(point.PointId, out var b) && b);
                    else
                    {
                        double v = _analogs.TryGetValue(point.PointId, out var a) ? a : 0;
                        values[point.PointId] = IoPointValue.FromRaw(point.DataType, (long)v, v);
                    }
                }
                error = null;
                return true;
            }

            protected override bool WriteCore(IReadOnlyList<IoWriteRequest> writes, out string? error)
            {
                if (SlowWriteMs > 0)
                    Thread.Sleep(SlowWriteMs);

                foreach (var write in writes)
                    _bits[write.PointId] = write.Value.Boolean;
                error = null;
                return true;
            }
        }

        private sealed class FakeDevice : IIoDevice
        {
            private readonly bool _online;

            public FakeDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus, bool online = true)
            {
                Descriptor = descriptor;
                Bus = bus;
                _online = online;
            }

            public IoDeviceDescriptor Descriptor { get; }
            public IoBusDescriptor Bus { get; }
            public IoDeviceState State => _online ? IoDeviceState.Online : IoDeviceState.Closed;
            public string StateDetail => _online ? "fake" : "模拟离线";
            public IoCapabilities Capabilities => IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput;
            public DateTime LastSuccessTime => DateTime.Now;
            public long WriteCount => 0;
            public long RejectedWriteCount => 0;
            public long FaultCount => 0;
            public IoWriteRequest? LastWrite { get; private set; }

            public event EventHandler? StateChanged { add { } remove { } }
            public event EventHandler<IoPointsChangedEventArgs>? PointsChanged { add { } remove { } }
            public event EventHandler<IoFault>? FaultOccurred { add { } remove { } }

            public bool Connect() => true;
            public void Disconnect() { }
            public bool TryRead(string pointId, out IoPointValue value) { value = IoPointValue.FromBool(true); return true; }
            public IoPointValue ReadNow(string pointId, int timeoutMs, CancellationToken ct, out string? error) { error = null; return IoPointValue.FromBool(true); }
            public IoWriteResult Write(string pointId, IoPointValue value, string? ownerId = null) => IoWriteResult.Accept(1, Task.CompletedTask);
            public bool WriteAndWait(string pointId, IoPointValue value, int timeoutMs, CancellationToken ct, out string? error, string? ownerId = null)
            {
                LastWrite = new IoWriteRequest(pointId, value, ownerId);
                error = null;
                return true;
            }
            public void ResetCounters() { }
            public void Dispose() { }
        }

        private sealed class FakeProvider : IIoProvider
        {
            private readonly List<IIoDevice> _devices;
            public FakeProvider(IEnumerable<IIoDevice> devices) => _devices = devices.ToList();
            public IReadOnlyList<IoBusDescriptor> Buses => Array.Empty<IoBusDescriptor>();
            public IReadOnlyList<IIoDevice> Devices => _devices;
            public event EventHandler? DevicesChanged { add { } remove { } }
            public bool TryGetDevice(string deviceId, out IIoDevice device)
            {
                device = _devices.FirstOrDefault(d => d.Descriptor.DeviceId == deviceId)!;
                return device != null;
            }
            public bool TryGetDeviceByKey(string key, out IIoDevice device) => TryGetDevice(key, out device);
            public void StartAll() { }
            public void StopAll() { }
            public void ReleaseAll() { }
        }

        private sealed class PartialFailureDevice : IoDeviceBase
        {
            public PartialFailureDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus)
                : base(descriptor, bus, new IoRuntimeContext(new NullLog())) { }

            /// <summary>最近一轮真正参与轮询的点数（隔离后应下降）。</summary>
            public int LastPollPointCount { get; private set; }

            protected override bool ConnectCore(out string? error) { error = null; return true; }
            protected override void DisconnectCore() { }

            protected override bool PollCore(IReadOnlyList<IoPointDescriptor> points,
                                             IDictionary<string, IoPointValue> values,
                                             out string? error)
            {
                LastPollPointCount = points.Count;
                foreach (var point in points)
                {
                    // DI_BAD 永远不返回值 = 坏地址；DI_OK 正常返回 true
                    if (string.Equals(point.PointId, "DI_BAD", StringComparison.OrdinalIgnoreCase))
                        continue;
                    values[point.PointId] = IoPointValue.FromBool(true);
                }
                error = null;
                return true;
            }

            protected override bool WriteCore(IReadOnlyList<IoWriteRequest> writes, out string? error)
            {
                error = null;
                return true;
            }
        }

        private sealed class FakeVariableProvider : ISystemVariableProvider
        {
            private IVariable[] _items;
            public FakeVariableProvider(string name, IEnumerable<IVariable> items)
            {
                Name = name;
                _items = items.ToArray();
            }
            public string Name { get; }
            public event EventHandler? Changed { add { } remove { } }
            public void Reset(IEnumerable<IVariable> items) => _items = items.ToArray();
            public IReadOnlyList<IVariable> Snapshot() => _items;
        }

        private sealed class NullNotifier : IUserNotifier
        {
            public void ShowInfo(string message) { }
            public void ShowWarn(string message) { }
            public void ShowError(string message) { }
        }

        private sealed class NullLog : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }
    }
}
