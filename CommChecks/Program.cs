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
    /// C9 串口隐藏无效的「超时时间(ms)」（SerialConfig 用 override 把它从属性面板摘掉）。</para>
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
