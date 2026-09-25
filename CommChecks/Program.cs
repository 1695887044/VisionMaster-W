using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UI.Attributes;
using VisionMaster;
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
            WritePriorityAndBackpressure();
            WaitSchedulingAndLogThrottleKey();
            GapToleranceByTransport();
            CommunicationProtocolsAndAddRollback();
            // 本段是唯一会真发起 TCP 连接的断言（靶子是本机未监听端口，内核秒拒），放最后
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
        }

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
            VariableAccessMode mode = VariableAccessMode.ReadOnly)
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
                AccessMode = mode
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

        #region B3 协议清单合一 / B4 添加连接失败回滚

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

        #region 反射探针（Manager 私有注册表）

        private static readonly FieldInfo? RegisteredVariablesField = typeof(AdvancedCommunicationManager)
            .GetField("_registeredVariables", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Manager 的连接→Worker 注册表（C6 要"占住 Worker 线程"来证明调用不阻塞，只能从这里取 Worker）</summary>
        private static readonly FieldInfo? WorkersField = typeof(AdvancedCommunicationManager)
            .GetField("_workers", BindingFlags.Instance | BindingFlags.NonPublic);

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
