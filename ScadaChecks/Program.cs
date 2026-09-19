using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Prism.Dialogs;
using Prism.Mvvm;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using ScadaEditorVM = VisionMaster.ViewModels.ScadaEditorViewModel;
using ScadaToolboxVM = VisionMaster.ViewModels.ScadaToolboxViewModel;
using ScadaPropertyVM = VisionMaster.ViewModels.ScadaPropertyViewModel;
using ScadaPropertyRow = VisionMaster.ViewModels.ScadaPropertyRow;
using ScadaPropertyRowBase = VisionMaster.ViewModels.ScadaPropertyRowBase;
using ScadaEventRow = VisionMaster.ViewModels.ScadaEventRow;
using ScadaActionTypeOption = VisionMaster.ViewModels.ScadaActionTypeOption;
using ScadaVariablePickerVM = VisionMaster.ViewModels.DialogViewModels.ScadaVariablePickerViewModel;
using ScadaRunWindowSettingsVM = VisionMaster.ViewModels.DialogViewModels.ScadaRunWindowSettingsViewModel;
using ScadaRuntimeWindow = VisionMaster.Views.ScadaRuntimeWindow;
using ScadaPropertyView = VisionMaster.Views.ScadaPropertyView;
using ScadaVariablePickerView = VisionMaster.Views.DialogViews.ScadaVariablePickerView;

namespace ScadaChecks
{
    /// <summary>
    /// SCADA 侧非 GUI 断言宿主（S0 阶段：变量稳定身份 → 绑定解析器 → 改名级联）。
    ///
    /// 为什么单独开工程而不是塞进 FlowCanvasChecks：
    /// 两者关注点不同——FlowCanvasChecks 校验"流程图纸/画布交互"，本工程校验"SCADA 数据面"。
    /// 合成一个会让任何一侧的失败都淹没在几百条输出里，定位成本远高于多维护一个 csproj。
    ///
    /// 运行：dotnet run --project ScadaChecks            （常规断言）
    ///       dotnet run --project ScadaChecks -- --stress（叠加万级压力测试）
    ///
    /// 前提与 FlowCanvasChecks 相同：被测代码不得依赖 Application.Current / Dispatcher。
    /// VariablePersistenceService 只做内存集合搬运，控制台进程里可直接调用。
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            bool stress = args.Any(a => a.Equals("--stress", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("========== SCADA 断言（S0 变量身份/改名级联 + S1 画面领域层） ==========");
            if (stress) Console.WriteLine("（已启用 --stress：叠加万级压力测试）");

            VariableIdentity();
            IdentitySurvivesRename();
            PersistenceRoundTrip();
            LegacyMigration();
            RegistryLookup();
            GlobalLinkResolution();
            RegistryRenameContract();
            RenameEntryContract();
            RenameCascade();
            NetworkVariableReregister();
            ScadaElementModel();
            ScadaPageVersion();
            ScadaPersistenceRoundTrip();
            ScadaRenameCascade();
            ElementRegistryChecks();
            ElementControlChecks();
            DragDropChecks();
            PropertyPanelChecks();
            LayerAndPageChecks();
            RuntimeSessionChecks();
            ViewportRenderChecks();
            EventHookDispatchChecks();
            RuntimeBinderChecks();
            VariablePickerDialogChecks();
            RunWindowModeChecks();
            if (stress) StressRoundTrip();
            if (stress) StressRegistryLookup();
            if (stress) StressScadaDocument();

            Finish();
            return Environment.ExitCode;
        }

        // ==================================================================
        //  [A] 变量稳定身份：新建即非空、彼此唯一、显式传入被保留
        // ==================================================================
        private static void VariableIdentity()
        {
            Section("[A] 变量稳定身份的生成");

            var a = VariableFactory.CreateLocal("A", typeof(int), "描述A", 1);
            var b = VariableFactory.CreateLocal("B", typeof(int), "描述B", 2);

            Check("工厂新建变量身份非空", a.VariableId != Guid.Empty, $"Id={a.VariableId}");
            Check("不同变量身份彼此不同", a.VariableId != b.VariableId,
                $"{a.VariableId} vs {b.VariableId}");

            var fixedId = Guid.NewGuid();
            var c = VariableFactory.CreateLocal("C", typeof(int), "描述C", 3, fixedId);
            Check("工厂显式传入的身份被原样保留", c.VariableId == fixedId, $"期望 {fixedId}，实得 {c.VariableId}");

            // 默认构造（变量管理弹窗"新增变量"走的路径）也必须自带身份
            var direct = new LocalVariableModel { Name = "D", DataType = typeof(int) };
            Check("直接 new 的变量自带身份（非 Guid.Empty）", direct.VariableId != Guid.Empty, $"Id={direct.VariableId}");

            var net = new NetworkVariableModel { Name = "N", DataType = typeof(int) };
            Check("网络变量自带身份（非 Guid.Empty）", net.VariableId != Guid.Empty, $"Id={net.VariableId}");
        }

        // ==================================================================
        //  [B] 身份与名字解耦：改名 / 换连接都不动身份
        // ==================================================================
        private static void IdentitySurvivesRename()
        {
            Section("[B] 改名与换连接不改变身份");

            var local = (LocalVariableModel)VariableFactory.CreateLocal("原名", typeof(int), "d", 1);
            var localId = local.VariableId;
            local.Name = "新名字";
            Check("本地变量改名后身份不变", local.VariableId == localId, $"Id={local.VariableId}");

            var net = new NetworkVariableModel { Name = "原名", DataType = typeof(int), ConnectionName = "PLC1" };
            var netId = net.VariableId;
            net.Name = "新名字";
            net.ConnectionName = "PLC2";
            Check("网络变量改名 + 换连接后身份不变", net.VariableId == netId, $"Id={net.VariableId}");

            // 数据类型变更同样不得重建身份（否则已存的连线/绑定会指向"不存在的变量"）
            var typed = (LocalVariableModel)VariableFactory.CreateLocal("Typed", typeof(int), "d", 1);
            var typedId = typed.VariableId;
            typed.DataType = typeof(double);
            Check("变量改类型后身份不变", typed.VariableId == typedId, $"Id={typed.VariableId}");
        }

        // ==================================================================
        //  [C] 方案往返：Capture → Restore 后身份逐一对上
        // ==================================================================
        private static void PersistenceRoundTrip()
        {
            Section("[C] 方案存取往返（Capture → Restore）");

            var w1 = new WorkspaceContext();
            w1.GlobalVariables.Clear();

            var local = VariableFactory.CreateLocal("LocalA", typeof(int), "本地变量", 7);
            var net = new NetworkVariableModel
            {
                Name = "NetA",
                DataType = typeof(int),
                ConnectionName = "PLC1",
                DefaultValue = 3,
                Value = 3
            };
            w1.GlobalVariables.Add(local);
            w1.GlobalVariables.Add(net);

            var solution = new SolutionModel();
            VariablePersistenceService.Capture(solution, w1);

            Check("Capture 快照数量与变量数一致", solution.VariableSnapshots.Count == 2,
                $"snapshots={solution.VariableSnapshots.Count}");
            Check("Capture 快照里的身份全部非空",
                solution.VariableSnapshots.All(d => d.VariableId != Guid.Empty),
                string.Join(",", solution.VariableSnapshots.Select(d => $"{d.Name}={d.VariableId}")));

            // 模拟"重启软件后打开方案"：全新的工作区，变量集合已被清空
            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();
            VariablePersistenceService.Restore(solution, w2);

            Check("Restore 后变量数量一致", w2.GlobalVariables.Count == 2, $"count={w2.GlobalVariables.Count}");

            var restoredLocal = w2.GlobalVariables.FirstOrDefault(v => v.Name == "LocalA");
            var restoredNet = w2.GlobalVariables.FirstOrDefault(v => v.Name == "NetA");
            Check("本地变量身份往返一致", restoredLocal != null && restoredLocal.VariableId == local.VariableId,
                $"期望 {local.VariableId}，实得 {restoredLocal?.VariableId}");
            Check("网络变量身份往返一致", restoredNet != null && restoredNet.VariableId == net.VariableId,
                $"期望 {net.VariableId}，实得 {restoredNet?.VariableId}");

            // 再存一次：第二次 Capture 的身份必须与第一次完全相同（不能"每次保存换一次身份"）
            var solution2 = new SolutionModel();
            VariablePersistenceService.Capture(solution2, w2);
            var idsRound1 = solution.VariableSnapshots.OrderBy(d => d.Name).Select(d => d.VariableId).ToArray();
            var idsRound2 = solution2.VariableSnapshots.OrderBy(d => d.Name).Select(d => d.VariableId).ToArray();
            Check("二次保存身份不漂移", idsRound1.SequenceEqual(idsRound2),
                $"{string.Join(",", idsRound1)} vs {string.Join(",", idsRound2)}");
        }

        // ==================================================================
        //  [D] 旧方案迁移：快照无 Id → 补发新 Id（不能让老方案加载即失败）
        // ==================================================================
        private static void LegacyMigration()
        {
            Section("[D] 旧方案（无 Id 字段）迁移");

            // 模拟旧 .vms：VariableDto 里没有 VariableId（反序列化后为 Guid.Empty）
            var legacy = new SolutionModel
            {
                VariableSnapshots = new List<VariableDto>
                {
                    new VariableDto
                    {
                        VarType = "Local",
                        Name = "LegacyLocal",
                        DataTypeString = "Int32",
                        DefaultValue = 11,
                        Value = 11,
                        VariableId = Guid.Empty
                    },
                    new VariableDto
                    {
                        VarType = "Network",
                        Name = "LegacyNet",
                        DataTypeString = "Int32",
                        ConnectionName = "PLC1",
                        DefaultValue = 22,
                        Value = 22,
                        VariableId = Guid.Empty
                    }
                }
            };

            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();
            VariablePersistenceService.Restore(legacy, w);

            Check("旧方案变量全部加载成功（不丢变量）", w.GlobalVariables.Count == 2, $"count={w.GlobalVariables.Count}");
            Check("旧方案本地变量补发身份", w.GlobalVariables.FirstOrDefault(v => v.Name == "LegacyLocal")?.VariableId != Guid.Empty,
                $"Id={w.GlobalVariables.FirstOrDefault(v => v.Name == "LegacyLocal")?.VariableId}");
            Check("旧方案网络变量补发身份", w.GlobalVariables.FirstOrDefault(v => v.Name == "LegacyNet")?.VariableId != Guid.Empty,
                $"Id={w.GlobalVariables.FirstOrDefault(v => v.Name == "LegacyNet")?.VariableId}");
            Check("两个补发的身份彼此不同",
                w.GlobalVariables[0].VariableId != w.GlobalVariables[1].VariableId, "");

            // 迁移后再保存：Id 必须固化进方案，否则每次打开都换一次身份，绑定永远稳不住
            var migrated = new SolutionModel();
            VariablePersistenceService.Capture(migrated, w);
            Check("迁移后的身份被回写进方案快照",
                migrated.VariableSnapshots.All(d => d.VariableId != Guid.Empty), "");

            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();
            VariablePersistenceService.Restore(migrated, w2);
            Check("二次打开身份保持（迁移只发生一次）",
                w2.GlobalVariables.Select(v => v.VariableId)
                    .OrderBy(g => g).SequenceEqual(w.GlobalVariables.Select(v => v.VariableId).OrderBy(g => g)), "");
        }

        // ==================================================================
        //  [F] 变量解析索引：Id/Name 双路查找 + 集合跟随（增量 / 替换 / 清空）
        // ==================================================================
        private static void RegistryLookup()
        {
            Section("[F] 变量解析索引（IVariableRegistry）的双路查找");

            var w = new WorkspaceContext();
            var registry = w.VariableRegistry;

            // 这一条同时验证"索引建立在 InitializeCommonVariables 之后"这个构造顺序约定：
            // 若顺序写反，索引会挂在随即被丢弃的旧集合上，数量对不上
            Check("构造后索引数量与变量集合一致（索引建在 InitializeCommonVariables 之后）",
                registry.Count == w.GlobalVariables.Count && registry.Count > 0,
                $"index={registry.Count} / collection={w.GlobalVariables.Count}");

            var total = w.GlobalVariables.First(v => v.Name == "TotalCount");
            Check("按 Id 命中同一实例", ReferenceEquals(registry.FindById(total.VariableId), total),
                $"Id={total.VariableId}");
            Check("按名命中同一实例", ReferenceEquals(registry.FindByName("TotalCount"), total), "");
            Check("按名查找大小写不敏感", ReferenceEquals(registry.FindByName("totalcount"), total), "");
            Check("Guid.Empty 寻址直接落空（旧工程哨兵值不做无意义遍历）",
                registry.FindById(Guid.Empty) == null, "");
            Check("不存在的 Id 落空", registry.FindById(Guid.NewGuid()) == null, "");
            Check("空名字落空", registry.FindByName(null) == null && registry.FindByName("") == null, "");
            Check("双路查找：Id 优先于 Name",
                ReferenceEquals(registry.Resolve(total.VariableId, "OKCount"), total),
                "Id 指向 TotalCount、Name 指向 OKCount 时应以 Id 为准");
            Check("双路查找：Id 落空时按 Name 兜底",
                ReferenceEquals(registry.Resolve(Guid.Empty, "OKCount"), w.GlobalVariables.First(v => v.Name == "OKCount")), "");

            // --- 集合变化必须被索引感知 ---
            int before = registry.Count;
            var added = VariableFactory.CreateLocal("AddedByCheck", typeof(int), "断言新增", 1);
            w.GlobalVariables.Add(added);
            Check("新增变量后索引增量跟随",
                registry.Count == before + 1 && ReferenceEquals(registry.FindById(added.VariableId), added),
                $"count={before} → {registry.Count}");

            w.GlobalVariables.Remove(added);
            Check("移除变量后索引摘除该键",
                registry.Count == before && registry.FindById(added.VariableId) == null
                && registry.FindByName("AddedByCheck") == null,
                $"count={registry.Count}");

            // --- 集合实例被整体替换：索引必须重挂（最容易被漏掉的一条路径）---
            var w2 = new WorkspaceContext();
            var replaced = w2.GlobalVariables.First();
            var newCollection = new ObservableCollection<IVariable>
            {
                VariableFactory.CreateLocal("OnlyInNewCollection", typeof(int), "替换后的集合", 5)
            };
            w2.GlobalVariables = newCollection;
            Check("集合实例整体替换后索引跟随新集合",
                w2.VariableRegistry.Count == 1
                && w2.VariableRegistry.FindByName("OnlyInNewCollection") != null,
                $"count={w2.VariableRegistry.Count}");
            Check("集合实例整体替换后旧集合变量不再可见（不留幽灵变量）",
                w2.VariableRegistry.FindById(replaced.VariableId) == null, "");
            // Attach 是宿主（WorkspaceContext 的 setter）用的实现细节，不暴露在 IVariableRegistry 上，
            // 这里显式转型以体现"只有换集合的宿主才需要调用它"
            ((VariableRegistry)w2.VariableRegistry).Attach(newCollection);
            Check("重复 Attach 同一集合是空操作（幂等）", w2.VariableRegistry.Count == 1, "");

            // --- Clear 走 Reset 分支 ---
            var w3 = new WorkspaceContext();
            w3.GlobalVariables.Clear();
            Check("Clear 后索引清空且不再命中演示变量",
                w3.VariableRegistry.Count == 0 && w3.VariableRegistry.FindByName("TotalCount") == null, "");

            // --- 脏数据：两个变量带同一身份（只可能来自手工改过的 .vms）---
            var w4 = new WorkspaceContext();
            w4.GlobalVariables.Clear();
            var sharedId = Guid.NewGuid();
            var keeper = VariableFactory.CreateLocal("Keeper", typeof(int), "先出现者", 1, sharedId);
            var intruder = VariableFactory.CreateLocal("Intruder", typeof(int), "后出现者", 2, sharedId);
            w4.GlobalVariables.Add(keeper);
            w4.GlobalVariables.Add(intruder);
            Check("重复身份只索引一份（不抛异常——脏数据不该让方案打不开）",
                w4.VariableRegistry.Count == 1, $"count={w4.VariableRegistry.Count}");
            Check("重复身份保留集合中靠前者（解析结果确定，不随插入顺序漂移）",
                ReferenceEquals(w4.VariableRegistry.FindById(sharedId), keeper), "");
            Check("重复身份的另一方仍可按名寻址（名字索引与 Id 索引互不牵连）",
                ReferenceEquals(w4.VariableRegistry.FindByName("Intruder"), intruder), "");
            w4.VariableRegistry.Rebuild();
            Check("重建索引后重复身份的取舍与增量路径一致",
                w4.VariableRegistry.Count == 1
                && ReferenceEquals(w4.VariableRegistry.FindById(sharedId), keeper), "");
        }

        // ==================================================================
        //  [G] ResolveGlobalLink：Id 命中 / Name 兜底 / 旧工程自愈回填
        // ==================================================================
        private static void GlobalLinkResolution()
        {
            Section("[G] 连线解析 ResolveGlobalLink（Id 优先 / Name 兜底 / 自愈回填）");

            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();
            var flag = VariableFactory.CreateLocal("PLC_Ready", typeof(bool), "握手信号", true);
            w.GlobalVariables.Add(flag);
            var registry = w.VariableRegistry;

            Check("link 为 null 时安全返回 null", registry.ResolveGlobalLink(null) == null, "");

            // ① Id 命中：名字故意写成已失效的旧名字，证明"Id 才是权威键"
            var byId = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "早已改掉的旧名字", "Global.早已改掉的旧名字")
            {
                TargetVariableId = flag.VariableId
            };
            Check("Id 命中：名字字段已失效仍能解析（Id 是权威键）",
                ReferenceEquals(registry.ResolveGlobalLink(byId), flag), $"Id={flag.VariableId}");

            // ② 只有名字：旧工程数据，按名命中并自愈回填 Id
            var byName = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "Global.PLC_Ready");
            Check("旧工程连线初始无 Id", byName.TargetVariableId == Guid.Empty, "");
            Check("Id 为空时按名兜底命中",
                ReferenceEquals(registry.ResolveGlobalLink(byName), flag), "");
            Check("按名命中后自愈回填 TargetVariableId（下次保存即落盘）",
                byName.TargetVariableId == flag.VariableId,
                $"期望 {flag.VariableId}，实得 {byName.TargetVariableId}");
            Check("自愈幂等：二次解析结果与 Id 均不再变化",
                ReferenceEquals(registry.ResolveGlobalLink(byName), flag)
                && byName.TargetVariableId == flag.VariableId, "");

            // 自愈后的连线在变量改名后依然命中——这正是 Id 化的收益
            Rename(registry, flag, "PLC_Ready_Renamed");
            Check("自愈后的连线在变量改名后仍不断链",
                ReferenceEquals(registry.ResolveGlobalLink(byName), flag),
                $"变量已改名为 {flag.Name}，连线仍按 Id 命中");

            // 孤立连线（没挂进任何流程，级联遍历不到）改名后按旧名落空。
            // 这是"级联只对方案树负责"的边界交代：真正在用的连线都挂在流程里，见 [K]
            var neverResolved = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "Global.PLC_Ready");
            Check("孤立老连线（未挂进方案树）改名后按旧名落空，级联不会触及",
                registry.ResolveGlobalLink(neverResolved) == null, "");

            // ③ 两者皆落空
            var ghost = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "NoSuchVariable", "Global.NoSuchVariable");
            Check("Id 与 Name 皆落空返回 null", registry.ResolveGlobalLink(ghost) == null, "");
            Check("解析落空时不写脏数据（Id 保持为空）", ghost.TargetVariableId == Guid.Empty, "");
        }

        // ==================================================================
        //  [H] NotifyRenamed 契约：索引修正、事件载荷、重名场景不误删
        // ==================================================================
        private static void RegistryRenameContract()
        {
            Section("[H] 改名契约 NotifyRenamed（索引修正 + 广播 + 重名保护）");

            var w = new WorkspaceContext();
            var registry = w.VariableRegistry;
            var target = w.GlobalVariables.First(v => v.Name == "OKCount");
            var id = target.VariableId;

            VariableRenamedEventArgs? captured = null;
            registry.VariableRenamed += (_, e) => captured = e;

            Rename(registry, target, "GoodCount");

            Check("改名后旧名不再命中", registry.FindByName("OKCount") == null, "");
            Check("改名后新名命中同一实例", ReferenceEquals(registry.FindByName("GoodCount"), target), "");
            Check("改名不影响 Id 寻址", ReferenceEquals(registry.FindById(id), target), "");
            Check("改名事件携带实例与旧名",
                captured != null && ReferenceEquals(captured.Variable, target) && captured.OldName == "OKCount",
                $"OldName={captured?.OldName}");

            // 反向断言：绕过注册表裸写模型名时索引不会"自动变聪明"——
            // 这是刻意的契约（Name 非 INPC，索引无法自行感知），也是 S0-b 把改名
            // 收敛到 TryRename 单一入口的原因：两步用法漏掉广播就留下死键
            RawRename(target, "SneakyRename");
            Check("裸写模型名（漏广播）时索引保留旧键（证明改名必须走统一入口）",
                ReferenceEquals(registry.FindByName("GoodCount"), target)
                && registry.FindByName("SneakyRename") == null, "");
            registry.NotifyRenamed(target, "GoodCount");

            // --- 重名保护：摘旧名键前必须验明该键确实指向被改名对象 ---
            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();
            var first = VariableFactory.CreateLocal("Dup", typeof(int), "先出现者", 1);
            var second = VariableFactory.CreateLocal("Dup", typeof(int), "后出现者", 2);
            w2.GlobalVariables.Add(first);
            w2.GlobalVariables.Add(second);
            var registry2 = w2.VariableRegistry;

            Check("重名以集合中靠前者为准（增量与重建两条路径一致）",
                ReferenceEquals(registry2.FindByName("Dup"), first), "");

            Rename(registry2, second, "Dup2");
            Check("重名场景下改名不误删他人的名字键",
                ReferenceEquals(registry2.FindByName("Dup"), first), "");
            Check("重名场景下改名者以新名入索引",
                ReferenceEquals(registry2.FindByName("Dup2"), second), "");
        }

        // ==================================================================
        //  [E] 压力测试：万级变量的 Capture/Restore 吞吐与身份守恒
        // ==================================================================
        private static void StressRoundTrip()
        {
            Section("[E] 万级变量往返压力（--stress）");

            const int N = 10000;
            var w1 = new WorkspaceContext();
            w1.GlobalVariables.Clear();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
                w1.GlobalVariables.Add(VariableFactory.CreateLocal($"V{i}", typeof(int), $"变量{i}", i));
            var buildMs = sw.ElapsedMilliseconds;

            var ids = w1.GlobalVariables.Select(v => v.VariableId).ToArray();
            Check($"{N} 个变量构造唯一性", ids.Distinct().Count() == N, $"distinct={ids.Distinct().Count()}");

            var solution = new SolutionModel();
            sw.Restart();
            VariablePersistenceService.Capture(solution, w1);
            var captureMs = sw.ElapsedMilliseconds;

            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();
            sw.Restart();
            VariablePersistenceService.Restore(solution, w2);
            var restoreMs = sw.ElapsedMilliseconds;

            Check($"{N} 变量 Capture 耗时 < 1000ms", captureMs < 1000, $"{captureMs}ms");
            Check($"{N} 变量 Restore 耗时 < 3000ms", restoreMs < 3000, $"{restoreMs}ms");
            Check("万级往返身份序列完全一致",
                w2.GlobalVariables.Select(v => v.VariableId).SequenceEqual(ids),
                $"构建 {buildMs}ms / 捕获 {captureMs}ms / 还原 {restoreMs}ms");
        }

        // ==================================================================
        //  [I] 压力测试：万级变量的索引构建与解析吞吐（对照旧的线性扫描）
        // ==================================================================
        private static void StressRegistryLookup()
        {
            Section("[I] 万级变量索引吞吐（--stress）");

            const int N = 10000;
            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
                w.GlobalVariables.Add(VariableFactory.CreateLocal($"S{i}", typeof(int), $"压力变量{i}", i));
            var buildMs = sw.ElapsedMilliseconds;

            var variables = w.GlobalVariables.ToArray();
            var ids = variables.Select(v => v.VariableId).ToArray();
            var registry = w.VariableRegistry;

            Check($"{N} 个变量逐条 Add 后索引数量一致（增量维护不丢键）",
                registry.Count == N, $"index={registry.Count} / 构建 {buildMs}ms");

            const int probes = 200000;
            sw.Restart();
            int hits = 0;
            for (int i = 0; i < probes; i++)
            {
                if (registry.FindById(ids[i % N]) != null)
                    hits++;
            }
            var indexMs = sw.ElapsedMilliseconds;
            Check($"{probes} 次按 Id 解析全部命中且耗时 < 2000ms",
                hits == probes && indexMs < 2000, $"hits={hits} / {indexMs}ms");

            // 对照组：Id 化之前编译器用的线性扫描（每次 O(n) 名字比较）。
            // 这里只取少量探针，避免对照组自己把测试跑成分钟级；两者都要求结果正确，
            // 耗时差仅作为"索引换掉了 O(n) 查找"的佐证写进明细
            const int linearProbes = 2000;
            var swLinear = Stopwatch.StartNew();
            int linearHits = 0;
            for (int i = 0; i < linearProbes; i++)
            {
                var name = variables[i % N].Name;
                if (w.GlobalVariables.FirstOrDefault(v => v.Name == name) != null)
                    linearHits++;
            }
            var linearMs = swLinear.ElapsedMilliseconds;
            Check($"对照组线性扫描 {linearProbes} 次结果同样正确（索引 {indexMs}ms/{probes} 次 vs 线性 {linearMs}ms/{linearProbes} 次）",
                linearHits == linearProbes, $"index={indexMs}ms / linear={linearMs}ms");

            // 改名吞吐：TryRename 走的是 O(1) 键改写，不应随变量规模退化
            sw.Restart();
            for (int i = 0; i < N; i++)
            {
                var variable = variables[i];
                Rename(registry, variable, variable.Name + "_R");
            }
            var renameMs = sw.ElapsedMilliseconds;
            Check($"{N} 次改名后新名全部可命中、旧名全部落空",
                variables.All(v => registry.FindByName(v.Name) != null)
                && registry.FindByName("S0") == null,
                $"耗时 {renameMs}ms");
        }

        // ==================================================================
        //  [J] 改名统一入口 TryRename 的判定规则（幂等 / 重名 / 空名 / 大小写 / 广播）
        // ==================================================================
        private static void RenameEntryContract()
        {
            Section("[J] 改名统一入口 TryRename 的判定规则");

            var w = new WorkspaceContext();
            var registry = w.VariableRegistry;
            var okCount = w.GlobalVariables.First(v => v.Name == "OKCount");
            var totalCount = w.GlobalVariables.First(v => v.Name == "TotalCount");

            VariableRenamedEventArgs? captured = null;
            registry.VariableRenamed += (_, e) => captured = e;

            // --- 拒绝：空名 ---
            Check("空名被拒绝", !registry.TryRename(okCount, "   ", out var emptyError), emptyError);
            Check("空名被拒后名字与索引都未改动",
                okCount.Name == "OKCount" && ReferenceEquals(registry.FindByName("OKCount"), okCount), "");

            // --- 拒绝：与别的变量重名（含仅大小写不同）---
            Check("与别的变量重名被拒绝",
                !registry.TryRename(okCount, "TotalCount", out var dupError), dupError);
            Check("重名判定大小写不敏感（'totalcount' 同样被拒）",
                !registry.TryRename(okCount, "totalcount", out _), "");
            Check("重名被拒后双方索引键都没被改写",
                ReferenceEquals(registry.FindByName("OKCount"), okCount)
                && ReferenceEquals(registry.FindByName("TotalCount"), totalCount), "");

            // --- 幂等：用户在弹窗里"编辑完原样确认" ---
            captured = null;
            Check("与旧名完全相同算成功（幂等，不报错）", registry.TryRename(okCount, "OKCount", out _), "");
            Check("幂等改名不广播事件（订阅方不做无谓级联）", captured == null, "");

            // --- 仅大小写不同：允许（索引是 OrdinalIgnoreCase，摘旧键后不会自撞）---
            Check("仅大小写不同允许通过（'OKCount' → 'okcount'）",
                registry.TryRename(okCount, "okcount", out var caseError), caseError);
            Check("大小写改名后新旧写法都能命中（查名大小写不敏感）",
                ReferenceEquals(registry.FindByName("okcount"), okCount)
                && ReferenceEquals(registry.FindByName("OKCount"), okCount), "");
            Check("大小写改名广播了事件（真改动，不是幂等）",
                captured != null && captured.OldName == "OKCount", $"OldName={captured?.OldName}");

            // --- 正常改名：新名命中、旧名立即落空（不留死键，对照 [H] 的裸写场景）---
            captured = null;
            Check("经统一入口改名：新名可命中、旧名立即落空（不留死键）",
                registry.TryRename(okCount, "GoodCount", out _)
                && ReferenceEquals(registry.FindByName("GoodCount"), okCount)
                && registry.FindByName("okcount") == null, "");
            Check("改名广播事件并携带改名前的旧名",
                captured != null && captured.OldName == "okcount", $"OldName={captured?.OldName}");
            Check("改名不改身份（Id 是寻址锚点，与名字解耦）",
                ReferenceEquals(registry.FindById(okCount.VariableId), okCount), $"Id={okCount.VariableId}");
        }

        // ==================================================================
        //  [K] 改名级联：连线自愈 / 监视项自愈 / 嵌套容器递归 / 无关引用不波及
        // ==================================================================
        private static void RenameCascade()
        {
            Section("[K] 变量改名的级联（WorkspaceContext 订阅注册表事件）");

            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();
            var flag = VariableFactory.CreateLocal("PLC_Ready", typeof(bool), "握手信号", true);
            w.GlobalVariables.Add(flag);
            var registry = w.VariableRegistry;

            // 老连线：S0-b 之前所有连线的样子——只有名字，没有 Id
            var legacyLink = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "Global.PLC_Ready");

            // 新连线：已按 Id 寻址。名字字段刻意写成早已失效的旧名，
            // 用来分辨"刷新的是显示串"和"寻址靠的是 Id"
            var idLink = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "早已改掉的旧名", "Global.早已改掉的旧名")
            {
                TargetVariableId = flag.VariableId
            };

            // 数组元素绑定：显示串带 [i] 下标后缀，只有"变量名那一段"该被替换
            var arrayLink = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "全局变量 (Global).PLC_Ready[2]")
            {
                TargetVariableId = flag.VariableId
            };

            // 显示串里也含 "PLC_Ready"、但 Kind 是常量的连线：改名不得波及它
            // （历史上区分类别靠显示串前缀，改一句 UI 文案就误伤——Kind 显式化后不再可能）
            var constantLink = new LinkReference(LinkKind.Constant, Guid.Empty, "PLC_Ready", "常量值: PLC_Ready");

            var outerStep = new ActionStep("icon", "等待信号", "WaitSignal", "等待");
            outerStep.LinkedSources["Enable"] = legacyLink;
            outerStep.LinkedSources["Reset"] = idLink;
            outerStep.LinkedSources["Item"] = arrayLink;
            outerStep.LinkedSources["Threshold"] = constantLink;

            // 嵌套容器：级联必须递归进分支，否则"分支里的连线"会静默失联
            var nestedLink = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "Global.PLC_Ready");
            var branchStep = new ActionStep("icon", "分支内", "BranchWait", "分支步骤");
            branchStep.LinkedSources["Enable"] = nestedLink;

            var container = new ConditionStep("icon", "条件", "IfPlugin", "条件一");
            container.Children[0].Steps.Add(branchStep);

            var flow = new FlowModel { FlowName = "MainTask" };
            flow.Steps.Add(outerStep);
            flow.Steps.Add(container);

            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);

            var legacyWatch = new WatchItemModel { ItemType = WatchItemType.GlobalVariable, GlobalVariableName = "PLC_Ready" };
            var idWatch = new WatchItemModel
            {
                ItemType = WatchItemType.GlobalVariable,
                GlobalVariableName = "PLC_Ready",
                VariableId = flag.VariableId
            };
            // 算子端口监视项：名字字段碰巧也等于变量名，同样不得被改动
            var portWatch = new WatchItemModel
            {
                ItemType = WatchItemType.PluginPort,
                StepName = "等待",
                PortName = "Done",
                GlobalVariableName = "PLC_Ready"
            };
            solution.WatchItems.Add(legacyWatch);
            solution.WatchItems.Add(idWatch);
            solution.WatchItems.Add(portWatch);

            w.SwitchSolution(solution);

            Check("改名成功", registry.TryRename(flag, "PLC_Ready_2", out var error), error);

            // --- ① 老连线：一次性自愈（换寻址键 + 补 Id），此后按 Id 寻址 ---
            Check("老连线的名字键被改写为新名", legacyLink.TargetPortName == "PLC_Ready_2",
                $"TargetPortName={legacyLink.TargetPortName}");
            Check("老连线被补上稳定身份（下次保存即完成迁移）",
                legacyLink.TargetVariableId == flag.VariableId, $"Id={legacyLink.TargetVariableId}");
            Check("老连线显示串刷新", legacyLink.DisplayAddress == "Global.PLC_Ready_2", legacyLink.DisplayAddress);
            Check("老连线改名后仍能解析（自愈后不再依赖名字）",
                ReferenceEquals(registry.ResolveGlobalLink(legacyLink), flag), "");

            // --- ② 新连线：只刷显示串，Id 寻址不受影响 ---
            Check("新连线显示串刷新", idLink.DisplayAddress == "Global.PLC_Ready_2", idLink.DisplayAddress);
            Check("新连线仍按 Id 命中", ReferenceEquals(registry.ResolveGlobalLink(idLink), flag), "");

            // --- ③ 数组元素绑定：前缀与下标后缀原样保留 ---
            Check("数组元素绑定只换变量名那一段（前缀 '全局变量 (Global).' 与后缀 '[2]' 保留）",
                arrayLink.DisplayAddress == "全局变量 (Global).PLC_Ready_2[2]", arrayLink.DisplayAddress);

            // --- ④ 同名但不同 Kind 的引用不被波及 ---
            Check("同名的常量连线不受变量改名影响（判据是 Kind，不是显示串）",
                constantLink.TargetPortName == "PLC_Ready" && constantLink.DisplayAddress == "常量值: PLC_Ready",
                constantLink.DisplayAddress);

            // --- ⑤ 嵌套容器：递归覆盖分支内的连线 ---
            Check("嵌套分支里的连线同样被级联（递归不漏层）",
                nestedLink.TargetPortName == "PLC_Ready_2"
                && nestedLink.TargetVariableId == flag.VariableId
                && nestedLink.DisplayAddress == "Global.PLC_Ready_2", nestedLink.DisplayAddress);

            // --- ⑥ 监视项 ---
            Check("老监视项改名为新名并补上稳定身份",
                legacyWatch.GlobalVariableName == "PLC_Ready_2" && legacyWatch.VariableId == flag.VariableId,
                $"Name={legacyWatch.GlobalVariableName} / Id={legacyWatch.VariableId}");
            Check("新监视项只更新展示名",
                idWatch.GlobalVariableName == "PLC_Ready_2", idWatch.GlobalVariableName);
            Check("非全局变量监视项不受影响（只处理 GlobalVariable 类型）",
                portWatch.GlobalVariableName == "PLC_Ready" && portWatch.PortName == "Done",
                portWatch.GlobalVariableName);

            // --- ⑦ 边界：没挂进方案树的孤立连线级联遍历不到（见 [G] 同名断言）---
            var orphan = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "PLC_Ready", "Global.PLC_Ready");
            Check("未挂进任何流程的孤立连线不被级联触及",
                orphan.TargetPortName == "PLC_Ready" && orphan.TargetVariableId == Guid.Empty, "");

            // --- ⑧ 连续改名：级联可重复触发，不依赖"只触发一次" ---
            Check("第二次改名同样完成级联",
                registry.TryRename(flag, "PLC_Ready_3", out _)
                && legacyLink.DisplayAddress == "Global.PLC_Ready_3"
                && arrayLink.DisplayAddress == "全局变量 (Global).PLC_Ready_3[2]"
                && legacyWatch.GlobalVariableName == "PLC_Ready_3",
                $"{legacyLink.DisplayAddress} / {arrayLink.DisplayAddress}");
        }

        // ==================================================================
        //  [L] 网络变量改名：轮询注册以 (连接名, 变量名) 为键 → 按旧键注销、按新名重注册
        // ==================================================================
        private static void NetworkVariableReregister()
        {
            Section("[L] 网络变量改名的轮询重注册（NetworkVariableBridge）");

            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();
            var registry = w.VariableRegistry;
            var logger = new LoggerStub();
            var manager = new AdvancedCommunicationManager();

            // 先放变量、后建桥接器：走的是构造时的 RebindAll（"已存在的网络变量"这条路径）
            // 地址显式指定保持寄存器区：Int 落在 Coils 区会被 IsAreaCompatibleWithDataType 判非法，
            // 而 Area 的枚举默认值是 Coils——不写就等于在断言里埋了一个"默认即正确"的假设
            var net = (NetworkVariableModel)VariableFactory.CreateNetwork(
                "NV_Old", typeof(int), "PLC1",
                new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "100" }, "网络变量");
            w.GlobalVariables.Add(net);

            NetworkVariableBridge? bridge = null;
            Quiet(() => bridge = new NetworkVariableBridge(w, manager, logger));

            var oldCommVar = RegisteredVariable(manager, "PLC1", "NV_Old");
            Check("桥接器构造时把已存在的网络变量接入轮询", oldCommVar != null, "");
            Check("注册进轮询的地址/类型来自变量配置",
                oldCommVar?.Address == "x=3;100" && !string.IsNullOrEmpty(oldCommVar.ValueType),
                $"Address={oldCommVar?.Address} / ValueType={oldCommVar?.ValueType}");

            bool renamed = false;
            string renameError = string.Empty;
            Quiet(() => renamed = registry.TryRename(net, "NV_New", out renameError));

            Check("网络变量改名成功", renamed, renameError);
            Check("旧键已注销（否则旧名继续幽灵轮询、旧实例被闭包钉住不回收）",
                !IsRegistered(manager, "PLC1", "NV_Old"), "");
            Check("新键已注册", IsRegistered(manager, "PLC1", "NV_New"), "");

            var newCommVar = RegisteredVariable(manager, "PLC1", "NV_New");
            Check("重注册登记的是新实例（不是把旧实例原地改键）",
                newCommVar != null && !ReferenceEquals(newCommVar, oldCommVar), "");
            Check("新实例带着新名与同一地址",
                newCommVar?.VariableName == "NV_New" && newCommVar?.Address == "x=3;100",
                $"Name={newCommVar?.VariableName} / Address={newCommVar?.Address}");

            // 桥接器构造之后才进集合的变量走集合事件（另一条路径，同样要接线）
            var late = (NetworkVariableModel)VariableFactory.CreateNetwork(
                "NV_Late", typeof(int), "PLC1",
                new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "200" }, "后加入");
            Quiet(() => w.GlobalVariables.Add(late));
            Check("桥接器构造后新加入集合的网络变量同样被接线",
                IsRegistered(manager, "PLC1", "NV_Late"), "");

            // 缺连接名：不该静默——"变量在列表里但当前值永远空"是头号暗坑，必须留痕
            // 地址保持合法（保持寄存器区），确保这条断言失败的唯一原因是"没连接名"，不是地址不合法
            var orphan = (NetworkVariableModel)VariableFactory.CreateNetwork(
                "NV_NoConn", typeof(int), "",
                new ModbusAddress { Area = ModbusArea.HoldingRegisters, Offset = "7" }, "无连接名");
            Quiet(() => w.GlobalVariables.Add(orphan));
            Check("缺连接名的网络变量不注册，但留下可见警告（不静默）",
                !IsRegistered(manager, "", "NV_NoConn") && logger.Warnings.Any(m => m.Contains("NV_NoConn")),
                $"warnings={logger.Warnings.Count}");

            bool orphanRenamed = false;
            Quiet(() => orphanRenamed = registry.TryRename(orphan, "NV_NoConn_2", out _));
            Check("未注册的网络变量改名不抛异常、也不凭空产生注册键",
                orphanRenamed && !IsRegistered(manager, "", "NV_NoConn_2"), "");

            Quiet(() => bridge?.Dispose());
            Check("桥接器释放后全部注销（不留幽灵轮询）",
                !IsRegistered(manager, "PLC1", "NV_New") && !IsRegistered(manager, "PLC1", "NV_Late"), "");
        }

        // ==================================================================
        //  [M] 画面图元与绑定：命中判定（Id 优先 / 名字兜底）与属性袋
        // ==================================================================
        private static void ScadaElementModel()
        {
            Section("[M] 画面图元的绑定命中判定与属性袋");

            var variableId = Guid.NewGuid();

            // --- 有 Id：Id 是权威键，名字字段过期也照样命中 ---
            var byId = new ScadaBinding
            {
                TargetProperty = "Value",
                VariableId = variableId,
                VariableName = "早已改掉的旧名"
            };
            Check("有 Id 的绑定：名字对不上也算命中（Id 是权威键）",
                byId.Matches(variableId, "早已改掉的旧名"), $"Id={byId.VariableId}");
            Check("有 Id 的绑定：别的变量不命中",
                !byId.Matches(Guid.NewGuid(), "早已改掉的旧名"), "");
            Check("有 Id 的绑定：Id 为空的一侧不命中（改名变量必有 Id，空 Id 不该认领任何人）",
                !byId.Matches(Guid.Empty, "早已改掉的旧名"), "");
            Check("IsLegacyByName 只对'无 Id 且有名字'成立",
                !byId.IsLegacyByName, $"IsLegacyByName={byId.IsLegacyByName}");

            // --- 无 Id（旧数据）：只能按名字找，大小写不敏感 ---
            var legacy = new ScadaBinding { TargetProperty = "Value", VariableName = "PLC_Ready" };
            Check("无 Id 的老绑定按名命中（大小写不敏感）",
                legacy.Matches(Guid.NewGuid(), "plc_ready"), "");
            Check("无 Id 的老绑定：名字不符不命中",
                !legacy.Matches(Guid.NewGuid(), "Other"), "");
            Check("无 Id 且旧名为空时不命中（否则一次空名改名会认领全部无 Id 绑定）",
                !legacy.Matches(Guid.NewGuid(), null) && !legacy.Matches(Guid.NewGuid(), ""), "");
            Check("IsLegacyByName 对'无 Id 且有名字'成立", legacy.IsLegacyByName, "");

            // --- 图元级刷新：自愈回填要动到、无关绑定不能碰 ---
            var element = new ScadaElement { TypeKey = "Hmi.Label", Name = "转速标签" };

            var legacyBind = new ScadaBinding { TargetProperty = "Value", VariableName = "PLC_Ready" };
            // 停用的绑定同样要跟着改名走：停用只是运行态跳过，配置仍是"引用着这个变量"
            var disabledBind = new ScadaBinding { TargetProperty = "Visible", VariableName = "PLC_Ready", IsEnabled = false };
            element.Bindings.Add(legacyBind);
            element.Bindings.Add(disabledBind);

            Check("老绑定（含停用的）按名命中后回填稳定身份与最新名字，返回命中条数",
                element.RefreshVariableReferences(variableId, "PLC_Ready", "PLC_Ready_2") == 2
                && legacyBind.VariableId == variableId && legacyBind.VariableName == "PLC_Ready_2"
                && disabledBind.VariableId == variableId && disabledBind.VariableName == "PLC_Ready_2",
                $"legacy={legacyBind.VariableName}/{legacyBind.VariableId} / disabled={disabledBind.VariableName}");
            Check("自愈后不再依赖名字（下次改名按 Id 照样命中）",
                legacyBind.Matches(variableId, "随便什么旧名"), "");
            Check("二次刷新幂等（内容不变，且不会把名字改回去）",
                element.RefreshVariableReferences(variableId, "PLC_Ready", "PLC_Ready_2") == 2
                && legacyBind.VariableName == "PLC_Ready_2", legacyBind.VariableName);
            Check("不存在的变量不改动任何绑定（返回 0，不静默误伤）",
                element.RefreshVariableReferences(Guid.NewGuid(), "PLC_Ready", "X") == 0, "");

            // --- 只按 Id 寻址的绑定：刷新展示名，但寻址键不受影响 ---
            var idOnly = new ScadaElement { TypeKey = "Hmi.Label" };
            var idBind = new ScadaBinding { TargetProperty = "Text", VariableId = variableId, VariableName = "旧展示名" };
            var unrelatedBind = new ScadaBinding { TargetProperty = "Visible", VariableName = "别的变量" };
            idOnly.Bindings.Add(idBind);
            idOnly.Bindings.Add(unrelatedBind);
            Check("按 Id 寻址的绑定只刷展示名",
                idOnly.RefreshVariableReferences(variableId, "PLC_Ready", "PLC_Ready_2") == 1
                && idBind.VariableName == "PLC_Ready_2" && idBind.VariableId == variableId, idBind.VariableName);
            Check("无关绑定不被波及（既不换名也不补 Id）",
                unrelatedBind.VariableName == "别的变量" && unrelatedBind.VariableId == Guid.Empty, "");

            // --- 属性袋：null 即删键、同值不通知 ---
            var bag = new ScadaElement { TypeKey = "Basic.Rectangle" };
            Check("属性袋读不存在的键返回默认值（消费方无需判空）",
                bag.GetProperty("Text", "默认文案") == "默认文案", "");
            bag.SetProperty("Text", "运行中");
            Check("属性袋写后读回一致", bag.GetProperty("Text") == "运行中", "");
            bag.SetProperty("Text", null);
            Check("写 null/空串即删键（不在文件里堆与描述符默认值重复的空条目）",
                bag.GetProperty("Text", "默认文案") == "默认文案" && bag.Properties.Count == 0,
                $"count={bag.Properties.Count}");

            int propertyNotifies = 0;
            bag.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScadaElement.Properties))
                    propertyNotifies++;
            };
            bag.SetProperty("Fill", "#FF0000");
            bag.SetProperty("Fill", "#FF0000");
            Check("同值重写不触发变更通知（避免把版本号刷高、把上层缓存白失效一次）",
                propertyNotifies == 1, $"notify={propertyNotifies}");

            var emptyKey = new ScadaElement();
            emptyKey.SetProperty("", "值");
            Check("空键写入是空操作（脏数据不该污染属性袋）", emptyKey.Properties.Count == 0, "");
        }

        // ==================================================================
        //  [N] 画面版本号与文档级 API（Version 递增 / 自动命名 / 补发身份）
        // ==================================================================
        private static void ScadaPageVersion()
        {
            Section("[N] 画面版本号与文档级 API");

            var page = new ScadaPage();
            int v0 = page.Version;
            Check("新建画面的版本号从 0 起", v0 == 0, $"Version={v0}");

            var element = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "矩形1" };
            page.Elements.Add(element);
            Check("新增图元使版本号 +1", page.Version == v0 + 1, $"{v0} → {page.Version}");

            int v1 = page.Version;
            element.X = 100;
            Check("改图元属性使版本号 +1", page.Version == v1 + 1, $"{v1} → {page.Version}");

            // 本次新修的订阅：绑定自身的属性变更必须往上冒到画面版本号
            var binding = new ScadaBinding { TargetProperty = "Value", VariableName = "V1" };
            element.Bindings.Add(binding);
            int v2 = page.Version;
            binding.VariableName = "V2";
            Check("改绑定属性使版本号 +1（S1-d 新补的订阅）", page.Version == v2 + 1, $"{v2} → {page.Version}");

            int v3 = page.Version;
            element.Bindings.Remove(binding);
            Check("移除绑定使版本号 +1", page.Version == v3 + 1, $"{v3} → {page.Version}");

            // 被摘掉订阅的绑定再变更，不该继续影响本画面
            int v4 = page.Version;
            binding.VariableName = "V3";
            Check("已移除的绑定属性变更不再计入本画面（订阅随移除一并摘掉）",
                page.Version == v4, $"{v4} → {page.Version}");

            int v5 = page.Version;
            page.Elements.Remove(element);
            Check("移除图元使版本号 +1", page.Version == v5 + 1, $"{v5} → {page.Version}");
            int v6 = page.Version;
            element.X = 999;
            Check("被移除的图元属性变更不再计入本画面（订阅已摘）",
                page.Version == v6, $"{v6} → {page.Version}");

            // 集合整体替换（反序列化走的路径）：订阅必须重新挂上
            var kept = new ScadaElement { TypeKey = "Basic.Text", Name = "文字1" };
            page.Elements = new ObservableCollection<ScadaElement> { kept };
            int v7 = page.Version;
            kept.Y = 5;
            Check("集合整体替换后图元的属性订阅仍保活（三件套的作用点）",
                page.Version == v7 + 1, $"{v7} → {page.Version}");

            // --- 身份寻址与绑定枚举 ---
            var lookup = new ScadaPage();
            var e1 = new ScadaElement { TypeKey = "Basic.Rectangle" };
            var e2 = new ScadaElement { TypeKey = "Hmi.Button" };
            lookup.Elements.Add(e1);
            lookup.Elements.Add(e2);
            e2.Bindings.Add(new ScadaBinding { TargetProperty = "Value", VariableName = "A" });
            e2.Bindings.Add(new ScadaBinding { TargetProperty = "Visible", VariableName = "B", IsEnabled = false });
            Check("按身份找图元命中同一实例", ReferenceEquals(lookup.FindElement(e2.ElementId), e2), "");
            Check("Guid.Empty 寻址直接落空（不做无意义遍历）", lookup.FindElement(Guid.Empty) == null, "");
            Check("不存在的身份落空", lookup.FindElement(Guid.NewGuid()) == null, "");
            Check("枚举绑定含停用的（加载期解析不能漏，运行态再按 IsEnabled 跳过）",
                lookup.EnumerateBindings().Count() == 2, $"count={lookup.EnumerateBindings().Count()}");

            // --- 身份索引：移除 / 身份改写 / 重复身份 / Clear 四条边都被索引如实跟上 ---
            lookup.Elements.Remove(e1);
            Check("移除图元后按身份立即落空（索引随移除同步）", lookup.FindElement(e1.ElementId) == null, "");

            var renamed = new ScadaElement { TypeKey = "Basic.Rectangle" };
            lookup.Elements.Add(renamed);
            var oldIdentity = renamed.ElementId;
            renamed.ElementId = Guid.NewGuid();
            Check("图元身份被改写后索引跟随（旧身份落空、新身份命中）",
                lookup.FindElement(oldIdentity) == null && ReferenceEquals(lookup.FindElement(renamed.ElementId), renamed), "");

            // 重复身份（脏数据/手工改文件都可能造出来）：口径与 VariableRegistry 一致，保留靠前者
            var dupPage = new ScadaPage();
            var first = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "靠前" };
            var second = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "靠后", ElementId = first.ElementId };
            dupPage.Elements.Add(first);
            dupPage.Elements.Add(second);
            Check("重复身份时按身份取到靠前者（口径与 VariableRegistry 一致）",
                ReferenceEquals(dupPage.FindElement(first.ElementId), first), "");
            dupPage.Elements.Remove(first);
            Check("移除重复身份的靠前者后由靠后者顶上（不返回已移除的图元）",
                ReferenceEquals(dupPage.FindElement(second.ElementId), second), "");

            // Clear() 走 Reset 分支：OldItems 为 null，只能靠登记表补摘订阅
            var cleared = new ScadaPage();
            var stale = new ScadaElement { TypeKey = "Basic.Rectangle" };
            cleared.Elements.Add(stale);
            cleared.Elements.Clear();
            int vClear = cleared.Version;
            stale.X = 1;
            Check("Clear 后索引清空（按身份落空）", cleared.FindElement(stale.ElementId) == null, "");
            Check("Clear 后旧图元的订阅已摘（属性变更不再刷本画面版本号）",
                cleared.Version == vClear, $"{vClear} → {cleared.Version}");

            // 绑定的 Clear() 同样走 Reset（OldItems 为 null）：清空后旧绑定不得再冒泡
            var bindingClear = new ScadaPage();
            var holder = new ScadaElement { TypeKey = "Hmi.Label" };
            var orphan = new ScadaBinding { TargetProperty = "Value", VariableName = "V" };
            holder.Bindings.Add(orphan);
            bindingClear.Elements.Add(holder);
            holder.Bindings.Clear();
            int vBinding = bindingClear.Version;
            orphan.VariableName = "V2";
            Check("Clear 绑定后旧绑定不再冒泡（图元与画面的版本号都不被刷）",
                bindingClear.Version == vBinding, $"{vBinding} → {bindingClear.Version}");

            // --- 文档级：自动命名 / 按身份与名字查找 ---
            var doc = new ScadaDocument();
            Check("新文档的结构版本号为当前版本",
                doc.SchemaVersion == ScadaDocument.CurrentSchemaVersion, $"SchemaVersion={doc.SchemaVersion}");

            var pg1 = doc.AddPage();
            var pg2 = doc.AddPage();
            Check("画面留空自动命名（画面_1 / 画面_2）",
                pg1.Name == "画面_1" && pg2.Name == "画面_2", $"{pg1.Name} / {pg2.Name}");

            var manual = doc.AddPage("  总览  ");
            Check("显式命名去除首尾空白", manual.Name == "总览", $"'{manual.Name}'");

            var pg3 = doc.AddPage();
            Check("自动命名跳过已占用序号（下一位是 画面_3）", pg3.Name == "画面_3", pg3.Name);

            Check("按身份找画面命中同一实例", ReferenceEquals(doc.FindPage(pg2.PageId), pg2), "");
            Check("Guid.Empty 找画面落空", doc.FindPage(Guid.Empty) == null, "");
            Check("按名找画面大小写不敏感且忽略首尾空白",
                ReferenceEquals(doc.FindPageByName(" 总览 "), manual), "");
            Check("按名找画面：空名落空（不做'匹配全部'式的误命中）",
                doc.FindPageByName(null) == null && doc.FindPageByName("   ") == null, "");

            // --- 文档级刷新的返回条数（改名日志据此回答"这次改名到底动了哪里"）---
            var statDoc = new ScadaDocument();
            var statPage = statDoc.AddPage("统计画面");
            var statElement = new ScadaElement { TypeKey = "Hmi.Label" };
            var statId = Guid.NewGuid();
            statElement.Bindings.Add(new ScadaBinding { TargetProperty = "A", VariableName = "V" });
            statElement.Bindings.Add(new ScadaBinding { TargetProperty = "B", VariableName = "V" });
            statElement.Bindings.Add(new ScadaBinding { TargetProperty = "C", VariableId = statId, VariableName = "X" });
            statElement.Bindings.Add(new ScadaBinding { TargetProperty = "D", VariableName = "别的" });
            statPage.Elements.Add(statElement);
            Check("文档级刷新返回跨画面命中条数（两条按名 + 一条按 Id）",
                statDoc.RefreshVariableReferences(statId, "V", "V2") == 3
                && statDoc.RefreshVariableReferences(Guid.NewGuid(), "V", "V2") == 0, "");

            // --- 旧数据补发身份 ---
            var legacyDoc = new ScadaDocument();
            var legacyPage = new ScadaPage { PageId = Guid.Empty, Name = "旧画面" };
            var legacyElement = new ScadaElement { ElementId = Guid.Empty, TypeKey = "Basic.Rectangle" };
            var legacyBinding = new ScadaBinding { TargetProperty = "Value", VariableName = "旧绑定变量" };
            legacyElement.Bindings.Add(legacyBinding);
            legacyPage.Elements.Add(legacyElement);
            legacyDoc.Pages.Add(legacyPage);

            int repaired = legacyDoc.EnsureIdentity();
            Check("旧画面补发四处身份（画面 + 图层 + 图元 + 图元归属）", repaired == 4, $"repaired={repaired}");
            Check("补发的身份非空且互不相同",
                legacyPage.PageId != Guid.Empty && legacyElement.ElementId != Guid.Empty
                && legacyPage.PageId != legacyElement.ElementId,
                $"page={legacyPage.PageId} / element={legacyElement.ElementId}");
            Check("S1 旧画面（没有 Layers 字段）打开即拥有一个默认图层，平铺的图元统一归到它上面",
                legacyPage.Layers.Count == 1 && legacyElement.LayerId == legacyPage.DefaultLayer!.LayerId,
                $"图层数={legacyPage.Layers.Count}");
            Check("绑定的 Guid.Empty 不被补发（它有'只能按名找'的语义，靠改名/加载期自愈回填）",
                legacyBinding.VariableId == Guid.Empty && legacyBinding.IsLegacyByName, "");
            Check("身份补发幂等（二次调用返回 0、身份不再变化）",
                legacyDoc.EnsureIdentity() == 0, "");
            Check("补发身份后索引立即可用（不必等下一次整体重建）",
                ReferenceEquals(legacyPage.FindElement(legacyElement.ElementId), legacyElement), "");
            // 走 AddPage 新建的画面自带图层：这里不是补出来的，图层面板因此永远不必面对空集合
            Check("新建画面自带一个自动命名的图层",
                doc.AddPage().Layers.Count == 1 && doc.AddPage().Layers[0].Name == "图层_1", "");
            legacyPage.Elements.Clear();
            Check("图元清空后图层仍然留着（删图元不等于删图层）", legacyPage.Layers.Count == 1, "");
        }

        // ==================================================================
        //  [O] 画面随方案落盘往返（SolutionService + $type 白名单 + 旧文件迁移）
        // ==================================================================
        private static void ScadaPersistenceRoundTrip()
        {
            Section("[O] 画面随方案落盘往返（.vms）");

            string dir = Path.Combine(Path.GetTempPath(), "ScadaChecks_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "scada.vms");

            try
            {
                var service = new SolutionService();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                var variableId = Guid.NewGuid();

                var page = solution.Scada.AddPage("主画面");
                page.Width = 1280;
                page.Height = 720;
                page.Background = "#FF102030";

                var element = new ScadaElement
                {
                    TypeKey = "Basic.Rectangle",
                    Name = "矩形1",
                    X = 12.5,
                    Y = 34,
                    Width = 200,
                    Height = 80,
                    Rotation = 45,
                    ZIndex = 3,
                    IsLocked = true
                };
                element.SetProperty("Fill", "#FF00FF00");
                element.SetProperty("Text", "转速");
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Value",
                    VariableId = variableId,
                    VariableName = "Speed",
                    DisplayFormat = "F2"
                });
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Visible",
                    VariableName = "Legacy",
                    IsEnabled = false
                });
                page.Elements.Add(element);

                var save = service.SaveAsync(solution, path).GetAwaiter().GetResult();
                Check("含画面的方案保存成功", save.Success, save.Message);

                string json = File.ReadAllText(path);
                Check("落盘 JSON 含 Scada 节点（画面确实随方案走，而不是活在内存里）",
                    json.Contains("\"Scada\"") && json.Contains("\"主画面\""), "");

                var load = service.LoadAsync(path).GetAwaiter().GetResult();
                Check("含画面的方案加载成功（$type 白名单未挡下 VisionMaster.Scada）",
                    load.Success, load.Message);

                var loaded = load.Data;
                Check("画面数量往返一致（整体替换而非与默认实例叠加）",
                    loaded.Scada.Pages.Count == 1, $"pages={loaded.Scada.Pages.Count}");

                var loadedPage = loaded.Scada.FindPageByName("主画面");
                Check("画面身份往返一致（按 Id 能找回同一画面）",
                    loadedPage != null && loadedPage.PageId == page.PageId, $"Id={loadedPage?.PageId}");
                Check("画面属性往返一致（尺寸 / 背景）",
                    loadedPage!.Width == 1280 && loadedPage.Height == 720
                    && loadedPage.Background == "#FF102030",
                    $"{loadedPage.Width}x{loadedPage.Height} / {loadedPage.Background}");

                var loadedElement = loadedPage.Elements.FirstOrDefault();
                Check("图元身份与几何往返一致",
                    loadedElement != null
                    && loadedElement.ElementId == element.ElementId
                    && loadedElement.X == 12.5 && loadedElement.Y == 34
                    && loadedElement.Width == 200 && loadedElement.Height == 80
                    && loadedElement.Rotation == 45 && loadedElement.ZIndex == 3
                    && loadedElement.IsLocked,
                    $"Id={loadedElement?.ElementId} / ({loadedElement?.X},{loadedElement?.Y})");
                Check("图元类型键往返一致（领域层只搬运、不认识它）",
                    loadedElement!.TypeKey == "Basic.Rectangle", loadedElement.TypeKey);
                Check("属性袋往返一致",
                    loadedElement.GetProperty("Fill") == "#FF00FF00"
                    && loadedElement.GetProperty("Text") == "转速", "");
                Check("绑定往返一致（按 Id 寻址的那条连同格式串）",
                    loadedElement.Bindings.Any(b => b.VariableId == variableId
                        && b.VariableName == "Speed" && b.DisplayFormat == "F2"), "");
                Check("停用的绑定同样往返保留（停用只是运行态跳过，配置不能丢）",
                    loadedElement.Bindings.Any(b => b.VariableName == "Legacy" && !b.IsEnabled), "");

                // 反序列化后订阅是否保活 —— 三件套的存在意义全押在这两条上
                int loadedVersion = loadedPage.Version;
                loadedElement.X = 999;
                Check("反序列化后图元属性订阅保活（改属性 → 画面版本号 +1）",
                    loadedPage.Version == loadedVersion + 1, $"{loadedVersion} → {loadedPage.Version}");

                int loadedVersion2 = loadedPage.Version;
                var loadedBinding = loadedElement.Bindings.First();
                loadedBinding.DisplayFormat = "F3";
                Check("反序列化后绑定属性订阅保活（改绑定 → 画面版本号 +1）",
                    loadedPage.Version == loadedVersion2 + 1, $"{loadedVersion2} → {loadedPage.Version}");

                // ============ 图层随方案落盘：分组开关也是文档内容，丢了等于把用户分好的组打平 ============

                var layerDoc = new ScadaDocument();
                var lpage = layerDoc.AddPage("图层画面");
                var layerBase = lpage.AddLayer("底图");
                var layerAlarm = lpage.AddLayer("报警");
                layerBase.IsLocked = true;
                layerAlarm.IsVisible = false;

                var eBase = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "管道", LayerId = layerBase.LayerId };
                var eAlarm = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "报警灯", LayerId = layerAlarm.LayerId };
                var ePlain = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "平铺的" };
                var danglingId = Guid.NewGuid();
                var eOrphan = new ScadaElement { TypeKey = "Basic.Rectangle", Name = "悬空", LayerId = danglingId };
                lpage.Elements.Add(eBase);
                lpage.Elements.Add(eAlarm);
                lpage.Elements.Add(ePlain);
                lpage.Elements.Add(eOrphan);

                var layerSolution = new SolutionModel { Scada = layerDoc };
                string layerPath = Path.Combine(dir, "layers.vms");
                Check("含多图层的方案保存成功",
                    service.SaveAsync(layerSolution, layerPath).GetAwaiter().GetResult().Success, "");

                string layerJson = File.ReadAllText(layerPath);
                Check("落盘 JSON 里有 Layers 节点与图层名（图层不是只活在内存里的临时分组）",
                    layerJson.Contains("\"Layers\"") && layerJson.Contains("\"底图\"") && layerJson.Contains("\"报警\""), "");

                var layerLoad = service.LoadAsync(layerPath).GetAwaiter().GetResult();
                Check("含多图层的方案加载成功", layerLoad.Success, layerLoad.Message);

                var rpage = layerLoad.Data.Scada.FindPageByName("图层画面");
                Check("图层数量与次序往返一致（列表次序就是图层面板的显示次序，新建画面自带的那层在最前）",
                    rpage != null && rpage.Layers.Count == 3
                    && string.Join("、", rpage.Layers.Select(l => l.Name)) == "图层_1、底图、报警",
                    rpage == null ? "null" : string.Join("、", rpage.Layers.Select(l => l.Name)));

                var rBase = rpage!.Layers.FirstOrDefault(l => l.Name == "底图");
                var rAlarm = rpage.Layers.FirstOrDefault(l => l.Name == "报警");
                Check("图层身份与两个开关往返一致（隐藏/锁定不丢，重开方案不该把锁住的底图解锁）",
                    rBase != null && rAlarm != null
                    && rBase.LayerId == layerBase.LayerId && rBase.IsLocked && rBase.IsVisible
                    && rAlarm.LayerId == layerAlarm.LayerId && !rAlarm.IsLocked && !rAlarm.IsVisible,
                    $"底图 locked={rBase?.IsLocked} visible={rBase?.IsVisible} / 报警 locked={rAlarm?.IsLocked} visible={rAlarm?.IsVisible}");

                var rBaseElement = rpage.Elements.First(e => e.Name == "管道");
                var rAlarmElement = rpage.Elements.First(e => e.Name == "报警灯");
                Check("图元归属按 Id 往返一致（按 LayerId 解析回同一图层）",
                    ReferenceEquals(rpage.ResolveLayer(rBaseElement), rBase)
                    && ReferenceEquals(rpage.ResolveLayer(rAlarmElement), rAlarm),
                    $"{rpage.ResolveLayer(rBaseElement)?.Name} / {rpage.ResolveLayer(rAlarmElement)?.Name}");

                Check("加载回来的图层仍驱动画面级判定（隐藏层上的图元一进来就判不可见，不用重算一遍）",
                    !rpage.IsElementVisible(rAlarmElement) && rpage.IsElementEditable(rAlarmElement)
                    && rpage.IsElementVisible(rBaseElement) && !rpage.IsElementEditable(rBaseElement), "");

                Check("保存时没写归属的平铺图元，加载后归进默认图层（旧画面的图元本来就铺在一起，归成一层是唯一不丢东西的解释）",
                    rpage.Elements.First(e => e.Name == "平铺的").LayerId == rpage.DefaultLayer!.LayerId, "");

                var rOrphan = rpage.Elements.First(e => e.Name == "悬空");
                Check("指向已删除图层的悬空归属原样保留，不被悄悄换个层（那种情形只有用户知道图元该去哪）",
                    rOrphan.LayerId == danglingId && rpage.ResolveLayer(rOrphan) == null, rOrphan.LayerId.ToString("N"));

                Check("补齐身份是幂等的：再调一次不重复补（重复打开同一方案不会越开越多图层）",
                    layerLoad.Data.Scada.EnsureIdentity() == 0 && rpage.Layers.Count == 3, rpage.Layers.Count.ToString());

                Check("归属认 Id 不认名字：加载后改图层名，图元还跟着同一层走",
                    rpage.TryRenameLayer(rAlarm, "报警层改名", out _)
                    && ReferenceEquals(rpage.ResolveLayer(rAlarmElement), rAlarm)
                    && rAlarm!.LayerId == layerAlarm.LayerId,
                    rpage.ResolveLayer(rAlarmElement)?.Name ?? "null");

                int layerVersion = rpage.Version;
                rBase!.IsVisible = false;
                Check("反序列化后图层属性订阅保活（改图层可见 → 画面 Version 动，否则隐藏图层不提示保存）",
                    rpage.Version == layerVersion + 1, $"{layerVersion} → {rpage.Version}");

                int layerVersion2 = rpage.Version;
                rpage.AddLayer("往返后再加一层");
                Check("反序列化后图层集合订阅保活（加图层 → 画面 Version 动）",
                    rpage.Version == layerVersion2 + 1, $"{layerVersion2} → {rpage.Version}");

                // --- 旧文件兼容：S1 存的画面根本没有 Layers 字段，也没有图元的 LayerId ---
                string oldLayerPath = Path.Combine(dir, "legacy-layers.vms");
                File.WriteAllText(oldLayerPath,
                    "{\"SolutionName\":\"旧图层方案\",\"Flows\":[],\"WatchItems\":[],"
                    + "\"VariableSnapshots\":[],\"CommunicationConfigs\":[],"
                    + "\"Scada\":{\"Pages\":[{\"Name\":\"老画面\",\"Elements\":["
                    + "{\"TypeKey\":\"Basic.Rectangle\",\"Name\":\"老图元甲\"},"
                    + "{\"TypeKey\":\"Basic.Rectangle\",\"Name\":\"老图元乙\"}]}]}}");

                var oldLayerLoad = service.LoadAsync(oldLayerPath).GetAwaiter().GetResult();
                var opage = oldLayerLoad.Data?.Scada?.Pages.FirstOrDefault();
                Check("没有 Layers 字段的旧画面能打开，并自动补出一个默认图层（图层面板得有个可操作的落点）",
                    oldLayerLoad.Success && opage != null && opage.Layers.Count == 1
                    && opage.DefaultLayer != null && opage.DefaultLayer.Name == "图层_1",
                    oldLayerLoad.Message + " / 图层数=" + (opage?.Layers.Count.ToString() ?? "null"));
                Check("旧画面的图元全部归进补出来的默认层，且不丢图元",
                    opage != null && opage.Elements.Count == 2 && opage.DefaultLayer != null
                    && opage.Elements.All(e => e.LayerId == opage.DefaultLayer.LayerId),
                    opage == null ? "画面缺失" : string.Join("、", opage.Elements.Select(e => $"{e.Name}:{e.LayerId:N}")));
                Check("补出来的图层当场就能改名/判定（与新建画面同一条路，不是只读的影子对象）",
                    opage != null && opage.DefaultLayer != null
                    && opage.TryRenameLayer(opage.DefaultLayer, "底图", out _)
                    && opage.Elements.All(e => opage.IsElementVisible(e) && opage.IsElementEditable(e)), "");

                // --- 旧文件迁移：老方案里根本没有 Scada 字段 / 被人工改成 null ---
                string legacyPath = Path.Combine(dir, "legacy.vms");
                File.WriteAllText(legacyPath,
                    "{\"SolutionName\":\"旧方案（无 Scada 字段）\",\"Flows\":[],\"WatchItems\":[],"
                    + "\"VariableSnapshots\":[],\"CommunicationConfigs\":[]}");
                var legacyLoad = service.LoadAsync(legacyPath).GetAwaiter().GetResult();
                Check("旧方案（无 Scada 字段）能打开且画面文档非空（字段初始化器兜底）",
                    legacyLoad.Success && legacyLoad.Data.Scada != null
                    && legacyLoad.Data.Scada.Pages.Count == 0, legacyLoad.Message);

                string nullPath = Path.Combine(dir, "nullscada.vms");
                File.WriteAllText(nullPath,
                    "{\"SolutionName\":\"手改文件\",\"Scada\":null,\"Flows\":[],\"WatchItems\":[],"
                    + "\"VariableSnapshots\":[],\"CommunicationConfigs\":[]}");
                var nullLoad = service.LoadAsync(nullPath).GetAwaiter().GetResult();
                Check("手改出的 \"Scada\": null 不炸（setter 兜底，后续 EnsureIdentity 可安全解引用）",
                    nullLoad.Success && nullLoad.Data.Scada != null, nullLoad.Message);

                // --- 启动画面（方案级单选 Id）与加载事件（画面级钩子）的落盘 ---
                // 这两个新字段一个是跨画面的单值、一个是每页一份，落盘层级不同，往返必须各自验一遍。
                var runDoc = new ScadaDocument();
                runDoc.AddPage("总览");
                var runSecond = runDoc.AddPage("详情");
                runDoc.SetStartupPage(runSecond);
                runSecond.EnableLoadedEvent = true;
                runSecond.GetOrAddEventHook(ScadaEventType.Loaded).Actions[0].Text = "详情已就位";

                var runSolution = new SolutionModel { Scada = runDoc };
                string runPath = Path.Combine(dir, "startup-page.vms");
                Check("带「启动画面 + 加载事件」配置的方案保存成功",
                    service.SaveAsync(runSolution, runPath).GetAwaiter().GetResult().Success, "");

                string runJson = File.ReadAllText(runPath);
                Check("落盘的是钩子集合而不是那个勾选框：画面上的事件与图元上的事件同一种形状",
                    runJson.Contains("\"StartupPageId\"") && runJson.Contains("\"EventHooks\"")
                    && runJson.Contains("详情已就位"),
                    $"StartupPageId={runJson.Contains("\"StartupPageId\"")} / EventHooks={runJson.Contains("\"EventHooks\"")}");
                // 「加载事件」这个勾只是投影，它不许再落一份盘：一处真相写在 ScadaPage.EnableLoadedEvent 的注释里，
                // 真落了盘就会出现"布尔勾着、钩子却被删了"这种两边对不上的文件，运行时听谁的都说不清。
                Check("投影字段绝不落盘（勾与钩子各存一份 = 迟早打架）",
                    !runJson.Contains("\"EnableLoadedEvent\""), "");

                var runLoad = service.LoadAsync(runPath).GetAwaiter().GetResult();
                var rdoc = runLoad.Data.Scada;
                var rdetail = rdoc?.FindPageByName("详情");
                Check("启动画面认 Id 不认名字：加载后指的还是原来那一页",
                    runLoad.Success && rdoc != null && rdetail != null
                    && rdoc.StartupPageId == runSecond.PageId
                    && ReferenceEquals(rdoc.ResolveStartupPage(), rdetail),
                    rdoc == null ? "文档缺失" : $"{rdoc.StartupPageId:N} / {rdoc.ResolveStartupPage()?.Name ?? "null"}");
                Check("加载事件勾在谁身上就还在谁身上：另一页不被牵连",
                    rdetail != null && rdetail.EnableLoadedEvent
                    && rdoc!.FindPageByName("总览") is { } roverview && !roverview.EnableLoadedEvent,
                    $"详情={rdetail?.EnableLoadedEvent} / 总览={(rdoc?.FindPageByName("总览")?.EnableLoadedEvent.ToString() ?? "null")}");
                // 上面那条只证明"勾"回来了；勾是投影，还得验投影底下那份真数据没在往返里变形。
                Check("动作文案与条数原样回来，且文件里没写动作的 CLR 类型名：认类型靠自己的 Type，不靠反射点名",
                    rdetail != null
                    && rdetail.EventHooks.Count == 1
                    && rdetail.EventHooks[0].Actions.Count == 1
                    && rdetail.EventHooks[0].Actions[0].Text == "详情已就位"
                    && rdetail.EventHooks[0].Actions[0].Type == ScadaActionType.Log
                    && !runJson.Contains("ScadaAction"),
                    rdetail == null ? "画面缺失" : $"钩子 {rdetail.EventHooks.Count} 条 / 动作 {rdetail.EventHooks[0].Actions.Count} 条");

                Check("删掉启动画面 = Id 跟着清空并回落第一页：不留指向已删画面的幽灵引用",
                    rdoc != null && rdetail != null
                    && rdoc.TryRemovePage(rdetail, out _)
                    && rdoc.StartupPageId == Guid.Empty
                    && ReferenceEquals(rdoc.ResolveStartupPage(), rdoc.Pages[0]),
                    rdoc == null ? "文档缺失" : $"{rdoc.StartupPageId:N} / {rdoc.ResolveStartupPage()?.Name ?? "null"}");

                // 先把启动指定位重新填上一个真实存在的页：不然下面传外来画面后"变空"说明不了任何问题
                // ——空→空是恒等式，测不出"拒绝外来画面"这条规则真的在生效。
                rdoc!.SetStartupPage(rdoc.Pages[0]);
                rdoc.SetStartupPage(runSecond); // runSecond 刚被移除，已不属于本方案
                Check("SetStartupPage 收到不属于本方案的画面按「取消指定」处理：不抛、不绑定外来 Id",
                    rdoc.StartupPageId == Guid.Empty
                    && ReferenceEquals(rdoc.ResolveStartupPage(), rdoc.Pages[0]),
                    $"{rdoc.StartupPageId:N} / 回落={rdoc.ResolveStartupPage()?.Name ?? "null"}");

                // 老文件（连这两个字段都还没有）：缺键 = "没配置"，不是"配置成了 false"
                Check("旧画面缺 EventHooks/StartupPageId 两个键时按未配置加载：不勾、回落第一页",
                    opage != null && oldLayerLoad.Data?.Scada != null
                    && !opage.EnableLoadedEvent
                    && oldLayerLoad.Data.Scada.StartupPageId == Guid.Empty
                    && ReferenceEquals(oldLayerLoad.Data.Scada.ResolveStartupPage(), opage),
                    opage == null ? "画面缺失" : $"{opage.EnableLoadedEvent}");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        // ==================================================================
        //  [P] 画面绑定的改名级联（WorkspaceContext 订阅注册表事件）
        // ==================================================================
        private static void ScadaRenameCascade()
        {
            Section("[P] 画面绑定的改名级联");

            var w = new WorkspaceContext();
            w.GlobalVariables.Clear();
            var speed = VariableFactory.CreateLocal("Speed", typeof(double), "转速", 0d);
            w.GlobalVariables.Add(speed);
            var registry = w.VariableRegistry;

            var solution = new SolutionModel();
            solution.Flows.Clear();

            var page = solution.Scada.AddPage("监视画面");

            // 三条绑定覆盖三种形态：老数据（只有名字）/ 新数据（有 Id）/ 无关
            var legacyBind = new ScadaBinding { TargetProperty = "Value", VariableName = "Speed" };
            var idBind = new ScadaBinding
            {
                TargetProperty = "Text",
                VariableId = speed.VariableId,
                VariableName = "早已改掉的旧名"
            };
            var unrelatedBind = new ScadaBinding { TargetProperty = "Visible", VariableName = "别的变量" };
            var labelElement = new ScadaElement { TypeKey = "Hmi.Label", Name = "转速标签" };
            labelElement.Bindings.Add(legacyBind);
            labelElement.Bindings.Add(idBind);
            labelElement.Bindings.Add(unrelatedBind);
            page.Elements.Add(labelElement);

            // 第二个画面上的绑定同样要被级联：级联遍历的是整个文档，不是"当前选中画面"
            var page2 = solution.Scada.AddPage("趋势画面");
            var trendElement = new ScadaElement { TypeKey = "Chart.Trend", Name = "转速曲线" };
            var secondPageBind = new ScadaBinding { TargetProperty = "Series", VariableName = "Speed" };
            trendElement.Bindings.Add(secondPageBind);
            page2.Elements.Add(trendElement);

            w.SwitchSolution(solution);

            Check("改名成功", registry.TryRename(speed, "Speed_Rpm", out var error), error);

            Check("老绑定按名命中：名字换新 + 补上稳定身份（一次改名完成迁移）",
                legacyBind.VariableName == "Speed_Rpm" && legacyBind.VariableId == speed.VariableId,
                $"Name={legacyBind.VariableName} / Id={legacyBind.VariableId}");
            Check("按 Id 寻址的绑定只刷展示名（名字字段为旧名也照样命中）",
                idBind.VariableName == "Speed_Rpm" && idBind.VariableId == speed.VariableId, idBind.VariableName);
            Check("无关绑定不被波及（同名不同变量不误伤）",
                unrelatedBind.VariableName == "别的变量" && unrelatedBind.VariableId == Guid.Empty, "");
            Check("非当前画面的绑定同样被级联（遍历整个文档，不止选中画面）",
                secondPageBind.VariableName == "Speed_Rpm" && secondPageBind.VariableId == speed.VariableId,
                $"Name={secondPageBind.VariableName} / Id={secondPageBind.VariableId}");

            Check("第二次改名同样完成级联（不依赖'只触发一次'）",
                registry.TryRename(speed, "Speed_Rpm_2", out _)
                && legacyBind.VariableName == "Speed_Rpm_2"
                && secondPageBind.VariableName == "Speed_Rpm_2"
                && idBind.VariableName == "Speed_Rpm_2",
                legacyBind.VariableName);

            // 级联之后再改一次名，绑定已全部按 Id 寻址：名字刷新照旧、Id 一个都不该变
            var idBefore = legacyBind.VariableId;
            Check("级联后绑定已全部按 Id 寻址（Id 稳定不变）",
                registry.TryRename(speed, "Speed_Rpm_3", out _)
                && legacyBind.VariableId == idBefore
                && legacyBind.VariableName == "Speed_Rpm_3", $"Id={legacyBind.VariableId}");
        }

        // ==================================================================
        //  [Q] 万级图元 / 万级绑定的画面吞吐（--stress）
        // ==================================================================
        private static void StressScadaDocument()
        {
            Section("[Q] 万级图元 / 万级绑定的画面吞吐（--stress）");

            const int N = 10000;
            var varId = Guid.NewGuid();

            var sw = Stopwatch.StartNew();
            var page = new ScadaPage();
            var elements = new ScadaElement[N];
            for (int i = 0; i < N; i++)
            {
                var element = new ScadaElement
                {
                    TypeKey = "Hmi.Label",
                    Name = $"标签{i}",
                    X = i % 1920,
                    Y = i % 1080
                };
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Value",
                    VariableId = varId,
                    VariableName = "Speed"
                });
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Visible",
                    VariableName = "Legacy"
                });
                elements[i] = element;
            }

            foreach (var element in elements)
                page.Elements.Add(element);
            var buildMs = sw.ElapsedMilliseconds;

            Check($"{N} 图元 + {N * 2} 绑定构造并挂入画面耗时 < 3000ms", buildMs < 3000, $"{buildMs}ms");
            Check("图元身份全部唯一", elements.Select(e => e.ElementId).Distinct().Count() == N, "");
            Check("跨图元枚举绑定条数正确",
                page.EnumerateBindings().Count() == N * 2, $"count={page.EnumerateBindings().Count()}");
            Check("每次图元挂入都计入版本号",
                page.Version >= N, $"Version={page.Version}");

            // 改名级联：一半绑定（按名的那条）应命中
            sw.Restart();
            int changed = page.RefreshVariableReferences(Guid.NewGuid(), "Legacy", "Legacy_2");
            var refreshMs = sw.ElapsedMilliseconds;
            Check($"{N * 2} 条绑定的级联遍历耗时 < 1000ms 且命中数正确",
                changed == N && refreshMs < 1000, $"changed={changed} / {refreshMs}ms");

            // 属性变更吞吐：每次都要冒泡到画面版本号（订阅链不能拖垮拖拽手感）
            sw.Restart();
            for (int i = 0; i < N; i++)
                elements[i].X += 1;
            var mutateMs = sw.ElapsedMilliseconds;
            Check($"{N} 次属性变更（逐次冒泡到画面版本号）耗时 < 1000ms",
                mutateMs < 1000 && page.Version >= N * 2, $"Version={page.Version} / {mutateMs}ms");

            // 按身份查找：设计期选中、运行态取图元都走它，不能是线性扫描量级的退化
            const int probes = 200000;
            sw.Restart();
            int hits = 0;
            for (int i = 0; i < probes; i++)
            {
                if (page.FindElement(elements[i % N].ElementId) != null)
                    hits++;
            }
            var findMs = sw.ElapsedMilliseconds;
            Check($"按身份查找 {probes} 次全部命中且耗时 < 3000ms",
                hits == probes && findMs < 3000, $"hits={hits} / {findMs}ms");

            // 旧数据迁移路径：万级图元一次性补发身份。
            // 守的是"补 Id 不能逐个整表重建索引"——逐次重建就是 O(n²)，
            // 万级画面会在打开方案时卡住数秒（这正是给 EnsureIdentity 加挂起标志的原因）。
            var legacy = new ScadaPage();
            for (int i = 0; i < N; i++)
                legacy.Elements.Add(new ScadaElement { ElementId = Guid.Empty, TypeKey = "Hmi.Label" });

            sw.Restart();
            int repairedCount = legacy.EnsureIdentity();
            var repairMs = sw.ElapsedMilliseconds;
            // 处数口径：每个图元两处（ElementId + LayerId 归属）+ 一个默认图层 = N*2 + 1
            Check($"旧数据补发 {N * 2 + 1} 处身份（含一次索引重建）耗时 < 1000ms 且索引可用",
                repairedCount == N * 2 + 1 && repairMs < 1000
                && legacy.Elements.All(e => ReferenceEquals(legacy.FindElement(e.ElementId), e)),
                $"repaired={repairedCount} / {repairMs}ms");
            // 图层归属在万级下也必须成立：平铺的旧图元全部归进同一个默认图层
            Check($"万级图元的归属统一落到同一个默认图层（{N} 个图元 / 1 个图层）",
                legacy.Layers.Count == 1
                && legacy.CountElements(legacy.DefaultLayer) == N
                && legacy.Elements.All(e => e.LayerId == legacy.DefaultLayer!.LayerId),
                $"图层数={legacy.Layers.Count} / 归属数={legacy.CountElements(legacy.DefaultLayer)}");
        }

        // ==================================================================
        //  断言基础设施（与 FlowCanvasChecks 保持同款输出格式）
        // ==================================================================

        /// <summary>
        /// 经统一入口改名（写模型 + 修索引 + 广播一体）。
        /// 断言与生产代码走同一个 API——只在断言里用两步写法（写模型 + 手动 NotifyRenamed），
        /// 就等于在验证一条生产环境不存在的路径。
        /// </summary>
        private static void Rename(IVariableRegistry registry, IVariable variable, string newName)
        {
            if (!registry.TryRename(variable, newName, out var error))
                throw new InvalidOperationException($"断言前置动作失败：改名 '{newName}' 被拒（{error}）");
        }

        /// <summary>
        /// 绕过注册表的裸改名（直接写模型名）。只用于反向断言：证明漏走统一入口会留下死键。
        /// S0-b 之前工程里散落的 <c>model.Name = ...</c> 正是这个形状。
        /// </summary>
        private static void RawRename(IVariable variable, string newName)
        {
            if (variable is not IRenameableVariable renameable)
                throw new InvalidOperationException($"断言未覆盖的变量模型：{variable.GetType().Name}");

            renameable.Name = newName;
        }

        /// <summary>
        /// 在"静音"状态下执行一段动作：<see cref="AdvancedCommunicationManager"/> 的诊断日志
        /// 直接走 Console.WriteLine，会与断言输出交错（注册/注销每个变量都留一行）。
        /// 本段断言关心的是"它做了什么"，不是"它说了什么"。
        /// </summary>
        private static void Quiet(Action action)
        {
            var real = Console.Out;
            try
            {
                Console.SetOut(TextWriter.Null);
                action();
            }
            finally
            {
                Console.SetOut(real);
            }
        }

        /// <summary>通信管理器"已注册变量清单"私有字段（见 <see cref="IsRegistered"/> 的说明）</summary>
        private static readonly FieldInfo? RegisteredVariablesField = typeof(AdvancedCommunicationManager)
            .GetField("_registeredVariables", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 白盒探针：某个 (连接名, 变量名) 是否仍在通信管理器的轮询清单里。
        ///
        /// 为什么必须反射：注册/注销的唯一可观测后果就是这个私有字典——管理器没有暴露任何
        /// "查询已注册变量"的公开 API，轮询计划又只在连接存在时才编译。断言"旧键已摘、新键已挂"
        /// 若不能直接读它，就只能靠肉眼看日志，那不叫断言。
        /// </summary>
        private static bool IsRegistered(AdvancedCommunicationManager manager, string connectionName, string variableName)
        {
            if (RegisteredVariablesField?.GetValue(manager)
                is not ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationVariable>> all)
                return false;

            return all.TryGetValue(connectionName, out var variables) && variables.ContainsKey(variableName);
        }

        /// <summary>白盒探针：取出注册在管理器里的 <see cref="CommunicationVariable"/> 实例</summary>
        private static CommunicationVariable? RegisteredVariable(AdvancedCommunicationManager manager, string connectionName, string variableName)
        {
            if (RegisteredVariablesField?.GetValue(manager)
                is not ConcurrentDictionary<string, ConcurrentDictionary<string, CommunicationVariable>> all)
                return null;

            return all.TryGetValue(connectionName, out var variables) && variables.TryGetValue(variableName, out var variable)
                ? variable
                : null;
        }

        /// <summary>
        /// 日志桩：捕获被测代码"出声"的内容。
        /// 断言"该警告的地方确实警告了"——未注册的网络变量静默不接线，是"当前值永远空"的头号暗坑。
        /// </summary>
        private sealed class LoggerStub : ILogService
        {
            public List<string> Warnings { get; } = new();

            /// <summary>
            /// 全级别、按调用次序记账（每行带 "I/W/E/S" 前缀）。
            /// 动作分发器那组断言要钉的是"一串动作按集合次序执行"，只留 Warnings 就把次序打断了。
            /// </summary>
            public List<string> Lines { get; } = new();

            public void Success(params string[] messages) => Record("S", messages);

            public void Error(params Exception[] messages) => Record("E", messages.Select(m => m.Message).ToArray());

            public void Error(params string[] messages) => Record("E", messages);

            public void Info(params string[] messages) => Record("I", messages);

            public void Warn(params string[] messages)
            {
                Warnings.AddRange(messages);
                Record("W", messages);
            }

            private void Record(string level, string[] messages)
            {
                foreach (var message in messages)
                    Lines.Add(level + " " + message);
            }
        }
        // ==================================================================
        //  [R] 图元注册表：内置图元自注册、描述符自检、TypeKey 工厂、属性袋↔强类型字段
        // ==================================================================
        private static void ElementRegistryChecks()
        {
            Section("[R] 图元注册表与内置图元描述符");

            Check("内置图元在类型初始化时自注册（宿主不必记得「先注册再使用」）",
                ElementRegistry.BuiltInErrors.Count == 0, string.Join(" | ", ElementRegistry.BuiltInErrors));

            var descriptorErrors = ElementRegistry.ValidateAll();
            Check("全部已注册描述符通过自检（键不重复、默认值能转成目标类型、几何键齐全）",
                descriptorErrors.Count == 0, string.Join(" | ", descriptorErrors));

            // 这份清单是"内置图元不许悄悄掉队"的名册：S7 每加一个图元就在这里加一行，
            // 漏加不会让断言变红（下面那条是"清单里的都在"），所以它同时也是新增图元的提醒处。
            string[] builtIns =
            {
                "Hmi.Rectangle", "Hmi.Ellipse", "Hmi.Text", "Hmi.Button", "Hmi.Indicator",
                "Hmi.ProgressBar", "Hmi.IOField", "Hmi.Lamp", "Hmi.Clock", "Hmi.Gauge",
                "Hmi.Valve",
            };
            Check("11 个内置图元全部注册",
                builtIns.All(ElementRegistry.IsRegistered),
                string.Join("、", ElementRegistry.All.Select(d => d.TypeKey)));

            // 名册与注册表必须等长：只钉"都在"会让"新增图元忘了登记名册"悄悄过关，
            // 而名册正是 [U] 段逐类遍历之外的又一道"图元有没有少"的账。
            Check("名册条数 = 注册表全量（新增图元漏登记会在这里红掉，而不是悄悄少一条）",
                builtIns.Length == ElementRegistry.All.Count,
                $"名册 {builtIns.Length} 条 / 注册表 {ElementRegistry.All.Count} 条");

            Check("每个内置图元的 TypeKey 都带 Hmi. 前缀（这是写进 .vms 的长期标识，发布后不许改）",
                ElementRegistry.All.All(d => d.TypeKey.StartsWith("Hmi.", StringComparison.Ordinal)), "");

            Check("类型键大小写不敏感（手写的 .vms 写错大小写仍能打开）",
                ElementRegistry.IsRegistered("hmi.rectangle") && ElementRegistry.Find("HMI.RECTANGLE") is not null, "");

            Check("未知/空类型键一律返回 null 而不是抛异常（由调用方决定容错策略）",
                ElementRegistry.Find("Hmi.NoSuch") is null && ElementRegistry.Find(null) is null
                && ElementRegistry.Find("") is null && !ElementRegistry.IsRegistered("Hmi.NoSuch"), "");

            // --- 工厂：字符串类型键 → 模型 ---
            var rect = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);
            Check("工厂按描述符填默认名字与尺寸",
                rect.TypeKey == "Hmi.Rectangle" && rect.Name == "矩形"
                && rect.X == 10 && rect.Y == 20 && rect.Width == 120 && rect.Height == 60,
                $"{rect.Name} ({rect.X},{rect.Y}) {rect.Width}×{rect.Height}");

            Check("新建图元各自带稳定且唯一的 ElementId（复制粘贴不该撞身份）",
                rect.ElementId != Guid.Empty
                && ElementRegistry.CreateElement("Hmi.Rectangle").ElementId != rect.ElementId, "");

            bool factoryThrew = false;
            try { ElementRegistry.CreateElement("Hmi.NoSuch"); }
            catch (InvalidOperationException) { factoryThrew = true; }
            Check("按未注册类型造图元当场抛错（笔误不该静默变成空白图元）", factoryThrew, "");

            // --- 属性描述符查找 ---
            Check("按「类型键 + 属性键」能查到属性描述符",
                ElementRegistry.FindProperty("Hmi.Indicator", "IsOn")?.Kind == ElementPropertyKind.Bool,
                ElementRegistry.FindProperty("Hmi.Indicator", "IsOn")?.DisplayName ?? "null");

            Check("属性查找对未知键/空键/未知类型返回 null",
                ElementRegistry.FindProperty("Hmi.Indicator", "NoSuch") is null
                && ElementRegistry.FindProperty("Hmi.Indicator", null) is null
                && ElementRegistry.FindProperty("Hmi.NoSuch", "IsOn") is null, "");

            Check("每个图元的属性清单都含 6 个几何键（属性面板与绑定引擎的硬前提）",
                ElementRegistry.All.All(d => ElementValueAccess.KnownGeometryKeys.All(k => d.Properties.Any(p => p.Key == k))), "");

            Check("几何键不声明 TargetProperty（由基类统一落到 FrameworkElement，声明了就是两个写入源）",
                ElementRegistry.All.All(d => d.Properties.Where(p => p.IsGeometry).All(p => p.TargetProperty is null)), "");

            Check("Choice 型属性必然列出候选项（属性面板画下拉框要用）",
                ElementRegistry.All.SelectMany(d => d.Properties)
                    .Where(p => p.Kind == ElementPropertyKind.Choice)
                    .All(p => p.Choices.Count > 0), "");

            // S7 的验收口径里有一条「描述符必须声明可绑变量类型」：指示 / 数值类图元的那个"输入"
            // 属性一定要 IsBindable，否则运行态接不上变量，图元就只是个静态装饰——
            // 而"图元画得出来但绑不了变量"正是 D2（组态 → 运行）最想避免的那种半成品。
            string[] bindableInputs = { "Hmi.ProgressBar:Value", "Hmi.IOField:Value", "Hmi.Lamp:State", "Hmi.Gauge:Value", "Hmi.Valve:Opening" };
            Check("新增图元的「输入」属性都声明了 IsBindable（否则运行态接不上变量）",
                bindableInputs.All(spec =>
                {
                    var parts = spec.Split(':');
                    return ElementRegistry.FindProperty(parts[0], parts[1])?.IsBindable == true;
                }),
                string.Join("、", bindableInputs));

            // 时钟没有可绑的"输入"（它的值来自系统时间），但它得有格式串与节拍两个可配项。
            Check("时钟图元声明了格式与节拍两个可配项（它的输入是系统时间，不绑变量）",
                ElementRegistry.FindProperty("Hmi.Clock", "Format") is not null
                && ElementRegistry.FindProperty("Hmi.Clock", "Interval") is not null, "");

            // --- 属性袋 / 保留键：字符串 ↔ 强类型字段 ---
            var element = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);

            ElementValueAccess.Write(element, ElementValueAccess.WidthKey, "150.5");
            Check("写 $Width 落到强类型字段，读回用不变文化（.vms 跨机器交换，小数点不能跟区域设置走）",
                element.Width == 150.5 && ElementValueAccess.Read(element, ElementValueAccess.WidthKey) == "150.5",
                $"Width={element.Width} 读回=\"{ElementValueAccess.Read(element, ElementValueAccess.WidthKey)}\"");

            ElementValueAccess.Write(element, ElementValueAccess.HeightKey, "-40");
            Check("几何键写负数取绝对值（模型约定宽高恒正）", element.Height == 40, $"Height={element.Height}");

            ElementValueAccess.Write(element, "$Widht", "99");
            Check("拼错的保留键被丢弃（既不生效，也不漏进属性袋躺着）",
                !element.Properties.ContainsKey("$Widht") && element.Properties.Count == 0,
                $"属性袋条目={element.Properties.Count}");

            ElementValueAccess.Write(element, ElementValueAccess.XKey, "abc");
            Check("几何键写非法数值保持原值（不写 NaN，也不用 0 顶替）", element.X == 10, $"X={element.X}");

            ElementValueAccess.Write(element, ElementValueAccess.NameKey, null);
            Check("名字写 null 退化为空串（Name 不允许 null）", element.Name == string.Empty, "");

            ElementValueAccess.Write(element, "Fill", "#FF112233");
            Check("非保留键落属性袋",
                element.Properties.TryGetValue("Fill", out var fill) && fill == "#FF112233", fill ?? "缺失");

            bool hasFill = element.Properties.ContainsKey("Fill");
            ElementValueAccess.Write(element, "Fill", null);
            Check("属性袋写 null 即删键（与 ScadaElement.SetProperty 语义一致）",
                hasFill && !element.Properties.ContainsKey("Fill"), "");

            var fontSize = ElementRegistry.FindProperty("Hmi.Rectangle", "FontSize")!;
            Check("读描述符属性：模型没配就现取默认值（默认值不落进属性袋）",
                ElementValueAccess.Read(element, fontSize) == "12" && !element.Properties.ContainsKey("FontSize"), "");
        }

        // ==================================================================
        //  [S] 图元控件层：描述符 → 控件 → 依赖属性（在独立 STA 线程内执行）
        // ==================================================================
        private static void ElementControlChecks()
        {
            Section("[S] 图元控件层：模型 → 控件 → 依赖属性");

            Exception? failure = null;

            // 主线程不是 STA，而 WPF 控件必须在 STA 上创建，所以另起一个 STA 线程专跑控件层断言：
            // 既满足 WPF 的房间要求，也不必把 Main 标成 STAThread 去改变 S0/S1 断言的既有运行环境。
            var thread = new Thread(() =>
            {
                try { RunElementControlChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("控件层断言全程未抛异常", false, failure.ToString());
        }

        private static void RunElementControlChecks()
        {
            // --- 注册表造控件并绑模型 ---
            var element = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);
            var control = ElementRegistry.CreateControl(element);

            Check("按字符串类型键造出对应控件（.vms 里只存字符串，这里是唯一的翻译点）",
                control is RectangleElement, control.GetType().Name);
            Check("造出来的控件已绑上模型（不必调用方二次赋值）",
                ReferenceEquals(control.Element, element), "");

            Check("几何单向落地：位置贴到 Canvas 附加属性",
                Canvas.GetLeft(control) == 10 && Canvas.GetTop(control) == 20,
                $"({Canvas.GetLeft(control)},{Canvas.GetTop(control)})");
            Check("几何单向落地：尺寸写控件自身 Width/Height",
                control.Width == 120 && control.Height == 60, $"{control.Width}×{control.Height}");

            // --- 描述符默认值 → 依赖属性（走 WPF 自带类型转换器）---
            Check("模型没配的属性用描述符默认值（矩形默认白底）",
                control.Fill is SolidColorBrush { Color: var fillDefault } && fillDefault == Colors.White,
                control.Fill?.ToString() ?? "null");
            Check("颜色字符串由 WPF 的 Brush 转换器解析成画刷",
                control.Stroke is SolidColorBrush { Color: var strokeDefault }
                && strokeDefault == Color.FromRgb(0x7A, 0x7A, 0x7A),
                control.Stroke?.ToString() ?? "null");
            Check("枚举/结构型默认值同样能转（FontWeight \"Normal\"）",
                control.FontWeight == FontWeights.Normal, control.FontWeight.ToString());

            // --- 属性袋 → 依赖属性（编辑器/运行态改模型后控件被对齐）---
            // 未挂可视树时控件不订阅模型（见 ScadaElementBase.SyncSubscription），
            // 所以这里显式 Refresh()——它正是 S3/S4「批量改完一堆属性后强制对齐」要调的那个 API。
            element.SetProperty("Fill", "#FF112233");
            control.Refresh();
            Check("属性袋写入后落到依赖属性",
                control.Fill is SolidColorBrush { Color: var fillWritten }
                && fillWritten == Color.FromArgb(0xFF, 0x11, 0x22, 0x33),
                control.Fill?.ToString() ?? "null");

            element.SetProperty("Text", "1#电机");
            control.Refresh();
            Check("文本落到 Text 依赖属性", control.Text == "1#电机", control.Text ?? "null");

            element.SetProperty("StrokeThickness", "0");
            control.Refresh();
            var thinInset = control.Padding.Left;

            element.SetProperty("StrokeThickness", "10");
            control.Refresh();
            Check("数值类属性按不变文化转成 double",
                Math.Abs(control.StrokeThickness - 10) < 1e-9, control.StrokeThickness.ToString());
            Check("边框内缩随线宽走（半线宽进 Padding，模板用 Margin 内缩，避免 100 宽的框视觉变 103）",
                Math.Abs(control.Padding.Left - (thinInset + 5)) < 1e-9,
                $"线宽 0 → {thinInset}，线宽 10 → {control.Padding.Left}");

            // --- 脏值不打断渲染 ---
            element.SetProperty("FontSize", "不是数字");
            control.Refresh();
            Check("转不了的脏值保持现状而不抛异常（一个坏值不该让整页渲染中断）",
                control.FontSize == 12, control.FontSize.ToString());

            element.SetProperty("FontSize", null);
            control.Refresh();
            Check("删键后回落到描述符默认值（而不是把值抹成 0）",
                control.FontSize == 12, control.FontSize.ToString());

            // --- 几何：位置/尺寸/旋转 ---
            element.X = 55;
            element.Y = 66;
            element.Width = 200;
            element.Height = 100;
            element.Rotation = 90;
            control.Refresh();
            Check("几何改动全量落地（位置/尺寸/旋转）",
                Canvas.GetLeft(control) == 55 && Canvas.GetTop(control) == 66
                && control.Width == 200 && control.Height == 100
                && control.RenderTransform is RotateTransform { Angle: 90 }, "");

            Check("旋转原点取相对中心的 (0.5,0.5)（转轴随尺寸自动跟着走，不留旧中心点）",
                control.RenderTransformOrigin == new Point(0.5, 0.5), control.RenderTransformOrigin.ToString());

            element.Rotation = 0;
            control.Refresh();
            Check("旋转归零时摘掉 RenderTransform（不在可视树上留一个恒等变换）",
                control.RenderTransform is null, "");

            // --- 未挂载控件与模型的解耦 ---
            var beforeX = Canvas.GetLeft(control);
            element.X = 999;
            Check("未挂可视树的控件不订阅模型（文档对象不会反过来钉住控件；挂载时 Loaded 补一次 Refresh 追上）",
                Canvas.GetLeft(control) == beforeX, $"Canvas.Left={Canvas.GetLeft(control)}");

            // --- 未知类型：只落几何、不落属性 ---
            var orphan = new RectangleElement
            {
                Element = new ScadaElement { TypeKey = "Hmi.NoSuch", X = 1, Y = 2, Width = 30, Height = 40 },
            };
            Check("未知图元只落几何、不落属性、也不抛异常（低版本打开高版本 .vms 仍能看见画面结构）",
                Canvas.GetLeft(orphan) == 1 && Canvas.GetTop(orphan) == 2
                && orphan.Width == 30 && orphan.Height == 40 && orphan.Fill == Brushes.Transparent, "");

            orphan.Element = null;
            Check("摘掉模型后控件不崩（渲染成模板默认样子）", orphan.Element is null, "");

            // --- 指示灯：外观由三个输入推导，只有一个写入源 ---
            var lampElement = ElementRegistry.CreateElement("Hmi.Indicator");
            var lamp = (IndicatorElement)ElementRegistry.CreateControl(lampElement);

            lampElement.SetProperty("OnColor", "#FF00FF00");
            lampElement.SetProperty("OffColor", "#FFFF0000");
            lamp.Refresh();
            Check("指示灯熄灭时显熄灭色（灯色由 IsOn + 两色推导，不是可写的 Fill）",
                lamp.LampBrush is SolidColorBrush { Color: var lampOff }
                && lampOff == Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                lamp.LampBrush?.ToString() ?? "null");

            lampElement.SetProperty("IsOn", "True");
            lamp.Refresh();
            Check("IsOn 置真后切到点亮色",
                lamp.LampBrush is SolidColorBrush { Color: var lampOn }
                && lampOn == Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                lamp.LampBrush?.ToString() ?? "null");

            lampElement.SetProperty("Shape", "Square");
            lamp.Refresh();
            Check("Choice 型属性按 WPF 枚举转换器落地", lamp.Shape == IndicatorShape.Square, lamp.Shape.ToString());

            lampElement.SetProperty("Shape", "NoSuchShape");
            lamp.Refresh();
            Check("非法枚举值保持现状（脏值不会把外观打回默认）",
                lamp.Shape == IndicatorShape.Square, lamp.Shape.ToString());

            // --- 文本：自建对齐依赖属性 ---
            var textElement = ElementRegistry.CreateElement("Hmi.Text");
            var textControl = (TextElement)ElementRegistry.CreateControl(textElement);
            Check("文本图元的默认文案现取描述符默认值（模型没写就不落属性袋）",
                textControl.Text == "文本", textControl.Text ?? "null");

            textElement.SetProperty("TextAlignment", "Right");
            textControl.Refresh();
            Check("水平对齐落到自建依赖属性（Control.HorizontalContentAlignment 的类型与它不通用）",
                textControl.TextAlignment == TextAlignment.Right, textControl.TextAlignment.ToString());

            // --- 按钮：默认外观来自描述符 ---
            var button = ElementRegistry.CreateControl(ElementRegistry.CreateElement("Hmi.Button"));
            Check("按钮是控件库自己的图元控件，不是 WPF 的 Button（点击语义留给运行态 S4）",
                button is ButtonElement
                && !typeof(System.Windows.Controls.Primitives.ButtonBase).IsAssignableFrom(button.GetType()),
                button.GetType().Name);
            Check("按钮默认外观来自描述符（品牌蓝底 + 居中文字）",
                button.Fill is SolidColorBrush { Color: var buttonFill }
                && buttonFill == Color.FromArgb(0xFF, 0x2D, 0x7D, 0xD2)
                && button.Text == "按钮", button.Fill?.ToString() ?? "null");

            // --- 棒图：值在量程里的比例 → 只读填充长度 ---
            //
            // 填充长度取决于"控件此刻有多少像素可用"，所以必须先把布局跑完再断言——
            // 真机也是布局之后条子才画得出来；不跑布局时 ActualWidth 是 0，
            // 断言会退化成"两个 0 相等"的伪通过。
            var barElement = ElementRegistry.CreateElement("Hmi.ProgressBar");
            var bar = (ProgressBarElement)ElementRegistry.CreateControl(barElement);
            bar.Measure(new Size(160, 20));
            bar.Arrange(new Rect(0, 0, 160, 20));

            barElement.SetProperty("Value", "50");
            bar.Refresh();
            Check("棒图：值 50 / 量程 0~100 → 填充长度是可用宽度的一半（长度算在控件里，断言直接读它，不必数像素）",
                Math.Abs(bar.FillLength - (bar.ActualWidth - 2 * bar.StrokeThickness) * 0.5) < 0.5,
                $"FillLength={bar.FillLength:0.##}，可用宽度={bar.ActualWidth - 2 * bar.StrokeThickness:0.##}");

            Check("棒图：条上数值按格式串格式化（默认 0.# → 50）",
                bar.ValueText == "50", bar.ValueText ?? "null");

            barElement.SetProperty("Value", "250");
            bar.Refresh();
            Check("棒图：值超上限按满条收敛（不画出界）",
                Math.Abs(bar.FillLength - (bar.ActualWidth - 2 * bar.StrokeThickness)) < 0.5,
                $"FillLength={bar.FillLength:0.##}");

            barElement.SetProperty("Value", "-250");
            bar.Refresh();
            Check("棒图：值低于下限按空条收敛", bar.FillLength == 0, $"FillLength={bar.FillLength:0.##}");

            barElement.SetProperty("Value", "50");
            barElement.SetProperty("Minimum", "100");
            barElement.SetProperty("Maximum", "0");
            bar.Refresh();
            Check("棒图：量程配错（下限 ≥ 上限）画空条，而不是让 NaN 把整页渲染打断",
                bar.FillLength == 0 && !double.IsNaN(bar.FillLength), $"FillLength={bar.FillLength}");

            barElement.SetProperty("Minimum", "0");
            barElement.SetProperty("Maximum", "100");
            barElement.SetProperty("Orientation", "Vertical");
            bar.Refresh();
            Check("棒图：纵向走向时按可用高度算长度（液位、料位天生是竖着的）",
                Math.Abs(bar.FillLength - (bar.ActualHeight - 2 * bar.StrokeThickness) * 0.5) < 0.5,
                $"FillLength={bar.FillLength:0.##}，可用高度={bar.ActualHeight - 2 * bar.StrokeThickness:0.##}");

            barElement.SetProperty("Orientation", "Horizontal");
            barElement.SetProperty("ValueFormat", "Z");
            bar.Refresh();
            Check("棒图：格式串写错退回通用格式（一个坏值不该让整页渲染中断）",
                bar.ValueText == "50", bar.ValueText ?? "null");

            // --- 多态灯：状态 → 灯色（四个颜色输入里挑一个）---
            var polyElement = ElementRegistry.CreateElement("Hmi.Lamp");
            var poly = (LampElement)ElementRegistry.CreateControl(polyElement);

            Check("多态灯默认熄灭色（\"没有信息\"也是一种状态，得画得出来）",
                poly.LampBrush is SolidColorBrush { Color: var polyOff }
                && polyOff == Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A),
                poly.LampBrush?.ToString() ?? "null");

            polyElement.SetProperty("State", "Warning");
            poly.Refresh();
            Check("多态灯：状态 Warning → 挑警告色（灯色由状态从四色里挑，不是可写的 Fill）",
                poly.LampBrush is SolidColorBrush { Color: var polyWarn }
                && polyWarn == Color.FromArgb(0xFF, 0xFF, 0xB0, 0x20),
                poly.LampBrush?.ToString() ?? "null");

            var stateProperty = ElementRegistry.FindProperty("Hmi.Lamp", "State")!;
            bool stateOk = poly.TryApplyRuntimeValue(stateProperty, 3, null, out var stateError);
            Check("多态灯：运行态按枚举序号换算（变量传整数 3 → 报警，灯色跟着切）",
                stateOk && poly.State == LampState.Alarm
                && poly.LampBrush is SolidColorBrush { Color: var polyAlarm }
                && polyAlarm == Color.FromArgb(0xFF, 0xE0, 0x3A, 0x2B),
                stateError ?? poly.State.ToString());

            bool stateRejected = !poly.TryApplyRuntimeValue(stateProperty, 9, null, out var rangeError);
            Check("多态灯：越界序号被挡在门外且灯保持原状（Enum.IsDefined 兜底，不落到别的状态上）",
                stateRejected && poly.State == LampState.Alarm && !string.IsNullOrEmpty(rangeError),
                rangeError ?? "无错误说明");

            // --- 时钟：值的来源在软件自己身上（系统时间 + 自己的节拍源）---
            var clockElement = ElementRegistry.CreateElement("Hmi.Clock");
            var clock = (ClockElement)ElementRegistry.CreateControl(clockElement);

            Check("时钟：构造即填第一帧（断言环境不挂可视树、定时器不跳，DisplayText 不能是个空框）",
                !string.IsNullOrEmpty(clock.DisplayText), clock.DisplayText ?? "null");

            clockElement.SetProperty("Format", "HH:mm:ss");
            clock.Refresh();
            var clockShown = clock.DisplayText ?? "";
            Check("时钟：改格式串后显示串跟着变（HH:mm:ss → 8 位、第 3 与第 6 个字符是冒号）",
                clockShown.Length == 8 && clockShown[2] == ':' && clockShown[5] == ':',
                clockShown);

            clockElement.SetProperty("Format", "Z");
            clock.Refresh();
            var clockFallback = clock.DisplayText ?? "";
            Check("时钟：格式串写错退回默认形状（yyyy-MM-dd HH:mm:ss → 19 位），不让整页渲染中断",
                clockFallback.Length == 19 && clockFallback[4] == '-' && clockFallback[10] == ' ',
                clockFallback);

            // --- 数值域：值 + 格式串 + 单位 → 只读显示串 ---
            var fieldElement = ElementRegistry.CreateElement("Hmi.IOField");
            var field = (IOFieldElement)ElementRegistry.CreateControl(fieldElement);

            Check("数值域：构造即填第一帧（三个输入的默认值与描述符一致，变更回调一个都不响，不显式算就是空框）",
                field.ValueText == "0", field.ValueText ?? "null");

            fieldElement.SetProperty("Value", "23.5");
            fieldElement.SetProperty("ValueFormat", "0.0");
            fieldElement.SetProperty("Unit", "℃");
            field.Refresh();
            Check("数值域：值 23.5 / 格式 0.0 / 单位 ℃ → 「23.5 ℃」（数值 → 显示串只写一遍）",
                field.ValueText == "23.5 ℃", field.ValueText ?? "null");

            fieldElement.SetProperty("Unit", string.Empty);
            field.Refresh();
            Check("数值域：单位留空就只显数值（\"留空\"是自然结果，不是特判）",
                field.ValueText == "23.5", field.ValueText ?? "null");

            fieldElement.SetProperty("ValueFormat", "#,##0");
            fieldElement.SetProperty("Value", "1204");
            field.Refresh();
            Check("数值域：千分位格式串按不变文化排版（德语系统上不会变成 1.204）",
                field.ValueText == "1,204", field.ValueText ?? "null");

            fieldElement.SetProperty("ValueFormat", "Z");
            field.Refresh();
            Check("数值域：格式串写错退回通用格式，不让整页渲染中断",
                field.ValueText == "1204", field.ValueText ?? "null");

            // --- 表盘：值 → 比例 → 角度；三个几何量随尺寸重算（与棒图同族，比例落在角度上）---
            //
            // 与棒图同理：几何量取决于"控件此刻有多大"，必须先把布局跑完再 Refresh，
            // 否则 ActualWidth 是 0、几何量全空，断言会退化成"两个 null 相等"的伪通过。
            //
            // 这里刻意排成 200×120 的<b>非正方形</b>：盘面必须是内接圆（RadiusX == RadiusY）。
            // 若模板里用 Ellipse 形状元素拼，盘面会被拉成椭圆，而刻度线的端点仍按圆周算，
            // 两者当场错位——这正是"几何全部算在代码里"这条决策要挡住的那种画法。
            var gaugeElement = ElementRegistry.CreateElement("Hmi.Gauge");
            var gauge = (GaugeElement)ElementRegistry.CreateControl(gaugeElement);
            gaugeElement.Width = 200;
            gaugeElement.Height = 120;

            // 第一刷：把模型上的 200×120 落到控件的 Width/Height。此刻布局还没跑，ActualWidth 是 0，
            // 控件刻意"不猜尺寸"——几何量留空，而不是拿一个待会儿会跳一下的猜测值画出来。
            gauge.Refresh();
            Check("表盘：布局还没跑时几何量留空（宁可这一帧不画，也不拿猜测的尺寸画一个会跳的表盘）",
                gauge.FaceGeometry == null && gauge.ScaleGeometry == null && gauge.NeedleGeometry == null
                && Math.Abs(gauge.NeedleAngle - 135) < 0.001,
                $"盘面={(gauge.FaceGeometry == null ? "空" : "有")}，角度={gauge.NeedleAngle:0.##}");

            gauge.Measure(new Size(200, 120));
            gauge.Arrange(new Rect(0, 0, 200, 120));

            // 第二刷：布局已定，几何按真实尺寸重算（与棒图"先布局再 Refresh"同一条纪律）。
            gauge.Refresh();

            Check("表盘：构造即算第一帧（默认值 0 → 指针停在起始角 135°，读数是 \"0\"）",
                Math.Abs(gauge.NeedleAngle - 135) < 0.001 && gauge.ValueText == "0",
                $"角度={gauge.NeedleAngle:0.##}，读数={gauge.ValueText ?? "null"}");

            var face = gauge.FaceGeometry as EllipseGeometry;
            Check("表盘：盘面是内接圆而不是被拉伸的椭圆（200×120 的控件里 RadiusX == RadiusY，且按短边内缩半线宽）",
                face != null && Math.Abs(face.RadiusX - face.RadiusY) < 0.001
                && Math.Abs(face.RadiusX - (120 / 2d - (gauge.StrokeThickness / 2 + 1))) < 0.001,
                face == null ? "盘面几何为空" : $"半径 {face.RadiusX:0.##} × {face.RadiusY:0.##}");

            var scaleGroup = gauge.ScaleGeometry as GeometryGroup;
            Check("表盘：量程弧 + 刻度线（默认 5 格 → 6 根刻度线，加 1 段弧 = 7 个几何子项）",
                scaleGroup != null && scaleGroup.Children.Count == 7,
                scaleGroup == null ? "刻度几何为空" : $"{scaleGroup.Children.Count} 个子项");

            Check("表盘：指针几何随布局算出来（一根线 + 中心一个轴环，线宽随表盘缩放）",
                gauge.NeedleGeometry is GeometryGroup { Children.Count: 2 } && gauge.NeedleThickness > 0,
                $"线宽={gauge.NeedleThickness:0.##}");

            gaugeElement.SetProperty("Value", "50");
            gauge.Refresh();
            Check("表盘：值 50 / 量程 0~100 → 指针落在量程弧正中（135 + 270 × 0.5 = 270°）",
                Math.Abs(gauge.Ratio() - 0.5) < 0.0001 && Math.Abs(gauge.NeedleAngle - 270) < 0.001,
                $"比例={gauge.Ratio():0.###}，角度={gauge.NeedleAngle:0.##}");

            gaugeElement.SetProperty("Value", "250");
            gauge.Refresh();
            Check("表盘：值超上限按满量程收敛（指针停在扫过角末端 405°，不绕出量程弧）",
                Math.Abs(gauge.NeedleAngle - 405) < 0.001, $"角度={gauge.NeedleAngle:0.##}");

            gaugeElement.SetProperty("Value", "-250");
            gauge.Refresh();
            Check("表盘：值低于下限停在起始角（指针不会倒着转出量程弧）",
                Math.Abs(gauge.NeedleAngle - 135) < 0.001, $"角度={gauge.NeedleAngle:0.##}");

            gaugeElement.SetProperty("Value", "50");
            gaugeElement.SetProperty("Minimum", "100");
            gaugeElement.SetProperty("Maximum", "0");
            gauge.Refresh();
            Check("表盘：量程配错（下限 ≥ 上限）指针停在起始角，而不是让 NaN 把整页渲染打断",
                gauge.Ratio() == 0 && !double.IsNaN(gauge.NeedleAngle)
                && Math.Abs(gauge.NeedleAngle - 135) < 0.001,
                $"比例={gauge.Ratio()}，角度={gauge.NeedleAngle}");

            gaugeElement.SetProperty("Minimum", "0");
            gaugeElement.SetProperty("Maximum", "100");
            gaugeElement.SetProperty("ValueFormat", "Z");
            gaugeElement.SetProperty("Unit", "MPa");
            gauge.Refresh();
            Check("表盘：格式串写错退回通用格式，单位照拼（50 MPa），不让整页渲染中断",
                gauge.ValueText == "50 MPa", gauge.ValueText ?? "null");

            gaugeElement.SetProperty("SweepAngle", "720");
            gauge.Refresh();
            var fullCircle = gauge.ScaleGeometry as GeometryGroup;
            Check("表盘：扫过角超出 ±360 收敛成整圈（一段 ArcSegment 画不出 360°，改画圆环几何）",
                fullCircle != null && fullCircle.Children.Count == 7 && fullCircle.Children[0] is EllipseGeometry,
                fullCircle == null
                    ? "刻度几何为空"
                    : $"{fullCircle.Children.Count} 个子项，首项 {fullCircle.Children[0].GetType().Name}");

            gaugeElement.SetProperty("SweepAngle", "270");
            gaugeElement.SetProperty("TickDivisions", "0");
            gauge.Refresh();
            var noTicks = gauge.ScaleGeometry as GeometryGroup;
            Check("表盘：刻度格数 0 → 只留量程弧（弧 1 项，不画刻度线）",
                noTicks != null && noTicks.Children.Count == 1,
                noTicks == null ? "刻度几何为空" : $"{noTicks.Children.Count} 个子项");

            // --- 阀门：开度 → 状态色 + 裁剪宽度；阀体几何随尺寸重算（与表盘同族，比例落在裁剪上）---
            //
            // 与表盘同理：裁剪宽度取决于"控件此刻有多大"，必须先把布局跑完再 Refresh。
            // 阀体是"两个尖角相对的三角形"（蝴蝶结），填充与阀体共用同一份几何、靠一个矩形裁出
            // "开了多少"，所以这里两头都要钉：几何有几个子项、裁剪矩形有多宽。
            var valveElement = ElementRegistry.CreateElement("Hmi.Valve");
            var valve = (ValveElement)ElementRegistry.CreateControl(valveElement);
            valveElement.Width = 200;
            valveElement.Height = 120;

            // 第一刷：把模型上的 200×120 落到控件。布局还没跑，ActualWidth 是 0 ——
            // 几何量留空，但"状态色 / 开度文字"与尺寸无关，构造时就已算好（断言环境不跑布局也读得到）。
            valve.Refresh();
            Check("阀门：布局还没跑时几何量留空，但状态色与开度文字已算出来（与尺寸无关的推导量不等布局）",
                valve.ValveGeometry == null && valve.FillClipGeometry == null
                && valve.State == ValveState.Closed && valve.OpeningText == "0%"
                && valve.ValveBrush is SolidColorBrush { Color: var closedColor }
                && closedColor == Color.FromRgb(0x3A, 0x3A, 0x3A),
                $"状态={valve.State}，开度={valve.OpeningText ?? "null"}");

            valve.Measure(new Size(200, 120));
            valve.Arrange(new Rect(0, 0, 200, 120));
            valve.Refresh();

            var bowtie = valve.ValveGeometry as GeometryGroup;
            Check("阀门：阀体是两个尖角相对的三角形（蝴蝶结；2 个子项，各 1 个闭合图形）",
                bowtie != null && bowtie.Children.Count == 2
                && bowtie.Children.All(c => c is PathGeometry { Figures.Count: 1 } figure && figure.Figures[0].IsClosed),
                bowtie == null ? "阀体几何为空" : $"{bowtie.Children.Count} 个子项");

            // 与控件同一条账：外沿落在标注尺寸上 → 可画区 = 控件尺寸 − 2 ×（半线宽 + 1）
            double valveInset = valve.StrokeThickness / 2 + 1;
            Check("阀门：全关时裁剪矩形宽度为 0（0 宽矩形合法，正好等于「一点都没开」，不必为全关单开分支）",
                valve.FillClipGeometry is RectangleGeometry { Rect: var shutRect }
                && Math.Abs(shutRect.Width) < 0.001
                && Math.Abs(shutRect.X - valveInset) < 0.001
                && Math.Abs(shutRect.Height - (120 - 2 * valveInset)) < 0.001,
                valve.FillClipGeometry is RectangleGeometry { Rect: var r } ? $"裁剪 {r.Width:0.##}×{r.Height:0.##}" : "裁剪几何为空");

            valveElement.SetProperty("Opening", "50");
            valve.Refresh();
            Check("阀门：开度 50 → 中间位（蓝）+ 裁剪推进到一半（可画宽 197 × 0.5 = 98.5）",
                Math.Abs(valve.Ratio() - 0.5) < 0.0001 && valve.State == ValveState.Throttled
                && valve.OpeningText == "50%"
                && valve.ValveBrush is SolidColorBrush { Color: var halfColor }
                && halfColor == Color.FromRgb(0x2F, 0x80, 0xED)
                && valve.FillClipGeometry is RectangleGeometry { Rect: var halfRect }
                && Math.Abs(halfRect.Width - (200 - 2 * valveInset) * 0.5) < 0.001,
                $"比例={valve.Ratio():0.###}，开度={valve.OpeningText ?? "null"}");

            valveElement.SetProperty("Opening", "100");
            valve.Refresh();
            Check("阀门：开度 100 → 全开（绿）+ 裁剪铺满整个可画宽度（比例 1，一点不剩）",
                valve.State == ValveState.Open && valve.OpeningText == "100%"
                && valve.ValveBrush is SolidColorBrush { Color: var openColor }
                && openColor == Color.FromRgb(0x34, 0xC7, 0x59)
                && valve.FillClipGeometry is RectangleGeometry { Rect: var fullRect }
                && Math.Abs(fullRect.Width - (200 - 2 * valveInset)) < 0.001,
                $"状态={valve.State}，开度={valve.OpeningText ?? "null"}");

            valveElement.SetProperty("Opening", "150");
            valve.Refresh();
            Check("阀门：开度超 100 按全开收敛（裁剪不会溢出阀体，比例封顶在 1）",
                Math.Abs(valve.Ratio() - 1) < 0.0001 && valve.State == ValveState.Open,
                $"比例={valve.Ratio():0.###}");

            valveElement.SetProperty("Opening", "-20");
            valve.Refresh();
            Check("阀门：开度负值按全关收敛（裁剪宽度归 0，不会出现反向的填充）",
                valve.Ratio() == 0 && valve.State == ValveState.Closed
                && valve.FillClipGeometry is RectangleGeometry { Rect: var negRect }
                && Math.Abs(negRect.Width) < 0.001,
                $"比例={valve.Ratio()}，状态={valve.State}");

            valveElement.SetProperty("Opening", "50");
            valveElement.SetProperty("OpeningFormat", "Z");
            valve.Refresh();
            Check("阀门：格式串写错退回通用格式，百分号照拼（50%），不让整页渲染中断",
                valve.OpeningText == "50%", valve.OpeningText ?? "null");

            valveElement.SetProperty("OpeningFormat", "0.0");
            valve.Refresh();
            Check("阀门：格式串生效（0.0 → 「50.0%」），数值 → 显示串只写一遍",
                valve.OpeningText == "50.0%", valve.OpeningText ?? "null");

            // 坏值（NaN）只从直接写依赖属性这条路进得来（运行态的转换链会先把 NaN 挡掉），
            // 但它进来之后也必须收敛：状态、比例、裁剪宽度都当全关，不让 NaN 顺着几何传到渲染层。
            //
            // 这一条刻意<b>不</b>再 Refresh：Refresh 会把模型上的 50 原样盖回来（模型才是权威，
            // 与 [Z] 段「Stop 后控件回到设计值」同一条纪律），那就等于什么都没测。
            // 写 DP 时 OnValveInputChanged 已经把推导量重算过一遍，这里直接读即可。
            valve.SetValue(ValveElement.OpeningProperty, double.NaN);
            Check("阀门：开度是 NaN 时按全关收敛（坏值不该顺着几何传到渲染层）",
                valve.Ratio() == 0 && valve.State == ValveState.Closed
                && valve.FillClipGeometry is RectangleGeometry { Rect: var nanRect }
                && Math.Abs(nanRect.Width) < 0.001,
                $"比例={valve.Ratio()}，状态={valve.State}");

            // --- 注册表造 vs 直接 new：不许出现第二条初始化路径 ---
            var direct = new RectangleElement { Element = ElementRegistry.CreateElement("Hmi.Rectangle", 7, 8) };
            Check("直接 new 的控件与注册表造的控件行为一致（只有一条刷新路径，两种造法结果相同）",
                direct.Fill is SolidColorBrush { Color: var directFill } && directFill == Colors.White
                && Canvas.GetLeft(direct) == 7 && Canvas.GetTop(direct) == 8
                && direct.Width == 120 && direct.Height == 60, "");
        }

        // ==================================================================
        //  [T] 工具箱 → 画布的拖放链路：负载协议、落点换算、放置语义（在独立 STA 线程内执行）
        // ==================================================================
        private static void DragDropChecks()
        {
            Section("[T] 图元拖放：负载协议 / 落点换算 / 放置语义");

            Exception? failure = null;

            // 与 [S] 同一套样板：本节要 new ScadaCanvas（WPF 控件），必须站在 STA 上；
            // 不去把 Main 标成 STAThread，免得改变 S0/S1 那批断言的既有运行环境。
            var thread = new Thread(() =>
            {
                try { RunDragDropChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("拖放断言全程未抛异常", false, failure.ToString());
        }

        private static void RunDragDropChecks()
        {
            // ---------------- ① 拖放协议：只认自己那一种格式 ----------------

            var payload = ScadaDrag.CreatePayload("Hmi.Rectangle");
            Check("负载装载/取回往返：类型键原样回来",
                ScadaDrag.TryGetTypeKey(payload, out string roundTrip) && roundTrip == "Hmi.Rectangle",
                roundTrip);

            bool blankThrew = false;
            try { ScadaDrag.CreatePayload("   "); }
            catch (ArgumentException) { blankThrew = true; }
            Check("空类型键不让装进负载（放下去只会造出一个无名图元）", blankThrew, "");

            Check("null 负载与空负载一律不认",
                !ScadaDrag.TryGetTypeKey(null, out _) && !ScadaDrag.TryGetTypeKey(new DataObject(), out _), "");

            var foreign = new DataObject();
            foreign.SetData(DataFormats.Text, "Hmi.Rectangle");
            Check("外部纯文本不做兜底解析（从记事本拖几个字进画布不该长出图元）",
                !ScadaDrag.TryGetTypeKey(foreign, out _), "");

            var whitespaceValue = new DataObject();
            whitespaceValue.SetData(ScadaDrag.ElementTypeKeyFormat, "  ");
            Check("格式名对但值是空白，也算不是我们的负载",
                !ScadaDrag.TryGetTypeKey(whitespaceValue, out _), "");

            // ---------------- ② 落点换算：中心、吸附、夹进画面、缩放平移 ----------------

            var canvas = new ScadaCanvas
            {
                PageWidth = 800,
                PageHeight = 600,
                GridSize = 10,
                SnapToGrid = true,
                Zoom = 1,
                Offset = new Point(0, 0),
            };

            Point centerDrop = canvas.ToDropOrigin(new Point(500, 300), 120, 60);
            Check("落点是图元中心而不是左上角（用户指着\"这儿\"，要图元出现在这儿）",
                centerDrop.X == 440 && centerDrop.Y == 270, centerDrop.ToString());

            Point jitter = canvas.ToDropOrigin(new Point(503, 296), 120, 60);
            Check("差几像素仍落到同一格（吸附复用拖动那条 GridStep 规则，不会\"拖过去压线、放下去差半格\"）",
                jitter == centerDrop, jitter.ToString());

            canvas.SnapToGrid = false;
            Point free = canvas.ToDropOrigin(new Point(503, 296), 120, 60);
            Check("关掉吸附后按原始坐标放（允许对齐到非整格处）",
                free.X == 443 && free.Y == 266, free.ToString());
            canvas.SnapToGrid = true;

            canvas.GridSize = 0.5;
            Point noGrid = canvas.ToDropOrigin(new Point(503, 296), 120, 60);
            Check("网格间距小于 1 视为\"没有网格\"：吸附自动失效，而不是把落点挤成 0",
                noGrid.X == 443 && noGrid.Y == 266, noGrid.ToString());
            canvas.GridSize = 10;

            canvas.Zoom = 2;
            canvas.Offset = new Point(40, 20);
            Point zoomed = canvas.ToDropOrigin(new Point(1000, 600), 120, 60);
            Check("缩放/平移后的视口点先折算回设计坐标再算落点（适应窗口后拖放不会偏半屏）",
                zoomed.X == 420 && zoomed.Y == 260, zoomed.ToString());

            Check("视口↔设计坐标严格互逆（同一约定：设计 ×Zoom +Offset = 视口）",
                canvas.ToViewportPoint(new Point(123, 45)) == new Point(286, 110)
                && canvas.ToModelPoint(canvas.ToViewportPoint(new Point(123, 45))) == new Point(123, 45), "");

            Point outside = canvas.ToDropOrigin(new Point(4000, 4000), 120, 60);
            Check("在画面外的灰边上松手：中心被夹进画面（图元不会生在看不见的地方）",
                outside.X + 60 <= 800 && outside.Y + 30 <= 600, outside.ToString());

            Point edge = canvas.ToDropOrigin(new Point(-2000, -2000), 120, 60);
            Check("左上角外松手同样被夹回：中心贴到画面边缘（左上角允许为负，居中优先）",
                Math.Abs(edge.X + 60) < 1e-9 && Math.Abs(edge.Y + 30) < 1e-9, edge.ToString());

            var unpinnedCanvas = new ScadaCanvas { PageWidth = 0, PageHeight = 0, SnapToGrid = false };
            Point unpinned = unpinnedCanvas.ToDropOrigin(new Point(3000, 3000), 120, 60);
            Check("画面尺寸还没绑上（0）时不夹取，只原样换算（否则落点会全堆到左上角）",
                unpinned.X == 2940 && unpinned.Y == 2970, unpinned.ToString());

            canvas.Zoom = double.NaN;
            Check("缩放写入 NaN 回落 100%（矩阵一旦 NaN，整棵子树会无声消失）",
                canvas.Zoom == 1, canvas.Zoom.ToString());
            canvas.Zoom = 99;
            Check("缩放被夹进上界 8 倍（下界 0.1）", canvas.Zoom == 8, canvas.Zoom.ToString());

            // ---------------- ③ 放置：编辑器视图模型造模型，画布自己长控件 ----------------

            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            var solution = new SolutionModel();
            solution.Flows.Clear();
            workspace.SwitchSolution(solution);

            // 运行态宿主传 null：本节只验画面管理与选中通知，"▶ 运行画面"不参与，
            // 置灰由 CanRunPage 的 null 判兜住（生产路径由容器注入，见 App.xaml.cs）
            var editor = new ScadaEditorVM(workspace, null!);

            int selectedRaised = 0;
            editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScadaEditorVM.SelectedElement)) selectedRaised++;
            };

            editor.AddPageCommand.Execute();
            var page = editor.SelectedPage;
            Check("新建画面立刻成为编辑目标（点完\"新建\"还看见旧画面会被以为没生效）", page != null, "null");

            var host = new ScadaCanvas
            {
                PageWidth = page!.Width,
                PageHeight = page.Height,
                GridSize = page.GridSize,
                SnapToGrid = page.SnapToGrid,
                ItemsSource = page.Elements,
            };
            // 断言环境里没有窗口，WPF 那条"DefaultStyleKey → 主题字典"的自动查找链走不通
            // （实测：即便先 new 一个 Application、再量一次布局，Style 依旧是 null）。
            // 所以这里显式把本库自己的样式套上去——读的仍是 Generic.xaml 那份内容，
            // 被测的是"命名件齐不齐、图元控件是不是画布自己长的"，与样式查找机制无关。
            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };
            host.Style = (Style)theme[typeof(ScadaCanvas)];

            // 先 ApplyTemplate 把命名件生成出来，再量一次布局。
            // 顺序反过来会得到一个"已经没模板可套"的 false：Measure 里 WPF 就把模板应用掉了，
            // 之后 ApplyTemplate() 只是报告"这次没有新套用任何东西"。
            bool templated = host.ApplyTemplate();

            host.Measure(new Size(host.PageWidth, host.PageHeight));
            host.Arrange(new Rect(0, 0, host.PageWidth, host.PageHeight));

            Check("画布套上模板、PART_ElementLayer 等命名件齐备", templated, $"ApplyTemplate={templated}");

            var placed = editor.AddElement("Hmi.Rectangle", new Point(300, 200));
            Check("放置成功：进的是模型集合，返回的就是那个图元",
                placed != null && page.Elements.Count == 1 && ReferenceEquals(page.Elements[0], placed),
                $"集合 {page.Elements.Count} 个");
            Check("放置坐标=画布换算好的设计坐标（视图模型不再二次加工落点）",
                placed!.X == 300 && placed.Y == 200, $"({placed.X},{placed.Y})");
            Check("尺寸取描述符默认值（拖放只传类型键，宽高由注册表补）",
                placed.Width == 120 && placed.Height == 60, $"{placed.Width}×{placed.Height}");
            Check("名字取显示名", placed.Name == "矩形", placed.Name);
            Check("首个图元 ZIndex=1（画面底图常见 ZIndex=0，不抬序就生在它下面）",
                placed.ZIndex == 1, placed.ZIndex.ToString());
            Check("放完即选中（选中框是\"确实放下了\"最直接的反馈）",
                ReferenceEquals(editor.SelectedElement, placed) && selectedRaised == 1,
                $"通知 {selectedRaised} 次");
            Check("画布按集合变更自己长控件（宿主没有手工 addChild，不会出现第二个孤儿控件）",
                templated && host.RenderedElementCount == page.Elements.Count,
                $"渲染 {host.RenderedElementCount} / 数据 {page.Elements.Count}");

            var second = editor.AddElement("Hmi.Rectangle", new Point(0, 0));
            var third = editor.AddElement("Hmi.Rectangle", new Point(0, 0));
            Check("同画面重名自动续号（矩形 / 矩形_2 / 矩形_3）",
                second!.Name == "矩形_2" && third!.Name == "矩形_3",
                string.Join("、", page.Elements.Select(e => e.Name)));
            Check("后放的排在上面（ZIndex 递增，不会被先放的盖住）",
                placed.ZIndex == 1 && second.ZIndex == 2 && third.ZIndex == 3,
                string.Join("、", page.Elements.Select(e => e.ZIndex)));
            Check("连续放置时渲染层逐条跟上", templated && host.RenderedElementCount == 3,
                host.RenderedElementCount.ToString());

            page.Elements.Remove(second!);
            Check("删掉一个图元，元素层同步收掉对应控件", templated && host.RenderedElementCount == 2,
                host.RenderedElementCount.ToString());

            page.Elements.Clear();
            Check("清空画面走 Reset 全量重建，元素层不留残余", templated && host.RenderedElementCount == 0,
                host.RenderedElementCount.ToString());

            bool unknownThrew = false;
            ScadaElement? unknown = null;
            try { unknown = editor.AddElement("Hmi.NoSuch", new Point(0, 0)); }
            catch (Exception) { unknownThrew = true; }
            Check("未注册类型键：静默返回 null 而不抛（松手时崩掉编辑器不可接受）",
                !unknownThrew && unknown == null, unknownThrew ? "抛异常" : "null");

            editor.AddPageCommand.Execute();
            Check("换个画面同名不再续号（唯一性只在画面内，跨页重名无害）",
                editor.AddElement("Hmi.Rectangle", new Point(0, 0))!.Name == "矩形",
                editor.SelectedPage!.Elements[0].Name);

            var keepPage = editor.SelectedPage;
            editor.SelectedPage = null;
            Check("没有可写画面时放置失败但不崩（返回 null）",
                editor.AddElement("Hmi.Rectangle", new Point(0, 0)) == null, "");
            editor.SelectedPage = keepPage;

            // ---------------- ④ 换方案后的重对齐与可逆挂摘 ----------------

            var otherSolution = new SolutionModel();
            otherSolution.Flows.Clear();
            var otherPage = otherSolution.Scada.AddPage("另一方案的画面");

            int pagesRaised = 0;
            editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScadaEditorVM.Pages)) pagesRaised++;
            };

            workspace.SwitchSolution(otherSolution);
            Check("换方案后编辑目标自动跟到新文档（盯的是 WorkspaceContext.CurrentSolution）",
                ReferenceEquals(editor.SelectedPage, otherPage) && pagesRaised == 1,
                $"Pages 通知 {pagesRaised} 次");

            editor.Deactivate();
            workspace.SwitchSolution(solution);
            Check("Deactivate 后不再响应方案切换（离树面板不会被偷改，也不会漏摘）",
                !ReferenceEquals(editor.SelectedPage, solution.Scada.Pages[0]),
                editor.SelectedPage?.Name ?? "null");

            editor.Activate();
            Check("重新 Activate 后又跟上新文档（挂摘可逆，AvalonDock 反复装卸不失效）",
                ReferenceEquals(editor.SelectedPage, solution.Scada.Pages[0]),
                editor.SelectedPage?.Name ?? "null");

            // ---------------- ⑤ 工具箱视图模型：只读注册表 ----------------

            var toolbox = new ScadaToolboxVM();
            int allCount = ElementRegistry.All.Count;

            Check("工具箱条目总数 = 注册表全量（注册即出现在面板上，不必改这里一行）",
                toolbox.TotalCount == allCount
                && toolbox.Groups.Sum(g => g.Items.Count) == allCount,
                $"共 {toolbox.TotalCount} 种");

            Check("分组覆盖注册表里的每个分类，且分类先后沿用注册表次序",
                toolbox.Groups.Select(g => g.Name)
                    .SequenceEqual(ElementRegistry.All.Select(d => d.Category)
                        .GroupBy(c => c).Select(g => g.Key)),
                string.Join("、", toolbox.Groups.Select(g => g.Name)));

            Check("每个分组非空且组内分类一致",
                toolbox.Groups.All(g => g.Items.Count > 0 && g.Items.All(i => i.Category == g.Name)), "");

            int groupsRaised = 0;
            toolbox.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScadaToolboxVM.Groups)) groupsRaised++;
            };

            static int Shown(ScadaToolboxVM vm) => vm.Groups.Sum(g => g.Items.Count);

            toolbox.SearchText = "矩形";
            Check("搜索命中显示名", Shown(toolbox) == 1
                && toolbox.Groups.SelectMany(g => g.Items).Single().TypeKey == "Hmi.Rectangle",
                $"命中 {Shown(toolbox)}");

            toolbox.SearchText = "hmi.ind";
            Check("搜索命中类型键（大小写不敏感，中英混输也能找到）", Shown(toolbox) == 1, $"命中 {Shown(toolbox)}");

            toolbox.SearchText = "操作";
            Check("搜索命中分类（\"操作\"出来的是这一类下全部图元）", Shown(toolbox) == 1, $"命中 {Shown(toolbox)}");

            toolbox.SearchText = "罐体";
            Check("搜索命中说明文字（用户记得的是\"罐体\"而不是\"椭圆\"）", Shown(toolbox) == 1, $"命中 {Shown(toolbox)}");

            toolbox.SearchText = "绝对没有这个东西";
            Check("无命中时列表为空（不硬塞一个默认项骗眼睛）", Shown(toolbox) == 0, $"命中 {Shown(toolbox)}");

            toolbox.SearchText = "   ";
            Check("纯空白关键字视为不过滤（否则删空搜索框的瞬间面板是空的）", Shown(toolbox) == allCount, $"命中 {Shown(toolbox)}");

            toolbox.SearchText = string.Empty;
            // 上面一共赋了 7 次不同的关键字，每次都该有一次 Groups 通知；
            // 少一次就是"搜索框改了但列表没跟着变"，多一次就是白刷界面。
            Check("清空搜索后恢复全量，且这 7 次改关键字都通知了 Groups（界面绑定靠它刷新）",
                Shown(toolbox) == allCount && groupsRaised == 7, $"通知 {groupsRaised} 次");
        }

        // ==================================================================
        //  [U] 属性面板：行只从描述符来、写回只走 ElementValueAccess、外部变更逐行回读
        // ==================================================================

        private static void PropertyPanelChecks()
        {
            Section("[U] 属性面板：描述符驱动的行生成 / 写回 / 回读");

            Exception? failure = null;

            // 面板行里带 Brush（色块预览），末了还要真造一个图元控件验证链路，
            // 与 [S]/[T] 同一套样板：单独开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunPropertyPanelChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("属性面板断言全程未抛异常", false, failure.ToString());
        }

        private static void RunPropertyPanelChecks()
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            var solution = new SolutionModel();
            solution.Flows.Clear();
            workspace.SwitchSolution(solution);

            var editor = new ScadaEditorVM(workspace, null!);
            editor.AddPageCommand.Execute();

            var notifier = new RecordingNotifier();
            var picker = new FakeVariablePicker();
            var panel = new ScadaPropertyVM(editor, notifier, picker);

            static int Total(ScadaPropertyVM vm) => vm.Groups.Sum(g => g.Rows.Count);

            static ScadaElement Named(string typeKey, string name)
            {
                var element = ElementRegistry.CreateElement(typeKey, 0, 0);
                element.Name = name;
                return element;
            }

            // 按键取行：图元行与画面行都继承自同一个基类，断言里用到的成员
            // （Value / IsValid / ErrorText / SwatchBrush / Kind）全在基类上，所以返回基类即可。
            ScadaPropertyRowBase Row(string key) => panel.Groups.SelectMany(g => g.Rows).First(r => r.Key == key);

            // ---------------- ① 行从哪来：只认描述符 ----------------

            var rect = Named("Hmi.Rectangle", "料箱");
            editor.SelectedElement = rect;

            var rectDescriptor = ElementRegistry.Find("Hmi.Rectangle")!;
            Check("选中矩形即长出属性行，行数与描述符声明数一一对应（面板不认识任何具体图元类型）",
                Total(panel) == rectDescriptor.Properties.Count + rectDescriptor.Events.Count
                && rectDescriptor.Properties.Count == 13 && rectDescriptor.Events.Count == 0,
                $"面板 {Total(panel)} 行 / 描述符 {rectDescriptor.Properties.Count}+{rectDescriptor.Events.Count} 条");

            Check("组序取自描述符里的首现次序：常规→位置与尺寸→外观→文字",
                string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/外观/文字",
                string.Join("/", panel.Groups.Select(g => g.Name)));

            Check("组内行数按声明落位：外观 4 条、文字 3 条",
                panel.Groups.Single(g => g.Name == "外观").Rows.Count == 4
                && panel.Groups.Single(g => g.Name == "文字").Rows.Count == 3
                && panel.Groups.Single(g => g.Name == "位置与尺寸").Rows.Count == 5,
                string.Join("/", panel.Groups.Select(g => $"{g.Name}:{g.Rows.Count}")));

            Check("标题条是「类型 · 名字」，一眼看得见在改谁",
                panel.HasElement && panel.HeaderText == "矩形 · 料箱", panel.HeaderText);

            rect.Name = "1#料箱";
            Check("图元改名（含从面板的名称行改）后标题跟着变，不是进面板时算死的一次性快照",
                panel.HeaderText == "矩形 · 1#料箱", panel.HeaderText);

            editor.SelectedElement = Named("Hmi.Ellipse", "罐体");
            Check("换选中即整份重建：椭圆 12 行 4 组，且写入目标已换成新图元",
                Total(panel) == 12 && panel.Groups.Count == 4 && panel.Element?.TypeKey == "Hmi.Ellipse",
                $"{Total(panel)} 行 / {panel.Groups.Count} 组");

            int rowMismatch = 0;
            int bindMismatch = 0;
            foreach (var descriptor in ElementRegistry.All)
            {
                editor.SelectedElement = Named(descriptor.TypeKey, "临时");

                // 行账本 = 属性行 + 事件行：声明了几个事件就多几行，一行不多一行不少
                if (Total(panel) != descriptor.Properties.Count + descriptor.Events.Count) rowMismatch++;

                // 可绑性只有描述符一个来源：按钮上的 ƒx 该不该亮，不由面板自己猜
                foreach (var row in panel.Groups.SelectMany(g => g.Rows))
                    if (panel.BindVariableCommand.CanExecute(row) != row.IsBindable) bindMismatch++;
            }

            Check("遍历全部已注册图元：每一类的行数都等于其描述符声明数（新增图元这里零改动这条成立）",
                rowMismatch == 0 && ElementRegistry.All.Count >= 5,
                $"不匹配 {rowMismatch} 类，共 {ElementRegistry.All.Count} 类");

            Check("ƒx 只对声明了 IsBindable 的行可用（不可绑属性不给按钮）",
                bindMismatch == 0, $"不匹配 {bindMismatch} 行");

            var startBtn = Named("Hmi.Button", "启动");
            editor.SelectedElement = startBtn;
            var buttonDescriptor = ElementRegistry.Find("Hmi.Button")!;
            Check("按钮：14 条属性 + 2 条事件 = 16 行 5 组，「事件」组收尾（组序是声明出来的，不是按字典序凑的）",
                Total(panel) == buttonDescriptor.Properties.Count + buttonDescriptor.Events.Count
                && buttonDescriptor.Events.Count == 2
                && string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/文字/外观/事件",
                $"{Total(panel)} 行 / " + string.Join("/", panel.Groups.Select(g => g.Name)));

            // ---------------- ①B 事件行：S5 组态事件在面板上的投影 ----------------
            //
            // 事件行的真值只有一个出处：EventHooks 集合里有没有那条钩子（Key=Event.{枚举值}，
            // Pressed=2 / Released=3）。设计期勾选/取消只动模型，绝不执行动作——
            // 执行是运行态会话的事，第一道闸门（IsRunning == false）在 [Y]④ 已验证。

            Check("事件行显示名与运行日志同一出处（面板勾的「按下」和日志里的「· 按下」对得上号）",
                Row("Event.2").DisplayName == ScadaEventType.Pressed.DisplayName()
                && Row("Event.3").DisplayName == ScadaEventType.Released.DisplayName(),
                $"{Row("Event.2").DisplayName} / {Row("Event.3").DisplayName}");

            Check("事件行是 Bool 投影：不给 ƒx（事件不绑变量），描述里说清「设计态点击不触发」",
                Row("Event.2").Kind == ElementPropertyKind.Bool
                && !Row("Event.2").IsBindable
                && (Row("Event.2").Description ?? "").Contains("设计态点击不触发"),
                $"{Row("Event.2").Kind} / ƒx={Row("Event.2").IsBindable}");

            Check("没勾过的事件行回读 False，图元上也没有钩子",
                Row("Event.2").Value == "False" && startBtn.FindEventHook(ScadaEventType.Pressed) == null,
                Row("Event.2").Value);

            Row("Event.2").Value = "True";
            var pressedHook = startBtn.FindEventHook(ScadaEventType.Pressed);
            Check("勾选「按下」：长出一条钩子并自动带一条「记录日志」（空动作表在执行侧等同没配），行回读 True",
                pressedHook != null && pressedHook.Actions.Count == 1
                && pressedHook.Actions[0].Type == ScadaActionType.Log
                && Row("Event.2").Value == "True",
                $"钩子={pressedHook != null} / 动作 {pressedHook?.Actions.Count ?? 0} 条 / 面板 {Row("Event.2").Value}");

            Row("Event.2").Value = "True";
            Check("重复勾选不再塞动作：值没变时 setter 短路，Commit 的幂等分支兜底，动作始终 1 条",
                startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions.Count == 1,
                $"{startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions.Count} 条");

            // 真实可达路径：用户在动作列表删光了动作觉得没配好，取消勾选重来。
            // 先动模型再动行——行的 _value 仍是 "True"，取消（Commit 摘整条钩子）后再勾
            // 才会真走进 Commit：此时没钩子，走「建新钩子 + 默认 Log」路径。
            startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions.Clear();
            Row("Event.2").Value = "False";
            Row("Event.2").Value = "True";
            Check("删光动作后取消再勾：走「没钩子建新钩子」路径，补回默认「记录日志」当起点",
                startBtn.FindEventHook(ScadaEventType.Pressed) != null
                && startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions.Count == 1
                && startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions[0].Type == ScadaActionType.Log,
                $"{startBtn.FindEventHook(ScadaEventType.Pressed)?.Actions.Count ?? -1} 条");

            Row("Event.2").Value = "False";
            Check("取消勾选：整条钩子连同动作一起摘掉，与画面级「加载事件」同一口径",
                startBtn.FindEventHook(ScadaEventType.Pressed) == null && Row("Event.2").Value == "False",
                $"{startBtn.FindEventHook(ScadaEventType.Pressed) == null} / {Row("Event.2").Value}");

            // ---------------- ①C 动作编辑：勾上之后在原地增 / 删 / 调序（S5 完善） ----------------
            //
            // 事件行是"复合编辑区"：勾选框之外还内联一张动作表。动作表直接双向绑到模型对象
            // （ScadaAction 本身带变更通知，没有"文本形态"这回事），所以这里断言的是
            // "命令把模型改对了"，而不是"行里另存了一份影子副本"。

            var eventRow = (ScadaEventRow)Row("Event.2");

            Check("事件行自述是复合编辑区、普通属性行不是：面板据此整份换模板，XAML 不必认得 ScadaEventRow 这个类型",
                eventRow.IsCompositeEditor && !Row("$X").IsCompositeEditor,
                $"事件行 {eventRow.IsCompositeEditor} / 属性行 {Row("$X").IsCompositeEditor}");

            Check("没勾的事件行没有动作表（null，不是空表）：「压根没配」和「配了但删光了」是两回事，模板据此整块收起动作区",
                eventRow.Actions == null && !eventRow.HasActions,
                $"Actions={(eventRow.Actions == null ? "null" : eventRow.Actions.Count.ToString())} / HasActions={eventRow.HasActions}");

            eventRow.Value = "True";
            Check("勾上后动作区露出，拿到的是模型里那张表本身（同一个对象，不是拷贝）：默认已带一条「记录日志」",
                eventRow.HasActions
                && ReferenceEquals(eventRow.Actions, startBtn.FindEventHook(ScadaEventType.Pressed)!.Actions)
                && eventRow.Actions!.Count == 1 && eventRow.Actions[0].Type == ScadaActionType.Log,
                $"HasActions={eventRow.HasActions} / {eventRow.Actions?.Count ?? -1} 条");

            Check("「添加动作」无参命令始终可点（看得见动作区就说明已勾上）",
                eventRow.AddActionCommand.CanExecute(), "");

            eventRow.AddActionCommand.Execute();
            Check("点「添加动作」：表变 2 条，新动作默认就是「记录日志」（先给一条能跑的，改类型再说）",
                eventRow.Actions!.Count == 2 && eventRow.Actions[1].Type == ScadaActionType.Log,
                $"{eventRow.Actions.Count} 条 / 新动作 {eventRow.Actions[1].Type}");

            // 顺序有语义：执行侧按集合顺序依次跑，所以"↓"不是排版，是改运行行为。
            var firstAction = eventRow.Actions![0];
            var secondAction = eventRow.Actions[1];

            Check("调序可用性看位置：第一条「↑」灰、「↓」亮；第二条反过来（边界不给越界按钮亮着）",
                !eventRow.MoveActionUpCommand.CanExecute(firstAction)
                && eventRow.MoveActionDownCommand.CanExecute(firstAction)
                && eventRow.MoveActionUpCommand.CanExecute(secondAction)
                && !eventRow.MoveActionDownCommand.CanExecute(secondAction),
                "");

            // 先把两条动作标上可区分的文案，才能证明"真的换了位"而不是"数量没变"
            firstAction.Text = "甲";
            secondAction.Text = "乙";
            eventRow.MoveActionDownCommand.Execute(firstAction);

            Check("点「↓」：两条动作真换位（不是「删了再插」留下的中间空档，集合始终 2 条）",
                eventRow.Actions!.Count == 2
                && eventRow.Actions[0].Text == "乙" && eventRow.Actions[1].Text == "甲",
                $"{eventRow.Actions[0].Text}/{eventRow.Actions[1].Text}");

            Check("换位后可用性跟着翻面：原来的第一条现在顶上还有「↑」、底下没「↓」了",
                eventRow.MoveActionUpCommand.CanExecute(firstAction)
                && !eventRow.MoveActionDownCommand.CanExecute(firstAction),
                "");

            var strayAction = new ScadaAction { Type = ScadaActionType.Log };
            Check("删命令的参数认对象不认序号：不在本表里的动作 CanExecute 为假（列表外的行点不动）",
                !eventRow.RemoveActionCommand.CanExecute(strayAction)
                && eventRow.RemoveActionCommand.CanExecute(eventRow.Actions![0]),
                "");

            eventRow.RemoveActionCommand.Execute(eventRow.Actions![0]);
            Check("点「✕」：那条动作从表里去掉，剩下那条原地不动（删的是选中的那条，不是末条）",
                eventRow.Actions!.Count == 1 && eventRow.Actions[0].Text == "甲",
                $"{eventRow.Actions.Count} 条 / 剩「{eventRow.Actions[0].Text}」");

            Check("类型下拉三项与枚举显示名同源：下拉里选的「写变量」和运行日志里的「写变量」是同一句话",
                eventRow.ActionTypeChoices.Count == 3
                && eventRow.ActionTypeChoices.Select(o => o.Type).Distinct().Count() == 3
                && eventRow.ActionTypeChoices.Select(o => o.DisplayName).SequenceEqual(
                    new[] { ScadaActionType.Log, ScadaActionType.WriteVariable, ScadaActionType.Navigate }
                        .Select(t => t.DisplayName())),
                string.Join("/", eventRow.ActionTypeChoices.Select(o => o.DisplayName)));

            Check("「还没接通」的说明由领域层一句话出，面板那行橙色提示与运行日志逐字相同（两处口径不许劈叉）；「写变量」已接通（S6-6）故不再有这句说明",
                new ScadaAction { Type = ScadaActionType.Log }.PendingReason == null
                && new ScadaAction { Type = ScadaActionType.WriteVariable }.PendingReason == null
                && new ScadaAction { Type = ScadaActionType.Navigate }.PendingReason == "切换画面要等画面导航（S8）接入"
                && new ScadaAction { Type = (ScadaActionType)99 }.PendingReason == "本版本不认识该动作",
                new ScadaAction { Type = ScadaActionType.Navigate }.PendingReason ?? "null");

            var typeProbe = new ScadaAction { Type = ScadaActionType.Log };
            var typeRaised = new List<string>();
            typeProbe.PropertyChanged += (_, e) => typeRaised.Add(e.PropertyName ?? "<null>");
            typeProbe.Type = ScadaActionType.Navigate;
            Check("改动作类型时 PendingReason 一并广播：橙色提示当场换字，不用等整块重建",
                typeRaised.Contains(nameof(ScadaAction.PendingReason))
                && typeProbe.PendingReason == "切换画面要等画面导航（S8）接入",
                string.Join("/", typeRaised));

            // ---------------- ①D 写变量的目标变量：面板只说"给我一个变量"（S6-6） ----------------
            //
            // 弹窗怎么弹、弹哪个由 IScadaVariablePicker 决定，面板与事件行不碰 IDialogService，
            // 所以这里给一个不弹窗的替身，"选中 → 回填"这条链能整条走完。

            var writeAction = eventRow.Actions![0];
            writeAction.Type = ScadaActionType.WriteVariable;

            Check("写变量动作一新建就是「还没选变量」：HasVariable 为假，面板据此亮橙色提示",
                !writeAction.HasVariable && writeAction.VariableId == Guid.Empty && writeAction.VariableName == null,
                $"{writeAction.VariableId} / {writeAction.VariableName ?? "null"}");

            var pickRaised = new List<string>();
            writeAction.PropertyChanged += (_, e) => pickRaised.Add(e.PropertyName ?? "<null>");

            var pickedId = Guid.NewGuid();
            picker.NextId = pickedId;
            picker.NextName = "启动";
            eventRow.PickVariableCommand.Execute(writeAction);

            Check("点「选择变量」：把这条动作的现状（Id 优先、名字兜底）交给选择器，选中后回填 Id 与名字",
                picker.PickCalls == 1
                && picker.LastCurrentId == Guid.Empty && picker.LastCurrentName == null
                && writeAction.VariableId == pickedId && writeAction.VariableName == "启动",
                $"{picker.PickCalls} 次 / {writeAction.VariableName}");

            Check("回填后 HasVariable 广播出去：面板上的占位文案与橙色提示当场收掉，不用等整块重建",
                pickRaised.Contains(nameof(ScadaAction.HasVariable)) && writeAction.HasVariable,
                string.Join("/", pickRaised));

            picker.NextId = Guid.Empty;   // 用户在弹窗里点了取消
            eventRow.PickVariableCommand.Execute(writeAction);
            Check("取消时回调一次都不触发：原绑定原样留着（取消不等于清空，破坏性动作不该藏在取消里）",
                writeAction.VariableId == pickedId && writeAction.VariableName == "启动",
                writeAction.VariableName ?? "null");

            Check("预选参数就是这条动作的现状：再点一次时交出去的是上次选的那个，弹窗才能停在原来那一行",
                picker.LastCurrentId == pickedId && picker.LastCurrentName == "启动",
                $"{picker.LastCurrentId} / {picker.LastCurrentName ?? "null"}");

            // RefreshValue 是面板唯一的回读入口：别处（脚本、反序列化、其它面板）改了模型，
            // 靠它把动作区状态与三条命令的可用性重新通知出去。只回读勾选框会漏掉整张动作表。
            var rowRaised = new List<string>();
            int canExecRaised = 0;
            void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                => rowRaised.Add(e.PropertyName ?? "<null>");
            eventRow.PropertyChanged += OnRowChanged;
            eventRow.RemoveActionCommand.CanExecuteChanged += (_, _) => canExecRaised++;
            eventRow.MoveActionUpCommand.CanExecuteChanged += (_, _) => canExecRaised++;
            eventRow.MoveActionDownCommand.CanExecuteChanged += (_, _) => canExecRaised++;

            eventRow.RefreshValue();

            eventRow.PropertyChanged -= OnRowChanged;
            Check("RefreshValue 把动作表状态与命令可用性一起通知出去（只通知勾选框值会漏掉动作区）",
                rowRaised.Contains(nameof(ScadaEventRow.Actions))
                && rowRaised.Contains(nameof(ScadaEventRow.HasActions))
                && canExecRaised >= 3,
                string.Join("/", rowRaised) + $" / 命令可用性通知 {canExecRaised} 次");

            eventRow.RemoveActionCommand.Execute(eventRow.Actions![0]);
            Check("删光最后一条动作不顺手摘钩子：空动作表在执行侧本来就等同没配，面板用提示说明比替用户做决定诚实",
                eventRow.Actions!.Count == 0 && eventRow.HasActions
                && startBtn.FindEventHook(ScadaEventType.Pressed) != null,
                $"{eventRow.Actions.Count} 条 / 钩子还在={startBtn.FindEventHook(ScadaEventType.Pressed) != null}");

            eventRow.Value = "False";
            Check("取消勾选把空动作表也一并收走：不留一张空表挂在图元上等着落盘",
                startBtn.FindEventHook(ScadaEventType.Pressed) == null
                && eventRow.Actions == null && !eventRow.HasActions,
                $"钩子={startBtn.FindEventHook(ScadaEventType.Pressed) != null} / Actions={(eventRow.Actions == null ? "null" : "有")}");

            editor.SelectedElement = Named("Hmi.Indicator", "运行指示");
            Check("指示灯：15 行 5 组，多出来的是一个状态组（点亮/点亮色/熄灭色）",
                Total(panel) == 15
                && string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/状态/外观/文字"
                && panel.Groups.Single(g => g.Name == "状态").Rows.Count == 3,
                string.Join("/", panel.Groups.Select(g => $"{g.Name}:{g.Rows.Count}")));

            // ---------------- ② 写回：合法值进模型，非法值就地标红 ----------------

            var box = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);
            box.Name = "写回试验台";
            box.Width = 44;
            editor.SelectedElement = box;

            Row("$Width").Value = "-30";
            Check("宽里打 -30：模型取绝对值存 30，输入框回填规范值（负宽进不了 .vms）",
                box.Width == 30 && Row("$Width").Value == "30" && Row("$Width").IsValid,
                $"模型 {box.Width} / 面板 {Row("$Width").Value}");

            Row("$X").Value = "abc";
            Check("X 里打字母：模型纹丝不动、用户文本原样留着、行标红并给出人话原因",
                box.X == 10 && Row("$X").Value == "abc" && !Row("$X").IsValid
                && Row("$X").ErrorText!.Contains("请输入数字"),
                Row("$X").ErrorText ?? "无提示");

            Row("$X").Value = "12.5";
            Check("改完就地接着用：小数量按不变文化往返，12.5 不会被念成 125",
                box.X == 12.5 && Row("$X").Value == "12.5", $"模型 {box.X} / 面板 {Row("$X").Value}");

            Row("$X").Value = string.Empty;
            Check("清空数字框：不写模型，回读把旧值放回输入框（图元不会因此跑到 0,0）",
                box.X == 12.5 && Row("$X").Value == "12.5" && Row("$X").IsValid,
                $"模型 {box.X} / 面板 {Row("$X").Value}");

            Row("$Rotation").Value = "400";
            Check("旋转打 400 越界：照样写进模型，只标红提示（静默夹回 360 会让人以为输入框吞字）",
                box.Rotation == 400 && !Row("$Rotation").IsValid
                && Row("$Rotation").ErrorText!.Contains("不能大于 360"),
                $"模型 {box.Rotation} / {Row("$Rotation").ErrorText}");

            Row("$Rotation").Value = "-90";
            Check("改回界内值红框自动消失：提示不是闸门，不会一直挂着",
                box.Rotation == -90 && Row("$Rotation").IsValid && Row("$Rotation").ErrorText == null,
                $"模型 {box.Rotation} / {(Row("$Rotation").ErrorText ?? "已清除")}");

            var label = Named("Hmi.Text", "工位名");
            editor.SelectedElement = label;
            Row("FontSize").Value = "3";
            Check("字号低于下界也是同一套口径：写入 + 标红「不能小于 6」",
                label.GetProperty("FontSize") == "3" && !Row("FontSize").IsValid
                && Row("FontSize").ErrorText!.Contains("不能小于 6"),
                Row("FontSize").ErrorText ?? "无提示");

            // 色块要看的是 Color 型行：文本图元没有填充色属性，切回矩形才有 Fill 这一行
            editor.SelectedElement = box;

            Row("Fill").Value = "#GGG";
            Check("颜色填了非法串：色块回落成灰色，不掀面板也不抛（转换器不认识就是异常）",
                Row("Fill").SwatchBrush is SolidColorBrush gray
                && gray.Color == Color.FromRgb(0xCC, 0xCC, 0xCC),
                (Row("Fill").SwatchBrush as SolidColorBrush)?.Color.ToString() ?? "null");

            Row("Fill").Value = "#FF00FF00";
            Check("颜色填对了：色块就是那个颜色，与画布上真实填出的颜色同一口径",
                Row("Fill").SwatchBrush is SolidColorBrush green && green.Color == Color.FromRgb(0, 0xFF, 0),
                box.GetProperty("Fill"));

            // ---------------- ③ 外部变更回读 / 空态 / 不认识的类型 ----------------

            editor.SelectedElement = box;
            box.X = 777;
            Check("画布拖动改了 X（模型侧写入）后，面板对应行立刻回读出 777——面板没有自己的第二份值",
                Row("$X").Value == "777" && Row("$X").IsValid, Row("$X").Value);

            Row("$Name").Value = "走面板改名";
            Check("名称行写的是模型的 Name，标题条同步跟上",
                box.Name == "走面板改名" && panel.HeaderText == "矩形 · 走面板改名", panel.HeaderText);

            editor.SelectedElement = null;
            Check("取消选中即转入画面模式：图元行一条不留，标题点名在编辑哪个画面（S3-e3 改的口径）",
                !panel.HasElement && panel.Page != null && panel.HeaderText == "画面 · 画面_1"
                && Total(panel) > 0 && panel.Groups.SelectMany(g => g.Rows).All(r => r is not ScadaPropertyRow),
                $"{Total(panel)} 行 / {panel.HeaderText}");

            var foreign = new ScadaElement { TypeKey = "Hmi.NotOnThisMachine", Name = "插件图元" };
            editor.SelectedElement = foreign;
            Check("类型没注册的图元：0 行 + 标题说明本机不认识它（不是属性丢了，是这台机器没装插件）",
                panel.Groups.Count == 0 && panel.HeaderText.Contains("本机未注册该图元"), panel.HeaderText);

            // ---------------- ④ 通知口径：改值不重建、换选中重建一次 ----------------

            editor.SelectedElement = box;
            int groupsRaised = 0;
            panel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScadaPropertyVM.Groups)) groupsRaised++;
            };

            Row("$Y").Value = "88";
            Row("Fill").Value = "#FFFF0000";
            Check("连改两行值不触发 Groups 通知（每敲一格就整表重建会把输入焦点顶掉）",
                groupsRaised == 0, $"通知 {groupsRaised} 次");

            editor.SelectedElement = Named("Hmi.Rectangle", "第二个矩形");
            Check("换选中恰好一次 Groups 通知（绑分组列表的界面就靠它刷新）",
                groupsRaised == 1, $"通知 {groupsRaised} 次");

            // ---------------- ⑤ 描述符是共享实例，编辑态绝不写回它 ----------------

            var fill = ElementRegistry.FindProperty("Hmi.Rectangle", "Fill")!;
            Check("改过值之后，描述符声明的默认值仍是原来那份（写回共享描述符会让下一个图元捡到上一个的颜色）",
                fill.DefaultValue == "#FFFFFFFF"
                && ElementRegistry.FindProperty("Hmi.Text", "FontSize")!.DefaultValue == "14",
                fill.DefaultValue);

            // ---------------- ⑥ Bool 与 Choice 型行：值换算放在行里 ----------------

            var lamp = Named("Hmi.Indicator", "1#电机");
            editor.SelectedElement = lamp;

            Check("指示灯的「点亮」行是 Bool 型，未配过时取描述符默认值 False",
                Row("IsOn").Kind == ElementPropertyKind.Bool && Row("IsOn").BoolValue == false,
                Row("IsOn").BoolValue?.ToString() ?? "null");

            Row("IsOn").BoolValue = true;
            Check("勾一下复选框：模型存的是字符串 True，回读仍是勾选态（复选框与属性袋之间只有这一处换算）",
                lamp.GetProperty("IsOn") == "True" && Row("IsOn").BoolValue == true,
                lamp.GetProperty("IsOn"));

            lamp.SetProperty("IsOn", "也许");
            Row("IsOn").RefreshValue();
            Check("手改过的 .vms 里出现非 True/False 的值：复选框呈未定态，不崩也不猜成 false",
                Row("IsOn").BoolValue == null && Row("IsOn").Value == "也许",
                Row("IsOn").BoolValue?.ToString() ?? "null");

            var shape = Row("Shape");
            Check("Choice 型行把候选原样交给下拉（面板不硬编码 Circle/Square）",
                shape.Kind == ElementPropertyKind.Choice
                && string.Join("|", shape.Choices) == "Circle|Square"
                && shape.Value == "Circle",
                string.Join("|", shape.Choices));

            shape.Value = "Square";
            Check("换下拉项即写模型：形状改成正方形",
                lamp.GetProperty("Shape") == "Square" && Row("Shape").Value == "Square",
                lamp.GetProperty("Shape"));

            // ---------------- ⑦ ƒx：选完变量即落模型（配置侧接通） ----------------
            //
            // 绑定的落点是"本画面里的图元"，所以被测图元必须真的在画面里：走工具箱放置那条路
            // （AddElement 顺带把它选中），而不是 Named() 那种只在内存里造个孤儿——
            // TrySetBinding 会以"图元不属于本画面"把后者拒掉，那是另一条断言要验的事。

            var boundPage = editor.SelectedPage!;
            var boundLamp = editor.AddElement("Hmi.Indicator", new Point(20, 20))!;
            var boundRow = (ScadaPropertyRow)Row("IsOn");

            Check("刚放下的图元没绑过：不显示变量带（绑没绑只问图元，面板不另存一份状态）",
                !boundRow.HasBinding && boundRow.BindingVariableName == null,
                $"{boundRow.HasBinding} / {boundRow.BindingVariableName ?? "null"}");

            // 点 ƒx → 弹窗 → 选中一个变量。假选择器把"选中"直接回调出去，真机上换的是弹窗实现。
            var speedId = Guid.NewGuid();
            picker.NextId = speedId;
            picker.NextName = "Speed";
            notifier.Messages.Clear();

            // 数一遍命令抛了几次 CanExecuteChanged。只看 CanExecute 的返回值是抓不到
            // "✕ 按钮灰着"这个缺陷的：谓词读的就是行状态，绑完自然是 true；坏的是
            // WPF 的 ButtonBase 只在收到这个事件时才重算 IsEnabledCore，不抛就停在
            // 模板实例化那一刻的初值 false 上（真机实锤：button "✕" (disabled)）。
            int clearStateRaises = 0;
            void CountClearState(object? _, EventArgs __) => clearStateRaises++;
            panel.ClearBindingCommand.CanExecuteChanged += CountClearState;

            panel.BindVariableCommand.Execute(boundRow);

            panel.ClearBindingCommand.CanExecuteChanged -= CountClearState;

            Check("绑上之后命令主动抛 CanExecuteChanged：✕ 才会从禁用翻成可用（不抛就停在初值上）",
                clearStateRaises > 0, $"抛了 {clearStateRaises} 次");

            Check("点 ƒx 选完变量：绑定真落到图元上（Id 是权威键、名字只作展示），画面长出一条",
                boundLamp.FindBinding("IsOn")?.VariableId == speedId
                && boundLamp.FindBinding("IsOn")?.VariableName == "Speed"
                && boundLamp.Bindings.Count == 1,
                $"{boundLamp.FindBinding("IsOn")?.VariableName ?? "null"} / {boundLamp.Bindings.Count} 条");
            Check("成功路径静默：不弹任何提示框（绑上了是预期结果，不需要弹窗道贺）",
                notifier.Messages.Count == 0, string.Join(" | ", notifier.Messages));
            Check("行自己回读绑定状态：变量带长出变量名、清除入口随之可用",
                boundRow.HasBinding && boundRow.BindingVariableName == "Speed"
                && panel.ClearBindingCommand.CanExecute(boundRow),
                $"{boundRow.BindingVariableName} / 可清除={panel.ClearBindingCommand.CanExecute(boundRow)}");

            // 换绑：用户已配的停用/格式串要保住——"换变量"不是"删了重配"
            var firstBinding = boundLamp.FindBinding("IsOn")!;
            firstBinding.DisplayFormat = "F2";
            firstBinding.IsEnabled = false;

            var torqueId = Guid.NewGuid();
            picker.NextId = torqueId;
            picker.NextName = "Torque";
            panel.BindVariableCommand.Execute(boundRow);

            Check("再点 ƒx 换个变量：复用同一条绑定（一个属性至多一条），旧变量被顶掉",
                boundLamp.Bindings.Count == 1
                && ReferenceEquals(boundLamp.FindBinding("IsOn"), firstBinding)
                && firstBinding.VariableId == torqueId && firstBinding.VariableName == "Torque",
                $"{boundLamp.Bindings.Count} 条 / {firstBinding.VariableName}");
            Check("换绑复用原条目：用户已配的停用与格式串一并保住（不是删了重配）",
                firstBinding.DisplayFormat == "F2" && !firstBinding.IsEnabled,
                $"{firstBinding.DisplayFormat} / 启用={firstBinding.IsEnabled}");
            Check("换绑时弹窗预选当前变量：打开就有现状（换绑是改一处，不是重新找一遍）",
                picker.LastCurrentId == speedId && picker.LastCurrentName == "Speed",
                $"{picker.LastCurrentName} / {picker.LastCurrentId}");

            // 取消 ≠ 清空：回调一次都不触发，原绑定原样留着
            picker.NextId = Guid.Empty;
            int picksBefore = picker.PickCalls;
            panel.BindVariableCommand.Execute(boundRow);
            Check("弹窗里点取消：原绑定原样留着（取消不是清空），只是白开了一次窗",
                picker.PickCalls == picksBefore + 1
                && boundLamp.FindBinding("IsOn")?.VariableId == torqueId,
                $"{picker.PickCalls} 次 / {boundLamp.FindBinding("IsOn")?.VariableName ?? "null"}");

            panel.ClearBindingCommand.Execute(boundRow);
            Check("点清除：绑定从图元上摘掉、变量带收起（清除入口只对确实绑了的行可用）",
                !boundRow.HasBinding && boundLamp.Bindings.Count == 0
                && !panel.ClearBindingCommand.CanExecute(boundRow),
                $"{boundLamp.Bindings.Count} 条 / 可清除={panel.ClearBindingCommand.CanExecute(boundRow)}");

            Check("null 参数与不可绑行（形状、字号）都不给 ƒx 亮着",
                !panel.BindVariableCommand.CanExecute(null!)
                && !panel.BindVariableCommand.CanExecute(Row("FontSize"))
                && panel.BindVariableCommand.CanExecute(Row("OnColor")), "");

            // ---------------- ⑦B 领域层写入口自己也得站得住（面板只是它的一个调用方） ----------------

            string bindErrorText = string.Empty;

            int versionBeforeAdd = boundPage.Version;
            Check("TrySetBinding：加一条真绑定 → 画面版本号涨起来（脏标记跟着走，不然改了没提示保存）",
                boundPage.TrySetBinding(boundLamp, "IsOn", speedId, "Speed", out bindErrorText)
                && boundPage.Version > versionBeforeAdd,
                $"v{versionBeforeAdd} → v{boundPage.Version} / {bindErrorText}");

            int versionBeforeRebind = boundPage.Version;
            Check("TrySetBinding 换变量：仍是同一条条目（不新增），版本号照样涨（绑定内容变了也是脏）",
                boundPage.TrySetBinding(boundLamp, "IsOn", torqueId, "Torque", out bindErrorText)
                && boundLamp.Bindings.Count == 1
                && boundPage.Version > versionBeforeRebind,
                $"{boundLamp.Bindings.Count} 条 / v{versionBeforeRebind} → v{boundPage.Version}");

            Check("TrySetBinding 拒绝空 Id：现场不该造出「只能按名找」的旧数据形态",
                !boundPage.TrySetBinding(boundLamp, "IsOn", Guid.Empty, "Speed", out bindErrorText)
                && bindErrorText.Contains("变量 Id 为空"),
                bindErrorText);
            Check("TrySetBinding 拒绝不属于本画面的图元（面板拿着别处的图元也绑不上）",
                !boundPage.TrySetBinding(Named("Hmi.Indicator", "别处的"), "IsOn", speedId, "Speed", out bindErrorText)
                && bindErrorText.Contains("图元不属于本画面"),
                bindErrorText);
            Check("TrySetBinding 拒绝空属性键（空格也不算键）",
                !boundPage.TrySetBinding(boundLamp, "   ", speedId, "Speed", out bindErrorText)
                && bindErrorText.Contains("属性键为空"),
                bindErrorText);

            Check("属性键比较大小写敏感：IsOn 与 ison 是两条不同的键（键是标识符，不是给人看的文本）",
                boundLamp.FindBinding("IsOn") != null && boundLamp.FindBinding("ison") == null, "");

            int versionBeforeRemove = boundPage.Version;
            Check("TryRemoveBinding：真删掉一条 → 版本号 +1",
                boundPage.TryRemoveBinding(boundLamp, "IsOn", out bindErrorText)
                && boundLamp.Bindings.Count == 0
                && boundPage.Version == versionBeforeRemove + 1,
                $"{boundLamp.Bindings.Count} 条 / v{versionBeforeRemove} → v{boundPage.Version}");

            int versionBeforeNoop = boundPage.Version;
            Check("TryRemoveBinding 幂等：本来就没绑也算成立，且版本号不平白刷高（点一下空按钮就提示未保存很恼人）",
                boundPage.TryRemoveBinding(boundLamp, "IsOn", out bindErrorText)
                && boundPage.Version == versionBeforeNoop,
                $"v{versionBeforeNoop} → v{boundPage.Version}");

            // ---------------- ⑧ 挂摘可逆、且幂等 ----------------

            int baseline = groupsRaised;

            panel.Deactivate();
            editor.SelectedElement = Named("Hmi.Ellipse", "摘掉之后");
            Check("Deactivate 后编辑器换选中不再驱动面板（面板离树时不在后台白重建）",
                groupsRaised == baseline, $"多通知 {groupsRaised - baseline} 次");

            panel.Activate();
            // Activate 自己就重建一次（把挂接期间错过的选中变更补回来），所以基线要在它之后重取
            int afterActivate = groupsRaised;
            panel.Activate();
            editor.SelectedElement = Named("Hmi.Rectangle", "重挂之后");
            Check("重新挂上立刻补一次重建，再换选中恢复正常驱动；重复 Activate 不攒第二层订阅（只多这一来一回）",
                afterActivate == baseline + 1 && groupsRaised == baseline + 2 && Total(panel) == 13,
                $"挂上时 {afterActivate - baseline} 次，之后 {groupsRaised - afterActivate} 次");

            // ---------------- ⑨ 面板写的值真的能落到控件上 ----------------

            var greenRect = Named("Hmi.Rectangle", "绿灯底");
            editor.SelectedElement = greenRect;
            Row("Fill").Value = "#FF00FF00";

            var control = ElementRegistry.CreateControl(greenRect);
            control.Refresh();
            Check("属性袋 → 依赖属性这条链走通：面板改的填充色在控件上就是那个绿",
                (control.Fill as SolidColorBrush)?.Color == Color.FromRgb(0, 0xFF, 0),
                control.Fill?.GetType().Name ?? "null");

            // ---------------- ⑩ 画面模式：同一个面板编辑画面自己（S3-e3） ----------------

            editor.SelectedElement = null;
            var page = editor.SelectedPage!;

            Check("没选中图元即长出画面属性行，行数与组序全取自 ScadaPageProperties 声明（面板不写死）",
                Total(panel) == ScadaPageProperties.All.Count
                && string.Join("/", panel.Groups.Select(g => g.Name)) == "基本信息/画布尺寸/背景/设计辅助/运行",
                $"{Total(panel)} 行 / {string.Join("/", panel.Groups.Select(g => g.Name))}");

            Check(
                "网格显示、吸附这两个开关不进画面面板：顶栏已有勾选框且绑同一个模型属性，" +
                "两处入口改同一个值只会让人怀疑它们不同步",
                panel.Groups.SelectMany(g => g.Rows).All(r => r.Key != "ShowGrid" && r.Key != "SnapToGrid"),
                string.Join("|", panel.Groups.SelectMany(g => g.Rows).Select(r => r.Key)));

            Row("Background").Value = "#FF205020";
            Check("面板改背景色：直接写进落盘字段（不中转副本），色块与模型同一次读到同一个值",
                page.Background == "#FF205020"
                && Row("Background").SwatchBrush is SolidColorBrush bg && bg.Color.A == 0xFF
                && bg.Color.R == 0x20 && bg.Color.G == 0x50,
                $"{page.Background} / {(Row("Background").SwatchBrush as SolidColorBrush)?.Color.ToString() ?? "null"}");

            page.Width = 1280.5;
            Check(
                "顶栏/别处改画面宽度，面板对应行立刻回读——画面模式与图元模式共用同一条回读路径",
                Row("Width").Value == "1280.5" && Row("Width").IsValid,
                Row("Width").Value);

            Row("Height").Value = "720.25";
            Check("画面尺寸按不变文化往返：小数不会被中文区域念成 72025 也不会被判非法",
                page.Height == 720.25 && Row("Height").Value == "720.25",
                $"模型 {page.Height} / 面板 {Row("Height").Value}");

            double heightBefore = page.Height;
            Row("Height").Value = "高一千";
            Check("画面数字行打了字母：模型纹丝不动、文本留着、行标红（与图元数字行同一套语义）",
                page.Height == heightBefore && Row("Height").Value == "高一千" && !Row("Height").IsValid,
                $"模型 {page.Height} / 面板 {Row("Height").Value}");

            Row("Width").Value = "0";
            Check("宽填 0 越界：照样写进模型，只标红「不能小于 1」（画面被改小到看不见是用户的选择，面板不替他决定）",
                page.Width == 0 && !Row("Width").IsValid && Row("Width").ErrorText!.Contains("不能小于 1"),
                $"模型 {page.Width} / {Row("Width").ErrorText ?? "无提示"}");

            Check("画面属性一律不给 ƒx：画面级绑定要另立落点，现在放开只会弹「还没接」",
                panel.Groups.SelectMany(g => g.Rows).All(r => !r.IsBindable)
                && ScadaPageProperties.All.All(s => !s.IsBindable), "");

            page.Name = "主画面";
            Check("画面改名（含走面板的名称行）：标题点名的是画面，不是残留的图元名",
                panel.HeaderText == "画面 · 主画面" && Row("Name").Value == "主画面",
                $"{panel.HeaderText} / {Row("Name").Value}");

            var page2 = editor.Document.AddPage("辅画面");
            editor.SelectedPage = page2;
            Check("切页等于换编辑对象：行的写入目标跟着换成新画面，不会把属性改到看不见的画面上",
                panel.HeaderText == "画面 · 辅画面" && Row("Background").Value == "#FF1E1E1E",
                $"{panel.HeaderText} / {Row("Background").Value}");

            Row("GridSize").Value = "25";
            Check("面板改网格间距落在新画面上，旧画面不受牵连",
                page2.GridSize == 25 && page.GridSize == 10,
                $"新 {page2.GridSize} / 旧 {page.GridSize}");

            // ---- 「运行」组：启动画面（方案级单选）与加载事件（画面级开关）----
            // 这两行看着长得一模一样（都是勾选框），落点却相反：一个存方案的单个 Id，
            // 一个存各自画面的 bool。下面的断言就是钉这个差别的，别再被界面骗过去。

            Row("StartupPage").Value = "True"; // 此刻选中的是 page2（辅画面）
            Check("勾「启动画面」写的是方案里那一个 Id，面板行读回来的也是它（投影，不是第二份真值）",
                editor.Document.StartupPageId == page2.PageId && Row("StartupPage").Value == "True",
                $"Id={editor.Document.StartupPageId} / 行={Row("StartupPage").Value}");

            editor.SelectedPage = page;
            Check("另一画面的同名行读作未勾选：互斥靠单值字段白送，不靠写入时横着清其余页",
                Row("StartupPage").Value == "False" && ReferenceEquals(editor.Document.ResolveStartupPage(), page2),
                $"{Row("StartupPage").Value} / 启动={(editor.Document.ResolveStartupPage()?.Name ?? "null")}");

            Row("StartupPage").Value = "True";
            Check("直接改勾另一页即换启动画面，前一页无需先取消勾选",
                editor.Document.StartupPageId == page.PageId
                && editor.Document.FindPage(editor.Document.StartupPageId) == page,
                $"Id={editor.Document.StartupPageId}");

            Row("Loaded").Value = "True";
            Check("「加载事件」落在画面自己的字段上：各页独立，与启动画面的方案级互斥正相反",
                page.EnableLoadedEvent && !page2.EnableLoadedEvent && Row("Loaded").Value == "True",
                $"{page.Name}={page.EnableLoadedEvent} / {page2.Name}={page2.EnableLoadedEvent}");

            editor.SelectedPage = page2;
            Row("StartupPage").Value = "False";
            Check("在别的画面上取消勾选是空操作：绝不允许把别人身上的启动指定擦掉",
                editor.Document.StartupPageId == page.PageId,
                $"Id={editor.Document.StartupPageId}");

            editor.SelectedPage = page;
            Row("StartupPage").Value = "False";
            Check("在启动画本尊上取消勾选才清空，运行态随即回落第一页而不是显示空白",
                editor.Document.StartupPageId == Guid.Empty
                && ReferenceEquals(editor.Document.ResolveStartupPage(), editor.Document.Pages[0]),
                $"Id={editor.Document.StartupPageId} / 回落={(editor.Document.ResolveStartupPage()?.Name ?? "null")}");

            editor.SelectedPage = page2;

            // 先趁还在画面模式里验摘订阅：画面行的键名固定，此时取 "Width" 才是画面那一行
            panel.Deactivate();
            page2.Width = 800;
            Check("Deactivate 后画面订阅也摘干净：外部改画面宽度不再回读到面板行（否则面板在后台钉着旧画面）",
                Row("Width").Value != "800", Row("Width").Value);
            panel.Activate();
            Check("重新 Activate 会补一次重建，画面上的新值读回来了",
                Row("Width").Value == "800", Row("Width").Value);

            editor.SelectedElement = Named("Hmi.Rectangle", "抢回焦点");
            Check("选中图元立刻切回图元模式：画面退居其后，两种模式互斥不混排",
                panel.HasElement && panel.Page == null && Total(panel) == 13 && Row("$Width").Value == "120",
                $"{Total(panel)} 行 / {panel.HeaderText}");

            // ---------------- ⑪ 绑定态的"长相"：真渲染一遍，像素与视觉树双复核 ----------------
            //
            // ⑦/⑦B 验的是"模型里到底有没有那条绑定"，这一节验的是"用户看不看得见"。
            // 属性面板此前从没被渲染过（[X] 只渲染过画布），所以这里把真 View 装起来离屏渲染：
            // 一来留一张证据图供人眼复核排版与配色，二来把"绑了 / 没绑"的视觉差别钉成断言——
            // 配色或可见性哪天被人顺手改坏，红的是断言，而不是交付后的客户现场。
            //
            // 刻意用真 View 而不是照 XAML 另搭一份 Grid：只有真 View 才能证明"面板确实长这样"，
            // 重搭一份验的只是重搭的那份（XAML 改坏了断言照样绿）。

            var shotPage = editor.SelectedPage!;
            var shotLamp = editor.AddElement("Hmi.Indicator", new Point(30, 30))!;
            Check("渲染前先放一条真绑定（另一行保持未绑，作对照）",
                shotPage.TrySetBinding(shotLamp, "IsOn", speedId, "Speed", out string shotError),
                shotError);

            // Prism 9 的 AutoWireViewModel 是在 InitializeComponent 里就立刻解析 VM 的
            // （不是等 Loaded），而断言宿主里没有 Prism 容器，默认回落到 Activator 会因为
            // ScadaPropertyViewModel 没有无参构造而炸在 XAML 解析期。这里登记一个工厂，
            // 让自动装配直接拿到我们手上这个已经装配好的 VM——生产代码一个字都不用改。
            ViewModelLocationProvider.Register<ScadaPropertyView>(() => panel);

            var view = new ScadaPropertyView { DataContext = panel };

            // 高度给无穷大：面板里是 ScrollViewer，给有限高度会把超出的行裁掉，
            // 而这一节要的恰恰是"整幅面板长什么样"，不是"某 600px 窗口里能看见多少"。
            view.Measure(new Size(260, double.PositiveInfinity));
            int shotH = (int)Math.Ceiling(Math.Max(1, view.DesiredSize.Height));
            view.Arrange(new Rect(0, 0, 260, shotH));
            view.UpdateLayout();

            var shot = RenderToBitmap(view, 260, shotH);

            Check("面板整体渲染成图：有实打实的高度、底色 #FFF7F8FA 真的被刷出来了（不是一片空白）",
                shotH > 200
                && ReferenceEquals(view.DataContext, panel)
                && HasColorIn(shot, new Rect(0, 0, 260, shotH), "#FFF7F8FA"),
                $"{shotH}px 高 / DataContext={(ReferenceEquals(view.DataContext, panel) ? "还是面板 VM" : "被换掉了")}");

            // 未绑的行（OnColor）不该有变量带；绑过的行（IsOn）必须有且只有一条。
            // 注意必须连同祖先一起问"显示出来了没有"：DataTemplate 会给<b>每一行</b>都造一份变量带，
            // 没绑的那些只是被 Collapsed 收掉了（元素仍在视觉树里、尺寸为零），
            // 按"树里有没有"去数会把它们全数进来。
            var chips = CollectVisual<Border>(view,
                b => b.Background is SolidColorBrush cb && SameColor(cb.Color, "#FFEAF3FF")
                     && IsShown(b, view));
            Check("整幅面板里只有绑过的那一行真的显示变量带（没绑的行仍是原来的一行，不白占高度）",
                chips.Count == 1, $"显示中的变量带 {chips.Count} 条");

            var chip = chips.FirstOrDefault();
            var chipText = FindVisual<TextBlock>(view, t => t.Text == "Speed");
            Check("变量带里写的就是变量名，且带底 #FFEAF3FF / 带边 #FFC6DDF5（与属性行同列对齐的那条）",
                chip != null && chipText != null
                && chip!.BorderBrush is SolidColorBrush cbb && SameColor(cbb.Color, "#FFC6DDF5")
                && FindVisual<Button>(chip, b => Equals(b.Content, "✕")) != null,
                chip == null ? "找不到变量带" : $"名字={(chipText?.Text ?? "null")} / ✕={(FindVisual<Button>(chip, b => Equals(b.Content, "✕")) != null)}");

            if (chip != null)
            {
                var chipRect = BoundsIn(chip, view);
                Check("变量带的底色真的落在画面上（在带内采到 #FFEAF3FF，不是被裁剪或被别的元素盖住）",
                    HasColorIn(shot, chipRect, "#FFEAF3FF"), RectText(chipRect));

                // 变量带是"行高随绑定长出来"的那一段，上下都得留出可见间隙。
                // 这类事故在离屏图上只有断言抓得住：带子贴着上一行的输入框（甚至压上去），
                // 屏幕上就是"这一行的值到底归谁"说不清，而面板其余部分看着一切正常。
                // 阈值取 4 而不是 1：2px 上下边距实测只剩 2.4px 的缝，带子像贴在输入框下沿；
                // 钉住 4 是为了把"分组感"这个结论固定下来，别哪天又被顺手改回紧贴。
                var hostEditor = CollectVisual<CheckBox>(view, _ => true)
                    .Where(c => (c.DataContext as ScadaPropertyRowBase)?.Key == "IsOn")
                    .Select(c => BoundsIn(c, view))
                    .FirstOrDefault();
                double gapAbove = chipRect.Top - hostEditor.Bottom;
                Check("变量带与上一行的编辑器之间留出可见间隙（不贴着、不压住：行高是长出来的，不是挤出来的）",
                    hostEditor.Height > 0 && gapAbove >= 4,
                    $"间隙 {gapAbove:0.#}px（编辑器底 {hostEditor.Bottom:0.#} / 带顶 {chipRect.Top:0.#}）");
            }

            // ƒx：同一颗按钮两种状态。绑过的是蓝的、没绑的是灰的——这里既看视觉树上的 Foreground，
            // 也看画面上有没有那片蓝，两头都钉住才算"用户真的看见蓝了"。
            var boundFx = FindVisual<Button>(view,
                b => Equals(b.Content, "ƒx") && (b.DataContext as ScadaPropertyRowBase)?.Key == "IsOn");
            var freeFx = FindVisual<Button>(view,
                b => Equals(b.Content, "ƒx") && (b.DataContext as ScadaPropertyRowBase)?.Key == "OnColor");

            Check("绑过那行的 ƒx 转成蓝色 SemiBold（一眼看出这行的值已被变量接管）",
                boundFx?.Foreground is SolidColorBrush bf && SameColor(bf.Color, "#FF2A6BB5")
                && boundFx!.FontWeight == FontWeights.SemiBold,
                boundFx == null ? "找不到 ƒx" : ColorName((boundFx.Foreground as SolidColorBrush)?.Color ?? Colors.Transparent));
            Check("没绑那行的 ƒx 仍是灰色常态（不是所有行都亮成蓝色）",
                freeFx?.Foreground is SolidColorBrush ff && SameColor(ff.Color, "#FF909399")
                && freeFx!.FontWeight != FontWeights.SemiBold,
                freeFx == null ? "找不到 ƒx" : ColorName((freeFx.Foreground as SolidColorBrush)?.Color ?? Colors.Transparent));

            if (boundFx != null && freeFx != null)
            {
                var boundRect = BoundsIn(boundFx, view);
                var freeRect = BoundsIn(freeFx, view);
                Check("蓝的 ƒx 在画面上真的画出来了（那一格里采得到 #FF2A6BB5）",
                    HasColorIn(shot, boundRect, "#FF2A6BB5", 90), RectText(boundRect));
                Check("灰的 ƒx 那一格里采不到蓝（对照组成立，否则上一条可能只是别处漏过来的颜色）",
                    !HasColorIn(shot, freeRect, "#FF2A6BB5", 90), RectText(freeRect));
            }

            // 留一份证据图，供人眼复核排版与配色（不参与判定）
            string shotDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(shotDir);
            string shotPng = Path.Combine(shotDir, "scada-property-binding.png");
            SavePng(shot, shotPng);
            Console.WriteLine($"        （渲染证据：{shotPng}）");

            // ---------------- ⑬ ✕"能不能点"必须真渲染才算数（真机实锤过的那条） ----------------
            //
            // ⑦ 只验到"命令的 CanExecute 返回 true"，可真机上看见的是 button "✕" (disabled)。
            // 差的那一步藏在 WPF 内部：ButtonBase 把命令的可用性抄进 IsEnabledCore，
            // 抄的时机是"命令抛 CanExecuteChanged"那一刻，而不是每次去问 CanExecute。
            // 所以这一节把真机那条时间线原样走一遍——先让按钮以"这一行还没绑"的样子被实例化出来
            // （此时初值就是禁用），再绑上变量，然后看按钮自己有没有翻过来。
            // 光问 CanExecute 是问不出这个缺陷的：绑完它当然是 true，坏的是没人通知按钮去重算。
            // 这条断言在补 RaiseCanExecuteChanged 之前是红的。

            var freshLamp = editor.AddElement("Hmi.Indicator", new Point(40, 40))!;
            var freshRow = (ScadaPropertyRow)Row("IsOn");

            var view2 = new ScadaPropertyView { DataContext = panel };
            view2.Measure(new Size(260, double.PositiveInfinity));
            view2.Arrange(new Rect(0, 0, 260, Math.Ceiling(Math.Max(1, view2.DesiredSize.Height))));
            view2.UpdateLayout();

            var clearBtn = FindVisual<Button>(view2,
                b => Equals(b.Content, "✕") && (b.DataContext as ScadaPropertyRowBase)?.Key == "IsOn");

            Check("未绑的行上，✕ 被实例化出来时就是禁用的（清除入口不该对没绑的行亮着）",
                clearBtn != null && !clearBtn.IsEnabled && !freshRow.HasBinding,
                clearBtn == null ? "找不到 ✕" : $"IsEnabled={clearBtn.IsEnabled} / 已绑={freshRow.HasBinding}");

            picker.NextId = Guid.NewGuid();
            picker.NextName = "Speed2";
            panel.BindVariableCommand.Execute(freshRow);

            Check("绑上之后同一颗 ✕ 自己翻成可用（不抛 CanExecuteChanged 就停在初值禁用上，真机实锤）",
                clearBtn != null && clearBtn.IsEnabled && freshRow.HasBinding,
                clearBtn == null ? "找不到 ✕" : $"IsEnabled={clearBtn.IsEnabled} / 已绑={freshRow.HasBinding} / {freshLamp.FindBinding("IsOn")?.VariableName}");
        }

        // ==================================================================
        //  [V] 图层与画面管理：Try* 家族的判定与文案 + 画布上的可见性契约
        //
        //  这一节盯的是两类东西：
        //  1) 模型侧——每个 Try* 的拒绝条件、中文提示原文、幂等放行、以及"该不该刷 Version"。
        //     界面侧的置灰条件与确认框文案全从这里读，口径一改就得同时改两处，所以钉死。
        //  2) 控件侧——图层开关落到画布上的形状（折叠不摘除、悬空归属按未分层处理、
        //     换画面/清集合时订阅摘干净）。这些不看断言根本发现不了，画错了只是"少一块东西"。
        // ==================================================================

        private static void LayerAndPageChecks()
        {
            Section("[V] 图层/画面管理：Try* 家族判定 + 画布可见性契约");

            LayerNamingChecks();
            LayerRemoveChecks();
            LayerMoveAndAssignChecks();
            LayerVerdictChecks();
            LayerVersionChecks();
            PageManagementChecks();
            LayerCanvasChecks();
        }

        // ---- V-1 图层命名与改名 ----

        private static void LayerNamingChecks()
        {
            var doc = new ScadaDocument();
            var page = doc.AddPage("主画面");
            var otherPage = doc.AddPage("辅画面");
            var first = page.DefaultLayer;

            Check("新建画面自带一个默认图层（AddPage 里先建层再进集合：订阅者不会看见半成品画面）",
                first != null && page.Layers.Count == 1 && page.Layers[0].Name == "图层_1",
                $"层数 {page.Layers.Count}");

            var second = page.AddLayer();
            var device = page.AddLayer(" 设备层 ");
            var third = page.AddLayer();
            Check("自动命名取未占用的最小序号，显式名两端去空白",
                second.Name == "图层_2" && device.Name == "设备层" && third.Name == "图层_3",
                string.Join("、", page.Layers.Select(l => l.Name)));

            Check("空图层允许删除，腾出的序号会被下一次自动命名复用（名字不是身份，不复用才不会撞 Id）",
                page.TryRemoveLayer(second, out var freedErr) && page.AddLayer().Name == "图层_2", freedErr);

            Check("改名只认本画面的图层：拿着别人画面的图层引用改不动",
                !page.TryRenameLayer(otherPage.Layers[0], "串门", out var foreignErr)
                && foreignErr == "图层不存在，无法改名", foreignErr);

            Check("纯空白名拒绝（Trim 之后长度为 0 也算空，不留一个看不见名字的行）",
                !page.TryRenameLayer(device, "   ", out var emptyErr) && emptyErr == "图层名不能为空", emptyErr);

            int idempotent = page.Version;
            Check("原名改回来是幂等空操作：放行且不刷 Version（不刷就不该把方案标脏）",
                page.TryRenameLayer(device, "设备层", out var sameErr)
                && sameErr.Length == 0 && page.Version == idempotent,
                $"Version {idempotent} → {page.Version}");

            Check("重名拒绝且忽略大小写（按名找图层只有一份解析口径，重名会解析到谁全看列表次序）",
                !page.TryRenameLayer(third, "设备层", out var dupErr)
                && dupErr == "已存在同名图层 [设备层]，请更换名称", dupErr);

            string caseOne = "", caseTwo = "";
            Check("只改大小写不算重名（查重必须排除自身，否则这种改名永远改不动）",
                page.TryRenameLayer(device, "Alarm", out caseOne)
                && page.TryRenameLayer(device, "ALARM", out caseTwo) && device.Name == "ALARM",
                caseOne + caseTwo);
        }

        // ---- V-2 删图层 ----

        private static void LayerRemoveChecks()
        {
            var doc = new ScadaDocument();
            var page = doc.AddPage();
            var only = page.Layers[0];

            Check("最后一个图层删不掉（画面没有图层时新图元无处可归，运行态也没有落点）",
                !page.TryRemoveLayer(only, out var lastErr)
                && lastErr == "画面至少需要保留一个图层，无法删除最后一个图层", lastErr);

            Check("CanRemoveLayer 与 TryRemoveLayer 同口径（按钮提前置灰的条件必须就是真删会被拒的条件）",
                !page.CanRemoveLayer(only) && !page.CanRemoveLayer(null), "两个判定不一致");

            var temp = page.AddLayer("临时");
            var kept = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "留着", LayerId = temp.LayerId };
            page.Elements.Add(kept);

            Check("非空图层拒绝删除，并把还剩几个图元如实带回（图元是用户资产，不能跟着图层悄悄消失）",
                !page.TryRemoveLayer(temp, out var busyErr)
                && busyErr == "图层 [临时] 上还有 1 个图元，请先删除或移走它们", busyErr);
            Check("被拒的图层原封不动还在原位",
                page.Layers.Contains(temp) && page.CountElements(temp) == 1,
                page.Layers.Count.ToString());

            string moveErr = "", delErr = "";
            Check("图元移走后空图层就能删",
                page.TryAssignLayer(kept, only, out moveErr) && page.TryRemoveLayer(temp, out delErr),
                moveErr + delErr);
            Check("删图层只删图层：图元还在画面上，只是归到了接它的那一层",
                page.Elements.Count == 1 && kept.LayerId == only.LayerId && page.CountElements(only) == 1,
                $"画面 {page.Elements.Count} 个 / 默认层 {page.CountElements(only)} 个");

            var orphan = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "悬空", LayerId = Guid.NewGuid() };
            page.Elements.Add(orphan);
            Check("指向已删除图层的悬空归属按未分层处理：照样可见可编辑（因为找不到层就把图元藏起来，是用户看不见的坑）",
                page.ResolveLayer(orphan) == null && page.IsElementVisible(orphan) && page.IsElementEditable(orphan),
                orphan.LayerId.ToString("N"));
        }

        // ---- V-3 图层排序与归属 ----

        private static void LayerMoveAndAssignChecks()
        {
            var doc = new ScadaDocument();
            var page = doc.AddPage("甲画面");
            var other = doc.AddPage("乙画面");
            var a = page.AddLayer("甲层");
            var b = page.AddLayer("乙层");
            var c = page.AddLayer("丙层");

            var e1 = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "一号", ZIndex = 3, LayerId = c.LayerId };
            var e2 = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "二号", ZIndex = 1, LayerId = a.LayerId };
            page.Elements.Add(e1);
            page.Elements.Add(e2);
            var zSnapshot = page.Elements.Select(e => e.Name + ":" + e.ZIndex).ToList();

            Check("图层排序：空图层直接拒",
                !page.TryMoveLayer(null, 0, out var nilErr) && nilErr == "图层不存在，无法调整次序", nilErr);
            Check("图层排序：别人画面的图层拒绝（挪了在本画面列表里也看不见，只会让人以为功能坏了）",
                !page.TryMoveLayer(other.Layers[0], 0, out var crossErr)
                && crossErr == "图层不属于本画面，无法调整次序", crossErr);

            int inPlace = page.Version;
            Check("原地放置算空操作：放行且不刷 Version",
                page.TryMoveLayer(c, page.Layers.IndexOf(c), out var sameIdx) && page.Version == inPlace,
                sameIdx + $"Version {inPlace} → {page.Version}");

            Check("越界下标按两端夹取（拖到列表末尾之外是常见手势，不该报错）",
                page.TryMoveLayer(c, 99, out _) && page.Layers.IndexOf(c) == page.Layers.Count - 1
                && page.TryMoveLayer(c, -5, out _) && page.Layers.IndexOf(c) == 0,
                string.Join("、", page.Layers.Select(l => l.Name)));

            Check("调次序只动图层列表，不动任何图元的 ZIndex（叠放只有一个来源：ZIndex）",
                page.Elements.Select(e => e.Name + ":" + e.ZIndex).SequenceEqual(zSnapshot),
                string.Join("、", page.Elements.Select(e => $"{e.Name}:{e.ZIndex}")));

            var foreignElement = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "外来的" };
            other.Elements.Add(foreignElement);
            Check("改归属：图元不属于本画面时拒绝",
                !page.TryAssignLayer(foreignElement, a, out var srcErr)
                && srcErr == "图元不属于本画面，无法调整图层", srcErr);
            Check("改归属：目标图层不属于本画面时拒绝（否则图元会掉进本画面里根本看不见的层）",
                !page.TryAssignLayer(e1, other.Layers[0], out var tgtErr)
                && tgtErr == "目标图层不属于本画面，无法调整图层", tgtErr);
            Check("目标传 null = 取消分层归属（LayerId 归零，与未分层同一口径）",
                page.TryAssignLayer(e1, null, out var unbindErr) && e1.LayerId == Guid.Empty
                && page.CountElements(c) == 0, unbindErr);
            Check("改归属就是把 LayerId 换个值，图元不摘集合、不重建、ZIndex 不动",
                page.TryAssignLayer(e2, c, out var assignErr) && e2.LayerId == c.LayerId
                && page.Elements.Count == 2 && e2.ZIndex == 1, assignErr);
        }

        // ---- V-4 可见 / 可编辑的唯一口径 ----

        private static void LayerVerdictChecks()
        {
            var page = new ScadaPage();
            var layer = page.AddLayer("层");
            var onLayer = new ScadaElement { TypeKey = "Hmi.Rectangle", LayerId = layer.LayerId };
            var plain = new ScadaElement { TypeKey = "Hmi.Rectangle" }; // 未分层
            page.Elements.Add(onLayer);
            page.Elements.Add(plain);

            Check("图层没做任何限制时层上图元可见可编辑",
                page.IsElementVisible(onLayer) && page.IsElementEditable(onLayer), "");

            layer.IsLocked = true;
            Check("图层锁定只锁编辑，不挡显示",
                !page.IsElementEditable(onLayer) && page.IsElementVisible(onLayer), "");
            Check("未分层的图元不受图层锁定牵连（Guid.Empty 不是某个层，不能顺手把它锁了）",
                page.IsElementEditable(plain) && page.IsElementVisible(plain), "");

            layer.IsLocked = false;
            layer.IsVisible = false;
            Check("图层隐藏只挡显示，不锁编辑（隐藏是投影开关，不是权限）",
                !page.IsElementVisible(onLayer) && page.IsElementEditable(onLayer), "");

            layer.IsLocked = true;
            onLayer.IsLocked = true;
            Check("图层锁定与图元锁定是或关系：任一条命中就不可编辑",
                !page.IsElementEditable(onLayer), "");
            layer.IsLocked = false;
            Check("图元自身锁定后，图层开着也照样不可编辑（两层锁互不覆盖，各说各的）",
                !page.IsElementEditable(onLayer) && !page.IsElementVisible(onLayer), "");

            Check("空引用一律判否且不抛异常（视图侧可以直接喂还没解析出来的结果）",
                !page.IsElementEditable(null) && !page.IsElementVisible(null)
                && page.CountElements(null) == 0 && page.ResolveLayer(null) == null
                && page.FindLayer(Guid.Empty) == null, "");
        }

        // ---- V-5 图层变更冒泡到画面 Version（脏标记的依据） ----

        private static void LayerVersionChecks()
        {
            var page = new ScadaPage();
            var keep = page.AddLayer();
            var extra = page.AddLayer("将被清空");

            int v = page.Version;
            keep.IsVisible = false;
            Check("图层改可见 → 画面 Version 动", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            keep.IsLocked = true;
            Check("图层改锁定 → 画面 Version 动", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            page.TryRenameLayer(keep, "改过名", out _);
            Check("图层改名 → 画面 Version 动（名字落盘）", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            page.AddLayer("新增层");
            Check("加图层 → 画面 Version 动", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            page.TryMoveLayer(extra, 0, out _); // extra 此刻正在下标 1，挪到 0 才是真挪动
            Check("调图层次序 → 画面 Version 动（次序也落盘）", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            page.TryRemoveLayer(extra, out _);
            Check("删图层 → 画面 Version 动", page.Version > v, $"{v} → {page.Version}");

            var ghosts = page.Layers.ToList();
            var oldCollection = page.Layers;
            v = page.Version;
            page.Layers.Clear();
            Check("整表清空 → 画面 Version 动", page.Version > v, $"{v} → {page.Version}");

            v = page.Version;
            foreach (var ghost in ghosts)
            {
                ghost.IsVisible = true;
                ghost.IsLocked = false;
                ghost.Name = "幽灵";
            }
            Check("清空后旧图层的变更不再刷 Version（Reset 走登记表全量摘挂：漏摘一条就是画面对一份没人用的旧数据白响应，还顺手把方案标脏）",
                page.Version == v, $"Version {v} → {page.Version}，幽灵 {ghosts.Count} 个");

            page.Layers = new ObservableCollection<ScadaLayer> { new ScadaLayer { Name = "换上来" } };
            Check("整个换掉图层集合后 DefaultLayer 指向新集合的第一项",
                page.DefaultLayer?.Name == "换上来", page.DefaultLayer?.Name ?? "null");

            v = page.Version;
            oldCollection.Add(new ScadaLayer { Name = "旧集合加的" });
            Check("换集合时旧集合的订阅摘掉了（旧集合再变更不该驱动画面）",
                page.Version == v, $"{v} → {page.Version}");

            v = page.Version;
            page.Layers[0].IsVisible = false;
            Check("换上的新集合确实被订阅了（setter 里不挂这一条，反序列化进来的图层开关就永远标不脏）",
                page.Version > v, $"{v} → {page.Version}");
        }

        // ---- V-6 画面管理（新建 / 删除 / 改名 / 排序） ----

        private static void PageManagementChecks()
        {
            var doc = new ScadaDocument();
            var home = doc.AddPage();
            var alarm = doc.AddPage(" 报警画面 ");

            Check("画面自动命名 画面_1 / 画面_2，显式名两端去空白",
                home.Name == "画面_1" && alarm.Name == "报警画面", $"{home.Name} / {alarm.Name}");
            Check("每个新建画面都自带一个图层（新画布上放图元得有个默认落点）",
                doc.Pages.All(p => p.DefaultLayer != null && p.Layers.Count == 1),
                string.Join("、", doc.Pages.Select(p => $"{p.Name}:{p.Layers.Count}")));

            Check("删掉的画面腾出的序号会被下一次自动命名复用",
                doc.TryRemovePage(home, out var removedErr) && doc.AddPage().Name == "画面_1", removedErr);

            var foreign = new ScadaDocument().AddPage("外人");
            string nilErr = "", fgErr = "";
            Check("画面删除：null 与别的方案里的画面一律拒（不能拿对象引用当通行证）",
                !doc.TryRemovePage(null, out nilErr) && nilErr == "画面不存在，无法删除"
                && !doc.TryRemovePage(foreign, out fgErr) && fgErr == "画面不存在，无法删除",
                nilErr + fgErr);

            var solo = new ScadaDocument();
            var only = solo.AddPage();
            Check("最后一个画面删不掉（运行态没有画面就没东西可显示，主窗体会停在空白上）",
                !solo.TryRemovePage(only, out var lastErr)
                && lastErr == "方案至少需要保留一个画面，无法删除最后一个画面", lastErr);

            var busy = solo.AddPage("带图元的");
            busy.Elements.Add(new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "上面有东西" });
            Check("带图元的画面照删：模型只守\"还剩一个\"这条底线（界面侧务必弹确认框——工程上没有撤销）",
                solo.TryRemovePage(busy, out var busyErr) && busyErr.Length == 0 && solo.Pages.Count == 1,
                busyErr);

            string emptyNameErr = "", nilName = "";
            Check("画面改名：纯空白拒、不认识的画面拒",
                !doc.TryRenamePage(alarm, "\t ", out emptyNameErr) && emptyNameErr == "画面名不能为空"
                && !doc.TryRenamePage(null, "x", out nilName) && nilName == "画面不存在，无法改名",
                emptyNameErr + nilName);

            int alarmVersion = alarm.Version;
            Check("画面改名：原名改回来是空操作（放行且不刷画面自己的 Version）",
                doc.TryRenamePage(alarm, "报警画面", out var sameName)
                && sameName.Length == 0 && alarm.Version == alarmVersion,
                sameName + $"Version {alarmVersion} → {alarm.Version}");

            Check("画面改名：忽略大小写查重（按名跳转只有一份解析口径，重名会解析到谁全看标签页次序）",
                !doc.TryRenamePage(alarm, "画面_1", out var dupPageErr)
                && dupPageErr == "已存在同名画面 [画面_1]，请更换名称", dupPageErr);

            string caseOne = "", caseTwo = "";
            Check("画面改名：只改大小写放行，且按名查找忽略大小写命中同一个画面",
                doc.TryRenamePage(alarm, "Alarm", out caseOne)
                && doc.TryRenamePage(alarm, "ALARM", out caseTwo)
                && alarm.Name == "ALARM" && ReferenceEquals(doc.FindPageByName("alarm"), alarm),
                caseOne + caseTwo);

            var thirdPage = doc.AddPage("第三个");
            string moveNil = "", moveFg = "";
            Check("画面排序：null 拒、别人方案里的画面拒（两种\"不认识\"分开措辞）",
                !doc.TryMovePage(null, 1, out moveNil) && moveNil == "画面不存在，无法调整次序"
                && !doc.TryMovePage(foreign, 0, out moveFg) && moveFg == "画面不属于本方案，无法调整次序",
                moveNil + moveFg);
            Check("画面排序：原地放置放行",
                doc.TryMovePage(thirdPage, doc.Pages.IndexOf(thirdPage), out var moveSame)
                && moveSame.Length == 0, moveSame);
            Check("画面排序：越界下标夹到端点（拖到标签页末尾之外是常见手势）",
                doc.TryMovePage(thirdPage, 99, out _) && doc.Pages.IndexOf(thirdPage) == doc.Pages.Count - 1
                && doc.TryMovePage(thirdPage, -3, out _) && doc.Pages.IndexOf(thirdPage) == 0,
                string.Join("、", doc.Pages.Select(p => p.Name)));
            Check("画面次序就是标签页次序：Move 之后按下标取得到那个画面",
                ReferenceEquals(doc.Pages[0], thirdPage), doc.Pages[0].Name);
        }

        // ---- V-7 图层规则落到画布上的形状 ----

        private static void LayerCanvasChecks()
        {
            Exception? failure = null;

            // 这一段要真造 ScadaCanvas 与图元控件（都带 DispatcherObject 血统），
            // 样板与 [T]/[U] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunLayerCanvasChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("图层画布断言全程未抛异常", false, failure.ToString());
        }

        private static void RunLayerCanvasChecks()
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };

            // 断言环境里没有窗口，样式查找链走不通，所以显式套样式；顺序必须是
            // Style → ApplyTemplate → Measure/Arrange（反过来 ApplyTemplate 会得到 false）。
            // withPage=false 用来演"宿主还没把 SelectedPage 绑上来"的空上下文状态。
            ScadaCanvas BuildHost(ScadaPage page, bool withPage)
            {
                var host = new ScadaCanvas
                {
                    PageWidth = page.Width,
                    PageHeight = page.Height,
                    GridSize = page.GridSize,
                    SnapToGrid = page.SnapToGrid,
                    ItemsSource = page.Elements,
                };
                host.Style = (Style)theme[typeof(ScadaCanvas)];
                if (withPage)
                    host.Page = page;
                host.ApplyTemplate();
                host.Measure(new Size(host.PageWidth, host.PageHeight));
                host.Arrange(new Rect(0, 0, host.PageWidth, host.PageHeight));
                return host;
            }

            Canvas ElementLayerOf(ScadaCanvas host)
                => (Canvas)host.Template.FindName("PART_ElementLayer", host)!;

            ScadaElementBase ControlOf(ScadaCanvas host, ScadaElement model)
                => ElementLayerOf(host).Children.OfType<ScadaElementBase>()
                    .First(c => ReferenceEquals(c.Element, model));

            // ---------------- ① 接上画面上下文：隐藏 = 折叠不摘除 ----------------

            var doc = new ScadaDocument();
            var page = doc.AddPage("主画面");
            var shownLayer = page.DefaultLayer!;
            var hiddenLayer = page.AddLayer("隐藏层");
            hiddenLayer.IsVisible = false;

            var onShown = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "在可见层", LayerId = shownLayer.LayerId, X = 0, Y = 0 };
            var onHidden = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "在隐藏层", LayerId = hiddenLayer.LayerId, X = 200, Y = 0 };
            var dangling = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "悬空归属", LayerId = Guid.NewGuid(), X = 400, Y = 0 };
            var unknown = new ScadaElement { TypeKey = "Hmi.NoSuchType", Name = "未知类型", LayerId = hiddenLayer.LayerId, X = 600, Y = 0 };
            foreach (var element in new[] { onShown, onHidden, dangling, unknown })
                page.Elements.Add(element);

            var host = BuildHost(page, withPage: true);
            var surface = ElementLayerOf(host);

            Check("隐藏图层不摘控件：元素层永远是数据集合的全量同步视图",
                host.RenderedElementCount == page.Elements.Count && host.RenderedElementCount == 4,
                $"渲染 {host.RenderedElementCount} / 数据 {page.Elements.Count}");
            Check("隐藏层上的图元当场就是 Collapsed（用 Collapsed 不用 Hidden：Hidden 仍占位仍吃命中，比不隐藏更糟）",
                ControlOf(host, onHidden).Visibility == Visibility.Collapsed, "");
            Check("可见层上的图元、以及归属悬空的图元照常显示（找不到图层 = 未分层 = 不受约束）",
                ControlOf(host, onShown).Visibility == Visibility.Visible
                && ControlOf(host, dangling).Visibility == Visibility.Visible, "");

            var placeholder = surface.Children.OfType<FrameworkElement>()
                .FirstOrDefault(f => f is not ScadaElementBase && ReferenceEquals(f.Tag, unknown));
            Check("未知类型的占位框也听图层的话（不跟着收，隐藏层上会留下一块谁也不认识的灰框）",
                placeholder != null && placeholder.Visibility == Visibility.Collapsed,
                placeholder?.GetType().Name ?? "没找到占位框");

            shownLayer.IsLocked = true;
            Check("锁定不是隐藏：图层锁定不改变任何容器的可见性（外观上锁与不锁一眼看不出，但绝不该把东西藏了）",
                ControlOf(host, onShown).Visibility == Visibility.Visible
                && ControlOf(host, onHidden).Visibility == Visibility.Collapsed, "");
            shownLayer.IsLocked = false;

            // ---------------- ② 选中项与图层开关的相互处置 ----------------

            host.SelectedElement = onShown;
            Check("可见层上的图元能正常选中", ReferenceEquals(host.SelectedElement, onShown), "");

            var hiddenControl = ControlOf(host, onHidden);
            hiddenLayer.IsVisible = true;
            Check("图层一开图元立刻回来，而且控件实例还是原来那个（走重建的话万级图元是一次几百毫秒白等）",
                ReferenceEquals(ControlOf(host, onHidden), hiddenControl)
                && surface.Children.Contains(hiddenControl)
                && hiddenControl.Visibility == Visibility.Visible, "");

            host.SelectedElement = onHidden;
            hiddenLayer.IsVisible = false;
            Check("选中项所在的图层一关就顺手取消选中：没有撤销，看不见却能被 Delete 的选中项是找不回来的坑",
                host.SelectedElement == null, host.SelectedElement?.Name ?? "null");

            host.SelectedElement = onShown;
            hiddenLayer.IsVisible = true;
            hiddenLayer.IsVisible = false;
            Check("关的是别的图层，选中不受牵连（不该把与本次操作无关的选中态一起清了）",
                ReferenceEquals(host.SelectedElement, onShown), host.SelectedElement?.Name ?? "null");

            // ---------------- ③ 往正藏着的图层上新建图元 ----------------

            var late = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "藏层上新建", LayerId = hiddenLayer.LayerId, X = 0, Y = 100 };
            page.Elements.Add(late);
            Check("往正被隐藏的图层上新建图元：容器一造出来就是 Collapsed（不是先闪一下再收）",
                host.RenderedElementCount == 5 && ControlOf(host, late).Visibility == Visibility.Collapsed,
                host.RenderedElementCount.ToString());

            // ---------------- ④ 整表清空：订阅要摘干净 ----------------

            var layerGhosts = page.Layers.ToList();
            page.Layers.Clear();
            Check("图层整表清空后图元按未分层处理：全部显示（找不到判定依据时宁可多显示）",
                host.RenderedElementCount == 5
                && surface.Children.OfType<ScadaElementBase>().All(c => c.Visibility == Visibility.Visible),
                surface.Children.OfType<ScadaElementBase>().Count(c => c.Visibility == Visibility.Visible).ToString());

            layerGhosts[0].IsVisible = false;
            layerGhosts[0].IsLocked = true;
            Check("清空后旧图层的开关再也驱动不了画布（Reset 按登记表全量摘挂；漏摘就是画布被一份不属于它的数据钉住）",
                ControlOf(host, onShown).Visibility == Visibility.Visible
                && ControlOf(host, late).Visibility == Visibility.Visible, "");

            // ---------------- ⑤ 没有画面上下文 / 后补上下文 / 换画面 ----------------

            var barePage = doc.AddPage("空上下文画面");
            var bareLayer = barePage.DefaultLayer!;
            var bareElement = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "无上下文", LayerId = bareLayer.LayerId };
            barePage.Elements.Add(bareElement);
            var bareHost = BuildHost(barePage, withPage: false);
            bareLayer.IsVisible = false;
            Check("没给画面上下文时图层规则完全不参与：一律显示（纯控件库用法不会凭空藏东西）",
                bareHost.RenderedElementCount == 1 && ControlOf(bareHost, bareElement).Visibility == Visibility.Visible,
                ControlOf(bareHost, bareElement).Visibility.ToString());

            bareHost.Page = barePage;
            Check("画面上下文后补上（宿主绑的 SelectedPage 从 null 变成有值）：立刻按图层重算一遍",
                ControlOf(bareHost, bareElement).Visibility == Visibility.Collapsed
                && bareHost.RenderedElementCount == 1,
                ControlOf(bareHost, bareElement).Visibility.ToString());

            var nextPage = doc.AddPage("下一个画面");
            var nextElement = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "新画面的" };
            nextPage.Elements.Add(nextElement);
            var oldControl = ControlOf(bareHost, bareElement);
            bareHost.ItemsSource = nextPage.Elements;
            bareHost.Page = nextPage;
            Check("换画面时容器整体跟着数据走：前一个画面的控件不残留在新画面上",
                bareHost.RenderedElementCount == 1 && !ElementLayerOf(bareHost).Children.Contains(oldControl)
                && ControlOf(bareHost, nextElement).Visibility == Visibility.Visible,
                bareHost.RenderedElementCount.ToString());

            bareLayer.IsVisible = true;
            Check("画面换人后旧画面的图层开关不再影响新画面（旧订阅在 Page 换人时摘掉了）",
                ControlOf(bareHost, nextElement).Visibility == Visibility.Visible, "");

            // ---------------- ⑥ 控件基类自己那条路（脱离画布） ----------------

            var solo = new RectangleElement { Element = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "单飞" } };
            Check("控件没有被画布托管时一律显示（没注入判定 = 不掺和图层规则，控件库能单独用）",
                solo.Visibility == Visibility.Visible, solo.Visibility.ToString());

            solo.LayerVisibilityResolver = _ => false;
            solo.RefreshLayerVisibility();
            Check("注入判定后控件自己折叠：可见性口径只有一份，画布给什么控件就照办",
                solo.Visibility == Visibility.Collapsed, solo.Visibility.ToString());

            solo.LayerVisibilityResolver = _ => true;
            solo.RefreshLayerVisibility();
            Check("判定说该显示就恢复显示（两条边都走通，翻图层不会留下收不回去的控件）",
                solo.Visibility == Visibility.Visible, solo.Visibility.ToString());

            solo.Element = null;
            solo.RefreshLayerVisibility();
            Check("模型摘掉后按可见兜底（没得判就显示，绝不凭空少一块东西）",
                solo.Visibility == Visibility.Visible, solo.Visibility.ToString());
        }

        // ==================================================================
        //  [W] 运行态会话：起步画面 / Loaded 触发条件 / Start·Stop 幂等
        //
        //  这一节钉的全是**产品规则**而不是渲染：从哪一页起步、勾了才发事件、
        //  连点不重复加载、Stop 可重复。规则住在零 WPF 的 ScadaRuntime 里，
        //  所以能在这里无头跑完——将来接报表、无人值守、多屏运行时，同一批断言照样成立。
        //  真正"渲染完那一帧"的耗时由 WPF 宿主掐表，不在这里测（领域层拿不到那一帧）。
        // ==================================================================
        private static void RuntimeSessionChecks()
        {
            Section("[W] 运行态会话：起步画面 + Loaded 触发条件 + Start/Stop 幂等");

            // ---------------- ① 没东西可跑的时候别硬撑 ----------------

            var idle = new ScadaRuntime(new ScadaDocument());
            var idleHits = 0;
            idle.PageLoaded += _ => idleHits++;
            Check("空方案 Start 返回 false 且不抛（一页都没有，弹个空白全屏窗口比不弹更让人慌）",
                !idle.Start() && idle.CurrentPage == null && !idle.IsRunning, "");
            idle.Stop();
            Check("没 Start 过也能直接 Stop（关窗口/切方案/关编辑器三条路都会调，不该互相甩异常）",
                idle.CurrentPage == null && !idle.IsRunning && idleHits == 0, "");

            // ---------------- ② 起步页取自「启动画面」，不是列表第一页 ----------------

            var doc = new ScadaDocument();
            var home = doc.AddPage("首页");
            var alarm = doc.AddPage("报警总览");
            doc.AddPage("参数设置");
            doc.SetStartupPage(alarm);

            var rt = new ScadaRuntime(doc);
            var shown = new List<ScadaPage>();
            rt.PageLoaded += shown.Add;
            Check("Start 显示的是指定的启动画面，不是列表第一页（用户指定过就得听他的）",
                rt.Start() && ReferenceEquals(rt.CurrentPage, alarm), rt.CurrentPage?.Name ?? "null");
            Check("会话持有的就是那份内存文档（用户刚改完点运行，看到的必须是他刚改的东西）",
                ReferenceEquals(rt.Document, doc), "");
            Check("PageLoaded 是无条件的事实事件：没配事件的画面照样通知一次（S5 起闸门挪到钩子那侧）",
                shown.Count == 1 && ReferenceEquals(shown[0], alarm), $"{shown.Count} 次");
            // 为什么事实事件不许按"配没配动作"来筛：将来想知道"当前显示哪一页"的订阅方
            // （状态栏、多屏、报表）不止日志一个，筛过一次就等于把这条事件私有给了日志。
            Check("但没配钩子的画面跑不出动作：RaisePageEvent 命中 0 条——判定的活全在会话里",
                rt.RaisePageEvent(alarm, ScadaEventType.Loaded) == 0, "");

            // ---------------- ③ 勾了才有动作命中，一次 Start 只通知一次 ----------------

            alarm.EnableLoadedEvent = true;
            var rt2 = new ScadaRuntime(doc);
            var hits = 0;
            var currentPageReadyWhenFired = false;
            rt2.PageLoaded += p =>
            {
                hits++;
                // 先赋值后通知：宿主在回调里要能读到"已经显示出来"的这一页，而不是上一页
                currentPageReadyWhenFired = ReferenceEquals(rt2.CurrentPage, p);
            };
            Check("启动画面：Start 之后恰好收到一次通知（多了就是重复加载，少了就是漏）",
                rt2.Start() && hits == 1, $"{hits} 次");
            Check("事件回调里读 CurrentPage 已经是这一页（顺序反了宿主就会记错页名）",
                currentPageReadyWhenFired, "");

            // 通知与执行是两跳：PageLoaded 只说"该显示这一页了"，动作要等宿主在首帧画完之后上报
            var firedHooks = new List<ScadaEventHook>();
            var firedPages = new List<ScadaPage>();
            rt2.PageEventRaised += (p, h) => { firedPages.Add(p); firedHooks.Add(h); };
            Check("宿主上报 Loaded：命中一条，带回来的就是画面上那条钩子（分发器只认钩子，不认画面）",
                rt2.RaisePageEvent(alarm, ScadaEventType.Loaded) == 1
                && firedHooks.Count == 1 && ReferenceEquals(firedPages[0], alarm)
                && ReferenceEquals(firedHooks[0], alarm.FindEventHook(ScadaEventType.Loaded)),
                $"{firedHooks.Count} 条");
            Check("事件与钩子对不上就不命中：Loaded 不许顺手把别的钩子一起跑掉",
                rt2.RaisePageEvent(alarm, ScadaEventType.Pressed) == 0 && firedHooks.Count == 1,
                $"{firedHooks.Count} 条");
            Check("报上来的不是当前页：不命中（切页途中旧页对象还活着，旧页的动作不许跑）",
                rt2.RaisePageEvent(home, ScadaEventType.Loaded) == 0 && firedHooks.Count == 1,
                $"{firedHooks.Count} 条");

            Check("连点第二次运行不会重新加载：「Loaded 只触发一次」这条承诺才算数",
                !rt2.Start() && hits == 1 && rt2.IsRunning, $"{hits} 次");

            // ---------------- ④ Stop 之后会话能干净复用 ----------------

            rt2.Stop();
            Check("Stop 复位会话：不再运行，也不残留当前页（残留会让下次 Stop 误判）",
                !rt2.IsRunning && rt2.CurrentPage == null, "");
            Check("Stop 之后页面对象虽还捏在手里，动作也不许再跑一遍：第一道闸门先把关",
                rt2.RaisePageEvent(alarm, ScadaEventType.Loaded) == 0 && firedHooks.Count == 1,
                $"{firedHooks.Count} 条");
            rt2.Stop();
            Check("重复 Stop 是空操作（关窗与切方案同时到达不会炸）", !rt2.IsRunning, "");
            Check("Stop 后再 Start：同一会话复用而不是新建，Loaded 会重新触发一次",
                rt2.Start() && hits == 2 && ReferenceEquals(rt2.CurrentPage, alarm), $"{hits} 次");
            rt2.Stop();

            // ---------------- ⑤ 启动画面被删掉之后的回落 ----------------

            doc.TryRemovePage(alarm, out _);
            var rt3 = new ScadaRuntime(doc);
            var rt3Hits = 0;
            rt3.PageLoaded += _ => rt3Hits++;
            Check("启动画面删除后自动回落到第一页：运行的是首页而不是那个已经不存在的页",
                rt3.Start() && ReferenceEquals(rt3.CurrentPage, home), rt3.CurrentPage?.Name ?? "null");
            Check("回落后照样收到一次通知：PageLoaded 不认配没配事件",
                rt3Hits == 1, $"{rt3Hits} 次");
            Check("但回落到的首页没配钩子，一条动作都跑不出来：勾选跟着画面走，不跟着位置走",
                rt3.RaisePageEvent(home, ScadaEventType.Loaded) == 0, "");

            // ---------------- ⑥ 构造入参 ----------------

            try
            {
                _ = new ScadaRuntime(null!);
                Check("文档传 null 直接抛 ArgumentNullException（没文档的会话是空转，别让它活着）", false, "没抛");
            }
            catch (ArgumentNullException)
            {
                Check("文档传 null 直接抛 ArgumentNullException（没文档的会话是空转，别让它活着）", true, "");
            }

            // ---------------- ⑦ 运行入口的可用性：命令刷新点必须成对交付 ----------------
            //
            // 这一组钉的是今晚真实踩到的坑：给命令写了 CanExecute，却没在它依赖的状态
            // 变化处显式 RaiseCanExecuteChanged()，结果"▶ 运行画面"永远灰着。
            // 本工程的 DelegateCommand 不跟随 CommandManager 自动重查（实测过：制造一次
            // 真实鼠标输入让 WPF 全局重查一遍，按钮照样灰）。所以漏了刷新点就是漏了，
            // 界面不会自愈——这种"不崩但永远点不动"的故障比崩溃更难发现，只能靠断言钉住。

            var editorWs = new WorkspaceContext();
            editorWs.GlobalVariables.Clear();

            var editorSol = new SolutionModel();
            editorSol.Flows.Clear();
            editorWs.SwitchSolution(editorSol);

            var noHost = new ScadaEditorVM(editorWs, null!);
            Check("没有运行宿主时入口置灰（与其点了没反应，不如压根不让点）",
                !noHost.RunPageCommand.CanExecute(), "");

            var fakeHost = new FakeRuntimeHost();
            var editor = new ScadaEditorVM(editorWs, fakeHost);
            Check("有宿主但方案一页都没有：入口仍然灰（弹一个空白全屏窗口比不弹更让人慌）",
                !editor.RunPageCommand.CanExecute(), $"{editor.Pages.Count} 页");

            editor.AddPageCommand.Execute();
            Check("新建画面之后入口立刻变亮——少了这次 raise 就是今晚那个置灰 bug",
                editor.RunPageCommand.CanExecute(), "");

            editor.AddPageCommand.Execute();
            Check("再建一页入口保持可用（刷新点被走第二遍不会把状态刷回 false）",
                editor.RunPageCommand.CanExecute(), "");

            var emptySol = new SolutionModel();
            emptySol.Flows.Clear();
            editorWs.SwitchSolution(emptySol);
            Check("切到空方案之后入口灰回去（换方案这条路也要刷，它和新建画面不是同一个入口）",
                !editor.RunPageCommand.CanExecute(), $"{editor.Pages.Count} 页");

            Check("入口的可用性判定没有副作用：置灰期间一次运行都没被启动过",
                fakeHost.Starts == 0, $"{fakeHost.Starts} 次");

            // 切回有画面的方案，真正点一次运行，核对交出去的到底是哪份文档
            editorWs.SwitchSolution(editorSol);
            Check("切回原方案入口重新变亮（换方案两条路都要刷：灰得过去，也要亮得回来）",
                editor.RunPageCommand.CanExecute(), $"{editor.Pages.Count} 页");

            editor.RunPageCommand.Execute();
            Check("点运行交出去的是内存里当前这份文档，不是磁盘上那份（刚改完点运行就该看见刚改的）",
                fakeHost.Starts == 1 && ReferenceEquals(fakeHost.LastDocument, editorSol.Scada),
                $"启动 {fakeHost.Starts} 次，文档 {fakeHost.LastDocument?.Pages.Count.ToString() ?? "null"} 页");
        }

        // ==================================================================
        //  [Y] 事件钩子 → 动作分发：命中规则、执行次序、跑不动时怎么交代
        //
        //  [W] 钉的是"会话该不该广播"，这一节钉的是"广播出去以后发生了什么"。
        //  两头都用真家伙：判定用零 WPF 的 ScadaRuntime，执行用生产那个
        //  ScadaActionDispatcher（它只依赖 ILogService 与 IScadaValueSource，控制台进程里可以直接 new；
        //  值源给一个不连硬件的替身，写变量那条链照样整条走完）。
        //  这里验的就是现场跑的那一套，不是为测试另写的替身。
        // ==================================================================
        private static void EventHookDispatchChecks()
        {
            Section("[Y] 事件钩子与动作分发：执行次序 + 未接入动作的措辞 + 脏标记");

            // ---------------- ① 一串动作：一行一条、按集合次序 ----------------

            var log = new LoggerStub();
            var values = new FakeValueSource();
            var startVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "启动", DataType = typeof(int) };
            values.ById[startVar.VariableId] = startVar;
            values.ByName[startVar.Name] = startVar;
            var dispatcher = new ScadaActionDispatcher(log, values);

            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "记一条" });
            hook.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = startVar.VariableId,
                VariableName = "启动",
                Value = "1",
            });
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "再记一条" });

            dispatcher.Dispatch(hook, "启动按钮");

            Check("三条动作出三行，且次序就是集合次序：日志本身就是执行流水",
                log.Lines.Count == 3, string.Join(" ⏎ ", log.Lines));
            Check("「记录日志」这条真执行了",
                log.Lines.Count == 3 && log.Lines[0] == "I [组态事件] 启动按钮 · 按下 → 记一条",
                log.Lines.FirstOrDefault() ?? "无");
            Check("「写变量」这条也真执行了（S6-6 已接通）：值按变量类型现转后写进变量，日志留一行成功",
                log.Lines.Count == 3
                && log.Lines[1] == "I [组态事件] 启动按钮 · 按下 → 写变量 启动 := 1"
                && Equals(startVar.Value, 1),
                $"{log.Lines[1]} / 变量值 {startVar.Value ?? "null"}");
            Check("中间那条不再「等版本」，后面的动作照跑（一条不成不中断一串：接口契约②）",
                log.Lines.Count == 3 && log.Lines[2] == "I [组态事件] 启动按钮 · 按下 → 再记一条",
                log.Lines.Skip(2).FirstOrDefault() ?? "无");

            // ---------------- ①B 写变量的四种写不成：四件事、四句话、两种级别 ----------------
            //
            // 分档的意义在现场：没选变量=配置漏了（去面板补）、找不到=改名或删了（去变量管理对）、
            // 转不过=值写错了、写不进=设备侧的事（离线/没配地址）。合成一句"写变量失败"，
            // 用户就得把这四条路各试一遍。前两档 Warn（软件没坏），后两档 Error（这次真没成）。

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values);

            var noTarget = new ScadaEventHook { Event = ScadaEventType.Pressed };
            noTarget.Actions.Add(new ScadaAction { Type = ScadaActionType.WriteVariable, Value = "1" });
            dispatcher.Dispatch(noTarget, "按钮");
            Check("①没选变量：Warn 一句「还没选变量」，不拿「写变量（未选变量）」再重复一遍",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].EndsWith("写变量：还没选变量，本条未执行"),
                log.Lines.FirstOrDefault() ?? "无");

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values);
            var ghost = new ScadaEventHook { Event = ScadaEventType.Pressed };
            ghost.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = Guid.NewGuid(),
                VariableName = "已改名的变量",
                Value = "1",
            });
            dispatcher.Dispatch(ghost, "按钮");
            Check("②找不到变量（改名/删了）：Warn 把变量名带出来，用户才知道去变量管理里对哪个名字",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].Contains("写变量 已改名的变量 := 1")
                && log.Lines[0].EndsWith("找不到变量「已改名的变量」，本条未执行"),
                log.Lines.FirstOrDefault() ?? "无");

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values);
            var badValue = new ScadaEventHook { Event = ScadaEventType.Pressed };
            badValue.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = startVar.VariableId,
                VariableName = "启动",
                Value = "abc",
            });
            dispatcher.Dispatch(badValue, "按钮");
            Check("③值转不过去（\"abc\" 写不进 int）：Error，且原因由转换器出（带目标类型名，可直接照做）",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("E ")
                && log.Lines[0].Contains("写变量 启动 := abc")
                && log.Lines[0].Contains("无法转换为 Int32"),
                log.Lines.FirstOrDefault() ?? "无");

            var offlineVar = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(),
                Name = "离线设备状态",
                DataType = typeof(int),
                WriteError = "设备未连接",
            };
            var offlineValues = new FakeValueSource();
            offlineValues.ById[offlineVar.VariableId] = offlineVar;
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, offlineValues);
            var offline = new ScadaEventHook { Event = ScadaEventType.Pressed };
            offline.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = offlineVar.VariableId,
                VariableName = "离线设备状态",
                Value = "1",
            });
            dispatcher.Dispatch(offline, "按钮");
            Check("④写不进（设备侧的事）：Error 且把变量自己给的原因原样带上来，不包一层「写入失败」把它盖掉",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("E ")
                && log.Lines[0].Contains("写变量 离线设备状态 := 1")
                && log.Lines[0].EndsWith("写入失败：设备未连接"),
                log.Lines.FirstOrDefault() ?? "无");

            // ---------------- ② 三种"什么都不该发生"的情况 ----------------

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, new FakeValueSource());

            dispatcher.Dispatch(null, "启动按钮");
            dispatcher.Dispatch(new ScadaEventHook { Event = ScadaEventType.Pressed }, "启动按钮");
            Check("钩子为 null、动作表为空：一行都不留（「没配动作」由空集合表达，不是错误）",
                log.Lines.Count == 0, string.Join(" ⏎ ", log.Lines));

            var blank = new ScadaEventHook { Event = ScadaEventType.Loaded };
            blank.Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
            dispatcher.Dispatch(blank, "   ");
            Check("日志内容留空 → 文案回落成「对象 · 事件」：新建一条动作当场就能用，不必先逼用户填话",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 未命名对象 · 加载完成",
                string.Join(" ⏎ ", log.Lines));

            // 下面两组各换一份新账本：上一条案例的日志不许混进这一条的证词里
            var withNull = new ScadaEventHook { Event = ScadaEventType.Released };
            withNull.Actions.Add(null!);
            withNull.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "坏数据后面这条" });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values);
            dispatcher.Dispatch(withNull, "按钮");
            Check("动作串里混进 null（手工改坏的 .vms 反序列化会原样塞进来）：跳过它，别把整串废掉",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 按钮 · 释放 → 坏数据后面这条",
                string.Join(" ⏎ ", log.Lines));

            var future = new ScadaEventHook { Event = ScadaEventType.Pressed };
            future.Actions.Add(new ScadaAction { Type = (ScadaActionType)99 });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values);
            dispatcher.Dispatch(future, "按钮");
            Check("高版本存下的动作类型读进本版本：警告一句、不抛、不执行（枚举按数值反序列化，本就不校验取值）",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].Contains("未知动作(99)") && log.Lines[0].Contains("本版本不认识该动作"),
                string.Join(" ⏎ ", log.Lines));

            // ---------------- ③ 动作执行中途抛异常（契约①不许外抛 + 契约②不中断） ----------------

            var faulty = new FaultyLogger("第二条");
            var faulted = new ScadaEventHook { Event = ScadaEventType.Pressed };
            faulted.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "第一条" });
            faulted.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "第二条" });
            faulted.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "第三条" });

            try
            {
                new ScadaActionDispatcher(faulty, values).Dispatch(faulted, "急停按钮");
                Check("动作内部抛异常绝不冒到调用方（调用点在 UI 线程的鼠标链上，抛出=操作员点一下就崩）",
                    true, "");
            }
            catch (Exception ex)
            {
                Check("动作内部抛异常绝不冒到调用方（调用点在 UI 线程的鼠标链上，抛出=操作员点一下就崩）",
                    false, ex.GetType().Name);
            }

            Check("炸掉的那条就地留一行 Error（带动作原文），前后两条照跑——三行齐，不是两行也不是三行成功",
                faulty.Lines.Count == 3
                && faulty.Lines[0] == "I [组态事件] 急停按钮 · 按下 → 第一条"
                && faulty.Lines[1].StartsWith("E ")
                && faulty.Lines[1].Contains("记录日志「第二条」")
                && faulty.Lines[1].Contains("执行失败：日志落地失败")
                && faulty.Lines[2] == "I [组态事件] 急停按钮 · 按下 → 第三条",
                string.Join(" ⏎ ", faulty.Lines));

            // ---------------- ④ 会话的图元闸门：一条都不能少 ----------------

            var doc = new ScadaDocument();
            var main = doc.AddPage("主控");
            var aside = doc.AddPage("别处");
            var button = new ScadaElement { TypeKey = "Hmi.Button", Name = "启动按钮" };
            main.Elements.Add(button);
            button.GetOrAddEventHook(ScadaEventType.Pressed)
                  .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "按下了" });

            var rt = new ScadaRuntime(doc);
            var fired = new List<(ScadaElement, ScadaEventHook)>();
            rt.ElementEventRaised += (e, h) => fired.Add((e, h));

            Check("没在运行就不执行动作：RaiseElementEvent 命中 0 条（设计期点按钮不该有反应，这条靠闸门而不是靠不接线）",
                rt.RaiseElementEvent(button, ScadaEventType.Pressed) == 0 && fired.Count == 0,
                $"{fired.Count} 条");

            rt.Start();

            Check("运行中按下：命中一条，回来的图元与钩子都是模型本身（分发器不需要再去找谁出的事）",
                rt.RaiseElementEvent(button, ScadaEventType.Pressed) == 1
                && fired.Count == 1 && ReferenceEquals(fired[0].Item1, button)
                && ReferenceEquals(fired[0].Item2, button.FindEventHook(ScadaEventType.Pressed)),
                $"{fired.Count} 条");
            Check("事件对不上不命中：Pressed 的钩子不许被 Released 顺手跑掉",
                rt.RaiseElementEvent(button, ScadaEventType.Released) == 0 && fired.Count == 1,
                $"{fired.Count} 条");

            // 面板上勾了事件、还没往里加动作，是配到一半的常态。执行侧当作没配：
            // 否则每次这种半成品配置都会冒一行"[组态事件] xxx · 释放"，日志被空口白话灌满。
            button.AddEventHook(ScadaEventType.Released);
            Check("钩子配了但动作表为空 = 没配：第三道闸门拦住，一行都不广播",
                rt.RaiseElementEvent(button, ScadaEventType.Released) == 0 && fired.Count == 1,
                $"{fired.Count} 条");

            var orphan = new ScadaElement { TypeKey = "Hmi.Button", Name = "别处的按钮" };
            orphan.GetOrAddEventHook(ScadaEventType.Pressed)
                  .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "不该响" });
            aside.Elements.Add(orphan);
            Check("不在当前画面上的图元不命中：切页途中旧页控件还没拆干净，旧页的动作不许跑",
                rt.RaiseElementEvent(orphan, ScadaEventType.Pressed) == 0 && fired.Count == 1,
                $"{fired.Count} 条");
            Check("图元传 null 直接收手（画布上被删掉的图元仍可能有一记鼠标抬起到达）",
                rt.RaiseElementEvent(null, ScadaEventType.Pressed) == 0 && fired.Count == 1,
                $"{fired.Count} 条");

            // 同一个事件配两条钩子（手工改文件、复制图元带过来）：按集合次序都跑，不报错也不合并
            var secondHook = button.AddEventHook(ScadaEventType.Pressed);
            secondHook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "第二条钩子" });
            Check("同一事件两条钩子：按集合次序各命中一次（口径见 ScadaEventHook 类注释）",
                rt.RaiseElementEvent(button, ScadaEventType.Pressed) == 2 && fired.Count == 3
                && ReferenceEquals(fired[1].Item2, button.FindEventHook(ScadaEventType.Pressed))
                && ReferenceEquals(fired[2].Item2, secondHook),
                $"{fired.Count} 条");

            rt.Stop();
            Check("Stop 之后再报：一条都不跑（画面已经关了，动作还在写日志就是幽灵）",
                rt.RaiseElementEvent(button, ScadaEventType.Pressed) == 0 && fired.Count == 3,
                $"{fired.Count} 条");

            // ---------------- ⑤ 画面勾选框 ↔ 钩子：投影不落地、脏标记不失真 ----------------

            var page = new ScadaPage { Name = "画面" };
            int version0 = page.Version;
            Check("投影字段读的是钩子表：一条钩子都没配时就是没勾",
                !page.EnableLoadedEvent && page.FindEventHook(ScadaEventType.Loaded) == null, "");

            page.EnableLoadedEvent = true;
            Check("勾上「加载事件」会长出一条带默认动作的钩子（空动作表在执行侧等同没配，不补就是「勾了却什么都没发生」）",
                page.FindEventHook(ScadaEventType.Loaded) is { Actions.Count: 1 } loadedHook
                && loadedHook.Actions[0].Type == ScadaActionType.Log, "");
            Check("勾选把画面标脏（Version 变大，编辑器才知道要提示保存）",
                page.Version > version0, $"{version0} → {page.Version}");

            int version1 = page.Version;
            page.EnableLoadedEvent = true;
            Check("重复勾一次不加钩子、也不空转版本号（否则点开面板再关掉就成了「有改动」）",
                page.EventHooks.Count == 1 && page.Version == version1,
                $"{page.EventHooks.Count} 条 / {page.Version}");

            page.GetOrAddEventHook(ScadaEventType.Loaded).Actions.Add(new ScadaAction());
            Check("往钩子里加一条动作同样标脏：动作是画面数据的一部分，漏了它保存就会被悄悄吞掉",
                page.Version > version1 && page.GetOrAddEventHook(ScadaEventType.Loaded).Actions.Count == 2,
                $"{version1} → {page.Version}");

            page.EnableLoadedEvent = false;
            Check("取消勾选整条摘掉（连它下面的动作一起），画面重新变脏",
                page.EnableLoadedEvent == false && page.EventHooks.Count == 0 && version1 < page.Version,
                $"{page.EventHooks.Count} 条");
            int version2 = page.Version;
            page.EnableLoadedEvent = false;
            Check("本来就没配时再取消一次不刷版本号（RemoveEventHook 用返回值区分「删了」与「没得删」）",
                page.Version == version2, $"{version2} → {page.Version}");

            // 订阅泄漏的反证：Clear() 走 Reset 分支拿不到被移除的是谁，只照事件参数摘就会漏摘
            var leakedHook = page.GetOrAddEventHook(ScadaEventType.Loaded);
            var doomed = new ScadaAction { Type = ScadaActionType.Log, Text = "将被清空" };
            leakedHook.Actions.Add(doomed);
            leakedHook.Actions.Clear();
            int version3 = page.Version;
            doomed.Text = "清空之后的改动";
            Check("Actions.Clear() 之后改那条已被移走的动作：画面不再变脏（订阅真摘干净了，没泄漏）",
                page.Version == version3, $"{version3} → {page.Version}");

            // ---------------- ⑥ 变量改名级联覆盖到画面钩子 ----------------

            var variableId = Guid.NewGuid();
            var cascadePage = new ScadaPage { Name = "级联" };
            cascadePage.GetOrAddEventHook(ScadaEventType.Loaded)
                       .Actions.Add(new ScadaAction
                       {
                           Type = ScadaActionType.WriteVariable,
                           VariableId = variableId,
                           VariableName = "旧名",
                           Value = "1",
                       });
            cascadePage.Elements.Add(new ScadaElement { TypeKey = "Hmi.Button", Name = "无关按钮" });

            int changed = cascadePage.RefreshVariableReferences(variableId, "旧名", "新名");
            var written = cascadePage.FindEventHook(ScadaEventType.Loaded)!.Actions[0];
            Check("画面钩子里的动作也吃改名级联：按 Id 命中后换名并回填 Id（漏了它，改个名动作就指向不存在的变量）",
                changed == 1 && written.VariableName == "新名" && written.VariableId == variableId,
                $"{changed} 处 / 名字={written.VariableName}");

            // ---------------- ⑦ 事件清单只声明"真能触发"的那几条 ----------------
            //
            // 这条钉的是 ScadaEventType 类注释里那句承诺：Unloaded 要等 S8、ValueChanged 要等 S6，
            // 接上之前谁把它们写进清单，用户配上的就是个永远不响的钩子。
            // 哪天补齐了触发源，改这条断言的白名单，别改描述符——顺序反了就会先长出哑配置。

            var premature = new List<string>();
            foreach (var descriptor in ElementRegistry.All)
            {
                foreach (var declared in descriptor.Events)
                {
                    if (declared != ScadaEventType.Pressed && declared != ScadaEventType.Released)
                        premature.Add($"{descriptor.TypeKey} → {declared}");
                }
            }

            Check("图元描述符只声明当前真能触发的事件（Pressed/Released）：清单里不许有发不出来的事件",
                premature.Count == 0, string.Join(" | ", premature));
        }

        /// <summary>
        /// 会挑一条消息抛异常的日志桩——用来把"动作执行到一半炸了"这条路走一遍。
        /// 真实场景里最常见的炸法是日志本身落地失败（磁盘满、文件被占用）与 S6 之后的类型转换，
        /// 分发器不能因为一次抛出就把整串动作和操作员的那一点都带走。
        /// </summary>
        private sealed class FaultyLogger : ILogService
        {
            private readonly string _trigger;

            public List<string> Lines { get; } = new();

            public FaultyLogger(string trigger) => _trigger = trigger;

            public void Success(params string[] messages) => Record("S", messages);

            public void Error(params Exception[] messages) => Record("E", messages.Select(m => m.Message).ToArray());

            public void Error(params string[] messages) => Record("E", messages);

            public void Warn(params string[] messages) => Record("W", messages);

            public void Info(params string[] messages)
            {
                if (messages.Any(m => m.Contains(_trigger)))
                    throw new IOException("日志落地失败");

                Record("I", messages);
            }

            private void Record(string level, string[] messages)
            {
                foreach (var message in messages)
                    Lines.Add(level + " " + message);
            }
        }

        // ==================================================================
        //  [X] 画布真实渲染：视口小于画面时画面尺寸不被夹取 + 位图像素采样（在独立 STA 线程内执行）
        //
        //  这一节是"看图猜 bug"的终结者。之前排查"画布上少了两个图元"只能对着截图数像素，
        //  量了三轮还得不出一致结论（渲染出来的东西受缩放、裁剪、底色、字体一堆因素影响，
        //  肉眼判断不可靠）。这里改成：把画布按真实数据装配好 → RenderTargetBitmap 渲染成位图
        //  → 直接采样设计坐标上那几个点的颜色。图元画没画出来，一个像素说了算。
        //
        //  两件事分开钉：
        //  ① 尺寸契约：PART_Surface 的宽高是代码按 PageWidth/PageHeight 写死的显式值，
        //     视口比画面小的时候它不许缩水（缩了画面就只剩一块取景框，底色都会缺一角）。
        //  ② 渲染契约：图元在自己那一格上必须留下颜色，且空文本的图元照样有布局尺寸——
        //     "看不见"和"没摆上去"是两种故障，混在一起下次又要靠猜。
        // ==================================================================
        private static void ViewportRenderChecks()
        {
            Section("[X] 画布渲染：尺寸不被夹取 + 取景不被推离视口 + 位图像素采样");

            Exception? failure = null;

            // 样板与 [T]/[U] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunViewportRenderChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("渲染断言全程未抛异常", false, failure.ToString());
        }

        private static void RunViewportRenderChecks()
        {
            // ---------------- 装配：照搬落盘方案里那份真实数据 ----------------

            var doc = new ScadaDocument();
            var page = doc.AddPage("画面_1");
            page.Width = 1920;
            page.Height = 1080;
            page.Background = "#FF1E1E1E";

            var layer = page.Layers.FirstOrDefault() ?? page.AddLayer();

            // 属性袋一律留空：这正是真机上那份方案的形态（Properties = {}），
            // 外观全靠描述符默认值兜出来，顺带把那条回落链一起验了。
            var ellipse = AddRenderElement(page, layer, "Hmi.Ellipse", 580, 310, 100, 100);
            var text = AddRenderElement(page, layer, "Hmi.Text", 570, 550, 120, 24);
            var button = AddRenderElement(page, layer, "Hmi.Button", 180, 110, 88, 32);
            var rect = AddRenderElement(page, layer, "Hmi.Rectangle", 190, 290, 120, 60);

            // S7 新增的四类图元也一并进像素采样：它们各自代表一种"外观由输入推导"的图元
            // （棒图算长度、多态灯挑颜色、数值域排版、时钟走时）。属性袋照样留空，
            // 只在需要"看得出效果"的地方补一个输入值（值为 0 的棒图本来就该是空的）。
            var bar = AddRenderElement(page, layer, "Hmi.ProgressBar", 180, 200, 160, 20);
            bar.SetProperty("Value", "50");
            var lamp = AddRenderElement(page, layer, "Hmi.Lamp", 400, 200, 40, 56);
            lamp.SetProperty("State", "Alarm");
            var clock = AddRenderElement(page, layer, "Hmi.Clock", 400, 420, 180, 28);
            var field = AddRenderElement(page, layer, "Hmi.IOField", 180, 420, 140, 28);
            // 表盘同样"外观由输入推导"，而且是唯一一个"几何随尺寸重算"的图元
            // （盘面是内接圆、刻度按圆周算），所以必须真画一遍才算交付。
            // 值留默认 0 → 指针指向左下 135°，数值文字压在底部，采样点据此避让。
            var gauge = AddRenderElement(page, layer, "Hmi.Gauge", 600, 700, 140, 140);

            // 阀门也是"外观由输入推导"（开度 → 状态色 + 裁剪宽度）。给 50 这个中间值：
            // 左半边该是中间位蓝、右半边仍是空腔灰，两个采样点各钉一头。
            var valve = AddRenderElement(page, layer, "Hmi.Valve", 820, 500, 72, 64);
            valve.SetProperty("Opening", "50");

            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };

            // 画布工厂：同一份数据要装配两个实例。_centeredOnce 是单向闩锁，
            // "首次布局"这条路一个实例只能走一次，复用同一个实例验不出东西。
            ScadaCanvas NewCanvas()
            {
                var canvas = new ScadaCanvas
                {
                    PageWidth = page.Width,
                    PageHeight = page.Height,
                    PageBackground = (Brush)new BrushConverter().ConvertFromString(page.Background)!,
                    ItemsSource = page.Elements,
                    Page = page,
                    ShowGrid = false,
                    SnapToGrid = false,
                };
                canvas.Style = (Style)theme[typeof(ScadaCanvas)];
                return canvas;
            }

            var host = NewCanvas();
            bool templated = host.ApplyTemplate();

            // ---------------- ① 尺寸契约：视口 800×600 装不下 1920×1080 ----------------

            const double viewportW = 800, viewportH = 600;
            host.Measure(new Size(viewportW, viewportH));
            host.Arrange(new Rect(0, 0, viewportW, viewportH));
            host.UpdateLayout();

            var surface = host.Template?.FindName("PART_Surface", host) as FrameworkElement;
            Check("视口远小于画面时，PART_Surface 仍是整幅画面的尺寸（显式宽高没被视口约束夹成取景框）",
                templated && surface != null
                && Math.Abs(surface!.RenderSize.Width - page.Width) < 0.5
                && Math.Abs(surface.RenderSize.Height - page.Height) < 0.5,
                surface == null ? "拿不到 PART_Surface"
                    : $"{surface.RenderSize.Width:0.#}×{surface.RenderSize.Height:0.#}，应为 {page.Width}×{page.Height}");

            // ---------------- ①-2 取景契约：画面比视口大时不能"居中" ----------------

            // 真机踩过的坑：1920×1080 的画面配 ~1560×800 的编辑区，CenterPage 套
            // (视口 - 画面 × Zoom) / 2 算出<b>负数</b> Offset，把画面左上角推到视口左上角之外，
            // 5 个图元全部落在可视区外，只剩贴着视口左边缘的一条窄带 —— 用户看到的就是
            // "画布空白、图元不见了"。而 _centeredOnce 是单向闩锁，写坏一次就永不再纠正。
            //
            // 这里刻意走「1:1」这条公开路径（Zoom=1 + CenterPage）而不是只看首次布局：
            // OnRenderSizeChanged 是否在断言环境里触发并不确定，靠它会让这条断言在
            // 回归时"假装通过"。ZoomToActualSize 是确定的、也是用户复现该症状的那条路。
            host.ZoomToActualSize();
            host.UpdateLayout();

            Check("画面比视口大时取景贴左上（负 Offset 会把图元整体推出视口）",
                host.Offset.X >= 0 && host.Offset.Y >= 0,
                $"Offset=({host.Offset.X:0.#},{host.Offset.Y:0.#})，画面 {page.Width}×{page.Height} 配视口 {viewportW}×{viewportH}");

            // 取景对不对，最终要落到"图元看不看得见"上。左上区的图元必须落在视口内。
            var buttonOrigin = host.ToViewportPoint(new Point(button.X, button.Y));
            Check("1:1 取景后左上区图元落在视口内（打开画面第一眼就该看得见图元）",
                buttonOrigin.X >= 0 && buttonOrigin.Y >= 0
                && buttonOrigin.X < viewportW && buttonOrigin.Y < viewportH,
                $"按钮左上角视口坐标=({buttonOrigin.X:0.#},{buttonOrigin.Y:0.#})，视口 {viewportW}×{viewportH}");

            // ---------------- ①-3 首次布局契约：真机启动走的就是这条路 ----------------

            // ①-2 走的是"1:1"按钮那条路；真机启动时用户没点任何按钮，走的是
            // OnRenderSizeChanged 里那一次"只做一次"的居中（_centeredOnce）。
            // 它是单向闩锁——写坏一次永不再纠正，所以必须单独钉，不能指望 ①-2 代劳。
            //
            // 视口尺寸照抄真机编辑区：UIA 报 ScadaEditorView = 1536×1167 物理像素，
            // 本机 150% DPI → 1024×778 DIP；画面 1920×1080 依然放不下。
            var firstLayout = NewCanvas();
            firstLayout.ApplyTemplate();

            const double editorW = 1024, editorH = 778; // 真机编辑区（DIP）
            firstLayout.Measure(new Size(editorW, editorH));
            firstLayout.Arrange(new Rect(0, 0, editorW, editorH));
            firstLayout.UpdateLayout();

            Check("首次布局（真机编辑区尺寸）后取景贴左上，不把图元推出视口",
                firstLayout.Offset.X >= 0 && firstLayout.Offset.Y >= 0,
                $"Offset=({firstLayout.Offset.X:0.#},{firstLayout.Offset.Y:0.#})，"
                + $"编辑区 {editorW}×{editorH}，画面 {page.Width}×{page.Height}");

            var firstButton = firstLayout.ToViewportPoint(new Point(button.X, button.Y));
            Check("首次布局后左上区图元落在真机编辑区内（打开组态面板第一眼就该看得见按钮）",
                firstButton.X >= 0 && firstButton.Y >= 0
                && firstButton.X < editorW && firstButton.Y < editorH,
                $"按钮左上角视口坐标=({firstButton.X:0.#},{firstButton.Y:0.#})，编辑区 {editorW}×{editorH}");

            // ---------------- ② 渲染契约：缩到 0.4 倍，整幅画面落进视口，逐点采样 ----------------

            host.Zoom = 0.4;
            host.Offset = new Point(0, 0);
            host.UpdateLayout();

            var frame = RenderToBitmap(host, (int)viewportW, (int)viewportH);

            // 设计坐标 → 视口像素（约定：视口 = 设计 × Zoom + Offset），采样点由这条公式反推，
            // 不写死像素，免得缩放一改整节断言集体说谎。
            Check("画面右下角仍是画面底色（底色铺满整幅，没被布局槽裁成一小块）",
                SameColor(Sample(frame, DesignToViewport(page.Width - 20, page.Height - 20)), page.Background),
                ColorName(Sample(frame, DesignToViewport(page.Width - 20, page.Height - 20))));
            Check("视口里画面外的灰边是画布底色 #FF141414（与画面底色差一档，边界一眼分得清）",
                SameColor(Sample(frame, new Point(795, 595)), "#FF141414"),
                ColorName(Sample(frame, new Point(795, 595))));

            Check("椭圆中心被真的画上（白填充压深底，差一个像素都不算过）",
                NearWhite(Sample(frame, DesignToViewport(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height / 2))));
            Check("矩形中心被真的画上",
                NearWhite(Sample(frame, DesignToViewport(rect.X + rect.Width / 2, rect.Y + rect.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(rect.X + rect.Width / 2, rect.Y + rect.Height / 2))));
            // 采样点刻意取按钮左侧、文字之外的填充区：按钮默认白字"按钮"压在中心，
            // 采正中心会落在字形像素上，混出淡蓝 #FECBDEF3 被误判成"没画上"（这条伪失败踩过一次）。
            Check("按钮填充区被真的画上（品牌蓝，与白底矩形、深底画面都拉得开）",
                IsBlue(Sample(frame, DesignToViewport(button.X + 6, button.Y + button.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(button.X + 6, button.Y + button.Height / 2))));

            // 文本图元没勾进像素断言：它 Text="" 且前景压深底，本来就"该看不见"。
            // 这里要钉的是它<b>摆没摆上去</b>——布局尺寸非零就是摆了，看不见是内容问题，不是渲染漏项。
            var textHost = FindContainer(host, text);
            Check("空文本图元照样有布局尺寸（\"看不见\"与\"没摆上去\"是两回事，分开钉）",
                textHost != null && textHost.RenderSize.Width > 0 && textHost.RenderSize.Height > 0,
                textHost == null ? "元素层里没有它的容器" : $"{textHost.RenderSize.Width}×{textHost.RenderSize.Height}");

            // ---------------- ②-2 S7 新增图元：外观由输入推导，画得出来才算交付 ----------------

            // 棒图：值 50 / 量程 0~100 → 左半段填充蓝、右半段仍是空槽灰。
            // 采样点取左端 6 设计像素处，避开压在条中央的数值文字（与按钮那条同一纪律）。
            Check("棒图：已填充那一段被真的画上（品牌蓝，与空槽灰、深底都拉得开）",
                IsBlue(Sample(frame, DesignToViewport(bar.X + 6, bar.Y + bar.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(bar.X + 6, bar.Y + bar.Height / 2))));
            Check("棒图：未填充那一段仍是空槽色（填充块没把整条铺满，比例才看得出来）",
                SameColor(Sample(frame, DesignToViewport(bar.X + bar.Width - 6, bar.Y + bar.Height / 2)), "#FF3A3A3A"),
                ColorName(Sample(frame, DesignToViewport(bar.X + bar.Width - 6, bar.Y + bar.Height / 2))));

            // 多态灯：State=Alarm → 灯体报警红。采样点取灯体正中——
            // 标签行为空时高度塌成 0，正中必然落在灯体里（与时钟模板同一条塌行口径）。
            Check("多态灯：报警态的灯体被真的画上（红灯压深底，操作员扫一眼先认颜色）",
                SameColor(Sample(frame, DesignToViewport(lamp.X + lamp.Width / 2, lamp.Y + lamp.Height / 2)), "#FFE03A2B"),
                ColorName(Sample(frame, DesignToViewport(lamp.X + lamp.Width / 2, lamp.Y + lamp.Height / 2))));

            // 数值域：默认白底 + 深灰边框。采样点取框内左侧——说明字为空时那一列塌掉、
            // 数值又是右对齐，"左侧留白"正是底色最纯的地方。
            Check("数值域：框内底色被真的画上（白底压深底，一列数值域在深色画面上才立得住）",
                NearWhite(Sample(frame, DesignToViewport(field.X + 10, field.Y + field.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(field.X + 10, field.Y + field.Height / 2))));

            // 时钟模板里只有两行文字，没有可采样的"面"（前景深灰压在深底上，本来就该看不清）——
            // 与文本图元同一条口径：要钉的是它摆没摆上去、布局尺寸是不是真的。
            var clockHost = FindContainer(host, clock);
            Check("时钟图元照样有布局尺寸（时间串在断言环境里不跳，\"看不见\"与\"没摆上去\"要分开钉）",
                clockHost != null && clockHost.RenderSize.Width > 0 && clockHost.RenderSize.Height > 0,
                clockHost == null ? "元素层里没有它的容器" : $"{clockHost.RenderSize.Width}×{clockHost.RenderSize.Height}");

            // 表盘：盘面是内接圆，圆外四个角本来就该露底色，所以采样点必须落在圆内、
            // 又要避开指针（默认值 0 → 指向左下 135°）、刻度弧（0.82r 之外）与压在底部的数值文字。
            // 取"圆心往左上 45°、半径 0.55 处"：三个可见部分都够不着那里。
            double gaugeCx = gauge.X + gauge.Width / 2;
            double gaugeCy = gauge.Y + gauge.Height / 2;
            double gaugeR = gauge.Width / 2 - 1.5; // 与控件同一条账：半径 = 短边/2 −（半线宽 + 1）
            var gaugeSpot = DesignToViewport(
                gaugeCx - gaugeR * 0.55 * Math.Sqrt(0.5),
                gaugeCy - gaugeR * 0.55 * Math.Sqrt(0.5));

            Check("表盘：盘底色被真的画上（指针与刻度都算在代码里，盘面本身也得看得见）",
                SameColor(Sample(frame, gaugeSpot), "#FF3A3A3A"), ColorName(Sample(frame, gaugeSpot)));

            // 阀门：蝴蝶结是左右两个尖角相对的三角形，开度 50 时裁剪矩形（宽 = 可画宽 × 0.5）
            // 正好推进到正中的尖角处——所以"左半蓝、右半灰"就是这张图唯一要看的东西。
            // 采样点取中腰那一行（local y = Height/2）：两个三角形在那里的横截面最宽，
            // 而上下两个凹口里的文字（位号贴顶、开度贴底）与正中的尖角都够不着这一行。
            Check("阀门：已开启那半边被真的画上（中间位蓝，与空腔灰、深底都拉得开）",
                IsBlue(Sample(frame, DesignToViewport(valve.X + 8, valve.Y + valve.Height / 2))),
                ColorName(Sample(frame, DesignToViewport(valve.X + 8, valve.Y + valve.Height / 2))));
            Check("阀门：未开启那半边仍是空腔色（填充没把整个阀体铺满，开度才看得出来）",
                SameColor(Sample(frame, DesignToViewport(valve.X + valve.Width - 8, valve.Y + valve.Height / 2)), "#FF3A3A3A"),
                ColorName(Sample(frame, DesignToViewport(valve.X + valve.Width - 8, valve.Y + valve.Height / 2))));

            // ---------------- ③ 留一份证据图，供人眼复核（不参与判定） ----------------

            string dir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(dir);
            string png = Path.Combine(dir, "scada-canvas-render.png");
            SavePng(frame, png);
            Console.WriteLine($"        （渲染证据：{png}）");
        }

        /// <summary>往画面里放一个图元，坐标尺寸写死成断言要的形态，并归入给定图层。</summary>
        private static ScadaElement AddRenderElement(
            ScadaPage page, ScadaLayer? layer, string typeKey, double x, double y, double width, double height)
        {
            var element = ElementRegistry.CreateElement(typeKey, x, y)!;
            element.Width = width;
            element.Height = height;
            element.ZIndex = page.Elements.Count + 1;
            page.Elements.Add(element);
            if (layer != null)
                page.TryAssignLayer(element, layer, out _);
            return element;
        }

        /// <summary>设计坐标 → 视口像素（与画布同一约定：视口 = 设计 × Zoom + Offset）</summary>
        private static Point DesignToViewport(double x, double y)
            => new Point(x * 0.4, y * 0.4);

        /// <summary>从元素层里找出某个图元对应的容器控件。</summary>
        private static FrameworkElement? FindContainer(ScadaCanvas host, ScadaElement element)
        {
            var layer = host.Template?.FindName("PART_ElementLayer", host) as Panel;
            if (layer == null)
                return null;

            foreach (var child in layer.Children)
            {
                if (child is ScadaElementBase control && ReferenceEquals(control.Element, element))
                    return control;
                if (child is FrameworkElement placeholder && ReferenceEquals(placeholder.Tag, element))
                    return placeholder;
            }

            return null;
        }

        /// <summary>把可视元素离屏渲染成位图（STA 线程、已 Measure/Arrange 的前提下可用）。</summary>
        private static RenderTargetBitmap RenderToBitmap(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>采样一个像素（越界返回透明，让断言红掉而不是抛）。</summary>
        private static Color Sample(RenderTargetBitmap bitmap, Point pixel)
        {
            int x = (int)Math.Round(pixel.X), y = (int)Math.Round(pixel.Y);
            if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight)
                return Colors.Transparent;

            var buffer = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), buffer, 4, 0);
            return Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);
        }

        private static void SavePng(RenderTargetBitmap bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static bool SameColor(Color actual, string argbHex)
        {
            var expected = (Color)ColorConverter.ConvertFromString(argbHex)!;
            return Distance(actual, expected) < 8;
        }

        private static bool NearWhite(Color actual)
            => actual.R > 240 && actual.G > 240 && actual.B > 240 && actual.A > 240;

        private static bool IsBlue(Color actual)
            => actual.B > 150 && actual.B - actual.R > 60;

        private static double Distance(Color a, Color b)
            => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A);

        private static string ColorName(Color color)
            => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>深度优先找第一个满足条件的可视子元素（找不到返回 null）。</summary>
        private static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit && match(hit))
                    return hit;

                var found = FindVisual(child, match);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>深度优先收集所有满足条件的可视子元素。</summary>
        private static List<T> CollectVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            var result = new List<T>();
            Collect(root);
            return result;

            void Collect(DependencyObject node)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is T hit && match(hit))
                        result.Add(hit);
                    Collect(child);
                }
            }
        }

        /// <summary>把元素在祖先坐标系里的占位换算成矩形（用于按元素位置去位图上采样）。</summary>
        private static Rect BoundsIn(FrameworkElement element, Visual ancestor)
            => element.TransformToAncestor(ancestor)
                .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

        /// <summary>
        /// 元素连同祖先都没被 Collapsed 掉。离屏渲染时 IsVisible 恒为 false（没接到 PresentationSource），
        /// 所以"用户看不看得见"只能自己沿视觉树往上问 Visibility。
        /// </summary>
        private static bool IsShown(DependencyObject node, DependencyObject root)
        {
            for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
                if (ReferenceEquals(cur, root))
                    break;
            }

            return true;
        }

        private static string RectText(Rect rect)
            => $"({rect.X:0},{rect.Y:0}) {rect.Width:0}×{rect.Height:0}";

        /// <summary>
        /// 指定矩形内是否出现过某个颜色（容差按 RGBA 四通道差值之和算）。
        /// 用于"这片蓝到底画出来没有"这类判定——比按单点采样抗锯齿、抗半像素错位。
        /// </summary>
        private static bool HasColorIn(RenderTargetBitmap bitmap, Rect area, string argbHex, int tolerance = 8)
        {
            var target = (Color)ColorConverter.ConvertFromString(argbHex)!;

            int x0 = Math.Max(0, (int)Math.Floor(area.Left));
            int y0 = Math.Max(0, (int)Math.Floor(area.Top));
            int x1 = Math.Min(bitmap.PixelWidth - 1, (int)Math.Ceiling(area.Right) - 1);
            int y1 = Math.Min(bitmap.PixelHeight - 1, (int)Math.Ceiling(area.Bottom) - 1);

            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            if (w <= 0 || h <= 0)
                return false;

            var buffer = new byte[w * h * 4];
            bitmap.CopyPixels(new Int32Rect(x0, y0, w, h), buffer, w * 4, 0);

            for (int i = 0; i < buffer.Length; i += 4)
            {
                var color = Color.FromArgb(buffer[i + 3], buffer[i + 2], buffer[i + 1], buffer[i]);
                if (color.A > 0 && Distance(color, target) < tolerance)
                    return true;
            }

            return false;
        }

        // ==================================================================
        //  [Z] 运行态数据泵：变量值变化 → 图元属性变化（在独立 STA 线程内执行）
        //
        //  这是 S6 的正题，也是整条运行态链路上唯一"每 20ms 都要跑一遍"的热路径。
        //  除了"变量"是假的，其余全是生产代码：建表与刷帧走 ScadaRuntimeBinder，
        //  落值走 ScadaElementBase.TryApplyRuntimeValue + ScadaValueConverter，
        //  打点走 ScadaDiagnosticOverlay。变量必须假——要验的是"值变了以后发生了什么"，
        //  而"什么时候变"必须捏在断言手里（真变量住在 VM.Core，且由后台轮询线程驱动）。
        //
        //  没有消息循环这件事决定了断言写法：Start() 与每次值变化都只往 Dispatcher 排一个
        //  回调（BeginInvoke），控制台进程里那个回调永远不会被执行。所以每处都显式
        //  FlushNow() 同步刷一帧把结果钉住——这正是 FlushNow() 存在的唯一理由。
        // ==================================================================
        private static void RuntimeBinderChecks()
        {
            Section("[Z] 运行态数据泵：变量值变化 → 图元属性变化");

            Exception? failure = null;

            // 样板与 [S]/[T]/[U]/[X] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunRuntimeBinderChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("数据泵断言全程未抛异常", false, failure.ToString());
        }

        // ==================================================================
        //  [AA] 组态「选择变量」弹窗：工作区变量 → 可见清单
        //
        //  这一节的存在理由是真机验证抓到的一个"静态看不出来"的 Bug：
        //  工作区里明明有 15 个变量，弹窗却显示「当前方案里还没有变量」。
        //  根因不在变量来源，而在 OnDialogOpened 靠 `Keyword = string.Empty` 的 setter
        //  副作用去触发 ApplyFilter —— 首次打开时 _keyword 本来就是空串，
        //  SetProperty 判定"值没变"直接返回，ApplyFilter 压根没跑，Variables 恒空。
        //  所以断言必须钉在"投影后的 Variables 条数"上：只查变量来源是查不出来的。
        // ==================================================================
        private static void VariablePickerDialogChecks()
        {
            Section("[AA] 组态变量选择弹窗：工作区变量 → 可见清单");

            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("TotalCount", typeof(int), "累计生产总数", 1500));
            workspace.GlobalVariables.Add(new NetworkVariableModel
            {
                Name = "PLC_Ready",
                DataType = typeof(bool),
                ConnectionName = "PLC1",
                Value = true
            });

            var vm = new ScadaVariablePickerVM(workspace);
            vm.OnDialogOpened(new DialogParameters());

            Check("打开弹窗后清单可见（全量投影进 Variables）",
                vm.Variables.Count == 2,
                $"Variables={vm.Variables.Count} / EmptyHint=「{vm.EmptyHint}」");
            Check("有变量时不显示空态提示",
                string.IsNullOrEmpty(vm.EmptyHint),
                $"EmptyHint=「{vm.EmptyHint}」");
            Check("HasVariables 与清单同步为真", vm.HasVariables, $"HasVariables={vm.HasVariables}");
            Check("没配过变量时不预选（免得随手一个回车就把动作绑到清单第一条）",
                vm.SelectedVariable == null && !vm.ConfirmCommand.CanExecute(),
                $"选中=「{vm.SelectedVariable?.Name}」");

            // 第二次走 setter：这次 _keyword 真的从空变"PLC"，过滤必须生效
            vm.Keyword = "PLC";
            Check("关键字过滤生效（名 / 类型 / 来源 / 说明任一命中）",
                vm.Variables.Count == 1 && vm.Variables[0].Name == "PLC_Ready",
                $"命中 {vm.Variables.Count} 条");

            // 再次打开（面板上已经配过变量）：按 Id 预选回那一条，改名也不认错
            var reopen = new ScadaVariablePickerVM(workspace);
            reopen.OnDialogOpened(new DialogParameters
            {
                { ScadaVariablePicker.CurrentIdKey, workspace.GlobalVariables[1].VariableId },
                { ScadaVariablePicker.CurrentNameKey, "PLC_Ready" },
            });
            Check("已配过变量时按 Id 预选回那一条（确认键随即可用）",
                reopen.SelectedVariable?.Name == "PLC_Ready" && reopen.ConfirmCommand.CanExecute(),
                $"选中=「{reopen.SelectedVariable?.Name}」");

            // 空工作区是另一档：这才是"真的没变量"，必须仍然给出空态提示
            var emptyWorkspace = new WorkspaceContext();
            emptyWorkspace.GlobalVariables.Clear();
            var emptyVm = new ScadaVariablePickerVM(emptyWorkspace);
            emptyVm.OnDialogOpened(new DialogParameters());
            Check("工作区真的为空时才提示「还没有变量」",
                emptyVm.Variables.Count == 0 && !string.IsNullOrEmpty(emptyVm.EmptyHint),
                $"EmptyHint=「{emptyVm.EmptyHint}」");

            // ---------------- ⑫ 弹窗的"长相"：真渲染一遍，钉住列渲染器 ----------------
            //
            // 真机报障：清单里每一行显示的都是 VisionMaster.ViewModels.DialogViewModels.ScadaVariableRow。
            // 根因不在数据（VM 的 Name / DataType / SourceLabel / ValueText / Description 都是齐的），
            // 而在行容器模板：它把 ListViewItem 的模板整个换成了裸 ContentPresenter，
            // 于是默认模板里的 GridViewRowPresenter 被顶掉 —— 列布局没了，ListView 只能
            // 把数据对象丢给 ToString() 兜底。这类"只读 XAML 静态看不出来"的错，
            // 只能把真 View 渲染一遍、再问视觉树上到底有什么来钉；顺带留一张证据图供人眼复核排版。
            //
            // WPF 控件必须在 STA 上造，样板与 [S]/[T]/[U]/[X]/[Z] 一致：单开 STA 线程，
            // 不把 Main 标成 STAThread 去改 S0/S1 那批断言的既有运行环境。
            RunPickerRenderChecks(reopen);
        }

        /// <summary>⑫ 节的线程壳：把渲染断言挪到 STA 上跑，异常不吞（转成一条红断言）。</summary>
        private static void RunPickerRenderChecks(ScadaVariablePickerVM reopen)
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunPickerRenderChecksCore(reopen); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("弹窗渲染断言全程未抛异常（STA 线程内造 View 不该炸）", false, failure.ToString());
        }

        private static void RunPickerRenderChecksCore(ScadaVariablePickerVM reopen)
        {
            ViewModelLocationProvider.Register<ScadaVariablePickerView>(() => reopen);

            var view = new ScadaVariablePickerView { DataContext = reopen };
            view.Measure(new Size(760, 520));
            view.Arrange(new Rect(0, 0, 760, 520));
            view.UpdateLayout();

            var rowPresenters = CollectVisual<GridViewRowPresenter>(view, _ => true);
            Check("每一行都挂着 GridViewRowPresenter（GridView 的列渲染器；被换成 ContentPresenter 就会退化成类型全名）",
                rowPresenters.Count == reopen.Variables.Count && rowPresenters.Count > 0,
                $"列渲染器 {rowPresenters.Count} 个 / 数据 {reopen.Variables.Count} 条");

            // 列头的视觉次序按"谁画在左边"来判，不能按视觉树遍历次序：
            // GridViewHeaderRowPresenter 挂子元素的方向与列的声明方向相反，直接遍历拿到的是倒序
            // （那只是遍历顺序的错觉，屏幕上变量名仍在最左）。位置才是操作员看到的事实。
            // 另外末尾那个 Content 为空的列头是 GridView 的"填缝头"，不参与比对。
            var headers = CollectVisual<GridViewColumnHeader>(view, _ => true)
                .Where(h => h.Content != null)
                .OrderBy(h => h.TranslatePoint(new Point(0, 0), view).X)
                .Select(h => h.Content!.ToString() ?? string.Empty).ToList();
            Check("五列的列头都排出来了（变量名/类型/来源/当前值/说明）",
                string.Join("/", headers) == "变量名/类型/来源/当前值/说明",
                string.Join("/", headers));

            // 列头与数据行是两套 presenter 画的：列头由 GridViewHeaderRowPresenter 摆，
            // 数据由每行的 GridViewRowPresenter 摆。谁多吃了左边距，谁就整体偏——屏幕上表现为
            // "表头压在数据上"，非常显眼，但只读代码看不出来。所以按"文字画在哪"逐列钉一次。
            var headerEls = CollectVisual<GridViewColumnHeader>(view, _ => true)
                .Where(h => h.Content != null)
                .OrderBy(h => h.TranslatePoint(new Point(0, 0), view).X).ToList();
            var headerTextXs = headerEls
                .Select(h => CollectVisual<TextBlock>(h, _ => true).FirstOrDefault())
                .Select(t => t?.TranslatePoint(new Point(0, 0), view).X ?? double.NaN).ToList();
            var firstRow = rowPresenters
                .OrderBy(p => p.TranslatePoint(new Point(0, 0), view).Y).FirstOrDefault();
            var rowTextXs = firstRow == null
                ? new List<double>()
                : CollectVisual<TextBlock>(firstRow, _ => true)
                    .OrderBy(t => t.TranslatePoint(new Point(0, 0), view).X)
                    .Select(t => t.TranslatePoint(new Point(0, 0), view).X).ToList();
            Check("列头文字与第一行单元格文字逐列左对齐（表头压在数据上是最显眼的排版事故）",
                headerTextXs.Count == 5 && rowTextXs.Count == 5
                && headerTextXs.Zip(rowTextXs).All(p => Math.Abs(p.First - p.Second) < 0.6),
                $"列头 {string.Join("/", headerTextXs.Select(x => x.ToString("0.#")))} "
                + $"vs 行 {string.Join("/", rowTextXs.Select(x => x.ToString("0.#")))}");

            var texts = CollectVisual<TextBlock>(view, _ => true)
                .Select(t => t.Text ?? string.Empty).ToList();
            Check("整棵树里没有任何一处渲染出类型全名（就是真机截图上的那行字）",
                !texts.Any(t => t.Contains("ScadaVariableRow", StringComparison.Ordinal)),
                texts.FirstOrDefault(t => t.Contains("ScadaVariableRow", StringComparison.Ordinal)) ?? "（没有）");

            Check("每一行的数据都按列摊开了：变量名 / 类型 / 来源 / 当前值 / 说明五列各自有值",
                texts.Contains("TotalCount") && texts.Contains("整数 (Int)") && texts.Contains("本地")
                && texts.Contains("1500") && texts.Contains("累计生产总数")
                && texts.Contains("PLC_Ready") && texts.Contains("布尔 (Bool)") && texts.Contains("PLC1"),
                string.Join(" | ", texts.Where(t => t.Length > 0)));

            // 列宽之和必须塞得进内容区：ListView 的横向滚动条已关，列一旦超宽就是"最后一列被切掉"
            // 而不是"多出一条滚动条"，屏幕上表现为说明列断字，比滚动条更难察觉。
            // 预算里必须扣掉行两侧的 10px（行容器 Padding 4 + GridViewRowPresenter 给单元格内置的 6）——
            // 只减滚动条会算出个假余量：真机上"类型/当前值/说明"三列都被硬裁，就是漏算了这一段。
            const double rowHorizontalInset = 20;
            var list = FindVisual<ListView>(view, _ => true);
            var grid = list?.View as GridView;
            double columnsWidth = grid?.Columns.Sum(c => c.ActualWidth) ?? 0;
            Check("五列宽度之和塞得进内容区（扣掉竖滚动条与行左右留白后仍有余量，说明最后一列不会被硬裁）",
                list != null && columnsWidth > 0
                && columnsWidth + SystemParameters.VerticalScrollBarWidth + rowHorizontalInset <= list.ActualWidth,
                $"列宽和 {columnsWidth:0.#}px + 滚动条 {SystemParameters.VerticalScrollBarWidth}px "
                + $"+ 行留白 {rowHorizontalInset}px vs 内容区 {list?.ActualWidth ?? 0:0.#}px");

            // 列宽是死的、变量名与说明是活的：单元格正文必须自带省略号修剪。
            // DisplayMemberBinding 生成的是裸 TextBlock（无修剪），超长内容只会被硬裁成半截字。
            var cellTexts = rowPresenters.SelectMany(p => CollectVisual<TextBlock>(p, _ => true)).ToList();
            Check("每个单元格正文都带省略号修剪 + 悬停全值提示（超长内容不再被硬裁）",
                cellTexts.Count >= reopen.Variables.Count * 5
                && cellTexts.All(t => t.TextTrimming != TextTrimming.None)
                && cellTexts.All(t => t.ToolTip != null),
                $"{cellTexts.Count} 个单元格正文（期望 ≥ {reopen.Variables.Count * 5}），"
                + $"未修剪 {cellTexts.Count(t => t.TextTrimming == TextTrimming.None)} 个，"
                + $"无提示 {cellTexts.Count(t => t.ToolTip == null)} 个");

            Check("横向滚动条关掉（这种清单里它只会帮倒忙：列被推出视野，用户还得左右拖）",
                list != null
                && ScrollViewer.GetHorizontalScrollBarVisibility(list) == ScrollBarVisibility.Disabled, "");

            var pickerShot = RenderToBitmap(view, 760, 520);
            Check("弹窗整体渲染成图：列头底色 #F5F7FA 与选中行底色 #ECF5FF 都真的刷在画面上",
                HasColorIn(pickerShot, new Rect(0, 0, 760, 520), "#F5F7FA")
                && HasColorIn(pickerShot, new Rect(0, 0, 760, 520), "#ECF5FF"), "");

            string pickerShotDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(pickerShotDir);
            string pickerPng = Path.Combine(pickerShotDir, "scada-variable-picker.png");
            SavePng(pickerShot, pickerPng);
            Console.WriteLine($"        （渲染证据：{pickerPng}）");
        }

        // ==================================================================
        //  [AB] 运行窗口显示形态：软件级配置（AppConfig.json）+ 两种形态的属性组合
        //
        //  这一节钉的是真机反馈的那个问题：「点运行，整个主界面（含视觉图像）被盖住」。
        //  根因不在宿主写错了，而是 ScadaRuntimeWindow.xaml 把
        //  WindowStyle=None / WindowState=Maximized / ResizeMode=NoResize 写死在了编译期，
        //  形态成了"改不了的事实"而不是"读配置来的策略"。所以断言分两层：
        //    · 窗口层：刚构造出来的窗口必须是"普通窗口"（写死三件套的回归守卫），
        //              ApplyRunWindowMode 对两种形态设置的属性组合必须各自成立；
        //    · 配置层：弹窗选项互斥、取消不写盘、确定才落盘，且枚举以整数写进 JSON。
        //  配置那一半用真的 AppSettingsService（路径写死在程序目录，没法注入临时目录），
        //  所以 finally 里把原值写回——不给下一次运行留一份脏配置。
        // ==================================================================
        private static void RunWindowModeChecks()
        {
            Section("[AB] 运行窗口显示形态：配置默认值 + 独立/依附的属性组合");

            // ---------------- ① 枚举顺序与默认值都是契约 ----------------

            Check("枚举成员顺序冻结（0=依附 / 1=独立）：整数值会落进 AppConfig.json，中间插成员会整体移位",
                (int)ScadaRunWindowMode.AttachedToMainWindow == 0
                && (int)ScadaRunWindowMode.IndependentWindow == 1,
                $"{(int)ScadaRunWindowMode.AttachedToMainWindow} / {(int)ScadaRunWindowMode.IndependentWindow}");

            Check("新建配置的默认形态是「依附主窗口」：升级不能让现场已在跑的设备变成窗口化运行",
                new AppConfigModel().RunWindowMode == ScadaRunWindowMode.AttachedToMainWindow,
                new AppConfigModel().RunWindowMode.ToString());

            // ---------------- ② 设置弹窗：互斥 / 取消不写盘 / 确定写盘 ----------------

            var settings = new AppSettingsService();
            var original = settings.Current.RunWindowMode;
            try
            {
                settings.Current.RunWindowMode = ScadaRunWindowMode.AttachedToMainWindow;
                settings.Save();

                var vm = new ScadaRunWindowSettingsVM(settings);
                vm.OnDialogOpened(new DialogParameters());
                Check("打开弹窗显示的是当前生效的形态，不是上次点过的那个",
                    vm.IsAttached && !vm.IsIndependent,
                    $"依附={vm.IsAttached} / 独立={vm.IsIndependent}");

                vm.IsIndependent = true;
                Check("选独立后两个选项互斥（独立亮、依附灭）",
                    vm.IsIndependent && !vm.IsAttached,
                    $"依附={vm.IsAttached} / 独立={vm.IsIndependent}");

                // RadioButton 失去选中时会往 IsChecked 写 false。那一写必须被忽略，
                // 否则"选中新的"会紧接着被"旧的取消选中"覆盖回原值（两个 setter 抢同一个字段）。
                vm.IsIndependent = false;
                vm.IsAttached = false;
                Check("RadioButton 取消选中写的 false 被忽略（否则新旧选项会互相覆盖）",
                    vm.IsIndependent && !vm.IsAttached,
                    $"依附={vm.IsAttached} / 独立={vm.IsIndependent}");

                ButtonResult? closed = null;
                vm.RequestClose = MakeCloseListener(result => closed = result);

                vm.CancelCommand.Execute();
                Check("取消：不写盘，配置保持原值（改到一半放弃不该留痕）",
                    closed == ButtonResult.Cancel
                    && settings.Current.RunWindowMode == ScadaRunWindowMode.AttachedToMainWindow,
                    $"关闭结果={closed} / 内存配置={settings.Current.RunWindowMode}");

                vm.ConfirmCommand.Execute();
                Check("确定：关窗并返回 OK，内存配置同步成独立窗口",
                    closed == ButtonResult.OK
                    && settings.Current.RunWindowMode == ScadaRunWindowMode.IndependentWindow,
                    $"关闭结果={closed} / 内存配置={settings.Current.RunWindowMode}");

                // 重新读一份服务（构造函数会从磁盘 Load）：这一步才证明真写了盘，而不只是改了内存对象
                var reread = new AppSettingsService();
                Check("确定：枚举以整数落进 AppConfig.json，重新加载读回来仍是独立窗口",
                    reread.Current.RunWindowMode == ScadaRunWindowMode.IndependentWindow,
                    reread.Current.RunWindowMode.ToString());

                // 再次打开：编辑态必须从配置现读，不能带上一轮的残留
                var reopen = new ScadaRunWindowSettingsVM(settings);
                reopen.OnDialogOpened(new DialogParameters());
                Check("再次打开：编辑态从配置现读（上一轮的编辑残留不会带进来）",
                    reopen.IsIndependent && !reopen.IsAttached,
                    $"依附={reopen.IsAttached} / 独立={reopen.IsIndependent}");
            }
            finally
            {
                settings.Current.RunWindowMode = original;
                settings.Save();
            }

            // ---------------- ③ 窗口形态：属性组合 ----------------

            RunWindowModeWindowChecks();
        }

        /// <summary>
        /// 造一个能观察关窗结果的 <see cref="DialogCloseListener"/>。
        ///
        /// Prism 9 的这个类型是个类而不是委托（只有 <c>Invoke</c> 是 public，回调本身是 internal 的），
        /// 真机上由 DialogService 注入，而断言环境里没有 DialogService。所以这里用反射走一次那个
        /// internal 构造器——不是在测反射，是拿它当 Prism 的替身，好断言"点了确定/取消之后以什么结果关窗"。
        /// </summary>
        private static DialogCloseListener MakeCloseListener(Action<ButtonResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException(
                    "Prism.Dialogs.DialogCloseListener", ".ctor(Action<IDialogResult>)");

            return (DialogCloseListener)ctor.Invoke(new object[]
            {
                (Action<IDialogResult>)(result => onClosed(result.Result))
            });
        }

        private static void RunWindowModeWindowChecks()
        {
            Exception? failure = null;

            // 要真造 Window（DispatcherObject 血统），样板与 [S]/[T]/[U]/[X] 一致：单开 STA 线程，
            // 不把 Main 标成 STAThread 去改 S0/S1 那批断言的既有运行环境。
            var thread = new Thread(() =>
            {
                try { RunWindowModeWindowChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("运行窗口形态断言全程未抛异常", false, failure.ToString());
        }

        private static void RunWindowModeWindowChecksCore()
        {
            // 窗口不 Show：Show 需要消息循环，控制台进程里没有。形态属性在 Show 之前就已经定死，
            // 所以"不 Show"不影响这里要钉的东西——这恰好也是宿主 Start() 里定形态的位置。
            var window = new ScadaRuntimeWindow(new ScadaRuntime(new ScadaDocument()));

            // 回归守卫：XAML 里一旦有人把 WindowStyle=None / WindowState=Maximized / NoResize 写回去，
            // 这条立刻红。这是本次问题的根因，必须有一条断言直接盯住它。
            Check("刚构造的窗口是普通窗口（XAML 不再写死无边框 + 最大化）",
                window.WindowStyle == WindowStyle.SingleBorderWindow
                && window.WindowState == WindowState.Normal
                && window.ResizeMode != ResizeMode.NoResize,
                $"WindowStyle={window.WindowStyle} / WindowState={window.WindowState} / ResizeMode={window.ResizeMode}");

            var area = SystemParameters.WorkArea;

            window.ApplyRunWindowMode(ScadaRunWindowMode.IndependentWindow);
            Check("独立形态：带系统标题栏 + 可缩放 + 正常态 + 占一个任务栏项",
                window.WindowStyle == WindowStyle.SingleBorderWindow
                && window.ResizeMode == ResizeMode.CanResize
                && window.WindowState == WindowState.Normal
                && window.ShowInTaskbar,
                $"WindowStyle={window.WindowStyle} / ResizeMode={window.ResizeMode} / ShowInTaskbar={window.ShowInTaskbar}");

            Check("独立形态：落在工作区右下角而不是屏幕中央（居中会正好压住图像区）",
                window.WindowStartupLocation == WindowStartupLocation.Manual
                && Math.Abs(window.Left + window.Width - (area.Right - 24)) < 0.5
                && Math.Abs(window.Top + window.Height - (area.Bottom - 24)) < 0.5,
                $"Left={window.Left:0} Top={window.Top:0} / 工作区 {area.Width:0}×{area.Height:0}");

            Check("独立形态：尺寸按工作区收过，不会一开就铺满（并排的前提是两边都看得见）",
                window.Width > 0 && window.Height > 0
                && window.Width <= 1280 && window.Height <= 800,
                $"{window.Width:0} × {window.Height:0}");

            window.ApplyRunWindowMode(ScadaRunWindowMode.AttachedToMainWindow);
            Check("依附形态：无边框 + 最大化 + 不占任务栏（现场运行形态，回到历史行为）",
                window.WindowStyle == WindowStyle.None
                && window.ResizeMode == ResizeMode.NoResize
                && window.WindowState == WindowState.Maximized
                && !window.ShowInTaskbar,
                $"WindowStyle={window.WindowStyle} / ResizeMode={window.ResizeMode} / ShowInTaskbar={window.ShowInTaskbar}");

            Check("窗口自身不碰 Owner：归谁管是宿主的策略（独立形态一挂 Owner 就没法并排看）",
                window.Owner == null, window.Owner?.GetType().Name ?? "null");
        }

        private static void RunRuntimeBinderChecks()
        {
            // ---------------- 装配：一个画面、十种绑定形态 ----------------

            var doc = new ScadaDocument();
            var page = doc.AddPage("运行画面");
            var layer = page.Layers.FirstOrDefault() ?? page.AddLayer();

            // 变量：真机上由 VM.Core 的注册表适配器提供，这里用假句柄把"什么时候变值"捏在手里
            var tempVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "温度", DataType = typeof(double), Value = 23.456 };
            var runVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "运行", DataType = typeof(int), Value = 0 };
            var posVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "位置", DataType = typeof(double), Value = 300d };
            var legacyVar = new FakeValueHandle { VariableId = Guid.Empty, Name = "旧名", DataType = typeof(string), Value = "旧数据" };
            var byIdVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "同名", DataType = typeof(string), Value = "按Id" };
            var byNameVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "同名", DataType = typeof(string), Value = "按名字" };
            var brokenVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "坏值", DataType = typeof(string), Value = "不是布尔" };
            var offVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "停用", DataType = typeof(string), Value = "不该出现" };

            var source = new FakeValueSource();
            foreach (var handle in new[] { tempVar, runVar, posVar, legacyVar, brokenVar, offVar })
            {
                if (handle.VariableId != Guid.Empty)
                    source.ById[handle.VariableId] = handle;

                source.ByName[handle.Name] = handle;
            }

            // 故意让"同名"这个名字指向<b>另一个</b>变量：Id 优先的口径一旦写反，
            // idEl 就会拿到按名字那个，"改一个变量串到别人家"这种现场最难查的故障就出现了。
            source.ById[byIdVar.VariableId] = byIdVar;
            source.ByName[byIdVar.Name] = byNameVar;

            ScadaElement Add(string typeKey, string name, double x, double y, double width, double height)
            {
                var element = ElementRegistry.CreateElement(typeKey, x, y)!;
                element.Name = name;
                element.Width = width;
                element.Height = height;
                element.ZIndex = page.Elements.Count + 1;
                page.Elements.Add(element);
                page.TryAssignLayer(element, layer, out _);
                return element;
            }

            static void Bind(ScadaElement element, string target, Guid variableId, string? variableName,
                string? format = null, bool enabled = true)
            {
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = target,
                    VariableId = variableId,
                    VariableName = variableName,
                    DisplayFormat = format,
                    IsEnabled = enabled,
                });
            }

            var textEl = Add("Hmi.Text", "温度显示", 100, 100, 160, 30);
            var textEl2 = Add("Hmi.Text", "温度显示2", 100, 140, 160, 30);
            var indEl = Add("Hmi.Indicator", "运行指示", 300, 100, 40, 40);
            var rectEl = Add("Hmi.Rectangle", "位置矩形", 40, 200, 80, 40);
            var legacyEl = Add("Hmi.Text", "旧数据文本", 100, 260, 160, 30);
            var idEl = Add("Hmi.Text", "同名陷阱", 100, 300, 160, 30);
            var badValEl = Add("Hmi.Indicator", "坏值指示", 400, 100, 40, 40);
            var offEl = Add("Hmi.Text", "停用绑定", 100, 340, 160, 30);
            var missVarEl = Add("Hmi.Text", "变量没找到", 100, 380, 160, 30);
            var badPropEl = Add("Hmi.Text", "属性不认识", 100, 420, 160, 30);

            ElementValueAccess.Write(textEl, "Text", "设计文本");

            Bind(textEl, "Text", tempVar.VariableId, tempVar.Name, "F2");
            Bind(textEl2, "Text", tempVar.VariableId, tempVar.Name);            // 同一个变量：订阅该只挂一份
            Bind(indEl, "IsOn", runVar.VariableId, runVar.Name);
            Bind(rectEl, ElementValueAccess.XKey, posVar.VariableId, posVar.Name); // 几何键
            Bind(legacyEl, "Text", Guid.Empty, "旧名");                          // 旧数据：只能按名字找
            Bind(idEl, "Text", byIdVar.VariableId, byIdVar.Name);                // Id 与名字指向两个变量
            Bind(badValEl, "IsOn", brokenVar.VariableId, brokenVar.Name);        // 接上了，但值转不过去
            Bind(offEl, "Text", offVar.VariableId, offVar.Name, enabled: false); // 停用
            Bind(missVarEl, "Text", Guid.Empty, "查无此变量");                    // 变量没找到
            Bind(badPropEl, "不存在的属性", tempVar.VariableId, tempVar.Name);     // 属性不在描述符里

            var canvas = BuildBinderCanvas(page);
            var controls = canvas.EnumerateControls().ToList();

            ScadaElementBase ControlOf(ScadaElement element)
                => controls.First(c => ReferenceEquals(c.Element, element));

            Check("画布渲染出了全部 10 个图元控件（建表的前提：控件要到布局阶段才被造出来，所以必须在首帧之后建表）",
                controls.Count == 10, $"{controls.Count} 个");
            Check("渲染后设计几何已落地（Canvas.Left = 模型 X，后面的「回到设计值」才有参照物）",
                Math.Abs(Canvas.GetLeft(ControlOf(rectEl)) - rectEl.X) < 0.001,
                $"{Canvas.GetLeft(ControlOf(rectEl))} vs {rectEl.X}");

            var offDesignText = ControlOf(offEl).Text;

            // ---------------- ① 建表：命中 / 未命中 / 停用 ----------------

            var reports = new List<(ScadaDiagnosticLevel Level, string Message)>();
            var binder = new ScadaRuntimeBinder(canvas, source, page, (level, message) => reports.Add((level, message)));

            binder.Start();
            binder.FlushNow(); // 没有消息循环：排进 Dispatcher 的那一帧不会自己跑，手动刷

            Check("建表计数：命中 7 条（含同变量的两条、几何键一条），未命中 2 条（变量没找到 / 属性不认识）",
                binder.BoundCount == 7 && binder.MissCount == 2,
                $"命中 {binder.BoundCount} / 未命中 {binder.MissCount}");
            Check("停用的绑定与属性不认识的绑定连解析都不做（前者在判定之前就返回，后者在查表阶段就回头）",
                source.ResolveCalls == 8, $"解析了 {source.ResolveCalls} 次");
            Check("按变量订阅一次：10 条绑定只换来 6 个变量订阅；同一变量被两条绑定指着也只挂一个 handler",
                binder.SubscriptionCount == 6 && tempVar.Subscribers == 1 && byNameVar.Subscribers == 0,
                $"订阅 {binder.SubscriptionCount} / 温度 {tempVar.Subscribers} / 同名(按名字那个) {byNameVar.Subscribers}");

            // ---------------- ② 建表即刷一遍当前值 ----------------

            Check("建表完立刻刷一遍当前值：操作员打开画面看到的是此刻的真实状态，不必等下一次变化",
                ControlOf(textEl).Text == "23.46" && ControlOf(textEl2).Text == "23.456",
                $"按格式串「{ControlOf(textEl).Text}」/ 无格式「{ControlOf(textEl2).Text}」");
            Check("旧数据（Id 为空）按名字兜底命中：Id 落地之前存下的方案照样跑得起来",
                ControlOf(legacyEl).Text == "旧数据", $"「{ControlOf(legacyEl).Text}」");

            // ---------------- ③ 值变化 → 图元属性变化 ----------------

            tempVar.Raise(88.888);
            binder.FlushNow();
            Check("变量变值 → 图元属性跟着变（同一个变量的两条绑定一起刷新）",
                ControlOf(textEl).Text == "88.89" && ControlOf(textEl2).Text == "88.888",
                $"「{ControlOf(textEl).Text}」/「{ControlOf(textEl2).Text}」");

            runVar.Raise(1);
            binder.FlushNow();
            Check("PLC 给整数 1 → 指示灯点亮（bool 目标认 1/0，不必让下位机改数据类型）",
                ((IndicatorElement)ControlOf(indEl)).IsOn, $"IsOn={((IndicatorElement)ControlOf(indEl)).IsOn}");

            runVar.Raise(0);
            binder.FlushNow();
            Check("给 0 → 熄灭（非零算真这条规则两个方向都要对）",
                !((IndicatorElement)ControlOf(indEl)).IsOn, $"IsOn={((IndicatorElement)ControlOf(indEl)).IsOn}");

            posVar.Raise(500d);
            binder.FlushNow();
            Check("几何键 $X 落到控件自身的 Canvas.Left（与设计期同一处落点，数据泵不认识几何语义）",
                Math.Abs(Canvas.GetLeft(ControlOf(rectEl)) - 500) < 0.001,
                $"{Canvas.GetLeft(ControlOf(rectEl))}");

            // ---------------- ④ Id 优先：同名不同 Id 不许串值 ----------------

            byNameVar.Raise("按名字");
            binder.FlushNow();
            Check("Id 优先：同名变量里按名字那个改了值，本绑定纹丝不动（口径写反就会串值，且看不出哪里错了）",
                ControlOf(idEl).Text == "按Id", $"「{ControlOf(idEl).Text}」");

            byIdVar.Raise("按Id改了");
            binder.FlushNow();
            Check("而 Id 那个一改就动（权威键是 Id，名字只是给人看的展示串）",
                ControlOf(idEl).Text == "按Id改了", $"「{ControlOf(idEl).Text}」");

            // ---------------- ⑤ 失败可见：橙=没接上、红=值用不了 ----------------

            Check("三条坏绑定各自打点：变量没找到（橙）+ 属性不认识（橙）+ 值转不过去（红）",
                canvas.Diagnostics.Count == 3, $"{canvas.Diagnostics.Count} 个角标");
            Check("诊断同时进日志，级别分得清「配置没配好」（警告）与「数据用不了」（错误）",
                reports.Count(r => r.Level == ScadaDiagnosticLevel.Error) == 1
                && reports.Any(r => r.Level == ScadaDiagnosticLevel.Error && r.Message.Contains("不是可识别的布尔值"))
                && reports.Count(r => r.Level == ScadaDiagnosticLevel.Warning) == 2
                && reports.Any(r => r.Level == ScadaDiagnosticLevel.Warning && r.Message.Contains("查无此变量"))
                && reports.Any(r => r.Level == ScadaDiagnosticLevel.Warning && r.Message.Contains("不存在的属性")),
                string.Join(" ⏎ ", reports.Select(r => $"{r.Level} {r.Message}")));

            brokenVar.Raise("1");
            binder.FlushNow();
            Check("坏值转好了：属性写进去、角标自动撤掉，且只撤自己那一个（别处的提示不许被一起抹掉）",
                ((IndicatorElement)ControlOf(badValEl)).IsOn && canvas.Diagnostics.Count == 2,
                $"IsOn={((IndicatorElement)ControlOf(badValEl)).IsOn} / {canvas.Diagnostics.Count} 个角标");

            offVar.Raise("偷偷改了");
            binder.FlushNow();
            Check("停用的绑定即使变量变了也不刷新、也不打点（配置保留、运行跳过）",
                ControlOf(offEl).Text == offDesignText && canvas.Diagnostics.Count == 2,
                $"「{ControlOf(offEl).Text}」/ {canvas.Diagnostics.Count} 个角标");

            // ---------------- ⑥ 摘表：订阅与角标一起归零，控件回到设计值 ----------------

            binder.Stop();

            Check("Stop 把订阅全退掉（不退就是泄漏：变量注册表是长生命周期对象，它拿着整棵控件树）",
                !binder.IsRunning && binder.SubscriptionCount == 0
                && tempVar.Subscribers == 0 && runVar.Subscribers == 0 && posVar.Subscribers == 0
                && legacyVar.Subscribers == 0 && byIdVar.Subscribers == 0 && brokenVar.Subscribers == 0,
                $"订阅 {binder.SubscriptionCount} / 温度 {tempVar.Subscribers} / 位置 {posVar.Subscribers}");
            Check("Stop 把计数与角标一起归零（留一个都会让下一次运行的日志说谎）",
                binder.BoundCount == 0 && binder.MissCount == 0 && canvas.Diagnostics.Count == 0,
                $"命中 {binder.BoundCount} / 未命中 {binder.MissCount} / 角标 {canvas.Diagnostics.Count}");
            Check("Stop 后控件回到设计值：运行值只活在控件上，重刷一遍模型就自己盖掉了（不需要任何恢复现场的备份表）",
                ControlOf(textEl).Text == "设计文本"
                && Math.Abs(Canvas.GetLeft(ControlOf(rectEl)) - rectEl.X) < 0.001
                && !((IndicatorElement)ControlOf(indEl)).IsOn
                && !((IndicatorElement)ControlOf(badValEl)).IsOn,
                $"「{ControlOf(textEl).Text}」/ Left={Canvas.GetLeft(ControlOf(rectEl))} / IsOn={((IndicatorElement)ControlOf(indEl)).IsOn}");

            // 宿主的两条生命周期路径都可能重入（窗口反复开关、ContentRendered 偶发两次）
            binder.Start();
            binder.FlushNow();
            Check("重新 Start 能再次建表（幂等而非一次性），值也跟着回来",
                binder.IsRunning && binder.BoundCount == 7 && binder.SubscriptionCount == 6
                && ControlOf(textEl).Text == "88.89",
                $"命中 {binder.BoundCount} / 订阅 {binder.SubscriptionCount} / 「{ControlOf(textEl).Text}」");

            binder.Stop();
            binder.Stop();
            Check("重复 Stop 不出事、也不把设计值再动一次（关窗口路径重复走一遍要安全）",
                !binder.IsRunning && binder.SubscriptionCount == 0 && canvas.Diagnostics.Count == 0
                && ControlOf(textEl).Text == "设计文本",
                $"「{ControlOf(textEl).Text}」/ 订阅 {binder.SubscriptionCount}");
        }

        /// <summary>
        /// 按生产模板装配一个真实画布：建表要靠它渲染出的图元控件（<c>PART_ElementLayer</c>），
        /// 打点要靠它的诊断层（<c>PART_DiagnosticLayer</c>）。两样都是模板部件，所以必须
        /// ApplyTemplate + Measure/Arrange 走一遍真实的布局流程，不能只 new 一个空画布。
        /// </summary>
        private static ScadaCanvas BuildBinderCanvas(ScadaPage page)
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };

            var host = new ScadaCanvas
            {
                PageWidth = page.Width,
                PageHeight = page.Height,
                ItemsSource = page.Elements,
                Page = page,
                ShowGrid = false,
                SnapToGrid = false,
            };

            host.Style = (Style)theme[typeof(ScadaCanvas)];
            host.ApplyTemplate();
            host.Measure(new Size(page.Width, page.Height));
            host.Arrange(new Rect(0, 0, page.Width, page.Height));
            host.UpdateLayout();
            return host;
        }

        /// <summary>
        /// 假的变量句柄：值由断言手动 <see cref="Raise"/> 推进（真机上这一步在后台轮询线程上）。
        /// 订阅者计数是"退订干不干净"的唯一证据——退不掉时它不会报错，只会在跑了几百个画面后
        /// 让内存悄悄涨上去。
        /// </summary>
        private sealed class FakeValueHandle : IScadaValueHandle
        {
            public Guid VariableId { get; init; }

            public string Name { get; init; } = string.Empty;

            public Type DataType { get; init; } = typeof(object);

            public object? Value { get; set; }

            /// <summary>写入失败时回的原因（null = 允许写）。用来演"设备侧写不进"那一档。</summary>
            public string? WriteError { get; init; }

            public event EventHandler? ValueChanged;

            /// <summary>当前挂着的订阅者数</summary>
            public int Subscribers => ValueChanged?.GetInvocationList().Length ?? 0;

            /// <summary>模拟"底层变量改了值"：改值并通知订阅方</summary>
            public void Raise(object? value)
            {
                Value = value;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool TryWrite(object? value, out string? error)
            {
                error = WriteError;

                if (error != null) return false;

                Raise(value);
                return true;
            }
        }

        /// <summary>
        /// 假的数据源：Id 优先、名字兜底，与生产实现（RegistryScadaValueSource）同一口径。
        /// 解析次数单独记账，用来证明"停用的绑定与查不到属性的绑定根本没走到解析"。
        /// </summary>
        private sealed class FakeValueSource : IScadaValueSource
        {
            public Dictionary<Guid, IScadaValueHandle> ById { get; } = new();

            public Dictionary<string, IScadaValueHandle> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);

            public int ResolveCalls { get; private set; }

            public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
            {
                ResolveCalls++;

                if (variableId != Guid.Empty && ById.TryGetValue(variableId, out handle))
                    return true;

                if (!string.IsNullOrWhiteSpace(fallbackName) && ByName.TryGetValue(fallbackName!, out handle))
                    return true;

                handle = null;
                return false;
            }
        }

        /// <summary>
        /// 假的「选变量」选择器：不弹窗，直接把预置结果回调出去。
        /// 面板与事件行只认 <see cref="IScadaVariablePicker"/> 这一个方法（不直接拿 IDialogService），
        /// 所以断言工程能在控制台里把"选中 → 回填"这条链整条走完；真机那条链上换的是弹窗实现，
        /// 调用方一行不动。
        /// </summary>
        private sealed class FakeVariablePicker : IScadaVariablePicker
        {
            /// <summary>下一次 Pick 要回传的变量 Id；Empty 表示"用户点了取消"（回调一次都不触发）</summary>
            public Guid NextId { get; set; }

            public string? NextName { get; set; }

            /// <summary>被点了几次「选择变量」</summary>
            public int PickCalls { get; private set; }

            /// <summary>最后一次收到的"当前绑定"，用来核对预选参数确实是这条动作的现状</summary>
            public Guid LastCurrentId { get; private set; }

            public string? LastCurrentName { get; private set; }

            public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked)
            {
                PickCalls++;
                LastCurrentId = currentId;
                LastCurrentName = currentName;

                // 取消的语义是"回调一次都不触发"，不是"回传 Empty 让调用方自己判断"——
                // 后者一旦哪边漏判，取消就变成了"清空原绑定"这种破坏性动作。
                if (NextId == Guid.Empty) return;

                onPicked(NextId, NextName);
            }
        }

        /// <summary>
        /// 假的运行态宿主：只记账，不弹任何窗口。
        /// 断言工程是控制台进程，真的宿主会去 ShowDialog 卡住整个测试；而视图模型只看
        /// 接口，所以这里给一个最笨的实现就足够把"可用性"这件事验完。
        /// </summary>
        private sealed class FakeRuntimeHost : IScadaRuntimeHost
        {
            public bool IsRunning { get; private set; }

            /// <summary>被启动的次数（用来证明置灰期间没人绕过判定偷偷跑起来）</summary>
            public int Starts { get; private set; }

            /// <summary>最后一次交进来的文档（供断言核对"跑的就是内存里这一份"）</summary>
            public ScadaDocument? LastDocument { get; private set; }

            public void Start(ScadaDocument document)
            {
                Starts++;
                LastDocument = document;
                IsRunning = true;
            }

            public void Stop() => IsRunning = false;
        }

        /// <summary>
        /// 假的 user notifier：把提示语记进列表，供 ⑦ 段核对 ƒx 到底说了什么。
        /// 断言工程里没有 IUserNotifier 实现（它是给 UI 层用的），面板构造又要求非空，
        /// 所以自带一个最笨的——只记账，不弹窗。
        /// </summary>
        private sealed class RecordingNotifier : IUserNotifier
        {
            public readonly List<string> Messages = new();

            public void ShowInfo(string message) => Messages.Add(message);

            public void ShowWarn(string message) => Messages.Add(message);

            public void ShowError(string message) => Messages.Add(message);
        }

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
    }
}
