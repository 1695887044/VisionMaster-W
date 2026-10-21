using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UI.Attributes;
using VisionMaster;
using Core.Interfaces.Io;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;

namespace CommChecks
{
    /// <summary>
    /// <para>通信侧非 GUI 断言宿主（对标 ScadaChecks）：把"多周期优先级调度 · 扫描组"这条链路上
    /// **不需要真设备**的规则固化成断言，离线可跑、可在 CI 里跑。</para>
    /// <para>为什么必须另起一个工程而不是复用 CommTest：CommTest 是**实机联调**工程
    /// （要求 Modbus TCP 502 / S7 108 模拟器在线），且只引 VM.Communication、拿不到 Core 的
    /// VariableDto / SolutionModel，无法断言"扫描组随方案落盘"。两者目标相反，不混用。</para>
    /// <para>覆盖范围：C1 组表过滤边界 / C2 归桶·回落·不丢点 / C3 改名级联·删组回落 /
    /// C4 组表复制 / C5 变量级扫描组落盘往返 / C6 连通测试的异步契约（不阻塞调用方）/
    /// C7 连接超时下发到 HSL 设备（配置的 TimeoutMs 真的生效）/
    /// C8 读超时下发到 HSL 设备（配置的 ReadTimeoutMs 真的生效，TCP 与串口共用入口）/
    /// C9 串口隐藏无效的「超时时间(ms)」（SerialConfig 用 override 把它从属性面板摘掉）/
    /// C10 串口字节序跟随配置（读写两侧同源，杜绝"写进去读不回来"）/
    /// C11 S7 读长度取自字节表（bool 必须 1 字节，不能用 Marshal.SizeOf 的 4）/
    /// C12 坏段不再静默（连续失败计数 + 旁路健康通道，且不改坏"全失败才判链路死"的原语义）/
    /// C13 写命令优先级与队列背压（读/写分队列，写插队；队列满先等再判失败）/
    /// C14 等待时长按到期算 + 限流 key 含连接名（不再 100Hz 空转；同址不同连接不互吞告警；
    /// 顺带钉住"从未 Start 的 Worker 也能安全 Dispose"——Join 对未启动线程抛 ThreadStateException）/
    /// B3 协议清单合一（Core 的 CommunicationProtocols 与连接工厂注册表必须一致；选中未实现协议
    /// 既不抛异常也不含糊，由 Validate 报「尚未实现 + 可用清单」）/
    /// B4 添加连接失败回滚（中途失败不留隐形僵尸连接，且只摘自己写进去的那一份，防 TOCTOU 误删）。</para>
    /// <para>不覆盖（需实机）：各扫描组的**实测周期与达成率**——那要真 PLC 才测得出，见
    /// <see cref="AdvancedCommunicationManager.GetScanGroupStats"/>。</para>
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 断言框架只捕得住"主线程同步抛出的异常"。若某条链路把异常甩到线程池，
            // 既没有红条也看不到是谁抛的，只剩一个退出码。挂兜底打印，把"静默死掉"变成"看得见的现场"。
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.Error.WriteLine("========== 未处理异常（进程即将退出） ==========");
                Console.Error.WriteLine(e.ExceptionObject);
            };

            Console.WriteLine("========== 通信断言（扫描组 / 多周期优先级调度） ==========");

            ScanGroupTableFilter();
            PollSchedulerBucketing();
            ScanGroupReassign();
            ConfigCloneAndCopy();
            ScanGroupPersistence();
            ConnectTimeoutPropagation();
            ReadTimeoutPropagation();
            SerialConnectTimeoutHidden();
            SerialByteOrderFollowsConfig();
            S7ByteCountFollowsHelperTable();
            FaultedSegmentHealth();
            PublishFailureIsolation();
            ValueTimestampPublication();
            SpanDecodeEquivalence();
            NetworkArrayDecodeAndOffset();
            NetworkStringAndBoolArrayDecode();
            NetworkArrayWriteEncoding();
            ArrayElementConversion();
            WritePriorityAndBackpressure();
            UrgentLanesAndPollYield();
            WaitSchedulingAndLogThrottleKey();
            GapToleranceByTransport();
            ModbusStationInAddressAndGroupKey();
            MergeAdjacentReadSwitch();
            EngineeringConversionOnReadPath();
            FaultedSegmentIsolation();
            CommunicationDriverRegistryContract();
            ModbusStationFrameContract();
            IoMigrationContract();
            McFinsFreeProtocolContract();
            McFinsBatchPlanning();
            McFinsAddressContract();
            ProtocolDisplayNameContract();
            FreeProtocolLoopback();
            CommunicationProtocolsAndAddRollback();
            KeepAliveConfigPropagation();
            ConfigJsonPerItemIsolation();
            // 以下两段会真发起 TCP 连接（靶子是本机未监听端口，内核秒拒），放最后
            RefusedClassificationAndBackoff();
            TestConnectionAsyncContract().GetAwaiter().GetResult();

            Finish();
            return Environment.ExitCode;
        }

        #region C1 组表过滤边界

        /// <summary>
        /// 组表合法性规则只有一个实现点（<c>ScanGroupTable.Resolve</c>），它同时被 UI 校验、
        /// 轮询编译、变量下拉框复用。这里通过 public 的 <see cref="PollScheduler.ResolveGroupNames"/>
        /// 打进去（该类是 internal，断言工程够不着）。
        /// </summary>
        private static void ScanGroupTableFilter()
        {
            Section("C1 组表过滤边界（PollScheduler.ResolveGroupNames）");

            var namesNull = PollScheduler.ResolveGroupNames(null);
            Check("C1-1 配置为 null 时组表只剩默认组",
                namesNull.Count == 1 && namesNull[0] == PollScheduler.DefaultGroupName,
                string.Join(",", namesNull));

            var names = PollScheduler.ResolveGroupNames(
                MakeConfig(1000, new ScanGroupConfig("快组", 100), new ScanGroupConfig("慢组", 5000)));
            Check("C1-2 默认组恒在首位（它是虚拟组，永远存在）",
                names.Count == 3 && names[0] == PollScheduler.DefaultGroupName,
                string.Join(",", names));

            Check("C1-3 默认组周期取连接配置的 ReadCycleMs",
                DefaultGroupInterval(250) == 250, $"实测 {DefaultGroupInterval(250)} ms");
            Check("C1-4 ReadCycleMs=0 时默认组周期回落 1000（不留 0 周期空转）",
                DefaultGroupInterval(0) == 1000, $"实测 {DefaultGroupInterval(0)} ms");
            Check("C1-4 ReadCycleMs=-5 时默认组周期回落 1000",
                DefaultGroupInterval(-5) == 1000, $"实测 {DefaultGroupInterval(-5)} ms");

            var reserved = PollScheduler.ResolveGroupNames(
                MakeConfig(1000, new ScanGroupConfig(PollScheduler.DefaultGroupName, 200)));
            Check("C1-5 自定义组用保留名「默认组」被剔除（周期只有一个真相源）",
                reserved.Count == 1, string.Join(",", reserved));

            var outOfRange = PollScheduler.ResolveGroupNames(MakeConfig(1000,
                new ScanGroupConfig("太快", PollScheduler.MinIntervalMs - 1),
                new ScanGroupConfig("太慢", PollScheduler.MaxIntervalMs + 1)));
            Check("C1-6 周期越界被剔除（下限-1 / 上限+1）",
                outOfRange.Count == 1, string.Join(",", outOfRange));

            var bounds = PollScheduler.ResolveGroupNames(MakeConfig(1000,
                new ScanGroupConfig("下限", PollScheduler.MinIntervalMs),
                new ScanGroupConfig("上限", PollScheduler.MaxIntervalMs)));
            Check("C1-7 周期边界值本身被接受（闭区间）",
                bounds.Count == 3, string.Join(",", bounds));

            var blank = PollScheduler.ResolveGroupNames(MakeConfig(1000,
                new ScanGroupConfig("", 100), new ScanGroupConfig("   ", 100)));
            Check("C1-8 空名 / 纯空白名被剔除",
                blank.Count == 1, string.Join(",", blank));

            var trim = PollScheduler.ResolveGroupNames(MakeConfig(1000, new ScanGroupConfig("  快组  ", 100)));
            Check("C1-9 组名前后空白被 Trim",
                trim.Count == 2 && trim[1] == "快组", string.Join(",", trim));

            var dupCfg = MakeConfig(1000, new ScanGroupConfig("同名", 100), new ScanGroupConfig("同名", 900));
            var dupSched = PollScheduler.Create(new[] { MakeVar("V", "同名", 0) }, dupCfg, null)!;
            var dupGroup = dupSched.GetStats().Single(s => s.GroupName == "同名");
            Check("C1-10 重名取首个（后一个被丢弃，不报错）",
                PollScheduler.ResolveGroupNames(dupCfg).Count == 2 && dupGroup.TargetIntervalMs == 100,
                $"{dupGroup.TargetIntervalMs} ms");

            var many = Enumerable.Range(0, 12).Select(i => new ScanGroupConfig($"G{i}", 100 + i)).ToArray();
            var capped = PollScheduler.ResolveGroupNames(MakeConfig(1000, many));
            Check("C1-11 自定义组封顶 MaxScanGroups（默认组不占额度）",
                CommunicationConfig.MaxScanGroups == 8 && capped.Count == 1 + CommunicationConfig.MaxScanGroups,
                $"{capped.Count} 组（1 默认 + {CommunicationConfig.MaxScanGroups} 自定义）");

            // 被剔除的组不应"吃掉"额度：一条空名 + 一条保留名 + 一条周期越界 + 8 条合法 → 恰好 1 + 8
            var skipThenFill = new List<ScanGroupConfig>
            {
                new("", 100),
                new(PollScheduler.DefaultGroupName, 100),
                new("越界", PollScheduler.MinIntervalMs - 1)
            };
            for (int i = 0; i < CommunicationConfig.MaxScanGroups; i++)
                skipThenFill.Add(new ScanGroupConfig($"合法{i}", 200));

            Check("C1-12 被剔除的组不占用封顶额度",
                PollScheduler.ResolveGroupNames(MakeConfig(1000, skipThenFill.ToArray())).Count
                    == 1 + CommunicationConfig.MaxScanGroups,
                string.Join(",", PollScheduler.ResolveGroupNames(MakeConfig(1000, skipThenFill.ToArray()))));
        }

        #endregion

        #region C2 归桶 / 回落 / 不丢点

        /// <summary>
        /// 归组的铁律：变量**绝不因为组配置问题而丢失轮询**——空名、指向已删除的组、保留名，
        /// 一律回落到默认组（安全侧设计）。这里用"变量数守恒"把这条铁律钉住。
        /// </summary>
        private static void PollSchedulerBucketing()
        {
            Section("C2 归桶 / 回落 / 不丢点（PollScheduler.Create）");

            var cfg = MakeConfig(1000, new ScanGroupConfig("快组", 100), new ScanGroupConfig("慢组", 5000));

            Check("C2-1 变量集为空 → Create 返回 null（不空转）",
                PollScheduler.Create(null, cfg) == null
                && PollScheduler.Create(new List<CommunicationVariable>(), cfg) == null,
                "");

            var writeOnly = new[]
            {
                MakeVar("W1", "", 0, VariableAccessMode.WriteOnly),
                MakeVar("W2", "", 1, VariableAccessMode.WriteOnly)
            };
            Check("C2-2 全部变量只写（无可轮询项）→ Create 返回 null",
                PollScheduler.Create(writeOnly, cfg) == null, "");

            var vars = new[]
            {
                MakeVar("A1", "快组", 0),
                MakeVar("A2", "快组", 1),
                MakeVar("B1", "慢组", 10),
                MakeVar("D1", "", 20)
            };
            var sched = PollScheduler.Create(vars, cfg, null)!;
            var stats = sched.GetStats();

            Check("C2-3 归桶正确：默认组 + 快组 + 慢组 = 3 组",
                sched.GroupCount == 3, sched.Describe());
            Check("C2-3 变量数守恒（4 个变量一个不少）",
                stats.Sum(s => s.VariableCount) == vars.Length,
                string.Join(",", stats.Select(s => $"{s.GroupName}={s.VariableCount}")));
            Check("C2-4 组按周期升序排列（首个最快 = 最高优先级）",
                stats[0].GroupName == "快组"
                && stats[1].GroupName == PollScheduler.DefaultGroupName
                && stats[2].GroupName == "慢组",
                sched.Describe());

            var merged = PollScheduler.Create(new[]
            {
                MakeVar("M1", "快组", 0), MakeVar("M2", "快组", 1), MakeVar("M3", "快组", 2)
            }, cfg, null)!;
            Check("C2-5 同组相邻地址合并为 1 段（3 变量 1 段，无单读兜底）",
                merged.TotalSegments == 1 && merged.TotalFallbacks == 0, merged.Describe());

            var cross = PollScheduler.Create(new[]
            {
                MakeVar("X1", "快组", 0), MakeVar("X2", "慢组", 1)
            }, cfg, null)!;
            Check("C2-6 不同扫描组不合并（周期不同，各自独立成段）",
                cross.TotalSegments == 2, cross.Describe());

            var logs = new List<string>();
            var fallback = PollScheduler.Create(new[]
            {
                MakeVar("F1", "已删除的组", 0),
                MakeVar("F2", "已删除的组", 1),
                MakeVar("F3", "已删除的组", 2)
            }, cfg, logs.Add)!;
            var fbDefault = fallback.GetStats().Single(s => s.GroupName == PollScheduler.DefaultGroupName);

            Check("C2-7 指向不存在组的变量回落到默认组（不丢点）",
                fbDefault.VariableCount == 3, fallback.Describe());
            Check("C2-7 回落汇总只出一条日志（几万点不逐变量刷屏）",
                logs.Count == 1 && logs[0].Contains("3 个变量") && logs[0].Contains(PollScheduler.DefaultGroupName),
                string.Join(" || ", logs));

            var reservedLogs = new List<string>();
            var reservedSched = PollScheduler.Create(
                new[] { MakeVar("R1", PollScheduler.DefaultGroupName, 0) }, cfg, reservedLogs.Add)!;
            Check("C2-8 变量扫描组写「默认组」直接归默认组，不计入回落",
                reservedSched.GroupCount == 1 && reservedLogs.Count == 0, reservedSched.Describe());

            var onlyDefault = PollScheduler.Create(new[] { MakeVar("S1", "", 0) }, cfg, null)!;
            Check("C2-9 没有任何变量的自定义组整组跳过（不留空转节拍）",
                onlyDefault.GroupCount == 1, onlyDefault.Describe());

            var trimVar = PollScheduler.Create(new[] { MakeVar("T1", "  快组  ", 0) }, cfg, null)!;
            Check("C2-10 变量组名的前后空白被 Trim 后归组（与组表解析同一口径）",
                trimVar.GetStats().Single(s => s.GroupName == "快组").VariableCount == 1,
                trimVar.Describe());

            var noConfig = PollScheduler.Create(new[] { MakeVar("N1", "快组", 0) }, null, null)!;
            Check("C2-11 连接配置缺失（组表未知）→ 全部回落默认组，不丢点",
                noConfig.GroupCount == 1
                && noConfig.GetStats()[0].GroupName == PollScheduler.DefaultGroupName
                && noConfig.GetStats()[0].VariableCount == 1,
                noConfig.Describe());
        }

        #endregion

        #region C3 改名级联 / 删组回落

        /// <summary>
        /// <see cref="AdvancedCommunicationManager.ReassignScanGroup"/> 是"组改名/删组"在轮询侧的唯一落点。
        /// 这里不 AddConnection、不 Connect——<c>RegisterVariable</c> 只校验 4 项非空、不查连接存在性，
        /// 故整段断言零后台线程、零设备依赖。
        /// </summary>
        private static void ScanGroupReassign()
        {
            Section("C3 改名级联 / 删组回落（AdvancedCommunicationManager.ReassignScanGroup）");

            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = false };

            var v1 = MakeVar("V1", "快组", 0);
            var v2 = MakeVar("V2", "快组", 1);
            var v3 = MakeVar("V3", "慢组", 2);
            var vx = MakeVar("VX", "", 9);
            manager.RegisterVariable(v1);
            manager.RegisterVariable(v2);
            manager.RegisterVariable(v3);
            manager.RegisterVariable(vx);

            Check("C3-1 变量已进入 Manager 私有注册表（反射探针）",
                IsRegistered(manager, "PLC1", "V1") && IsRegistered(manager, "PLC1", "V2")
                && IsRegistered(manager, "PLC1", "V3") && IsRegistered(manager, "PLC1", "VX"),
                "");

            int moved = manager.ReassignScanGroup("PLC1", "快组", "慢组");
            var reg1 = RegisteredVariable(manager, "PLC1", "V1");
            Check("C3-2 改名级联：受影响数 = 2", moved == 2, $"返回 {moved}");
            Check("C3-2 改的是注册表里的那一份实例（引用未换，重建后仍生效）",
                ReferenceEquals(reg1, v1) && reg1?.ScanGroup == "慢组" && v2.ScanGroup == "慢组",
                $"V1={reg1?.ScanGroup} V2={v2.ScanGroup}");
            Check("C3-2 未命中组名的变量不受影响",
                vx.ScanGroup == "", $"VX='{vx.ScanGroup}'");

            int fell = manager.ReassignScanGroup("PLC1", "慢组", null);
            Check("C3-3 删组回落：3 个变量全部回落默认组（newName=null → 空串）",
                fell == 3 && v1.ScanGroup == "" && v2.ScanGroup == "" && v3.ScanGroup == "",
                $"返回 {fell}");

            var v4 = MakeVar("V4", "快组", 3);
            manager.RegisterVariable(v4);
            int blank = manager.ReassignScanGroup("PLC1", "快组", "   ");
            Check("C3-4 newName 为纯空白 → 同样回落默认组",
                blank == 1 && v4.ScanGroup == "", $"返回 {blank}");

            Check("C3-5 老组名已无变量指向时返回 0（幂等，重复点删除不出错）",
                manager.ReassignScanGroup("PLC1", "不存在的组", "X") == 0, "");
            Check("C3-6 连接名 / 老组名为空 → 返回 0（不抛异常）",
                manager.ReassignScanGroup("", "快组", "X") == 0
                && manager.ReassignScanGroup("PLC1", "  ", "X") == 0, "");
            Check("C3-6 未注册的连接 → 返回 0",
                manager.ReassignScanGroup("不存在的连接", "快组", "X") == 0, "");

            var emptyNames = manager.GetScanGroupNames("");
            Check("C3-7 GetScanGroupNames 空连接名 → 仅默认组（编辑器下拉不空）",
                emptyNames.Count == 1 && emptyNames[0] == PollScheduler.DefaultGroupName,
                string.Join(",", emptyNames));

            var unknownNames = manager.GetScanGroupNames("PLC1");
            Check("C3-8 连接未缓存配置时组名只剩默认组（安全兜底，不抛）",
                unknownNames.Count == 1, string.Join(",", unknownNames));

            var preview0 = manager.GetScanGroupPreview("PLC1");
            Check("C3-9 无组表时预览只剩默认组，变量数守恒",
                preview0.Count == 1 && preview0[0].VariableCount == 5,
                string.Join(",", preview0.Select(s => $"{s.GroupName}={s.VariableCount}")));

            var v5 = MakeVar("V5", "新增组", 5);
            manager.RegisterVariable(v5);
            var previewCopy = manager.GetScanGroupPreview("PLC1", MakeConfig(1000, new ScanGroupConfig("新增组", 200)));
            Check("C3-10 传入组表副本即可预览「刚加的组」（编辑器取消不弄脏活对象）",
                previewCopy.Any(s => s.GroupName == "新增组" && s.VariableCount == 1),
                string.Join(",", previewCopy.Select(s => $"{s.GroupName}={s.VariableCount}")));
            Check("C3-11 预览是静态画像：实测周期恒为 0（还没上线测量）",
                previewCopy.All(s => s.AvgActualMs == 0 && s.AchieveRate == 0), "");
            Check("C3-12 预览不要求连接在线（未 AddConnection 也能算，故可离线断言）",
                manager.ConnectionCount == 0 && previewCopy.Count > 0,
                $"连接数 {manager.ConnectionCount}");

            Check("C3-13 无已注册变量的连接 → 预览为空表",
                manager.GetScanGroupPreview("别的连接").Count == 0, "");
        }

        #endregion

        #region C4 组表复制

        /// <summary>
        /// 扫描组编辑器编辑的是**副本**（取消不能弄脏活对象）。深拷一旦退化成浅拷，
        /// 用户在弹窗里点"取消"也会改到活配置——这是最难查的一类脏写。
        /// </summary>
        private static void ConfigCloneAndCopy()
        {
            Section("C4 组表复制（CommunicationConfig.Clone / CopyFrom）");

            var src = MakeConfig(750, new ScanGroupConfig("快组", 100), new ScanGroupConfig("慢组", 5000));
            var clone = src.Clone();

            Check("C4-1 Clone 深拷组表（容器与元素都不是同一引用）",
                clone.ScanGroups.Count == 2
                && !ReferenceEquals(clone.ScanGroups, src.ScanGroups)
                && !ReferenceEquals(clone.ScanGroups[0], src.ScanGroups[0])
                && !ReferenceEquals(clone.ScanGroups[1], src.ScanGroups[1]),
                "");
            Check("C4-2 Clone 保留组定义内容与默认组周期",
                clone.ScanGroups[0].Name == "快组" && clone.ScanGroups[0].IntervalMs == 100
                && clone.ScanGroups[1].IntervalMs == 5000 && clone.ReadCycleMs == 750,
                $"{string.Join(",", clone.ScanGroups)} / 默认 {clone.ReadCycleMs}ms");
            Check("C4-3 Clone 改连接名（副本不抢占原名）",
                clone.ConnectionName == src.ConnectionName + "_Copy", clone.ConnectionName);

            clone.ScanGroups[0].IntervalMs = 9999;
            Check("C4-4 改副本的组不影响原件（深拷的全部意义）",
                src.ScanGroups[0].IntervalMs == 100, src.ScanGroups[0].IntervalMs.ToString());

            var target = MakeConfig(1000);
            int hint = 0;
            target.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CommunicationConfig.PollHint)) hint++;
            };
            target.CopyFrom(src);

            Check("C4-5 CopyFrom 深拷组表",
                target.ScanGroups.Count == 2
                && !ReferenceEquals(target.ScanGroups, src.ScanGroups)
                && !ReferenceEquals(target.ScanGroups[0], src.ScanGroups[0]),
                "");
            Check("C4-6 CopyFrom 后「轮询」列文案联动（PollHint 收到通知）",
                hint >= 1, $"通知 {hint} 次");
            Check("C4-7 PollHint 文案 = 默认周期 + 自定义组数",
                target.PollHint == "默认 750 ms  +2 组", target.PollHint);
            Check("C4-8 无自定义组时 PollHint 只报默认周期",
                MakeConfig(500).PollHint == "默认 500 ms", MakeConfig(500).PollHint);

            var noGroups = new CommunicationConfig(CommunicationType.ModbusTcp)
            {
                ConnectionName = "NoGroups",
                ScanGroups = null!
            };
            var noGroupsClone = noGroups.Clone();
            Check("C4-9 原件组表为 null 时 Clone 仍给空表（不抛 NRE）",
                noGroupsClone.ScanGroups != null && noGroupsClone.ScanGroups.Count == 0, "");

            // P0 回归：Clone/CopyFrom 必须带上基类 Type。
            // 漏拷时副本自报 ModbusTcp（枚举默认值 0），而 Validate 会判"协议类型不匹配"——
            // 表现是"编辑连接点确定就失败"，且此时旧连接已被管理器先删后加地摘掉了。
            foreach (var protocol in CommunicationProtocols.Implemented)
            {
                var live = new CommunicationConfig(protocol);
                var copy = live.Clone();
                bool cloneOk = copy.Config?.Type == protocol && copy.Validate(out _);

                var targetConfig = new CommunicationConfig(protocol);
                targetConfig.CopyFrom(copy);
                bool copyOk = targetConfig.Config?.Type == protocol && targetConfig.Validate(out _);

                Check($"C4-10 {protocol}：Clone / CopyFrom 后 Config.Type 跟随协议且 Validate 通过",
                    cloneOk && copyOk,
                    $"clone={copy.Config?.Type} copyFrom={targetConfig.Config?.Type}");
            }
        }

        #endregion

        #region B8/B9 MC·FINS 地址与显示名

        /// <summary>
        /// MC/FINS 地址的"进制 / 厂商 / 位偏移"契约。
        /// <para>为什么必须有：MC 的 X/Y/B/W 是十六进制编址，而批处理规划器用 "前缀 + 十进制 Start"
        /// 拼段地址——对十六进制区会把 W100(=256) 拼成 "W256"(=598)，单读与批读打到不同设备。
        /// 这条断言钉住"单读地址 == 批读段地址"。</para>
        /// </summary>
        private static void McFinsAddressContract()
        {
            Section("B8 MC/FINS 地址（进制 / 厂商 / 位偏移）");

            var w = new McAddress { Area = McArea.W, Offset = "100", DataType = DataValueType.Int16, Length = 1 };
            bool wBatch = w.TryCreatePollAddress(out var wPoll);
            Check("B8-1 MC W 区：单读地址与批读段地址一致（十六进制区不能二次转写）",
                w.Address == "W100" && wBatch && wPoll!.SegmentAddress == "W100" && wPoll.Start == 256,
                $"addr={w.Address} seg={wPoll?.SegmentAddress} start={wPoll?.Start}");

            var x = new McAddress { Area = McArea.X, Offset = "1A", DataType = DataValueType.Boolean, Length = 1 };
            var xHexPrefix = new McAddress { Area = McArea.X, Offset = "0x1A", DataType = DataValueType.Boolean, Length = 1 };
            Check("B8-2 MC X 区接受十六进制写法（1A / 0x1A）",
                x.Validate().IsValid && x.Address == "X1A" && xHexPrefix.Address == "X1A",
                $"{x.Address} / {xHexPrefix.Address} / {x.Validate().ErrorMessage}");

            var m = new McAddress { Area = McArea.M, Offset = "100", AddressOffset = 5, DataType = DataValueType.Boolean, Length = 1 };
            Check("B8-3 MC 位区也吃「地址偏移」（旧实现位区直接忽略）",
                m.Address == "M105", m.Address);

            var mInt = new McAddress { Area = McArea.M, Offset = "100", DataType = DataValueType.Int32, Length = 1 };
            var mIntResult = mInt.Validate();
            Check("B8-4 MC 位区配数值类型在创建阶段被拒（不再等轮询时出怪值）",
                !mIntResult.IsValid, mIntResult.ErrorMessage);

            var panaM = new McAddress { Area = McArea.M, Vendor = McVendor.Panasonic, Offset = "100", DataType = DataValueType.Boolean, Length = 1 };
            var keyF = new McAddress { Area = McArea.F, Vendor = McVendor.Keyence, Offset = "100", DataType = DataValueType.Boolean, Length = 1 };
            var keyB = new McAddress { Area = McArea.B, Vendor = McVendor.Keyence, Offset = "1A", DataType = DataValueType.Boolean, Length = 1 };
            Check("B8-5 MC 地址校验按厂商解析器（松下无 M / 基恩士 F 只认 FM / 基恩士 B 可用）",
                !panaM.Validate().IsValid && !keyF.Validate().IsValid && keyB.Validate().IsValid,
                $"松下M={panaM.Validate().IsValid} 基恩士F={keyF.Validate().IsValid} 基恩士B={keyB.Validate().IsValid}");

            var zrM = new McAddress { Area = McArea.ZR, Vendor = McVendor.Mitsubishi, Offset = "100", DataType = DataValueType.Int16, Length = 1 };
            var zrK = new McAddress { Area = McArea.ZR, Vendor = McVendor.Keyence, Offset = "100", DataType = DataValueType.Int16, Length = 1 };
            bool zrMok = zrM.TryCreatePollAddress(out var zrMPoll);
            bool zrKok = zrK.TryCreatePollAddress(out var zrKPoll);
            Check("B8-6 ZR 的进制随厂商（三菱十进制 100 / 基恩士十六进制 0x100）",
                zrMok && zrKok && zrMPoll!.Start == 100 && zrKPoll!.Start == 256
                && zrMPoll.SegmentAddress == "ZR100" && zrKPoll.SegmentAddress == "ZR100",
                $"三菱 {zrMPoll?.Start}/{zrMPoll?.SegmentAddress} vs 基恩士 {zrKPoll?.Start}/{zrKPoll?.SegmentAddress}");

            var finsBit = new FinsAddress { Area = FinsArea.D, Offset = "100", BitOffset = 15, DataType = DataValueType.Boolean, Length = 1 };
            var finsNoBit = new FinsAddress { Area = FinsArea.D, Offset = "100", BitOffset = -1, DataType = DataValueType.Boolean, Length = 1 };
            Check("B8-7 FINS 位偏移 0-15（旧实现钳到 7，D100.12 建不出来）",
                finsBit.Validate().IsValid && finsBit.Address == "D100.15" && !finsNoBit.Validate().IsValid,
                $"{finsBit.Address} / 无位偏移 valid={finsNoBit.Validate().IsValid}");

            var serialText = new SerialConfig().ToString();
            Check("B8-8 串口配置 ToString 不是类型全名（通讯列表「IP:端口」列）",
                !serialText.Contains("VisionMaster.", StringComparison.Ordinal), serialText);
        }

        /// <summary>
        /// R40/R84：枚举上屏必须走中文显示名。
        /// <para>属性面板（FlatPropertyGrid 的 EnumGenerator）读 <c>[Description]</c>；
        /// MC/FINS 的存储区下拉读枚举上的 <c>[Display(Name)]</c>。两条路读的不是同一个特性，
        /// 所以这里分开断言——只补一个会让另一条路继续显示英文。</para>
        /// </summary>
        private static void ProtocolDisplayNameContract()
        {
            Section("B9 协议/枚举显示名（R40/R84）");

            Check("B9-1 CommunicationType 每个成员都有中文 [Description]",
                AllHaveDescription<CommunicationType>(), "");
            Check("B9-2 McProtocolType / McFrameFormat 有 [Description]",
                AllHaveDescription<McProtocolType>() && AllHaveDescription<McFrameFormat>(), "");
            Check("B9-3 ParityMode / StopBitsMode 有 [Description]",
                AllHaveDescription<ParityMode>() && AllHaveDescription<StopBitsMode>(), "");
            Check("B9-4 McArea / FinsArea 每个成员都有 [Display(Name)]（存储区下拉的数据源）",
                AllHaveDisplay<McArea>() && AllHaveDisplay<FinsArea>(), "");
            Check("B9-5 已实现协议文案走中文显示名（不再拼枚举成员名）",
                CommunicationProtocols.ImplementedText
                    == "Modbus TCP / Modbus RTU / 西门子 S7 / 三菱 MC / 欧姆龙 FINS / 自由协议",
                CommunicationProtocols.ImplementedText);
        }

        private static bool AllHaveDescription<TEnum>() where TEnum : struct, Enum
            => Enum.GetValues<TEnum>().All(v => v.GetType().GetField(v.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 });

        private static bool AllHaveDisplay<TEnum>() where TEnum : struct, Enum
            => Enum.GetValues<TEnum>().All(v =>
                v.GetType().GetField(v.ToString())?
                    .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                    .OfType<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                    .Any(d => !string.IsNullOrWhiteSpace(d.Name)) == true);

        #endregion

        #region C5 变量级扫描组落盘往返

        /// <summary>
        /// <para>这条是本次改造里最隐蔽的坑：组表存在**连接配置**（communications.json）里、不随方案走，
        /// 而"变量属于哪个组"存在**方案**里。若 VariableDto 漏了 ScanGroup，用户在变量管理里挂好的快组
        /// 会在下次打开方案时**静默退回默认组**（100ms → 1s），界面上一点异常都看不出。</para>
        /// <para>断言链：Capture → Serialize → Deserialize → Restore → NetworkVariableModel.ScanGroup，
        /// 全程照着真实的保存/加载路径走。</para>
        /// </summary>
        private static void ScanGroupPersistence()
        {
            Section("C5 变量级扫描组落盘往返（VariableDto.ScanGroup / VariablePersistenceService）");

            var w1 = new WorkspaceContext();
            w1.GlobalVariables.Clear();
            w1.GlobalVariables.Add(VariableFactory.CreateLocal("LocalA", typeof(int), "本地变量", 7));
            w1.GlobalVariables.Add(MakeNetworkVar("NetFast", "PLC1", "快组"));
            w1.GlobalVariables.Add(MakeNetworkVar("NetDefault", "PLC1", ""));

            var solution = new SolutionModel();
            VariablePersistenceService.Capture(solution, w1);

            var netFastDto = solution.VariableSnapshots.Single(d => d.Name == "NetFast");
            var netDefaultDto = solution.VariableSnapshots.Single(d => d.Name == "NetDefault");

            Check("C5-1 Capture 把 NetworkVariableModel.ScanGroup 抄进快照",
                netFastDto.ScanGroup == "快组" && netDefaultDto.ScanGroup == "",
                $"NetFast='{netFastDto.ScanGroup}' NetDefault='{netDefaultDto.ScanGroup}'");
            Check("C5-2 本地变量快照不受影响（本地变量没有扫描组概念）",
                solution.VariableSnapshots.Count(d => d.VarType == "Local") == 1
                && solution.VariableSnapshots.Count == 3, $"{solution.VariableSnapshots.Count} 条快照");

            var json = SolutionService.Serialize(solution);
            Check("C5-3 序列化文本里真的有 ScanGroup 字段（不是内存里对、落盘丢）",
                json.Contains("\"ScanGroup\": \"快组\""), "");

            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            Check("C5-4 反序列化后快照里的扫描组存活",
                reloaded.VariableSnapshots.Single(d => d.Name == "NetFast").ScanGroup == "快组",
                reloaded.VariableSnapshots.Single(d => d.Name == "NetFast").ScanGroup);

            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();
            VariablePersistenceService.Restore(reloaded, w2);

            var restored = w2.GlobalVariables.OfType<NetworkVariableModel>().Single(v => v.Name == "NetFast");
            Check("C5-5 Restore 把扫描组还原到 NetworkVariableModel（桥接器随后抄进 CommunicationVariable）",
                restored.ScanGroup == "快组", restored.ScanGroup);
            Check("C5-6 还原后变量数守恒（本地 1 + 网络 2）",
                w2.GlobalVariables.Count == 3, w2.GlobalVariables.Count.ToString());

            // 还原出来的扫描组必须真的能归对桶（端到端闭环，而不是"字段有值"就算过）
            var restoredSched = PollScheduler.Create(
                new[] { MakeVar("V", restored.ScanGroup, 0) },
                MakeConfig(1000, new ScanGroupConfig("快组", 100)), null)!;
            Check("C5-7 还原后的组名能归进对应的自定义组（端到端闭环）",
                restoredSched.GetStats().Any(s => s.GroupName == "快组" && s.TargetIntervalMs == 100),
                restoredSched.Describe());

            // 旧方案零回归：字段缺失 → 反序列化为空串 → 正好等于"默认组"
            var legacyJson = json;
            if (JObject.Parse(json) is { } jObj && jObj["VariableSnapshots"] is JArray snaps)
            {
                foreach (var snap in snaps.OfType<JObject>()) snap.Remove("ScanGroup");
                legacyJson = jObj.ToString();
            }
            Check("C5-8 构造出「旧方案」（VariableSnapshots 内无 ScanGroup 字段）",
                !legacyJson.Contains("ScanGroup"), "");

            var legacy = JsonConvert.DeserializeObject<SolutionModel>(legacyJson, RoundTripSettings)!;
            var w3 = new WorkspaceContext();
            w3.GlobalVariables.Clear();
            VariablePersistenceService.Restore(legacy, w3);
            var legacyVar = w3.GlobalVariables.OfType<NetworkVariableModel>().Single(v => v.Name == "NetFast");
            Check("C5-9 旧方案缺字段 → 还原为空串（= 默认组，行为与改造前完全一致）",
                legacyVar.ScanGroup == "", $"'{legacyVar.ScanGroup}'");
        }

        #endregion

        #region 构造辅助

        /// <summary>方案序列化设置：与 <c>SolutionService</c> 内部那份保持一致（否则往返断言测的不是同一条链路）</summary>
        private static readonly JsonSerializerSettings RoundTripSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new ConnectionConfigSerializationBinder()
        };

        private static CommunicationConfig MakeConfig(int readCycleMs, params ScanGroupConfig[] groups)
        {
            var cfg = new CommunicationConfig(CommunicationType.ModbusTcp)
            {
                ConnectionName = "PLC1",
                ReadCycleMs = readCycleMs
            };
            if (groups.Length > 0)
                cfg.ScanGroups = groups.ToList();
            return cfg;
        }

        /// <summary>
        /// 造一个"规划器不会跳过"的 Modbus 保持寄存器变量：有结构化地址（避免字符串解析）、
        /// 可解析的 CLR 类型、非只写。offset 相邻即会被合并成同一段。
        /// </summary>
        private static CommunicationVariable MakeVar(
            string name, string scanGroup, int offset,
            VariableAccessMode mode = VariableAccessMode.ReadOnly,
            bool mergeAdjacent = true)
        {
            var addr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = offset.ToString()
            };
            return new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = name,
                Address = addr.Address,
                AddressConfig = addr,
                ValueType = typeof(short).AssemblyQualifiedName!,
                ScanGroup = scanGroup,
                AccessMode = mode,
                MergeAdjacentRead = mergeAdjacent
            };
        }

        private static NetworkVariableModel MakeNetworkVar(string name, string connectionName, string scanGroup)
        {
            var addr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "0"
            };
            return new NetworkVariableModel
            {
                Name = name,
                VariableId = Guid.NewGuid(),
                DataType = typeof(short),
                ConnectionName = connectionName,
                AddressConfig = addr,
                Description = "网络变量",
                DefaultValue = (short)0,
                Value = (short)0,
                ScanGroup = scanGroup
            };
        }

        /// <summary>读默认组周期的唯一口径：编译一次调度器取快照（ResolveGroupNames 只给名字，不给周期）</summary>
        private static int DefaultGroupInterval(int readCycleMs)
        {
            var sched = PollScheduler.Create(new[] { MakeVar("V", "", 0) }, MakeConfig(readCycleMs), null);
            return sched?.GetStats()[0].TargetIntervalMs ?? -1;
        }

        #endregion

        #region C6 连通测试的异步契约

        /// <summary>
        /// A1：<see cref="AdvancedCommunicationManager.TestConnectionAsync"/> 存在的唯一理由就是
        /// "别在 UI 线程上硬等一个 TimeoutMs"（旧同步版 <c>GetAwaiter().GetResult()</c> 会把界面冻住 3 秒，
        /// 偏偏"连不上"时必然等满——那正是用户最需要按这个按钮的场景）。
        /// 这里锁两条契约：①调用**立即返回**（真探测在 Worker 线程上跑）②结果与同步版一致。
        /// 靶子用 TEST-NET-1（192.0.2.1，RFC 5737 保留段，必然不可达），零真设备依赖。
        /// </summary>
        private static async Task TestConnectionAsyncContract()
        {
            Section("C6 连通测试的异步契约（A1：不阻塞调用方）");

            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = false };

            Check("C6-1 不存在的连接 → 异步版返回 false（不抛）",
                await manager.TestConnectionAsync("没这条连接") == false, "");
            Check("C6-2 不存在的连接 → 同步版同样返回 false（同步版只是异步版的硬等包装，CommunicationCheck 仍在用）",
                manager.TestConnection("没这条连接") == false, "");

            var cfg = new CommunicationConfig(CommunicationType.ModbusTcp)
            {
                ConnectionName = "PLC_REFUSED"
            };
            cfg.Config.TimeoutMs = 300; // 默认 3000：压到 300ms，断言不必陪着等
            var mcfg = (ModbusTcpConfig)cfg.Config;
            // 靶子为什么用 127.0.0.1:1（本机必然无人监听的端口），而不是 192.0.2.1（TEST-NET-1 保留段）：
            // 实测在装了本地代理/TUN 的机器上，到 192.0.2.1:502 的 TCP 连接会被代理**秒接**成功（0ms），
            // 于是 HSL 的 ConnectServer() 报 IsSuccess=true——它只证明"TCP 连接建立"，不证明"对面是活的 Modbus 设备"。
            // 本机回环的未监听端口由内核直接 ECONNREFUSED，是唯一不依赖网络环境、必然"连不上"的靶子。
            mcfg.IpAddress = "127.0.0.1";
            mcfg.Port = 1;
            manager.AddConnection(cfg);

            // 先把 Worker 线程占住 600ms，再发起探测：这样"调用当即返回"才有确定性证据——
            // 若 TestConnectionAsync 退化成同步硬等（A1 之前的写法），这里必然陪等 600ms 以上，远超 150ms 阈值
            var worker = GetWorker(manager, "PLC_REFUSED");
            var blocker = worker.Invoke(_ =>
            {
                Thread.Sleep(600);
                return true;
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var probe = manager.TestConnectionAsync("PLC_REFUSED");
            var callMs = sw.ElapsedMilliseconds;

            Check("C6-3 调用异步版当即返回（<150ms 且探测尚未完成），不阻塞调用方——界面不会被冻",
                callMs < 150 && !probe.IsCompleted,
                $"调用耗时 {callMs}ms，探测已完成={probe.IsCompleted}");

            await blocker;
            bool ok = await probe;
            Check("C6-4 连不上（本机未监听端口）→ 探测结果为 false（异步版把结果如实带回，不吞异常）",
                ok == false, $"返回 {ok}");

            manager.RemoveConnection("PLC_REFUSED");
            Check("C6-5 连接已不在册 → 同步/异步版都返回 false（不抛异常）",
                manager.TestConnection("PLC_REFUSED") == false
                && await manager.TestConnectionAsync("PLC_REFUSED") == false, "");
        }

        #endregion

        #region C7 连接超时下发

        /// <summary>
        /// 「超时时间(ms)」是 <see cref="ConnectionConfigBase.TimeoutMs"/>，在连接设置的「高级参数」里
        /// 对每一种连接都可见。但它此前**只**被 Manager 当作"等待首次建连"的预算
        /// （<see cref="AdvancedCommunicationManager.Connect"/>），**从未下发到 HSL 设备**——
        /// 于是 HSL 自己的 ConnectTimeOut（默认 10000ms）才是真正约束 socket 建连的值，
        /// 用户配的数被静默忽略：调大没用（socket 10 秒就放弃），调小也不彻底
        /// （上层报了失败，建连线程还占着）。
        /// <para>这里断言"配置值确实落到了设备对象上"。不真建连，所以离线可跑。</para>
        /// </summary>
        private static void ConnectTimeoutPropagation()
        {
            Section("C7 连接超时下发到 HSL 设备（config.TimeoutMs → device.ConnectTimeOut）");

            // 默认值路径：HSL 自身默认 10000，不下发就会被它盖掉
            using (var mtConn = new ModbusTcpConnection(new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502 }))
            {
                int actual = DeviceConnectTimeOut(mtConn);
                Check("C7-1 ModbusTcp：默认超时 3000 下发到设备（HSL 默认是 10000，不下发就被盖掉）",
                    actual == 3000, $"设备实际值 {actual}ms");
            }

            // 非默认值路径：证明不是"恰好等于默认值"的假通过，也不是被 HSL 上限截断
            using (var mtConn = new ModbusTcpConnection(
                new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502, TimeoutMs = 60000 }))
            {
                int actual = DeviceConnectTimeOut(mtConn);
                Check("C7-2 ModbusTcp：改成 60000 同样如实下发（用户调大超时时能真的等更久）",
                    actual == 60000, $"设备实际值 {actual}ms");
            }

            // 同一条基类字段，不允许只有 Modbus 生效
            using (var s7Conn = new SiemensS7Connection(
                new SiemensS7Config { IpAddress = "192.168.0.1", TimeoutMs = 1234 }))
            {
                int actual = DeviceConnectTimeOut(s7Conn);
                Check("C7-3 SiemensS7：配置值同样下发（基类字段对每种连接都可见，不能只有 Modbus 生效）",
                    actual == 1234, $"设备实际值 {actual}ms");
            }

            // 串口不参与本段断言：ModbusRtu 没有"TCP 建连超时"这个量，其 TimeoutMs 的去向
            // 见 C8-4（串口真正能用上的超时是"读超时"）。
        }

        #endregion

        #region C8 读超时下发

        /// <summary>
        /// 「读超时(ms)」是 <see cref="ConnectionConfigBase.ReadTimeoutMs"/>，约束的是
        /// <b>每一次读写帧等对端回包</b>的时长，与"建立连接"是两件事：连接超时管"能不能连上"，
        /// 读超时管"连上之后每次读写能不能拿到答复"。
        /// <para>此前项目里这个值完全没人下发：所有连接的读超时都是 HSL 自己的默认 5000ms；
        /// 串口更是一个超时入口都没有（<see cref="ConnectionConfigBase.TimeoutMs"/> 在串口上不消费，
        /// 因为 Open 是本地动作、没有建连超时可言）。</para>
        /// <para>这里断言"配置值确实落到了设备对象上"。不真建连，所以离线可跑。</para>
        /// </summary>
        private static void ReadTimeoutPropagation()
        {
            Section("C8 读超时下发到 HSL 设备（config.ReadTimeoutMs → device.ReceiveTimeOut）");

            // 默认值路径：恰好与 HSL 默认值同值，故这条只证明"默认行为没被改坏"，
            // 真正证明"确实下发了"的是下面三条非默认值用例。
            using (var mtConn = new ModbusTcpConnection(new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502 }))
            {
                int actual = DeviceReceiveTimeOut(mtConn);
                Check("C8-1 ModbusTcp：默认读超时 5000 在设备上（与 HSL 默认同值，保证不填=旧行为）",
                    actual == 5000, $"设备实际值 {actual}ms");
            }

            // 非默认值路径：证明配置真的被下发，而不是一直吃 HSL 默认
            using (var mtConn = new ModbusTcpConnection(
                new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502, ReadTimeoutMs = 800 }))
            {
                int actual = DeviceReceiveTimeOut(mtConn);
                Check("C8-2 ModbusTcp：改成 800 如实下发（掉线设备能更快暴露，不必死等 5 秒）",
                    actual == 800, $"设备实际值 {actual}ms");
            }

            // 同一条基类字段，不允许只有 Modbus 生效；S7 建连的 COTP 握手读也吃这个值
            using (var s7Conn = new SiemensS7Connection(
                new SiemensS7Config { IpAddress = "192.168.0.1", ReadTimeoutMs = 2500 }))
            {
                int actual = DeviceReceiveTimeOut(s7Conn);
                Check("C8-3 SiemensS7：配置值同样下发（S7 建连的 COTP 握手读也受它约束）",
                    actual == 2500, $"设备实际值 {actual}ms");
            }

            // 串口：TimeoutMs 在串口上是死配置（Open 是本地动作），读超时是串口唯一能真正用上的超时
            using (var serialConn = new SerialConnection(new SerialConfig { PortName = "COM1", ReadTimeoutMs = 1500 }))
            {
                int actual = DeviceReceiveTimeOut(serialConn);
                Check("C8-4 串口：读超时同样下发（TimeoutMs 在串口无处可用，读超时才是它的实际超时）",
                    actual == 1500, $"设备实际值 {actual}ms");
            }
        }

        #endregion

        #region C9 串口隐藏无效的「连接超时」

        /// <summary>
        /// 串口不使用"连接超时"（<see cref="ConnectionConfigBase.TimeoutMs"/>）——HSL 的 <c>ModbusRtu.Open()</c>
        /// 只是打开本地串口句柄，没有网络建连等待，所以这个值在串口上改了没有任何反应。
        /// 既然无效，就不该在界面上占一个输入框误导用户。
        /// <para>做法：给 <see cref="SuperDisplayAttribute"/> 加 <see cref="SuperDisplayAttribute.Visible"/>
        /// （默认 true，老字段行为不变），<see cref="SerialConfig"/> 用 <c>override</c> 重贴
        /// <c>[SuperDisplay(Visible = false)]</c>。</para>
        /// <para>这里断言的是"模型层的显示声明正确"，不启 WPF 窗体，所以离线可跑；
        /// 真正的界面效果（框是否消失）仍需实机点验。</para>
        /// </summary>
        private static void SerialConnectTimeoutHidden()
        {
            Section("C9 串口隐藏无效的「超时时间(ms)」（SerialConfig.TimeoutMs → Visible = false）");

            // 串口：该字段应被隐藏
            var serialAttr = DisplayAttr(typeof(SerialConfig), nameof(ConnectionConfigBase.TimeoutMs));
            Check("C9-1 串口：TimeoutMs 声明为不显示（改了没反应的框不该留在界面上）",
                serialAttr != null && !serialAttr.Visible,
                $"Visible={serialAttr?.Visible.ToString() ?? "无 SuperDisplay 特性"}");

            // 串口：真正生效的「读超时」必须留着，别把有效的那个也一起摘掉
            var serialReadAttr = DisplayAttr(typeof(SerialConfig), nameof(ConnectionConfigBase.ReadTimeoutMs));
            Check("C9-2 串口：ReadTimeoutMs 仍显示（它是串口唯一真正生效的超时）",
                serialReadAttr != null && serialReadAttr.Visible,
                $"Visible={serialReadAttr?.Visible.ToString() ?? "无 SuperDisplay 特性"}");

            // 以太网：同一条基类字段必须照常显示，不能被"顺手一起隐藏"误伤
            var tcpAttr = DisplayAttr(typeof(ModbusTcpConfig), nameof(ConnectionConfigBase.TimeoutMs));
            Check("C9-3 ModbusTcp：TimeoutMs 仍显示（连接超时对以太网是有效配置，不能被误伤）",
                tcpAttr != null && tcpAttr.Visible,
                $"Visible={tcpAttr?.Visible.ToString() ?? "无 SuperDisplay 特性"}");

            // 结构断言：必须用 override（反射去重）而不是 new（基类/子类两份同名属性会被渲染两行）
            int dupCount = typeof(SerialConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Count(p => p.Name == nameof(ConnectionConfigBase.TimeoutMs));
            Check("C9-4 串口：TimeoutMs 在反射里只出现一次（必须 override 而非 new，否则字段会渲染两行）",
                dupCount == 1, $"同名属性数 {dupCount}");

            // 隐藏的只是"显示"，模型本身必须仍可读写，否则老方案里的值会在加载后丢失
            var serial = new SerialConfig { TimeoutMs = 4321 };
            Check("C9-5 串口：隐藏仅作用于界面，模型仍可读写（老方案里的值不会丢）",
                serial.TimeoutMs == 4321 && serial.Clone().TimeoutMs == 4321,
                $"本体 {serial.TimeoutMs} / 克隆 {serial.Clone().TimeoutMs}");
        }

        /// <summary>读属性上的 SuperDisplay 特性（断言"这个字段在属性面板里显不显示"）</summary>
        private static SuperDisplayAttribute? DisplayAttr(Type type, string propertyName)
            => type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?.GetCustomAttribute<SuperDisplayAttribute>();

        #endregion

        #region C10 串口字节序（读写两侧同源）

        /// <summary>
        /// 串口（Modbus RTU）历史上两侧字序不一致：写侧走 HSL 设备的 <c>ByteTransform</c>
        /// （<c>ModbusRtu</c> 构造函数里写死 <c>DataFormat.CDAB</c>），读侧走 <c>HslHelper.ConvertTo(…, ABCD)</c>——
        /// 于是 32/64 位"写进去、读不回来"（16 位因 ABCD 与 CDAB 在 HSL 里退化等价而无感）。
        /// <para>修复方式与 ModbusTcp 同源：<see cref="SerialConfig.ByteOrder"/> 一个值同时喂给写侧
        /// （下发 <c>ByteTransform</c>）与读侧（<c>ConvertTo</c>），从此不可能再对不上。</para>
        /// <para>断言全部走模型 / 反射，不打开串口、不接硬件，离线可跑。</para>
        /// </summary>
        private static void SerialByteOrderFollowsConfig()
        {
            Section("C10 串口字节序跟随配置（读写两侧同源）");

            // 1) 默认值必须是 ABCD：串口读侧的历史行为就是 ABCD，改默认会让存量连接的 32 位读数凭空翻字序
            var defaults = new SerialConfig();
            Check("C10-1 串口：ByteOrder 默认 ABCD（读侧历史行为，保住存量连接的读数不变）",
                defaults.ByteOrder == ByteOrderFormat.ABCD, $"默认值 {defaults.ByteOrder}");

            // 2) Clone 必须带上字序：ModbusTcpConfig 就漏拷过，克隆一份配置等于把设置丢了（同一个坑）
            var cloned = (SerialConfig)new SerialConfig { ByteOrder = ByteOrderFormat.CDAB }.Clone();
            Check("C10-2 串口：Clone 带上字序（漏拷的话克隆一份配置就把设置丢了）",
                cloned.ByteOrder == ByteOrderFormat.CDAB, $"克隆后 {cloned.ByteOrder}");

            // 3) 连接读出的字序要跟随配置，不能写死常量（写死等于配置摆着看）
            using (var conn = new SerialConnection(new SerialConfig { PortName = "COM1", ByteOrder = ByteOrderFormat.CDAB }))
            {
                Check("C10-3 串口：连接的字序跟随配置（写死常量就等于配置摆着看）",
                    conn.ByteOrder == ByteOrderFormat.CDAB, $"连接读出的字序 {conn.ByteOrder}");
            }

            // 4) 配 CDAB：写侧必须真的下发（HSL 默认恰好也是 CDAB，所以这条只能证明"没被改坏"）
            using (var conn = new SerialConnection(new SerialConfig { PortName = "COM1", ByteOrder = ByteOrderFormat.CDAB }))
            {
                string actual = DeviceByteTransformFormat(conn);
                Check("C10-4 串口：配 CDAB 时写侧 DataFormat 是 CDAB（HSL 默认值，证明没被改坏）",
                    actual == "CDAB", $"设备实际 DataFormat {actual}");
            }

            // 5) 配 ABCD：这条才是关键——HSL 默认是 CDAB，只有"真的下发了"才会变成 ABCD；
            //    没有这条，只测 CDAB 是测不出"根本没下发"的
            using (var conn = new SerialConnection(new SerialConfig { PortName = "COM1", ByteOrder = ByteOrderFormat.ABCD }))
            {
                string actual = DeviceByteTransformFormat(conn);
                Check("C10-5 串口：配 ABCD 时写侧确实改成 ABCD（只测 CDAB 测不出「根本没下发」）",
                    actual == "ABCD", $"设备实际 DataFormat {actual}");
            }
        }

        #endregion

        #region C11 S7 读长度取自字节表（而非 Marshal.SizeOf）

        /// <summary>
        /// S7 单点直读（<see cref="SiemensS7Connection.Read{T}"/>）原先用 <c>Marshal.SizeOf&lt;T&gt;()</c>
        /// 算设备读长度，而它**对 bool 返回 4**（Win32 <c>BOOL</c> 的尺寸，不是 1）。
        /// 于是 <c>Read&lt;bool&gt;("M10.0")</c> 向设备要 4 个字节：
        /// 多读相邻 3 个字节；若位地址靠近区末还会越界读失败。
        /// 值本身常常"碰巧对"（<c>ConvertTo&lt;bool&gt;</c> 只取首字节），所以这个错藏得住。
        /// <para>修复方式：长度改从 <c>HslHelper.MinByteCount</c> 取——它与轮询批量解码
        /// （<c>PollBatchPlanner</c>）用的是**同一张字节表**，单点读与批量读从此不可能对不上。</para>
        /// <para>断言全部走反射调 internal 的 <c>HslHelper</c>，不接硬件、离线可跑。</para>
        /// </summary>
        private static void S7ByteCountFollowsHelperTable()
        {
            Section("C11 S7 读长度取自字节表（bool 必须 1 字节）");

            // 1) 核心断言：修复目标就是这个值。它是 4 的话，Read<bool> 会向设备多要 3 个字节
            int boolBytes = HslHelperMinByteCount(typeof(bool));
            Check("C11-1 字节表：bool → 1（S7 单点读 bool 只能要 1 字节）",
                boolBytes == 1, $"实际 {boolBytes} 字节");

            // 2) 整表核对：只要有一个值错，对应类型的单点读就会多读/少读字节
            var expected = new (Type Type, int Bytes)[]
            {
                (typeof(byte), 1), (typeof(sbyte), 1),
                (typeof(short), 2), (typeof(ushort), 2),
                (typeof(int), 4), (typeof(uint), 4), (typeof(float), 4),
                (typeof(long), 8), (typeof(ulong), 8), (typeof(double), 8),
            };
            var wrong = expected.Where(e => HslHelperMinByteCount(e.Type) != e.Bytes)
                .Select(e => $"{e.Type.Name}={HslHelperMinByteCount(e.Type)}(应 {e.Bytes})").ToList();
            Check("C11-2 字节表：定长数值类型整表正确（byte1/short2/int4/long8/float4/double8）",
                wrong.Count == 0, wrong.Count == 0 ? "10 个类型全对" : string.Join("，", wrong));

            // 3) 未知类型返回 0：Read<T> 靠这个 0 走 Marshal.SizeOf 回退，保住旧行为（不抛异常）
            int decimalBytes = HslHelperMinByteCount(typeof(decimal));
            Check("C11-3 字节表：未知类型（decimal）返回 0 —— Read<T> 靠它走回退分支",
                decimalBytes == 0, $"实际 {decimalBytes}");

            // 4) 哨兵：证明"为什么必须换掉 Marshal.SizeOf"。哪天 .NET 改了 bool 的封送尺寸，
            //    这条会失败，提醒我们重新审视第 1 条的注释是否还成立
            int marshalBool = System.Runtime.InteropServices.Marshal.SizeOf<bool>();
            Check("C11-4 哨兵：Marshal.SizeOf<bool>() 仍是 4（旧写法确实会多读 3 字节）",
                marshalBool == 4, $"实际 {marshalBool}");

            // 5) 回退分支可达性：decimal 在字节表里是 0、在 Marshal 里非 0，
            //    两者组合证明"0 → 回退"这条路径有意义（不是死代码）
            Check("C11-5 回退分支：decimal 字节表为 0 且 Marshal 非 0（回退不是死代码）",
                decimalBytes == 0 && System.Runtime.InteropServices.Marshal.SizeOf<decimal>() > 0,
                $"字节表 {decimalBytes} / Marshal {System.Runtime.InteropServices.Marshal.SizeOf<decimal>()}");
        }

        #endregion

        #region C12 坏段不再静默（连续失败计数 + 旁路健康通道）

        /// <summary>
        /// A2 回归：<c>PollBatchPlanner.Poll</c> 的**单个布尔返回值**同时承担了两个职责——
        /// ①"链路是否还活着"（消费者是 Worker 的断线重连判定）；②"采集是否健康"。
        /// 判定式 <c>failed &lt; attempted</c> 把二者混成一个值：只要有一条段读成功就返回 true。
        /// <para>这是**故意的**：若改成"全部成功才算成功"，一个坏地址就会把整条连接打进
        /// 无限重连循环（连上→轮询→有一项失败→判链路死→断开重连→…），把"一批点偶发黄"
        /// 升级成"所有点全灰"。但"个别段恒坏"也不能就这么静默——它会让变量永久停在黄点而无人在意。</para>
        /// <para>故本组断言钉住两件事：</para>
        /// <para>1) 返回值语义**一个字没变**（部分失败仍 true；全失败仍 false）；</para>
        /// <para>2) 坏段另走一条**旁路健康通道**：段级连续失败计数达阈值即计入诊断快照，
        /// 并在跨阈值那一刻发一条**不节流**告警（限流日志会被 5 秒窗口吞掉，靠它上报等于没报）。</para>
        /// <para>用 <see cref="FaultyConnection"/> 桩按地址注入失败，不接硬件、离线可跑。</para>
        /// </summary>
        private static void FaultedSegmentHealth()
        {
            Section("C12 坏段不再静默（连续失败计数 + 旁路健康通道）");

            var cfg = MakeConfig(1000);

            // 两个地址相隔 200 个寄存器 → 超过以太网档的间隙容忍（= 单请求上限 120）→ 必然拆成 2 段，可分别注入失败。
            // ⚠ 必须 > 120：早期用 100 是"串口档阈值 8"下调出来的数；放宽阈值后 100 < 120 会被并成 1 段，
            // 本组"可分别注入失败"的前提就塌了。
            var goodVar = MakeVar("GOOD", "", 0);
            var badVar = MakeVar("BAD", "", 200);
            string badAddress = badVar.Address; // 段地址 = SegmentPrefix + Start，与变量地址同串

            var logs = new List<string>();
            var sched = PollScheduler.Create(new[] { goodVar, badVar }, cfg, logs.Add)!;

            Check("C12-0 前提：两个远隔地址拆成 2 段（可分别注入失败）",
                sched.TotalSegments == 2 && sched.TotalFallbacks == 0, sched.Describe());

            // 坏地址可控开关：先坏后好，用于验证"成功一轮即归零"
            bool addressIsBad = true;
            using var conn = new FaultyConnection("PLC1", addr => addressIsBad && addr == badAddress);

            int FaultedCount() => sched.GetStats()[0].FaultedSegmentCount;
            string? FaultDetail() => sched.GetStats()[0].FaultDetail;
            int FaultLogs() => logs.Count(l => l.Contains("异常段"));

            // ---- 第 1 轮：1 段成功 + 1 段失败 ----
            sched.ResetAllDue();
            bool round1 = sched.Run(conn);

            Check("C12-1 部分段失败 → Poll 仍返回 true（不误判为链路故障 → 不会无限重连）",
                round1, $"Run 返回 {round1}");
            Check("C12-2 坏段变量质量 = Uncertain（不是 Bad：值保留旧值，黄点提示最近一次读失败）",
                badVar.Quality == VariableQuality.Uncertain, $"实际 {badVar.Quality}");
            Check("C12-2 好段变量质量 = Good（同一轮里互不牵连）",
                goodVar.Quality == VariableQuality.Good, $"实际 {goodVar.Quality}");
            Check("C12-2 连续失败 1 轮 < 阈值 3 → 还不算异常段（单轮抖动不该报警）",
                FaultedCount() == 0 && FaultDetail() == null,
                $"异常段 {FaultedCount()} / 摘要 {FaultDetail() ?? "null"}");

            // ---- 第 2 轮：计数继续累计 ----
            sched.ResetAllDue();
            sched.Run(conn);
            Check("C12-3 连续失败按轮累计：2 轮仍 < 阈值 3（仍不算异常段）",
                FaultedCount() == 0, $"异常段 {FaultedCount()}");

            // ---- 第 3 轮：跨阈值那一刻 ----
            sched.ResetAllDue();
            sched.Run(conn);
            Check("C12-4 连续失败达阈值 3 → 计为 1 个异常段",
                FaultedCount() == 1, $"异常段 {FaultedCount()}");
            Check("C12-4 异常段摘要含地址与连续次数（面板 ToolTip 直接可读）",
                FaultDetail() != null && FaultDetail()!.Contains(badAddress) && FaultDetail()!.Contains("3 轮"),
                FaultDetail() ?? "null");
            Check("C12-4 跨阈值那一刻发一条不节流告警（走 LogThrottled 会被 5 秒窗口吞掉）",
                FaultLogs() == 1, $"告警 {FaultLogs()} 条：{string.Join(" || ", logs)}");

            // ---- 第 4 轮：仍然坏 → 计数继续涨，但不重复告警 ----
            sched.ResetAllDue();
            sched.Run(conn);
            Check("C12-4 持续坏 → 计数继续累加（4 轮），告警不重复刷（仍只有 1 条）",
                FaultedCount() == 1 && FaultDetail()!.Contains("4 轮") && FaultLogs() == 1,
                $"{FaultDetail()} / 告警 {FaultLogs()} 条");

            // ---- 第 5 轮：地址恢复 ----
            addressIsBad = false;
            sched.ResetAllDue();
            bool round5 = sched.Run(conn);
            Check("C12-3 成功一轮即归零：异常段消失（不需要人工复位）",
                round5 && FaultedCount() == 0 && FaultDetail() == null,
                $"异常段 {FaultedCount()} / 摘要 {FaultDetail() ?? "null"}");

            // ---- 第 6/7 轮：再次坏，计数从 1 重新开始 ----
            // 若"成功归零"没生效，计数会停在 4 并继续涨，这两轮一跑就已 ≥ 阈值
            addressIsBad = true;
            sched.ResetAllDue();
            sched.Run(conn);
            sched.ResetAllDue();
            sched.Run(conn);
            Check("C12-3 归零后重新计数：再坏 2 轮仍 < 阈值（未归零的话此刻早已是异常段）",
                FaultedCount() == 0, $"异常段 {FaultedCount()}");

            // ---- 全段失败：原语义必须没被改坏 ----
            using var allBad = new FaultyConnection("PLC1", _ => true);
            sched.ResetAllDue();
            bool allFail = sched.Run(allBad);
            Check("C12-5 全段失败 → Poll 返回 false（通信级故障语义未被改坏，仍触发重连）",
                !allFail, $"Run 返回 {allFail}");
        }

        /// <summary>
        /// 测试桩：按地址注入读失败，其余地址正常返回。
        /// <para>为什么不直接用真 <c>ModbusTcpConnection</c>：本组断言要验的是"规划器怎么消化失败"，
        /// 不是"设备怎么失败"。接模拟器会让断言依赖外部进程（CommTest 踩过的坑），
        /// 且做不到"同一个地址按需坏/好"——而归零与重新计数这两条恰恰需要中途翻转。</para>
        /// </summary>
        private sealed class FaultyConnection : ICommunicationConnection
        {
            private readonly Func<string, bool> _isBad;

            public FaultyConnection(string connectionName, Func<string, bool> isBad)
            {
                ConnectionName = connectionName;
                _isBad = isBad;
            }

            public string ConnectionName { get; }

            public CommunicationType Type => CommunicationType.ModbusTcp;

            public bool IsConnected { get; private set; }

            /// <summary>Modbus 才有多寄存器字序概念；桩固定 ABCD（与配置默认一致）</summary>
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect()
            {
                IsConnected = true;
                return true;
            }

            public void Disconnect() => IsConnected = false;

            public bool TestConnection() => IsConnected;

            public void Dispose() => Disconnect();

            public T Read<T>(string address) where T : struct
                => throw new NotSupportedException("桩不支持单点读：本组断言只走段读路径");

            public void Write(string address, object value)
            {
            }

            public byte[] ReadBytes(string address, ushort length)
            {
                if (_isBad(address)) throw new InvalidOperationException($"模拟设备拒绝该地址：{address}");

                // Modbus 寄存器区 1 单元 = 2 字节：必须给足，否则规划器会按"字节不足"另记一次失败，
                // 干扰本组要验的"段读异常"这条路径
                return new byte[length * 2];
            }

            public void WriteBytes(string address, byte[] data)
            {
            }

            public bool[] ReadBits(string address, ushort count)
            {
                if (_isBad(address)) throw new InvalidOperationException($"模拟设备拒绝该地址：{address}");
                return new bool[count];
            }
        }

        #endregion

        #region C19 轮询派发异常隔离（F1）

        /// <summary>
        /// <para>F1：<c>PollBatchPlanner</c> 的"解码"与"派发（UpdateValue → 下游订阅者）"必须分开兜底。</para>
        /// <para>旧实现共用一个 try：下游订阅者（UI 绑定 / SCADA / 报警）抛异常时，值其实已经读到并写进变量，
        /// 变量却被 <c>MarkUncertain()</c>（UI 黄点"读取失败"）并记一条误导性的"解码失败"日志。</para>
        /// <para>断言用桩连接 + 一个故意抛异常的 <c>ValueChanged</c> 订阅者，覆盖段读路径（<c>ApplyBytes</c>）。</para>
        /// </summary>
        private static void PublishFailureIsolation()
        {
            Section("C19 轮询派发异常隔离（F1：下游订阅者抛异常不污染质量戳）");

            var cfg = MakeConfig(1000);
            var v1 = MakeVar("V1", "", 0);
            var v2 = MakeVar("V2", "", 1);   // 相邻寄存器 → 与 V1 同段，一次段读覆盖两个变量
            var logs = new List<string>();
            var sched = PollScheduler.Create(new[] { v1, v2 }, cfg, logs.Add)!;

            Check("C19-0 前提：两个相邻地址合并为 1 段（同一次段读，异常隔离可对照）",
                sched.TotalSegments == 1 && sched.TotalFallbacks == 0, sched.Describe());

            v1.ValueChanged += (_, _) => throw new InvalidOperationException("模拟下游订阅者异常");

            using var conn = new FaultyConnection("PLC1", _ => false);
            sched.ResetAllDue();
            bool ok = sched.Run(conn);

            Check("C19-1 派发异常不影响 Poll 返回值（通信本身成功，不该触发重连）", ok, $"Run 返回 {ok}");
            Check("C19-2 派发异常不把变量降级为 Uncertain（值已读到，质量戳必须保持 Good）",
                v1.Quality == VariableQuality.Good, $"实际 {v1.Quality}");
            Check("C19-3 值已更新（异常发生在派发阶段，写值先于回调）",
                v1.CurrentValue is short s && s == 0, $"当前值 {v1.CurrentValue ?? "null"}");
            Check("C19-4 同段其它变量不受牵连（异常隔离到单个变量）",
                v2.Quality == VariableQuality.Good && v2.CurrentValue != null,
                $"V2 质量 {v2.Quality} / 值 {v2.CurrentValue ?? "null"}");
            Check("C19-5 日志不误报「解码失败」，而是指向下游处理",
                logs.All(l => !l.Contains("解码失败")) && logs.Any(l => l.Contains("下游处理异常")),
                string.Join(" || ", logs));
        }

        #endregion

        #region C20 值与时间戳发布配对（F2）

        /// <summary>
        /// F2：<c>CommunicationVariable</c> 的值 / 时间戳 / 质量戳跨线程读写必须成对发布。
        /// <para>旧实现用自动属性：<c>DateTime</c>（8 字节 struct）读写可撕裂，且"值"与"时间戳"之间没有发布顺序，
        /// UI 会读到"新值 + 旧时间戳"这种不属于同一轮的组合。</para>
        /// <para>断言：并发写者持续 <c>UpdateValue</c>，读者按"先时间戳、后值"的顺序读——
        /// 时间戳非默认即必须读到非 null 值（写者先发布值、再写时间戳）。</para>
        /// </summary>
        private static void ValueTimestampPublication()
        {
            Section("C20 值与时间戳发布配对（F2：并发读不到撕裂组合）");

            var v = new CommunicationVariable();
            Check("C20-0 初始状态：值 null / 时间戳 default / 质量 Bad",
                v.CurrentValue == null && v.LastUpdateTime == default && v.Quality == VariableQuality.Bad,
                $"值 {v.CurrentValue ?? "null"} / 时间戳 {v.LastUpdateTime:O} / 质量 {v.Quality}");

            v.UpdateValue((short)42);
            Check("C20-1 更新后三项同步可见（值 / 时间戳 / 质量）",
                v.CurrentValue is short s && s == 42 && v.LastUpdateTime != default && v.Quality == VariableQuality.Good,
                $"值 {v.CurrentValue ?? "null"} / 时间戳 {v.LastUpdateTime:O} / 质量 {v.Quality}");

            const int rounds = 200_000;
            var gate = new ManualResetEventSlim(false);
            bool violated = false;

            var writer = Task.Run(() =>
            {
                gate.Wait();
                for (int i = 0; i < rounds; i++)
                    v.UpdateValue((short)i);
            });
            var reader = Task.Run(() =>
            {
                gate.Wait();
                // 一直读到写者收工：保证采样窗口覆盖整个并发期
                while (!writer.IsCompleted && !violated)
                {
                    var stamp = v.LastUpdateTime;
                    var value = v.CurrentValue;
                    if (stamp != default && value == null)
                        violated = true;
                }
            });

            gate.Set();
            Task.WaitAll(writer, reader);

            Check($"C20-2 并发读写（写 {rounds:N0} 轮）：时间戳非默认 ⇒ 值必非 null（发布配对无撕裂）",
                !violated, violated ? "抓到「时间戳已更新但值为 null」的撕裂组合" : "未观察到撕裂");
        }

        #endregion

        #region C21 热路径解码（P-A3）

        /// <summary>
        /// P-A3：<c>HslHelper</c> 必须提供 <c>ReadOnlySpan&lt;byte&gt;</c> 版解码入口（热路径零中间数组），
        /// 且新实现与旧实现的解码结果逐值等价（16 位退化、四种字序）。
        /// </summary>
        private static void SpanDecodeEquivalence()
        {
            Section("C21 热路径解码零分配重载（P-A3：Span 入口 + 行为等价）");

            var helper = typeof(AdvancedCommunicationManager).Assembly
                .GetType("VisionMaster.Communications.HslHelper", throwOnError: false);
            Check("C21-0 前提：能定位 internal HslHelper 类型（取不到就红，不静默跳过）",
                helper != null, helper?.FullName ?? "未找到 VisionMaster.Communications.HslHelper");
            if (helper == null) return;

            var convertMethods = helper.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            bool hasSpanConvert = convertMethods.Any(m =>
                m.Name == "ConvertTo"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsByRefLike);
            bool hasSpanByType = convertMethods.Any(m =>
                m.Name == "ConvertToByType"
                && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType.IsByRefLike);

            Check("C21-1 ConvertTo 存在 ReadOnlySpan<byte> 重载（不再 Clone + new byte[]）", hasSpanConvert,
                string.Join("；", convertMethods.Where(m => m.Name == "ConvertTo").Select(DescribeParams)));
            Check("C21-2 ConvertToByType 存在 ReadOnlySpan<byte> 重载（段内切片直接解码）", hasSpanByType,
                string.Join("；", convertMethods.Where(m => m.Name == "ConvertToByType").Select(DescribeParams)));

            // 行为等价：byte[] 重载现在委托给 Span 版，逐值对照手算期望
            var byType = helper.GetMethod("ConvertToByType",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(byte[]), typeof(Type), typeof(ByteOrderFormat) }, null);
            Check("C21-3 能取到 byte[] 版 ConvertToByType（行为等价测试入口）",
                byType != null, byType?.ToString() ?? "null");
            if (byType == null) return;

            var cases = new (byte[] Wire, ByteOrderFormat Order, Type Type, object Expected)[]
            {
                (new byte[] { 0x12, 0x34 }, ByteOrderFormat.ABCD, typeof(short), (short)0x1234),
                (new byte[] { 0x12, 0x34 }, ByteOrderFormat.BADC, typeof(short), (short)0x3412),
                (new byte[] { 0x12, 0x34 }, ByteOrderFormat.CDAB, typeof(short), (short)0x1234),   // 16 位退化
                (new byte[] { 0x12, 0x34 }, ByteOrderFormat.DCBA, typeof(short), (short)0x3412),   // 16 位退化
                (new byte[] { 0x12, 0x34, 0x56, 0x78 }, ByteOrderFormat.ABCD, typeof(int), 0x12345678),
                (new byte[] { 0x12, 0x34, 0x56, 0x78 }, ByteOrderFormat.BADC, typeof(int), unchecked((int)0x34127856)),
                (new byte[] { 0x12, 0x34, 0x56, 0x78 }, ByteOrderFormat.CDAB, typeof(int), 0x56781234),
                (new byte[] { 0x12, 0x34, 0x56, 0x78 }, ByteOrderFormat.DCBA, typeof(int), unchecked((int)0x78563412)),
                (new byte[] { 0x3F, 0x80, 0x00, 0x00 }, ByteOrderFormat.ABCD, typeof(float), 1.0f),
                (new byte[] { 0x00, 0x00, 0x80, 0x3F }, ByteOrderFormat.DCBA, typeof(float), 1.0f),
            };

            int mismatch = 0;
            string firstMismatch = string.Empty;
            foreach (var c in cases)
            {
                object? actual = byType.Invoke(null, new object[] { c.Wire, c.Type, c.Order });
                if (!Equals(actual, c.Expected))
                {
                    mismatch++;
                    if (firstMismatch.Length == 0)
                        firstMismatch = $"{c.Type.Name}/{c.Order}: 期望 {c.Expected}，实际 {actual ?? "null"}";
                }
            }

            Check($"C21-4 解码行为等价：{cases.Length} 组（4 种字序 × short/int/float）逐值一致",
                mismatch == 0, mismatch == 0 ? "全部一致" : $"{mismatch} 组不一致；首个：{firstMismatch}");
        }

        /// <summary>
        /// C22：通讯数组与地址偏移。
        /// <para>数组第一期只做数值数组只读，但"只读"不等于"只加一个类型"——
        /// 解码、内容比较、跨度上限、方案落盘四件事任何一件漏了都会静默坏：
        /// 解码漏了数组永远为空；内容比较漏了每轮都推 ValueChanged；跨度上限漏了建完永远读不到；
        /// Length 不落盘则重开方案数组变回标量。</para>
        /// </summary>
        private static void NetworkArrayDecodeAndOffset()
        {
            Section("C22 通讯数组与地址偏移（数值数组解码 / 40001 写法 / 内容比较 / 持久化）");

            // C22-1~3：40001 写法 + 地址偏移 → 0 基有效地址
            var legacy = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int32,
                Offset = "40001",
                UseLegacyAddress = true,
                AddressOffset = 2,
                Length = 3
            };
            Check("C22-1 40001 写法 + 偏移 2 → x=3;2", legacy.Address == "x=3;2", legacy.Address);
            bool legacyPollOk = legacy.TryCreatePollAddress(out var legacyPoll);
            Check("C22-2 40001 写法 + 偏移 2 的轮询起点 = 2、跨度 = 6 寄存器",
                legacyPollOk && legacyPoll!.Start == 2 && legacyPoll.SpanUnits == 6,
                legacyPoll == null ? "null" : $"Start={legacyPoll.Start} Span={legacyPoll.SpanUnits}");
            Check("C22-3 40001 写法与存储区匹配时 Validate 通过", legacy.Validate().IsValid, legacy.Validate().ErrorMessage);

            // C22-4：开关关闭时保持 0 基字面值（旧数据零影响）
            var literal = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int32,
                Offset = "40001"
            };
            Check("C22-4 40001 写法关闭 → 仍按 0 基字面值 x=3;40001", literal.Address == "x=3;40001", literal.Address);

            // C22-5：写法与存储区不匹配必须红（不静默读错位置）
            var mismatched = new ModbusAddress
            {
                Area = ModbusArea.InputRegisters,
                DataType = DataValueType.Int32,
                Offset = "40001",
                UseLegacyAddress = true
            };
            var mismatch = mismatched.Validate();
            Check("C22-5 40001 写法与存储区不匹配 → Validate 失败", !mismatch.IsValid, mismatch.ErrorMessage);

            // C22-6：规划器把 3 个连续 Int32 解成 int[3]
            var arrayAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int32,
                Offset = "0",
                Length = 3
            };
            var arrayVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "Arr",
                Address = arrayAddr.Address,
                AddressConfig = arrayAddr,
                ValueType = typeof(int[]).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var arrayPlanner = PollBatchPlanner.Build(new[] { arrayVar }, PollTransportKind.Ethernet);
            var arrayConn = new ArrayBytesConnection(new byte[]
            {
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x02,
                0x00, 0x00, 0x00, 0x03
            });
            bool arrayOk = arrayPlanner.TryReadOne(arrayConn, out var arrayValue, out var arrayError);
            var ints = arrayValue as int[];
            Check("C22-6 规划器把 3 个连续 Int32 解成 int[3]（值 1/2/3）",
                arrayOk && ints is { Length: 3 } && ints[0] == 1 && ints[1] == 2 && ints[2] == 3,
                arrayOk
                    ? (ints == null ? "null" : string.Join(",", ints))
                    : arrayError ?? "null");

            // C22-7：数组按内容比较——同值不推送，变值推送一次
            var changeVar = new CommunicationVariable();
            int events = 0;
            changeVar.ValueChanged += (_, _) => events++;
            changeVar.UpdateValue(new[] { 1, 2, 3 });
            changeVar.UpdateValue(new[] { 1, 2, 3 });
            int afterSame = events;
            changeVar.UpdateValue(new[] { 1, 2, 4 });
            Check("C22-7 数组按内容比较：同值不推送、变值推送一次", afterSame == 1 && events == 2,
                $"同值后 {afterSame} 次，变值后 {events} 次");

            // C22-8：超过单请求上限 → 规划器跳过并留日志（第一期不做分块）
            var tooLongAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int32,
                Offset = "0",
                Length = 61
            };
            var tooLongVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "TooLong",
                Address = tooLongAddr.Address,
                AddressConfig = tooLongAddr,
                ValueType = typeof(int[]).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var logs = new List<string>();
            var tooLongPlanner = PollBatchPlanner.Build(new[] { tooLongVar }, PollTransportKind.Ethernet, logs.Add);
            Check("C22-8 61 个 Int32 超过 Modbus 单段上限 → 不参与轮询且日志说明原因",
                tooLongPlanner.PollItemCount == 0 && logs.Any(l => l.Contains("超过单请求上限", StringComparison.Ordinal)),
                logs.Count == 0 ? "无日志" : logs[0]);

            // C22-9：方案落盘往返保留元素个数/地址偏移/40001 写法
            var nv = new NetworkVariableModel
            {
                Name = "Arr",
                DataType = typeof(int[]),
                ConnectionName = "PLC1",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "40001",
                    UseLegacyAddress = true,
                    AddressOffset = 2,
                    Length = 3
                }
            };
            var dto = VariableDto.FromNetwork(nv, CommunicationType.ModbusTcp);
            var restored = dto.BuildAddressConfig() as ModbusAddress;
            Check("C22-9 方案落盘往返保留元素个数/地址偏移/40001 写法",
                dto.Length == 3 && dto.AddressOffset == 2 && dto.UseLegacyAddress
                && restored != null && restored.Length == 3 && restored.AddressOffset == 2
                && restored.UseLegacyAddress && restored.Address == "x=3;2",
                $"dto: Length={dto.Length} Offset={dto.AddressOffset} Legacy={dto.UseLegacyAddress}；restored: {restored?.Address ?? "null"}");
        }

        /// <summary>
        /// C23：字符串标量 / 字符串数组 / bool 数组解码。
        /// <para>这三类与数值数组的差别都在"宽度"：字符串标量用 Length 当总字节数，
        /// 字符串数组要 Length（元素个数）+ ElementByteLength（每元素宽度）两个数，
        /// bool 数组则按位区点数走。任何一处宽度口径错了，读回来就是截断/乱码/错位。</para>
        /// </summary>
        private static void NetworkStringAndBoolArrayDecode()
        {
            Section("C23 字符串 / bool 数组解码（定长宽度与位区语义）");

            // C23-1：字符串标量（Length = 总字节数 20）
            var strAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.String,
                Offset = "0",
                Length = 20
            };
            var strVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "Text",
                Address = strAddr.Address,
                AddressConfig = strAddr,
                ValueType = typeof(string).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var strPlanner = PollBatchPlanner.Build(new[] { strVar }, PollTransportKind.Ethernet);
            var strData = new byte[20];
            Encoding.ASCII.GetBytes("ABC").CopyTo(strData, 0);
            var strConn = new ArrayBytesConnection(strData);
            bool strOk = strPlanner.TryReadOne(strConn, out var strValue, out var strError);
            Check("C23-1 字符串标量按定长 20 字节解码并去掉尾部 \\0",
                strOk && Equals(strValue, "ABC"),
                strOk ? $"值=[{strValue}]" : strError ?? "null");

            // C23-2：字符串数组（2 项 × 每项 8 字节）
            var strArrAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.String,
                Offset = "0",
                Length = 2,
                ElementByteLength = 8
            };
            var strArrVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "Texts",
                Address = strArrAddr.Address,
                AddressConfig = strArrAddr,
                ValueType = typeof(string[]).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var strArrPlanner = PollBatchPlanner.Build(new[] { strArrVar }, PollTransportKind.Ethernet);
            var strArrData = new byte[16];
            Encoding.ASCII.GetBytes("ABC").CopyTo(strArrData, 0);
            Encoding.ASCII.GetBytes("DEF").CopyTo(strArrData, 8);
            var strArrConn = new ArrayBytesConnection(strArrData);
            bool strArrOk = strArrPlanner.TryReadOne(strArrConn, out var strArrValue, out var strArrError);
            var strings = strArrValue as string[];
            Check("C23-2 字符串数组按 2 项 × 8 字节切分（ABC / DEF）",
                strArrOk && strings is { Length: 2 } && strings[0] == "ABC" && strings[1] == "DEF",
                strArrOk ? (strings == null ? "null" : string.Join("|", strings)) : strArrError ?? "null");

            // C23-3：bool 数组（Modbus 线圈，3 点）
            var bitAddr = new ModbusAddress
            {
                Area = ModbusArea.Coils,
                DataType = DataValueType.Boolean,
                Offset = "0",
                Length = 3
            };
            var bitVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "Flags",
                Address = bitAddr.Address,
                AddressConfig = bitAddr,
                ValueType = typeof(bool[]).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var bitPlanner = PollBatchPlanner.Build(new[] { bitVar }, PollTransportKind.Ethernet);
            var bitConn = new ArrayBitsConnection(new[] { true, false, true });
            bool bitOk = bitPlanner.TryReadOne(bitConn, out var bitValue, out var bitError);
            var bits = bitValue as bool[];
            Check("C23-3 bool 数组按线圈 3 点解码（True / False / True）",
                bitOk && bits is { Length: 3 } && bits[0] && !bits[1] && bits[2],
                bitOk ? (bits == null ? "null" : string.Join(",", bits)) : bitError ?? "null");

            // C23-4：字符串数组缺 ElementByteLength → 规划器明确跳过并留日志
            var missingWidthAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.String,
                Offset = "0",
                Length = 2
            };
            var missingWidthVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "BadTexts",
                Address = missingWidthAddr.Address,
                AddressConfig = missingWidthAddr,
                ValueType = typeof(string[]).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
            var missingLogs = new List<string>();
            var missingPlanner = PollBatchPlanner.Build(
                new[] { missingWidthVar }, PollTransportKind.Ethernet, missingLogs.Add);
            Check("C23-4 字符串数组缺每元素字节数 → 不参与轮询且日志说明原因",
                missingPlanner.PollItemCount == 0
                && missingLogs.Any(l => l.Contains("每元素字节数", StringComparison.Ordinal)),
                missingLogs.Count == 0 ? "无日志" : missingLogs[0]);

            // C23-5：方案落盘往返保留每元素字节数
            var nv = new NetworkVariableModel
            {
                Name = "Texts",
                DataType = typeof(string[]),
                ConnectionName = "PLC1",
                AddressConfig = strArrAddr
            };
            var dto = VariableDto.FromNetwork(nv, CommunicationType.ModbusTcp);
            var restored = dto.BuildAddressConfig() as ModbusAddress;
            Check("C23-5 方案落盘往返保留 ElementByteLength（8 字节）",
                dto.ElementByteLength == 8 && restored != null && restored.ElementByteLength == 8
                && restored.Length == 2,
                $"dto: Length={dto.Length} Width={dto.ElementByteLength}；restored: {restored?.ElementByteLength}");
        }

        /// <summary>
        /// C24：数组写入前的归一化与定长编码（不碰 ICommunicationManager，直接打私有纯函数）。
        /// <para>写链路真正发设备那一步由 Manager/Connection 负责；这里钉住的是最容易错、
        /// 也最容易静默坏的两段：元素类型/长度归一、字符串定长补零。</para>
        /// </summary>
        private static void NetworkArrayWriteEncoding()
        {
            Section("C24 数组写入编码（元素归一 / 定长补零 / 超长拒绝）");

            var normalize = typeof(NetworkVariableModel).GetMethod(
                "TryNormalizeArray", BindingFlags.NonPublic | BindingFlags.Instance);
            Check("C24-0 前提：能定位 NetworkVariableModel.TryNormalizeArray",
                normalize != null, normalize?.ToString() ?? "未找到");

            if (normalize != null)
            {
                var model = new NetworkVariableModel
                {
                    Name = "Arr",
                    DataType = typeof(int[]),
                    AddressConfig = new ModbusAddress
                    {
                        Area = ModbusArea.HoldingRegisters,
                        DataType = DataValueType.Int32,
                        Offset = "0",
                        Length = 3
                    }
                };

                object?[] goodArgs = { new object[] { "1", 2, 3 }, null, null };
                bool good = (bool)normalize.Invoke(model, goodArgs)!;
                var normalized = goodArgs[1] as int[];
                Check("C24-1 元素归一：object[] { \"1\", 2, 3 } → int[3]",
                    good && normalized is { Length: 3 } && normalized[0] == 1 && normalized[1] == 2 && normalized[2] == 3,
                    good ? (normalized == null ? "null" : string.Join(",", normalized)) : goodArgs[2] as string ?? "null");

                object?[] shortArgs = { new[] { 1, 2 }, null, null };
                bool shortOk = (bool)normalize.Invoke(model, shortArgs)!;
                Check("C24-2 长度不一致 → 明确失败（不短写覆盖邻居）",
                    !shortOk && (shortArgs[2] as string)?.Contains("不一致", StringComparison.Ordinal) == true,
                    shortArgs[2] as string ?? "null");

                var boolModel = new NetworkVariableModel
                {
                    Name = "Flags",
                    DataType = typeof(bool[]),
                    AddressConfig = new ModbusAddress
                    {
                        Area = ModbusArea.Coils,
                        DataType = DataValueType.Boolean,
                        Offset = "0",
                        Length = 3
                    }
                };
                object?[] boolArgs = { new object[] { "1", "0", "true" }, null, null };
                bool boolOk = (bool)normalize.Invoke(boolModel, boolArgs)!;
                var bools = boolArgs[1] as bool[];
                Check("C24-3 bool 元素归一：\"1\"/\"0\"/\"true\" → True/False/True",
                    boolOk && bools is { Length: 3 } && bools[0] && !bools[1] && bools[2],
                    boolOk ? (bools == null ? "null" : string.Join(",", bools)) : boolArgs[2] as string ?? "null");
            }

            var encodeString = typeof(NetworkVariableModel).GetMethod(
                "TryEncodeFixedString", BindingFlags.NonPublic | BindingFlags.Static);
            Check("C24-4 前提：能定位 TryEncodeFixedString", encodeString != null,
                encodeString?.ToString() ?? "未找到");

            if (encodeString != null)
            {
                object?[] args = { "ABC", 5, null, null };
                bool ok = (bool)encodeString.Invoke(null, args)!;
                var bytes = args[2] as byte[];
                Check("C24-5 字符串定长补零：\"ABC\" → 5 字节 [65,66,67,0,0]",
                    ok && bytes is { Length: 5 } && bytes[0] == 65 && bytes[1] == 66 && bytes[2] == 67
                    && bytes[3] == 0 && bytes[4] == 0,
                    ok ? (bytes == null ? "null" : string.Join(",", bytes)) : args[3] as string ?? "null");

                object?[] tooLong = { "ABCDEF", 5, null, null };
                bool tooLongOk = (bool)encodeString.Invoke(null, tooLong)!;
                Check("C24-6 字符串超长 → 明确失败（不覆盖相邻字段）",
                    !tooLongOk && (tooLong[3] as string)?.Contains("超过", StringComparison.Ordinal) == true,
                    tooLong[3] as string ?? "null");
            }

            var encodeArray = typeof(NetworkVariableModel).GetMethod(
                "TryEncodeStringArray", BindingFlags.NonPublic | BindingFlags.Static);
            Check("C24-7 前提：能定位 TryEncodeStringArray", encodeArray != null,
                encodeArray?.ToString() ?? "未找到");

            if (encodeArray != null)
            {
                object?[] args = { new[] { "A", "BC" }, 4, null, null };
                bool ok = (bool)encodeArray.Invoke(null, args)!;
                var bytes = args[2] as byte[];
                Check("C24-8 字符串数组定长：[\"A\",\"BC\"] × 4 字节 → 8 字节且逐项补零",
                    ok && bytes is { Length: 8 }
                    && bytes[0] == 65 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
                    && bytes[4] == 66 && bytes[5] == 67 && bytes[6] == 0 && bytes[7] == 0,
                    ok ? (bytes == null ? "null" : string.Join(",", bytes)) : args[3] as string ?? "null");
            }
        }

        private static string DescribeParams(MethodInfo method)
            => method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name)) + ")";

        /// <summary>
        /// C26：数组逐元素工程量换算（读/写互逆、缺失项不换算、读路径注入、超限预警）。
        /// <para>为什么单独一组：标量换算的 C24 只证明"地址对象上那一份"是通的；
        /// 数组的档案挂在变量上（每个元素一份），读走 <c>ToEngineeringValue</c>、
        /// 写走私有的 <c>TryToRawArray</c>，两条路径必须同一套公式，否则"读 105、写回 1000.5"这种错位
        /// 在界面上完全看不出来。</para>
        /// </summary>
        private static void ArrayElementConversion()
        {
            Section("C26 数组逐元素工程量换算（读/写互逆 / 缺失项不换算 / 超限预警）");

            var model = new NetworkVariableModel
            {
                Name = "Ctrl",
                DataType = typeof(int[]),
                ConnectionName = "PLC1",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 3
                },
                Value = new[] { 1000, 2000, 3000 },
                ElementConversions = new ElementConversion?[]
                {
                    new() { Scale = 0.1, Offset = 5, DecimalPlaces = 1, Unit = "℃" },
                    null,
                    new() { Scale = 2, Offset = 0, DecimalPlaces = 0, Unit = "mm" },
                }
            };

            var engineering = model.ToEngineeringValue(new[] { 1000, 2000, 3000 }) as double[];
            Check("C26-1 读路径：1000/2000/3000 → 105.0 / 2000 / 6000（null 项不换算）",
                engineering is { Length: 3 }
                && engineering[0] == 105.0 && engineering[1] == 2000.0 && engineering[2] == 6000.0,
                engineering == null ? "(null)" : string.Join(",", engineering));

            var plain = new NetworkVariableModel
            {
                DataType = typeof(int[]),
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 3
                }
            };
            var original = new[] { 1, 2, 3 };
            Check("C26-2 无档案：读路径原样返回（引用不变，零分配）",
                ReferenceEquals(plain.ToEngineeringValue(original), original),
                "返回值被换成了新数组");

            var toRawArray = typeof(NetworkVariableModel).GetMethod(
                "TryToRawArray", BindingFlags.NonPublic | BindingFlags.Instance);
            Check("C26-3 前提：能定位 NetworkVariableModel.TryToRawArray",
                toRawArray != null, toRawArray?.ToString() ?? "未找到");

            if (toRawArray != null)
            {
                object?[] args = { new[] { 105, 2000, 6000 }, null, null };
                bool ok = (bool)toRawArray.Invoke(model, args)!;
                var raw = args[1] as int[];
                Check("C26-3 写路径：工程值 105/2000/6000 → 原始 1000/2000/3000",
                    ok && raw is { Length: 3 } && raw[0] == 1000 && raw[1] == 2000 && raw[2] == 3000,
                    ok ? (raw == null ? "null" : string.Join(",", raw)) : args[2] as string ?? "null");
            }

            // 读路径的注入点：轮询发布 / 立刻读都走 CommunicationVariable.ToEngineering
            var commVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "Ctrl",
                Address = model.AddressConfig.Address,
                AddressConfig = model.AddressConfig,
                EngineeringConverter = model.ToEngineeringValue,
            };
            var injected = commVar.ToEngineering(new[] { 1000, 2000, 3000 }) as double[];
            Check("C26-4 读路径注入：CommunicationVariable.ToEngineering 走变量模型的逐元素换算",
                injected is { Length: 3 } && injected[0] == 105.0 && injected[2] == 6000.0,
                injected == null ? "(null)" : string.Join(",", injected));

            model.ElementConversions = new ElementConversion?[]
            {
                new() { Scale = 0.1, Offset = 5, DecimalPlaces = 1, Unit = "℃" }
            };
            var partial = model.ToEngineeringValue(new[] { 1000, 2000, 3000 }) as double[];
            Check("C26-5 档案比数组短：缺失项按不换算处理（不整组丢数据）",
                partial is { Length: 3 } && partial[0] == 105.0 && partial[1] == 2000.0 && partial[2] == 3000.0,
                partial == null ? "(null)" : string.Join(",", partial));

            var big = new NetworkVariableModel
            {
                DataType = typeof(int[]),
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 100
                }
            };
            Check("C26-6 整组写入超限预警：100×Int32 = 200 个寄存器 > 120 → 建议只写修改项",
                big.GetWholeGroupWriteWarning()?.Contains("120", StringComparison.Ordinal) == true,
                big.GetWholeGroupWriteWarning() ?? "(无预警)");

            var s7 = new NetworkVariableModel
            {
                DataType = typeof(int[]),
                AddressConfig = new S7Address
                {
                    Area = S7Area.DB,
                    DbNumber = 1,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 50
                }
            };
            Check("C26-7 整组写入超限预警：S7 50×Int32 = 200 字节 > 110 → 建议只写修改项",
                s7.GetWholeGroupWriteWarning()?.Contains("110", StringComparison.Ordinal) == true,
                s7.GetWholeGroupWriteWarning() ?? "(无预警)");

            Check("C26-8 按元素写支持判定：Modbus Int32[] 支持；Modbus byte[] 明确不支持",
                model.SupportsElementWrite
                && !new NetworkVariableModel
                {
                    DataType = typeof(byte[]),
                    AddressConfig = new ModbusAddress
                    {
                        Area = ModbusArea.HoldingRegisters,
                        DataType = DataValueType.ByteArray,
                        Offset = "0",
                        Length = 4
                    }
                }.SupportsElementWrite,
                $"Int32[]={model.SupportsElementWrite}");
        }

        #endregion

        #region C13 写命令优先级与队列背压（N1 / N2）

        /// <summary>
        /// <para>N1（写命令优先级）：读/写拆成两条队列后，Worker 每轮"先清空写队列、再清空读队列"，
        /// 写命令得以插到读命令前面——而不是像旧实现那样挤同一条 FIFO、排在队尾等整轮跑完。</para>
        /// <para>N2（队列背压）：队列满时先等 <c>CommandEnqueueTimeoutMs</c> 让 Worker 消化，
        /// 超时才判失败——把"瞬时排队"与"真故障"分开。</para>
        /// <para>为什么用桩连接 + 直接 new ConnectionWorker：本组验的是"队列怎么排序、怎么背压"，与设备无关。
        /// 走 Manager 拿不到可注入的假连接（连接对象由工厂按配置创建），而真 Modbus 连接又要真设备才写得成功——
        /// 桩是唯一能同时做到"离线"与"可观测"的办法。</para>
        /// <para>闸门（<see cref="ManualResetEventSlim"/>）的作用：把 Worker 线程**钉死**在某条命令上，
        /// 这样"排队顺序"才是确定性的，断言不依赖机器快慢。</para>
        /// </summary>
        private static void WritePriorityAndBackpressure()
        {
            Section("C13 写命令优先级与队列背压（N1：写插队 / N2：满队列先等再判失败）");

            int capacity = CommandQueueCapacity();
            int enqueueTimeoutMs = CommandEnqueueTimeoutMs();

            Check("C13-1 队列容量已放宽（旧实现固定 256，UI 连点写 / 批量写易打满）",
                capacity > 256, $"实际容量 {capacity}");

            using var conn = new OrderRecordingConnection();
            using var worker = new ConnectionWorker(conn);
            worker.Start();

            // ---- N1：读先入队、写后入队，执行顺序仍是「写 → 读」 ----
            using (var gate = new ManualResetEventSlim(false))
            {
                var blocker = worker.Invoke(_ => { gate.Wait(10000); return true; });
                // 等 blocker 被取走：此刻 Worker 已卡在闸门上，下面两条命令必然都还留在各自队列里，
                // 顺序才可比。300ms 对"空闲线程取一条命令"是极宽松的余量。
                Thread.Sleep(300);

                var readTask = worker.Invoke(c => { c.Read<short>("D0"); return true; });
                var writeTask = worker.EnqueueWrite("D100", (short)1);

                gate.Set();
                Task.WaitAll(blocker, readTask, writeTask);

                string seq = string.Join(" → ", conn.Log);
                Check("C13-2 写命令插队：读先入队、写后入队，执行顺序仍是「写 → 读」（旧实现是「读 → 写」）",
                    seq == "写:D100 → 读:D0", $"实际顺序 {seq}");
            }

            // ---- N1：InvokeWrite 与 Invoke 语义一致（返回值 / 异常回抛） ----
            Check("C13-3 InvokeWrite 与 Invoke 语义一致：能取回结果",
                worker.InvokeWrite(_ => 42).GetAwaiter().GetResult() == 42, "");

            bool writeThrew = false;
            try
            {
                worker.InvokeWrite<bool>(_ => throw new InvalidOperationException("模拟写失败")).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { writeThrew = true; }
            Check("C13-4 InvokeWrite 把委托异常回抛给调用方（走写队列不等于吞异常）",
                writeThrew, "");

            // ---- N1：空闲时命令必须"立刻"唤醒 Worker（否则双队列反而比单队列更慢） ----
            Thread.Sleep(300); // 让 Worker 进入"空闲阻塞在信号上"的状态（此时 ComputeWaitMs = 200）
            var swIdle = System.Diagnostics.Stopwatch.StartNew();
            worker.Invoke(_ => true).GetAwaiter().GetResult();
            long idleLatency = swIdle.ElapsedMilliseconds;
            Check("C13-5 空闲时投递命令被立刻唤醒执行（<150ms，不等满 200ms 空闲等待）",
                idleLatency < 150, $"耗时 {idleLatency}ms");

            // ---- N2：队列满 → 先等 timeout 让 Worker 消化，超时才判失败 ----
            using (var gate = new ManualResetEventSlim(false))
            {
                var blocker = worker.Invoke(_ => { gate.Wait(20000); return true; });
                Thread.Sleep(300); // 同上：先让 blocker 被取走，队列腾空，才能精确填满 capacity

                var pending = new List<Task>(capacity);
                for (int i = 0; i < capacity; i++)
                    pending.Add(worker.Invoke(_ => true));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var overflow = worker.Invoke(_ => true);
                bool threw = false;
                string message = "";
                try { overflow.GetAwaiter().GetResult(); }
                catch (InvalidOperationException ex) { threw = true; message = ex.Message; }
                long waited = sw.ElapsedMilliseconds;

                Check($"C13-6 队列满 → 先等 {enqueueTimeoutMs}ms 让 Worker 消化，超时才判失败（旧实现立即失败）",
                    threw && waited >= enqueueTimeoutMs - 300 && message.Contains("队列已满"),
                    $"等待 {waited}ms，抛异常={threw}，消息「{message}」");

                gate.Set();
                Task.WaitAll(blocker);
                Task.WaitAll(pending.ToArray(), 10000);
            }
        }

        /// <summary>
        /// C29：插队读/写车道（L0/L1）与轮询段间让路。
        /// <para>桩记录"谁被执行了、按什么顺序"，闸门把 Worker 钉死让排队顺序可复现；
        /// 轮询让路用"第一段读完后置插队标志"的桩，验证第二段之前主动返回 Yielded。</para>
        /// </summary>
        private static void UrgentLanesAndPollYield()
        {
            Section("C29 插队读/写车道与轮询段间让路（L0/L1）");

            int urgentCapacity = UrgentQueueCapacity();
            int urgentTimeout = UrgentEnqueueTimeoutMs();
            Check("C29-0 插队队列容量与入队等待按设计取值（256 / 200ms）",
                urgentCapacity == 256 && urgentTimeout == 200,
                $"容量 {urgentCapacity} / 等待 {urgentTimeout}ms");

            using var conn = new OrderRecordingConnection();
            using var worker = new ConnectionWorker(conn);
            worker.Start();

            // ---- C29-1/2/3：四车道顺序（插队写 → 插队读 → 常规写 → 常规读） ----
            using (var gate = new ManualResetEventSlim(false))
            {
                var blocker = worker.Invoke(_ => { gate.Wait(10000); return true; });
                Thread.Sleep(300); // 让 blocker 被取走：四条命令必然都还在各自队列里

                var normalRead = worker.Invoke(c => { c.Read<short>("D0"); return true; });
                var normalWrite = worker.EnqueueWrite("D100", (short)1);
                var urgentRead = worker.InvokeUrgentRead(c => { c.Read<short>("D1"); return true; });
                var urgentWrite = worker.InvokeUrgentWrite(c => { c.Write("D200", (short)2); return true; });

                gate.Set();
                Task.WaitAll(blocker, normalRead, normalWrite, urgentRead, urgentWrite);

                string seq = string.Join(" → ", conn.Log);
                Check("C29-1 四车道顺序：插队写 → 插队读 → 常规写 → 常规读",
                    seq == "写:D200 → 读:D1 → 写:D100 → 读:D0", $"实际顺序 {seq}");
                Check("C29-2 插队读优先于常规写（不是只优先于读）",
                    seq.IndexOf("读:D1", StringComparison.Ordinal) < seq.IndexOf("写:D100", StringComparison.Ordinal),
                    $"实际顺序 {seq}");
                Check("C29-3 插队写优先于插队读",
                    seq.IndexOf("写:D200", StringComparison.Ordinal) < seq.IndexOf("读:D1", StringComparison.Ordinal),
                    $"实际顺序 {seq}");
            }

            // ---- C29-5：空闲时插队命令立刻唤醒 ----
            Thread.Sleep(300);
            var swIdle = System.Diagnostics.Stopwatch.StartNew();
            worker.InvokeUrgentRead(_ => true).GetAwaiter().GetResult();
            long idle = swIdle.ElapsedMilliseconds;
            Check("C29-5 空闲时插队命令被立刻唤醒执行（<150ms，不等满 200ms 空闲等待）",
                idle < 150, $"耗时 {idle}ms");

            // ---- C29-4：插队队列满 → 约 200ms 后失败，不回退常规队列 ----
            using (var gate = new ManualResetEventSlim(false))
            {
                var blocker = worker.Invoke(_ => { gate.Wait(20000); return true; });
                Thread.Sleep(300);

                var pending = new List<Task>(urgentCapacity);
                for (int i = 0; i < urgentCapacity; i++)
                    pending.Add(worker.InvokeUrgentRead(_ => true));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool threw = false;
                string message = "";
                try { worker.InvokeUrgentRead(_ => true).GetAwaiter().GetResult(); }
                catch (InvalidOperationException ex) { threw = true; message = ex.Message; }
                long waited = sw.ElapsedMilliseconds;

                Check("C29-4 插队队列满 → 约 200ms 后失败（不是 2000ms），且不回退常规队列",
                    threw && waited >= urgentTimeout - 300 && message.Contains("插队命令队列已满"),
                    $"等待 {waited}ms，抛异常={threw}，消息「{message}」");

                gate.Set();
                Task.WaitAll(blocker);
                Task.WaitAll(pending.ToArray(), 10000);
            }

            // ---- C29-8/9：轮询段间让路 ----
            var vars = new[]
            {
                MakeVar("V0", "", 0, mergeAdjacent: false),
                MakeVar("V1", "", 100, mergeAdjacent: false),
                MakeVar("V2", "", 200, mergeAdjacent: false),
            };
            var sched = PollScheduler.Create(vars, MakeConfig(1000), null)!;

            int reads = 0;
            bool urgent = false;
            var segConn = new PollSegmentConnection(() =>
            {
                reads++;
                if (reads == 1) urgent = true; // 第一段读完后置"有插队"，下一段前应让路
            });

            var first = sched.Run(segConn, () => urgent);
            Check("C29-8 轮询在段间让路：第一段后检测到插队 → Yielded，第二段未执行",
                first == PollCycleOutcome.Yielded && reads == 1,
                $"结局 {first}，段读 {reads} 次");

            urgent = false;
            var second = sched.Run(segConn, () => urgent);
            Check("C29-9 让路后本组保持到期：下一拍重跑并完成（含重读第一段）",
                second == PollCycleOutcome.Completed && reads >= 4,
                $"结局 {second}，累计段读 {reads} 次");

            // ---- C29-10：立刻读的单变量路径不做坏段隔离判断 ----
            var single = PollBatchPlanner.Build(
                new[] { MakeVar("S0", "", 0, mergeAdjacent: false) },
                PollTransportKind.Ethernet,
                log: null,
                isolateFaultedSegments: true);
            var singleConn = new PollSegmentConnection(null);
            bool oneOk = single.TryReadOne(singleConn, out var oneValue, out var oneError);
            Check("C29-10 单变量读取（立刻读路径）不做坏段隔离判断，仍然真读",
                oneOk && oneValue is short && singleConn.ReadCalls == 1,
                $"成功={oneOk}，值={oneValue}，错误「{oneError}」，段读 {singleConn.ReadCalls} 次");

            // ---- C29-6/7/11/12：Manager 两个插队 API ----
            VerifyManagerUrgentApis();
        }

        /// <summary>
        /// C29-6/7/11/12：直接验 <see cref="AdvancedCommunicationManager.ReadVariableNow"/> /
        /// <see cref="AdvancedCommunicationManager.WriteVariableNow"/>。
        /// <para>Manager 的连接表/变量表是私有字段，这里用反射注入一个桩 Worker——
        /// 与 C13 的"私有常量用反射读"同一思路：断言按实现自己的结构验，不另造一套管理器。</para>
        /// </summary>
        private static void VerifyManagerUrgentApis()
        {
            var mgr = new AdvancedCommunicationManager();
            try
            {
                var workerField = typeof(AdvancedCommunicationManager)
                    .GetField("_workers", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var varField = typeof(AdvancedCommunicationManager)
                    .GetField("_registeredVariables", BindingFlags.NonPublic | BindingFlags.Instance)!;

                var workers = (ConcurrentDictionary<string, ConnectionWorker>)workerField.GetValue(mgr)!;
                var registered = (ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationVariable>>)
                    varField.GetValue(mgr)!;

                var conn = new OrderRecordingConnection();
                var worker = new ConnectionWorker(conn);
                worker.Start();
                workers["PLC1"] = worker;

                var variable = MakeVar("V1", "", 0);
                registered["PLC1"] = new ConcurrentDictionary<string, CommunicationVariable>();
                registered["PLC1"]["V1"] = variable;

                bool connected = worker.Connect(3000).GetAwaiter().GetResult();
                Check("C29-6 前提：桩连接已进入 Connected", connected, $"状态 {worker.State}");

                var value = mgr.ReadVariableNow("PLC1", "V1");
                Check("C29-6 ReadVariableNow 返回解码值并更新变量镜像",
                    value is short s && s == 0 && variable.CurrentValue is short cv && cv == 0,
                    $"返回 {value}，镜像 {variable.CurrentValue}");

                mgr.WriteVariableNow("PLC1", "V1", (short)123);
                Check("C29-7 WriteVariableNow 把原始值写到变量地址",
                    conn.Log.Any(x => x == $"写:{variable.Address}"),
                    string.Join(" → ", conn.Log));

                // C29-13：只入队不等待。日志里已有 C29-7 的那次写，必须按"新增一次"判。
                int writesBefore = conn.Log.Count(x => x == $"写:{variable.Address}");
                bool queued = mgr.TryTriggerWriteNow("PLC1", "V1", (short)77, out var queueError);
                bool executed = SpinWait.SpinUntil(
                    () => conn.Log.Count(x => x == $"写:{variable.Address}") > writesBefore,
                    TimeSpan.FromSeconds(2));
                Check("C29-13 TryTriggerWriteNow 只入队：返回 true 后 Worker 异步执行，且不改镜像",
                    queued && executed && variable.CurrentValue is short cv0 && cv0 == 0,
                    $"入队={queued}，执行={executed}，镜像={variable.CurrentValue}，错误「{queueError}」");

                bool missingThrew = false;
                try { mgr.ReadVariableNow("PLC1", "NOPE"); }
                catch (InvalidOperationException ex) { missingThrew = ex.Message.Contains("未接入轮询"); }
                Check("C29-11 变量未注册 → 明确错误，不发起 I/O", missingThrew, "");

                bool offlineThrew = false;
                try { mgr.ReadVariableNow("NOPE", "V1"); }
                catch (InvalidOperationException ex) { offlineThrew = ex.Message.Contains("连接不存在"); }
                Check("C29-12 连接不存在 → 立即失败，不入队", offlineThrew, "");
            }
            finally
            {
                mgr.Dispose();
            }
        }

        private static int UrgentQueueCapacity()
            => (int)typeof(ConnectionWorker)
                .GetField("UrgentQueueCapacity", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        private static int UrgentEnqueueTimeoutMs()
            => (int)typeof(ConnectionWorker)
                .GetField("UrgentEnqueueTimeoutMs", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        /// <summary>轮询让路桩：只记录段读次数，第一段读完可回调（用来置"有插队命令"）。</summary>
        private sealed class PollSegmentConnection : ICommunicationConnection
        {
            private readonly Action? _onRead;

            public PollSegmentConnection(Action? onRead) => _onRead = onRead;

            public int ReadCalls { get; private set; }

            public string ConnectionName => "STUB_SEG";

            public CommunicationType Type => CommunicationType.ModbusTcp;

            public bool IsConnected { get; private set; } = true;

            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() { IsConnected = true; return true; }

            public void Disconnect() => IsConnected = false;

            public bool TestConnection() => IsConnected;

            public void Dispose() => Disconnect();

            public T Read<T>(string address) where T : struct => default;

            public void Write(string address, object value) { }

            public byte[] ReadBytes(string address, ushort length)
            {
                ReadCalls++;
                _onRead?.Invoke();
                return new byte[length * 2];
            }

            public void WriteBytes(string address, byte[] data) { }

            public bool[] ReadBits(string address, ushort count) => new bool[count];
        }

        /// <summary>读私有常量：断言按"实现自己的口径"验，而不是在测试里再抄一份数字</summary>
        private static int CommandQueueCapacity()
            => (int)typeof(ConnectionWorker)
                .GetField("CommandQueueCapacity", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        private static int CommandEnqueueTimeoutMs()
            => (int)typeof(ConnectionWorker)
                .GetField("CommandEnqueueTimeoutMs", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        /// <summary>
        /// 测试桩：只记录"谁被执行了、按什么顺序"。
        /// <para>为什么不复用 <see cref="FaultyConnection"/>：那个桩的语义是"按地址注入读失败"，
        /// 本组要的是"记录执行顺序"，混在一起会让两个断言的意图互相污染。</para>
        /// </summary>
        private sealed class OrderRecordingConnection : ICommunicationConnection
        {
            public ConcurrentQueue<string> Log { get; } = new();

            public string ConnectionName => "STUB_ORDER";

            public CommunicationType Type => CommunicationType.ModbusTcp;

            public bool IsConnected { get; private set; }

            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() { IsConnected = true; return true; }

            public void Disconnect() => IsConnected = false;

            public bool TestConnection() => IsConnected;

            public void Dispose() => Disconnect();

            public T Read<T>(string address) where T : struct
            {
                Log.Enqueue($"读:{address}");
                return default;
            }

            public void Write(string address, object value) => Log.Enqueue($"写:{address}");

            public byte[] ReadBytes(string address, ushort length) => new byte[length * 2];

            public void WriteBytes(string address, byte[] data) => Log.Enqueue($"写字节:{address}");

            public bool[] ReadBits(string address, ushort count) => new bool[count];
        }

        #endregion

        #region C14 等待时长按到期算（C1）/ 限流 key 含连接名（C5）

        /// <summary>
        /// <para>C1（100Hz 空转）：<c>ConnectionWorker.ComputeWaitMs</c> 旧实现在"已连接且有轮询工作"时
        /// <b>恒定返回 10</b>——每秒醒 100 次只为看一眼"到没到期"。N1 引入唤醒信号（<c>_commandSignal</c>）后，
        /// "好快点响应命令"这个理由已由信号接管，等待时长于是可以纯粹为轮询节拍服务：
        /// 改为 <c>Clamp(距最近一个组到期还有多久, 1, 200)</c>，短周期组照旧准时，长周期组不再被高频唤醒。</para>
        /// <para>为什么下限是 1、上限是 <c>MaxIdleWaitMs</c>：见该常量注释与 <see cref="PollScheduler.MsUntilNextDue"/>。
        /// 本组断言把"下限不为 0"也钉上——返回 0 会让 <c>SemaphoreSlim.Wait(0)</c> 立即返回，
        /// 万一出现"到期却没能跑"的边角场景就变成死循环空转。</para>
        /// <para>C5（限流 key 缺连接名）：段级限流表 <c>PollBatchPlanner._logTicks</c> 是 <b>static</b>（跨连接共享），
        /// 而段读失败的 key 原本只有地址。于是两条连接读<b>同一个地址</b>时共用同一个 5 秒窗口——
        /// 后一条的失败告警被前一条吞掉，日志里永远看不到第二台设备出问题。修法是 key 补上连接名。</para>
        /// <para>为什么 C1 走反射白盒：<c>ComputeWaitMs</c> 是私有方法，而它的返回值正是要钉的量。
        /// 改成"真起线程跑一拍照再测耗时"会把"睡得对不对"与"线程调度抖不抖"混在一起，断言不稳。</para>
        /// </summary>
        private static void WaitSchedulingAndLogThrottleKey()
        {
            Section("C14 等待时长按到期算（C1）/ 限流 key 含连接名（C5）");

            // ---- C1：距到期计算（纯调度器，不涉线程） ----
            var sched = PollScheduler.Create(new[] { MakeVar("V1", "", 0) }, MakeConfig(200), null)!;

            Check("C14-1 刚编译好的调度器：距最近到期为 0（首轮立即可跑，不必空等一个周期）",
                sched.MsUntilNextDue() == 0, $"实际 {sched.MsUntilNextDue()}ms");

            using var ok = new FaultyConnection("PLC1", _ => false); // 全好：段读必成功
            sched.Run(ok);
            int until = sched.MsUntilNextDue();
            Check("C14-2 跑完一拍后：等待时长 ≈ 目标周期 200ms（不再是固定 10ms 空转）",
                until > 0 && until <= 200, $"实际 {until}ms");

            // ---- C1：多组取"最近到期"（最不缺组 = 快组决定下次醒来时刻） ----
            var cfgMin = MakeConfig(1000, new ScanGroupConfig("快组", 100));
            var schedMin = PollScheduler.Create(
                new[] { MakeVar("VF", "快组", 0), MakeVar("VS", "", 100) }, cfgMin, null)!;
            Check("C14-3 前提：编出两个组（快组 100ms / 默认组 1000ms）",
                schedMin.GroupCount == 2, schedMin.Describe());

            schedMin.Run(ok);
            int untilMin = schedMin.MsUntilNextDue();
            Check("C14-3 多组时取「最近到期」的那个（≈100ms 快组，而不是默认组的 1000ms）",
                untilMin > 0 && untilMin <= 150, $"实际 {untilMin}ms");

            // ---- C1：Worker 真的用它（白盒：置为已连接后直接问 ComputeWaitMs） ----
            int maxIdle = MaxIdleWaitMsConst();

            // 这个 Worker 不 Start：白盒探针不需要线程，起线程反而让"State 被状态机改写"与探针赛跑
            using var worker = new ConnectionWorker(new OrderRecordingConnection());

            var longSched = PollScheduler.Create(new[] { MakeVar("VL", "", 0) }, MakeConfig(60000), null)!;
            longSched.Run(ok);
            int longUntil = longSched.MsUntilNextDue();
            Check($"C14-4 前提：长周期组（60s）距到期远大于等待上限 {maxIdle}ms",
                longUntil > maxIdle, $"距到期 {longUntil}ms / 上限 {maxIdle}ms");

            int capWait = ProbeWaitMs(worker, longSched);
            Check($"C14-4 长周期组不睡满 60s：等待时长被封顶为 {maxIdle}ms（脏标记的最坏响应不劣于旧实现）",
                capWait == maxIdle, $"实际 {capWait}ms");

            var shortSched = PollScheduler.Create(new[] { MakeVar("VS2", "", 0) }, MakeConfig(200), null)!;
            shortSched.Run(ok);
            int shortWait = ProbeWaitMs(worker, shortSched);
            Check($"C14-5 已连接且有轮询工作：等待时长 > 10ms 且 ≤ {maxIdle}ms（旧实现恒为 10ms → 100Hz 空转）",
                shortWait > 10 && shortWait <= maxIdle, $"实际 {shortWait}ms");

            var dueSched = PollScheduler.Create(new[] { MakeVar("VD", "", 0) }, MakeConfig(200), null)!;
            int dueWait = ProbeWaitMs(worker, dueSched);
            Check("C14-5 有组已到期：等待仍 ≥ 1ms（返回 0 会让 Wait 立即返回，边角场景下变成死循环空转）",
                dueWait == 1, $"实际 {dueWait}ms");

            // ---- C5：两条连接读同一坏地址，各自都要出告警 ----
            var badA = MakeVar("BAD_A", "", 200);
            badA.ConnectionName = "C5_A";
            var badB = MakeVar("BAD_B", "", 200);
            badB.ConnectionName = "C5_B";

            Check("C14-6 前提：两条连接的变量地址完全相同（同址不同连接才会撞 key）",
                badA.Address == badB.Address, $"{badA.Address} / {badB.Address}");

            var logsA = new List<string>();
            var logsB = new List<string>();
            var plannerA = PollBatchPlanner.Build(new[] { badA }, PollTransportKind.Ethernet, logsA.Add)!;
            var plannerB = PollBatchPlanner.Build(new[] { badB }, PollTransportKind.Ethernet, logsB.Add)!;

            using var connA = new FaultyConnection("C5_A", _ => true);
            using var connB = new FaultyConnection("C5_B", _ => true);

            plannerA.Poll(connA);
            plannerB.Poll(connB);

            int segLogsA = logsA.Count(l => l.Contains("段读失败"));
            int segLogsB = logsB.Count(l => l.Contains("段读失败"));
            Check("C14-6 两条连接读同一坏地址：各自都报出限流日志（旧 key 只有地址，第二条会被第一条的 5 秒窗口吞掉）",
                segLogsA == 1 && segLogsB == 1, $"A={segLogsA} 条 / B={segLogsB} 条");

            // 紧接着再各读一轮：限流仍然生效（每连接一个窗口），证明修的是 key，不是把限流关掉
            plannerA.Poll(connA);
            plannerB.Poll(connB);
            Check("C14-6 同一连接 5 秒内重复失败仍被限流（修的是 key 的粒度，不是把限流关掉）",
                logsA.Count(l => l.Contains("段读失败")) == 1 && logsB.Count(l => l.Contains("段读失败")) == 1,
                $"A={logsA.Count} 条 / B={logsB.Count} 条");

            // ---- Dispose：从未 Start 的 Worker 也能安全释放 ----
            // 旧实现 catch (InvalidOperationException)，而 Join 对未启动线程抛的是 ThreadStateException——
            // 抓错类型，异常直接穿透 Dispose（本组探针第一次踩出，进程 exit code 3762504530）。
            // 生产路径上 Worker 都会先 Start，所以这个洞只在"建了就废"的 Worker 上暴露。
            bool disposeThrew = false;
            string disposeDetail = "正常释放";
            try
            {
                var neverStarted = new ConnectionWorker(new OrderRecordingConnection());
                neverStarted.Dispose(); // 故意不 Start：模拟"建了就废"的 Worker
            }
            catch (Exception ex)
            {
                disposeThrew = true;
                disposeDetail = ex.GetType().Name + ": " + ex.Message;
            }

            Check("C14-7 从未 Start 的 Worker 能安全 Dispose（Join 的 ThreadStateException 已被捕获，不再穿透）",
                !disposeThrew, disposeDetail);
        }

        /// <summary>读私有常量 MaxIdleWaitMs：断言按"实现自己的口径"验，而不是在测试里再抄一份数字</summary>
        private static int MaxIdleWaitMsConst()
            => (int)typeof(ConnectionWorker)
                .GetField("MaxIdleWaitMs", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

        /// <summary>Worker 的状态字段（白盒置为"已连接"用；正常没有"不起线程就置 Connected"的入口）</summary>
        private static readonly FieldInfo? WorkerStateField = typeof(ConnectionWorker)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Worker 的等待时长计算（C1 改的就是它，故直接问它，不经过线程）</summary>
        private static readonly MethodInfo? WorkerComputeWaitMsMethod = typeof(ConnectionWorker)
            .GetMethod("ComputeWaitMs", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>把 Worker 白盒置为"已连接 + 挂了该调度器"，再直接问它"这一拍打算睡多久"</summary>
        private static int ProbeWaitMs(ConnectionWorker worker, PollScheduler? scheduler)
        {
            worker.PollScheduler = scheduler;
            WorkerStateField!.SetValue(worker, (int)ConnectionState.Connected);
            return (int)WorkerComputeWaitMsMethod!.Invoke(worker, null)!;
        }

        #endregion

        #region C15 空档容忍按传输分档

        /// <summary>
        /// 空档容忍（<c>GapOf</c>）必须按<b>传输</b>分档，而不是一刀切。
        ///
        /// <para>背景：原实现只看协议（S7 / Modbus 寄存器区 / Modbus 位区）给了三个常数（16 / 8 / 64），
        /// 在以太网上代价失衡——多读 119 个寄存器只有 238 字节，却省下一次往返（实测约 1ms），
        /// 而"步长 10"的点表因为 9 &gt; 8 被拆成"每点一段"（压测 S3：1000 点 → 1000 段 → 923 请求/s 占满 1000ms 周期）。</para>
        ///
        /// <para>但也不能无脑放开：串口上多读 1 个寄存器 ≈ 2.29ms 线时（9600bps），
        /// 合并反而比多跑一次往返还贵。故以太网档取"该区的单请求上限"（等价于不限空档，只受上限约束），
        /// 串口档保留原常数。<b>本组同时钉住"放开"与"不放开"两侧</b>，缺任何一侧都可能被后人改坏。</para>
        /// </summary>
        private static void GapToleranceByTransport()
        {
            Section("C15 空档容忍按传输分档（以太网放开 / 串口不变）");

            // 100 个点、步长 10 个寄存器（相邻间隔 9）——压测 S3 里"每个点自成一段"的那张点表
            var sparse = Enumerable.Range(0, 100)
                .Select(i => MakeVar($"V{i}", "", i * 10))
                .ToArray();

            var tcpCfg = MakeConfig(1000);   // ModbusTcp → EthernetConfigBase → 以太网档
            var rtuCfg = new CommunicationConfig(CommunicationType.ModbusRtu)
            {
                ConnectionName = "PLC1",
                ReadCycleMs = 1000
            };                               // ModbusRtu → SerialConfig → 串口档

            var tcp = PollScheduler.Create(sparse, tcpCfg, null)!;
            var rtu = PollScheduler.Create(sparse, rtuCfg, null)!;

            // 以太网档：一段能装下 12 个点（10×11+1 = 111 ≤ 120，第 13 个点 121 > 120 必须另起）→ ceil(100/12) = 9
            Check("C15-1 以太网档：100 点 / 步长 10 → 9 段（空档近乎免费，只受单请求上限 120 约束）",
                tcp.TotalSegments == 9, tcp.Describe());
            Check("C15-2 串口档：同一点表 → 100 段（空档要花真实线时，行为与改动前一字不差）",
                rtu.TotalSegments == 100, rtu.Describe());
            Check("C15-3 两种档位点数守恒（合并只改变往返次数，一个点都不丢）",
                tcp.GetStats()[0].VariableCount == 100 && rtu.GetStats()[0].VariableCount == 100,
                $"TCP={tcp.GetStats()[0].VariableCount} / RTU={rtu.GetStats()[0].VariableCount}");

            // 上限必须仍然生效：放开的是"空档"，不是"段大小"。900 个连续寄存器不能并成 1 段
            var dense = Enumerable.Range(0, 900).Select(i => MakeVar($"D{i}", "", i)).ToArray();
            var denseTcp = PollScheduler.Create(dense, tcpCfg, null)!;
            Check("C15-4 以太网档仍受单请求上限封顶：900 个连续寄存器 → 8 段（不是 1 段）",
                denseTcp.TotalSegments == 8, denseTcp.Describe());
        }

        #endregion

        #region 站号 / 合并开关 / 换算 / 隔离 / 驱动注册表

        /// <summary>
        /// <para><b>站号必须逐帧生效</b>：一条串口上挂多个 Modbus 从站时，"读哪个从站"是每帧的事。</para>
        /// <para>HSL 支持富地址 <c>s=N;x=3;100</c>：组帧时把站号写进帧头、应答按<b>本次发出的站号</b>校验
        /// （<c>ModbusInfo</c> / <c>ModbusHelper.ExtraRtuResponseContent</c>）。本组钉住三件事：</para>
        /// <para>① 地址真的带上了 <c>s=</c> 前缀；② 站号进了 <see cref="PollAddress.GroupKey"/>
        /// （否则不同从站的同地址变量会被合并成一段，一次 FC3 只发给一个站，另一站静默读到错值）；
        /// ③ 站号进了段前缀（否则段地址丢了 <c>s=</c>，读的还是连接默认站）。</para>
        /// </summary>
        private static void ModbusStationInAddressAndGroupKey()
        {
            Section("C16 Modbus 站号逐帧生效（一条串口多从站）");

            var station2 = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "100",
                Station = 2
            };
            Check("C22-1 站号 > 0 时地址带 s= 前缀（HSL 富地址，逐帧写帧头）",
                station2.Address == "s=2;x=3;100", station2.Address);

            var station0 = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "100"
            };
            Check("C22-2 站号 = 0（默认）时不写 s= 前缀——历史行为一字不差",
                station0.Address == "x=3;100", station0.Address);

            // 两个从站、同一地址、同一存储区：必须分成两段（不能合并）
            var vars = new[]
            {
                MakeVarWithStation("A", 1, 100),
                MakeVarWithStation("B", 2, 100)
            };
            var cfg = MakeConfig(1000);
            var scheduler = PollScheduler.Create(vars, cfg, null)!;

            Check("C22-3 不同站号 + 同地址 → 不合并（2 段），否则另一站静默读到错值",
                scheduler.TotalSegments == 2, scheduler.Describe());
            Check("C22-4 两个变量都还在轮询里（分组只改变往返次数，一个点都不丢）",
                scheduler.GetStats()[0].VariableCount == 2,
                $"{scheduler.GetStats()[0].VariableCount} 变量");

            // 同站号 + 相邻地址 → 照常合并（站号不该破坏批量化）
            var sameStation = new[]
            {
                MakeVarWithStation("C", 1, 100),
                MakeVarWithStation("D", 1, 101)
            };
            var merged = PollScheduler.Create(sameStation, cfg, null)!;
            Check("C22-5 同站号 + 相邻地址 → 照常合并成 1 段（站号不破坏批量化）",
                merged.TotalSegments == 1, merged.Describe());

            // 结构化解码路径
            Check("C22-6 TryCreatePollAddress 把站号写进 GroupKey 与段前缀",
                station2.TryCreatePollAddress(out var poll)
                && poll!.GroupKey.Contains("s=2;")
                && poll.SegmentPrefix == "s=2;x=3;",
                poll == null ? "(null)" : $"{poll.GroupKey} / {poll.SegmentPrefix}");

            // 旧字符串地址路径（手填 "s=2;x=3;100"）
            var legacy = PollAddressResolver.ParseLegacy("s=2;x=3;100", typeof(short).AssemblyQualifiedName);
            Check("C22-7 旧字符串地址 s=2;x=3;100 也保留站号（手填地址不被合并掉）",
                legacy != null && legacy.GroupKey.Contains("s=2;") && legacy.SegmentPrefix == "s=2;x=3;",
                legacy == null ? "(解析失败)" : $"{legacy.GroupKey} / {legacy.SegmentPrefix}");

            var legacyNoStation = PollAddressResolver.ParseLegacy("x=3;100", typeof(short).AssemblyQualifiedName);
            Check("C22-8 旧字符串地址无站号 → GroupKey 与改动前一致（FC3，零回归）",
                legacyNoStation != null && legacyNoStation.GroupKey == "FC3",
                legacyNoStation?.GroupKey ?? "(解析失败)");

            // ReadBits 的 FC1/FC2 判定不能靠前缀（带站号后前缀不在开头）
            Check("C22-9 离散输入判定按 x= 段解析：s=2;x=2;100 认作 FC2（不是线圈）",
                ModbusRichAddress.IsDiscreteInput("s=2;x=2;100")
                && !ModbusRichAddress.IsDiscreteInput("s=2;x=1;100")
                && !ModbusRichAddress.IsDiscreteInput("x=3;100"),
                "前缀判断会把 s=2;x=2;100 误判成线圈");
        }

        /// <summary>
        /// <para><b>区域合并读开关</b>：默认开启（行为与改造前一致）；关掉只影响"跟邻居合并"，
        /// 不退回单点反射读——仍走段读（自己的跨度一次读完、按结构化解码）。</para>
        /// <para>关掉之后段数上升是用户显式选择的结果，不是缺陷。</para>
        /// </summary>
        private static void MergeAdjacentReadSwitch()
        {
            Section("C17 逐变量「区域合并读」开关");

            var cfg = MakeConfig(1000);

            // 基线：三个相邻变量默认合并成 1 段
            var allOn = new[]
            {
                MakeVar("V0", "", 100),
                MakeVar("V1", "", 101),
                MakeVar("V2", "", 102)
            };
            Check("C23-1 默认（开关开）三个相邻变量合并成 1 段",
                PollScheduler.Create(allOn, cfg, null)!.TotalSegments == 1,
                PollScheduler.Create(allOn, cfg, null)!.Describe());

            // 中间那个关掉：它自成一段，左右两个**不能跨过它**再合并（跨过去就等于读了它的寄存器，
            // 与"只读自己的跨度"自相矛盾）→ 3 段。
            var middleOff = new[]
            {
                MakeVar("V0", "", 100),
                MakeVar("V1", "", 101, mergeAdjacent: false),
                MakeVar("V2", "", 102)
            };
            var middleScheduler = PollScheduler.Create(middleOff, cfg, null)!;
            Check("C23-2 中间变量关掉合并 → 3 段（关掉的自成一段，左右不跨过它合并）",
                middleScheduler.TotalSegments == 3, middleScheduler.Describe());
            Check("C23-3 关掉合并的变量仍在轮询里（不是退回单点反射读、更不是丢弃）",
                middleScheduler.GetStats()[0].VariableCount == 3,
                $"{middleScheduler.GetStats()[0].VariableCount} 变量");

            // 全部关掉：3 段
            var allOff = new[]
            {
                MakeVar("V0", "", 100, mergeAdjacent: false),
                MakeVar("V1", "", 101, mergeAdjacent: false),
                MakeVar("V2", "", 102, mergeAdjacent: false)
            };
            var allOffScheduler = PollScheduler.Create(allOff, cfg, null)!;
            Check("C23-4 全部关掉 → 3 段（每个变量读自己的跨度）",
                allOffScheduler.TotalSegments == 3, allOffScheduler.Describe());

            // 关键：关掉合并的变量落在"跨多寄存器的邻居"跨度内时，不能被挂错段
            // （旧实现按区间包含反推归属，会把这个变量挂到邻居那段，自己那段空转成白发请求）
            var overlap = new[]
            {
                MakeVarOfType("Wide", "", 100, DataValueType.Int32),   // 跨 100..101
                MakeVar("Inner", "", 101, mergeAdjacent: false)         // 落在 Wide 的跨度内
            };
            var overlapScheduler = PollScheduler.Create(overlap, cfg, null)!;
            Check("C23-5 关合并的变量落在邻居跨度内 → 仍分成 2 段且各自有归属（不挂错段）",
                overlapScheduler.TotalSegments == 2
                && overlapScheduler.GetStats()[0].VariableCount == 2,
                overlapScheduler.Describe());
        }

        /// <summary>
        /// <para><b>工程量换算必须读写对称</b>：<c>DeviceAddressBase</c> 里的换算管道
        /// （<see cref="DeviceAddressBase.ConvertToEngineering"/> / <see cref="DeviceAddressBase.ConvertToRaw"/>）
        /// 早就存在，但改造前只有<b>写</b>路径调了 <c>ConvertToRaw</c>，<b>读</b>路径从没调过——</para>
        /// <para>于是"配了缩放系数的变量写进去是工程值、读回来却是原始值"。本组钉住读路径也换算，
        /// 且默认（未启用换算）行为零变化。</para>
        /// </summary>
        private static void EngineeringConversionOnReadPath()
        {
            Section("C18 工程量换算（读路径与写路径对称）");

            var plain = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "0"
            };
            var plainVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "P",
                Address = plain.Address,
                AddressConfig = plain,
                ValueType = typeof(short).AssemblyQualifiedName!
            };
            Check("C24-1 未启用换算 → 原值返回（默认行为零变化）",
                Equals(plainVar.ToEngineering((short)1234), (short)1234),
                plainVar.ToEngineering((short)1234)?.ToString() ?? "(null)");

            var scaled = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "0",
                EnableConversion = true,
                Scale = 0.1,
                EngineeringOffset = 5,
                DecimalPlaces = 2
            };
            var scaledVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "S",
                Address = scaled.Address,
                AddressConfig = scaled,
                ValueType = typeof(short).AssemblyQualifiedName!
            };

            // 1000 * 0.1 + 5 = 105
            Check("C24-2 启用换算：原始值 1000 → 工程值 105（×0.1 + 5）",
                Convert.ToDouble(scaledVar.ToEngineering((short)1000)) == 105.0,
                scaledVar.ToEngineering((short)1000)?.ToString() ?? "(null)");

            // 读写互逆：工程值 105 → 原始值 1000
            Check("C24-3 读写互逆：工程值 105 经 ConvertToRaw 回到原始值 1000",
                Convert.ToInt16(scaled.ConvertToRaw(105.0)) == 1000,
                scaled.ConvertToRaw(105.0)?.ToString() ?? "(null)");

            Check("C24-4 bool/string/byte[] 不参与换算（只换算数值）",
                Equals(scaledVar.ToEngineering(true), true)
                && Equals(scaledVar.ToEngineering("abc"), "abc"),
                "非数值类型原样返回");

            // 端到端：换算必须真的接在**轮询发布路径**上（只测 ToEngineering 辅助方法测不出这个）。
            // 假连接返回原始字节 0x03 0xE8 = 1000 → 经 ×0.1 + 5 应发布 105。
            var e2eAddr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = "0",
                EnableConversion = true,
                Scale = 0.1,
                EngineeringOffset = 5,
                DecimalPlaces = 2
            };
            var e2eVar = new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = "E2E",
                Address = e2eAddr.Address,
                AddressConfig = e2eAddr,
                ValueType = typeof(short).AssemblyQualifiedName!
            };
            var e2eScheduler = PollScheduler.Create(new[] { e2eVar }, MakeConfig(1000), null)!;
            using (var e2eConn = new RawBytesConnection(1000))
            {
                e2eScheduler.ResetAllDue();
                e2eScheduler.Run(e2eConn);
            }
            Check("C24-5 端到端：轮询读到的原始值 1000 → 变量上发布的是工程值 105",
                Convert.ToDouble(e2eVar.CurrentValue) == 105.0,
                e2eVar.CurrentValue?.ToString() ?? "(null)");
        }

        /// <summary>
        /// <para><b>坏段隔离</b>：一个恒坏地址（配置错/越界/从站掉线）在 100ms 周期下会每秒烧 10 次超时，
        /// 每次都拖慢同拍的其它段。隔离后它只按 <see cref="PollBatchPlanner.IsolationProbeIntervalMs"/> 探测一次。</para>
        /// <para>关键约束：<b>隔离不能变成"永久放弃"</b>——地址修好后必须能自己回来（探测成功即恢复）。</para>
        /// <para>本组用假连接驱动真实 <see cref="PollBatchPlanner"/>：让指定地址读失败、其余成功。</para>
        /// </summary>
        private static void FaultedSegmentIsolation()
        {
            Section("C25 坏段隔离（显式开启；连续失败移出正常轮询，可自愈）");

            // 隔离是**可选**能力（默认关闭，见 CommunicationConfig.IsolateFaultedSegments）：
            // 默认"每轮都试"是既有契约，诊断面板的"连续失败 N 轮"靠每轮真读才累加得出来（见 C12）。
            var cfg = MakeConfig(1000);
            cfg.IsolateFaultedSegments = true;

            // 两段必须真的分开：用不相邻地址（以太网档的空档容忍是 120 个寄存器，
            // 相邻地址会被合并成一段，那样"坏地址"根本不会单独命中，本组就变成假绿）。
            var good = MakeVar("Good", "", 200);
            var bad = MakeVar("Bad", "", 900);
            var scheduler = PollScheduler.Create(new[] { good, bad }, cfg, null)!;

            Check("C25-0 前提：两个不相邻地址分成 2 段（否则隔离无从验证）",
                scheduler.TotalSegments == 2, scheduler.Describe());

            // 先让坏段连续失败到阈值。
            // 每轮必须 ResetAllDue()：组周期是 1000ms，而这里连调 Run 只隔几毫秒，
            // 不重置到期基准的话第 2 轮起会被 IsDue() 直接跳过（累计读次数停在第一轮，断言变假绿/假红）。
            var conn = new ScriptedConnection(failAddress: "x=3;900");
            for (int i = 0; i < PollBatchPlanner.FaultedSegmentThreshold; i++)
            {
                scheduler.ResetAllDue();
                scheduler.Run(conn);
            }

            int readsBefore = conn.ReadCount;
            Check("C25-1 未达阈值前，坏段每轮都被读（隔离还没生效）",
                readsBefore >= PollBatchPlanner.FaultedSegmentThreshold * 2,
                $"累计 {readsBefore} 次读（2 段 × {PollBatchPlanner.FaultedSegmentThreshold} 轮）");

            // 达阈值之后紧接着的一轮：坏段应被跳过（只读好段）
            scheduler.ResetAllDue();
            int beforeSkip = conn.ReadCount;
            scheduler.Run(conn);
            int delta = conn.ReadCount - beforeSkip;

            Check("C25-2 达阈值后坏段被跳过：本轮只读好段（读次数 = 1，不是 2）",
                delta == 1, $"本轮读 {delta} 次");

            // 好段必须不受影响（这是隔离的真正目的：别拖慢同拍其它段）
            Check("C25-3 好段不受隔离影响（隔离只针对坏段，不牵连整条连接）",
                good.Quality == VariableQuality.Good, good.Quality.ToString());

            // 自愈：地址修好后，探测成功即恢复
            conn.FailAddress = null;
            Thread.Sleep(PollBatchPlanner.IsolationProbeIntervalMs + 200);
            scheduler.ResetAllDue();
            int beforeRecover = conn.ReadCount;
            scheduler.Run(conn);
            Check("C25-4 探测间隔过后重新尝试（不是永久放弃）",
                conn.ReadCount > beforeRecover, $"探测读 {conn.ReadCount - beforeRecover} 次");

            // 探测成功后应回到正常轮询：再跑两轮，坏段要重新每轮都被读
            int beforeNormal = conn.ReadCount;
            scheduler.ResetAllDue();
            scheduler.Run(conn);
            scheduler.ResetAllDue();
            scheduler.Run(conn);
            Check("C25-5 探测成功后恢复为每轮都读（自愈，不需要人工复位）",
                conn.ReadCount - beforeNormal >= 4,
                $"恢复后两轮共读 {conn.ReadCount - beforeNormal} 次（期望 4）");

            // 反向：默认（未开启隔离）必须每轮都试——这是 C12 依赖的既有契约，
            // "一律隔离"这种过度实现必须被这条钉住。
            var plainCfg = MakeConfig(1000);
            var plainScheduler = PollScheduler.Create(new[] { MakeVar("G2", "", 200), MakeVar("B2", "", 900) }, plainCfg, null)!;
            var plainConn = new ScriptedConnection("x=3;900");
            for (int i = 0; i < PollBatchPlanner.FaultedSegmentThreshold + 1; i++)
            {
                plainScheduler.ResetAllDue();
                plainScheduler.Run(plainConn);
            }
            plainScheduler.ResetAllDue();
            int beforePlain = plainConn.ReadCount;
            plainScheduler.Run(plainConn);
            Check("C25-6 反向：未开启隔离时坏段仍每轮都试（默认契约不许被改掉）",
                plainConn.ReadCount - beforePlain == 2,
                $"本轮读 {plainConn.ReadCount - beforePlain} 次（期望 2）");
        }

        /// <summary>
        /// <para><b>驱动注册表</b>：让"新增一个协议 = 写一个类 + 注册一次"成立。</para>
        /// <para>改造前要同时改 6 处（枚举 / TryCreateConfig switch / Implemented 数组 / 配置类 /
        /// 连接实现 / RegisterDefaults），且一致性只靠一条断言手工钉着。</para>
        /// </summary>
        private static void CommunicationDriverRegistryContract()
        {
            Section("C20 通讯驱动注册表（自定义协议只实现接口即可接入）");

            var registry = new CommunicationDriverRegistry();

            var fake = new FakeDriverFactory("fake.protocol", typeof(FakeConfig));
            registry.Register(fake);
            Check("C26-1 注册后可按 DriverId 取到",
                registry.TryGet("fake.protocol", out var found) && ReferenceEquals(found, fake), "fake.protocol");

            Check("C26-2 DriverId 大小写不敏感（落盘字符串不因大小写分裂）",
                registry.TryGet("FAKE.PROTOCOL", out _), "FAKE.PROTOCOL");

            Check("C26-3 可按配置类型反推驱动（反序列化后不用 DriverId 也能找回）",
                registry.TryGetByConfigType(typeof(FakeConfig), out var byType) && ReferenceEquals(byType, fake),
                nameof(FakeConfig));

            bool threw = false;
            try { registry.Register(new FakeDriverFactory("fake.protocol", typeof(FakeConfig))); }
            catch (InvalidOperationException) { threw = true; }
            Check("C26-4 同 Id 重复注册抛异常（不静默覆盖——否则跑的是哪个实现全看运气）",
                threw, threw ? "已抛出 InvalidOperationException" : "未抛异常");

            Check("C26-5 空 Id / 空显示名被拒绝",
                Throws<ArgumentException>(() => registry.Register(new FakeDriverFactory("", typeof(FakeConfig))))
                && Throws<ArgumentException>(() => registry.Register(new FakeDriverFactory("x", typeof(FakeConfig), ""))),
                "空 Id 与空显示名都必须抛 ArgumentException");

            Check("C26-6 未注册的驱动造连接 → NotSupportedException 且列出可用清单",
                ThrowsWithMessage<NotSupportedException>(() => registry.Create("nope", new FakeConfig()), "fake.protocol"),
                "错误信息里带可用驱动清单");

            var made = registry.Create("fake.protocol", new FakeConfig());
            Check("C26-7 注册的驱动能真的造出连接实例（自定义协议只需实现接口）",
                made != null && ReferenceEquals(made.GetType(), typeof(ScriptedConnection)),
                made?.GetType().Name ?? "(null)");

            Check("C26-8 注销后取不到（测试夹具可替换实现）",
                registry.Unregister("fake.protocol") && !registry.TryGet("fake.protocol", out _),
                "注销后 TryGet 返回 false");

            // 生产注册表：内建三种协议必须都在（否则界面里选了协议却造不出连接）
            var prod = ConnectionFactoryManager.Instance.Drivers;
            Check("C26-9 生产注册表含内建三协议（modbus.tcp / modbus.rtu / siemens.s7）",
                prod.TryGet("modbus.tcp", out _) && prod.TryGet("modbus.rtu", out _) && prod.TryGet("siemens.s7", out _),
                prod.ImplementedText);

            Check("C26-10 生产注册表的驱动数与已实现协议数一致（两套清单不许分裂）",
                prod.Count == CommunicationProtocols.Implemented.Length,
                $"驱动 {prod.Count} 个 / 已实现协议 {CommunicationProtocols.Implemented.Length} 个");
        }

        /// <summary>
        /// <para><b>帧级站号契约</b>：我们依赖 HSL 的富地址 <c>s=N;</c> 逐帧生效——组帧时把 N 写进帧头、
        /// 应答按本次发出的站号校验。这条是"一条 COM 挂多从站"的地基，且它在<b>第三方库里</b>，
        /// 升级 HSL 时不会有人提醒我们。</para>
        /// <para>本组直接对 <c>ModbusInfo.BuildReadModbusCommand</c> 断言首字节，把这条依赖钉死：
        /// 库一旦改成"只认实例站号"，这里当场变红，而不是等到现场"所有从站都读成 1 号站"。</para>
        /// <para>（IO 接入层下线前，这类覆盖在 <c>IoChecks</c> 的 RTM 组里；随 IO 层删除后迁到这里。）</para>
        /// </summary>
        private static void ModbusStationFrameContract()
        {
            Section("C28 帧级站号契约（HSL 富地址 s= 逐帧生效，第三方依赖钉子）");

            var readWithStation = HslCommunication.ModBus.ModbusInfo.BuildReadModbusCommand(
                "s=2;x=3;100", 1, 1, true, 3);
            Check("C28-1 读帧：地址 s=2 覆盖连接默认站号 1（帧头第 1 字节 = 2）",
                readWithStation.IsSuccess && readWithStation.Content[0][0] == 2,
                readWithStation.IsSuccess ? $"帧头={readWithStation.Content[0][0]}" : readWithStation.Message);

            var readDefault = HslCommunication.ModBus.ModbusInfo.BuildReadModbusCommand(
                "x=3;100", 1, 1, true, 3);
            Check("C28-2 读帧：地址无 s= → 用连接默认站号（帧头 = 1，历史行为不变）",
                readDefault.IsSuccess && readDefault.Content[0][0] == 1,
                readDefault.IsSuccess ? $"帧头={readDefault.Content[0][0]}" : readDefault.Message);

            var writeWithStation = HslCommunication.ModBus.ModbusInfo.BuildWriteWordModbusCommand(
                "s=7;x=3;100", new byte[] { 0, 1 }, 1, true, 16);
            Check("C28-3 写帧：地址 s=7 覆盖默认站号（帧头 = 7）",
                writeWithStation.IsSuccess && writeWithStation.Content[0] == 7,
                writeWithStation.IsSuccess ? $"帧头={writeWithStation.Content[0]}" : writeWithStation.Message);

            Check("C28-4 带站号时功能码仍正确（s=2;x=3 的读帧功能码 = 3）",
                readWithStation.IsSuccess && readWithStation.Content[0][1] == 3,
                readWithStation.IsSuccess ? $"功能码={readWithStation.Content[0][1]}" : readWithStation.Message);

            var fc2 = HslCommunication.ModBus.ModbusInfo.BuildReadModbusCommand("s=2;x=2;100", 1, 1, true, 2);
            Check("C28-5 带站号时离散输入功能码仍正确（s=2;x=2 → FC2）",
                fc2.IsSuccess && fc2.Content[0][1] == 2,
                fc2.IsSuccess ? $"功能码={fc2.Content[0][1]}" : fc2.Message);
        }

        /// <summary>
        /// <para><b>IO → 通讯折算</b>：旧 IO 配置（总线/设备/点位）必须能折成"通讯连接 + 网络变量"。</para>
        /// <para>这是整个合并里最容易出错的一块——站号丢了就是"多从站全读同一站"、
        /// 换算丢了就是"读回来是原始值"、幂等丢了就是"每开一次方案变量翻一倍"。
        /// 所以三条都单独钉住，另加一条"旧字段保留"（回退旧版本仍能读到配置）。</para>
        /// </summary>
        private static void IoMigrationContract()
        {
            Section("C27 IO → 通讯折算（总线/设备/点位 → 连接 + 网络变量）");

            // ---------- 串口总线 + 两个从站（站号 1 / 2） ----------
            var bus = new IoBusDescriptor
            {
                BusId = "COM3",
                DisplayName = "RTU 总线",
                TransportKind = IoTransportKind.Serial,
                Serial = new SerialTransportConfig
                {
                    PortName = "COM3",
                    BaudRate = 19200,
                    DataBits = 8,
                    Parity = IoParity.Even,
                    StopBits = IoStopBits.One,
                    ReadTimeoutMs = 800
                }
            };

            var device1 = MakeIoDevice("Meter1", "COM3", 1, new IoPointDescriptor
            {
                PointId = "Temp",
                Kind = IoPointKind.AnalogInput,
                Address = "100",
                DataType = IoDataType.Int16,
                Scale = 0.1,
                Offset = 5,
                Unit = "℃"
            });

            var device2 = MakeIoDevice("Meter2", "COM3", 2, new IoPointDescriptor
            {
                PointId = "Run",
                Kind = IoPointKind.DigitalInput,
                Address = "0",
                DataType = IoDataType.Bool
            });

            var solution = new SolutionModel();
            solution.IoBuses.Add(bus);
            solution.IoDevices.Add(device1);
            solution.IoDevices.Add(device2);

            var (connections, variables) = IoMigrationApplier.Apply(solution);

            Check("C27-1 一条总线 → 一条连接，两个点位 → 两个变量",
                connections == 1 && variables == 2,
                $"连接 {connections} / 变量 {variables}");

            var conn = solution.CommunicationConfigs.FirstOrDefault();
            Check("C27-2 连接类型与串口参数照搬（COM3 / 19200 / Even / 读超时 800）",
                conn != null && conn.Protocol == CommunicationType.ModbusRtu
                && conn.Config is SerialConfig s
                && s.PortName == "COM3" && s.BaudRate == 19200
                && s.Parity == ParityMode.Even && s.ReadTimeoutMs == 800,
                conn?.Config is SerialConfig sc
                    ? $"{sc.PortName}/{sc.BaudRate}/{sc.Parity}/{sc.ReadTimeoutMs}"
                    : "(不是串口配置)");

            // 站号是"一条 COM 多从站"的全部依据
            var tempVar = solution.GlobalVariables.OfType<NetworkVariableModel>()
                .FirstOrDefault(v => v.Name == "Meter1.Temp");
            var runVar = solution.GlobalVariables.OfType<NetworkVariableModel>()
                .FirstOrDefault(v => v.Name == "Meter2.Run");

            Check("C27-3 站号逐帧落地：Meter1 → s=1，Meter2 → s=2（丢了就是多从站全读同一站）",
                tempVar?.AddressConfig is ModbusAddress a1 && a1.Station == 1
                && runVar?.AddressConfig is ModbusAddress a2 && a2.Station == 2,
                $"Meter1={(tempVar?.AddressConfig as ModbusAddress)?.Station} / " +
                $"Meter2={(runVar?.AddressConfig as ModbusAddress)?.Station}");

            Check("C27-4 地址带 s= 前缀（HSL 富地址，逐帧写帧头）",
                tempVar?.AddressConfig?.Address == "s=1;x=3;100"
                && runVar?.AddressConfig?.Address == "s=2;x=2;0",
                $"{tempVar?.AddressConfig?.Address} / {runVar?.AddressConfig?.Address}");

            // 换算：Scale 0.1 + Offset 5 → EnableConversion + 参数照搬
            Check("C27-5 工程量换算随点位迁过来（Scale 0.1 / Offset 5 / 单位 ℃）",
                tempVar?.AddressConfig is ModbusAddress a3
                && a3.EnableConversion && a3.Scale == 0.1
                && a3.EngineeringOffset == 5 && a3.Unit == "℃",
                tempVar?.AddressConfig is ModbusAddress a4
                    ? $"Enabled={a4.EnableConversion} Scale={a4.Scale} Offset={a4.EngineeringOffset} Unit={a4.Unit}"
                    : "(无地址)");

            Check("C27-6 恒等换算不开启（避免给所有变量加一层无谓的乘法）",
                runVar?.AddressConfig is ModbusAddress a5 && !a5.EnableConversion,
                runVar?.AddressConfig is ModbusAddress a6 ? $"Enabled={a6.EnableConversion}" : "(无地址)");

            Check("C27-7 变量类型跟随点位（模拟量 double / 数字量 bool）",
                tempVar?.DataType == typeof(double) && runVar?.DataType == typeof(bool),
                $"{tempVar?.DataType.Name} / {runVar?.DataType.Name}");

            // ---------- 幂等：再折一次不新增 ----------
            int beforeConnections = solution.CommunicationConfigs.Count;
            int beforeVariables = solution.GlobalVariables.Count;
            var (c2, v2) = IoMigrationApplier.Apply(solution);
            Check("C27-8 幂等：重复折算不新增连接与变量（否则每开一次方案翻一倍）",
                c2 == 0 && v2 == 0
                && solution.CommunicationConfigs.Count == beforeConnections
                && solution.GlobalVariables.Count == beforeVariables,
                $"第二次折算 连接 {c2} / 变量 {v2}");

            // ---------- 旧字段保留（回退旧版本仍能读到配置） ----------
            Check("C27-9 旧 IO 字段保留不删（回退到旧版本软件仍能读到配置）",
                solution.IoBuses.Count == 1 && solution.IoDevices.Count == 2,
                $"IoBuses={solution.IoBuses.Count} / IoDevices={solution.IoDevices.Count}");

            // ---------- TCP 总线 ----------
            var tcpSolution = new SolutionModel();
            tcpSolution.IoBuses.Add(new IoBusDescriptor
            {
                BusId = "PLC_TCP",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "192.168.1.10", Port = 502, ReadTimeoutMs = 1200 }
            });
            tcpSolution.IoDevices.Add(MakeIoDevice("IO1", "PLC_TCP", 7, new IoPointDescriptor
            {
                PointId = "Y0",
                Kind = IoPointKind.DigitalOutput,
                Address = "16",
                DataType = IoDataType.Bool,
                AllowSharedWrite = true,
                LeaseTimeoutMs = 15000
            }));

            var (tcpConn, tcpVars) = IoMigrationApplier.Apply(tcpSolution);
            var tcpConfig = tcpSolution.CommunicationConfigs.FirstOrDefault();
            Check("C27-10 TCP 总线 → ModbusTcp 连接（Host/Port/读超时照搬）",
                tcpConn == 1 && tcpVars == 1
                && tcpConfig?.Protocol == CommunicationType.ModbusTcp
                && tcpConfig.Config is ModbusTcpConfig mc
                && mc.IpAddress == "192.168.1.10" && mc.Port == 502 && mc.ReadTimeoutMs == 1200,
                tcpConfig?.Config is ModbusTcpConfig mc2
                    ? $"{mc2.IpAddress}:{mc2.Port}/{mc2.ReadTimeoutMs}"
                    : "(不是 TCP 配置)");

            var y0 = tcpSolution.GlobalVariables.OfType<NetworkVariableModel>()
                .FirstOrDefault(v => v.Name == "IO1.Y0");
            Check("C27-11 写共享与租约超时随点位迁过来",
                y0 != null && y0.AllowSharedWrite && y0.LeaseTimeoutMs == 15000,
                y0 == null ? "(无变量)" : $"Shared={y0.AllowSharedWrite} Timeout={y0.LeaseTimeoutMs}");

            // ---------- 已存在同名连接 → 跳过该总线 ----------
            var conflictSolution = new SolutionModel();
            conflictSolution.IoBuses.Add(new IoBusDescriptor
            {
                BusId = "DUP",
                TransportKind = IoTransportKind.Tcp,
                Tcp = new TcpTransportConfig { Host = "10.0.0.1", Port = 502 }
            });
            conflictSolution.IoDevices.Add(MakeIoDevice("D1", "DUP", 1, new IoPointDescriptor
            {
                PointId = "P", Kind = IoPointKind.DigitalInput, Address = "0", DataType = IoDataType.Bool
            }));
            conflictSolution.CommunicationConfigs.Add(new CommunicationConfig(CommunicationType.ModbusTcp)
            {
                ConnectionName = IoMigrationPlanner.ConnectionNamePrefix + "DUP"
            });

            var (dupConn, dupVars) = IoMigrationApplier.Apply(conflictSolution);
            Check("C27-12 同名连接已存在 → 跳过该总线（不覆盖用户手配的连接）",
                dupConn == 0 && dupVars == 0,
                $"连接 {dupConn} / 变量 {dupVars}");
        }

        /// <summary>造一台 IO 设备（含一个点位）。</summary>
        private static IoDeviceDescriptor MakeIoDevice(
            string deviceId, string busId, byte unitId, IoPointDescriptor point)
        {
            var device = new IoDeviceDescriptor
            {
                DeviceId = deviceId,
                BusId = busId,
                DriverId = "modbus.rtu",
                Enabled = true,
                DriverConfigJson = $"{{\"UnitId\":{unitId},\"ByteOrder\":\"CDAB\"}}"
            };
            device.Points.Add(point);
            return device;
        }

        #region C22-C26 夹具
        private static CommunicationVariable MakeVarWithStation(string name, int station, int offset)
        {
            var addr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = DataValueType.Int16,
                Offset = offset.ToString(),
                Station = station
            };
            return new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = name,
                Address = addr.Address,
                AddressConfig = addr,
                ValueType = typeof(short).AssemblyQualifiedName!,
                AccessMode = VariableAccessMode.ReadOnly
            };
        }

        private static CommunicationVariable MakeVarOfType(
            string name, string scanGroup, int offset, DataValueType dataType)
        {
            var addr = new ModbusAddress
            {
                Area = ModbusArea.HoldingRegisters,
                DataType = dataType,
                Offset = offset.ToString()
            };
            Type clrType = dataType == DataValueType.Int32 ? typeof(int) : typeof(short);
            return new CommunicationVariable
            {
                ConnectionName = "PLC1",
                VariableName = name,
                Address = addr.Address,
                AddressConfig = addr,
                ValueType = clrType.AssemblyQualifiedName!,
                ScanGroup = scanGroup,
                AccessMode = VariableAccessMode.ReadOnly
            };
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try { action(); return false; }
            catch (TException) { return true; }
            catch { return false; }
        }

        private static bool ThrowsWithMessage<TException>(Action action, string expectedFragment)
            where TException : Exception
        {
            try { action(); return false; }
            catch (TException ex) { return ex.Message.Contains(expectedFragment, StringComparison.Ordinal); }
            catch { return false; }
        }

        /// <summary>
        /// 脚本化假连接：让指定地址读失败（模拟坏地址），其余返回固定值。
        /// <para>只实现轮询真正用到的那几个成员——<see cref="ReadBytes"/> / <see cref="ReadBits"/> /
        /// <see cref="ByteOrder"/>；其余抛 NotSupported（用到就说明规划器越界了）。</para>
        /// </summary>
        private sealed class ScriptedConnection : ICommunicationConnection
        {
            public ScriptedConnection(string? failAddress) => FailAddress = failAddress;

            /// <summary>命中该地址就抛（null = 全部成功）。可运行期改，用于验自愈。</summary>
            public string? FailAddress { get; set; }

            public int ReadCount { get; private set; }

            public string ConnectionName => "SCRIPT";
            public CommunicationType Type => CommunicationType.ModbusTcp;
            public bool IsConnected => true;
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() => true;
            public void Disconnect() { }
            public bool TestConnection() => true;

            public byte[] ReadBytes(string address, ushort length)
            {
                ReadCount++;
                if (FailAddress != null && string.Equals(address, FailAddress, StringComparison.Ordinal))
                    throw new InvalidOperationException($"模拟坏地址：{address}");
                return new byte[Math.Max(1, (int)length * 2)];
            }

            public bool[] ReadBits(string address, ushort count)
            {
                ReadCount++;
                if (FailAddress != null && string.Equals(address, FailAddress, StringComparison.Ordinal))
                    throw new InvalidOperationException($"模拟坏地址：{address}");
                return new bool[Math.Max(1, (int)count)];
            }

            public T Read<T>(string address) where T : struct => throw new NotSupportedException();
            public void Write(string address, object value) => throw new NotSupportedException();
            public void WriteBytes(string address, byte[] data) => throw new NotSupportedException();
            public void Dispose() { }
        }

        private sealed class FakeConfig : ConnectionConfigBase
        {
            public override ConnectionConfigBase Clone() => new FakeConfig();
        }

        /// <summary>
        /// 返回脚本化字节的假连接（数组解码用）：按请求长度铺满，内容取构造时给的字节。
        /// </summary>
        private sealed class ArrayBytesConnection : ICommunicationConnection
        {
            private readonly byte[] _data;

            public ArrayBytesConnection(byte[] data) => _data = data;

            public string ConnectionName => "ARRAY";
            public CommunicationType Type => CommunicationType.ModbusTcp;
            public bool IsConnected => true;
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() => true;
            public void Disconnect() { }
            public bool TestConnection() => true;

            public byte[] ReadBytes(string address, ushort length)
            {
                var result = new byte[Math.Max(1, (int)length * 2)];
                Array.Copy(_data, result, Math.Min(_data.Length, result.Length));
                return result;
            }

            public bool[] ReadBits(string address, ushort count) => new bool[Math.Max(1, (int)count)];
            public T Read<T>(string address) where T : struct => throw new NotSupportedException();
            public void Write(string address, object value) => throw new NotSupportedException();
            public void WriteBytes(string address, byte[] data) => throw new NotSupportedException();
            public void Dispose() { }
        }

        /// <summary>返回脚本化位数组的假连接（bool[] 位区解码用）。</summary>
        private sealed class ArrayBitsConnection : ICommunicationConnection
        {
            private readonly bool[] _bits;

            public ArrayBitsConnection(bool[] bits) => _bits = bits;

            public string ConnectionName => "BITS";
            public CommunicationType Type => CommunicationType.ModbusTcp;
            public bool IsConnected => true;
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() => true;
            public void Disconnect() { }
            public bool TestConnection() => true;

            public byte[] ReadBytes(string address, ushort length) => new byte[Math.Max(1, (int)length * 2)];

            public bool[] ReadBits(string address, ushort count)
            {
                var result = new bool[Math.Max(1, (int)count)];
                Array.Copy(_bits, result, Math.Min(_bits.Length, result.Length));
                return result;
            }

            public T Read<T>(string address) where T : struct => throw new NotSupportedException();
            public void Write(string address, object value) => throw new NotSupportedException();
            public void WriteBytes(string address, byte[] data) => throw new NotSupportedException();
            public void Dispose() { }
        }

        /// <summary>
        /// 返回固定原始字节的假连接（验证"读路径真的做了换算"这类端到端行为）。
        /// <para>value 按大端 Int16 编成 2 字节——与 Modbus 寄存器区 1 单元 = 2 字节一致。</para>
        /// </summary>
        private sealed class RawBytesConnection : ICommunicationConnection
        {
            private readonly short _value;

            public RawBytesConnection(short value) => _value = value;

            public string ConnectionName => "RAW";
            public CommunicationType Type => CommunicationType.ModbusTcp;
            public bool IsConnected => true;
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

            public bool Connect() => true;
            public void Disconnect() { }
            public bool TestConnection() => true;

            public byte[] ReadBytes(string address, ushort length)
            {
                var data = new byte[Math.Max(1, (int)length * 2)];
                data[0] = (byte)((_value >> 8) & 0xFF);
                data[1] = (byte)(_value & 0xFF);
                return data;
            }

            public bool[] ReadBits(string address, ushort count) => new bool[Math.Max(1, (int)count)];
            public T Read<T>(string address) where T : struct => throw new NotSupportedException();
            public void Write(string address, object value) => throw new NotSupportedException();
            public void WriteBytes(string address, byte[] data) => throw new NotSupportedException();
            public void Dispose() { }
        }

        private sealed class FakeDriverFactory : ICommunicationDriverFactory
        {
            public FakeDriverFactory(string id, Type configType, string? displayName = null)
            {
                DriverId = id;
                ConfigType = configType;
                DisplayName = displayName ?? "假协议";
            }

            public string DriverId { get; }
            public string DisplayName { get; }
            public string Description => "断言用假驱动";
            public Type? ConfigType { get; }

            public ICommunicationConnection Create(ConnectionConfigBase config)
                => new ScriptedConnection(null);
        }

        #endregion


        /// <summary>
        /// B3 + B4 的回归钉子。
        ///
        /// <para><b>B3</b>：把"哪些协议可用"这句话钉成<b>两份清单必须一致</b>——
        /// Core 的 <see cref="CommunicationProtocols.Implemented"/> 与
        /// <see cref="ConnectionFactoryManager.SupportedTypes"/>。谁加了工厂忘了改清单（或反之）本组当场失败。
        /// 为什么必须有这条断言而不是让代码自己取工厂清单：Core 层不能引用 Communication 层，取不到，
        /// 只能手工双写 + 用回归钉住（详见 <see cref="CommunicationProtocols"/> 的注释）。</para>
        ///
        /// <para><b>B4</b>：<see cref="AdvancedCommunicationManager.AddConnection"/> 中途失败必须回滚干净。
        /// 触发手段是"给 ConnectionStateChanged 挂一个抛异常的订阅者"——该事件在 AddConnection 里
        /// <b>五个登记动作全部做完之后</b>才触发（见方法内注释），故异常抛出时正好落在"最该回滚"的那一刻。
        /// 订阅者抛异常在生产里并不罕见（UI 订阅者的任何一句都可能抛），而事件广播本身没有 try/catch，
        /// 异常会一路穿透回 AddConnection。</para>
        /// </summary>
        // ==================================================================
        //  B5/B6：MC / FINS / FreeProtocol
        // ==================================================================

        private static void McFinsFreeProtocolContract()
        {
            Section("B5 MC / FINS / FreeProtocol 契约");

            var mc = new CommunicationConfig(CommunicationType.MitsubishiMc);
            Check("B5-1 MC 配置 Validate 通过且 Config 类型正确",
                mc.Validate(out string mcErr) && mc.Config is MitsubishiMcConfig,
                mcErr.Length == 0 ? $"Config={mc.Config?.GetType().Name}" : mcErr);

            var fins = new CommunicationConfig(CommunicationType.OmronFins);
            Check("B5-1 FINS 配置 Validate 通过且 Config 类型正确",
                fins.Validate(out string finsErr) && fins.Config is OmronFinsConfig,
                finsErr.Length == 0 ? $"Config={fins.Config?.GetType().Name}" : finsErr);

            var free = new CommunicationConfig(CommunicationType.FreeProtocol);
            Check("B5-1 FreeProtocol 配置 Validate 通过且 Config 类型正确",
                free.Validate(out string freeErr) && free.Config is FreeProtocolConfig,
                freeErr.Length == 0 ? $"Config={free.Config?.GetType().Name}" : freeErr);

            Check("B5-2 MC 地址 D100 通过（三菱）",
                MitsubishiMcConnection.TryValidateAddress("D100", McVendor.Mitsubishi, out string mcAddrErr), mcAddrErr);
            Check("B5-2 MC 地址 @@@ 被拒",
                !MitsubishiMcConnection.TryValidateAddress("@@@", McVendor.Mitsubishi, out _), "");
            Check("B5-2 FINS 地址 D100 通过",
                OmronFinsConnection.TryValidateAddress("D100", out string finsAddrErr), finsAddrErr);
            Check("B5-2 FINS 地址 @@@ 被拒",
                !OmronFinsConnection.TryValidateAddress("@@@", out _), "");

            bool factoryOk = true;
            string factoryDetail = "";
            try
            {
                factoryOk =
                    ConnectionFactoryManager.Instance.CreateConnection(mc) is MitsubishiMcConnection &&
                    ConnectionFactoryManager.Instance.CreateConnection(fins) is OmronFinsConnection &&
                    ConnectionFactoryManager.Instance.CreateConnection(free) is FreeProtocolConnection;
            }
            catch (Exception ex)
            {
                factoryOk = false;
                factoryDetail = ex.GetType().Name + ": " + ex.Message;
            }
            Check("B5-3 工厂创建 MC/FINS/Free 连接类型正确", factoryOk, factoryDetail);

            var udp = new MitsubishiMcConfig { ProtocolType = McProtocolType.Udp };
            Check("B5-4 MC UDP 在 Validate 阶段被拒（不等到现场才炸）",
                !udp.Validate(out string udpErr) && udpErr.Contains("UDP"),
                udpErr);

            // R84：FlatPropertyGrid 的枚举读 [Description]；只写 [Display] 会显示英文
            bool vendorDesc = Enum.GetValues<McVendor>().All(v =>
                v.GetType().GetField(v.ToString())?
                    .GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 });
            Check("B5-5 McVendor 上屏走中文 [Description]", vendorDesc, "");

            bool encodingDesc = Enum.GetValues<FreeProtocolEncoding>().All(v =>
                v.GetType().GetField(v.ToString())?
                    .GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 });
            Check("B5-5 FreeProtocolEncoding 上屏走 [Description]", encodingDesc, "");

            var keyenceAscii = new MitsubishiMcConfig
            {
                Vendor = McVendor.Keyence,
                FrameFormat = McFrameFormat.Ascii,
            };
            Check("B5-6 基恩士/松下 + ASCII 在 Validate 阶段被拒",
                !keyenceAscii.Validate(out string kaErr) && kaErr.Contains("二进制"),
                kaErr);
        }

        private static void McFinsBatchPlanning()
        {
            Section("B7 MC/FINS 批量规划");

            var mc = new McAddress { Area = McArea.D, Offset = "100", DataType = DataValueType.Int16, Length = 1 };
            Check("B7-1 MC D100 可批量（Mc / Start=100 / 1 字）",
                mc.TryCreatePollAddress(out var mcPoll) &&
                mcPoll.Protocol == PollProtocol.Mc && mcPoll.Start == 100 && mcPoll.SpanUnits == 1,
                mcPoll == null ? "null" : $"{mcPoll.Protocol}/{mcPoll.Start}/{mcPoll.SpanUnits}");

            var mcBit = new McAddress { Area = McArea.M, Offset = "100", DataType = DataValueType.Boolean };
            Check("B7-1 MC 位区 M100 不批量（走单读）", !mcBit.TryCreatePollAddress(out _), "");

            var fins = new FinsAddress { Area = FinsArea.D, Offset = "100", DataType = DataValueType.Int16, Length = 1 };
            Check("B7-1 FINS D100 可批量（Fins / Start=100 / 1 字）",
                fins.TryCreatePollAddress(out var finsPoll) &&
                finsPoll.Protocol == PollProtocol.Fins && finsPoll.Start == 100 && finsPoll.SpanUnits == 1,
                finsPoll == null ? "null" : $"{finsPoll.Protocol}/{finsPoll.Start}/{finsPoll.SpanUnits}");

            var finsBit = new FinsAddress { Area = FinsArea.D, Offset = "100", DataType = DataValueType.Boolean, BitOffset = 0 };
            Check("B7-1 FINS D100.0 位地址不批量（走单读）", !finsBit.TryCreatePollAddress(out _), "");

            var mcVars = Enumerable.Range(0, 1000)
                .Select(i => NewBatchVariable($"mc{i}", new McAddress
                {
                    Area = McArea.D, Offset = i.ToString(), DataType = DataValueType.Int16, Length = 1
                }))
                .ToList();
            var mcPlanner = PollBatchPlanner.Build(mcVars, PollTransportKind.Ethernet);
            Check("B7-2 MC 1000 点：2 段 / 0 兜底",
                mcPlanner.SegmentCount == 2 && mcPlanner.FallbackCount == 0,
                $"段 {mcPlanner.SegmentCount} / 兜底 {mcPlanner.FallbackCount}");

            var finsVars = Enumerable.Range(0, 1000)
                .Select(i => NewBatchVariable($"fins{i}", new FinsAddress
                {
                    Area = FinsArea.D, Offset = i.ToString(), DataType = DataValueType.Int16, Length = 1
                }))
                .ToList();
            var finsPlanner = PollBatchPlanner.Build(finsVars, PollTransportKind.Ethernet);
            Check("B7-2 FINS 1000 点：2 段 / 0 兜底",
                finsPlanner.SegmentCount == 2 && finsPlanner.FallbackCount == 0,
                $"段 {finsPlanner.SegmentCount} / 兜底 {finsPlanner.FallbackCount}");
        }

        private static CommunicationVariable NewBatchVariable(string name, DeviceAddressBase address)
            => new()
            {
                ConnectionName = "BATCH",
                VariableName = name,
                Address = address.Address,
                AddressConfig = address,
                ValueType = typeof(short).AssemblyQualifiedName,
                AccessMode = VariableAccessMode.ReadWrite,
            };

        private static void FreeProtocolLoopback()
        {
            Section("B6 FreeProtocol 本地 TCP 回环");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var server = Task.Run(() =>
            {
                try
                {
                    using var client = listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    var buffer = new byte[512];
                    var pending = new StringBuilder();
                    while (true)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) break;
                        pending.Append(Encoding.ASCII.GetString(buffer, 0, n));

                        string text = pending.ToString();
                        int idx;
                        while ((idx = text.IndexOf("\r\n", StringComparison.Ordinal)) >= 0)
                        {
                            string line = text.Substring(0, idx);
                            text = text.Substring(idx + 2);
                            pending.Clear().Append(text);

                            string response = line.StartsWith("READ", StringComparison.Ordinal) ? "42\r\n"
                                            : line.StartsWith("SET", StringComparison.Ordinal) ? "OK\r\n"
                                            : "ERR\r\n";
                            byte[] bytes = Encoding.ASCII.GetBytes(response);
                            stream.Write(bytes, 0, bytes.Length);
                            stream.Flush();
                        }
                    }
                }
                catch { /* 回环服务器异常不拖垮断言 */ }
            });

            var cfg = new FreeProtocolConfig
            {
                IpAddress = "127.0.0.1",
                Port = port,
                FrameTerminator = "\\r\\n",
                ResponseTimeoutMs = 2000,
            };

            using var conn = new FreeProtocolConnection(cfg);
            bool connected = conn.Connect();
            Check("B6-1 连接本地回环", connected, conn.LastConnectError ?? "");

            if (connected)
            {
                try
                {
                    Check("B6-2 Read<int>(\"READ\") 解析 42", conn.Read<int>("READ") == 42, "");
                }
                catch (Exception ex) { Check("B6-2 Read<int>(\"READ\") 解析 42", false, ex.Message); }

                try
                {
                    conn.Write("SET {0}", 7);
                    Check("B6-3 Write 模板 \"SET {0}\" 发送并收到 OK", true, "");
                }
                catch (Exception ex) { Check("B6-3 Write 模板 \"SET {0}\" 发送并收到 OK", false, ex.Message); }

                try
                {
                    Check("B6-4 连续读仍正确（帧边界未串）", conn.Read<int>("READ") == 42, "");
                }
                catch (Exception ex) { Check("B6-4 连续读仍正确（帧边界未串）", false, ex.Message); }
            }

            conn.Disconnect();
            listener.Stop();
            server.Wait(2000);
        }

        private static void CommunicationProtocolsAndAddRollback()
        {
            Section("B3 协议清单合一 / B4 添加连接失败回滚");

            // ================= B3-1：两份清单必须一致（跨层一致性，改一边忘另一边当场失败） =================
            var implemented = CommunicationProtocols.Implemented.OrderBy(t => t).ToArray();
            var fromFactories = ConnectionFactoryManager.Instance.SupportedTypes.OrderBy(t => t).ToArray();
            Check("B3-1 CommunicationProtocols.Implemented 与连接工厂注册表完全一致（跨层手工同步的自动校验）",
                implemented.SequenceEqual(fromFactories),
                $"清单=[{string.Join("/", implemented)}] 工厂=[{string.Join("/", fromFactories)}]");

            var unimplemented = Enum.GetValues<CommunicationType>()
                .Where(t => !CommunicationProtocols.IsImplemented(t)).ToArray();
            Check("B3-1 前提：确实存在「未实现」协议（否则下面几条对比毫无意义）",
                implemented.Length > 0 && unimplemented.Length > 0,
                $"已实现 {implemented.Length} 个 / 未实现 {unimplemented.Length} 个：[{string.Join("/", unimplemented)}]");

            // ================= B3-2：选中未实现协议必须"安静 + 人话" =================
            foreach (var protocol in unimplemented)
            {
                CommunicationConfig? probe = null;
                bool threw = false;
                string detail = "";
                string error = "";

                try
                {
                    // 与"属性面板下拉选中等价"的动作：走 Protocol 的 setter（内部 OnChanged）。
                    // 改造前选中 FreeProtocol 就是在这里抛 ArgumentOutOfRangeException——
                    // 该交互发生在"创建新通信"弹窗内部，调用方的 try/catch 根本拦不到，程序当场没了。
                    probe = new CommunicationConfig { Protocol = protocol };
                    detail = $"Config={probe.Config?.Type.ToString() ?? "null"}";
                }
                catch (Exception ex)
                {
                    threw = true;
                    detail = ex.GetType().Name + ": " + ex.Message;
                }

                Check($"B3-2 下拉选中未实现协议 {protocol} 不抛异常（属性写入路径必须安静）",
                    !threw, detail);

                Check($"B3-2 未实现协议 {protocol} 保留原 Config 而不置 null（属性面板的嵌套分组才有处展开）",
                    !threw && probe!.Config != null,
                    "Config=" + (probe?.Config?.Type.ToString() ?? "null"));

                bool validated = !threw && probe!.Validate(out error);
                Check($"B3-2 未实现协议 {protocol} 的 Validate 报「尚未实现」+ 可用清单，而非含糊的「协议类型不匹配」",
                    !validated && error.Contains("尚未实现") && error.Contains(CommunicationProtocols.ImplementedText),
                    error);

                // 哪怕有人绕过 Validate 直接建连接，工厂层也得兜底（不然就是又一次"走到最后一步才炸"）
                bool notSupported = false;
                string factoryDetail = "";
                try
                {
                    ConnectionFactoryManager.Instance.CreateConnection(new CommunicationConfig { Protocol = protocol });
                    factoryDetail = "竟然没抛";
                }
                catch (NotSupportedException ex) { notSupported = true; factoryDetail = ex.Message; }
                catch (Exception ex) { factoryDetail = "异常类型不对：" + ex.GetType().Name + " - " + ex.Message; }

                Check($"B3-2 工厂层对未实现协议 {protocol} 兜底抛 NotSupportedException（Validate 被绕过也不放行）",
                    notSupported, factoryDetail);
            }

            // ================= B3-3：构造器回归钉（钉住被删掉的那行重复赋值） =================
            // 改造前 ctor 里 `Config = CreateConfig(protocol)` 会覆盖 OnChanged 刚建好的那份，
            // 而 CreateConfig 不赋 Type（默认 0 = ModbusTcp）→ CommunicationConfig(ModbusRtu)/(SiemensS7)
            // 造出来的对象 Protocol 与 Config.Type 不符，Validate 必报"协议类型不匹配"。
            foreach (var protocol in implemented)
            {
                var cfg = new CommunicationConfig(protocol);
                bool ok = cfg.Validate(out string ctorError);
                Check($"B3-3 new CommunicationConfig({protocol}) 立即 Validate 通过且 Config.Type 与协议一致",
                    ok && cfg.Config?.Type == protocol,
                    ok ? $"Config.Type={cfg.Config?.Type}" : ctorError);
            }

            // ================= B3-4：选错协议不会把对象卡死 =================
            var recoverProtocol = unimplemented[0];
            var recovered = new CommunicationConfig { Protocol = recoverProtocol };
            recovered.Protocol = CommunicationType.ModbusTcp;
            bool recoverOk = recovered.Validate(out string recoverError);
            Check($"B3-4 误选 {recoverProtocol} 后再改回 ModbusTcp：Validate 立刻恢复正常（保留原 Config 不会卡死对象）",
                recoverOk && recovered.Config?.Type == CommunicationType.ModbusTcp,
                recoverOk ? $"Config.Type={recovered.Config?.Type}" : recoverError);

            // ================= B4：添加连接中途失败必须回滚 =================
            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = false };
            const string b4Name = "B4_Rollback";

            // 抛之前先取证：此刻"五个登记动作"是否都已完成——这是"回滚"这件事成立的前提。
            // 只有确认了"失败点在全登记之后"，下面"回滚干净"的断言才有意义（否则可能测的是"还没登记就失败"）。
            bool registeredAtFailure = false, listedAtFailure = false;
            EventHandler<ConnectionStateChangedEventArgs> bomb = (_, _) =>
            {
                registeredAtFailure = manager.GetConnection(b4Name) != null;
                listedAtFailure = manager.GetAllConnections().Any(c => c.ConnectionName == b4Name);
                throw new InvalidOperationException("B4 测试：订阅者故意抛异常");
            };
            manager.ConnectionStateChanged += bomb;

            var b4Config = new CommunicationConfig(CommunicationType.ModbusTcp) { ConnectionName = b4Name };
            bool addThrew = false;
            string addDetail = "";
            try
            {
                manager.AddConnection(b4Config);
                addDetail = "竟然没抛";
            }
            catch (Exception ex) { addThrew = true; addDetail = ex.GetType().Name + ": " + ex.Message; }

            Check("B4-1 前提：失败发生在「五个登记动作全做完之后」（订阅者抛异常时连接已在册、也已进列表）",
                addThrew && registeredAtFailure && listedAtFailure,
                $"抛={addThrew} / 当时在 _connections={registeredAtFailure} / 当时在列表={listedAtFailure}");

            Check("B4-1 失败后 GetConnection 查不到它（不留「界面看不见、线程却在跑」的隐形僵尸）",
                manager.GetConnection(b4Name) == null, "");
            Check("B4-1 失败后连接清单里也没有它（UI 列表不该出现删不掉的假条目）",
                manager.GetAllConnections().All(c => c.ConnectionName != b4Name),
                "[" + string.Join(",", manager.GetAllConnections().Select(c => c.ConnectionName)) + "]");
            Check("B4-1 失败后 ConnectionCount 归零",
                manager.ConnectionCount == 0, manager.ConnectionCount.ToString());
            Check("B4-1 失败后 Worker 注册表也空了（登记进去的线程已被释放，不是只删了引用）",
                ((ConcurrentDictionary<string, ConnectionWorker>)WorkersField!.GetValue(manager)!).IsEmpty,
                "剩余 " + ((ConcurrentDictionary<string, ConnectionWorker>)WorkersField!.GetValue(manager)!).Count + " 条");

            // B4-2：僵尸的典型症状是"开头那句 ContainsKey 预检永远为真"，同名重试只会一直得到"已存在同名连接"
            manager.ConnectionStateChanged -= bomb;
            bool retryOk = false;
            string retryDetail = "";
            try { retryOk = manager.AddConnection(b4Config); }
            catch (Exception ex) { retryDetail = ex.GetType().Name + ": " + ex.Message; }
            Check("B4-2 同名可立即重试成功（旧实现因残留登记会永远报「已存在同名连接」，除了删连接无路可走）",
                retryOk && manager.GetConnection(b4Name) != null,
                retryOk ? $"ConnectionCount={manager.ConnectionCount}" : retryDetail);

            // B4-3：护栏——回滚只摘"自己写进去的那一份"。
            // 方法开头那句 ContainsKey 预检与后面的写入之间没有锁（TOCTOU），并发下同一名字可能已由
            // 另一路 Add 写入了它自己的对象；若回滚按名字盲删，就会把别人的连接连线程一起干掉。
            // 这里拿一个"同名但不是登记对象"的 config 去调回滚，真实连接必须毫发无损。
            var foreign = new CommunicationConfig(CommunicationType.ModbusTcp) { ConnectionName = b4Name };
            bool guardOk = false;
            string guardDetail = "";
            try
            {
                InvokeRollbackFailedAdd(manager, foreign, null, null);
                guardOk = manager.GetConnection(b4Name) != null
                    && manager.ConnectionCount == 1
                    && manager.GetAllConnections().Any(c => c.ConnectionName == b4Name);
                guardDetail = $"Count={manager.ConnectionCount}";
            }
            catch (Exception ex) { guardDetail = ex.GetType().Name + ": " + ex.Message; }

            Check("B4-3 拿「同名但非登记对象」去回滚：真实连接毫发无损（ReferenceEquals 护栏，防 TOCTOU 误删）",
                guardOk, guardDetail);

            manager.RemoveConnection(b4Name);
            Check("B4-3 收尾：真实连接可正常移除（回滚护栏没把管理器弄坏）",
                manager.ConnectionCount == 0 && manager.GetConnection(b4Name) == null,
                $"ConnectionCount={manager.ConnectionCount}");
        }

        /// <summary>
        /// B4 的私有回滚方法 <c>RollbackFailedAdd</c>。护栏断言要"拿别人的对象"去调它，
        /// 生产路径上无处可调（正常调用都在 catch 里），只能反射。
        /// </summary>
        private static readonly MethodInfo? ManagerRollbackFailedAddMethod = typeof(AdvancedCommunicationManager)
            .GetMethod("RollbackFailedAdd", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>反射调私有 <c>RollbackFailedAdd</c>；取不到直接抛，不静默放过（否则护栏断言会变成空跑）</summary>
        private static void InvokeRollbackFailedAdd(
            AdvancedCommunicationManager manager,
            CommunicationConfig config,
            ICommunicationConnection? connection,
            ConnectionWorker? worker)
        {
            if (ManagerRollbackFailedAddMethod == null)
                throw new InvalidOperationException(
                    "反射未取到 AdvancedCommunicationManager.RollbackFailedAdd——方法名可能被改过，B4 断言需要同步更新");
            ManagerRollbackFailedAddMethod.Invoke(manager, new object?[] { config, connection, worker });
        }

        #endregion

        #region C16 KeepAlive 死配置落地

        /// <summary>
        /// <para>「开启 KeepAlive / 心跳间隔(ms)」是 <see cref="EthernetConfigBase"/> 上的两个契约属性
        /// （默认 true / 30000ms），此前在 Communication 树里<b>零消费</b>——界面上勾不勾都一样，纯属死配置。</para>
        /// <para>落地方式：构造连接时下发到 HSL 的 <c>SocketKeepAliveTime</c>——HSL 在 <c>ConnectServer</c>
        /// 成功那一刻对底层 socket 做 IOControl（<c>SetKeepAlive(time, interval)</c>），即"连接成功后才启用"。
        /// 关闭时不设置（保持 HSL 默认 -1），未启用时行为与落地前零变化。</para>
        /// <para>本组断言"配置值确实落到了设备对象上"（与 C7/C8 同一反射口径，不真建连、离线可跑）；
        /// 连接成功后 socket 选项真的翻开的运行时证据见 CommPerf 的 F1 段（本机 TcpListener 靶子）。</para>
        /// </summary>
        private static void KeepAliveConfigPropagation()
        {
            Section("C16 KeepAlive 死配置落地（EnableKeepAlive/KeepAliveIntervalMs → device.SocketKeepAliveTime）");

            // 契约默认值：EnableKeepAlive=true / interval=30000——落地后默认配置即启用 30s TCP keepalive
            using (var mt = new ModbusTcpConnection(new ModbusTcpConfig { IpAddress = "127.0.0.1", Port = 502 }))
            {
                int actual = DeviceIntProperty(mt, "SocketKeepAliveTime");
                Check("C16-1 ModbusTcp：契约默认值（true/30000）已下发到设备（HSL 建连成功后按它启用）",
                    actual == 30000, $"设备 SocketKeepAliveTime={actual}");
            }

            using (var mtOff = new ModbusTcpConnection(new ModbusTcpConfig
            { IpAddress = "127.0.0.1", Port = 502, EnableKeepAlive = false }))
            {
                int actual = DeviceIntProperty(mtOff, "SocketKeepAliveTime");
                Check("C16-2 ModbusTcp：EnableKeepAlive=false → 不设置（保持 HSL 默认 -1，未启用时零行为变化）",
                    actual == -1, $"设备 SocketKeepAliveTime={actual}");
            }

            using (var mtCustom = new ModbusTcpConnection(new ModbusTcpConfig
            { IpAddress = "127.0.0.1", Port = 502, EnableKeepAlive = true, KeepAliveIntervalMs = 5000 }))
            {
                int actual = DeviceIntProperty(mtCustom, "SocketKeepAliveTime");
                Check("C16-3 ModbusTcp：自定义间隔 5000 如实下发（不是恰好等于默认值的假通过）",
                    actual == 5000, $"设备 SocketKeepAliveTime={actual}");
            }

            // 同一条基类字段，不允许只有 Modbus 生效
            using (var s7 = new SiemensS7Connection(new SiemensS7Config { IpAddress = "192.168.0.1" }))
            {
                int actual = DeviceIntProperty(s7, "SocketKeepAliveTime");
                Check("C16-4 SiemensS7：契约默认值同样下发（与 Modbus 同一基类路径）",
                    actual == 30000, $"设备 SocketKeepAliveTime={actual}");
            }

            using (var s7Off = new SiemensS7Connection(new SiemensS7Config
            { IpAddress = "192.168.0.1", EnableKeepAlive = false }))
            {
                int actual = DeviceIntProperty(s7Off, "SocketKeepAliveTime");
                Check("C16-5 SiemensS7：EnableKeepAlive=false → 不设置（HSL 默认 -1）",
                    actual == -1, $"设备 SocketKeepAliveTime={actual}");
            }
        }

        #endregion

        #region C18 communications.json 逐条隔离

        /// <summary>
        /// <para>加载容错语义（本轮修复点）：旧实现对整份 JSON 一次 <c>DeserializeObject</c>（all-or-nothing）——
        /// 一处语法错误或一个越权 $type 就把整份文件打成 0 条并抛异常。新实现逐条反序列化逐条登记：
        /// 单条坏点只影响该条，其余照常加载；文件级语法错误记 ERROR、加载 0 条、<b>不抛异常</b>。</para>
        /// <para>$type 白名单 SerializationBinder 与 JsonSerializerSettings 原样保留（安全口径不变），
        /// 所以"越权类型"从"炸掉整份"变成"只跳过那一条"。</para>
        /// </summary>
        private static void ConfigJsonPerItemIsolation()
        {
            Section("C18 communications.json 逐条隔离（单条坏点只影响该条；文件级语法错误加载 0 条且不抛）");

            string dir = Path.Combine(Path.GetTempPath(), "CommChecksJson");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "case.json");

            List<CommunicationConfig> LoadList(string json)
            {
                File.WriteAllText(file, json, new System.Text.UTF8Encoding(false));
                using var mgr = new AdvancedCommunicationManager { ConfigFilePath = file };
                mgr.LoadConfig(); // 新语义：任何内容级错误都不抛异常
                return mgr.GetAllConnections();
            }

            const string good = "{\"connectionName\":\"J_A\",\"protocol\":0," +
                "\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":10010,\"type\":0,\"timeoutMs\":2500},\"autoStart\":false}";
            const string badType = "{\"connectionName\":\"J_E\",\"protocol\":0," +
                "\"config\":{\"$type\":\"System.DateTime, System.Private.CoreLib\"},\"autoStart\":false}";
            const string badProto = "{\"connectionName\":\"J_B\",\"protocol\":3," +
                "\"config\":{\"ipAddress\":\"127.0.0.1\",\"port\":9600,\"type\":0}}";

            Check("C18-1 正常单条 → 加载 1 条（基线不回归）",
                LoadList("[" + good + "]") is { Count: 1 }, "");

            Check("C18-2 文件级语法错误（截断）→ 不抛异常，加载 0 条（旧实现抛 JsonReaderException 且整份丢弃）",
                LoadList("[" + good + ",{\"connectionName\":\"J_B\",\"protocol\":0,\"config\":{\"ipAddr") is { Count: 0 }, "");

            var badFirst = LoadList("[" + badType + "," + good + "]");
            Check("C18-3 $type 越权条在前、好条在后 → 只跳过坏条，好条照常加载（逐条隔离的核心证据）",
                badFirst is { Count: 1 } && badFirst[0].ConnectionName == "J_A"
                && badFirst[0].Config is ModbusTcpConfig m1 && m1.Port == 10010 && m1.TimeoutMs == 2500,
                $"加载 [{string.Join(",", badFirst.Select(c => c.ConnectionName))}]");

            Check("C18-4 $type 越权条在后、好条在前 → 同样只跳过坏条（顺序无关）",
                LoadList("[" + good + "," + badType + "]") is { Count: 1 }, "");

            Check("C18-5 未实现协议条 → 仍按 AddConnection 阶段隔离（旧语义保持）",
                LoadList("[" + good + "," + badProto + "]") is { Count: 1 }, "");

            Check("C18-6 缺字段条 → 反序列化兜底默认值（timeoutMs 缺省 3000），不算坏点",
                LoadList("[{\"connectionName\":\"J_C\",\"protocol\":0,\"config\":{\"ipAddress\":\"127.0.0.1\"}}]")
                    .SingleOrDefault()?.Config is ModbusTcpConfig m2 && m2.TimeoutMs == 3000, "");

            Check("C18-7 顶层不是数组（整份写成对象）→ 不抛异常，加载 0 条",
                LoadList(good) is { Count: 0 }, "");

            Check("C18-8 空数组 → 加载 0 条（不算错误）",
                LoadList("[]") is { Count: 0 }, "");
        }

        #endregion

        #region C17 连接拒绝分类 + 退避加速

        /// <summary>
        /// <para>修复点：HSL 对建连失败只给一句消息，refused（目标拒绝）与网络超时无法区分；
        /// 上层（<see cref="AdvancedCommunicationManager.Connect"/>）在幽灵端口上要陪等满 TimeoutMs 才认输。</para>
        /// <para>修法三件套：① 连接实现按 HSL 失败消息识别 refused（OS 文本主通道）+ 快速失败时毫秒级探测
        /// （补充通道），经新增的 <see cref="IConnectionFailureDetail"/> 上报（LastError 带 [ConnectionRefused] 标记）；
        /// ② Worker 检测到 refused 后让本次等待立即返回（重连意图不变）；
        /// ③ Worker 对 refused 用固定短间隔重试（<c>RefusedRetryIntervalMs</c>），不再指数退避拖到 30s。</para>
        /// <para>⚠ 实测基线（本机装了本地代理/TUN）：回环上连<b>活端口</b>是 0~3ms，连<b>无人监听端口</b>
        /// 要 ~2030ms 才回 ConnectionRefused（OS/代理层延迟 RST，裸 socket 实测，与 HSL 无关）——
        /// 所以 refused 的"更快"体现在：分类零额外探测开销（消息识别）、Manager.Connect 不再陪等满预算、
        /// 退避节奏用固定短间隔；socket 层那 ~2s 是 OS 底线，任何实现都省不掉。</para>
        /// <para>靶子：127.0.0.1:1（回环上必然无人监听的端口，内核/代理层最终回 RST），零外部依赖。</para>
        /// </summary>
        private static void RefusedClassificationAndBackoff()
        {
            Section("C17 连接拒绝分类 + 退避加速（[ConnectionRefused] / 幽灵端口快速失败）");

            using (var conn = new ModbusTcpConnection(new ModbusTcpConfig
            { IpAddress = "127.0.0.1", Port = 1, TimeoutMs = 3000, ReadTimeoutMs = 3000 }))
            {
                var sw = Stopwatch.StartNew();
                bool ok = conn.Connect();
                sw.Stop();
                var detail = (IConnectionFailureDetail)conn;
                Check("C17-1 ModbusTcp 幽灵端口：Connect 返回 false（前提）",
                    !ok, $"返回 {ok}");
                Check("C17-1 失败被分类为 ConnectionRefused（IConnectionFailureDetail 新能力，消息识别主通道）",
                    detail.IsLastConnectFailureRefused, $"refused={detail.IsLastConnectFailureRefused}");
                Check("C17-1 LastError 带 [ConnectionRefused] 标记（上层可区分故障类别）",
                    detail.LastConnectError?.Contains("[ConnectionRefused]") == true,
                    detail.LastConnectError ?? "null");
                Check("C17-1 refused 失败耗时 < TimeoutMs=3000（本机 OS 层拒绝延迟 ~2s 是底线；分类不额外加探测耗时）",
                    sw.ElapsedMilliseconds < 3000, $"耗时 {sw.ElapsedMilliseconds}ms");
            }

            // TCP 建连被拒与协议无关：S7 与 Modbus 必须对称
            using (var conn7 = new SiemensS7Connection(new SiemensS7Config
            { IpAddress = "127.0.0.1", Port = 1, TimeoutMs = 3000, ReadTimeoutMs = 3000 }))
            {
                bool ok = conn7.Connect();
                bool refused = conn7 is IConnectionFailureDetail d && d.IsLastConnectFailureRefused;
                Check("C17-2 SiemensS7 幽灵端口：同样分类为 ConnectionRefused（两实现对称）",
                    !ok && refused, $"连接={ok} refused={refused}");
            }

            // Manager.Connect 幽灵端口路径：refused 失败后立即结束等待，不再陪等满 TimeoutMs
            using var manager = new AdvancedCommunicationManager { AutoReconnectEnabled = true };
            var cfg = new CommunicationConfig(CommunicationType.ModbusTcp) { ConnectionName = "C17_GHOST" };
            cfg.Config.TimeoutMs = 3000;
            ((ModbusTcpConfig)cfg.Config).IpAddress = "127.0.0.1";
            ((ModbusTcpConfig)cfg.Config).Port = 1;
            ((ModbusTcpConfig)cfg.Config).RetryIntervalMs = 1000;
            manager.AddConnection(cfg);
            var ghostWorker = GetWorker(manager, "C17_GHOST");

            var swM = Stopwatch.StartNew();
            bool connected = manager.Connect("C17_GHOST");
            swM.Stop();
            Check("C17-3 Manager.Connect 幽灵端口：refused 失败即返回 false（旧实现陪等满 TimeoutMs=3000）",
                !connected && swM.ElapsedMilliseconds < 2900,
                $"返回 {connected}，耗时 {swM.ElapsedMilliseconds}ms");
            Check("C17-3 Worker.LastError 带 [ConnectionRefused] 标记",
                ghostWorker.LastError?.Contains("[ConnectionRefused]") == true,
                ghostWorker.LastError ?? "null");
            manager.RemoveConnection("C17_GHOST");

            // 退避节奏（桩连接，秒级返回，节奏可精确度量）：refused 走固定 RefusedRetryIntervalMs，
            // 不再按指数退避拖长。真实 socket 的 refused 一次要 ~2s（OS 底线），桩把它归零才能看清节奏本身
            using var stubWorker = new ConnectionWorker(new AlwaysRefusedConnection())
            {
                ReconnectBaseIntervalMs = 1000,
                RefusedRetryIntervalMs = 200,
                AutoReconnect = true
            };
            stubWorker.Start();
            var swW = Stopwatch.StartNew();
            bool stubConnected = stubWorker.Connect(1500).GetAwaiter().GetResult();
            swW.Stop();
            Check("C17-4 桩：refused 失败立即结束本次等待（不等满 1500ms，重连意图不受影响）",
                !stubConnected && swW.ElapsedMilliseconds < 800, $"返回 {stubConnected}，耗时 {swW.ElapsedMilliseconds}ms");

            int f0 = (int)(WorkerConnectFailureSerialField?.GetValue(stubWorker) ?? 0);
            Thread.Sleep(1300);
            int f1 = (int)(WorkerConnectFailureSerialField?.GetValue(stubWorker) ?? 0);
            Check("C17-4 refused 固定短间隔重试：1.3s 内 ≥5 次建连尝试（间隔 200ms；旧指数退避 base 1s 只有 1~2 次）",
                f1 - f0 >= 5, $"失败尝试 {f1 - f0} 次");
        }

        /// <summary>C17 桩：永远拒绝连接、自带 refused 分类——退避节奏只取决于 Worker，与 socket 无关</summary>
        private sealed class AlwaysRefusedConnection : ICommunicationConnection, IConnectionFailureDetail
        {
            public string ConnectionName => "STUB_REFUSED";
            public CommunicationType Type => CommunicationType.ModbusTcp;
            public bool IsConnected => false;
            public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;
            public bool IsLastConnectFailureRefused => true;
            public string? LastConnectError => "[ConnectionRefused] 桩：模拟目标拒绝连接";

            public bool Connect() => false;
            public void Disconnect() { }
            public bool TestConnection() => false;
            public void Dispose() { }
            public T Read<T>(string address) where T : struct => default;
            public void Write(string address, object value) { }
            public byte[] ReadBytes(string address, ushort length) => new byte[length];
            public void WriteBytes(string address, byte[] data) { }
            public bool[] ReadBits(string address, ushort count) => new bool[count];
        }

        #endregion

        #region 反射探针（Manager 私有注册表）

        private static readonly FieldInfo? RegisteredVariablesField = typeof(AdvancedCommunicationManager)
            .GetField("_registeredVariables", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Manager 的连接→Worker 注册表（C6 要"占住 Worker 线程"来证明调用不阻塞，只能从这里取 Worker）</summary>
        private static readonly FieldInfo? WorkersField = typeof(AdvancedCommunicationManager)
            .GetField("_workers", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Worker 的建连失败序号（每次建连失败自增，C17 用它数 refused 场景的重试节奏）</summary>
        private static readonly FieldInfo? WorkerConnectFailureSerialField = typeof(ConnectionWorker)
            .GetField("_connectFailureSerial", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 取连接实现里私有的 HSL 设备实例（<c>_device</c>）。走反射是为了让断言工程不必引用
        /// HSL 命名空间——三种连接实现各自持有 <c>ModbusTcpNet</c> / <c>SiemensS7Net</c> / <c>ModbusRtu</c>。
        /// </summary>
        private static object GetDevice(ICommunicationConnection connection)
            => connection.GetType()
                .GetField("_device", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(connection)!;

        /// <summary>读设备上的一个 int 属性（C7 用 <c>ConnectTimeOut</c>、C8 用 <c>ReceiveTimeOut</c>）</summary>
        private static int DeviceIntProperty(ICommunicationConnection connection, string propertyName)
        {
            var device = GetDevice(connection);
            return (int)device.GetType().GetProperty(propertyName)!.GetValue(device)!;
        }

        /// <summary>设备的 TCP 建连超时（<c>DeviceTcpNet.ConnectTimeOut</c>），C7 用</summary>
        private static int DeviceConnectTimeOut(ICommunicationConnection connection)
            => DeviceIntProperty(connection, "ConnectTimeOut");

        /// <summary>设备的读超时（<c>BinaryCommunication.ReceiveTimeOut</c>，TCP 与串口共用），C8 用</summary>
        private static int DeviceReceiveTimeOut(ICommunicationConnection connection)
            => DeviceIntProperty(connection, "ReceiveTimeOut");

        /// <summary>
        /// 读设备 <c>ByteTransform</c> 上的 <c>DataFormat</c>（C10 用）。返回枚举名而非枚举值：
        /// CommChecks 不引用 HSL 命名空间（<c>IByteTransform.DataFormat</c> 是 HSL 的 <c>DataFormat</c>），
        /// 且两套枚举（本项目 <c>ByteOrderFormat</c> 与 HSL <c>DataFormat</c>）成员顺序不同，比名字才安全。
        /// </summary>
        private static string DeviceByteTransformFormat(ICommunicationConnection connection)
        {
            var device = GetDevice(connection);
            var transform = device.GetType().GetProperty("ByteTransform")!.GetValue(device)!;
            return transform.GetType().GetProperty("DataFormat")!.GetValue(transform)!.ToString()!;
        }

        /// <summary>
        /// <c>HslHelper</c> 是 internal 类，CommChecks 够不着，只能按名反射取。
        /// 取到后缓存 <c>MinByteCount(Type)</c> 的 <see cref="MethodInfo"/>，C11 用。
        /// 从 <see cref="SiemensS7Connection"/> 所在程序集找：HslHelper 与它同在实现层，必然同程序集。
        /// </summary>
        private static readonly MethodInfo? HslHelperMinByteCountMethod =
            typeof(SiemensS7Connection).Assembly
                .GetType("VisionMaster.Communications.HslHelper")
                ?.GetMethod("MinByteCount", BindingFlags.Public | BindingFlags.Static);

        /// <summary>反射调 internal 的 <c>HslHelper.MinByteCount(Type)</c>（C11 用）；取不到直接失败，不静默放过</summary>
        private static int HslHelperMinByteCount(Type type)
        {
            if (HslHelperMinByteCountMethod == null)
                throw new InvalidOperationException("反射未取到 HslHelper.MinByteCount——internal 类名/方法名可能被改过，C11 需要同步更新");
            return (int)HslHelperMinByteCountMethod.Invoke(null, new object[] { type })!;
        }

        private static ConnectionWorker GetWorker(AdvancedCommunicationManager manager, string connectionName)
            => ((ConcurrentDictionary<string, ConnectionWorker>)WorkersField!.GetValue(manager)!)[connectionName];

        private static bool IsRegistered(AdvancedCommunicationManager manager, string connectionName, string variableName)
            => RegisteredVariable(manager, connectionName, variableName) != null;

        private static CommunicationVariable? RegisteredVariable(
            AdvancedCommunicationManager manager, string connectionName, string variableName)
        {
            if (RegisteredVariablesField?.GetValue(manager) is not System.Collections.IDictionary outer)
                return null;
            if (outer[connectionName] is not System.Collections.IDictionary inner)
                return null;
            return inner[variableName] as CommunicationVariable;
        }

        #endregion

        #region 断言框架

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"---- {title} ----");
        }

        private static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  →  " + detail)}");
        }

        private static void Finish()
        {
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        #endregion
    }
}
