using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Prism.Dialogs;
using Prism.Mvvm;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Lifetime;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using ScadaEditorVM = VisionMaster.ViewModels.ScadaEditorViewModel;
using ScadaToolboxVM = VisionMaster.ViewModels.ScadaToolboxViewModel;
using ScadaPropertyVM = VisionMaster.ViewModels.ScadaPropertyViewModel;
using ScadaPropertyRow = VisionMaster.ViewModels.ScadaPropertyRow;
using ScadaPropertyRowBase = VisionMaster.ViewModels.ScadaPropertyRowBase;
using ScadaEventRow = VisionMaster.ViewModels.ScadaEventRow;
using ScadaRowEditorKind = VisionMaster.ViewModels.ScadaRowEditorKind;
using ScadaMenuItem = VisionMaster.ViewModels.ScadaMenuItem;
using ScadaLayerVM = VisionMaster.ViewModels.ScadaLayerViewModel;
using ScadaLayerItem = VisionMaster.ViewModels.ScadaLayerItem;
using ScadaActionTypeOption = VisionMaster.ViewModels.ScadaActionTypeOption;
using ScadaVariablePickerVM = VisionMaster.ViewModels.DialogViewModels.ScadaVariablePickerViewModel;
using ScadaPagePickerVM = VisionMaster.ViewModels.DialogViewModels.ScadaPagePickerViewModel;
using ScadaRunWindowSettingsVM = VisionMaster.ViewModels.DialogViewModels.ScadaRunWindowSettingsViewModel;
using ScadaSystemParametersVM = VisionMaster.ViewModels.DialogViewModels.ScadaSystemParametersViewModel;
using ScadaVariableEventRow = VisionMaster.ViewModels.DialogViewModels.ScadaVariableEventRow;
using ScadaVariableEventDialogViewModel = VisionMaster.ViewModels.DialogViewModels.ScadaVariableEventDialogViewModel;
using SolutionListVM = VisionMaster.ViewModels.DialogViewModels.SolutionListViewModel;
using CollectionViewGroup = System.Windows.Data.CollectionViewGroup;
using ScadaRuntimeWindow = VisionMaster.Views.ScadaRuntimeWindow;
using ScadaPropertyView = VisionMaster.Views.ScadaPropertyView;
using ScadaEditorView = VisionMaster.Views.ScadaEditorView;
using ScadaVariablePickerView = VisionMaster.Views.DialogViews.ScadaVariablePickerView;
using ScadaPagePickerView = VisionMaster.Views.DialogViews.ScadaPagePickerView;
using ScadaAlarmHistoryVM = VisionMaster.ViewModels.ScadaAlarmHistoryViewModel;
using ScadaAlarmHistoryRow = VisionMaster.ViewModels.ScadaAlarmHistoryRow;
using ScadaAlarmHistoryFilter = VisionMaster.ViewModels.ScadaAlarmHistoryFilter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            // 断言框架只捕得住"主线程同步抛出的异常"。Prism 的 DialogCloseListener.Invoke 是 async void，
            // 里面的异常会经 Task.ThrowAsync 甩到线程池，绕过 Check 直接把进程打死——那时既没有红条，
            // 也看不到是谁抛的，只剩一个退出码。这里挂一个兜底打印，把"静默死掉"变成"看得见的现场"。
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.Error.WriteLine("========== 未处理异常（进程即将退出） ==========");
                Console.Error.WriteLine(e.ExceptionObject);
            };

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
            AnimationChecks();
            VariableEventChecks();
            VariablePickerDialogChecks();
            PagePickerDialogChecks();
            RunWindowModeChecks();
            EditHistoryChecks();
            NudgeChecks();
            LayerPanelChecks();
            ElementLockChecks();
            ElementGroupChecks();
            ClipboardAndTemplateChecks();
            AlarmChecks();
            AlarmHistoryChecks();
            AlarmBannerChecks();
            AlarmHistoryPanelChecks();
            UserStoreChecks();
            AuditWriterChecks();
            SystemParametersChecks();
            SolutionListAuditChecks();
            PackagingChecks();
            VariableEventDialogChecks();
            if (stress) StressRoundTrip();
            if (stress) StressRegistryLookup();
            if (stress) StressScadaDocument();
            if (stress) PerformanceBaseline();
            if (stress) SoakRuntimeBinder();

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

                // ============ 全部内置图元：描述符声明的属性必须整体往返（s7-12 / s7-13）============
                //
                // 上面那几条只钉住了一个矩形的一条 Fill。图元库现在已经 14 个图元、上百条属性，
                // 每一条都可能是"画得出来、存不下去"的半成品——属性袋是纯字符串字典，
                // 序列化层不认识任何业务键（它只负责搬运），所以漏没漏全靠这条断言兜底，
                // 而不是靠"我记得写过了"。
                //
                // 做法：把**每一个已注册图元**描述符声明的全部非几何属性各写一个可辨认的探针值，
                // 落盘再读回，逐键对账。几何键走强类型字段（上面另有断言），这里不重复钉。
                //
                // 名单不写死、从注册表全量取：新增图元时这条断言自动覆盖它，
                // 不必记得回来补一行数组——"靠记得"正是这类覆盖型断言最常漏的地方。
                //
                // 探针值必须与描述符默认值不同，否则"整条属性被漏掉"也会读到默认值而蒙混过关。
                string[] probeTypes = ElementRegistry.All.Select(d => d.TypeKey).ToArray();

                static string ProbeValue(ElementPropertyDescriptor p)
                {
                    switch (p.Kind)
                    {
                        case ElementPropertyKind.Color:
                            return "#FF1A2B3C";

                        case ElementPropertyKind.Number:
                            // 取量程内一个不与默认值重合的点；越界值会被控件夹取，那就测不出"存的是我写的那个"
                            double lo = double.IsNegativeInfinity(p.Min) ? 0d : p.Min;
                            double hi = double.IsPositiveInfinity(p.Max) ? lo + 100d : p.Max;
                            Func<double, string> at = t => Math.Round(lo + (hi - lo) * t, 3)
                                .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                            return at(0.375) != p.DefaultValue ? at(0.375) : at(0.625);

                        case ElementPropertyKind.Bool:
                            return p.DefaultValue == "True" ? "False" : "True";

                        case ElementPropertyKind.Choice:
                            // 挑第一个不等于默认值的候选（候选只有一个时只能退回默认值，那种属性本就无可探）
                            return p.Choices.FirstOrDefault(c => c != p.DefaultValue) ?? p.DefaultValue;

                        default:
                            return "往返探针";
                    }
                }

                var propDoc = new ScadaDocument();
                var propPage = propDoc.AddPage("属性往返");
                var probes = new Dictionary<Guid, Dictionary<string, string>>();
                int probeCount = 0;
                int bareTypes = 0;
                string bareFirst = string.Empty;

                foreach (string typeKey in probeTypes)
                {
                    var pe = ElementRegistry.CreateElement(typeKey, 10, 10);
                    var values = new Dictionary<string, string>();
                    foreach (var p in ElementRegistry.Find(typeKey)!.Properties.Where(p => !p.IsGeometry))
                    {
                        string probe = ProbeValue(p);
                        ElementValueAccess.Write(pe, p.Key, probe);
                        values[p.Key] = probe;
                    }

                    // 一条属性袋键都没有的图元，在这条断言里是"完全测不到"的：
                    // 它会一路绿着走过去，却什么也没证明。单独计出来，别让覆盖出现盲区。
                    if (values.Count == 0)
                    {
                        bareTypes++;
                        if (bareFirst.Length == 0) bareFirst = typeKey;
                    }

                    propPage.Elements.Add(pe);
                    probes[pe.ElementId] = values;
                    probeCount += values.Count;
                }

                // 探针有效性自检：探针必须与描述符默认值不同。
                // 否则"整条属性被漏掉"也会读到默认值，下面那条逐键往返就成了永远绿的摆设——
                // 这类"断言自己失效了却还是绿的"比断言红掉更难发现，所以单钉一条。
                int weakProbes = 0;
                string weakFirst = string.Empty;
                foreach (string typeKey in probeTypes)
                    foreach (var p in ElementRegistry.Find(typeKey)!.Properties.Where(p => !p.IsGeometry))
                        if (ProbeValue(p) == p.DefaultValue)
                        {
                            weakProbes++;
                            if (weakFirst.Length == 0) weakFirst = $"{typeKey}.{p.Key}";
                        }

                Check("探针有效性：每条属性的探针值都与描述符默认值不同（否则逐键往返会退化成永远绿）",
                    weakProbes == 0,
                    weakProbes == 0 ? $"{probeCount} 条全部有效" : $"{weakProbes} 条探针与默认值重合，首个：{weakFirst}");

                Check("探针覆盖面：每个已注册图元都至少有一条可探的属性袋键",
                    bareTypes == 0,
                    bareTypes == 0 ? $"{probeTypes.Length} 个图元 / {probeCount} 条属性"
                                   : $"{bareTypes} 个图元一条可探属性都没有，首个：{bareFirst}");

                var propSolution = new SolutionModel { Scada = propDoc };
                string propPath = Path.Combine(dir, "props-roundtrip.vms");
                Check("全部内置图元带全属性探针的方案保存成功",
                    service.SaveAsync(propSolution, propPath).GetAwaiter().GetResult().Success, "");

                var propLoad = service.LoadAsync(propPath).GetAwaiter().GetResult();
                var propLoaded = propLoad.Data?.Scada?.FindPageByName("属性往返");
                Check("带全属性探针的方案加载成功", propLoad.Success && propLoaded != null, propLoad.Message);

                int propMismatch = 0;
                int propMissing = 0;
                string propFirstBad = string.Empty;

                foreach (var le in propLoaded?.Elements.ToList() ?? new List<ScadaElement>())
                {
                    if (!probes.TryGetValue(le.ElementId, out var expected)) { propMissing++; continue; }

                    foreach (var kv in expected)
                    {
                        string readBack = ElementValueAccess.Read(le, kv.Key);
                        if (readBack != kv.Value)
                        {
                            propMismatch++;
                            if (propFirstBad.Length == 0)
                                propFirstBad = $"{le.TypeKey}.{kv.Key}：写「{kv.Value}」读回「{readBack}」";
                        }
                    }

                    // 描述符声明了几条非几何属性，属性袋就该有几条键：
                    // 少一条说明"声明了却写不进去"，多一条说明有两条属性共用了同一个键。
                    int declared = ElementRegistry.Find(le.TypeKey)!.Properties.Count(p => !p.IsGeometry);
                    if (le.Properties.Count != declared)
                    {
                        propMismatch++;
                        if (propFirstBad.Length == 0)
                            propFirstBad = $"{le.TypeKey}：声明 {declared} 条属性，属性袋里 {le.Properties.Count} 条";
                    }
                }

                Check("全部内置图元逐键往返：每一条声明出来的属性都原样回来",
                    propMismatch == 0 && propMissing == 0 && probeCount > 0,
                    propMismatch == 0 && propMissing == 0
                        ? $"{probeTypes.Length} 个图元 / {probeCount} 条属性"
                        : $"不符 {propMismatch} 条、丢图元 {propMissing} 个；首个：{propFirstBad}");

                Check("全部内置图元往返后类型键不变（.vms 里只留字符串，类型键是长期标识）",
                    propLoaded != null && probeTypes.All(t =>
                        propLoaded.Elements.Count(e => e.TypeKey == t) == 1),
                    propLoaded == null ? "画面缺失" : $"{propLoaded.Elements.Count} 个图元：{string.Join("、", propLoaded.Elements.Select(e => e.TypeKey))}");

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
        //  [BL] 四组规模基线（--stress，S13-a）
        //
        //  与 [E] / [I] / [Q] 的分工
        //  ---------
        //  那三段各自证明"某一类对象在万级下不塌"；本段回答的是另一个问题：
        //  "比上一版慢了多少"。没有数字的回归只能靠感觉，而感觉分不清
        //  "好像有点卡"和"确实慢了三倍"——这正是路线图要"看数字不看感觉"的原因。
        //
        //  所以本段只做前三段没做的两件事：
        //  ① 报**单位成本**（µs/图元、µs/绑定、µs/切页、µs/报警轮）。规模可以不同、
        //     机器可以不同，单位成本是唯一能跨版本、跨机器对比的数字；
        //  ② 补上前三段没碰的两组规模：**切页次数**与**报警数**——
        //     后两组各自带着一条会随操作时长增长的结构（页栈、报警历史），
        //     所以除了成本，还要钉住"增长有上限、停止后归零"。
        //
        //  阈值为什么取得这么宽
        //  ---------
        //  基线断言只该拦住"数量级退化"（O(n) 变 O(n²)、索引被换回线性扫描），
        //  不该拦住机器抖动——否则它会变成"偶尔红一次"的噪声，而噪声最后一定会被忽略。
        //  精确数字看本段末尾打印的"基线数字"块（留档用），阈值只管住量级。
        // ==================================================================
        private static void PerformanceBaseline()
        {
            Section("[BL] 四组规模基线（--stress，S13-a）");

            ScadaEditHistory.Clear(); // 撤销栈是全局静态的，本段压栈、出段前清
            var baseline = new List<string>();

            try
            {
                var sw = new Stopwatch();

                // ----------------------------------------------------------
                // ① 图元数：构造 + 按身份查找
                // 取 20000 而不是 [Q] 的 10000：基线要看的是"规模再翻一倍曲线还直不直"，
                // 与 [Q] 同规模只会得到同一个数字，等于白跑一遍。
                // ----------------------------------------------------------
                const int elementCount = 20000;
                sw.Restart();
                var elementPage = new ScadaPage();
                for (int i = 0; i < elementCount; i++)
                {
                    elementPage.Elements.Add(new ScadaElement
                    {
                        TypeKey = "Hmi.Label",
                        Name = $"基线标签{i}",
                        X = i % 1920,
                        Y = i % 1080
                    });
                }
                var elementBuildMs = sw.ElapsedMilliseconds;

                var elementIds = elementPage.Elements.Select(e => e.ElementId).ToArray();

                const int findProbes = 200000;
                sw.Restart();
                int findHits = 0;
                for (int i = 0; i < findProbes; i++)
                {
                    if (elementPage.FindElement(elementIds[i % elementCount]) != null)
                        findHits++;
                }
                var findMs = sw.ElapsedMilliseconds;

                Check($"① 图元数 {elementCount}：构造与按身份查找都不随规模退化",
                    findHits == findProbes && elementBuildMs < 8000 && findMs < 3000,
                    $"构造 {elementBuildMs}ms（{Per(elementBuildMs, elementCount):F1}µs/图元）/ 查找 {findProbes} 次 {findMs}ms（{Per(findMs, findProbes):F2}µs/次）/ hits={findHits}");

                baseline.Add($"图元数 | {elementCount} 个 | 构造 {elementBuildMs}ms = {Per(elementBuildMs, elementCount):F1}µs/图元 | 查找 {findProbes} 次 {findMs}ms = {Per(findMs, findProbes):F2}µs/次");

                // ----------------------------------------------------------
                // ② 绑定数：跨图元枚举 + 改名级联
                // 形状与 [Q] 相同（每条图元两条绑定：一条按 Id、一条按名），
                // 但这里量的是"每条绑定摊到多少 µs"——级联是一次 O(绑定数) 的遍历，
                // 一旦有人在里面加了"按图元重建索引"，单位成本会立刻跳一个量级。
                // ----------------------------------------------------------
                const int bindingElements = 10000;
                var bindingVariableId = Guid.NewGuid();
                var bindingPage = new ScadaPage();

                sw.Restart();
                for (int i = 0; i < bindingElements; i++)
                {
                    var element = new ScadaElement { TypeKey = "Hmi.Label", Name = $"绑定标签{i}" };
                    element.Bindings.Add(new ScadaBinding
                    {
                        TargetProperty = "Value",
                        VariableId = bindingVariableId,
                        VariableName = "Speed"
                    });
                    element.Bindings.Add(new ScadaBinding
                    {
                        TargetProperty = "Visible",
                        VariableName = "Legacy"
                    });
                    bindingPage.Elements.Add(element);
                }
                var bindingBuildMs = sw.ElapsedMilliseconds;

                int bindingTotal = bindingPage.EnumerateBindings().Count();

                sw.Restart();
                int bindingChanged = bindingPage.RefreshVariableReferences(Guid.NewGuid(), "Legacy", "Legacy_2");
                var refreshMs = sw.ElapsedMilliseconds;

                Check($"② 绑定数 {bindingTotal}：枚举条数与改名级联命中数都正确、单位成本不退化",
                    bindingTotal == bindingElements * 2 && bindingChanged == bindingElements && refreshMs < 1000,
                    $"构造 {bindingBuildMs}ms / 级联 {refreshMs}ms（{Per(refreshMs, bindingTotal):F2}µs/绑定）/ 命中 {bindingChanged}");

                baseline.Add($"绑定数 | {bindingTotal} 条（{bindingElements} 图元 × 2）| 级联 {refreshMs}ms = {Per(refreshMs, bindingTotal):F2}µs/绑定 | 命中 {bindingChanged}");

                // ----------------------------------------------------------
                // ③ 切页次数：运行态来回切页
                // 切页是运行态唯一"每次操作都要走完整条链"的动作：
                // 卸旧页 → 换 CurrentPage → 载新页 → 压页栈。
                // 页栈是运行态唯一会随操作时长增长的集合（组态软件连开几个月），
                // 所以除了单次成本，这里必须钉住"栈深有上限、停止后归零"。
                // ----------------------------------------------------------
                const int pageCount = 8;
                const int elementsPerPage = 200;
                const int switchCount = 5000;

                var runtimeDocument = new ScadaDocument();
                var runtimePages = new List<ScadaPage>();
                for (int p = 0; p < pageCount; p++)
                {
                    var runtimePage = runtimeDocument.AddPage($"基线页{p}");
                    for (int e = 0; e < elementsPerPage; e++)
                        runtimePage.Elements.Add(new ScadaElement { TypeKey = "Hmi.Label", Name = $"页{p}标签{e}" });
                    runtimePages.Add(runtimePage);
                }

                var runtime = new ScadaRuntime(runtimeDocument);
                int loadedTimes = 0;
                runtime.PageLoaded += _ => loadedTimes++;
                bool runtimeStarted = runtime.Start();

                sw.Restart();
                for (int i = 0; i < switchCount; i++)
                    runtime.Navigate(runtimePages[(i + 1) % pageCount]);
                var switchMs = sw.ElapsedMilliseconds;

                int stackDepthWhileRunning = runtime.PageStackDepth;
                runtime.Stop();

                Check($"③ 切页 {switchCount} 次：每次都真的换页（Loaded = 切页数 + 1 次启动）且单次成本不退化",
                    runtimeStarted && loadedTimes == switchCount + 1 && switchMs < 5000,
                    $"启动={runtimeStarted} / Loaded={loadedTimes} / 切页 {switchMs}ms（{Per(switchMs, switchCount):F2}µs/次）");

                Check("③ 切页：运行中页栈受上限约束（连开几个月不会无限增长）",
                    stackDepthWhileRunning > 0 && stackDepthWhileRunning <= 32 && stackDepthWhileRunning < switchCount,
                    $"栈深={stackDepthWhileRunning} / 切页={switchCount}");

                Check("③ 切页：停止后当前页与页栈全部归零（运行态不留残渣）",
                    !runtime.IsRunning && runtime.CurrentPage == null
                    && runtime.PageStackDepth == 0 && !runtime.CanGoBack,
                    $"IsRunning={runtime.IsRunning} / 当前页={runtime.CurrentPage?.Name ?? "null"} / 栈深={runtime.PageStackDepth}");

                baseline.Add($"切页次数 | {switchCount} 次（{pageCount} 页 × {elementsPerPage} 图元）| {switchMs}ms = {Per(switchMs, switchCount):F2}µs/次 | 页栈峰值 {stackDepthWhileRunning}、Stop 后归零");

                // ----------------------------------------------------------
                // ④ 报警数：M 条定义同时在线，走完整的"报 → 确认 → 恢复"轮次
                // 报警与前三组的区别：它有一条**会随时长增长的历史**（MaxHistoryRecords）。
                // 所以这一组除单位成本外，必须钉住两件事：
                // ① 历史被削在上限内（10 轮 × 500 条 = 5000 条记录，必须已经削到 2000）；
                // ② 退订后订阅数归零——500 条报警漏掉一条订阅，就是 500 个对象永远回收不掉。
                // ----------------------------------------------------------
                const int alarmCount = 500;
                const int alarmCycles = 10;

                var alarmDocument = new ScadaDocument();
                var alarmSource = new FakeValueSource();
                var alarmHandles = new FakeValueHandle[alarmCount];
                for (int i = 0; i < alarmCount; i++)
                {
                    var handle = new FakeValueHandle
                    {
                        VariableId = Guid.NewGuid(),
                        Name = $"基线变量{i}",
                        DataType = typeof(double),
                        Value = 0d
                    };
                    alarmHandles[i] = handle;
                    alarmSource.ById[handle.VariableId] = handle;

                    var definition = alarmDocument.AddAlarm($"基线报警{i}");
                    definition.Bind(handle.VariableId, handle.Name);
                    definition.Threshold = 50;
                    definition.Deadband = 5;
                }

                // 假时钟：本组不测延时，但引擎的每个公开入口都从它取"现在几点"，
                // 给它一个定值就够（不传时钟也行，这里传是为了和 [AH] 段同一写法）。
                DateTime alarmNow = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
                var alarmEngine = new ScadaAlarmEngine(alarmDocument, alarmSource, () => alarmNow);

                int alarmRaised = 0, alarmCleared = 0;
                alarmEngine.AlarmRaised += _ => alarmRaised++;
                alarmEngine.AlarmCleared += _ => alarmCleared++;

                sw.Restart();
                alarmEngine.Attach(alarmNow); // 初判：500 条全部正常，一条都不该报
                var alarmAttachMs = sw.ElapsedMilliseconds;

                for (int cycle = 0; cycle < alarmCycles; cycle++)
                {
                    for (int i = 0; i < alarmCount; i++)
                        alarmHandles[i].Raise(60d);  // 越限 → 值回调里同步判定 → 全部激活
                    alarmEngine.AcknowledgeAll();    // 操作员按「全部确认」
                    for (int i = 0; i < alarmCount; i++)
                        alarmHandles[i].Raise(0d);   // 回落 → 已确认的直接了结
                    alarmEngine.Tick(alarmNow);      // 再推一拍：覆盖"没有值变化、纯靠节拍"的那条路
                }
                var alarmMs = sw.ElapsedMilliseconds;

                int alarmHistory = alarmEngine.History.Count;
                int alarmActive = alarmEngine.ActiveAlarms.Count;
                int alarmTransitions = alarmCount * alarmCycles;

                Check($"④ 报警数 {alarmCount} 条 × {alarmCycles} 轮（报→确认→恢复）：迁移次数正确、单位成本不退化",
                    alarmRaised == alarmTransitions && alarmCleared == alarmTransitions
                    && alarmActive == 0 && alarmMs < 10000,
                    $"挂载 {alarmAttachMs}ms / 循环 {alarmMs}ms（{Per(alarmMs, alarmTransitions):F2}µs/报警轮）/ 激活={alarmRaised} 了结={alarmCleared} / 实时列表={alarmActive}");

                Check($"④ 报警历史被削在上限内（{alarmTransitions} 条记录 → {ScadaAlarmEngine.MaxHistoryRecords} 条）",
                    alarmHistory == ScadaAlarmEngine.MaxHistoryRecords,
                    $"历史={alarmHistory} / 上限={ScadaAlarmEngine.MaxHistoryRecords}");

                alarmEngine.Detach();
                int lingeringSubscribers = alarmHandles.Sum(h => h.Subscribers);
                Check("④ 报警退订后订阅数归零（500 条一条都不漏，不留无法回收的对象）",
                    !alarmEngine.IsAttached && alarmEngine.SubscribedCount == 0 && lingeringSubscribers == 0,
                    $"引擎订阅={alarmEngine.SubscribedCount} / 通道残留={lingeringSubscribers}");

                baseline.Add($"报警数 | {alarmCount} 条 × {alarmCycles} 轮 | 循环 {alarmMs}ms = {Per(alarmMs, alarmTransitions):F2}µs/报警轮 | 历史 {alarmHistory}/{ScadaAlarmEngine.MaxHistoryRecords} | 退订残留 {lingeringSubscribers}");

                // ----------------------------------------------------------
                // 基线数字留档块：断言只管量级，这一块才是"看数字"的那一份。
                // 每次改动跑完把它整段粘进 docs/code-changes 的开发记录即可对比。
                // ----------------------------------------------------------
                Console.WriteLine();
                Console.WriteLine("  ┌─ 基线数字（留档：粘贴进 docs/code-changes）────────────────────");
                foreach (var line in baseline)
                    Console.WriteLine($"  │ {line}");
                Console.WriteLine("  └────────────────────────────────────────────────────────────────");
            }
            finally
            {
                ScadaEditHistory.Clear(); // 撤销栈不能把本段的压栈留给后面的段
            }
        }

        /// <summary>
        /// 把"总毫秒 / 次数"折算成"每次多少微秒"。
        /// 规模与机器都会变，单位成本才是能跨版本、跨机器对比的那个数字。
        /// </summary>
        private static double Per(long milliseconds, int count)
            => count <= 0 ? 0 : milliseconds * 1000.0 / count;

        // ==================================================================
        //  [ST] 长稳压测：200 绑定 × 20ms × 60s（--stress，S13-b）
        //
        //  与 [BL] 的分工：[BL] 量的是"瞬时单位成本"（一次操作摊多少 µs），
        //  本段量的是"持续负载下的曲线"——同一份负载连跑 60s，句柄数、订阅数、
        //  内存、节拍都不许随时长往上走。它补的是路线图 S6 唯一未闭合项：
        //  「1920×1080 画面挂 200 个绑定，接模拟变量以 20ms 周期跳变，跑 60s，
        //    UI 不卡、内存不涨、停止后句柄与订阅归零」。
        //
        //  已知边界：这是**单进程内的等价负载**——没有真窗口、没有 GPU 合成、
        //  也不是 8 小时。真机 8 小时长稳仍挂在路线图 S13 的"长稳"一条上。
        // ==================================================================
        private static void SoakRuntimeBinder()
        {
            Section("[ST] 长稳压测：200 绑定 × 20ms × 60s（--stress，S13-b）");

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { RunSoakRuntimeBinderChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("长稳压测全程未抛异常", false, failure.ToString());
        }

        private static void RunSoakRuntimeBinderChecks()
        {
            const int bindingCount = 200;   // 路线图口径：200 个绑定
            const int intervalMs = 20;      // 路线图口径：20ms 周期跳变
            const int warmupMs = 2000;      // 预热段：JIT / 模板解析 / 首次刷帧都发生在这里
            const int mainMs = 58000;       // 正式段：预热 + 正式 = 60s

            // ---------------- 装配：1920×1080 画面 + 200 图元 / 200 绑定 / 200 变量 ----------------

            var doc = new ScadaDocument();
            var page = doc.AddPage("压力画面");   // 尺寸取 ScadaPage 默认值（1920×1080），与路线图口径一致
            var layer = page.Layers.FirstOrDefault() ?? page.AddLayer();

            var source = new FakeValueSource();
            var handles = new FakeValueHandle[bindingCount];

            for (int i = 0; i < bindingCount; i++)
            {
                var handle = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(),
                    Name = $"压力变量{i}",
                    DataType = typeof(double),
                    Value = 0d,
                };
                handles[i] = handle;
                source.ById[handle.VariableId] = handle;

                // 20 列 × 10 行铺开：图元位置本身不参与断言，但必须真铺开——
                // 挤在同一个点上会被渲染层的可见性裁剪跳过一大片，那就压了个空。
                var element = ElementRegistry.CreateElement("Hmi.Text", 20 + i % 20 * 95, 20 + i / 20 * 30)!;
                element.Name = $"压力文本{i}";
                element.Width = 90;
                element.Height = 24;
                element.ZIndex = i + 1;
                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Text",
                    VariableId = handle.VariableId,
                    VariableName = handle.Name,
                    DisplayFormat = "F1",
                });
                page.Elements.Add(element);
                page.TryAssignLayer(element, layer, out _);
            }

            Check("压力画面按路线图口径装配：1920×1080（ScadaPage 默认尺寸）+ 200 图元 / 200 条绑定 / 200 个变量",
                page.Width == 1920 && page.Height == 1080
                && page.Elements.Count == bindingCount
                && page.EnumerateBindings().Count() == bindingCount
                && handles.Select(h => h.VariableId).Distinct().Count() == bindingCount,
                $"{page.Width}×{page.Height} / 图元 {page.Elements.Count} / 绑定 {page.EnumerateBindings().Count()}");

            var canvas = BuildBinderCanvas(page);
            var controls = canvas.EnumerateControls().ToList();
            var designTexts = controls.Select(c => c.Text).ToArray();   // Stop 之后要回到的"设计值"

            // 取值断言只认这一个图元：按引用配对，避免依赖 EnumerateControls 的返回顺序
            var probeElement = page.Elements[0];
            var probeControl = controls.First(c => ReferenceEquals(c.Element, probeElement));

            var binder = new ScadaRuntimeBinder(canvas, source, page);
            binder.Start();

            Check("建表：200 条绑定全部命中、0 未命中；一个变量一份订阅，200 个句柄各挂 1 个订阅者",
                binder.BoundCount == bindingCount && binder.MissCount == 0
                && binder.SubscriptionCount == bindingCount
                && handles.All(h => h.Subscribers == 1),
                $"命中 {binder.BoundCount} / 未命中 {binder.MissCount} / 订阅 {binder.SubscriptionCount}");

            // ---------------- 20ms 周期跳变：后台线程推值，UI 线程只负责刷 ----------------

            // 生产上值变化来自后台轮询线程（IScadaValueHandle.ValueChanged 的文档写明"可能在非 UI 线程"），
            // 所以这里也用后台计时器推值：UI 线程不生产数据、只消费，才测得出"UI 会不会被压垮"。
            // 若让 UI 线程自己推值，这个测法就是循环论证。
            var lastRaised = new double[bindingCount];
            int ticks = 0;
            int inFlight = 0;
            var raiser = new System.Threading.Timer(_ =>
            {
                Interlocked.Increment(ref inFlight);
                try
                {
                    int tick = Interlocked.Increment(ref ticks);
                    for (int i = 0; i < lastRaised.Length; i++)
                    {
                        double value = (tick + i) % 1000;
                        lastRaised[i] = value;
                        handles[i].Raise(value);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }, null, Timeout.Infinite, Timeout.Infinite);

            // UI 线程心跳探针：它的"间隔"就是 UI 线程的可用性——
            // 刷 200 个绑定若把 UI 线程占住，探针的间隔会立刻被拉长。
            long maxGapMs = 0;
            long lastProbeMs = 0;
            var probeWatch = Stopwatch.StartNew();
            var probe = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs),
            };
            probe.Tick += (_, _) =>
            {
                long now = probeWatch.ElapsedMilliseconds;
                long gap = now - lastProbeMs;
                lastProbeMs = now;
                if (gap > maxGapMs)
                    maxGapMs = gap;
            };

            // 停推值 → 等在途回调落地 → 把最后一拍排队的帧排空。
            // 等 inFlight 归零是为了让"最后一拍的值"可确定：否则读控件与读 lastRaised 之间
            // 可能夹进一拍，断言会偶发假红。
            void PauseRaiser()
            {
                raiser.Change(Timeout.Infinite, Timeout.Infinite);
                SpinWait.SpinUntil(() => Volatile.Read(ref inFlight) == 0, 2000);
                PumpDispatcher(200);
            }

            probe.Start();
            raiser.Change(0, intervalMs);

            // ---- 预热段（2s ≈ 100 拍）：让 JIT / 模板 / 首次刷帧先发生完 ----
            PumpDispatcher(warmupMs);

            // ---- 中途取样：句柄 / 订阅 / 角标都不许随时长增长 ----
            PauseRaiser();
            int midTicks = ticks;
            int midSubscriptions = binder.SubscriptionCount;
            int midBound = binder.BoundCount;
            int midDiagnostics = canvas.Diagnostics.Count;
            int midLingering = handles.Sum(h => h.Subscribers);
            long midMemory = GC.GetTotalMemory(true);
            string midText = probeControl.Text;
            double midExpected = lastRaised[0];

            // ---- 正式段（58s）：预热 + 正式 = 60s，与路线图口径一致 ----
            maxGapMs = 0;
            lastProbeMs = 0;
            probeWatch.Restart();
            raiser.Change(0, intervalMs);
            PumpDispatcher(mainMs);
            PauseRaiser();
            probe.Stop();

            long endMemory = GC.GetTotalMemory(true);
            int totalTicks = ticks;
            long uiMaxGapMs = maxGapMs;
            string endText = probeControl.Text;
            double endExpected = lastRaised[0];

            Check($"跑到中途（{midTicks} 拍）时：订阅数与命中数都还停在 200、无角标、200 个句柄各 1 个订阅者（句柄不随时长增长）",
                midSubscriptions == bindingCount && midBound == bindingCount
                && midDiagnostics == 0 && midLingering == bindingCount,
                $"订阅 {midSubscriptions} / 命中 {midBound} / 角标 {midDiagnostics} / 句柄订阅者合计 {midLingering}");

            Check("预热段的值已经真的落到控件上（走的是生产路径：后台线程改值 → 脏表 → BeginInvoke → 刷帧，没拿 FlushNow 抄近路）",
                midText == midExpected.ToString("F1"),
                $"「{midText}」 vs 期望「{midExpected.ToString("F1")}」");

            Check($"60s 内跑出 {totalTicks} 拍（目标 3000 拍 = 60s ÷ 20ms，留 20% 余量）：持续负载没把节拍拖垮",
                totalTicks >= 2400, $"{totalTicks} 拍 / 目标 3000 拍");

            Check($"UI 线程全程最长停顿 {uiMaxGapMs}ms（<1000ms 才算「不卡」；精确数字看段末留档块）",
                uiMaxGapMs < 1000, $"{uiMaxGapMs}ms");

            Check($"58s 持续负载后内存不涨（强制 GC 后与中途基线对比，增长 {(endMemory - midMemory) / 1024}KB）",
                endMemory - midMemory < 8 * 1024 * 1024,
                $"{midMemory / 1024}KB → {endMemory / 1024}KB（+{(endMemory - midMemory) / 1024}KB）");

            Check("跑完 60s 后控件上是最新值（负载跑满整个时长，最后一拍的值也没被丢掉）",
                endText == endExpected.ToString("F1"),
                $"「{endText}」 vs 期望「{endExpected.ToString("F1")}」");

            // ---------------- 停止：句柄与订阅一起归零，图元回设计值 ----------------

            binder.Stop();

            Check("停止后句柄与订阅一起归零（200 份订阅、200 个句柄一条都不许留下——变量注册表是长生命周期对象）",
                !binder.IsRunning && binder.SubscriptionCount == 0
                && handles.Sum(h => h.Subscribers) == 0
                && binder.BoundCount == 0 && binder.MissCount == 0
                && canvas.Diagnostics.Count == 0,
                $"订阅 {binder.SubscriptionCount} / 句柄订阅者合计 {handles.Sum(h => h.Subscribers)} / 命中 {binder.BoundCount} / 角标 {canvas.Diagnostics.Count}");

            int notRestored = controls.Select((c, i) => c.Text == designTexts[i]).Count(ok => !ok);
            Check("停止后 200 个图元全部回到设计值（运行值只活在控件上，不需要任何恢复现场的备份表）",
                notRestored == 0, $"{notRestored} 个没回去");

            // ----------------------------------------------------------
            // 长稳数字留档块：断言只管量级，这一块才是"看曲线"的那一份。
            // 每次改动跑完把它整段粘进 docs/code-changes 的开发记录即可对比。
            // ----------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("  ┌─ 长稳数字（留档：粘贴进 docs/code-changes）────────────────────");
            Console.WriteLine($"  │ 装配 | {page.Width}×{page.Height} | {bindingCount} 图元 / {bindingCount} 绑定 / {bindingCount} 变量 | 建表命中 {bindingCount}、未命中 0、订阅 {bindingCount}");
            Console.WriteLine($"  │ 节拍 | 目标 20ms × 60s = 3000 拍 | 实跑 {totalTicks} 拍 = {Per(60000, totalTicks) / 1000:F2}ms/拍");
            Console.WriteLine($"  │ UI  | 最长停顿 {uiMaxGapMs}ms");
            Console.WriteLine($"  │ 内存 | 中途 {midMemory / 1024}KB → 终了 {endMemory / 1024}KB（+{(endMemory - midMemory) / 1024}KB）");
            Console.WriteLine($"  │ 订阅 | 建表 {bindingCount} → 中途 {midSubscriptions} → 停止 0");
            Console.WriteLine("  └────────────────────────────────────────────────────────────────");
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
                "Hmi.Rectangle", "Hmi.Ellipse", "Hmi.Text", "Hmi.Button", "Hmi.BitButton", "Hmi.Indicator",
                "Hmi.ProgressBar", "Hmi.IOField", "Hmi.Lamp", "Hmi.Clock", "Hmi.Gauge",
                "Hmi.Valve", "Hmi.Pump", "Hmi.Motor", "Hmi.Pipe", "Hmi.AlarmBanner",
            };
            Check("16 个内置图元全部注册",
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

            // 候选是落盘值（Circle、Output 这类英文），面板要显示中文——译名在 ScadaChoiceNames 那张
            // 全局词汇表里。判据取"纯 ASCII"：候选本来就是中文的（位按钮的模式：置位/复位/取反）天然通过，
            // 英文候选则必须有译名。这条断言是给将来加图元的人兜底的——新加一个英文候选忘了配译名，
            // 表现是"面板上孤零零一个英文单词"，没人会主动去查，放在这里当场就红。
            string[] untranslated = ElementRegistry.All.SelectMany(d => d.Properties)
                .Where(p => p.Kind == ElementPropertyKind.Choice)
                .SelectMany(p => p.Choices)
                .Where(c => c.Length > 0 && c.All(ch => ch < 128))
                .Distinct()
                .Where(c => ScadaChoiceNames.DisplayName(c) == c)
                .ToArray();
            Check("Choice 候选里凡是英文的都有中文译名（漏配一个就会在这里被抓住）",
                untranslated.Length == 0, string.Join("、", untranslated));

            // S7 的验收口径里有一条「描述符必须声明可绑变量类型」：指示 / 数值类图元的那个"输入"
            // 属性一定要 IsBindable，否则运行态接不上变量，图元就只是个静态装饰——
            // 而"图元画得出来但绑不了变量"正是 D2（组态 → 运行）最想避免的那种半成品。
            string[] bindableInputs = { "Hmi.ProgressBar:Value", "Hmi.IOField:Value", "Hmi.Lamp:State", "Hmi.Gauge:Value", "Hmi.Valve:Opening", "Hmi.Pump:State", "Hmi.Motor:State", "Hmi.Pipe:State" };
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

            // ----------------------------------------------------------
            // 数值域：可读可写（S9）——模式 / 格式类型 / 缩放 / 上下限 / 就地编辑
            // ----------------------------------------------------------
            //
            // 这一段钉住三件事，一条像素都不数：
            //   ① 能不能改（IsEditable）= 模式允许写 且 宿主给了写通道 且 「数值」真绑了变量；
            //   ② 显示什么（ValueText / DisplayBrush）= 变量值 × 增益 + 偏移量，再按格式类型排版；
            //   ③ 提交交给谁 = IScadaValueWriter（图元自己一个字节都不碰变量）。
            var ioElement = ElementRegistry.CreateElement("Hmi.IOField", 0, 0);
            var io = (IOFieldElement)ElementRegistry.CreateControl(ioElement);
            var ioWriter = new FakeValueWriter();

            var ioEngine = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
            var ioBeat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var ioMonitorContext = new ScadaRuntimeContext(ioEngine, ioBeat);            // 纯监视画面：没写通道
            var ioContext = new ScadaRuntimeContext(ioEngine, ioBeat, ioWriter);         // 能改值：带写通道

            var ioBinding = ioElement.GetOrAddBinding("Value");
            ioBinding.Bind(Guid.NewGuid(), "SetPoint");

            ioElement.SetProperty("Value", "10");
            io.Refresh();

            Check("数值域（默认只读）：设计态没装运行上下文 → IsEditable=false（编辑器里的预览点不出输入框）",
                !io.IsEditable && io.Mode == IOFieldElement.ModeOutput,
                $"mode={io.Mode} / editable={io.IsEditable}");

            io.RuntimeContext = ioContext;
            Check("数值域（默认只读）：装好上下文、绑好变量，模式仍是 Output → 还是不能改（老方案行为一点没变）",
                !io.IsEditable, $"mode={io.Mode} / editable={io.IsEditable}");

            io.BeginEdit();
            Check("数值域（默认只读）：只读模式下 BeginEdit 是空操作（点了不该弹出键盘，更不该改到变量）",
                !io.IsEditing && ioWriter.Writes.Count == 0,
                $"editing={io.IsEditing} / 写入次数={ioWriter.Writes.Count}");

            ioElement.SetProperty("Mode", IOFieldElement.ModeInputOutput);
            io.Refresh();
            Check("数值域：模式选「输入输出」+ 绑了变量 + 有写通道 → 三个条件齐了才可编辑",
                io.IsEditable, $"editable={io.IsEditable}");

            io.RuntimeContext = ioMonitorContext;
            Check("数值域：宿主没给写通道（纯监视画面）→ 又变回不可编辑（「点了没反应」好过「敲完才说写不了」）",
                !io.IsEditable, $"editable={io.IsEditable}");

            io.RuntimeContext = ioContext;
            ioBinding.IsEnabled = false;
            io.Refresh();
            Check("数值域：绑定被停用 → 不可编辑（读都不生效了写更不该生效，否则会出现「值不动、一改变量就变」）",
                !io.IsEditable, $"editable={io.IsEditable}");

            ioBinding.IsEnabled = true;
            io.Refresh();

            // ② 显示换算：现场量纲 ≠ 画面量纲时靠它俩对齐
            ioElement.SetProperty("Gain", "2");
            ioElement.SetProperty("Offset", "5");
            io.Refresh();
            Check("数值域：显示值 = 变量值 × 增益 + 偏移量（10 × 2 + 5 = 25；PLC 存 0.1℃ 整数、画面要 ℃ 就是靠它）",
                io.ValueText == "25", io.ValueText ?? "null");

            io.BeginEdit();
            Check("数值域：进编辑态预填的是「显示值」而不是变量原值（操作员改的是他看到的那个量纲）",
                io.IsEditing && io.EditText == "25", $"editing={io.IsEditing} / edit=\"{io.EditText}\"");

            io.EditText = "30";
            io.CommitEdit();
            Check("数值域：提交时按「变量值 = (输入值 − 偏移量) ÷ 增益」反算写回（(30 − 5) ÷ 2 = 12.5）",
                ioWriter.Writes.Count == 1
                && ioWriter.Writes[0].Target == "Value"
                && ioWriter.Writes[0].Text == "12.5",
                ioWriter.Writes.Count == 1 ? ioWriter.Writes[0].Text ?? "null" : $"写入次数={ioWriter.Writes.Count}");

            Check("数值域：提交成功即退出编辑态、清掉错误，且图元自己不改 Value（值由变量持有，数据泵读回来显示）",
                !io.IsEditing && io.EditError == null && io.Value == 10,
                $"editing={io.IsEditing} / error={io.EditError ?? "null"} / value={io.Value}");

            ioElement.SetProperty("Gain", "1");
            ioElement.SetProperty("Offset", "0");
            io.Refresh();
            io.BeginEdit();
            io.EditText = "23.50";
            io.CommitEdit();
            Check("数值域：增益 1 / 偏移 0 时把原文原样交出去（「23.50」这种写法与「写变量」动作同一条转换口径）",
                ioWriter.Writes.Count == 2 && ioWriter.Writes[1].Text == "23.50",
                ioWriter.Writes.Count == 2 ? ioWriter.Writes[1].Text ?? "null" : $"写入次数={ioWriter.Writes.Count}");

            // ③ 输入限制：越界不写下去，也不夹到边界值
            ioElement.SetProperty("HasMinimum", "True");
            ioElement.SetProperty("Minimum", "0");
            ioElement.SetProperty("HasMaximum", "True");
            ioElement.SetProperty("Maximum", "100");
            io.Refresh();

            io.BeginEdit();
            io.EditText = "200";
            io.CommitEdit();
            Check("数值域：超过上限不写入、留在编辑态并给出原因（夹到边界值是在静默改操作员敲的数，比拒绝更危险）",
                io.IsEditing && io.EditError == "高于上限 100，未写入" && ioWriter.Writes.Count == 2,
                $"editing={io.IsEditing} / error={io.EditError ?? "null"} / 写入次数={ioWriter.Writes.Count}");

            io.EditText = "-5";
            io.CommitEdit();
            Check("数值域：低于下限同样不写入（上下限都按显示量纲填，与操作员看到的刻度一致）",
                io.IsEditing && io.EditError == "低于下限 0，未写入" && ioWriter.Writes.Count == 2,
                $"error={io.EditError ?? "null"} / 写入次数={ioWriter.Writes.Count}");

            io.EditText = "abc";
            io.CommitEdit();
            Check("数值域：敲进去的不是数 → 不写入，原因带上原文（操作员照着自己敲的那串字改就能过）",
                io.IsEditing && io.EditError == "「abc」不是有效数值，未写入" && ioWriter.Writes.Count == 2,
                io.EditError ?? "null");

            io.EditText = "   ";
            io.CommitEdit();
            Check("数值域：把框清空再提交 = 放弃这次输入（与 Esc 同义，不会把一个空值写下去）",
                !io.IsEditing && io.EditError == null && ioWriter.Writes.Count == 2,
                $"editing={io.IsEditing} / 写入次数={ioWriter.Writes.Count}");

            ioWriter.FailReason = "变量「SetPoint」所在设备离线，写入被拒绝";
            io.BeginEdit();
            io.EditText = "50";
            io.CommitEdit();
            Check("数值域：写通道报错 → 留在编辑态、原因原样显示（设备侧的真实原因只有那一刻拿得到，包一层就丢了）",
                io.IsEditing && io.EditError == "变量「SetPoint」所在设备离线，写入被拒绝",
                io.EditError ?? "null");

            ioWriter.FailReason = null;
            io.CancelEdit();
            Check("数值域：CancelEdit 退出编辑态并清掉错误，Value 一个字节都没动",
                !io.IsEditing && io.EditError == null && io.Value == 10,
                $"editing={io.IsEditing} / value={io.Value}");

            ioElement.SetProperty("Gain", "0");
            io.Refresh();
            io.BeginEdit();
            io.EditText = "10";
            io.CommitEdit();
            Check("数值域：增益为 0 时拒绝输入并说明原因（反算会得无穷大，不能默默写下去）",
                io.IsEditing && io.EditError == "显示换算增益为 0，无法算出要写入的变量值",
                io.EditError ?? "null");

            io.CancelEdit();
            ioElement.SetProperty("Gain", "1");
            io.Refresh();

            // ④ 超限变色：颜色算在只读属性里，模板一条 Trigger 都不用写
            ioElement.SetProperty("Value", "120");
            io.Refresh();
            Check("数值域：显示值超上限 → 数值文字换成「上限以上颜色」（断言直接读这个只读属性，不必数像素）",
                ReferenceEquals(io.DisplayBrush, io.OverMaxColor),
                io.DisplayBrush?.ToString() ?? "null");

            ioElement.SetProperty("Value", "-20");
            io.Refresh();
            Check("数值域：显示值低于下限 → 换成「下限以下颜色」",
                ReferenceEquals(io.DisplayBrush, io.UnderMinColor),
                io.DisplayBrush?.ToString() ?? "null");

            ioElement.SetProperty("Value", "50");
            io.Refresh();
            Check("数值域：回到范围内 → 数值文字还原成图元自己的前景色",
                ReferenceEquals(io.DisplayBrush, io.Foreground),
                io.DisplayBrush?.ToString() ?? "null");

            // ⑤ 出错描红：边框色只被"提交失败"这一件事占用（编辑态的提示走输入框那层淡蓝底，不抢这个属性）
            io.BeginEdit();
            io.EditText = "999";
            io.CommitEdit();
            Check("数值域：提交失败时框子描红，用的是固定的报警红（报警语义的颜色不该被皮肤改掉）",
                io.DisplayStroke is SolidColorBrush { Color: var strokeOnError }
                && strokeOnError == Color.FromRgb(0xD3, 0x2F, 0x2F),
                io.DisplayStroke?.ToString() ?? "null");

            io.CancelEdit();
            Check("数值域：退出编辑态后描边回到图元自己的「边框色」（#FF7A7A7A）",
                io.DisplayStroke is SolidColorBrush { Color: var strokeNormal }
                && strokeNormal == Color.FromRgb(0x7A, 0x7A, 0x7A),
                io.DisplayStroke?.ToString() ?? "null");

            // ⑥ 格式类型：进制只影响显示与输入，变量里存的始终是同一个十进制数
            // 先关掉上下限——255 本来就超出 0~100 那条限制，留着它会掩盖这里真正要验的东西
            ioElement.SetProperty("HasMinimum", "False");
            ioElement.SetProperty("HasMaximum", "False");
            ioElement.SetProperty("FormatType", IOFieldElement.FormatHex);
            ioElement.SetProperty("Value", "31");
            io.Refresh();
            Check("数值域：格式类型选十六进制 → 按整数出 1F（位状态、字状态、设备地址都当整数看）",
                io.ValueText == "1F", io.ValueText ?? "null");

            io.BeginEdit();
            Check("数值域：十六进制下进编辑态预填的也是 1F（敲什么进制读什么进制）",
                io.EditText == "1F", io.EditText ?? "null");

            io.EditText = "FF";
            io.CommitEdit();
            Check("数值域：十六进制下敲 FF → 写回变量的仍是十进制 255（若把「FF」原样交出去，转换器只会回一句「转不成 Double」）",
                ioWriter.Writes[^1].Text == "255", ioWriter.Writes[^1].Text ?? "null");

            ioElement.SetProperty("FormatType", IOFieldElement.FormatBinary);
            io.Refresh();
            Check("数值域：格式类型选二进制 → 11111（31 的二进制）",
                io.ValueText == "11111", io.ValueText ?? "null");

            ioElement.SetProperty("FormatType", IOFieldElement.FormatDecimal);
            io.Refresh();

            // ⑦ 只写模式：值只往变量里送，不往画面里拉
            ioElement.SetProperty("Mode", IOFieldElement.ModeInput);
            ioElement.SetProperty("Value", "7");
            io.Refresh();
            Check("数值域（只写模式）：同样可编辑（模式允许写 + 有写通道 + 绑了变量）",
                io.IsEditable, $"editable={io.IsEditable}");

            var ioValueSpec = ElementRegistry.FindProperty("Hmi.IOField", "Value")!;
            var ioApplied = io.TryApplyRuntimeValue(ioValueSpec, 88d, null, out var ioApplyError);
            Check("数值域（只写模式）：变量刷新被认下但不落到控件上（否则操作员正打字、数字自己跳走）",
                ioApplied && ioApplyError == null && io.Value == 7,
                $"applied={ioApplied} / value={io.Value}");

            io.BeginEdit();
            io.EditText = "42";
            io.CommitEdit();
            Check("数值域（只写模式）：提交成功后本地承接这次输入（那条变量刷新被拒收了，不接一下框里会一直停在旧数）",
                io.Value == 42 && ioWriter.Writes[^1].Text == "42",
                $"value={io.Value} / 写出=\"{ioWriter.Writes[^1].Text}\"");

            // ⑧ 描述符：新属性必须落在描述符里，属性面板才会出现它们
            var ioSpec = ElementRegistry.Find("Hmi.IOField")!;
            Check("数值域描述符：模式/格式类型/增益/偏移四条落「数据」组，上下限与超限色六条落新增的「限制」组",
                ioSpec.Properties.Any(p => p.Key == "Mode" && p.Group == "数据" && p.Kind == ElementPropertyKind.Choice)
                && ioSpec.Properties.Any(p => p.Key == "FormatType" && p.Group == "数据" && p.Kind == ElementPropertyKind.Choice)
                && ioSpec.Properties.Any(p => p.Key == "Gain" && p.Group == "数据")
                && ioSpec.Properties.Any(p => p.Key == "Offset" && p.Group == "数据")
                && ioSpec.Properties.Any(p => p.Key == "HasMinimum" && p.Group == "限制")
                && ioSpec.Properties.Any(p => p.Key == "Minimum" && p.Group == "限制")
                && ioSpec.Properties.Any(p => p.Key == "UnderMinColor" && p.Group == "限制")
                && ioSpec.Properties.Any(p => p.Key == "HasMaximum" && p.Group == "限制")
                && ioSpec.Properties.Any(p => p.Key == "Maximum" && p.Group == "限制")
                && ioSpec.Properties.Any(p => p.Key == "OverMaxColor" && p.Group == "限制"),
                string.Join(" / ", ioSpec.Properties.Where(p => p.Group == "限制").Select(p => p.Key)));

            Check("数值域描述符：模式三条候选值就是三个常量（属性面板的下拉直接吃这份声明，两处不会各写一份）",
                ioSpec.Properties.First(p => p.Key == "Mode").Choices.SequenceEqual(
                    new[] { IOFieldElement.ModeOutput, IOFieldElement.ModeInput, IOFieldElement.ModeInputOutput })
                && ioSpec.Properties.First(p => p.Key == "FormatType").Choices.SequenceEqual(
                    new[] { IOFieldElement.FormatDecimal, IOFieldElement.FormatHex, IOFieldElement.FormatBinary }),
                string.Join(",", ioSpec.Properties.First(p => p.Key == "Mode").Choices));

            // ⑨ 「输入完成时」事件：只在"提交成功"那一刻发（手册 7.5.2 把它挂在数字IO域上）
            //
            // 这里直接订控件自己的 ScadaEvent，不经运行窗口：RaiseScadaEvent 本就发在控件本尊上，
            // 冒泡是给运行窗口"一根 handler 收全画面"用的，断言不需要一个窗口来当中转。
            // 三条"没成"的路径各走一次，确保它们一条都不发——这条事件的全部价值就在这个分界上。
            var ioEvents = new List<ScadaEventType>();
            io.ScadaEvent += (_, e) => ioEvents.Add(e.ScadaEvent);

            ioElement.SetProperty("Mode", IOFieldElement.ModeInputOutput);
            ioElement.SetProperty("HasMinimum", "True");
            ioElement.SetProperty("Minimum", "0");
            ioElement.SetProperty("HasMaximum", "True");
            ioElement.SetProperty("Maximum", "100");
            io.Refresh();

            io.BeginEdit();
            io.EditText = "abc";
            io.CommitEdit();
            Check("数值域：敲的不是数 → 不发「输入完成时」（留在编辑态、错误摆在框边，改一下就能重提）",
                ioEvents.Count == 0 && io.IsEditing && !string.IsNullOrEmpty(io.EditError),
                $"事件 {ioEvents.Count} 次 / editing={io.IsEditing} / error={io.EditError ?? "null"}");

            io.EditText = "150";
            io.CommitEdit();
            Check("数值域：高于上限被拒 → 不发（那一次没算数；发了就等于告诉操作员「你刚才那次成了」，比不发更糟）",
                ioEvents.Count == 0 && io.IsEditing, $"事件 {ioEvents.Count} 次 / editing={io.IsEditing}");

            io.EditText = "42";
            io.CommitEdit();
            Check("数值域：提交成功 → 发一次「输入完成时」，且此刻已退出编辑态、错误已清（动作看到的是「这件事真成了」）",
                ioEvents.Count == 1 && ioEvents[0] == ScadaEventType.InputCompleted
                && !io.IsEditing && io.EditError == null,
                $"事件 {ioEvents.Count} 次 / 首条={ioEvents.FirstOrDefault()} / editing={io.IsEditing}");

            io.BeginEdit();
            io.CancelEdit();
            Check("数值域：放弃编辑（Esc / 清空）→ 不发（什么都没提交，谈不上「完成」）",
                ioEvents.Count == 1 && !io.IsEditing, $"事件 {ioEvents.Count} 次");

            Check("「输入完成时」的中文显示名与枚举值都钉住（属性面板那一行、日志里那一句都靠它；数值 9 一旦发布不许改）",
                ScadaEventType.InputCompleted.DisplayName() == "输入完成时"
                && (int)ScadaEventType.InputCompleted == 9
                && ScadaEventType.InputCompleted != ScadaEventType.ValueChanged,
                $"{(int)ScadaEventType.InputCompleted} / {ScadaEventType.InputCompleted.DisplayName()}");

            // ----------------------------------------------------------
            // 位按钮（N-3）：一根开关量的操作化身——六模式 / 双色双文本 / 可操作三条件
            // ----------------------------------------------------------
            //
            // 手册把「按钮」与「位按钮」分成两个控件（7.3.3.5 / 7.3.3.7）：按钮是动作入口
            // （点下去干什么由动作表决定），位按钮是变量的化身（绑一根开关量，点击直接对这根量做位操作）。
            // 这一段钉住三件事，一条像素都不数：
            //   ① 显示什么（DisplayText / DisplayFill / DisplayForeground）= 状态 + 六条状态外观推出来的；
            //   ② 按下/释放各写什么 = 六模式各自的位语义（写出去的是 "1"/"0"，
            //      落到 bool 变量靠的是既有转换器认 1/0、true/false、是/否——不必新增动作类型）；
            //   ③ 能不能操作（CanOperate）= 有写通道 且 「读变量」真绑了变量 且 没被禁用。
            //
            // 按下/释放不伪造鼠标事件，而是走控件公开的 BeginPress / ReleasePress / CancelPress
            // （与数值域的 BeginEdit 同一先例）：MouseEventArgs 连公开构造函数都没有，
            // 而"交互的唯一真相"本来就该留在控件里，断言只是它的另一个调用方。
            var bitElement = ElementRegistry.CreateElement("Hmi.BitButton", 0, 0);
            var bit = (BitButtonElement)ElementRegistry.CreateControl(bitElement);
            var bitWriter = new FakeValueWriter();
            var bitContext = new ScadaRuntimeContext(ioEngine, ioBeat, bitWriter);   // 能操作：带写通道
            var bitMonitorContext = new ScadaRuntimeContext(ioEngine, ioBeat);       // 纯监视画面：没写通道

            var bitBinding = bitElement.GetOrAddBinding("IsOn");
            bitBinding.Bind(Guid.NewGuid(), "MotorRun");

            Check("位按钮：构造即算第一帧（输入的默认值与描述符完全一致，变更回调一个都不响，所以必须自己算一遍）",
                bit.DisplayText == "按钮" && !bit.IsPressed
                && bit.DisplayFill is SolidColorBrush { Color: var bitOffDefault }
                && bitOffDefault == Color.FromArgb(0xFF, 0x4A, 0x55, 0x68),
                $"{bit.DisplayText} / {bit.DisplayFill}");

            bitElement.SetProperty("OnText", "运行");
            bitElement.SetProperty("OffText", "停止");
            bitElement.SetProperty("IsOn", "True");
            bit.Refresh();
            Check("位按钮：状态 1 时文字与背景一起切到「状态1」那一套（模板里一条 DataTrigger 都不写，颜色是算出来的）",
                bit.DisplayText == "运行"
                && bit.DisplayFill is SolidColorBrush { Color: var bitOnFill }
                && bitOnFill == Color.FromArgb(0xFF, 0x2D, 0x7D, 0xD2),
                $"{bit.DisplayText} / {bit.DisplayFill}");

            bit.OutputInvert = true;
            Check("位按钮：输出反向只翻「状态」判定（低有效信号靠它对齐），两套外观随判定一起换",
                !bit.State && bit.DisplayText == "停止",
                $"state={bit.State} / {bit.DisplayText}");

            bit.OutputInvert = false;
            bitElement.SetProperty("IsOn", "False");
            bit.Refresh();
            Check("位按钮：状态 0 显示另一套（同一根变量、两种长相——启停/手自动这类按钮全靠它）",
                bit.DisplayText == "停止", bit.DisplayText ?? "null");

            bitElement.SetProperty("OnText", string.Empty);
            bitElement.SetProperty("OffText", string.Empty);
            bit.Refresh();
            Check("位按钮：两条状态文字都留空就回落到「文字」（只想换个颜色、字不变的那种按钮只填一处）",
                bit.DisplayText == "按钮", bit.DisplayText ?? "null");

            // --- ② 六模式的写出 ---
            bit.RuntimeContext = bitContext;

            // 跑一轮"按下 → 释放/滑开"，分别返回按下侧与释放侧写出去的文本（空 = 一条都没写）
            (List<string?> Press, List<string?> Release) Round(string mode, bool cancel)
            {
                bitElement.SetProperty("Mode", mode);
                bit.Refresh();

                int from = bitWriter.Writes.Count;
                bit.BeginPress();
                int afterPress = bitWriter.Writes.Count;
                if (cancel) bit.CancelPress(); else bit.ReleasePress();

                return (bitWriter.Writes.Skip(from).Take(afterPress - from).Select(w => w.Text).ToList(),
                        bitWriter.Writes.Skip(afterPress).Select(w => w.Text).ToList());
            }

            static string Texts((List<string?> Press, List<string?> Release) round)
                => $"按下[{string.Join(",", round.Press.Select(t => t ?? "null"))}] / "
                 + $"释放[{string.Join(",", round.Release.Select(t => t ?? "null"))}]";

            var bitSet = Round(BitButtonElement.ModeSet, cancel: false);
            Check("位按钮（置位）：按下一条都不写、只亮按下态，释放才写 1（「置位」= 这一下确认后才动现场量）",
                bitSet.Press.Count == 0 && bitSet.Release.Count == 1 && bitSet.Release[0] == "1",
                Texts(bitSet));

            Check("位按钮：写请求的落点就是「读变量」那个键（读写同一根量，回写按它找绑定）",
                bitWriter.Writes.Count > 0 && bitWriter.Writes[^1].Target == "IsOn",
                bitWriter.Writes.Count > 0 ? bitWriter.Writes[^1].Target : "没有写请求");

            var bitReset = Round(BitButtonElement.ModeReset, cancel: false);
            Check("位按钮（复位）：释放写 0（与置位成对，急停复位/清标志都是它）",
                bitReset.Press.Count == 0 && bitReset.Release.Count == 1 && bitReset.Release[0] == "0",
                Texts(bitReset));

            var bitInvertOff = Round(BitButtonElement.ModeInvert, cancel: false);
            Check("位按钮（取反）：当前状态 0 → 释放写 1（写的是「显示的状态」取反，写回去显示随之翻转）",
                bitInvertOff.Press.Count == 0 && bitInvertOff.Release.Count == 1 && bitInvertOff.Release[0] == "1",
                Texts(bitInvertOff));

            bitElement.SetProperty("IsOn", "True");
            bit.Refresh();
            var bitInvertOn = Round(BitButtonElement.ModeInvert, cancel: false);
            Check("位按钮（取反）：当前状态 1 → 释放写 0（取反是读回当前值再写反向，不是无脑写一个固定值）",
                bitInvertOn.Release.Count == 1 && bitInvertOn.Release[0] == "0",
                Texts(bitInvertOn));

            bitElement.SetProperty("IsOn", "False");
            bit.Refresh();
            var bitPressOn = Round(BitButtonElement.ModePressOn, cancel: false);
            Check("位按钮（按下ON）：按下写 1、释放写 0（瞬动——点动/夹紧这类「手按着才动」的量）",
                bitPressOn.Press.Count == 1 && bitPressOn.Press[0] == "1"
                && bitPressOn.Release.Count == 1 && bitPressOn.Release[0] == "0",
                Texts(bitPressOn));

            var bitPressOff = Round(BitButtonElement.ModePressOff, cancel: false);
            Check("位按钮（按下OFF）：按下写 0、释放写 1（与按下ON 互补，用于低有效的那一侧）",
                bitPressOff.Press.Count == 1 && bitPressOff.Press[0] == "0"
                && bitPressOff.Release.Count == 1 && bitPressOff.Release[0] == "1",
                Texts(bitPressOff));

            // --- ③ 按下后滑开：这一下取消，但瞬动模式必须复位 ---
            var bitSetCancel = Round(BitButtonElement.ModeSet, cancel: true);
            Check("位按钮（置位/滑开）：按下后滑开 = 这一下没成，一条都不写（与画布对「释放」的同对象校验同口径）",
                bitSetCancel.Press.Count == 0 && bitSetCancel.Release.Count == 0,
                Texts(bitSetCancel));

            var bitPressOnCancel = Round(BitButtonElement.ModePressOn, cancel: true);
            Check("位按钮（按下ON/滑开）：按下写的 1 必须复位成 0（否则留下「手已移开、量还按着」，现场就是电机一直转）",
                bitPressOnCancel.Press.Count == 1 && bitPressOnCancel.Press[0] == "1"
                && bitPressOnCancel.Release.Count == 1 && bitPressOnCancel.Release[0] == "0",
                Texts(bitPressOnCancel));

            var bitPressOffCancel = Round(BitButtonElement.ModePressOff, cancel: true);
            Check("位按钮（按下OFF/滑开）：同理把 0 复位成 1（瞬动两侧都要复位，只复位一侧是漏判）",
                bitPressOffCancel.Press.Count == 1 && bitPressOffCancel.Press[0] == "0"
                && bitPressOffCancel.Release.Count == 1 && bitPressOffCancel.Release[0] == "1",
                Texts(bitPressOffCancel));

            // --- ④ 能不能操作：三条件缺一不可（与数值域 IsEditable 同一口径）---
            int bitWrites = bitWriter.Writes.Count;

            bit.RuntimeContext = null;
            bit.BeginPress();
            Check("位按钮（设计态）：没装运行上下文 → 点一下不动现场量，连按下态都不亮（编辑器里点它是选中图元）",
                !bit.IsPressed && bitWriter.Writes.Count == bitWrites,
                $"pressed={bit.IsPressed} / 新增写入={bitWriter.Writes.Count - bitWrites}");
            bit.ReleasePress();

            bit.RuntimeContext = bitMonitorContext;
            bit.BeginPress();
            Check("位按钮：宿主没给写通道（纯监视画面）→ 不写（「点了没反应」好过「点完才在日志里说写不进去」）",
                !bit.IsPressed && bitWriter.Writes.Count == bitWrites,
                $"pressed={bit.IsPressed} / 新增写入={bitWriter.Writes.Count - bitWrites}");
            bit.ReleasePress();

            bit.RuntimeContext = bitContext;
            bitBinding.IsEnabled = false;
            bit.Refresh();
            bit.BeginPress();
            Check("位按钮：绑定被停用 → 不写（读都不生效了写更不该生效，否则会出现「显示不动、一点就改变量」）",
                !bit.IsPressed && bitWriter.Writes.Count == bitWrites,
                $"pressed={bit.IsPressed} / 新增写入={bitWriter.Writes.Count - bitWrites}");
            bit.ReleasePress();

            bitBinding.IsEnabled = true;
            bit.Refresh();
            bit.IsEnabled = false;
            bit.BeginPress();
            Check("位按钮：图元被禁用 → 不写（禁用态的操作员手不该改到现场量）",
                !bit.IsPressed && bitWriter.Writes.Count == bitWrites,
                $"pressed={bit.IsPressed} / 新增写入={bitWriter.Writes.Count - bitWrites}");
            bit.ReleasePress();

            bit.IsEnabled = true;
            bit.BeginPress();
            Check("位按钮：三条件齐了才按下态亮起（按下反馈与能不能写是同一条判定，不各判一次）",
                bit.IsPressed, $"pressed={bit.IsPressed}");
            bit.ReleasePress();
            Check("位按钮：释放后按下态必然熄灭（滑开/释放都收口到同一个 EndPress）",
                !bit.IsPressed, $"pressed={bit.IsPressed}");

            // --- ⑤ 描述符：新属性必须落在描述符里，属性面板才会出现它们 ---
            var bitSpec = ElementRegistry.Find("Hmi.BitButton")!;
            Check("位按钮描述符：模式五条候选值就是五个常量（属性面板的下拉直接吃这份声明，两处不会各写一份）",
                bitSpec.Properties.First(p => p.Key == "Mode").Choices.SequenceEqual(new[]
                {
                    BitButtonElement.ModeSet, BitButtonElement.ModeReset, BitButtonElement.ModeInvert,
                    BitButtonElement.ModePressOn, BitButtonElement.ModePressOff
                }),
                string.Join(",", bitSpec.Properties.First(p => p.Key == "Mode").Choices));

            Check("位按钮描述符：「状态」组八条，其中七条可绑 ƒx（两种文字、两种背景、两种文字色 + 读变量）；"
                + "「输出反向」刻意不给 ƒx——它是设计期定死的极性，不是现场变量",
                bitSpec.Properties.Count(p => p.Group == "状态") == 8
                && bitSpec.Properties.Where(p => p.Group == "状态" && p.Key != "OutputInvert").All(p => p.IsBindable)
                && !bitSpec.Properties.First(p => p.Key == "OutputInvert").IsBindable,
                string.Join(" / ", bitSpec.Properties.Where(p => p.Group == "状态").Select(p => p.Key)));

            Check("位按钮描述符：外观组三条不声明可绑（画刷/边框是设计期定死的；描述符注册期会硬拦「不可绑组里声明 ƒx」）",
                bitSpec.Properties.Where(p => p.Group == "外观").All(p => !p.IsBindable)
                && bitSpec.Properties.Count(p => p.Group == "外观") == 3,
                string.Join(" / ", bitSpec.Properties.Where(p => p.Group == "外观").Select(p => p.Key)));

            Check("位按钮描述符：模式是下拉（Choice）而不是让用户敲字符串，默认「按下ON」（最常用的瞬动）",
                bitSpec.Properties.First(p => p.Key == "Mode").Kind == ElementPropertyKind.Choice
                && bitSpec.Properties.First(p => p.Key == "Mode").DefaultValue == BitButtonElement.ModePressOn,
                bitSpec.Properties.First(p => p.Key == "Mode").DefaultValue ?? "null");

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

            // --- 泵：状态 → 叶轮色；泵壳取可画区内接圆，叶轮是它的内接等边三角（顶点朝右）---
            //
            // 与阀门同族：状态色与尺寸无关（构造即算好），几何量必须等布局跑完。
            // 泵多一层"上下分账"：底下四分之一让给位号，泵壳只在上方四分之三里内接 ——
            // 这条账决定了几何量的每个数，所以"壳没侵入位号行"也要一起钉。
            var pumpElement = ElementRegistry.CreateElement("Hmi.Pump");
            var pump = (PumpElement)ElementRegistry.CreateControl(pumpElement);
            pumpElement.Width = 72;
            pumpElement.Height = 72;

            pump.Refresh();
            Check("泵：布局还没跑时几何量留空，但状态色已算出来（与尺寸无关的推导量不等布局）",
                pump.BodyGeometry == null && pump.ImpellerGeometry == null
                && pump.State == DeviceState.Stopped
                && pump.StateBrush is SolidColorBrush { Color: var pumpStoppedColor }
                && pumpStoppedColor == Color.FromRgb(0x7A, 0x7A, 0x7A),
                $"状态={pump.State}");

            pump.Measure(new Size(72, 72));
            pump.Arrange(new Rect(0, 0, 72, 72));
            pump.Refresh();

            // 与控件同一条账：可画区 = 控件尺寸 − 2 ×（半线宽 + 1）；底边四分之一让给位号。
            double pumpInset = pump.StrokeThickness / 2 + 1;
            double pumpSymbolBottom = 72 * 0.75;
            double pumpDiameter = Math.Min(72 - 2 * pumpInset, pumpSymbolBottom - 2 * pumpInset);
            double pumpRadius = pumpDiameter / 2;
            var pumpBody = pump.BodyGeometry as EllipseGeometry;
            var pumpBounds = pumpBody?.Bounds ?? Rect.Empty;

            Check("泵：泵壳是可画区的内接正圆（短边定直径，宽高不等也拉不成椭圆）",
                pumpBody != null
                && Math.Abs(pumpBounds.Width - pumpDiameter) < 0.001
                && Math.Abs(pumpBounds.Height - pumpDiameter) < 0.001
                && Math.Abs(pumpBounds.Top - pumpInset) < 0.001,
                $"泵壳 {pumpBounds.Width:0.###}×{pumpBounds.Height:0.###}，顶={pumpBounds.Top:0.###}");

            Check("泵：泵壳没侵入底下留给位号的那四分之一（位号不会被图形压住）",
                pumpBounds.Bottom > 0 && pumpBounds.Bottom <= pumpSymbolBottom + 0.001,
                $"壳下沿={pumpBounds.Bottom:0.###}，位号行顶={pumpSymbolBottom:0.###}");

            var impeller = pump.ImpellerGeometry as PathGeometry;
            Check("泵：叶轮是泵壳的内接等边三角形（1 个闭合填充图形、3 个顶点）",
                impeller != null && impeller.Figures.Count == 1
                && impeller.Figures[0].IsClosed && impeller.Figures[0].IsFilled
                && impeller.Figures[0].Segments.Count == 2,
                impeller == null ? "叶轮几何为空" : $"{impeller.Figures.Count} 个图形");

            // 叶轮半径 = 泵壳半径 × 0.62（与控件同一个系数）：0° / 120° / 240° 三个角均分圆周，
            // 于是"第一个顶点落在正右方"就是这条均分账的可读出口 —— 那个方向正是泵的出口。
            double pumpImpellerRadius = pumpRadius * 0.62;
            Check("泵：叶轮第一个顶点落在正右方（那是泵的出口方向，现场一眼读出介质往哪走）",
                impeller != null
                && Math.Abs(impeller.Figures[0].StartPoint.X
                    - (pumpBounds.Left + pumpBounds.Width / 2 + pumpImpellerRadius)) < 0.01
                && Math.Abs(impeller.Figures[0].StartPoint.Y
                    - (pumpBounds.Top + pumpBounds.Height / 2)) < 0.01,
                impeller == null ? "叶轮几何为空" : $"首顶点={impeller.Figures[0].StartPoint}");

            Check("泵：叶轮整个含在泵壳里（半径按 0.62 收，三个顶点都戳不出壳外）",
                impeller != null && pumpBody != null && pumpBounds.Contains(impeller.Bounds),
                impeller == null ? "叶轮几何为空" : $"叶轮外框={impeller.Bounds}");

            pumpElement.SetProperty("State", "Running");
            pump.Refresh();
            Check("泵：运行态叶轮转绿（操作员扫一眼先认颜色，不必去读位号）",
                pump.State == DeviceState.Running
                && pump.StateBrush is SolidColorBrush { Color: var pumpRunColor }
                && pumpRunColor == Color.FromRgb(0x34, 0xC7, 0x59),
                $"状态={pump.State}");

            pumpElement.SetProperty("State", "Fault");
            pump.Refresh();
            Check("泵：故障态叶轮转红（与阀门的报警色同一档）",
                pump.State == DeviceState.Fault
                && pump.StateBrush is SolidColorBrush { Color: var pumpFaultColor }
                && pumpFaultColor == Color.FromRgb(0xE0, 0x3A, 0x2B),
                $"状态={pump.State}");

            // --- 电机：状态 → M 笔画色；机身取可画区内接圆，顶上切一条给接线盒、底下切一条给位号 ---
            var motorElement = ElementRegistry.CreateElement("Hmi.Motor");
            var motor = (MotorElement)ElementRegistry.CreateControl(motorElement);
            motorElement.Width = 72;
            motorElement.Height = 80;

            motor.Refresh();
            Check("电机：布局还没跑时三份几何都留空、M 的笔画粗细归 0，但状态色已算出来",
                motor.BodyGeometry == null && motor.BoxGeometry == null && motor.LetterGeometry == null
                && motor.LetterThickness == 0
                && motor.State == DeviceState.Stopped
                && motor.StateBrush is SolidColorBrush { Color: var motorStoppedColor }
                && motorStoppedColor == Color.FromRgb(0x7A, 0x7A, 0x7A),
                $"状态={motor.State}，笔画={motor.LetterThickness:0.###}");

            motor.Measure(new Size(72, 80));
            motor.Arrange(new Rect(0, 0, 72, 80));
            motor.Refresh();

            double motorInset = motor.StrokeThickness / 2 + 1;
            double motorRegionTop = 80 * 0.18;    // 顶上留给接线盒
            double motorRegionBottom = 80 * 0.75; // 底下留给位号
            double motorDiameter = Math.Min(72 - 2 * motorInset, (motorRegionBottom - motorRegionTop) - 2 * motorInset);
            double motorRadius = motorDiameter / 2;
            double motorCenterX = motorInset + (72 - 2 * motorInset) / 2;
            double motorCenterY = (motorRegionTop + motorRegionBottom) / 2;
            var motorBody = motor.BodyGeometry as EllipseGeometry;
            var motorBounds = motorBody?.Bounds ?? Rect.Empty;

            Check("电机：机身是可画区的内接正圆（上下各切一条之后按短边取直径）",
                motorBody != null
                && Math.Abs(motorBounds.Width - motorDiameter) < 0.001
                && Math.Abs(motorBounds.Height - motorDiameter) < 0.001
                && Math.Abs(motorBounds.Top - (motorCenterY - motorRadius)) < 0.001,
                $"机身 {motorBounds.Width:0.###}×{motorBounds.Height:0.###}，顶={motorBounds.Top:0.###}");

            Check("电机：机身没侵入底下留给位号的那条带（位号不会被图形压住）",
                motorBounds.Bottom > 0 && motorBounds.Bottom <= motorRegionBottom + 0.001,
                $"机身下沿={motorBounds.Bottom:0.###}，位号带顶={motorRegionBottom:0.###}");

            // 接线盒：宽 = 机身直径 × 0.5，顶边贴内缩线，下沿探进机身 0.12 倍直径 ——
            // "探进去"才是重点：圆与方只有一个切点，贴着放会在方块底边与圆弧之间裂出一条缝。
            var motorBox = motor.BoxGeometry as RectangleGeometry;
            double motorBoxWidth = motorDiameter * 0.5;
            double motorBoxTop = motorInset;
            double motorBoxBottom = (motorCenterY - motorRadius) + motorDiameter * 0.12;
            Check("电机：接线盒下沿探进机身一截（贴着切点放会裂出一条缝，看上去像没装上去）",
                motorBox != null
                && Math.Abs(motorBox.Rect.X - (motorCenterX - motorBoxWidth / 2)) < 0.001
                && Math.Abs(motorBox.Rect.Width - motorBoxWidth) < 0.001
                && Math.Abs(motorBox.Rect.Top - motorBoxTop) < 0.001
                && Math.Abs(motorBox.Rect.Bottom - motorBoxBottom) < 0.001
                && motorBox.Rect.Bottom > motorCenterY - motorRadius
                && Math.Abs(motorBox.RadiusX - Math.Min(3, (motorBoxBottom - motorBoxTop) / 2)) < 0.001,
                motorBox == null ? "接线盒几何为空" : $"盒 {motorBox.Rect.Width:0.###}×{motorBox.Rect.Height:0.###}");

            var motorLetter = motor.LetterGeometry as PathGeometry;
            var motorFigure = motorLetter is { Figures.Count: 1 } ? motorLetter.Figures[0] : null;
            Check("电机：M 是一段不闭合的折线（不填充、4 段），折线本身没有面积，模板上只描边",
                motorFigure != null && !motorFigure.IsClosed && !motorFigure.IsFilled
                && motorFigure.Segments.Count == 4,
                motorLetter == null ? "M 几何为空" : $"{motorLetter.Figures.Count} 个图形");

            Check("电机：M 中间那个尖扎在字高的 60% 处（扎到底会变成 W 的倒影，扎浅了又像两道竖线）",
                motorFigure != null
                && motorFigure.Segments[1] is LineSegment { Point: var motorSpike }
                && Math.Abs(motorSpike.X - motorCenterX) < 0.001
                && motorSpike.Y > ((LineSegment)motorFigure.Segments[0]).Point.Y
                && motorSpike.Y < motorFigure.StartPoint.Y,
                motorFigure == null ? "M 几何为空" : $"中间尖={((LineSegment)motorFigure.Segments[1]).Point}");

            Check("电机：M 的笔画粗细随机身半径走（半径 × 0.18，机身缩小字也跟着细）",
                motor.LetterThickness > 0 && Math.Abs(motor.LetterThickness - motorRadius * 0.18) < 0.001,
                $"笔画={motor.LetterThickness:0.###}，机身半径={motorRadius:0.###}");

            motorElement.SetProperty("State", "Running");
            motor.Refresh();
            Check("电机：运行态 M 转绿（与泵、管道同一套状态色，整幅工艺图一眼扫得下来）",
                motor.State == DeviceState.Running
                && motor.StateBrush is SolidColorBrush { Color: var motorRunColor }
                && motorRunColor == Color.FromRgb(0x34, 0xC7, 0x59),
                $"状态={motor.State}");

            motorElement.SetProperty("State", "Fault");
            motor.Refresh();
            Check("电机：故障态 M 转红",
                motor.State == DeviceState.Fault
                && motor.StateBrush is SolidColorBrush { Color: var motorFaultColor }
                && motorFaultColor == Color.FromRgb(0xE0, 0x3A, 0x2B),
                $"状态={motor.State}");

            // --- 管道：状态 → 箭头色；管身取可画区整块，箭头个数按"排得下几个"现算再夹 1~6 ---
            var pipeElement = ElementRegistry.CreateElement("Hmi.Pipe");
            var pipe = (PipeElement)ElementRegistry.CreateControl(pipeElement);
            pipeElement.Width = 160;
            pipeElement.Height = 32;

            pipe.Refresh();
            Check("管道：布局还没跑时管身与箭头都留空、箭头粗细归 0，但状态色已算出来",
                pipe.TubeGeometry == null && pipe.ArrowGeometry == null && pipe.ArrowThickness == 0
                && pipe.Direction == PipeFlowDirection.LeftToRight
                && pipe.State == DeviceState.Stopped
                && pipe.StateBrush is SolidColorBrush { Color: var pipeStoppedColor }
                && pipeStoppedColor == Color.FromRgb(0x7A, 0x7A, 0x7A),
                $"状态={pipe.State}，箭头粗细={pipe.ArrowThickness:0.###}");

            pipe.Measure(new Size(160, 32));
            pipe.Arrange(new Rect(0, 0, 160, 32));
            pipe.Refresh();

            double pipeInset = pipe.StrokeThickness / 2 + 1;
            double pipeWidth = 160 - 2 * pipeInset;
            double pipeHeight = 32 - 2 * pipeInset;
            double pipeSize = Math.Min(pipeWidth, pipeHeight); // 短边：箭头的一切尺寸随它走
            var pipeTube = pipe.TubeGeometry as RectangleGeometry;

            Check("管道：管身是可画区整块圆角矩形（圆角取 3，且不超过管高折半）",
                pipeTube != null
                && Math.Abs(pipeTube.Rect.X - pipeInset) < 0.001
                && Math.Abs(pipeTube.Rect.Y - pipeInset) < 0.001
                && Math.Abs(pipeTube.Rect.Width - pipeWidth) < 0.001
                && Math.Abs(pipeTube.Rect.Height - pipeHeight) < 0.001
                && Math.Abs(pipeTube.RadiusX - Math.Min(3, pipeSize / 2)) < 0.001,
                pipeTube == null ? "管身几何为空" : $"管身 {pipeTube.Rect.Width:0.###}×{pipeTube.Rect.Height:0.###}");

            Check("管道：箭头笔画粗细随短边走（短边 × 0.10，管细箭头也细，不会糊成一团）",
                pipe.ArrowThickness > 0 && Math.Abs(pipe.ArrowThickness - pipeSize * 0.10) < 0.001,
                $"箭头粗细={pipe.ArrowThickness:0.###}，短边={pipeSize:0.###}");

            int pipeArrowCount = Math.Clamp(
                (int)Math.Round(pipeWidth / (pipeSize * 1.8), MidpointRounding.AwayFromZero), 1, 6);
            var pipeArrows = pipe.ArrowGeometry as PathGeometry;
            Check("管道：箭头个数按「能排下几个」现算（160×32 排 3 个），每个箭头是一段 2 折的不闭合折线",
                pipeArrows != null && pipeArrows.Figures.Count == pipeArrowCount
                && pipeArrows.Figures.All(f => !f.IsClosed && !f.IsFilled && f.Segments.Count == 2),
                pipeArrows == null ? "箭头几何为空" : $"{pipeArrows.Figures.Count} 个箭头，应 {pipeArrowCount} 个");

            // 人字的"尖"是中间那个折点（即 Segments[0] 的终点），两个"端头"在起笔点与收笔点上。
            // 尖落在两个端头的右侧就是朝右——比坐标大小即可，不必知道具体数值。
            Check("管道：左→右时箭头尖朝右（尖落在两个端头的右侧）",
                pipeArrows != null
                && pipeArrows.Figures.All(f => ((LineSegment)f.Segments[0]).Point.X > f.StartPoint.X),
                pipeArrows == null ? "箭头几何为空" : "箭头朝向对不上");

            pipeElement.SetProperty("Direction", "RightToLeft");
            pipe.Refresh();
            var pipeReversed = pipe.ArrowGeometry as PathGeometry;
            Check("管道：右→左时同一份几何整体翻向（个数与位置都不重排，只把尖角换到另一侧）",
                pipeReversed != null && pipeReversed.Figures.Count == pipeArrowCount
                && pipeReversed.Figures.All(f => ((LineSegment)f.Segments[0]).Point.X < f.StartPoint.X),
                pipeReversed == null ? "箭头几何为空" : $"{pipeReversed.Figures.Count} 个箭头");

            pipeElement.SetProperty("Direction", "None");
            pipe.Refresh();
            Check("管道：纯管身（None）不画箭头，管身照旧（管线的流向另有标注时用得上）",
                pipe.TubeGeometry != null && pipe.ArrowGeometry == null,
                pipe.ArrowGeometry == null ? "箭头已留空" : "箭头还在");

            // 拉长到 2000：按公式该排 38 个，必须被夹到上限 6 —— 一根管子上密密麻麻全是箭头
            // 既看不清流向，也白算一堆几何。夹取的边界单独钉，不然它只在"特别长的管子"上才暴露。
            pipeElement.SetProperty("Direction", "LeftToRight");
            pipeElement.Width = 2000;
            pipe.Refresh();
            pipe.Measure(new Size(2000, 32));
            pipe.Arrange(new Rect(0, 0, 2000, 32));
            pipe.Refresh();
            var pipeLong = pipe.ArrowGeometry as PathGeometry;
            Check("管道：管子拉得再长箭头也不超过 6 个（上限夹住了）",
                pipeLong != null && pipeLong.Figures.Count == 6,
                pipeLong == null ? "箭头几何为空" : $"{pipeLong.Figures.Count} 个箭头");

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

            // ---------------- ①-b 拖放协议：模板负载（第二种格式） ----------------

            // 模板与图元走两种格式而不是"一种格式 + 区分字段"：语义根本不同——类型键是"造一个空壳"，
            // 模板 Id 是"把库里那份带属性/绑定/事件的内容搬出来"。分开了，画布各自一问就知道走哪条路。
            var templateId = Guid.NewGuid();
            var templatePayload = ScadaDrag.CreateTemplatePayload(templateId);

            Check("模板负载装载/取回往返：模板 Id 原样回来（存的是 \"D\" 形态的字符串，跨进程也读得懂）",
                ScadaDrag.TryGetTemplateId(templatePayload, out Guid templateRoundTrip)
                && templateRoundTrip == templateId,
                templateRoundTrip.ToString("D"));

            bool emptyThrew = false;
            try { ScadaDrag.CreateTemplatePayload(Guid.Empty); }
            catch (ArgumentException) { emptyThrew = true; }
            Check("空 Guid 不让装进负载（放下去只会去库里查一个不存在的模板）", emptyThrew, "");

            Check("null 负载与空负载一律不认（模板这条路）",
                !ScadaDrag.TryGetTemplateId(null, out _) && !ScadaDrag.TryGetTemplateId(new DataObject(), out _), "");

            var brokenTemplateValue = new DataObject();
            brokenTemplateValue.SetData(ScadaDrag.TemplateIdFormat, "不是 Guid");
            Check("格式名对但值不是合法 Guid，也算不是我们的负载",
                !ScadaDrag.TryGetTemplateId(brokenTemplateValue, out _), "");

            var emptyGuidText = new DataObject();
            emptyGuidText.SetData(ScadaDrag.TemplateIdFormat, Guid.Empty.ToString("D"));
            Check("值是全零 Guid 同样不算负载（那是\"没带模板\"，不是\"模板叫全零\"）",
                !ScadaDrag.TryGetTemplateId(emptyGuidText, out _), "");

            // 两条协议必须互不串门：类型键的负载不能被读成模板，反之亦然。
            // 少了这条，画布上"拖图元"与"拖模板"两条分支就可能同时命中，落点算法用错一套（外接框 vs 默认宽高）。
            Check("两种格式互不串门：类型键负载读不出模板 Id，模板负载也读不出类型键",
                !ScadaDrag.TryGetTemplateId(payload, out _) && !ScadaDrag.TryGetTypeKey(templatePayload, out _), "");

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
            Check("搜索命中分类（\"操作\"出来的是这一类下全部图元：按钮 + 位按钮）",
                Shown(toolbox) == 2, $"命中 {Shown(toolbox)}");

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
            var pagePicker = new FakePagePicker();
            var panel = new ScadaPropertyVM(editor, notifier, picker, pagePicker);

            static int Total(ScadaPropertyVM vm) => vm.Groups.Sum(g => g.Rows.Count);

            // 面板行账本 = 描述符声明的属性行 + 事件行 + 动画行 + 通用关系行。
            //
            // 通用关系行目前只有一条：S12 的「操作权限」。它不是属性袋里的键，描述符也声明不了
            // （RequiredRole 是图元上的强类型字段，理由见该属性的注释），但每个图元都有它——
            // "谁能操作我"对任何图元都成立。所以算"应有行数"时必须恒加这一条；
            // 漏掉它，下面几条会集体报假红，而红的原因跟被测代码毫无关系。
            const int UniversalRows = 1;

            // 动画行（r4）：四种动画（外观变化 / 水平移动 / 垂直移动 / 可见性）各一行，恒加四行。
            //
            // 为什么不是"描述符声明了几个动画就几行"：动画不是图元类型的表态，而是"这一台设备上
            // 这个图元要不要跟变量联动"——任何图元都可能需要，所以面板对每个已注册图元都出满四种
            // （Rebuild 里用 Enum.GetValues<ScadaAnimationType>() 生成）。这四行因此与描述符无关，
            // 是行账本里的第三个常量项，跟 UniversalRows 一个道理：少加就会集体报假红。
            //
            // 注意未注册图元（ElementRegistry.Find 返回 null）走的是"不认识"那一支，一行都不出，
            // 动画行也不出；画面模式同理（动画只长在图元上）。
            const int AnimationRows = 4;

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
            Check("选中矩形即长出属性行，行数 = 描述符声明数 + 动画行 + 通用关系行（面板不认识任何具体图元类型）",
                Total(panel) == rectDescriptor.Properties.Count + rectDescriptor.Events.Count + AnimationRows + UniversalRows
                && rectDescriptor.Properties.Count == 13 && rectDescriptor.Events.Count == 0,
                $"面板 {Total(panel)} 行 / 描述符 {rectDescriptor.Properties.Count}+{rectDescriptor.Events.Count} 条");

            Check("组序取自描述符里的首现次序：常规→位置与尺寸→外观→文字→动画→权限（动画行恒在权限之前）",
                string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/外观/文字/动画/权限",
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
            Check("换选中即整份重建：椭圆 12 条属性 + 4 条动画 + 1 条通用关系 = 17 行 6 组，且写入目标已换成新图元",
                Total(panel) == 12 + AnimationRows + UniversalRows && panel.Groups.Count == 6
                && panel.Element?.TypeKey == "Hmi.Ellipse",
                $"{Total(panel)} 行 / {panel.Groups.Count} 组");

            int rowMismatch = 0;
            int bindMismatch = 0;
            foreach (var descriptor in ElementRegistry.All)
            {
                editor.SelectedElement = Named(descriptor.TypeKey, "临时");

                // 行账本 = 属性行 + 事件行 + 动画行 + 通用关系行：声明了几个事件就多几行，面板一行不多一行不少
                if (Total(panel) != descriptor.Properties.Count + descriptor.Events.Count + AnimationRows + UniversalRows) rowMismatch++;

                // 可绑性只有描述符一个来源：按钮上的 ƒx 该不该亮，不由面板自己猜
                foreach (var row in panel.Groups.SelectMany(g => g.Rows))
                    if (panel.BindVariableCommand.CanExecute(row) != row.IsBindable) bindMismatch++;
            }

            Check("遍历全部已注册图元：每一类的行数都等于其描述符声明数 + 通用关系行（新增图元这里零改动这条成立）",
                rowMismatch == 0 && ElementRegistry.All.Count >= 5,
                $"不匹配 {rowMismatch} 类，共 {ElementRegistry.All.Count} 类");

            Check("ƒx 只对声明了 IsBindable 的行可用（不可绑属性不给按钮）",
                bindMismatch == 0, $"不匹配 {bindMismatch} 行");

            // 位按钮（N-3）在面板上的投影：15 条特有 + 6 条几何 = 21 条属性 + 2 条事件 + 4 条动画 + 1 条通用关系。
            // 多出来的「状态」组夹在「位置与尺寸」与「文字」之间——组序就是描述符里属性的首现次序，
            // 面板不排序、不猜（把状态那八条放到几何之后，操作员一打开面板就先看到状态，而不是先看到 X/Y）。
            //
            // 这一条必须排在下面 Hmi.Button 那一节<b>之前</b>：属性面板是"选中谁就重建谁"，
            // 把它插在按钮那一节中间会把面板切到另一个图元上，后面那些 Row("Event.x") 就会读到别人的行。
            editor.SelectedElement = Named("Hmi.BitButton", "启动");
            var bitButtonDescriptor = ElementRegistry.Find("Hmi.BitButton")!;
            Check("位按钮：21 条属性 + 2 条事件 + 4 条动画 + 1 条通用关系 = 28 行 8 组（组序按声明首现，状态组排在几何之后、文字之前，动画组恒在权限之前）",
                Total(panel) == bitButtonDescriptor.Properties.Count + bitButtonDescriptor.Events.Count + AnimationRows + UniversalRows
                && bitButtonDescriptor.Properties.Count == 21
                && string.Join("/", panel.Groups.Select(g => g.Name))
                   == "常规/位置与尺寸/状态/文字/外观/事件/动画/权限",
                $"{Total(panel)} 行 / " + string.Join("/", panel.Groups.Select(g => g.Name)));

            var startBtn = Named("Hmi.Button", "启动");
            editor.SelectedElement = startBtn;
            var buttonDescriptor = ElementRegistry.Find("Hmi.Button")!;
            Check("按钮：14 条属性 + 2 条事件 + 4 条动画 + 1 条通用关系 = 21 行 7 组（组序是声明出来的，不是按字典序凑的）",
                Total(panel) == buttonDescriptor.Properties.Count + buttonDescriptor.Events.Count + AnimationRows + UniversalRows
                && buttonDescriptor.Events.Count == 2
                && string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/文字/外观/事件/动画/权限",
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
            Check("取消勾选：整条钩子连同动作一起摘掉，图元与画面同一口径",
                startBtn.FindEventHook(ScadaEventType.Pressed) == null && Row("Event.2").Value == "False",
                $"{startBtn.FindEventHook(ScadaEventType.Pressed) == null} / {Row("Event.2").Value}");

            // ---------------- ①C 动作编辑：勾上之后在原地增 / 删 / 调序（S5 完善） ----------------
            //
            // 事件行是"复合编辑区"：勾选框之外还内联一张动作表。动作表直接双向绑到模型对象
            // （ScadaAction 本身带变更通知，没有"文本形态"这回事），所以这里断言的是
            // "命令把模型改对了"，而不是"行里另存了一份影子副本"。

            var eventRow = (ScadaEventRow)Row("Event.2");

            Check("行自述编辑器种类：事件行 Event、动画行 Animation、普通属性行 Inline——面板据此整份换模板，XAML 不必认得任何具体行类型",
                eventRow.EditorKind == ScadaRowEditorKind.Event
                && Row("Animation.1").EditorKind == ScadaRowEditorKind.Animation
                && Row("$X").EditorKind == ScadaRowEditorKind.Inline,
                $"事件行 {eventRow.EditorKind} / 动画行 {Row("Animation.1").EditorKind} / 属性行 {Row("$X").EditorKind}");

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

            Check("三条动作现在都已接通（S6 写变量、S8 切换画面），PendingReason 只剩「本版本不认识」这一档；面板那行橙色提示与运行日志仍逐字同源",
                new ScadaAction { Type = ScadaActionType.Log }.PendingReason == null
                && new ScadaAction { Type = ScadaActionType.WriteVariable }.PendingReason == null
                && new ScadaAction { Type = ScadaActionType.Navigate }.PendingReason == null
                && new ScadaAction { Type = (ScadaActionType)99 }.PendingReason == "本版本不认识该动作",
                new ScadaAction { Type = ScadaActionType.Navigate }.PendingReason ?? "null");

            var typeProbe = new ScadaAction { Type = ScadaActionType.Log };
            var typeRaised = new List<string>();
            typeProbe.PropertyChanged += (_, e) => typeRaised.Add(e.PropertyName ?? "<null>");
            typeProbe.Type = (ScadaActionType)99;
            Check("改动作类型时 PendingReason 一并广播：橙色提示当场换字，不用等整块重建",
                typeRaised.Contains(nameof(ScadaAction.PendingReason))
                && typeProbe.PendingReason == "本版本不认识该动作",
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

            // ---------------- ①E 切换画面的目标画面：面板只说"给我一个画面"（S8） ----------------
            //
            // 与 ①D 同构，但另立一个事件行：目标画面的回填不该借用写变量那条动作的现场，
            // 否则"回填到的是画面字段还是变量字段"这件事就没有独立证据了。

            var navHost = Named("Hmi.Button", "去列表");
            var navRow = new ScadaEventRow(navHost, ScadaEventType.Pressed, ScadaEventRow.ElementGroup, picker, pagePicker);
            navRow.AddActionCommand.Execute();
            var navAction = navRow.Actions![0];
            navAction.Type = ScadaActionType.Navigate;

            Check("切换画面动作一新建就是「还没选画面」：HasTargetPage 为假，面板据此亮橙色提示",
                !navAction.HasTargetPage && navAction.TargetPageId == Guid.Empty && navAction.TargetPageName == null,
                $"{navAction.TargetPageId} / {navAction.TargetPageName ?? "null"}");

            var navRaised = new List<string>();
            navAction.PropertyChanged += (_, e) => navRaised.Add(e.PropertyName ?? "<null>");

            var targetPageId = Guid.NewGuid();
            pagePicker.NextId = targetPageId;
            pagePicker.NextName = "B 列表";
            navRow.PickPageCommand.Execute(navAction);

            Check("点「选择画面」：把这条动作的现状（Id 优先、名字兜底）交给选择器，选中后回填 Id 与名字",
                pagePicker.PickCalls == 1
                && pagePicker.LastCurrentId == Guid.Empty && pagePicker.LastCurrentName == null
                && navAction.TargetPageId == targetPageId && navAction.TargetPageName == "B 列表",
                $"{pagePicker.PickCalls} 次 / {navAction.TargetPageName}");

            Check("回填后 HasTargetPage 广播出去：面板上的占位文案与橙色提示当场收掉，不用等整块重建",
                navRaised.Contains(nameof(ScadaAction.HasTargetPage)) && navAction.HasTargetPage,
                string.Join("/", navRaised));

            Check("切换画面的描述与详情都指向同一个目标画面（日志里那句与面板上那句是同一句）",
                navAction.Describe() == "切换画面 → B 列表" && navAction.Detail == "B 列表",
                navAction.Describe());

            pagePicker.NextId = Guid.Empty;   // 用户在弹窗里点了取消
            navRow.PickPageCommand.Execute(navAction);
            Check("取消时回调一次都不触发：原目标画面原样留着（取消不等于清空，破坏性动作不该藏在取消里）",
                navAction.TargetPageId == targetPageId && navAction.TargetPageName == "B 列表",
                navAction.TargetPageName ?? "null");

            Check("预选参数就是这条动作的现状：再点一次时交出去的是上次选的那一页",
                pagePicker.LastCurrentId == targetPageId && pagePicker.LastCurrentName == "B 列表",
                $"{pagePicker.LastCurrentId} / {pagePicker.LastCurrentName ?? "null"}");

            Check("切换画面已接通：PendingReason 为 null，面板不再显示「还没接通」提示",
                navAction.PendingReason == null, navAction.PendingReason ?? "null");

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
            var indicatorDescriptor = ElementRegistry.Find("Hmi.Indicator")!;
            Check("指示灯：行数 = 描述符声明数 + 动画行 + 通用关系行，多出来的是一个状态组（点亮/点亮色/熄灭色）",
                Total(panel) == indicatorDescriptor.Properties.Count + indicatorDescriptor.Events.Count + AnimationRows + UniversalRows
                && string.Join("/", panel.Groups.Select(g => g.Name)) == "常规/位置与尺寸/状态/外观/文字/动画/权限"
                && panel.Groups.Single(g => g.Name == "状态").Rows.Count == 3,
                string.Join("/", panel.Groups.Select(g => $"{g.Name}:{g.Rows.Count}")));

            // ---------------- ①B 权限行（S12）：每个图元都有的通用关系行 ----------------
            //
            // 它是上面那条"行数 = 描述符声明数"之外的第二种行源：RequiredRole 是图元上的强类型字段，
            // 不在属性袋里、描述符也声明不了（理由见该属性注释），所以单独把它的
            // 候选 / 写回 / 坏值 / 拒绝四件事钉在这里。
            //
            // 图元必须真落在画面上：写回走 ScadaPage.TrySetRequiredRole，它按"在不在本画面"过滤
            // （与 TryMoveElementZ / TrySetElementLocked 同一口径），拿一个游离的 ScadaElement
            // 是验不出写回的。
            var stopPage = editor.SelectedPage!;
            var stopBtn = editor.AddElement("Hmi.Button", new Point(200, 200))!;
            stopBtn.Name = "急停";
            editor.SelectedElement = stopBtn;

            var roleRow = Row("RequiredRole");
            Check("每个图元都长出权限行：Choice 型、候选固定四档、且不给 ƒx（权限不该被变量驱动）",
                roleRow.Kind == ElementPropertyKind.Choice
                && !roleRow.IsBindable
                && string.Join("/", roleRow.Choices) == "不限制/操作员/工程师/管理员",
                $"{roleRow.Kind} / ƒx={roleRow.IsBindable} / {string.Join("/", roleRow.Choices)}");

            // 权限行的候选本身就是中文文案，所以它过一遍词汇表应当原样出来（值 == 显示名）。
            // 这正是"认不出的值原样返回"这条规矩要保住的：若哪天有人把词汇表改成"查不到就返回空"，
            // 权限下拉会当场变成四个空白项——那比显示英文严重得多（用户根本不知道能选什么）。
            Check("权限行的中文候选过词汇表原样出来：下拉里显示什么，写回的就是什么",
                roleRow.ChoiceOptions.Count == roleRow.Choices.Count
                && roleRow.ChoiceOptions.Select(o => o.Value).SequenceEqual(roleRow.Choices)
                && roleRow.ChoiceOptions.All(o => o.Value == o.DisplayName),
                string.Join("/", roleRow.ChoiceOptions.Select(o => $"{o.Value}={o.DisplayName}")));

            Check("默认是「不限制」，模型里就是 null（旧 .vms 反序列化后天然落在这一档，零迁移）",
                roleRow.Value == "不限制" && stopBtn.RequiredRole == null,
                $"{roleRow.Value} / {stopBtn.RequiredRole?.ToString() ?? "null"}");

            ScadaEditHistory.Clear();
            roleRow.Value = "工程师";
            Check("选一个角色：模型跟着变，且撤销栈里是一条带操作名的记录（裸写 setter 只会留一条没名字的「编辑」）",
                stopBtn.RequiredRole == ScadaRole.Engineer
                && ScadaEditHistory.UndoCount == 1
                && ScadaEditHistory.NextUndoLabel == "设置操作权限 [急停] → 工程师",
                $"{stopBtn.RequiredRole?.ToString() ?? "null"} / {ScadaEditHistory.UndoCount} 条 / {ScadaEditHistory.NextUndoLabel ?? "null"}");

            int versionBeforeSame = stopPage.Version;
            Check("值没变是空操作：返回 true 且版本号不动（面板刷新、下拉重选同一项都不该在撤销栈里堆记录）",
                stopPage.TrySetRequiredRole(stopBtn, ScadaRole.Engineer, out _)
                && stopPage.Version == versionBeforeSame
                && ScadaEditHistory.UndoCount == 1,
                $"v{versionBeforeSame} → v{stopPage.Version} / {ScadaEditHistory.UndoCount} 条");

            Check("拒绝写入非法角色 Undefined：否则会造出一个「谁都按不动、界面上还看不出为什么」的图元",
                !stopPage.TrySetRequiredRole(stopBtn, ScadaRole.Undefined, out string undefError)
                && stopBtn.RequiredRole == ScadaRole.Engineer,
                undefError);

            Check("不属于本画面的图元一律拒（拿对象引用当通行证，权限就会写到看不见的画面上）",
                !stopPage.TrySetRequiredRole(Named("Hmi.Button", "外来的"), ScadaRole.Operator, out string foreignError)
                && foreignError.Contains("不属于本画面"),
                foreignError);

            roleRow.Value = "不限制";
            Check("选「不限制」回到 null：与「没配过」是同一个状态，不是另存一个「不限」哨兵值",
                stopBtn.RequiredRole == null && roleRow.Value == "不限制",
                $"{stopBtn.RequiredRole?.ToString() ?? "null"} / {roleRow.Value}");

            // 坏值：模拟"高版本存、低版本读"——文件里就是 9 这个数，直接裸写绕开写入口。
            stopBtn.RequiredRole = (ScadaRole)9;
            Check("文件里的坏值不装看不见：模型一变面板就自动跟上，候选临时补上那一项，显示「未知角色(9)」而不是一片空白",
                roleRow.Value == "未知角色(9)" && roleRow.Choices.Contains("未知角色(9)"),
                $"{roleRow.Value} / {string.Join("/", roleRow.Choices)}");

            stopBtn.RequiredRole = ScadaRole.Administrator;
            Check("坏值被改走后候选也收回去（RefreshValue 里补的那次通知）：下拉里不留一条再也选不中的僵尸项",
                roleRow.Value == "管理员" && !roleRow.Choices.Contains("未知角色(9)"),
                $"{roleRow.Value} / {string.Join("/", roleRow.Choices)}");

            // 认不出的文案（候选失配时 WPF 会把 SelectedItem 置空并回写）绝不能当成"取消限制"，
            // 那是一次静默的权限放大——现场只会看到"本来要工程师才能开，怎么操作员也能开了"。
            roleRow.Value = string.Empty;
            Check("空文案不当作「不限制」：标红且模型一动不动（静默放权比报错难查一百倍）",
                !roleRow.IsValid && stopBtn.RequiredRole == ScadaRole.Administrator,
                $"valid={roleRow.IsValid} / {roleRow.ErrorText} / {stopBtn.RequiredRole}");

            // ---------------- ①C 图元右键菜单：图层归属 + 叠放次序 + 对齐分布 + 锁定 + 删除 ----------------
            //
            // 前两件事的真值都不在属性袋里：归属就是 ScadaElement.LayerId、叠放就是 ZIndex，
            // 所以它们不占属性面板的行，而是选中图元后右键弹出的菜单。锁定与删除同理——
            // 它们作用在"图元这个对象"上而不是它的某个属性，塞进属性面板只会变成一行没有值的按钮。
            //
            // 菜单内容由 ScadaEditorViewModel.BuildElementContextMenu 产出——一份与 WPF 无关的
            // "菜单长什么样"的描述（ScadaMenuItem），视图只把描述翻译成 MenuItem。
            // 切这一刀就是为了让"菜单里有什么"能在没有 Application/Dispatcher 的这里被断言。
            //
            // 写入一律回到 ScadaPage.Try* 家族（D3 唯一写入口）：VM 不自己算 ZIndex、也不自己校验归属。

            var layerHost = editor.AddElement("Hmi.Rectangle", new Point(5, 5))!;
            editor.SelectedElement = layerHost;

            var menu = editor.BuildElementContextMenu();

            Check("右键菜单十栏且次序冻结：图层 + 叠放次序（标题带当前位次）+ 对齐与分布 + 复制 + 粘贴 + 再制 + 存为模板… + 锁定/解锁 + 组合/取消组合 + 删除图元（删除放末尾，免得误点）",
                menu.Count == 10 && menu[0].Name == "图层"
                && menu[1].Name.StartsWith("叠放次序（", StringComparison.Ordinal)
                && menu[2].Name == "对齐与分布"
                && menu[3].Name == "复制"
                && menu[4].Name == "粘贴"
                && menu[5].Name == "再制"
                && menu[6].Name == "存为模板…"
                && (menu[7].Name == "锁定图元" || menu[7].Name == "解锁图元")
                && (menu[8].Name == "组合" || menu[8].Name == "取消组合")
                && menu[9].Name == "删除图元",
                string.Join(" / ", menu.Select(m => m.Name)));

            Check("前三栏是分组项：各带图标、自己不执行任何动作，点开才见子项",
                menu.Take(3).All(m => m.HasChildren && m.Command == null && !string.IsNullOrEmpty(m.Icon)),
                string.Join(" / ", menu.Take(3).Select(m => $"{m.Name}:子项{m.Children.Count}")));

            // 剪贴板三件（复制 / 粘贴 / 再制）紧挨着摆、不折进子菜单：用得最勤的一组，折一层就多一次移动和判断。
            // 三者都是叶子项、各带图标与命令，且判灰（CanExecute）各自跟着自己的判据走——
            // 复制要选中集合非空、粘贴要有货且有画面。判据全在命令那一侧，菜单这一层不重算。
            Check("第四~六栏是剪贴板三件（复制 / 粘贴 / 再制）：叶子项、各带图标与命令、按此序紧挨着摆",
                menu.Skip(3).Take(3).All(m => !m.HasChildren && m.Command != null && !string.IsNullOrEmpty(m.Icon))
                && menu[3].Command == editor.CopyCommand
                && menu[4].Command == editor.PasteCommand
                && menu[5].Command == editor.DuplicateCommand,
                string.Join(" / ", menu.Skip(3).Take(3).Select(m => $"{m.Name}:命令={(m.Command == null ? "null" : "有")}")));

            // "存为模板…"与上面三件同属"把选中的东西变成一份可再用的内容"，故紧随再制之后、锁定之前。
            // 标题必须带省略号——点下去还会问一个名字，不带就是骗用户"点一下就完了"。
            Check("第七栏是「存为模板…」：叶子项、自带图标与命令，且标题带省略号（点下去要问名字，不能装成一步完成）",
                !menu[6].HasChildren && menu[6].Command == editor.SaveTemplateCommand
                && !string.IsNullOrEmpty(menu[6].Icon) && menu[6].Name.EndsWith("…", StringComparison.Ordinal),
                $"{menu[6].Name}:子项{menu[6].Children.Count} / 命令={(menu[6].Command == null ? "null" : "有")}");

            // 锁定、组合与删除同属"改保护位/改关系/改内容"的一组（上面几栏只换个摆法/搬份内容），
            // 所以都排在删除之前。三者都是叶子项：自带命令与图标——点下去要真生效，不能只是个标题。
            Check("第八栏是锁定叶子项：无子项、自带命令与图标（与组合、删除同属'改内容'，排在删除之前）",
                !menu[7].HasChildren && menu[7].Command != null && !string.IsNullOrEmpty(menu[7].Icon),
                $"{menu[7].Name}:子项{menu[7].Children.Count} / 命令={(menu[7].Command == null ? "null" : "有")}");

            // 组合与锁定同形（叶子项、自带命令与图标），且紧随锁定之后——两者都是"改图元之间的关系/保护状态"。
            Check("第九栏是组合叶子项：无子项、自带命令与图标（与锁定同形，排在删除之前）",
                !menu[8].HasChildren && menu[8].Command != null && !string.IsNullOrEmpty(menu[8].Icon),
                $"{menu[8].Name}:子项{menu[8].Children.Count} / 命令={(menu[8].Command == null ? "null" : "有")}");

            // 删除与上面几栏分组项的形态刻意不同：它不是"换摆法"而是"改内容"，所以是叶子项而不是分组，
            // 且必须自己带命令与图标——点下去要真删，不能只是个标题。
            // （锁定、组合两栏也是叶子项，同属"改内容"，只是删除放在最末——末位留给唯一不可逆的那一项。）
            Check("末栏是叶子项：无子项、自带命令与图标（分组形态会让它点下去毫无反应）",
                !menu[9].HasChildren && menu[9].Command != null && !string.IsNullOrEmpty(menu[9].Icon),
                $"{menu[9].Name}:子项{menu[9].Children.Count} / 命令={(menu[9].Command == null ? "null" : "有")}");

            var hostPage = editor.SelectedPage!;
            var extraLayer = hostPage.AddLayer("设备层");
            menu = editor.BuildElementContextMenu();

            Check("「图层」候选 = 本画面的图层表 + 末位的「（未分层）」（未分层是个能选中的项，不是「什么都不选」）",
                menu[0].Children.Select(i => i.Name)
                    .SequenceEqual(hostPage.Layers.Select(l => l.Name).Append(ScadaEditorVM.UnassignedLayerName)),
                string.Join("、", menu[0].Children.Select(i => i.Name)));

            // 新图元不是"未分层"：画面自带一个默认图层（图层_1），AddElement 会把它挂上去
            //（ScadaEditorViewModel:227）。所以这里勾的必须是那一层——勾在「（未分层）」上
            // 反而说明自动归属断了，菜单与模型对不上。
            Check("归属项都可勾选且当前归属恰好勾一项：新图元自动落在画面默认图层上，勾就打在它上面",
                menu[0].Children.All(i => i.IsCheckable && i.Command != null)
                && menu[0].Children.Count(i => i.IsChecked) == 1
                && menu[0].Children.Single(i => i.IsChecked).Name == hostPage.DefaultLayer!.Name
                && layerHost.LayerId == hostPage.DefaultLayer.LayerId,
                string.Join("、", menu[0].Children.Select(i => $"{i.Name}={(i.IsChecked ? "✓" : "·")}")));

            menu[0].Children.Single(i => i.Name == "设备层").Command!.Execute(null);
            Check("点「设备层」：归属写进图元（走 TryAssignLayer，LayerId 指向那个图层对象而不是层名）",
                layerHost.LayerId == extraLayer.LayerId, layerHost.LayerId.ToString());

            menu = editor.BuildElementContextMenu();
            Check("菜单每次弹出即重建：勾选态跟着模型走，现在勾在「设备层」上（不持有会过期的快照）",
                menu[0].Children.Single(i => i.IsChecked).Name == "设备层",
                string.Join("、", menu[0].Children.Select(i => $"{i.Name}={(i.IsChecked ? "✓" : "·")}")));

            menu[0].Children.Single(i => i.Name == ScadaEditorVM.UnassignedLayerName).Command!.Execute(null);
            Check("点「（未分层）」：归属退回 Guid.Empty——「取消分层」是个说得出口的操作",
                layerHost.LayerId == Guid.Empty, layerHost.LayerId.ToString());

            var zTarget = editor.AddElement("Hmi.Ellipse", new Point(20, 20))!;
            editor.SelectedElement = zTarget;
            int zTotal = hostPage.Elements.Count;

            menu = editor.BuildElementContextMenu();
            Check("叠放次序四项齐备，排列从最前到最底，每项一个方向图标（都是叶子动作项）",
                menu[1].Children.Select(i => i.Name).SequenceEqual(new[] { "置顶", "上移一层", "下移一层", "置底" })
                && menu[1].Children.All(i => i.Command != null && !i.HasChildren && !string.IsNullOrEmpty(i.Icon)),
                string.Join(" / ", menu[1].Children.Select(i => i.Name)));

            Check("标题里的位次与模型一致（「叠放次序（2 / N）」——属性面板撤掉叠放行后，这是用户唯一能读到「我在第几层」的地方）",
                menu[1].Name == $"叠放次序（{hostPage.Elements.OrderBy(e => e.ZIndex).ToList().IndexOf(zTarget) + 1} / {zTotal}）",
                menu[1].Name);

            menu[1].Children.Single(i => i.Name == "置底").Command!.Execute(null);
            menu = editor.BuildElementContextMenu();
            Check("置底：ZIndex 被重编号成最小值，菜单标题立刻回读成「1 / N」",
                zTarget.ZIndex == hostPage.Elements.Min(e => e.ZIndex)
                && menu[1].Name == $"叠放次序（1 / {zTotal}）",
                $"ZIndex={zTarget.ZIndex} / 标题={menu[1].Name}");

            Check("已经在最下面：置底与下移一层都判灰（点下去什么都不发生的项最伤信任）",
                !menu[1].Children.Single(i => i.Name == "置底").Command!.CanExecute(null)
                && !menu[1].Children.Single(i => i.Name == "下移一层").Command!.CanExecute(null)
                && menu[1].Children.Single(i => i.Name == "上移一层").Command!.CanExecute(null)
                && menu[1].Children.Single(i => i.Name == "置顶").Command!.CanExecute(null),
                string.Join("/", menu[1].Children.Select(i => $"{i.Name}={i.Command!.CanExecute(null)}")));

            menu[1].Children.Single(i => i.Name == "置顶").Command!.Execute(null);
            menu = editor.BuildElementContextMenu();
            Check("置顶：ZIndex 被重编号成最大值，菜单标题回读成「N / N」",
                zTarget.ZIndex == hostPage.Elements.Max(e => e.ZIndex)
                && menu[1].Name == $"叠放次序（{zTotal} / {zTotal}）",
                $"ZIndex={zTarget.ZIndex} / 标题={menu[1].Name}");

            Check("已经在最上面：置顶与上移一层都判灰（四个方向的可用性口径与 TryMoveElementZ 的空操作条件一致）",
                !menu[1].Children.Single(i => i.Name == "置顶").Command!.CanExecute(null)
                && !menu[1].Children.Single(i => i.Name == "上移一层").Command!.CanExecute(null)
                && menu[1].Children.Single(i => i.Name == "下移一层").Command!.CanExecute(null)
                && menu[1].Children.Single(i => i.Name == "置底").Command!.CanExecute(null),
                string.Join("/", menu[1].Children.Select(i => $"{i.Name}={i.Command!.CanExecute(null)}")));

            // ---- 对齐与分布：八个动作共用一个子菜单 ----
            //
            // 与前两栏有一处本质差别：图层/叠放作用于"主选中那一个"，对齐作用于"整批选中"。
            // 所以判灰看的是<b>可编辑的选中数</b>够不够（对齐 2 个、分布 3 个），
            // 下界与领域层 TryAlignElements 的前置校验同源（ScadaAlign.MinimumCount）——
            // 菜单里另写一个数字，就会出现"菜单亮着、点了返回 false"。
            var alignItems = editor.BuildElementContextMenu()[2].Children;

            Check("「对齐与分布」八项齐备且次序冻结：先六种对齐（水平三、垂直三）再两种分布",
                alignItems.Select(i => i.Name).SequenceEqual(
                    new[] { "左对齐", "水平居中", "右对齐", "顶对齐", "垂直居中", "底对齐", "水平分布", "垂直分布" }),
                string.Join(" / ", alignItems.Select(i => i.Name)));

            Check("八项都是带图标、带命令的叶子项，且八个图标各不相同（分组形态会让点下去毫无反应，图标重了则分不清动作）",
                alignItems.All(i => i.Command != null && !i.HasChildren && !string.IsNullOrEmpty(i.Icon))
                && alignItems.Select(i => i.Icon).Distinct().Count() == alignItems.Count,
                string.Join("、", alignItems.Select(i => $"{i.Name}={i.Icon}")));

            Check("只选中一个：六种对齐全判灰（对着一个图元「对齐」没有任何含义）",
                alignItems.Take(6).All(i => !i.Command!.CanExecute(null)),
                string.Join("/", alignItems.Select(i => $"{i.Name}={i.Command!.CanExecute(null)}")));

            editor.SelectedElements = new[] { layerHost, zTarget };
            alignItems = editor.BuildElementContextMenu()[2].Children;

            Check("选中两个：六种对齐亮起、两种分布仍判灰（两个图元「分布」的结果就是原地不动，亮着只会让人点了以为坏了）",
                alignItems.Take(6).All(i => i.Command!.CanExecute(null))
                && alignItems.Skip(6).All(i => !i.Command!.CanExecute(null)),
                string.Join("/", alignItems.Select(i => $"{i.Name}={i.Command!.CanExecute(null)}")));

            var alignThird = editor.AddElement("Hmi.Rectangle", new Point(90, 60))!;
            editor.SelectedElements = new[] { layerHost, zTarget, alignThird };
            alignItems = editor.BuildElementContextMenu()[2].Children;

            Check("选中三个：分布也亮起（门槛确实读的是 MinimumCount，不是菜单里另写的数）",
                alignItems.Skip(6).All(i => i.Command!.CanExecute(null)),
                string.Join("/", alignItems.Select(i => $"{i.Name}={i.Command!.CanExecute(null)}")));

            // 真的点一次：左对齐把三个图元的 X 都拉到最靠左那个的左边缘，
            // 且整批只产出一条撤销记录（一次排列 = 一次 Ctrl+Z）。
            var alignGroup = new[] { layerHost, zTarget, alignThird };
            double alignLeftEdge = alignGroup.Min(e => e.X);
            var alignXsBefore = alignGroup.Select(e => e.X).ToArray();

            ScadaEditHistory.Clear();
            alignItems[0].Command!.Execute(null);

            Check("左对齐：三个图元的 X 都贴到最靠左那个的左边缘（基准是整组包围盒，不是「某一个图元」）",
                alignGroup.All(e => e.X == alignLeftEdge),
                $"{string.Join("/", alignGroup.Select(e => e.X))} vs 基准 {alignLeftEdge}");

            Check("一次排列 = 一条撤销记录，标签里带个数与动作名（撤销按钮上能读出这一步撤的是什么）",
                ScadaEditHistory.UndoCount == 1
                && ScadaEditHistory.NextUndoLabel == "对齐 [3 个图元]：左对齐",
                $"{ScadaEditHistory.UndoCount} 条 / {ScadaEditHistory.NextUndoLabel ?? "null"}");

            ScadaEditHistory.Undo();
            Check("撤销一次，三个图元全部回到对齐前的位置（不是只回去一个）",
                alignGroup.Select(e => e.X).SequenceEqual(alignXsBefore),
                string.Join("/", alignGroup.Select(e => e.X)));

            // ---- 批量删除：一次动作、一条记录、撤销整批回来 ----
            //
            // 与对齐同一口径：作用于"可编辑的整批选中"，锁定项跳过而不是整批失败
            //（多选里混进锁住的底图是常态，为此让 Delete 键时灵时不灵最伤信任）。
            // 删除会真的改画面内容（不像对齐只换摆法），所以"撤销一次全回来"这条尤其要紧。
            var delA = editor.AddElement("Hmi.Rectangle", new Point(10, 10))!;
            var delB = editor.AddElement("Hmi.Rectangle", new Point(40, 10))!;
            var delC = editor.AddElement("Hmi.Rectangle", new Point(70, 10))!;
            var delKeep = editor.AddElement("Hmi.Rectangle", new Point(100, 10))!;
            delKeep.IsLocked = true;

            editor.SelectedElements = new[] { delA, delB, delC, delKeep };
            int beforeDelete = hostPage.Elements.Count;

            ScadaEditHistory.Clear();

            Check("多选里混着锁定的图元也能删：锁住的跳过而不是整批失败（Delete 键不该时灵时不灵）",
                editor.RemoveSelectedElement(), "");

            Check("删掉的是可编辑的那三个，锁住的那个原样留着",
                hostPage.Elements.Count == beforeDelete - 3
                && hostPage.Elements.Contains(delKeep)
                && !hostPage.Elements.Contains(delA)
                && !hostPage.Elements.Contains(delB)
                && !hostPage.Elements.Contains(delC),
                $"剩 {hostPage.Elements.Count} 个 / 锁定项还在={hostPage.Elements.Contains(delKeep)}");

            Check("一次删除 = 一条撤销记录，标签里带个数（多选删除不是一个图元一条）",
                ScadaEditHistory.UndoCount == 1 && ScadaEditHistory.NextUndoLabel == "删除 3 个图元",
                $"{ScadaEditHistory.UndoCount} 条 / {ScadaEditHistory.NextUndoLabel ?? "null"}");

            Check("删完顺手清选中：主选中不该还指着刚删掉的对象（否则属性面板继续编辑一个幽灵）",
                editor.SelectedElements.Count == 0 && editor.SelectedElement == null,
                $"集合 {editor.SelectedElements.Count} 个 / 主选中={editor.SelectedElement?.Name ?? "null"}");

            ScadaEditHistory.Undo();

            Check("撤销一次，三个图元全部回来（不是只回来一个）",
                hostPage.Elements.Count == beforeDelete
                && hostPage.Elements.Contains(delA)
                && hostPage.Elements.Contains(delB)
                && hostPage.Elements.Contains(delC),
                $"回到 {hostPage.Elements.Count} 个");

            Check("撤销后选中仍是空集：不留下指向已不在画面上的对象的幽灵选中",
                editor.SelectedElements.Count == 0, $"{editor.SelectedElements.Count} 个");

            // 全锁死时删不动：一个都删不掉就该返回 false（菜单据此判灰，而不是"点了没反应"）
            editor.SelectedElements = new[] { delKeep };
            ScadaEditHistory.Clear();
            Check("选中的全是锁定项：删除返回 false 且画面纹丝不动（菜单判灰与这里同源）",
                !editor.RemoveSelectedElement() && hostPage.Elements.Count == beforeDelete
                && ScadaEditHistory.UndoCount == 0,
                $"{hostPage.Elements.Count} 个 / {ScadaEditHistory.UndoCount} 条");

            // ---- 领域层：TryAlignElements 的拒绝口径与分布语义 ----
            //
            // 上面验的是"菜单点下去对不对"，这里直接问领域层"什么情况下该拒绝"。
            // 菜单判灰读的是同一个 MinimumCount，但那只是"提前告诉用户"；这一道是"真拦下来"——
            // 从键盘、脚本或将来别处进来的调用绕不过菜单，只能靠这道兜住。
            var domA = editor.AddElement("Hmi.Rectangle", new Point(0, 0))!;
            var domB = editor.AddElement("Hmi.Rectangle", new Point(50, 0))!;
            var domLocked = editor.AddElement("Hmi.Rectangle", new Point(90, 0))!;
            domLocked.IsLocked = true;

            Check("只给一个图元：TryAlignElements 拒绝并给出人话（对齐至少两个）",
                !hostPage.TryAlignElements(new[] { domA }, ScadaAlign.Left, out string oneErr)
                && oneErr.Contains("至少") && oneErr.Contains("2"),
                oneErr);

            Check("两个可编辑 + 一个锁定：对齐照常成立（锁定项不参与排列，也不拖垮整批）",
                hostPage.TryAlignElements(new[] { domA, domB, domLocked }, ScadaAlign.Left, out _), "");

            Check("一个可编辑 + 一个锁定：可排列的只剩一个，拒绝并点名「锁定的图元不参与」",
                !hostPage.TryAlignElements(new[] { domA, domLocked }, ScadaAlign.Left, out string lockErr)
                && lockErr.Contains("锁定"),
                lockErr);

            Check("分布门槛比对齐高一级：两个可编辑时被拒（两个图元的「分布」就是原地不动）",
                !hostPage.TryAlignElements(new[] { domA, domB }, ScadaAlign.DistributeHorizontal, out string distErr)
                && distErr.Contains("至少") && distErr.Contains("3"),
                distErr);

            // 外来户（不属于本画面）被剔除：拿别处的图元来对齐，不该把本画面的坐标改坏，
            // 也不该把"只剩一个可排列"这种本该拒绝的局面算成通过。
            var foreignElement = Named("Hmi.Rectangle", "外来户");
            double domABefore = domA.X;
            Check("不属于本画面的图元被剔除：外来户进不来，本画面图元也不会被它的坐标带偏",
                !hostPage.TryAlignElements(new[] { domA, foreignElement }, ScadaAlign.Left, out string foreignErr)
                && domA.X == domABefore,
                foreignErr);

            // 分布 = 等间隙，不是等中心距：宽度不一时，等中心距看上去仍是乱的
            //（宽的挤在一起、窄的之间空一大片），等间隙才是肉眼能验证"排匀了"的口径。
            var d1 = editor.AddElement("Hmi.Rectangle", new Point(0, 0))!;
            var d2 = editor.AddElement("Hmi.Rectangle", new Point(0, 100))!;
            var d3 = editor.AddElement("Hmi.Rectangle", new Point(0, 200))!;
            d1.Width = 40;
            d2.Width = 100;
            d3.Width = 20;
            d1.X = 0;
            d2.X = 200;
            d3.X = 400;

            Check("水平分布：三个图元被等间隙摊开，且两端原地不动（排匀不该把整组挪走）",
                hostPage.TryAlignElements(new[] { d1, d2, d3 }, ScadaAlign.DistributeHorizontal, out _)
                && d1.X == 0 && d3.X == 400
                && Math.Abs((d2.X - (d1.X + d1.Width)) - (d3.X - (d2.X + d2.Width))) < 1e-9,
                $"X={d1.X}/{d2.X}/{d3.X} 宽={d1.Width}/{d2.Width}/{d3.Width}");

            ScadaEditHistory.Clear();

            editor.SelectedElement = null;
            Check("没选中图元时不产出菜单（宿主据此不弹，而不是弹一个空壳）",
                editor.BuildElementContextMenu().Count == 0, "");

            Check("不属于本画面的图元：TryMoveElementZ 拒绝并给出人话（菜单与画布外的调用共用这一条判定）",
                !hostPage.TryMoveElementZ(Named("Hmi.Rectangle", "外来户"), ScadaZMove.ToFront, out var zError)
                && zError.Contains("不属于本画面"),
                zError);

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
            Check("Choice 型行的候选分两层：Choices 仍是描述符的落盘值，ChoiceOptions 才配上中文名",
                shape.Kind == ElementPropertyKind.Choice
                && string.Join("|", shape.Choices) == "Circle|Square"
                && string.Join("|", shape.ChoiceOptions.Select(o => o.Value)) == "Circle|Square"
                && string.Join("|", shape.ChoiceOptions.Select(o => o.DisplayName)) == "圆形|正方形"
                && shape.Value == "Circle",
                string.Join("|", shape.ChoiceOptions.Select(o => $"{o.Value}={o.DisplayName}")));

            // 词汇表是"面板显示中文"的唯一产地，也是将来做中英切换要换的那张表。
            // 这里钉的是它的两条对外契约：认得的值翻成中文、认不得的原样返回（原样返回的理由见类注释）。
            Check("词汇表：认得的值翻中文，认不得的原样返回（位按钮的中文候选、权限行的「不限制」都靠这一条）",
                ScadaChoiceNames.DisplayName("Output") == "输出"
                && ScadaChoiceNames.DisplayName("Circle") == "圆形"
                && ScadaChoiceNames.DisplayName("置位") == "置位"
                && ScadaChoiceNames.DisplayName("不限制") == "不限制"
                && ScadaChoiceNames.DisplayName("还没翻译的新值") == "还没翻译的新值"
                && ScadaChoiceNames.DisplayName(null) == string.Empty,
                ScadaChoiceNames.DisplayName("Output"));

            // 写回的是 Value 而不是显示名：下拉选中"正方形"，模型里必须还是 "Square"。
            // 这条断言与上面那条是一对——分开两层之后最怕的就是"中文被写进了 .vms"。
            shape.Value = "Square";
            Check("换下拉项即写模型：形状改成正方形（写回的是落盘值 Square，不是显示名「正方形」）",
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
                afterActivate == baseline + 1 && groupsRaised == baseline + 2 && Total(panel) == 13 + AnimationRows + UniversalRows,
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

            Check("没选中图元即长出画面属性行 + 画面事件行，行数与组序全取自两份声明（面板不写死）",
                Total(panel) == ScadaPageProperties.All.Count + ScadaPageEvents.All.Count
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

            // ---- 「运行」组：启动画面（方案级单选） + 画面级事件行（加载完成 / 卸载）----
            // 「启动画面」与事件行看着长得一模一样（都是勾选框），落点却相反：一个存方案的单个 Id，
            // 一个存各自画面上的钩子。下面的断言就是钉这个差别的，别再被界面骗过去。

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

            // ---------------- ⑩B 画面级事件行：动作表在界面上直接编（S3-e4） ----------------
            //
            // 此前画面级动作只能手改 .vms（易错、且写错字段是静默丢），现在与图元级共用同一份行实现。
            // 断言方式刻意与图元侧保持一致：同样验"命令把模型改对了"、同样验行自述——
            // 因为两边跑的就是同一份代码，差别只剩清单来源与分组。

            var pageLoadedRow = (ScadaEventRow)Row("Event.0");
            var pageUnloadRow = (ScadaEventRow)Row("Event.1");

            Check("画面事件清单长出的行落在「运行」组（与「启动画面」同处），图元事件另有「事件」组——分组是宿主清单给的，不是写死在行里",
                panel.Groups.Single(g => g.Name == "运行").Rows.Count == 1 + ScadaPageEvents.All.Count
                && pageLoadedRow.Group == ScadaPageEvents.Group
                && ScadaEventRow.ElementGroup == "事件",
                string.Join("/", panel.Groups.Select(g => $"{g.Name}:{g.Rows.Count}")));

            Check("画面事件行的宿主是画面本身（不是图元）：同一份行实现靠的正是这个接口，面板这一层没有类型分支",
                ReferenceEquals(pageLoadedRow.Host, page)
                && pageLoadedRow.EventType == ScadaEventType.Loaded
                && pageUnloadRow.EventType == ScadaEventType.Unloaded,
                $"{pageLoadedRow.EventType}/{pageUnloadRow.EventType}");

            Check("「卸载」也在清单里：运行态停止 / 切走这一页时真会发，不是永远不响的空钩子",
                ScadaPageEvents.All.Contains(ScadaEventType.Unloaded), "");

            Check("没勾的画面事件行没有动作表（null，不是空表）——与图元侧同一语义，模板据此整块收起动作区",
                pageLoadedRow.Actions == null && !pageLoadedRow.HasActions, "");

            ScadaEditHistory.Clear(); // 撤销栈是全局静态的，本段压栈、出段前清
            pageLoadedRow.Value = "True";
            Check("勾上画面「加载完成」：建钩子 + 自动补一条默认「记录日志」，与图元侧、与 EnableLoadedEvent 写出同一个模型状态",
                pageLoadedRow.HasActions
                && ReferenceEquals(pageLoadedRow.Actions, page.FindEventHook(ScadaEventType.Loaded)!.Actions)
                && pageLoadedRow.Actions!.Count == 1 && pageLoadedRow.Actions[0].Type == ScadaActionType.Log
                && page.EnableLoadedEvent,
                $"HasActions={pageLoadedRow.HasActions} / {pageLoadedRow.Actions?.Count ?? -1} 条");

            Check("一次勾选 = 一条撤销位（钩子与首条动作包在同一个作用域里，撤销不会只删掉动作、留下一条空钩子）",
                ScadaEditHistory.UndoCount == 1 && ScadaEditHistory.NextUndoLabel == "配置事件 [Loaded]",
                $"{ScadaEditHistory.UndoCount} 条 / {ScadaEditHistory.NextUndoLabel ?? "null"}");

            pageLoadedRow.AddActionCommand.Execute();
            Check("点「添加动作」：画面级动作表同样变 2 条——增删改序那一整套命令与图元侧是同一份代码",
                pageLoadedRow.Actions!.Count == 2 && pageLoadedRow.Actions[1].Type == ScadaActionType.Log,
                $"{pageLoadedRow.Actions.Count} 条");

            var pageAction1 = pageLoadedRow.Actions![0];
            var pageAction2 = pageLoadedRow.Actions[1];
            pageAction1.Text = "甲";
            pageAction2.Text = "乙";
            pageLoadedRow.MoveActionDownCommand.Execute(pageAction1);
            Check("画面级动作表调序：两条真换位（执行侧按集合顺序跑，所以「↓」改的是运行行为）",
                pageLoadedRow.Actions!.Count == 2
                && pageLoadedRow.Actions[0].Text == "乙" && pageLoadedRow.Actions[1].Text == "甲",
                $"{pageLoadedRow.Actions[0].Text}/{pageLoadedRow.Actions[1].Text}");

            pageLoadedRow.RemoveActionCommand.Execute(pageLoadedRow.Actions![0]);
            Check("画面级动作表删除：只剩一条（删光也不顺手摘钩子——「配置」与「行为」是两件事，与图元侧同一口径）",
                pageLoadedRow.Actions!.Count == 1, $"{pageLoadedRow.Actions.Count} 条");

            Row("Event.0").Value = "False";
            Check("取消勾选画面事件：整条钩子（连同动作）摘掉，投影字段 EnableLoadedEvent 同步回假",
                page.FindEventHook(ScadaEventType.Loaded) == null && !page.EnableLoadedEvent
                && Row("Event.0").Value == "False",
                $"{page.EnableLoadedEvent} / {Row("Event.0").Value}");

            Check("画面事件行与「启动画面」各写各的：前者是画面自己的钩子，后者是方案上的 Id（同组不同源）",
                editor.Document.StartupPageId == page.PageId
                && page.FindEventHook(ScadaEventType.Loaded) == null && !page2.EnableLoadedEvent,
                $"Id={editor.Document.StartupPageId} / 钩子 {(page.FindEventHook(ScadaEventType.Loaded) == null ? "无" : "有")}");

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
                panel.HasElement && panel.Page == null && Total(panel) == 13 + AnimationRows + UniversalRows
                && Row("$Width").Value == "120",
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

            // 叠放要有"邻居"才谈得上端点：画面里只剩它一个时四个方向都无事可做（既在最上也在最下），
            // 那是真实状态，却验不出"菜单项会跟着位次翻转"。垫一张底图、再把指示灯压到最前，
            // 于是这一节的起点是"已经在最上面"——正好是上端点那一半。
            editor.AddElement("Hmi.Rectangle", new Point(0, 0));
            editor.SelectedElement = shotLamp;   // 放下新图元会自动选中它，把编辑目标抢回指示灯
            shotPage.TryMoveElementZ(shotLamp, ScadaZMove.ToFront, out _);

            var zMenuBefore = editor.BuildElementContextMenu();
            string zTitleBefore = zMenuBefore.Count > 1 ? zMenuBefore[1].Name : "（没产出菜单）";
            Check("右键菜单备好了邻居：编辑目标回到指示灯，起点是「已在最上」2 / 2（否则菜单操作的是刚放下的底图）",
                ReferenceEquals(editor.SelectedElement, shotLamp) && zTitleBefore == "叠放次序（2 / 2）",
                $"{editor.SelectedElement?.Name} / {zTitleBefore}");

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

            // 留证据图供人眼复核（不参与判定）：属性面板整幅的样子——行距、分组、变量带与 ƒx 的配色。
            string shotDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(shotDir);
            SavePng(shot, Path.Combine(shotDir, "scada-property-panel.png"));

            // ---- ⑪B 右键菜单的"长相"：描述翻成控件之后，图标/勾选/判灰/子菜单都还在 ----
            //
            // ⑪C 验的是"视图模型吐出的描述对不对"（纯数据，与 WPF 无关）。可用户看见的是控件：
            // 描述里 Icon/IsChecked/Children 都写对了，翻译那一步漏抄一个字段，菜单照样是残的——
            // 图层勾不出勾、叠放该灰的不灰、子菜单点开是空的、点了没反应。
            // 这一段就把真视图里那份翻译（ScadaEditorView.ToMenuItem）拿来装成真 ContextMenu 量一次，
            // 再渲染成图。刻意不照着描述另写一份翻译：那样验的是另写的那份，真视图里这份坏了照样漏。
            //
            // 断言宿主里没有 Application，App.xaml 里合并的样式字典取不到、{StaticResource Icon}
            // 也解析不了，所以把真 App.xaml 合并的那本 /UI;component/Themes/Generic.xaml 原样手工塞进
            // 菜单自己的 Resources，图标字体同样手工造一份等价的——这不是绕过真实，
            // 是把真实那本字典搬到一个没有 App 的环境里。
            //
            // 图标字体：App.xaml 里那份是 pack://application:,,,/UI;component/... 的绝对写法，
            // 那写法要靠 Application.ResourceAssembly 定位"application"那一档；断言宿主里没有 Application，
            // 实测会静默回落成 Arial（FamilyNames=Arial、TryGetGlyphTypeface=False），菜单上的图标就全成豆腐块。
            // 换成"包基址 + 相对路径"这一档（WPF 给无 App 宿主留的写法）指向同一个 otf、同一个族名，
            // 拿到的就是真字形——不是换了个字体，是换了个能解析出来的地址写法。
            var iconFont = new FontFamily(
                new Uri("pack://application:,,,/UI;component/Asserts/FontFamilys/"),
                "./Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");

            var iconFace = new Typeface(iconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // 菜单用到的码点逐个验：缺一个字形就是一格空白/豆腐块，而字符串层的断言照样全绿——
            // 那种绿是骗人的。这里把整份清单列全（图层 + 叠放四箭头 + 垃圾桶 + 对齐与分布九颗 + 锁定/解锁
            // + 复制/粘贴/再制/存为模板），以后换字形或改码点，漏验的这颗会以"证据图上一格空白"的形式暴露，
            // 而不是靠人眼去数。
            var menuGlyphs = new (uint Code, string What)[]
            {
                (0xF5FD, "图层"),
                (0xF0C9, "叠放次序"),
                (0xF102, "置顶"),
                (0xF062, "上移一层"),
                (0xF063, "下移一层"),
                (0xF103, "置底"),
                (0xF2ED, "删除图元"),
                (0xF039, "对齐与分布（分组）"),
                (0xF036, "左对齐"),
                (0xF037, "水平居中"),
                (0xF038, "右对齐"),
                (0xF341, "顶对齐"),
                (0xF034, "垂直居中"),
                (0xF33D, "底对齐"),
                (0xF337, "水平分布"),
                (0xF338, "垂直分布"),
                (0xF023, "锁定图元"),
                (0xF3C1, "解锁图元"),
                (0xF0C5, "复制"),
                (0xF0EA, "粘贴"),
                (0xF24D, "再制"),
                (0xF02E, "存为模板"),
            };

            bool hasIconGlyphs = iconFace.TryGetGlyphTypeface(out var iconGlyphs);

            var missingGlyphs = hasIconGlyphs
                ? menuGlyphs
                    .Where(g => !iconGlyphs.CharacterToGlyphMap.ContainsKey((int)g.Code))
                    .Select(g => $"{g.What}(U+{g.Code:X4})")
                    .ToList()
                : new List<string>();

            Check("图标字体真的解析出来了、菜单要用的每一个码点都在字形表里（缺一个就是证据图上一格空白，"
                + "而字符串层的断言照样全绿——那种绿是骗人的）",
                hasIconGlyphs
                && iconFont.FamilyNames.Values.Contains("Font Awesome 6 Pro Solid")
                && missingGlyphs.Count == 0,
                !hasIconGlyphs
                    ? "字体没解析出来（整排都会是豆腐块）"
                    : missingGlyphs.Count > 0
                        ? $"缺字形：{string.Join("、", missingGlyphs)}"
                        : string.Join("|", iconFont.FamilyNames.Values));

            ContextMenu BuildMenu(IEnumerable<ScadaMenuItem> descriptions)
            {
                var built = new ContextMenu { FontFamily = iconFont };
                built.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/UI;component/Themes/Generic.xaml", UriKind.Relative),
                });

                foreach (var description in descriptions)
                    built.Items.Add(ScadaEditorView.ToMenuItem(description));

                built.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                built.Arrange(new Rect(0, 0,
                    Math.Max(1, built.DesiredSize.Width), Math.Max(1, built.DesiredSize.Height)));
                built.UpdateLayout();
                return built;
            }

            // 给这一节铺一个"有归属"的样子：图元挂到具名图层上，勾才打得在具名层上
            //（否则证据图上只有「（未分层）」那一项有勾，看不出"归属是跟着模型走的"）。
            var shotLayer = shotPage.AddLayer("设备层");
            shotPage.TryAssignLayer(shotLamp, shotLayer, out _);

            // 再铺一个"够得着对齐"的样子：对齐要两个以上才有意义，只选中一个的话整栏都是灰的，
            // 证据图上就看不出这些图标本来长什么样（灰字色下细节几乎看不清）。
            // 补完必须把指示灯重新置顶——新图元的 ZIndex 比它大，否则下面那条"已经在最上"的判灰断言会反过来。
            var alignShotA = editor.AddElement("Hmi.Rectangle", new Point(120, 30))!;
            var alignShotB = editor.AddElement("Hmi.Ellipse", new Point(200, 60))!;
            shotPage.TryMoveElementZ(shotLamp, ScadaZMove.ToFront, out _);

            // 三个一起选中：主选中仍是指示灯（图层那一栏的勾要打在它的归属上）
            editor.SelectedElements = new[] { shotLamp, alignShotA, alignShotB };

            var menuDesc = editor.BuildElementContextMenu();

            // 顶层那一栏与三个子菜单各装一份：子菜单的真身住在 Popup 里，而 Popup 没有窗口就渲染不出来，
            // 所以出图这份把子项平铺成独立的一份菜单——同一份翻译、同一本样式字典，
            // 长得就是弹出后那个样子（模板按 Role 分派，而叶子项与子菜单项共用同一套模板）。
            var headerMenu = BuildMenu(menuDesc);
            var layerMenu = BuildMenu(menuDesc[0].Children);
            var zMenu = BuildMenu(menuDesc[1].Children);
            var alignMenu = BuildMenu(menuDesc[2].Children);

            var headerItems = headerMenu.Items.OfType<MenuItem>().ToList();
            Check("顶层十栏都翻成了控件：前三栏是分组项（各有子项、自己不带命令），第四~七栏是剪贴板与模板叶子项，末三栏是锁定/组合/删除叶子项",
                headerItems.Count == 10
                && headerItems.All(m => m.Icon is string icon && icon.Length > 0)
                && headerItems.Take(3).All(m => m.Items.Count > 0 && m.Command == null)
                && headerItems[0].Items.Count == menuDesc[0].Children.Count
                && headerItems[1].Items.Count == menuDesc[1].Children.Count
                && headerItems[2].Items.Count == menuDesc[2].Children.Count
                && headerItems.Skip(3).All(m => m.Items.Count == 0 && m.Command != null),
                string.Join(" / ", headerItems.Select(m => $"{m.Header}:子项{m.Items.Count}")));

            // 勾选态要真的长在模板上：默认模板里 CheckMark 是 Collapsed，只有 IsChecked=True 才翻出来。
            // 只钉 IsChecked 这个 DP 不够——模板触发器被改坏、或 CheckMark 被删，菜单里就是
            // "勾了但看不见勾"，而 DP 上一切正常，截图上看也一切正常。
            var layerItems = layerMenu.Items.OfType<MenuItem>().ToList();
            var checkMarks = CollectVisual<TextBlock>(layerMenu, t => t.Name == "CheckMark").ToList();
            int visibleChecks = checkMarks.Count(t => IsShown(t, layerMenu));

            Check("「图层」那一列翻出来：全部可勾选、恰好一项打勾，且那一项的 ✓ 真的从 Collapsed 翻出来了",
                layerItems.Count == menuDesc[0].Children.Count
                && layerItems.All(i => i.IsCheckable && i.Command != null)
                && layerItems.Count(i => i.IsChecked) == 1
                && checkMarks.Count == layerItems.Count
                && visibleChecks == 1,
                $"{layerItems.Count} 项 / 勾 {layerItems.Count(i => i.IsChecked)} / CheckMark {checkMarks.Count} 个、可见 {visibleChecks} 个");

            Check("打勾的那一项就是模型当前归属（翻译把描述里的 IsChecked 搬过来了，不是照着顺序瞎勾一个）",
                layerItems.Single(i => i.IsChecked).Header is string checkedName && checkedName == shotLayer.Name,
                string.Join("、", layerItems.Select(i => $"{i.Header}={(i.IsChecked ? "✓" : "·")}")));

            // 端点判灰：四颗方向项里处在端点的两颗必须"看着就不能点"。起点已经在最上面，
            // 所以钉上端；IsEnabled 与字色两头都钉——"点下去什么都不发生的项"和"能点的"长得一样，
            // 是最伤信任的一种界面，而这种缺陷在静态的 XAML 里看不出来。
            var zItems = zMenu.Items.OfType<MenuItem>().ToList();

            static bool Greyed(MenuItem item)
                => !item.IsEnabled
                   && item.Foreground is SolidColorBrush grey && SameColor(grey.Color, "#FFA0A0A0");

            static bool Normal(MenuItem item)
                => item.IsEnabled
                   && item.Foreground is SolidColorBrush ink && SameColor(ink.Color, "#FF202020");

            static string Say(MenuItem item)
                => $"{item.Header}={(item.IsEnabled ? "亮" : "灰")}{ColorName((item.Foreground as SolidColorBrush)?.Color ?? Colors.Transparent)}";

            Check("「叠放次序」那一列翻出来四项，已经在最上时置顶/上移真的判灰（IsEnabled=false 且字色被模板刷成 #FFA0A0A0）",
                zItems.Count == 4 && Greyed(zItems[0]) && Greyed(zItems[1]) && Normal(zItems[2]) && Normal(zItems[3]),
                string.Join("、", zItems.Select(Say)));

            // 「对齐与分布」是四栏里唯一的"整批"栏：这一栏的图标最多（八颗各不相同），
            // 也是唯一"判灰与否取决于选中几个"的栏。当前恰好选中三个，八项都该是亮的——
            // 若翻译把 CanExecute 漏了，这里会以"八项全灰"的形式暴露，而那在截图上很像"没选中东西"，
            // 人眼未必分得清，所以交给断言。
            var alignShotItems = alignMenu.Items.OfType<MenuItem>().ToList();

            Check("「对齐与分布」那一列翻出来八项，各带图标、图标互不相同、选中三个时八项全亮",
                alignShotItems.Count == menuDesc[2].Children.Count
                && alignShotItems.Count == 8
                && alignShotItems.All(i => i.Icon is string a && a.Length > 0 && i.Command != null)
                && alignShotItems.Select(i => (string)i.Icon).Distinct().Count() == alignShotItems.Count
                && alignShotItems.All(i => i.IsEnabled),
                string.Join("、", alignShotItems.Select(i => $"{i.Header}={i.Icon}")));

            // 真执行一次：走的是控件上那颗 Command（翻译搬过来的那个），不是描述里那个。
            // 判灰亮着只说明"能点"，点下去动不动还得看 Command 有没有被搬到控件上。
            var alignGroupShot = new[] { shotLamp, alignShotA, alignShotB };
            double alignShotLeft = alignGroupShot.Min(e => e.X);
            alignShotItems[0].Command!.Execute(null);
            Check("点「左对齐」：三个图元的 X 都贴到最左那条边（控件上的命令真的落到了模型上）",
                alignGroupShot.All(e => e.X == alignShotLeft),
                string.Join("、", alignGroupShot.Select(e => e.X.ToString("0.##"))));

            // 点下去要真的落到模型上：翻译漏抄 Command 的话，菜单长得一模一样、点了一点反应没有——
            // 这类缺陷在截图上完全看不出来，只有真执行一次才知道。
            layerItems.Single(i => Equals(i.Header, ScadaEditorVM.UnassignedLayerName)).Command!.Execute(null);
            Check("点「（未分层）」：命令真的落到模型上，归属退回 Guid.Empty（「取消分层」是个说得出口的操作）",
                shotLamp.LayerId == Guid.Empty, shotLamp.LayerId.ToString());

            layerItems.Single(i => Equals(i.Header, shotLayer.Name)).Command!.Execute(null);
            Check("点具名图层：归属又写回去，写的是那个图层对象而不是同名新层",
                shotLamp.LayerId == shotLayer.LayerId, shotLamp.LayerId.ToString());

            // 样式字典到底生效没有：模板挂上了才会有那张 #FAFAFA 的卡片。
            // 这一条钉的是"注入的那本字典真的被吃进去了"——它一旦失效，上面的模板断言会集体变红，
            // 但那时很难分辨是模板写错了还是字典没进来，所以单独钉一次。
            var layerShot = RenderToBitmap(layerMenu,
                (int)Math.Ceiling(Math.Max(1, layerMenu.RenderSize.Width)),
                (int)Math.Ceiling(Math.Max(1, layerMenu.RenderSize.Height)));
            Check("菜单的模板真的挂上了：卡片底色 #FFFAFAFA 被刷出来（说明手工注入的样式字典生效，不是一摞裸控件）",
                layerShot.PixelWidth > 60 && layerShot.PixelHeight > 20
                && HasColorIn(layerShot, new Rect(0, 0, layerShot.PixelWidth, layerShot.PixelHeight), "#FFFAFAFA"),
                $"{layerShot.PixelWidth}×{layerShot.PixelHeight}");

            // 留证据图供人眼复核（不参与判定）：菜单好不好看只有人眼说了算，
            // 字形能不能出来则由上面那条图标字体断言兜底（它红了说明这几张图是豆腐块）。
            foreach (var (shotMenu, fileName) in new[]
                     {
                         (headerMenu, "scada-element-context-menu.png"),
                         (layerMenu, "scada-element-context-menu-layer.png"),
                         (zMenu, "scada-element-context-menu-zorder.png"),
                         (alignMenu, "scada-element-context-menu-align.png"),
                     })
            {
                string path = Path.Combine(shotDir, fileName);
                SavePng(RenderToBitmap(shotMenu,
                    (int)Math.Ceiling(Math.Max(1, shotMenu.RenderSize.Width)),
                    (int)Math.Ceiling(Math.Max(1, shotMenu.RenderSize.Height))), path);
                Console.WriteLine($"        （渲染证据：{path}）");
            }

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
            LayerZOrderChecks();
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

        // ---- V-7 叠放次序（TryMoveElementZ：唯一写入口，四方向 + 重编号 + 端点空操作） ----

        private static void LayerZOrderChecks()
        {
            var page = new ScadaPage();
            var layer = page.AddLayer("甲层");

            // 老工程的常见形状：一片图元的 ZIndex 全是 0（早期版本没写这个字段）。
            // 这一片恰好也是"稳定排序"最该被钉住的输入——分不出先后的图元不该被随机洗一遍。
            var a = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "甲", LayerId = layer.LayerId, ZIndex = 0 };
            var b = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "乙", LayerId = layer.LayerId, ZIndex = 0 };
            var c = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "丙", LayerId = layer.LayerId, ZIndex = 0 };
            page.Elements.Add(a);
            page.Elements.Add(b);
            page.Elements.Add(c);

            static string Names(ScadaPage p)
                => string.Join("、", p.Elements.OrderBy(e => e.ZIndex).Select(e => e.Name));

            static string ZText(ScadaPage p)
                => string.Join("、", p.Elements.OrderBy(e => e.ZIndex).Select(e => $"{e.Name}:{e.ZIndex}"));

            static bool Renumbered(ScadaPage p)
                => p.Elements.OrderBy(e => e.ZIndex).Select((e, i) => e.ZIndex == i + 1).All(ok => ok);

            var foreign = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "外来的" };
            Check("叠放：图元不属于本画面时拒绝（文案与改归属同款口径，界面直接照抄）",
                !page.TryMoveElementZ(foreign, ScadaZMove.ToFront, out var foreignErr)
                && foreignErr == "图元不属于本画面，无法调整叠放次序", foreignErr);
            Check("叠放：null 图元拒绝且不抛（视图侧可以直接喂还没解析出来的结果）",
                !page.TryMoveElementZ(null, ScadaZMove.ToFront, out var nullErr) && nullErr.Length > 0, nullErr);

            Check("ZIndex 整片重复时按集合次序认先后，一个数字都不改（不动就不该算一次变更）",
                Names(page) == "甲、乙、丙" && page.Elements.All(e => e.ZIndex == 0), ZText(page));

            Check("置顶：挪到所有图元之上，并把全画面重编号成 1..N（用户不必心算数字，只表达『我要它在最上面』）",
                page.TryMoveElementZ(a, ScadaZMove.ToFront, out var frontErr)
                && Names(page) == "乙、丙、甲" && Renumbered(page), frontErr + " / " + ZText(page));

            int vNoop = page.Version;
            Check("已在最上层时再置顶 / 上移都是空操作：放行且不刷 Version（不然每点一下都把方案标脏一次）",
                page.TryMoveElementZ(a, ScadaZMove.ToFront, out _)
                && page.TryMoveElementZ(a, ScadaZMove.Forward, out _)
                && page.Version == vNoop, $"Version {vNoop} → {page.Version}");

            int vMove = page.Version;
            Check("下移一层：与紧挨着它下面的那个图元换位，且 Version 动（真挪动才标脏）",
                page.TryMoveElementZ(a, ScadaZMove.Backward, out var backErr)
                && Names(page) == "乙、甲、丙" && page.Version > vMove, backErr + " / " + ZText(page));

            Check("置底：挪到所有图元之下",
                page.TryMoveElementZ(a, ScadaZMove.ToBack, out _) && Names(page) == "甲、乙、丙" && Renumbered(page),
                ZText(page));

            int vBottom = page.Version;
            Check("已在最下层时置底 / 下移都是空操作",
                page.TryMoveElementZ(a, ScadaZMove.ToBack, out _)
                && page.TryMoveElementZ(a, ScadaZMove.Backward, out _)
                && page.Version == vBottom, $"Version {vBottom} → {page.Version}");

            int vUnknown = page.Version;
            Check("不认识的叠放取值按原地不动处理（坏在一个枚举上不该让整张画面炸掉）",
                page.TryMoveElementZ(b, (ScadaZMove)99, out _) && page.Version == vUnknown,
                $"Version {vUnknown} → {page.Version}");

            Check("调叠放只改 ZIndex：图元集合的次序与实例都不动（集合次序是身份，ZIndex 才是叠放）",
                page.Elements.Count == 3 && ReferenceEquals(page.Elements[0], a)
                && ReferenceEquals(page.Elements[1], b) && ReferenceEquals(page.Elements[2], c), "");

            // 跨图层：叠放是画面级的一条序列，图层次序不参与"谁盖住谁"的计算。
            var otherLayer = page.AddLayer("乙层");
            var d = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "丁", LayerId = otherLayer.LayerId, ZIndex = 0 };
            page.Elements.Add(d);
            Check("叠放是画面级的：跨图层的图元一起重编号，图层归属一点不动（谁盖住谁只看 ZIndex）",
                page.TryMoveElementZ(d, ScadaZMove.ToFront, out _)
                && Names(page) == "甲、乙、丙、丁" && Renumbered(page) && d.LayerId == otherLayer.LayerId,
                ZText(page));

            var solo = new ScadaPage();
            var only = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "独苗", ZIndex = 7 };
            solo.Elements.Add(only);
            int vSolo = solo.Version;
            Check("画面上只有一个图元时四个方向都是空操作（不刷 Version，也不把 7 改写成 1）",
                solo.TryMoveElementZ(only, ScadaZMove.ToFront, out _)
                && solo.TryMoveElementZ(only, ScadaZMove.ToBack, out _)
                && solo.TryMoveElementZ(only, ScadaZMove.Forward, out _)
                && solo.TryMoveElementZ(only, ScadaZMove.Backward, out _)
                && solo.Version == vSolo && only.ZIndex == 7,
                $"Version {vSolo} → {solo.Version}，ZIndex {only.ZIndex}");
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

            // ---------------- ⑦ 多选：选中集合的归一与收口 ----------------
            //
            // 画布这边只维护一条不变式：**主选中必须落在选中集合里**（见 SelectedElements 的注释）。
            // 维持方式不是"互相回写"而是"谁写谁负责"：画布走 SetSelection（先集合、后主选中），
            // 宿主走它自己的 ApplySelection（同样顺序）。于是任何一侧发起的改动经绑定流到另一侧时，
            // 落到那边就已经是自洽的，不需要第二遍归一。
            //
            // 这里逐个钉住 DP 层面能观察到的几种收口形态。走错了不会崩，
            // 只会让"选中框画在谁身上"和"属性面板编辑谁"悄悄对不上——而那要等用户误删一次才发现。
            //
            // 为什么不验选中框本身：视觉层在 UpdateSelectionVisual 里要 IsLoaded 才画，
            // 而断言宿主没有窗口（IsLoaded 恒 false），多选虚线/合并包围盒在无头环境里量不到。
            // 能无头量到的是"选中集合这份数据长什么样"，那也正是多选全部下游命令的唯一输入。
            var multiDoc = new ScadaDocument();
            var multiPage = multiDoc.AddPage("多选画面");
            var multiLayer = multiPage.DefaultLayer!;
            var multiHidden = multiPage.AddLayer("多选隐藏层");
            multiHidden.IsVisible = false;

            var mRect = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "甲", LayerId = multiLayer.LayerId, X = 0, Y = 0, Width = 40, Height = 30 };
            var mEllipse = new ScadaElement { TypeKey = "Hmi.Ellipse", Name = "乙", LayerId = multiLayer.LayerId, X = 100, Y = 0, Width = 40, Height = 30 };
            var mGhost = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "丙", LayerId = multiHidden.LayerId, X = 200, Y = 0, Width = 40, Height = 30 };
            foreach (var element in new[] { mRect, mEllipse, mGhost })
                multiPage.Elements.Add(element);

            var multiHost = BuildHost(multiPage, withPage: true);

            Check("选中集合的默认值是空数组而不是 null（消费侧每一处都免判空）",
                multiHost.SelectedElements != null && multiHost.SelectedElements.Count == 0,
                multiHost.SelectedElements?.Count.ToString() ?? "null");

            // ① 只写主选中、且它不在集合里 → 集合被归一成"就它一个"。
            //    这是单选时代所有宿主的写法（editor.SelectedElement = x），归一漏了就会
            //    出现"属性面板编辑甲、选中框却画在乙身上"。
            multiHost.SelectedElement = mEllipse;
            Check("只写主选中（不在集合里）→ 集合归一成单元素，主选中与集合立刻自洽",
                multiHost.SelectedElements.Count == 1
                && ReferenceEquals(multiHost.SelectedElements[0], mEllipse)
                && ReferenceEquals(multiHost.SelectedElement, mEllipse),
                $"集合 {multiHost.SelectedElements.Count} 个 / 主选中={multiHost.SelectedElement?.Name ?? "null"}");

            // ② 集合里已有两个，再把主选中写成组内第二个 → 集合一个字节都不动。
            //    多选状态下右键点组内某一个走的正是这条路径；打散了就变成"右键一按，整批选中没了"。
            multiHost.SelectedElements = new[] { mRect, mEllipse };
            multiHost.SelectedElement = mEllipse;
            Check("主选中落在集合里：集合原样不动（多选下右键点组内某一个不该把整批打散）",
                multiHost.SelectedElements.Count == 2
                && ReferenceEquals(multiHost.SelectedElements[0], mRect)
                && ReferenceEquals(multiHost.SelectedElements[1], mEllipse)
                && ReferenceEquals(multiHost.SelectedElement, mEllipse),
                string.Join("、", multiHost.SelectedElements.Select(e => e.Name)));

            // ③ 主选中置 null 而集合非空 → 集合一起清掉。
            //    只清主选中会留下"集合还在、主选中没了"，而下一批命令判可用性看的是集合，
            //    于是"点了空白处，删除键却还能用"。
            multiHost.SelectedElement = null;
            Check("主选中置 null：集合跟着清空（留半截集合会让「点了空白处删除键仍可用」）",
                multiHost.SelectedElements.Count == 0 && multiHost.SelectedElement == null,
                $"集合 {multiHost.SelectedElements.Count} 个");

            // ④ 直接写集合：画布只读它，不替宿主改主选中。
            //    这是"谁写谁维持"的另一半——画布若顺手把主选中也设成集合首项，
            //    宿主那边"批量选中、主选中仍是上一次那一个"的用法就被无声改掉了。
            multiHost.SelectedElements = new[] { mRect, mEllipse };
            Check("直接写集合：画布不替宿主改主选中（不变式由写入方维持，画布只读这个属性）",
                multiHost.SelectedElements.Count == 2 && multiHost.SelectedElement == null,
                $"集合 {multiHost.SelectedElements.Count} 个 / 主选中={multiHost.SelectedElement?.Name ?? "null"}");

            // ⑤ 集合成员被删 → 从选中里剔掉，主选中跟着落到幸存者首项。
            //    这一条走的是 ItemsSource 的集合变更回调（不是绑定），是"事后收口"那一半。
            multiHost.SelectedElements = new[] { mRect, mEllipse };
            multiHost.SelectedElement = mRect;
            multiPage.Elements.Remove(mRect);
            Check("集合成员被删：从选中里剔掉，主选中跟着落到幸存者首项（不留指向空气的选中框）",
                multiHost.SelectedElements.Count == 1
                && ReferenceEquals(multiHost.SelectedElements[0], mEllipse)
                && ReferenceEquals(multiHost.SelectedElement, mEllipse),
                $"集合 {multiHost.SelectedElements.Count} 个 / 主选中={multiHost.SelectedElement?.Name ?? "null"}");

            // ⑥ 选中项所在图层被隐藏 → 同样剔掉。
            //    这条走的是图层开关那条路（ApplyLayerVisibility 的第一句），
            //    与 ⑤ 是两条独立入口，都得收口——漏一条就会留下"看不见却随时能被 Delete"的选中项。
            multiHidden.IsVisible = true;    // 先开一次：同值赋值会被 SetProperty 短路，翻不出重算
            multiHost.SelectedElements = new[] { mEllipse, mGhost };
            multiHost.SelectedElement = mGhost;
            multiHidden.IsVisible = false;
            Check("选中项所在图层被隐藏：照样从选中里剔掉（与「成员被删」是两条独立入口，都得收口）",
                multiHost.SelectedElements.Count == 1
                && ReferenceEquals(multiHost.SelectedElements[0], mEllipse)
                && ReferenceEquals(multiHost.SelectedElement, mEllipse),
                $"集合 {multiHost.SelectedElements.Count} 个 / 主选中={multiHost.SelectedElement?.Name ?? "null"}");
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

            // ---------------- ⑧ 画面导航（S8）：换页顺序、幂等、页栈与回退 ----------------
            //
            // 这一组钉的是路线图上那句"运行中能在画面之间走，且走得干净"：
            // 顺序（旧页 Unloaded 先于新页 Loaded）、幂等（连点两下只触发一次 Loaded）、
            // 页栈（能回退、有上限）、以及会话停掉之后的迟到调用不许再换页。

            var navDoc = new ScadaDocument();
            var pA = navDoc.AddPage("A 主页");
            var pB = navDoc.AddPage("B 列表");
            var pC = navDoc.AddPage("C 详情");
            navDoc.SetStartupPage(pA);

            // 三页各配一条 Loaded 与一条 Unloaded（都带一条动作），这样"发了几次"才数得清：
            // RaiseHooks 对"配了钩子但动作表是空的"直接跳过，不配动作就看不见广播。
            foreach (var page in new[] { pA, pB, pC })
            {
                page.AddEventHook(ScadaEventType.Loaded).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
                page.AddEventHook(ScadaEventType.Unloaded).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
            }

            var navRt = new ScadaRuntime(navDoc);
            var navOrder = new List<string>();
            navRt.PageLoaded += p => navOrder.Add($"L:{p.Name}");
            navRt.PageEventRaised += (p, h) =>
                navOrder.Add($"{(h.Event == ScadaEventType.Unloaded ? "U" : "L")}:{p.Name}");

            Check("会话就是那个切页出口：分发器只认领域层的接口，不认具体的会话类型",
                navRt is IScadaNavigator, navRt.GetType().Name);

            navRt.Start();
            Check("起步：当前页是启动画面、页栈空（刚进来没地方可回退）",
                ReferenceEquals(navRt.CurrentPage, pA) && navRt.PageStackDepth == 0 && !navRt.CanGoBack,
                $"{navRt.CurrentPage?.Name ?? "null"} / 栈 {navRt.PageStackDepth}");

            navOrder.Clear();
            Check("切到 B 页：返回 true、当前页换过去、旧页压进栈（回退按钮从此有得点）",
                navRt.Navigate(pB) && ReferenceEquals(navRt.CurrentPage, pB)
                && navRt.PageStackDepth == 1 && navRt.CanGoBack,
                $"{navRt.CurrentPage?.Name ?? "null"} / 栈 {navRt.PageStackDepth}");
            Check("换页顺序是「旧页 Unloaded → 新页 Loaded」：两条钩子写同一个变量时，顺序反了最终值就是错的",
                string.Join("|", navOrder) == "U:A 主页|L:B 列表", string.Join("|", navOrder));

            // 幂等：目标就是当前页 —— 要的状态已经成立，但不能再发一次 Loaded、也不能再压一层栈
            navOrder.Clear();
            var stackBefore = navRt.PageStackDepth;
            Check("目标画面本来就是当前页：返回 true（意图达成）但一条事件都不发、栈也不动",
                navRt.Navigate(pB) && navOrder.Count == 0 && navRt.PageStackDepth == stackBefore,
                $"事件 {navOrder.Count} 条 / 栈 {navRt.PageStackDepth}");

            Check("按名字切页忽略大小写（画面改过名之后，动作里存的名字未必与现在一模一样）",
                navRt.Navigate(Guid.Empty, "c 详情", out _) && ReferenceEquals(navRt.CurrentPage, pC),
                navRt.CurrentPage?.Name ?? "null");

            navOrder.Clear();
            var stackBeforeMiss = navRt.PageStackDepth;
            var missOk = navRt.Navigate(Guid.Empty, "不存在的画面", out var missReason);
            Check("目标画面找不到：返回 false 且原因照实说，当前页与页栈一动不动、一条事件都不发",
                !missOk && missReason == "方案里没有叫「不存在的画面」的画面"
                && ReferenceEquals(navRt.CurrentPage, pC) && navRt.PageStackDepth == stackBeforeMiss
                && navOrder.Count == 0,
                missReason ?? "null");

            Check("Id 优先于名字：Id 命中就直接切过去，名字是过期的不影响",
                navRt.Navigate(pB.PageId, "不存在的画面", out _) && ReferenceEquals(navRt.CurrentPage, pB),
                navRt.CurrentPage?.Name ?? "null");

            Check("GoBack 回到刚离开的那一页（栈顶就是它）",
                navRt.GoBack() && ReferenceEquals(navRt.CurrentPage, pC), navRt.CurrentPage?.Name ?? "null");
            Check("GoBack 第二次继续往下回退，栈随之变浅",
                navRt.GoBack() && ReferenceEquals(navRt.CurrentPage, pB) && navRt.PageStackDepth == 1,
                $"{navRt.CurrentPage?.Name ?? "null"} / 栈 {navRt.PageStackDepth}");
            Check("回退到底：当前页回到起步页，栈空且 CanGoBack 变 false（回退按钮该跟着置灰）",
                navRt.GoBack() && ReferenceEquals(navRt.CurrentPage, pA)
                && navRt.PageStackDepth == 0 && !navRt.CanGoBack,
                $"{navRt.CurrentPage?.Name ?? "null"} / 栈 {navRt.PageStackDepth}");
            Check("栈空再点回退：返回 false 而不是抛异常（按钮点多了不该炸）",
                !navRt.GoBack(), "");

            // 页栈上限：来回跳不该无限增长（组态软件是要连开几个月的）
            for (int i = 0; i < 40; i++)
            {
                navRt.Navigate(pA);
                navRt.Navigate(pB);
            }
            Check("页栈有上限：来回跳 80 次之后只留最近 32 层（这是唯一会随操作时长增长的集合）",
                navRt.PageStackDepth == 32, $"栈 {navRt.PageStackDepth}");

            navOrder.Clear();
            navRt.Stop();
            Check("Stop 补发当前页的 Unloaded：用户配在卸载事件上的收尾动作不会被静默丢掉",
                navOrder.Count == 1 && navOrder[0] == "U:B 列表", string.Join("|", navOrder));
            Check("Stop 之后页栈清空（下次运行是干净的一轮，不带着上一轮的回退历史）",
                navRt.PageStackDepth == 0 && !navRt.CanGoBack, $"栈 {navRt.PageStackDepth}");

            Check("运行结束后的迟到切页：返回 false 且原因是「运行已经结束」，当前页保持为空",
                !navRt.Navigate(pB.PageId, null, out var lateReason)
                && lateReason == "运行已经结束" && navRt.CurrentPage == null,
                lateReason ?? "null");

            var navPort = new ScadaNavigator();
            Check("切页出口没挂会话时安静返回 false 而不是空引用（关窗口的收尾会与迟到的动作撞上）",
                !navPort.Navigate(pB.PageId, "B 列表", out var portReason)
                && portReason == "运行已经结束" && navPort.Session == null,
                portReason ?? "null");

            // 出口是"长命的分发器"与"一次性的会话"之间唯一那根线头：宿主 Start() 挂上、关窗口摘掉。
            // 上面那条只验了"没挂"这一档，挂上之后到底转不转发、摘掉之后是不是立刻失效，得另起一个干净的会话来钉。
            var portDoc = new ScadaDocument();
            var portA = portDoc.AddPage("A 主页");
            var portB = portDoc.AddPage("B 列表");
            portDoc.SetStartupPage(portA);
            var portSession = new ScadaRuntime(portDoc);
            portSession.Start();

            navPort.Attach(portSession);
            var fwdOk = navPort.Navigate(portB.PageId, "B 列表", out var fwdReason);
            Check("出口挂上会话后：切页转发到会话，当前页真的换过去（分发器与运行态之间就这一根线头）",
                ReferenceEquals(navPort.Session, portSession)
                && fwdOk && ReferenceEquals(portSession.CurrentPage, portB) && fwdReason == null,
                $"{portSession.CurrentPage?.Name ?? "null"} / {fwdReason ?? "null"}");

            Check("会话报的失败原因原样透传（出口只转交，不另立一套措辞）",
                !navPort.Navigate(Guid.Empty, "不存在的画面", out var passReason)
                && passReason == "方案里没有叫「不存在的画面」的画面",
                passReason ?? "null");

            navPort.Attach(null);
            var detachedOk = navPort.Navigate(portB.PageId, "B 列表", out var afterDetach);
            Check("摘掉会话后立刻回到「运行已经结束」：关窗口的收尾与迟到的动作撞上也不出事",
                navPort.Session == null && !detachedOk && afterDetach == "运行已经结束",
                afterDetach ?? "null");

            portSession.Stop();
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
            // 切页出口也换成替身：分发器现在依赖它（S8），控制台里不该去碰任何运行态会话。
            // 整节共用一个，末尾可以核对"该不该走到它"——比每处新建一个更能说明问题。
            var nav = new FakeNavigator();
            var startVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "启动", DataType = typeof(int) };
            values.ById[startVar.VariableId] = startVar;
            values.ByName[startVar.Name] = startVar;
            var dispatcher = new ScadaActionDispatcher(log, values, nav);

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
                log.Lines.Count == 3 && log.Lines[0] == "I [组态事件] 未登录 · 启动按钮 · 按下 → 记一条",
                log.Lines.FirstOrDefault() ?? "无");
            Check("「写变量」这条也真执行了（S6-6 已接通）：值按变量类型现转后写进变量，日志留一行成功",
                log.Lines.Count == 3
                && log.Lines[1] == "I [组态事件] 未登录 · 启动按钮 · 按下 → 写变量 启动 := 1"
                && Equals(startVar.Value, 1),
                $"{log.Lines[1]} / 变量值 {startVar.Value ?? "null"}");
            Check("中间那条不再「等版本」，后面的动作照跑（一条不成不中断一串：接口契约②）",
                log.Lines.Count == 3 && log.Lines[2] == "I [组态事件] 未登录 · 启动按钮 · 按下 → 再记一条",
                log.Lines.Skip(2).FirstOrDefault() ?? "无");

            // ---------------- ①A 操作署名（S12）：日志每一行都要能看出「是谁在操作」 ----------------
            //
            // 这一行同时就是操作审计流水。气泡与状态栏都是转瞬即逝的，出了争执（「他说他按过」）
            // 能查的只有日志——而一条没有署名的日志，等于这条记录白记了。
            // 署名由调用方带进来（宿主取 IScadaAccessPolicy.CurrentUserName），分发器不认识用户系统。

            log = new LoggerStub();
            var named = new ScadaEventHook { Event = ScadaEventType.Pressed };
            named.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "改配方" });
            new ScadaActionDispatcher(log, values, nav).Dispatch(named, "配方按钮", "张三");
            Check("署名跟着操作者走，且排在最前：现场翻日志是「先看谁干的、再看干了什么」",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 张三 · 配方按钮 · 按下 → 改配方",
                log.Lines.FirstOrDefault() ?? "无");

            log = new LoggerStub();
            var anon = new ScadaEventHook { Event = ScadaEventType.Pressed };
            anon.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "改配方" });
            new ScadaActionDispatcher(log, values, nav).Dispatch(anon, "配方按钮", "   ");
            Check("署名取不到（null/空白）时落「未登录」，不留空白：空着的那一段在日志里读不出任何信息",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 未登录 · 配方按钮 · 按下 → 改配方",
                log.Lines.FirstOrDefault() ?? "无");

            log = new LoggerStub();
            var whoVar = new ScadaEventHook { Event = ScadaEventType.Pressed };
            whoVar.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = startVar.VariableId,
                VariableName = "启动",
                Value = "1",
            });
            new ScadaActionDispatcher(log, values, nav).Dispatch(whoVar, "启动按钮", "李四");
            Check("署名是**每一行**都带的（不是只写开头那行）：写变量那条同样能查出是谁写的",
                log.Lines.Count == 1
                && log.Lines[0] == "I [组态事件] 李四 · 启动按钮 · 按下 → 写变量 启动 := 1",
                log.Lines.FirstOrDefault() ?? "无");

            // ---------------- ①B 写变量的四种写不成：四件事、四句话、两种级别 ----------------
            //
            // 分档的意义在现场：没选变量=配置漏了（去面板补）、找不到=改名或删了（去变量管理对）、
            // 转不过=值写错了、写不进=设备侧的事（离线/没配地址）。合成一句"写变量失败"，
            // 用户就得把这四条路各试一遍。前两档 Warn（软件没坏），后两档 Error（这次真没成）。

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);

            var noTarget = new ScadaEventHook { Event = ScadaEventType.Pressed };
            noTarget.Actions.Add(new ScadaAction { Type = ScadaActionType.WriteVariable, Value = "1" });
            dispatcher.Dispatch(noTarget, "按钮");
            Check("①没选变量：Warn 一句「还没选变量」，不拿「写变量（未选变量）」再重复一遍",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].EndsWith("写变量：还没选变量，本条未执行"),
                log.Lines.FirstOrDefault() ?? "无");

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
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
            dispatcher = new ScadaActionDispatcher(log, values, nav);
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
            dispatcher = new ScadaActionDispatcher(log, offlineValues, nav);
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

            // ---------------- ①E 切换画面：走"解析 → 换页"这条出口，失败原因照实说（S8） ----------------
            //
            // 分发器拿不到运行态会话，只认 IScadaNavigator。这里给的是替身，
            // 验的是分发器这一侧：什么情况该走到出口、什么情况根本不该走、日志怎么交代。
            // "目标页正在显示算成功"那条语义在会话侧（见 [W] 段），这里不重复。

            var navCallsBefore = nav.NavigateCalls;

            var noPage = new ScadaEventHook { Event = ScadaEventType.Pressed };
            noPage.Actions.Add(new ScadaAction { Type = ScadaActionType.Navigate });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
            dispatcher.Dispatch(noPage, "按钮");
            Check("切画面①没选画面：Warn 一句「还没选画面」，且根本不去问导航出口（配置漏了不该惊动运行态）",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].EndsWith("切换画面：还没选画面，本条未执行")
                && nav.NavigateCalls == navCallsBefore,
                log.Lines.FirstOrDefault() ?? "无");

            nav.FailReason = "方案里没有叫「已删掉的画面」的画面";
            var gonePage = new ScadaEventHook { Event = ScadaEventType.Pressed };
            gonePage.Actions.Add(new ScadaAction { Type = ScadaActionType.Navigate, TargetPageName = "已删掉的画面" });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
            dispatcher.Dispatch(gonePage, "按钮");
            Check("切画面②目标画面找不到（改名/删了）：Warn 带动作原文 + 出口给的原因，本条未执行",
                log.Lines.Count == 1 && log.Lines[0].StartsWith("W ")
                && log.Lines[0].Contains("切换画面 → 已删掉的画面")
                && log.Lines[0].EndsWith("：方案里没有叫「已删掉的画面」的画面，本条未执行"),
                log.Lines.FirstOrDefault() ?? "无");

            nav.FailReason = null;
            var targetPageId = Guid.NewGuid();
            var goPage = new ScadaEventHook { Event = ScadaEventType.Pressed };
            goPage.Actions.Add(new ScadaAction
            {
                Type = ScadaActionType.Navigate,
                TargetPageId = targetPageId,
                TargetPageName = "参数页",
            });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
            dispatcher.Dispatch(goPage, "按钮");
            Check("切画面③切成功：Info 一行「切换画面 → 参数页」，Id 与名字原样交给出口（寻址口径归它一处说了算）",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 未登录 · 按钮 · 按下 → 切换画面 → 参数页"
                && nav.LastPageId == targetPageId && nav.LastPageName == "参数页",
                log.Lines.FirstOrDefault() ?? "无");

            // ---------------- ② 三种"什么都不该发生"的情况 ----------------

            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, new FakeValueSource(), nav);

            dispatcher.Dispatch(null, "启动按钮");
            dispatcher.Dispatch(new ScadaEventHook { Event = ScadaEventType.Pressed }, "启动按钮");
            Check("钩子为 null、动作表为空：一行都不留（「没配动作」由空集合表达，不是错误）",
                log.Lines.Count == 0, string.Join(" ⏎ ", log.Lines));

            var blank = new ScadaEventHook { Event = ScadaEventType.Loaded };
            blank.Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
            dispatcher.Dispatch(blank, "   ");
            Check("日志内容留空 → 文案回落成「对象 · 事件」：新建一条动作当场就能用，不必先逼用户填话",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 未登录 · 未命名对象 · 加载完成",
                string.Join(" ⏎ ", log.Lines));

            // 下面两组各换一份新账本：上一条案例的日志不许混进这一条的证词里
            var withNull = new ScadaEventHook { Event = ScadaEventType.Released };
            withNull.Actions.Add(null!);
            withNull.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "坏数据后面这条" });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
            dispatcher.Dispatch(withNull, "按钮");
            Check("动作串里混进 null（手工改坏的 .vms 反序列化会原样塞进来）：跳过它，别把整串废掉",
                log.Lines.Count == 1 && log.Lines[0] == "I [组态事件] 未登录 · 按钮 · 释放 → 坏数据后面这条",
                string.Join(" ⏎ ", log.Lines));

            var future = new ScadaEventHook { Event = ScadaEventType.Pressed };
            future.Actions.Add(new ScadaAction { Type = (ScadaActionType)99 });
            log = new LoggerStub();
            dispatcher = new ScadaActionDispatcher(log, values, nav);
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
                new ScadaActionDispatcher(faulty, values, nav).Dispatch(faulted, "急停按钮");
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
                && faulty.Lines[0] == "I [组态事件] 未登录 · 急停按钮 · 按下 → 第一条"
                && faulty.Lines[1].StartsWith("E ")
                && faulty.Lines[1].Contains("记录日志「第二条」")
                && faulty.Lines[1].Contains("执行失败：日志落地失败")
                && faulty.Lines[2] == "I [组态事件] 未登录 · 急停按钮 · 按下 → 第三条",
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
            // 这条钉的是 ScadaEventType 类注释里那句承诺：谁把还没接触发源的事件写进清单，
            // 用户配上的就是个永远不响的钩子。
            // 哪天补齐了触发源，改这条断言的白名单，别改描述符——顺序反了就会先长出哑配置。

            var premature = new List<string>();
            foreach (var descriptor in ElementRegistry.All)
            {
                foreach (var declared in descriptor.Events)
                {
                    if (declared != ScadaEventType.Pressed
                        && declared != ScadaEventType.Released
                        && declared != ScadaEventType.InputCompleted)
                        premature.Add($"{descriptor.TypeKey} → {declared}");
                }
            }

            Check("图元描述符只声明当前真能触发的事件（Pressed/Released/InputCompleted）：清单里不许有发不出来的事件",
                premature.Count == 0, string.Join(" | ", premature));

            // 反向一条：能触发的事件必须有人声明。少了它，上面那条只要把所有声明都删掉就"通过"了。
            var declaredTypes = ElementRegistry.All.SelectMany(d => d.Events).Distinct().ToList();
            Check("能触发的事件必须至少被一条描述符声明（输入完成时只开给可输入的图元，且正是 Hmi.IOField 那一条）",
                declaredTypes.Contains(ScadaEventType.Pressed) && declaredTypes.Contains(ScadaEventType.Released)
                && declaredTypes.Contains(ScadaEventType.InputCompleted)
                && ElementRegistry.Find("Hmi.IOField")!.Events.Count == 1
                && ElementRegistry.Find("Hmi.IOField")!.Events[0] == ScadaEventType.InputCompleted
                && ElementRegistry.Find("Hmi.Rectangle")!.Events.Count == 0,
                $"声明到的事件：{string.Join(" / ", declaredTypes)}");
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

            // S7 第二批的三个工艺符号也各来一个。它们与阀门同族（状态 → 颜色 + 几何随尺寸重算），
            // 但"上下分账"各不相同：泵给位号留底边、电机顶上还要再切一条给接线盒、管道按短边排箭头。
            // 三个都给运行态（绿）：深底上"绿 = 在转"一眼就能认，也正是这三个图元存在的理由。
            //
            // 尺寸刻意比默认值放大：管道与电机的可见部分都是<b>描边</b>而不是填充，缩到 0.4 倍后
            // 线宽会掉到两三个像素，采样点就踩在抗锯齿的边上，断言红绿全看运气。
            //
            // 放大有个讲究：<b>决定线宽的是短边</b>。管道第一版只把宽度翻倍（320×64），短边仍是 64，
            // 箭头线宽只有 6.1 设计像素 → 2.44 设备像素，采样点混进 4% 管身灰（距离正好 8，卡在阈值上，
            // 伪失败踩过一次）。所以宽高得一起给：640×128 → 线宽 5 设备像素，采样点离两侧边缘一个多像素。
            var pump = AddRenderElement(page, layer, "Hmi.Pump", 940, 420, 144, 144);
            pump.SetProperty("State", "Running");
            var motor = AddRenderElement(page, layer, "Hmi.Motor", 940, 600, 144, 160);
            motor.SetProperty("State", "Running");
            var pipe = AddRenderElement(page, layer, "Hmi.Pipe", 1120, 420, 640, 128);
            pipe.SetProperty("State", "Running");

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

            // ---------------- ②-3 S7 第二批：泵 / 电机 / 管道 ----------------
            //
            // 三个图元各有各的"采样点该怎么找"的讲究，共同的一条是：<b>可见部分要么是填充、
            // 要么是描边</b>，采样点必须落在真正有颜色的地方，而不是"大概在图元里"。

            // 泵：叶轮是泵壳的内接等边三角，重心正好落在泵壳圆心上 —— 采圆心必然采在叶轮上。
            // 圆心不能拿 Height/2 顶：底边四分之一让给了位号，得按控件那条账现算。
            double pumpInset = 1.5; // 半线宽(1/2) + 1 像素呼吸空间，与控件同一条账
            double pumpSymbolBottom = pump.Height * 0.75;
            double pumpRadius = Math.Min(pump.Width - 2 * pumpInset, pumpSymbolBottom - 2 * pumpInset) / 2;
            double pumpCx = pump.X + pump.Width / 2;
            double pumpCy = pump.Y + pumpInset + (pumpSymbolBottom - 2 * pumpInset) / 2;
            Check("泵：运行态叶轮被真的画上（绿，压着泵壳空腔灰与画面深底）",
                SameColor(Sample(frame, DesignToViewport(pumpCx, pumpCy)), "#FF34C759"),
                ColorName(Sample(frame, DesignToViewport(pumpCx, pumpCy))));
            Check("泵：泵壳空腔仍是空腔色（叶轮没把泵壳铺满，一眼看得出转的是叶轮）",
                SameColor(Sample(frame, DesignToViewport(pumpCx - pumpRadius * 0.8, pumpCy)), "#FF3A3A3A"),
                ColorName(Sample(frame, DesignToViewport(pumpCx - pumpRadius * 0.8, pumpCy))));

            // 电机：M 的可见部分是描边，采样点得落在<b>笔画中心线</b>上，而不是"大概在机身里"。
            // M 的第一段是机身左侧那根竖画（左下 → 左上），取它的中腰就是笔画正中心。
            double motorInset = 1.5;
            double motorRegionTop = motor.Height * 0.18;
            double motorRegionBottom = motor.Height * 0.75;
            double motorDiameter = Math.Min(motor.Width - 2 * motorInset, (motorRegionBottom - motorRegionTop) - 2 * motorInset);
            double motorRadius = motorDiameter / 2;
            double motorCx = motor.X + motor.Width / 2;
            double motorCy = motor.Y + (motorRegionTop + motorRegionBottom) / 2;
            var motorLetterSpot = DesignToViewport(motorCx - motorRadius * 0.46, motorCy);
            Check("电机：运行态 M 被真的画上（绿，压在机身灰上——深底上一眼认出是台在转的电机）",
                SameColor(Sample(frame, motorLetterSpot), "#FF34C759"), ColorName(Sample(frame, motorLetterSpot)));
            Check("电机：机身空腔仍是空腔色（M 只占中间一竖条，机身没被铺满）",
                SameColor(Sample(frame, DesignToViewport(motorCx - motorRadius * 0.8, motorCy)), "#FF3A3A3A"),
                ColorName(Sample(frame, DesignToViewport(motorCx - motorRadius * 0.8, motorCy))));

            // 管道：箭头也是描边。人字的两条斜边是"端头 → 尖"，采样点取上边那条的中心线中点，
            // 也就是 (箭头中心 x, 管中心 y − 半高/2)：笔画正中心，离两侧边缘各有一个多像素，
            // 不会被抗锯齿混色骗过去（管道第一版只把宽度放大、短边没放大，就栽在这里）。
            // 箭头中心 x 与控件的"等分 + 两端各留一份空档"同一条账，个数也照同一条公式现算。
            double pipeInset = 1.5;
            double pipeWidth = pipe.Width - 2 * pipeInset;
            double pipeSize = Math.Min(pipeWidth, pipe.Height - 2 * pipeInset);
            double pipeCy = pipe.Y + pipe.Height / 2;
            int pipeCount = Math.Clamp(
                (int)Math.Round(pipeWidth / (pipeSize * 1.8), MidpointRounding.AwayFromZero), 1, 6);
            double pipeArrow1Cx = pipe.X + pipeInset + pipeWidth / (pipeCount + 1);
            var pipeArrowSpot = DesignToViewport(pipeArrow1Cx, pipeCy - pipeSize * 0.26 / 2);
            Check("管道：运行态箭头被真的画上（绿，压在管身灰上——流向与运行态一个图元里全给）",
                SameColor(Sample(frame, pipeArrowSpot), "#FF34C759"), ColorName(Sample(frame, pipeArrowSpot)));
            Check("管道：箭头之间仍是管身色（箭头没把整根管子铺满，一眼数得出几个流向标记）",
                SameColor(Sample(frame, DesignToViewport(pipe.X + pipeInset + pipeWidth * 3 / 8, pipeCy)), "#FF3A3A3A"),
                ColorName(Sample(frame, DesignToViewport(pipe.X + pipeInset + pipeWidth * 3 / 8, pipeCy))));

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
        //  [Anim] 图元动画：值 → 外观 / 位置 / 可见性（手册 7.5.1 的四种动画）
        //
        //  三种落点分三层验，各管一段、互不遮丑：
        //  ① 领域层纯函数（ScadaAnimation 的三个 TryEvaluate*）——不碰 WPF，
        //     把换算规则逐条钉死（比例 / 夹取 / 单点阶跃 / 闭区间 / 坏档跳过）；
        //  ② 剪贴板快照——动画要跟着图元一起复制粘贴，档位表不许在往返里丢一段；
        //  ③ 运行态落点——ScadaRuntimeBinder 按 Type 分流到控件：
        //     外观落 Foreground/Fill、移动落 Canvas.Left/Top、可见性落 Visibility，
        //     带闪烁的档位再挂全画面统一节拍（相位 = 累计时长除以半周期的奇偶）。
        //
        //  与 [Z] 数据泵同一套假件（FakeValueHandle/FakeValueSource）：要验的是
        //  "值变了以后动画把控件改成什么样"，而"什么时候变"必须捏在断言手里。
        // ==================================================================
        private static void AnimationChecks()
        {
            Section("[Anim] 图元动画：值 → 外观 / 位置 / 可见性");

            Exception? failure = null;

            // 样板与 [Z] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunAnimationChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("动画断言全程未抛异常", false, failure.ToString());
        }

        private static void RunAnimationChecks()
        {
            // ---------------- ① 领域层：换算规则是纯函数，逐条钉死 ----------------

            var appearance = new ScadaAnimation { Type = ScadaAnimationType.Appearance };
            appearance.States.Add(new ScadaAnimationState { ValueLow = "0", ValueHigh = "50", Foreground = "#FFAA0000" });
            appearance.States.Add(new ScadaAnimationState { ValueLow = "50", ValueHigh = "100", Foreground = "#FF00AA00" });

            appearance.TryEvaluateAppearance(0, out var atLow, out _);
            appearance.TryEvaluateAppearance(50, out var atShared, out _);
            appearance.TryEvaluateAppearance(75, out var atHigh, out _);
            appearance.TryEvaluateAppearance(120, out var atMiss, out _);

            Check("外观变化：闭区间命中（0 与 100 两个端点都算命中，不是开区间）",
                atLow is not null && ReferenceEquals(atLow, appearance.States[0]),
                atLow?.Foreground ?? "(null)");
            Check("外观变化：档位边界重叠时先命中先用（50 归第一档，手册要求档位不许重复定义）",
                ReferenceEquals(atShared, appearance.States[0]),
                atShared?.Foreground ?? "(null)");
            Check("外观变化：落在第二档区间取第二档；一档都不命中返回 null（调用方据此恢复默认外观）",
                ReferenceEquals(atHigh, appearance.States[1]) && atMiss is null,
                $"{atHigh?.Foreground ?? "(null)"} / {atMiss?.Foreground ?? "(null)"}");

            // 半配好的档位：端点填不出数字、下限比上限大——两种都不许让整条动画失效
            appearance.States.Add(new ScadaAnimationState { ValueLow = "打了一半", ValueHigh = "300", Foreground = "#FF123456" });
            appearance.States.Add(new ScadaAnimationState { ValueLow = "400", ValueHigh = "300", Foreground = "#FF654321" });

            bool halfConfiguredOk = appearance.TryEvaluateAppearance(350, out var halfMatched, out _);

            Check("外观变化：半配好的档位被跳过而不是让整条动画失效，且求值恒返回 true（没命中是正常情形，不是错误）",
                halfConfiguredOk && halfMatched is null,
                halfMatched?.Foreground ?? "(null)");

            var move = new ScadaAnimation
            {
                Type = ScadaAnimationType.HorizontalMove,
                RangeLow = "0",
                RangeHigh = "100",
                EndX = 300,
                EndY = 999,
            };

            move.TryEvaluateTargetPosition(50, 100, out var mid, out _);
            move.TryEvaluateTargetPosition(200, 100, out var over, out _);
            move.TryEvaluateTargetPosition(-50, 100, out var under, out _);

            Check("水平移动：按比例换算（值 50 / 范围 0~100 / 起点 100 / 终点 300 → 200）",
                Math.Abs(mid - 200) < 1e-9, $"{mid}");
            Check("水平移动：值超出上限夹到结束位置、低于下限夹到起始位置（夹取是公式的自然结果，不是特判）",
                Math.Abs(over - 300) < 1e-9 && Math.Abs(under - 100) < 1e-9,
                $"{over} / {under}");

            var step = new ScadaAnimation
            {
                Type = ScadaAnimationType.HorizontalMove,
                RangeLow = "50",
                RangeHigh = "50",
                EndX = 300,
            };
            step.TryEvaluateTargetPosition(50, 100, out var stepOn, out _);
            step.TryEvaluateTargetPosition(49, 100, out var stepOff, out _);

            Check("水平移动：单点范围（两个端点相同）按阶跃处理，值 ≥ 端点跳结束位置、否则留起始位置",
                Math.Abs(stepOn - 300) < 1e-9 && Math.Abs(stepOff - 100) < 1e-9,
                $"{stepOn} / {stepOff}");

            var vertical = new ScadaAnimation
            {
                Type = ScadaAnimationType.VerticalMove,
                RangeLow = "0",
                RangeHigh = "10",
                EndY = 400,
                EndX = 999,
            };
            vertical.TryEvaluateTargetPosition(5, 40, out var verticalMid, out _);

            Check("垂直移动读的是结束位置 Y 而不是 X（值 5 / 范围 0~10 / 起点 40 / 终点 400 → 220）",
                Math.Abs(verticalMid - 220) < 1e-9, $"{verticalMid}");

            var badRangeMove = new ScadaAnimation
            {
                Type = ScadaAnimationType.HorizontalMove,
                RangeLow = "100",
                RangeHigh = "0",
                EndX = 300,
            };
            bool badRangeOk = badRangeMove.TryEvaluateTargetPosition(50, 100, out _, out var badRangeError);

            var textRangeMove = new ScadaAnimation
            {
                Type = ScadaAnimationType.HorizontalMove,
                RangeLow = "甲",
                RangeHigh = "乙",
                EndX = 300,
            };
            bool textRangeOk = textRangeMove.TryEvaluateTargetPosition(50, 100, out _, out var textRangeError);

            Check("移动：范围非法时求值失败并给出可直接展示的中文原因（下限比上限大 / 端点不是数字）",
                !badRangeOk && badRangeError != null && badRangeError.Contains("下限比上限大")
                && !textRangeOk && textRangeError != null && textRangeError.Contains("不是有效数字"),
                $"{badRangeError} ⏎ {textRangeError}");

            var visibility = new ScadaAnimation
            {
                Type = ScadaAnimationType.Visibility,
                RangeLow = "0",
                RangeHigh = "10",
                VisibleInRange = false,
            };
            visibility.TryEvaluateVisibility(5, out var inRange, out _);
            visibility.TryEvaluateVisibility(50, out var outOfRange, out _);

            Check("可见性：命中范围按「命中时的对象状态」给出显隐；不命中返回 null = 本条动画不接管（回落图层判定）",
                inRange == false && outOfRange is null,
                $"命中 {inRange?.ToString() ?? "(不接管)"} / 未命中 {outOfRange?.ToString() ?? "(不接管)"}");

            var badRangeVisibility = new ScadaAnimation
            {
                Type = ScadaAnimationType.Visibility,
                RangeLow = "10",
                RangeHigh = "0",
            };
            bool badVisibilityOk = badRangeVisibility.TryEvaluateVisibility(5, out _, out var badVisibilityError);

            Check("可见性：范围非法同样失败并说明原因（不许默默当成「不接管」，那会让该隐的没隐）",
                !badVisibilityOk && badVisibilityError != null && badVisibilityError.Contains("下限比上限大"),
                badVisibilityError ?? "(无)");

            // ---------------- ② 一个图元上每种动画至多一条 ----------------

            var manager = ElementRegistry.CreateElement("Hmi.Rectangle", 0, 0);
            var firstAnimation = manager.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            var secondAnimation = manager.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);

            Check("同一类型只留一条动画（GetOrAdd 复用已配好的那条，已配的范围与档位一并保住）",
                ReferenceEquals(firstAnimation, secondAnimation) && manager.Animations.Count == 1,
                $"{manager.Animations.Count} 条");
            Check("没配过的类型 FindAnimation 返回 null；删没配过的类型返回 false（不许把画面版本号刷高）",
                manager.FindAnimation(ScadaAnimationType.Visibility) is null
                && !manager.RemoveAnimation(ScadaAnimationType.Visibility)
                && manager.RemoveAnimation(ScadaAnimationType.HorizontalMove)
                && manager.Animations.Count == 0,
                $"{manager.Animations.Count} 条");
            Check("摘要文案随配置走（外观变化没档位时明说「还没有档位」，不是空串）",
                new ScadaAnimation { Type = ScadaAnimationType.Appearance }.Detail == "(还没有档位)",
                new ScadaAnimation { Type = ScadaAnimationType.Appearance }.Detail);

            // ---------------- ③ 剪贴板：动画跟着图元一起复制粘贴 ----------------

            ScadaClipboard.Clear();

            var clipDocument = new ScadaDocument();
            var clipPage = clipDocument.AddPage("剪贴源");
            var clipSource = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);
            clipSource.Name = "液位块";
            clipSource.Width = 50;
            clipSource.Height = 30;
            clipPage.Elements.Add(clipSource);

            var clipAppearance = clipSource.GetOrAddAnimation(ScadaAnimationType.Appearance);
            clipAppearance.BindVariable(Guid.NewGuid(), "液位");
            clipAppearance.States.Add(new ScadaAnimationState
            {
                ValueLow = "80",
                ValueHigh = "100",
                Foreground = "#FFFF0000",
                Fill = "#FFFFEE00",
                IsFlashing = true,
            });

            var clipMove = clipSource.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            clipMove.BindVariable(Guid.NewGuid(), "开度");
            clipMove.RangeLow = "0";
            clipMove.RangeHigh = "100";
            clipMove.EndX = 250;

            var payload = ScadaClipboard.Capture(clipPage, new[] { clipSource });

            Check("剪贴板负载版本升到 2（动画是 v2 新增的一段，老负载没有这一段要能照样读）",
                payload.Version == ScadaClipboardPayload.CurrentVersion && ScadaClipboardPayload.CurrentVersion == 2,
                $"v{payload.Version}");

            var snapshot = payload.Items[0];
            var snapshotAppearance = snapshot.Animations.FirstOrDefault(a => a.Type == ScadaAnimationType.Appearance);
            var snapshotMove = snapshot.Animations.FirstOrDefault(a => a.Type == ScadaAnimationType.HorizontalMove);

            Check("抓快照：两条动画连同驱动变量、范围、结束位置、档位表（含闪烁位与两个颜色）一起带走",
                snapshot.Animations.Count == 2
                && snapshotAppearance is { VariableName: "液位" }
                && snapshotAppearance.States.Count == 1
                && snapshotAppearance.States[0].ValueHigh == "100"
                && snapshotAppearance.States[0].IsFlashing
                && snapshotAppearance.States[0].Fill == "#FFFFEE00"
                && snapshotMove is { EndX: 250, VariableName: "开度" },
                $"{snapshot.Animations.Count} 条");

            var clipTarget = clipDocument.AddPage("剪贴目标");
            var made = ScadaClipboard.Materialize(clipTarget, payload, 10, 10, "粘贴 1 个图元");

            var pasted = made.Count == 1 ? made[0] : null;
            var pastedAppearance = pasted?.FindAnimation(ScadaAnimationType.Appearance);
            var pastedMove = pasted?.FindAnimation(ScadaAnimationType.HorizontalMove);

            Check("放快照：粘出来的图元带着同样的动画，档位表逐字段往返（少一段的现场表现是「粘完动画没了」）",
                pasted is not null
                && pasted.Animations.Count == 2
                && pastedAppearance is { VariableName: "液位" }
                && pastedAppearance.States.Count == 1
                && pastedAppearance.States[0].ValueLow == "80"
                && pastedAppearance.States[0].ValueHigh == "100"
                && pastedAppearance.States[0].Foreground == "#FFFF0000"
                && pastedAppearance.States[0].IsFlashing
                && pastedMove is { EndX: 250, VariableName: "开度" },
                $"{pasted?.Animations.Count ?? 0} 条");

            ScadaClipboard.Clear();

            // ---------------- ④ 运行态落点：数据泵按动画类型分流到控件 ----------------

            var doc = new ScadaDocument();
            var page = doc.AddPage("动画画面");
            var layer = page.Layers.FirstOrDefault() ?? page.AddLayer();

            var posVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "位置", DataType = typeof(double), Value = 50d };
            var vertVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "竖直", DataType = typeof(double), Value = 5d };
            var colorVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "液位", DataType = typeof(double), Value = 10d };
            var visVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "隐藏开关", DataType = typeof(double), Value = 5d };
            var badRangeVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "坏范围", DataType = typeof(double), Value = 50d };
            var textVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "文本值", DataType = typeof(string), Value = "不是数字" };
            var flashVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "报警液位", DataType = typeof(double), Value = 5d };
            var offVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "停用动画", DataType = typeof(double), Value = 50d };

            var valueSource = new FakeValueSource();
            foreach (var handle in new[] { posVar, vertVar, colorVar, visVar, badRangeVar, textVar, flashVar, offVar })
            {
                valueSource.ById[handle.VariableId] = handle;
                valueSource.ByName[handle.Name] = handle;
            }

            ScadaElement AddElement(string typeKey, string name, double x, double y, double width, double height)
            {
                var element = ElementRegistry.CreateElement(typeKey, x, y);
                element.Name = name;
                element.Width = width;
                element.Height = height;
                element.ZIndex = page.Elements.Count + 1;
                page.Elements.Add(element);
                page.TryAssignLayer(element, layer, out _);
                return element;
            }

            var moveEl = AddElement("Hmi.Rectangle", "水平移动", 40, 200, 80, 40);
            var moveAnim = moveEl.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            moveAnim.BindVariable(posVar.VariableId, posVar.Name);
            moveAnim.RangeLow = "0";
            moveAnim.RangeHigh = "100";
            moveAnim.EndX = 300;

            var vertEl = AddElement("Hmi.Rectangle", "垂直移动", 200, 40, 60, 40);
            var vertAnim = vertEl.GetOrAddAnimation(ScadaAnimationType.VerticalMove);
            vertAnim.BindVariable(vertVar.VariableId, vertVar.Name);
            vertAnim.RangeLow = "0";
            vertAnim.RangeHigh = "10";
            vertAnim.EndY = 400;

            var colorEl = AddElement("Hmi.Rectangle", "外观变化", 500, 40, 80, 40);
            var colorAnim = colorEl.GetOrAddAnimation(ScadaAnimationType.Appearance);
            colorAnim.BindVariable(colorVar.VariableId, colorVar.Name);
            colorAnim.States.Add(new ScadaAnimationState { ValueLow = "0", ValueHigh = "50", Foreground = "#FFFF0000", Fill = "#FFFFEE00" });
            colorAnim.States.Add(new ScadaAnimationState { ValueLow = "51", ValueHigh = "100", Foreground = "#FF00AA00" });

            var visEl = AddElement("Hmi.Text", "可见性", 600, 40, 100, 30);
            var visAnim = visEl.GetOrAddAnimation(ScadaAnimationType.Visibility);
            visAnim.BindVariable(visVar.VariableId, visVar.Name);
            visAnim.RangeLow = "0";
            visAnim.RangeHigh = "10";
            visAnim.VisibleInRange = false;

            var offEl = AddElement("Hmi.Text", "停用动画", 600, 100, 100, 30);
            var offAnim = offEl.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            offAnim.BindVariable(offVar.VariableId, offVar.Name);
            offAnim.RangeLow = "0";
            offAnim.RangeHigh = "100";
            offAnim.EndX = 120;
            offAnim.IsEnabled = false;

            var noVarEl = AddElement("Hmi.Text", "没选变量", 600, 140, 100, 30);
            var noVarAnim = noVarEl.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            noVarAnim.RangeLow = "0";
            noVarAnim.RangeHigh = "100";
            noVarAnim.EndX = 120;

            var missEl = AddElement("Hmi.Text", "变量没找到", 600, 180, 100, 30);
            var missAnim = missEl.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            missAnim.BindVariable(Guid.Empty, "查无此变量");
            missAnim.RangeLow = "0";
            missAnim.RangeHigh = "100";
            missAnim.EndX = 120;

            var badRangeEl = AddElement("Hmi.Text", "范围非法", 600, 220, 100, 30);
            var badRangeAnim = badRangeEl.GetOrAddAnimation(ScadaAnimationType.HorizontalMove);
            badRangeAnim.BindVariable(badRangeVar.VariableId, badRangeVar.Name);
            badRangeAnim.RangeLow = "100";
            badRangeAnim.RangeHigh = "0";
            badRangeAnim.EndX = 300;

            var badValueEl = AddElement("Hmi.Text", "值不是数字", 600, 260, 100, 30);
            var badValueAnim = badValueEl.GetOrAddAnimation(ScadaAnimationType.VerticalMove);
            badValueAnim.BindVariable(textVar.VariableId, textVar.Name);
            badValueAnim.RangeLow = "0";
            badValueAnim.RangeHigh = "100";
            badValueAnim.EndY = 300;

            var flashEl = AddElement("Hmi.Rectangle", "闪烁档位", 700, 40, 40, 40);
            var flashAnim = flashEl.GetOrAddAnimation(ScadaAnimationType.Appearance);
            flashAnim.BindVariable(flashVar.VariableId, flashVar.Name);
            flashAnim.States.Add(new ScadaAnimationState { ValueLow = "0", ValueHigh = "100", IsFlashing = true });

            var canvas = BuildBinderCanvas(page);
            var controls = canvas.EnumerateControls().ToList();

            ScadaElementBase ControlOf(ScadaElement element)
                => controls.First(c => ReferenceEquals(c.Element, element));

            Check("动画画面渲染出全部 10 个图元控件（建表发生在首帧之后，控件必须先到位）",
                controls.Count == 10, $"{controls.Count} 个");

            var reports = new List<(ScadaDiagnosticLevel Level, string Message)>();
            var binder = new ScadaRuntimeBinder(canvas, valueSource, page, (level, message) => reports.Add((level, message)));

            // 闪烁要一个运行上下文（相位来自全画面统一节拍）；报警引擎这一节用不到，只为把上下文凑齐。
            var engine = new ScadaAlarmEngine(doc, valueSource);
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var context = new ScadaRuntimeContext(engine, beat);

            binder.Start();
            binder.FlushNow(); // 没有消息循环：排进 Dispatcher 的那一帧不会自己跑，手动刷

            Check("建表：命中 7 条动画（移动 3 + 外观 2 + 可见性 1 + 值转不过去那条），未命中 1 条（变量没找到）",
                binder.BoundCount == 7 && binder.MissCount == 1,
                $"命中 {binder.BoundCount} / 未命中 {binder.MissCount}");
            Check("停用的动画与没选变量的动画连解析都不做（既不订阅也不打点，临时关掉一条看现象要的就是干净）",
                valueSource.ResolveCalls == 8, $"解析了 {valueSource.ResolveCalls} 次");
            Check("按变量订阅一次：8 条动画换来 7 个变量订阅（每个变量只被一条动画指着）",
                binder.SubscriptionCount == 7 && posVar.Subscribers == 1,
                $"订阅 {binder.SubscriptionCount} / 位置 {posVar.Subscribers}");

            // ---------------- 建表即刷一遍当前值 ----------------

            var moveControl = ControlOf(moveEl);
            var vertControl = ControlOf(vertEl);
            var colorControl = ControlOf(colorEl);
            var visControl = ControlOf(visEl);
            var flashControl = ControlOf(flashEl);

            Check("水平移动落 Canvas.Left，起点现读模型 X（值 50 / 范围 0~100 / 起点 40 / 终点 300 → 170）",
                Math.Abs(Canvas.GetLeft(moveControl) - 170) < 0.001,
                $"{Canvas.GetLeft(moveControl)}");
            Check("垂直移动落 Canvas.Top（值 5 / 范围 0~10 / 起点 40 / 终点 400 → 220）",
                Math.Abs(Canvas.GetTop(vertControl) - 220) < 0.001,
                $"{Canvas.GetTop(vertControl)}");
            Check("外观变化落前景与底色：值 10 命中第一档 → 前景红、底色黄",
                colorControl.Foreground is SolidColorBrush { Color: var redColor } && redColor == Color.FromRgb(0xFF, 0x00, 0x00)
                && colorControl.Fill is SolidColorBrush { Color: var yellowColor } && yellowColor == Color.FromRgb(0xFF, 0xEE, 0x00),
                $"{colorControl.Foreground} / {colorControl.Fill}");
            Check("可见性落 Visibility：值 5 命中 0~10 且「命中时隐藏」→ Collapsed",
                visControl.Visibility == Visibility.Collapsed,
                visControl.Visibility.ToString());

            // ---------------- 值变化 → 落点跟着变 ----------------

            posVar.Raise(200d);
            binder.FlushNow();
            Check("水平移动夹到结束位置（值 200 超出范围 0~100）",
                Math.Abs(Canvas.GetLeft(moveControl) - 300) < 0.001,
                $"{Canvas.GetLeft(moveControl)}");

            posVar.Raise(0d);
            binder.FlushNow();
            Check("回到起始位置：起点每次都现读模型 X（40），不是从上次落点再挪——反复刷新不会漂移",
                Math.Abs(Canvas.GetLeft(moveControl) - 40) < 0.001,
                $"{Canvas.GetLeft(moveControl)}");

            colorVar.Raise(60d);
            binder.FlushNow();
            Check("外观换档：值 60 命中第二档 → 前景变绿；这一档没配底色 → 底色回落到设计色，而不是留着上一档的黄",
                colorControl.Foreground is SolidColorBrush { Color: var greenColor } && greenColor == Color.FromRgb(0x00, 0xAA, 0x00)
                && colorControl.Fill is SolidColorBrush { Color: var restoredFill } && restoredFill == Color.FromRgb(0xFF, 0xFF, 0xFF),
                $"{colorControl.Foreground} / {colorControl.Fill}");

            colorVar.Raise(999d);
            binder.FlushNow();
            Check("一档都不命中 → 恢复设计外观（前景 #FF202020、底色 #FFFFFFFF），不是「保持上一档」",
                colorControl.Foreground is SolidColorBrush { Color: var designFore } && designFore == Color.FromRgb(0x20, 0x20, 0x20)
                && colorControl.Fill is SolidColorBrush { Color: var designFill } && designFill == Color.FromRgb(0xFF, 0xFF, 0xFF),
                $"{colorControl.Foreground} / {colorControl.Fill}");

            visVar.Raise(50d);
            binder.FlushNow();
            Check("可见性：值不命中范围 → 本条动画不接管，回落到图层判定（图层可见就显示）",
                visControl.Visibility == Visibility.Visible,
                visControl.Visibility.ToString());

            // ---------------- 失败可见：范围非法 / 值用不了 ----------------

            // 角标是按控件画的，三条问题落在三个控件上，故这里数得出 3 个：
            // 两条错误级（范围非法 / 值转不过去）+ 一条警告级（建表时变量没找到）。
            Check("坏动画各自打点：范围下限比上限大（红）+ 值不是数字（红），外加建表时那条「变量没找到」的黄标",
                canvas.Diagnostics.Count == 3, $"{canvas.Diagnostics.Count} 个角标");
            Check("诊断进日志且是错误级：动画配错了要能一眼看出是「范围」还是「值」的问题",
                reports.Count(r => r.Level == ScadaDiagnosticLevel.Error) == 2
                && reports.Any(r => r.Level == ScadaDiagnosticLevel.Error && r.Message.Contains("下限比上限大"))
                && reports.Any(r => r.Level == ScadaDiagnosticLevel.Error && r.Message.Contains("不是有效数字")),
                string.Join(" ⏎ ", reports.Select(r => $"{r.Level} {r.Message}")));

            var badValueControl = ControlOf(badValueEl);
            textVar.Raise(50d);
            binder.FlushNow();
            Check("值转得动了：位置落下去、角标只撤自己那一个（别处的提示不许被一起抹掉）",
                Math.Abs(Canvas.GetTop(badValueControl) - 280) < 0.001 && canvas.Diagnostics.Count == 2,
                $"Top={Canvas.GetTop(badValueControl)} / {canvas.Diagnostics.Count} 个角标");

            var offControl = ControlOf(offEl);
            offVar.Raise(99d);
            binder.FlushNow();
            Check("停用的动画即使变量变了也不动控件、也不打点（配置保留、运行跳过）",
                Math.Abs(Canvas.GetLeft(offControl) - offEl.X) < 0.001 && canvas.Diagnostics.Count == 2,
                $"Left={Canvas.GetLeft(offControl)} / {canvas.Diagnostics.Count} 个角标");

            // ---------------- ⑤ 闪烁：相位来自全画面统一节拍 ----------------

            flashControl.RuntimeContext = context;

            Check("配了闪烁档位的图元挂上节拍订阅；没配的图元一条都不挂（几十个不闪的图元陪着走回调是白烧 CPU）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 1,
                $"{SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} 个节拍订阅者");
            Check("闪烁落点 = 控件自身的 Opacity，开闪立刻亮（不等下一拍，否则配好到第一次 Tick 之间会有一截暗相）",
                Math.Abs(flashControl.Opacity - 1d) < 1e-9, $"{flashControl.Opacity}");

            beat.Start();
            PumpDispatcher(650);
            double darkPhase = flashControl.Opacity;

            PumpDispatcher(600);
            double litPhase = flashControl.Opacity;

            Check("相位由共享节拍的累计时长算出（半周期 500ms）：第一拍暗相 → Opacity 0，下一拍亮相 → 回到 1",
                Math.Abs(darkPhase - 0d) < 1e-9 && Math.Abs(litPhase - 1d) < 1e-9,
                $"暗相 {darkPhase} / 亮相 {litPhase}");

            // ---------------- ⑥ 摘表：回设计值、订阅归零 ----------------

            binder.Stop();

            Check("Stop 把动画订阅全退掉（不退就是泄漏：变量注册表是长命对象，它拿着整棵控件树）",
                !binder.IsRunning && binder.SubscriptionCount == 0
                && posVar.Subscribers == 0 && flashVar.Subscribers == 0,
                $"订阅 {binder.SubscriptionCount} / 位置 {posVar.Subscribers} / 报警液位 {flashVar.Subscribers}");
            Check("Stop 把计数与角标一起归零",
                binder.BoundCount == 0 && binder.MissCount == 0 && canvas.Diagnostics.Count == 0,
                $"命中 {binder.BoundCount} / 未命中 {binder.MissCount} / 角标 {canvas.Diagnostics.Count}");
            Check("Stop 后控件回设计值：移动回设计位置、外观回设计色、可见性回图层判定、闪烁的透明度还原",
                Math.Abs(Canvas.GetLeft(moveControl) - moveEl.X) < 0.001
                && Math.Abs(Canvas.GetTop(vertControl) - vertEl.Y) < 0.001
                && colorControl.Foreground is SolidColorBrush { Color: var stopFore } && stopFore == Color.FromRgb(0x20, 0x20, 0x20)
                && visControl.Visibility == Visibility.Visible
                && Math.Abs(flashControl.Opacity - 1d) < 1e-9,
                $"Left={Canvas.GetLeft(moveControl)} / Top={Canvas.GetTop(vertControl)} / 透明度={flashControl.Opacity}");

            flashControl.RuntimeContext = null;
            beat.Stop();

            Check("摘掉运行上下文后节拍订阅归零（上下文是长命对象，挂不退就是「上一轮攥着这一轮」的泄漏）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 0,
                $"{SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} 个节拍订阅者");
        }

        // ==================================================================
        //  [VEv] 变量事件引擎：变量值 → 边沿判定 → 动作串
        //
        //  手册 7.5.2 把"可组态对象 = 变量"的事件单列一类（更改数值 / 值为真 / 值为假 /
        //  上限 / 下限）。这一节钉住三件静态看不出来的事：
        //  ① 挂载只记基线、不发事件（否则开机时所有"值为真"的钩子都会跑一遍）；
        //  ② 五类边沿的判定条件（含"旧值判不了不算边沿"与"阈值 null = 不判"）；
        //  ③ 运行中改配置靠 Tick 自动重挂，且重挂不许产生重复订阅——
        //     重复订阅不报错，表现是"一次变化跑两遍动作"，现场极难归因。
        // ==================================================================
        private static void VariableEventChecks()
        {
            Section("[VEv] 变量事件引擎：变量值 → 边沿判定 → 动作串");

            Exception? failure = null;

            // 样板与 [Z] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
            var thread = new Thread(() =>
            {
                try { RunVariableEventChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("变量事件断言全程未抛异常", false, failure.ToString());
        }

        private static void RunVariableEventChecks()
        {
            var doc = new ScadaDocument();

            // 变量：真机上由 VM.Core 的注册表适配器提供，这里用假句柄把"什么时候变值"捏在手里
            var tempVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "温度", DataType = typeof(double), Value = 23.0 };
            var runVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "运行", DataType = typeof(bool), Value = false };
            var nullVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "未接上", DataType = typeof(object), Value = null };
            var plainVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "无门限", DataType = typeof(int), Value = 0 };
            var mixVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "混合", DataType = typeof(int), Value = 0 };
            var offVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "停用", DataType = typeof(int), Value = 0 };
            var legacyVar = new FakeValueHandle { VariableId = Guid.Empty, Name = "按名旧数据", DataType = typeof(int), Value = 0 };

            var source = new FakeValueSource();
            foreach (var handle in new[] { tempVar, runVar, nullVar, plainVar, mixVar, offVar })
                source.ById[handle.VariableId] = handle;

            // 旧数据只有名字：注册表里也只按名字挂着它，Id 那一路必须查不到
            source.ByName[legacyVar.Name] = legacyVar;

            ScadaVariableEvent Record(Guid variableId, string? variableName, params ScadaEventType[] events)
            {
                var record = doc.GetOrAddVariableEvent(variableId, variableName);

                foreach (var eventType in events)
                    record.AddEventHook(eventType).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });

                return record;
            }

            var recTemp = Record(tempVar.VariableId, tempVar.Name,
                ScadaEventType.ValueChanged, ScadaEventType.ValueOverUpperLimit, ScadaEventType.ValueUnderLowerLimit);
            recTemp.UpperLimit = 80;
            recTemp.LowerLimit = 5;

            var recRun = Record(runVar.VariableId, runVar.Name,
                ScadaEventType.ValueBecameTrue, ScadaEventType.ValueBecameFalse);

            var recNull = Record(nullVar.VariableId, nullVar.Name,
                ScadaEventType.ValueBecameTrue, ScadaEventType.ValueBecameFalse);

            // 配了钩子但一个门限都不配：用来证明"阈值 null = 不判"，而不是"上限当 0 用"
            var recPlain = Record(plainVar.VariableId, plainVar.Name,
                ScadaEventType.ValueChanged, ScadaEventType.ValueOverUpperLimit, ScadaEventType.ValueUnderLowerLimit);

            var recMix = Record(mixVar.VariableId, mixVar.Name,
                ScadaEventType.ValueChanged, ScadaEventType.ValueBecameTrue, ScadaEventType.ValueOverUpperLimit);
            recMix.UpperLimit = 0.5;

            var recLegacy = Record(Guid.Empty, legacyVar.Name, ScadaEventType.ValueChanged);

            var recOff = Record(offVar.VariableId, offVar.Name, ScadaEventType.ValueChanged);
            recOff.IsEnabled = false;

            var recMissing = Record(Guid.NewGuid(), "查无此变量", ScadaEventType.ValueChanged);

            var engine = new ScadaVariableEventEngine(doc, source);

            // 引擎承诺"事件在锁外抛"，所以这里可以同步收；真机上这条回调可能在变量轮询线程上
            var raised = new List<(string Variable, ScadaEventType Event)>();
            engine.VariableEventRaised += (record, hook) => raised.Add((record.VariableName ?? "<无名>", hook.Event));

            // ---------------- ① 挂载：只记基线、只订阅该订阅的 ----------------

            engine.Attach();

            Check("挂载不触发任何事件：挂载时读到的第一个值只是基线、不是「变化」（否则一开机所有「值为真」的钩子都会跑一遍）",
                engine.IsAttached && engine.RaisedCount == 0 && raised.Count == 0,
                $"已挂载={engine.IsAttached} / 命中 {engine.RaisedCount} / 事件 {raised.Count} 条");

            Check("槽数 = 记录条数（8 条），订阅数 = 6 个（停用的、变量解析不到的都不订）",
                engine.SlotCount == 8 && engine.SubscribedCount == 6,
                $"槽 {engine.SlotCount} / 订阅 {engine.SubscribedCount}");

            Check("停用的记录连变量都不解析、也不订阅（配置保留、运行完全跳过）",
                source.ResolveCalls == 7 && offVar.Subscribers == 0,
                $"解析 {source.ResolveCalls} 次 / 停用 {offVar.Subscribers} 个订阅");

            Check("该订的都订上了，且一个变量只订一份（同一个变量被多条记录指着也不会订两次）",
                tempVar.Subscribers == 1 && runVar.Subscribers == 1 && nullVar.Subscribers == 1
                && plainVar.Subscribers == 1 && mixVar.Subscribers == 1 && legacyVar.Subscribers == 1,
                $"温度 {tempVar.Subscribers} / 运行 {runVar.Subscribers} / 未接上 {nullVar.Subscribers} / 按名旧数据 {legacyVar.Subscribers}");

            Check("旧数据（Id 为空）按名字兜底订上了：Id 落地之前存下的方案照样跑得起来",
                recLegacy.VariableId == Guid.Empty && recLegacy.IsLegacyByName,
                $"Id={recLegacy.VariableId}");

            Check("每条记录上的钩子就是配上去的那些（IScadaEventHost 一份实现服务图元 / 画面 / 变量三种宿主）",
                recRun.EventHooks.Count == 2 && recNull.EventHooks.Count == 2 && recPlain.EventHooks.Count == 3
                && recMissing.EventHooks.Count == 1 && recOff.EventHooks.Count == 1,
                $"运行 {recRun.EventHooks.Count} / 未接上 {recNull.EventHooks.Count} / 无门限 {recPlain.EventHooks.Count} / 查无此变量 {recMissing.EventHooks.Count}");

            Check("条件的人话描述：配了几个门限就说几个，一个都没配是空串（不是「上下限都是 0」）",
                recTemp.ConditionText == "超上限 80 / 低于下限 5" && recPlain.ConditionText.Length == 0,
                $"「{recTemp.ConditionText}」/「{recPlain.ConditionText}」");

            // ---------------- ② 更改数值：真变了才发，换个数值壳不算变 ----------------

            int mark = raised.Count;
            tempVar.Raise(30);
            Check("值真的变了 → 发「更改数值」",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueChanged,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            tempVar.Raise(30.0); // 同一个数、换了个 CLR 类型（PLC 重连后类型可能变）
            Check("同一个数换了类型壳（int 30 → double 30.0）不算变化：Equals(30, 30.0) 是 false，只按数比才不会误触发",
                raised.Count == mark, $"{raised.Count - mark} 条");

            // ---------------- ③ 上/下限：边沿而不是电平 ----------------

            mark = raised.Count;
            tempVar.Raise(85);
            Check("越过上限 → 「更改数值」+「上限」两条（阈值 80 生效）",
                raised.Count - mark == 2
                && raised[mark].Event == ScadaEventType.ValueChanged
                && raised[mark + 1].Event == ScadaEventType.ValueOverUpperLimit,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            tempVar.Raise(90);
            Check("已经在上限之上再涨：只发「更改数值」，不再重复发「上限」（边沿，不是电平）",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueChanged,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            tempVar.Raise(3);
            Check("从上限之上直接掉到下限之下 → 「更改数值」+「下限」两条（掉下来的路上不补发「上限」）",
                raised.Count - mark == 2
                && raised[mark].Event == ScadaEventType.ValueChanged
                && raised[mark + 1].Event == ScadaEventType.ValueUnderLowerLimit,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            tempVar.Raise(1);
            Check("已经在下限之下再降：只发「更改数值」",
                raised.Count - mark == 1, string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            // ---------------- ④ 阈值 null = 不判 ----------------

            mark = raised.Count;
            plainVar.Raise(9999);
            Check("没配门限的记录：值冲到 9999 也只发「更改数值」（null 是「不判」，不是「上限为 0」）",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueChanged,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            plainVar.Raise(-9999);
            Check("往下冲同理：没配下限就不发「下限」",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueChanged,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            // ---------------- ⑤ BOOL 的两条边沿 ----------------

            mark = raised.Count;
            runVar.Raise(true);
            Check("BOOL 由假变真 → 「值为真」（旧值 false 是确知的，才敢算边沿）",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueBecameTrue,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            mark = raised.Count;
            runVar.Raise(true);
            Check("还是真：一条都不发（值没变，BOOL 上也没配「更改数值」钩子）",
                raised.Count == mark, $"{raised.Count - mark} 条");

            mark = raised.Count;
            runVar.Raise(false);
            Check("BOOL 由真变假 → 「值为假」",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueBecameFalse,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            // ---------------- ⑥ 旧值判不了不算边沿 ----------------

            mark = raised.Count;
            nullVar.Raise(true);
            Check("旧值是 null（变量刚接上、值还没来）：不算「由假变真」——宁可漏一条说不清的触发，也不刷一堆假动作",
                raised.Count == mark, $"{raised.Count - mark} 条");

            mark = raised.Count;
            nullVar.Raise(false);
            Check("旧值从这一刻起能判了：由真变假照常发「值为假」（上面那条只是「基线不算边沿」，不是「永远不发」）",
                raised.Count - mark == 1 && raised[mark].Event == ScadaEventType.ValueBecameFalse,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            // ---------------- ⑦ 一次变化同时命中多条：顺序确定 ----------------

            mark = raised.Count;
            mixVar.Raise(1); // 0 → 1：既是「更改数值」、又「由假变真」、又「越过上限 0.5」
            Check("一次变化同时命中三条：按事件取值序发（更改数值 → 值为真 → 上限），动作执行顺序因此是确定的",
                raised.Count - mark == 3
                && raised[mark].Event == ScadaEventType.ValueChanged
                && raised[mark + 1].Event == ScadaEventType.ValueBecameTrue
                && raised[mark + 2].Event == ScadaEventType.ValueOverUpperLimit,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            Check("命中的事件带的是「哪条变量事件记录」：订阅方要拿它把变量名写进日志（分发器不认识模型、也不该认识）",
                raised[mark].Variable == mixVar.Name, raised[mark].Variable);

            // ---------------- ⑧ 停用的槽完全空转 ----------------

            mark = raised.Count;
            offVar.Raise(7);
            Check("停用的记录：变量变了也不发（配置保留、运行跳过）",
                raised.Count == mark, $"{raised.Count - mark} 条");

            Check("累计命中数与实际广播条数逐条对得上（自检面不虚报）",
                engine.RaisedCount == raised.Count, $"计数 {engine.RaisedCount} / 实际 {raised.Count}");

            // ---------------- ⑨ 运行中改配置：Tick 自动重挂 ----------------

            var lateVar = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "后加的", DataType = typeof(int), Value = 0 };
            source.ById[lateVar.VariableId] = lateVar;

            Record(lateVar.VariableId, lateVar.Name, ScadaEventType.ValueChanged);

            Check("新增一条记录：引擎当场就知道（它盯住了 VariableEvents 集合），但还没重挂——槽数与订阅数先不动",
                engine.SlotCount == 8 && lateVar.Subscribers == 0,
                $"槽 {engine.SlotCount} / 后加的 {lateVar.Subscribers}");

            engine.Tick();

            Check("一拍之后自动重挂：新记录进槽、变量订上了（用户改完配置不必重启运行）",
                engine.SlotCount == 9 && engine.SubscribedCount == 7 && lateVar.Subscribers == 1,
                $"槽 {engine.SlotCount} / 订阅 {engine.SubscribedCount} / 后加的 {lateVar.Subscribers}");

            mark = raised.Count;
            lateVar.Raise(5);
            Check("新加的记录立刻生效", raised.Count - mark == 1, $"{raised.Count - mark} 条");

            // 改阈值同样置脏：重挂之后不许出现"同一个变量订了两份"（表现是一次变化跑两遍动作）
            recTemp.UpperLimit = 100;
            engine.Tick();

            Check("改阈值触发重挂，且不重复订阅：同一变量的订阅者始终只有一个",
                engine.SlotCount == 9 && tempVar.Subscribers == 1,
                $"槽 {engine.SlotCount} / 温度 {tempVar.Subscribers}");

            mark = raised.Count;
            tempVar.Raise(50);
            Check("重挂后事件只发一次（订了两份的话这里会是 2 条）",
                raised.Count - mark == 1, $"{raised.Count - mark} 条");

            mark = raised.Count;
            tempVar.Raise(150);
            Check("新阈值 100 立刻生效：50 → 150 越过新上限发「上限」（旧阈值 80 已经不管用了）",
                raised.Count - mark == 2 && raised[mark + 1].Event == ScadaEventType.ValueOverUpperLimit,
                string.Join("|", raised.Skip(mark).Select(r => r.Event.ToString())));

            // 删记录：必须把变量订阅一起退掉（退不掉 = 泄漏 + 已删配置还在跑动作）
            Check("删掉一条记录：TryRemoveVariableEvent 报成功",
                doc.TryRemoveVariableEvent(doc.FindVariableEvent(lateVar.VariableId), out _), string.Empty);

            engine.Tick();

            Check("一拍之后重挂：槽数回落、被删记录的变量订阅退干净（不退就是「删了还在跑」）",
                engine.SlotCount == 8 && engine.SubscribedCount == 6 && lateVar.Subscribers == 0,
                $"槽 {engine.SlotCount} / 订阅 {engine.SubscribedCount} / 后加的 {lateVar.Subscribers}");

            mark = raised.Count;
            lateVar.Raise(9);
            Check("被删记录的变量再变也不发", raised.Count == mark, $"{raised.Count - mark} 条");

            // ---------------- ⑩ 摘表：订阅全退、槽清空，计数不清零 ----------------

            var totalRaised = engine.RaisedCount;

            engine.Detach();

            Check("Detach 把订阅全退掉（不退就是泄漏：变量注册表比窗口活得久得多，它拿着整棵控件树）",
                !engine.IsAttached && engine.SlotCount == 0 && engine.SubscribedCount == 0
                && tempVar.Subscribers == 0 && runVar.Subscribers == 0 && nullVar.Subscribers == 0
                && plainVar.Subscribers == 0 && mixVar.Subscribers == 0 && legacyVar.Subscribers == 0
                && offVar.Subscribers == 0 && lateVar.Subscribers == 0,
                $"温度 {tempVar.Subscribers} / 混合 {mixVar.Subscribers} / 按名旧数据 {legacyVar.Subscribers}");

            Check("Detach 不清累计计数（它是「这一轮跑过几次」的证据，清掉就没法回答「刚才到底响没响」）",
                engine.RaisedCount == totalRaised, $"{engine.RaisedCount}");

            engine.Detach();
            engine.Tick();
            Check("重复 Detach 与摘表后的 Tick 都不出事（关窗口、切方案、重新挂载三条路都会走到这里）",
                !engine.IsAttached && engine.SlotCount == 0, $"槽 {engine.SlotCount}");

            mark = raised.Count;
            tempVar.Raise(999);
            Check("摘表后变量再变：一条事件都不发（迟到的值事件不该落到已经收场的引擎上）",
                raised.Count == mark, $"{raised.Count - mark} 条");

            // ---------------- ⑪ 重新挂载：幂等、基线重记 ----------------

            engine.Attach();
            engine.Attach(); // 宿主的两条生命周期路径都可能重入（窗口反复开关）

            Check("重复 Attach 是空操作：槽数不翻倍、变量订阅也不翻倍",
                engine.SlotCount == 8 && engine.SubscribedCount == 6 && tempVar.Subscribers == 1,
                $"槽 {engine.SlotCount} / 订阅 {engine.SubscribedCount} / 温度 {tempVar.Subscribers}");

            mark = raised.Count;
            Check("重新挂载只记基线、不补发历史：这一轮的「值为真」不会被上一轮遗留的值顶出来",
                raised.Count == mark && engine.RaisedCount == totalRaised,
                $"事件 {raised.Count - mark} 条 / 累计 {engine.RaisedCount}");

            engine.Detach();
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

        // ==================================================================
        //  [AC] 组态「选择画面」弹窗：当前方案画面 → 可见清单
        //
        //  与 [AA] 同构，只是清单来源不同（画面来自当前方案文档，变量来自工程变量表）。
        //  这里必须钉住三件事：
        //  ① 首次打开清单就可见（ApplyFilter 不能被 setter 短路——[AA] 踩过的坑同源）；
        //  ② 「起点」徽标只落在 ResolveStartupPage 选中的那一页（"运行先看到哪一页"最容易记混）；
        //  ③ 确认回传的是 (Id, 名字)，Id 拿不到就什么都不回传（不许退化成"按名字猜一个"）。
        // ==================================================================
        private static void PagePickerDialogChecks()
        {
            Section("[AC] 组态画面选择弹窗：当前方案画面 → 可见清单");

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            workspace.SwitchSolution(solution);

            var document = solution.Scada;
            var pageA = document.AddPage("A 主页");
            var pageB = document.AddPage("B 列表");
            var pageC = document.AddPage("C 详情");
            document.SetStartupPage(pageB);

            // 说明列要有人眼能核对的内容，否则渲染证据图上那一列是空的，看不出"列有没有接上数据"。
            // 措辞刻意避开关键字"列表"，免得影响下面那条过滤断言。
            pageA.Description = "开机默认页，总览看板";
            pageB.Description = "产线运行总览";
            pageC.Description = "参数与实时数据";

            var vm = new ScadaPagePickerVM(workspace);
            vm.OnDialogOpened(new DialogParameters());

            Check("打开弹窗后清单可见（全量投影进 Pages，不靠 setter 副作用）",
                vm.Pages.Count == 3 && vm.HasPages,
                $"Pages={vm.Pages.Count} / EmptyHint=「{vm.EmptyHint}」");
            Check("有画面时不显示空态提示",
                string.IsNullOrEmpty(vm.EmptyHint),
                $"EmptyHint=「{vm.EmptyHint}」");
            Check("没配过画面时不预选（免得随手一个回车就把动作切到清单第一页）",
                vm.SelectedPage == null && !vm.ConfirmCommand.CanExecute(),
                $"选中=「{vm.SelectedPage?.Name}」");

            Check("「起点」徽标只落在 ResolveStartupPage 选中的那一页（其余行为空串）",
                vm.Pages.Single(r => r.IsStartup).PageId == pageB.PageId
                && vm.Pages.Count(r => r.StartupBadge.Length > 0) == 1,
                string.Join("、", vm.Pages.Select(r => $"{r.Name}{(r.IsStartup ? "[起点]" : "")}")));

            Check("行上的尺寸/图元数来自画面模型本身（切过去一片空白的画面最容易被当成没生效）",
                vm.Pages[0].SizeText == $"{pageA.Width:0} × {pageA.Height:0}"
                && vm.Pages[0].ElementCountText == pageA.Elements.Count.ToString(),
                $"{vm.Pages[0].SizeText} / {vm.Pages[0].ElementCountText}");

            // 第二次走 setter：这次 _keyword 真的从空变"列表"，过滤必须生效
            vm.Keyword = "列表";
            Check("关键字过滤生效（名 / 尺寸 / 说明任一命中）",
                vm.Pages.Count == 1 && vm.Pages[0].Name == "B 列表",
                $"命中 {vm.Pages.Count} 条");

            // 再次打开（面板上已经配过画面）：按 Id 预选回那一页
            var reopen = new ScadaPagePickerVM(workspace);
            reopen.OnDialogOpened(new DialogParameters
            {
                { ScadaPagePicker.CurrentIdKey, pageC.PageId },
                { ScadaPagePicker.CurrentNameKey, "C 详情" },
            });
            Check("已配过画面时按 Id 预选回那一页（确认键随即可用）",
                reopen.SelectedPage?.PageId == pageC.PageId && reopen.ConfirmCommand.CanExecute(),
                $"选中=「{reopen.SelectedPage?.Name}」");

            // 名字兜底：Id 找不到（旧数据）时按名字找，大小写不敏感
            var byName = new ScadaPagePickerVM(workspace);
            byName.OnDialogOpened(new DialogParameters
            {
                { ScadaPagePicker.CurrentIdKey, Guid.Empty },
                { ScadaPagePicker.CurrentNameKey, "c 详情" },
            });
            Check("Id 缺失时按名字兜底预选，且大小写不敏感（旧数据只有名字）",
                byName.SelectedPage?.PageId == pageC.PageId,
                $"选中=「{byName.SelectedPage?.Name}」");

            // 确认回传：RequestClose 收到 OK + (Id, 名字)
            IDialogResult? closed = null;
            var confirmVm = new ScadaPagePickerVM(workspace);
            confirmVm.RequestClose = MakeDialogCloseListener(r => closed = r);
            confirmVm.OnDialogOpened(new DialogParameters
            {
                { ScadaPagePicker.CurrentIdKey, pageB.PageId },
                { ScadaPagePicker.CurrentNameKey, "B 列表" },
            });
            confirmVm.ConfirmCommand.Execute();

            var closedId = Guid.Empty;
            string? closedName = null;
            if (closed != null)
            {
                closed.Parameters.TryGetValue<Guid>(ScadaPagePicker.PickedIdKey, out closedId);
                closed.Parameters.TryGetValue<string>(ScadaPagePicker.PickedNameKey, out closedName);
            }

            Check("确认回传 (画面 Id, 画面名)：Id 是权威锚点，改名不会让这条动作断链",
                closed != null && closed.Result == ButtonResult.OK
                && closedId == pageB.PageId && closedName == "B 列表",
                closed == null ? "没有回调" : $"{closed.Result} / {closedName ?? "null"}");

            // 空方案是另一档：这才是"真的没画面"，必须仍然给出空态提示
            var emptyWorkspace = new WorkspaceContext();
            var emptySolution = new SolutionModel();
            emptySolution.Flows.Clear();
            emptyWorkspace.SwitchSolution(emptySolution);
            var emptyVm = new ScadaPagePickerVM(emptyWorkspace);
            emptyVm.OnDialogOpened(new DialogParameters());
            Check("方案里真的没有画面时才提示「还没有画面」",
                emptyVm.Pages.Count == 0 && !emptyVm.HasPages && !string.IsNullOrEmpty(emptyVm.EmptyHint),
                $"EmptyHint=「{emptyVm.EmptyHint}」");

            // ---------------- ⑫ 弹窗的"长相"：真渲染一遍，钉住列渲染器 ----------------
            //
            // 与 [AA] 的 ⑫ 同源（那边踩过"行容器被换成裸 ContentPresenter，清单退化成类型全名"的坑）。
            // 这里另外多钉两件画面选择器独有的事：
            //   ① 画面名列是 Grid(*/Auto) 两列，"起点"徽标只该占右边那一列，不该把画面名挤没；
            //   ② 四列之和塞得进内容区——横向滚动条已关，超宽就是最后一列被硬裁。
            RunPagePickerRenderChecks(reopen);
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

        /// <summary>⑫ 节的线程壳（画面选择器版）：与 <see cref="RunPickerRenderChecks"/> 同构。</summary>
        private static void RunPagePickerRenderChecks(ScadaPagePickerVM reopen)
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunPagePickerRenderChecksCore(reopen); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("画面选择弹窗渲染断言全程未抛异常（STA 线程内造 View 不该炸）", false, failure.ToString());
        }

        private static void RunPagePickerRenderChecksCore(ScadaPagePickerVM reopen)
        {
            ViewModelLocationProvider.Register<ScadaPagePickerView>(() => reopen);

            var view = new ScadaPagePickerView { DataContext = reopen };
            view.Measure(new Size(760, 520));
            view.Arrange(new Rect(0, 0, 760, 520));
            view.UpdateLayout();

            var rowPresenters = CollectVisual<GridViewRowPresenter>(view, _ => true);
            Check("每一行都挂着 GridViewRowPresenter（四列的摆位全靠它；退化成 ContentPresenter 就是一行行类型全名）",
                rowPresenters.Count == reopen.Pages.Count && rowPresenters.Count > 0,
                $"列渲染器 {rowPresenters.Count} 个 / 数据 {reopen.Pages.Count} 条");

            // 列头次序按"画在哪"判，不按视觉树遍历次序（GridViewHeaderRowPresenter 挂子元素的方向与列声明相反）。
            // 末尾 Content 为空的列头是 GridView 的"填缝头"，不参与比对。
            var headers = CollectVisual<GridViewColumnHeader>(view, _ => true)
                .Where(h => h.Content != null)
                .OrderBy(h => h.TranslatePoint(new Point(0, 0), view).X)
                .Select(h => h.Content!.ToString() ?? string.Empty).ToList();
            Check("四列的列头都排出来了（画面名/尺寸/图元数/说明）",
                string.Join("/", headers) == "画面名/尺寸/图元数/说明",
                string.Join("/", headers));

            // 列头与数据行由两套 presenter 摆：谁多吃了左边距谁就整体偏，屏幕上就是"表头压在数据上"。
            // 画面名列的单元格里是个 Grid（文字 + 徽标），所以取"最靠左的那个 TextBlock"来对位。
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
                    .Where(t => !string.IsNullOrEmpty(t.Text))
                    .OrderBy(t => t.TranslatePoint(new Point(0, 0), view).X)
                    .Select(t => t.TranslatePoint(new Point(0, 0), view).X).ToList();
            Check("列头文字与第一行单元格文字逐列左对齐（表头压在数据上是最显眼的排版事故）",
                headerTextXs.Count == 4 && rowTextXs.Count >= 4
                && headerTextXs.Zip(rowTextXs).All(p => Math.Abs(p.First - p.Second) < 0.6),
                $"列头 {string.Join("/", headerTextXs.Select(x => x.ToString("0.#")))} "
                + $"vs 行 {string.Join("/", rowTextXs.Select(x => x.ToString("0.#")))}");

            var texts = CollectVisual<TextBlock>(view, _ => true)
                .Select(t => t.Text ?? string.Empty).ToList();
            Check("整棵树里没有任何一处渲染出类型全名（行模板退化时屏幕上就是这行字）",
                !texts.Any(t => t.Contains("ScadaPageRow", StringComparison.Ordinal)),
                texts.FirstOrDefault(t => t.Contains("ScadaPageRow", StringComparison.Ordinal)) ?? "（没有）");

            Check("四列各自接上了数据：画面名 / 尺寸 / 图元数 / 说明（说明列空着就说明列没接上）",
                texts.Contains("A 主页") && texts.Contains("B 列表") && texts.Contains("C 详情")
                && texts.Contains($"{reopen.Pages[0].SizeText}")
                && texts.Contains("开机默认页，总览看板")
                && texts.Contains("产线运行总览") && texts.Contains("参数与实时数据"),
                string.Join(" | ", texts.Where(t => t.Length > 0)));

            // 「起点」徽标：数据上只有一行 IsStartup，画面上也只该出现一个"起点"字样。
            // 这一条同时钉住了 StartupBadgeStyle 的收起触发（DataTrigger 判空串）没写反。
            var badgeTexts = CollectVisual<TextBlock>(view, t => t.Text == "起点").ToList();
            Check("「起点」徽标在画面上只出现一次，且落在启动画面（B 列表）那一行",
                badgeTexts.Count == 1,
                $"{badgeTexts.Count} 个「起点」徽标");

            // 列宽之和必须塞得进内容区：横向滚动条已关，列一旦超宽就是"最后一列被切掉"。
            // 预算里要扣掉行两侧各 10px（行容器 Padding 4 + GridViewRowPresenter 给单元格内置的 6）——
            // 只减滚动条会算出个假余量（[AA] 真机上"类型/当前值/说明"三列被硬裁就是漏算了这一段）。
            const double rowHorizontalInset = 20;
            var list = FindVisual<ListView>(view, _ => true);
            var grid = list?.View as GridView;
            double columnsWidth = grid?.Columns.Sum(c => c.ActualWidth) ?? 0;
            Check("四列宽度之和塞得进内容区（扣掉竖滚动条与行左右留白后仍有余量，说明最后一列不会被硬裁）",
                list != null && columnsWidth > 0
                && columnsWidth + SystemParameters.VerticalScrollBarWidth + rowHorizontalInset <= list.ActualWidth,
                $"列宽和 {columnsWidth:0.#}px + 滚动条 {SystemParameters.VerticalScrollBarWidth}px "
                + $"+ 行留白 {rowHorizontalInset}px vs 内容区 {list?.ActualWidth ?? 0:0.#}px");

            // 列宽是死的、画面名与说明是活的：单元格正文必须自带省略号修剪。
            // 徽标文字（"起点"）与收起后的空串不走 PageCellTextStyle，不参与这条比对。
            var cellTexts = rowPresenters.SelectMany(p => CollectVisual<TextBlock>(p, _ => true))
                .Where(t => !string.IsNullOrEmpty(t.Text) && t.Text != "起点")
                .ToList();
            Check("四个数据单元格正文都带省略号修剪 + 悬停全值提示（超长画面名不再被硬裁）",
                cellTexts.Count >= reopen.Pages.Count * 4
                && cellTexts.All(t => t.TextTrimming != TextTrimming.None)
                && cellTexts.All(t => t.ToolTip != null),
                $"{cellTexts.Count} 个单元格正文（期望 ≥ {reopen.Pages.Count * 4}），"
                + $"未修剪 {cellTexts.Count(t => t.TextTrimming == TextTrimming.None)} 个，"
                + $"无提示 {cellTexts.Count(t => t.ToolTip == null)} 个");

            Check("横向滚动条关掉（这种清单里它只会帮倒忙：列被推出视野，用户还得左右拖）",
                list != null
                && ScrollViewer.GetHorizontalScrollBarVisibility(list) == ScrollBarVisibility.Disabled, "");

            var pagePickerShot = RenderToBitmap(view, 760, 520);
            Check("弹窗整体渲染成图：列头底色 #F5F7FA 与选中行底色 #ECF5FF 都真的刷在画面上",
                HasColorIn(pagePickerShot, new Rect(0, 0, 760, 520), "#F5F7FA")
                && HasColorIn(pagePickerShot, new Rect(0, 0, 760, 520), "#ECF5FF"), "");

            string pagePickerShotDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(pagePickerShotDir);
            string pagePickerPng = Path.Combine(pagePickerShotDir, "scada-page-picker.png");
            SavePng(pagePickerShot, pagePickerPng);
            Console.WriteLine($"        （渲染证据：{pagePickerPng}）");
        }

        // ==================================================================
        //  [VEd] 变量事件弹窗：左清单（变量 / 已失效）+ 右详情（阈值 + 5 类事件）
        //
        //  这一节钉的是 [VEv] 那个引擎"配置从哪来"的另一半：引擎只认记录，
        //  记录得有人在界面上配出来。手册 7.5.2 把"可组态对象 = 变量"单列一类，
        //  本弹窗就是那条口径的落点。三件静态读代码看不出来的事：
        //  ① 未配置的变量必须"显式点新建"才建记录（选中即建会让方案凭空多一条空配置、
        //     版本号白脏一次，而"我只是点了一下看看"在界面上是看不出来的）；
        //  ② 阈值框收字符串、解析不了要"回灌原值 + 说清原因"，不能把看不懂的输入当 0 写进模型；
        //  ③ 左栏那个状态字（未配置 / 已配置 / 已停用）要跟着整条监视开关实时变——
        //     不订阅记录属性变更的话，用户停用之后左栏还写着"已配置"，他会以为没点着。
        // ==================================================================
        private static void VariableEventDialogChecks()
        {
            Section("[VEd] 变量事件弹窗：左清单（变量 / 已失效）+ 右详情（阈值 + 5 类事件）");

            Exception? failure = null;

            // 样板与 [VEv] 一致：单开 STA 线程。本弹窗用 CollectionViewSource 投影分组，
            // 那是 DispatcherObject，得在带消息泵的线程上造。
            var thread = new Thread(() =>
            {
                try { RunVariableEventDialogChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("变量事件弹窗断言全程未抛异常", false, failure.ToString());
        }

        private static void RunVariableEventDialogChecks()
        {
            var picker = new FakeVariablePicker();
            var pagePicker = new FakePagePicker();

            // ---------------- ① 空工作区：左栏真的空 ----------------

            var emptyWorkspace = new WorkspaceContext();
            emptyWorkspace.GlobalVariables.Clear();

            var emptyVm = new ScadaVariableEventDialogViewModel(emptyWorkspace, picker, pagePicker);
            emptyVm.OnDialogOpened(new DialogParameters());

            Check("工作区里一个变量都没有时左栏为空（空态提示靠 HasAnyRows 显隐）",
                !emptyVm.HasAnyRows && emptyVm.Rows.IsEmpty,
                $"HasAnyRows={emptyVm.HasAnyRows} / 行数 {emptyVm.Rows.Cast<object>().Count()}");

            Check("刚打开时不预选任何一行（免得随手一个回车就把某条记录删了）",
                emptyVm.SelectedRow == null && !emptyVm.HasSelection && !emptyVm.HasRecord && !emptyVm.ShowCreatePrompt,
                $"选中=「{emptyVm.SelectedRow?.Name}」");

            Check("没有选中行时「新建」「删除」都置灰",
                !emptyVm.CreateCommand.CanExecute() && !emptyVm.RemoveCommand.CanExecute(), "");

            // ---------------- ② 主场景：3 个变量 + 3 条记录（含 1 条失效） ----------------

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            workspace.SwitchSolution(solution);
            workspace.GlobalVariables.Clear();

            var doc = solution.Scada;

            var tempVar = VariableFactory.CreateLocal("炉温", typeof(double), "炉膛温度", 23.0);
            var runVar = VariableFactory.CreateLocal("运行", typeof(bool), "设备运行标志", false);
            var idleVar = VariableFactory.CreateLocal("待机计数", typeof(int), "待机次数", 0);
            workspace.GlobalVariables.Add(tempVar);
            workspace.GlobalVariables.Add(runVar);
            workspace.GlobalVariables.Add(idleVar);

            // 炉温：上下限 + 三类事件（左栏该显「已配置」）
            var tempRecord = doc.GetOrAddVariableEvent(tempVar.VariableId, tempVar.Name);
            tempRecord.UpperLimit = 80;
            tempRecord.LowerLimit = 5;
            foreach (var type in new[]
                     {
                         ScadaEventType.ValueChanged,
                         ScadaEventType.ValueOverUpperLimit,
                         ScadaEventType.ValueUnderLowerLimit,
                     })
            {
                tempRecord.AddEventHook(type).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
            }

            // 运行：配了事件但整条停用（左栏该显「已停用」）
            var runRecord = doc.GetOrAddVariableEvent(runVar.VariableId, runVar.Name);
            runRecord.AddEventHook(ScadaEventType.ValueBecameTrue).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
            runRecord.IsEnabled = false;

            // 待机计数：故意不建记录（左栏该显「未配置」）

            // 一条引用了已不存在变量的记录（左栏该落在「已失效」段）
            var staleRecord = doc.GetOrAddVariableEvent(Guid.NewGuid(), "已删除的变量");
            staleRecord.AddEventHook(ScadaEventType.ValueChanged).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });

            var vm = new ScadaVariableEventDialogViewModel(workspace, picker, pagePicker);
            vm.OnDialogOpened(new DialogParameters());

            var rows = vm.Rows.Cast<ScadaVariableEventRow>().ToList();

            Check("左栏把工作区变量与「变量已不存在」的记录一起列出来（4 行 = 3 个变量 + 1 条失效）",
                rows.Count == 4, $"行数 {rows.Count}：{string.Join("、", rows.Select(r => r.Name))}");

            var groupNames = vm.Rows.Groups == null
                ? string.Empty
                : string.Join("/", vm.Rows.Groups.Cast<CollectionViewGroup>().Select(g => g.Name?.ToString() ?? string.Empty));
            Check("分组只有两段，且顺序是「变量」在前「已失效」在后（组序按行首次出现顺序，不是字典序）",
                groupNames == "变量/已失效（变量已不存在）", $"分组：{groupNames}");

            var tempRow = rows.Single(r => r.Name == "炉温");
            var runRow = rows.Single(r => r.Name == "运行");
            var idleRow = rows.Single(r => r.Name == "待机计数");
            var staleRow = rows.Single(r => r.IsStale);

            Check("已配置的变量行状态字是「已配置」",
                tempRow.SummaryText == "已配置" && tempRow.HasVariable && !tempRow.IsStale, tempRow.SummaryText);

            Check("整条停用的记录状态字是「已停用」（配置还在、运行态连变量都不订阅）",
                runRow.SummaryText == "已停用" && runRow.Record != null, runRow.SummaryText);

            Check("还没配记录的变量状态字是「未配置」",
                idleRow.SummaryText == "未配置" && idleRow.Record == null, idleRow.SummaryText);

            Check("失效行落在「已失效（变量已不存在）」段，名字靠记录里的名字快照兜底",
                staleRow.GroupLabel == "已失效（变量已不存在）" && staleRow.Name == "已删除的变量"
                && !staleRow.HasVariable && staleRow.Info == null,
                $"组=「{staleRow.GroupLabel}」/ 名=「{staleRow.Name}」");

            Check("已配置行的阈值摘要来自记录本身（与面板对「超上限 / 低于下限」的措辞同源）",
                tempRow.ConditionText == "超上限 80 / 低于下限 5", tempRow.ConditionText);

            Check("一条阈值都没配的行摘要为空串（不是「超上限 0」——什么都没判不该显示成判了 0）",
                idleRow.ConditionText.Length == 0, $"「{idleRow.ConditionText}」");

            // ---------------- ③ 选中「未配置」的变量：只给「新建」，不给「删除」 ----------------

            vm.SelectedRow = idleRow;
            Check("选中未配置的变量：右栏给的是「新建」入口而不是空荡荡一片",
                vm.HasSelection && !vm.HasRecord && vm.ShowCreatePrompt,
                $"HasSelection={vm.HasSelection} / HasRecord={vm.HasRecord} / ShowCreatePrompt={vm.ShowCreatePrompt}");

            Check("未配置时「新建」可用、「删除」置灰（没有记录可删）",
                vm.CreateCommand.CanExecute() && !vm.RemoveCommand.CanExecute(), "");

            Check("未配置时 5 个编辑器不建出来（它们构造时要把宿主钉死在记录上）",
                vm.Editors.Count == 0, $"编辑器 {vm.Editors.Count} 个");

            vm.SelectedRow = runRow;
            Check("选中已有记录的变量：「新建」置灰（不许建第二条）、「删除」可用",
                !vm.CreateCommand.CanExecute() && vm.RemoveCommand.CanExecute(), "");

            vm.SelectedRow = idleRow;
            var beforeCreate = doc.VariableEvents.Count;
            vm.CreateCommand.Execute();

            Check("点「新建」才真的建记录（选中即建会让方案凭空多一条空配置、版本号白脏一次）",
                doc.VariableEvents.Count == beforeCreate + 1
                && idleRow.Record != null
                && ReferenceEquals(doc.FindVariableEvent(idleVar.VariableId), idleRow.Record),
                $"记录数 {beforeCreate} → {doc.VariableEvents.Count}");

            Check("新建之后右栏切到编辑态：5 个编辑器 + 「删除」可用 + 不再显示新建提示",
                vm.HasRecord && !vm.ShowCreatePrompt && vm.Editors.Count == 5 && vm.RemoveCommand.CanExecute(),
                $"HasRecord={vm.HasRecord} / 编辑器 {vm.Editors.Count} 个 / 删除可用={vm.RemoveCommand.CanExecute()}");

            Check("新建后左栏那一行的状态字立刻变「已配置」（不刷新的话用户会以为没建上）",
                idleRow.SummaryText == "已配置", idleRow.SummaryText);

            Check("5 个编辑器就是手册 7.5.2 那五类值驱动事件，顺序即界面顺序",
                string.Join("/", vm.Editors.Select(e => e.EventType))
                    == "ValueChanged/ValueBecameTrue/ValueBecameFalse/ValueOverUpperLimit/ValueUnderLowerLimit",
                string.Join("/", vm.Editors.Select(e => e.EventType)));

            Check("每个编辑器的显示名走 ScadaEventType.DisplayName()，不是枚举名",
                vm.Editors.All(e => e.DisplayName == e.EventType.DisplayName() && e.DisplayName.Length > 0),
                string.Join("/", vm.Editors.Select(e => e.DisplayName)));

            Check("每个编辑器都有悬停说明（说清「什么时候触发」，而不是重复事件名）",
                vm.Editors.All(e => !string.IsNullOrEmpty(e.Description)),
                $"「{vm.Editors[0].Description}」");

            Check("刚建的记录一条钩子都没配：IsConfigured 全假，且 Actions 是 null 而不是空表（两者语义不同）",
                vm.Editors.All(e => !e.IsConfigured) && vm.Editors.All(e => e.Actions == null),
                $"已配 {vm.Editors.Count(e => e.IsConfigured)} 条 / 有动作表 {vm.Editors.Count(e => e.Actions != null)} 条");

            // ---------------- ④ 已有配置的行：勾选态与阈值都是回读的 ----------------

            vm.SelectedRow = tempRow;

            Check("已有配置的记录打开就是勾上的：已配三类为真、另两类为假（真值始终从宿主回读，不缓存）",
                string.Join(",", vm.Editors.Select(e => e.IsConfigured)) == "True,False,False,True,True",
                string.Join(",", vm.Editors.Select(e => $"{e.EventType}={e.IsConfigured}")));

            Check("选中已配置行时阈值两格与两个开关都是回读的：80 / 5，两个勾都亮着",
                vm.UpperLimitEnabled && vm.UpperLimitText == "80"
                && vm.LowerLimitEnabled && vm.LowerLimitText == "5" && vm.IsRecordEnabled,
                $"{vm.UpperLimitEnabled}/{vm.UpperLimitText} · {vm.LowerLimitEnabled}/{vm.LowerLimitText} · 监视={vm.IsRecordEnabled}");

            // ---------------- ⑤ 阈值：字符串接输入，解析不了就回灌原值 ----------------

            vm.UpperLimitText = "8O";   // 字母 O，不是零
            Check("填了非数字（8O，字母 O）：回灌原值 + 给一句中文原因，模型一个数都不动",
                vm.HasError && vm.UpperLimitText == "80" && tempRecord.UpperLimit == 80
                && (vm.ErrorMessage ?? string.Empty).Contains("请填数字"),
                $"提示=「{vm.ErrorMessage}」/ 框内=「{vm.UpperLimitText}」/ 模型={tempRecord.UpperLimit}");

            vm.UpperLimitText = "90";
            Check("改成有效数字立刻写进模型（即时写回，本弹窗没有「确定」这一步）",
                !vm.HasError && tempRecord.UpperLimit == 90 && vm.UpperLimitText == "90",
                $"模型={tempRecord.UpperLimit} / 框内=「{vm.UpperLimitText}」");

            vm.UpperLimitText = "   ";
            Check("清空 = 不判这条限值：模型落 null、勾也跟着摘掉（界面上两处始终一致）",
                tempRecord.UpperLimit == null && !vm.UpperLimitEnabled && vm.UpperLimitText.Length == 0,
                $"模型={(tempRecord.UpperLimit == null ? "null" : tempRecord.UpperLimit.ToString())} / 勾={vm.UpperLimitEnabled}");

            vm.UpperLimitEnabled = true;
            Check("勾上时框里是空的就给一个「立刻看得见」的初值 0（勾了却不判，界面上是在骗人）",
                tempRecord.UpperLimit == 0 && vm.UpperLimitText == "0" && vm.UpperLimitEnabled,
                $"模型={tempRecord.UpperLimit} / 框内=「{vm.UpperLimitText}」");

            vm.LowerLimitEnabled = false;
            Check("取消下限判定：模型落 null、框里也一并清空（框是模型的投影，不留「看着还在其实已不判」的旧值）",
                tempRecord.LowerLimit == null && !vm.LowerLimitEnabled && vm.LowerLimitText.Length == 0,
                $"模型={(tempRecord.LowerLimit == null ? "null" : tempRecord.LowerLimit.ToString())} / 框内=「{vm.LowerLimitText}」");

            vm.LowerLimitEnabled = true;
            Check("重新勾上下限判定：框里已经空了，就给初值 0（取消时清空是刻意的，避免旧值偷偷复活）",
                tempRecord.LowerLimit == 0 && vm.LowerLimitText == "0",
                $"模型={tempRecord.LowerLimit} / 框内=「{vm.LowerLimitText}」");

            // ---------------- ⑥ 整条监视开关：左栏状态字要跟着变 ----------------

            vm.SelectedRow = runRow;
            Check("选中停用的记录时开关是关的（回读，不是默认值）", !vm.IsRecordEnabled, $"监视={vm.IsRecordEnabled}");

            vm.IsRecordEnabled = true;
            Check("打开整条监视：模型 IsEnabled 跟着变，左栏状态字从「已停用」翻成「已配置」",
                runRecord.IsEnabled && runRow.SummaryText == "已配置",
                $"模型={runRecord.IsEnabled} / 状态字={runRow.SummaryText}");

            // ---------------- ⑦ 删除：记录没了，选中留在同一个变量上 ----------------

            vm.SelectedRow = tempRow;
            var beforeRemove = doc.VariableEvents.Count;
            vm.RemoveCommand.Execute();

            var rowsAfterRemove = vm.Rows.Cast<ScadaVariableEventRow>().ToList();
            var tempRowAfter = rowsAfterRemove.Single(r => r.VariableId == tempVar.VariableId);

            Check("点「删除」把记录从文档里摘掉（连同它下面的钩子与动作一起走）",
                doc.VariableEvents.Count == beforeRemove - 1 && tempRowAfter.Record == null,
                $"记录数 {beforeRemove} → {doc.VariableEvents.Count}");

            Check("删完选中留在同一个变量上（右栏塌回「请从左侧选择」会让用户刚动过的地方凭空消失）",
                vm.SelectedRow != null && vm.SelectedRow.VariableId == tempVar.VariableId
                && vm.HasSelection && !vm.HasRecord && vm.ShowCreatePrompt,
                $"选中=「{vm.SelectedRow?.Name}」");

            Check("删完那一行回到「未配置」，5 个编辑器一起收掉",
                tempRowAfter.SummaryText == "未配置" && vm.Editors.Count == 0,
                $"状态字={tempRowAfter.SummaryText} / 编辑器 {vm.Editors.Count} 个");

            Check("删完左栏仍是 4 行（失效记录还在，它只能靠用户显式删）",
                rowsAfterRemove.Count == 4, $"行数 {rowsAfterRemove.Count}");

            // ---------------- ⑧ 关闭：本弹窗没有「确定」，关的就是窗 ----------------

            IDialogResult? closed = null;
            vm.RequestClose = MakeDialogCloseListener(r => closed = r);
            vm.CloseCommand.Execute();

            Check("「关闭」以 OK 关窗、且不带任何参数（写回是即时的，关窗不代表「是否采纳」）",
                closed != null && closed.Result == ButtonResult.OK && closed.Parameters.Count == 0,
                closed == null ? "没有回调" : $"{closed.Result} / 参数 {closed.Parameters.Count} 项");

            // ---------------- ⑨ 审计：成功与失败都记，署名来自注入的提供者 ----------------

            string veAuditDir = Path.Combine(Path.GetTempPath(), "ScadaVeAudit_" + Guid.NewGuid().ToString("N"));
            var veAudit = new ScadaAuditWriter(veAuditDir);

            // 读回全部审计行（跳过每份文件的表头）。按目录里所有 Audit-*.csv 汇总，
            // 这样断言不会因为"正好跑过午夜"而假红。
            string[] VeAuditRows()
            {
                if (!Directory.Exists(veAuditDir)) return Array.Empty<string>();
                return Directory.GetFiles(veAuditDir, "Audit-*.csv")
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .SelectMany(p => File.ReadAllLines(p).Skip(1))
                    .ToArray();
            }

            string veActor = "王工";
            var audited = new ScadaVariableEventDialogViewModel(workspace, picker, pagePicker, veAudit, () => veActor);
            audited.OnDialogOpened(new DialogParameters());

            var auditedTarget = audited.Rows.Cast<ScadaVariableEventRow>().Single(r => r.Name == "炉温");
            audited.SelectedRow = auditedTarget;
            audited.CreateCommand.Execute();

            var veRows1 = VeAuditRows();
            Check("新建落一条审计：事件列写「变量事件」、动作列写「新建配置」、说明里带上变量名",
                veRows1.Length == 1 && veRows1[0].Contains("变量事件") && veRows1[0].Contains("新建配置")
                && veRows1[0].Contains("成功") && veRows1[0].Contains("炉温"),
                string.Join(" | ", veRows1));

            Check("署名来自注入的提供者（本类不认识会话，宿主把它接到「此刻登录着谁」上）",
                veRows1.Length == 1 && veRows1[0].Contains("王工"), veRows1[0]);

            audited.UpperLimitEnabled = true;   // 框里是空的 → seed 0
            var veRows2 = VeAuditRows();
            Check("勾上上限判定落一条「修改阈值」审计，说明写明启用后的值",
                veRows2.Length == 2 && veRows2[1].Contains("修改阈值")
                && veRows2[1].Contains("上限判定") && veRows2[1].Contains("启用（0）"),
                veRows2[1]);

            audited.UpperLimitText = "0";       // 与旧值相同：不写模型、也不落审计
            Check("填的数字和原值一样时既不写模型也不落审计（「点了但没改」不该在审计里留痕）",
                VeAuditRows().Length == 2, string.Join(" | ", VeAuditRows()));

            veActor = "李工";
            audited.UpperLimitText = "88";
            var veRows3 = VeAuditRows();
            Check("改阈值落一条审计，说明写「旧值 → 新值」：把上限从 0 提到 88 之后要答得出是谁改的",
                veRows3.Length == 3 && veRows3[2].Contains("上限：0 → 88") && veRows3[2].Contains("李工"),
                veRows3[2]);

            audited.UpperLimitText = "8O";      // 解析不了：不写模型、也不落审计
            Check("填了非数字不落审计（没写进模型的事不该在审计里冒充「改过」），只给一句中文原因",
                VeAuditRows().Length == 3 && audited.HasError, $"提示=「{audited.ErrorMessage}」");

            audited.IsRecordEnabled = false;
            var veRows4 = VeAuditRows();
            Check("停用整条监视落一条「启停监视」审计（停用是「连变量都不订阅」的开关，事后要答得出是谁关的）",
                veRows4.Length == 4 && veRows4[3].Contains("启停监视") && veRows4[3].Contains("停用监视"),
                veRows4[3]);

            audited.RemoveCommand.Execute();
            var veRows5 = VeAuditRows();
            Check("删除落一条「删除配置」审计",
                veRows5.Length == 5 && veRows5[4].Contains("删除配置") && veRows5[4].Contains("成功"),
                veRows5[4]);

            // 没有打开的方案：新建必须失败，而且失败也要留痕
            var noSolution = new WorkspaceContext();
            noSolution.GlobalVariables.Clear();
            noSolution.GlobalVariables.Add(VariableFactory.CreateLocal("孤儿变量", typeof(int), "没有方案时的变量", 1));

            var orphanVm = new ScadaVariableEventDialogViewModel(noSolution, picker, pagePicker, veAudit, () => null);
            orphanVm.OnDialogOpened(new DialogParameters());
            orphanVm.SelectedRow = orphanVm.Rows.Cast<ScadaVariableEventRow>().Single();
            orphanVm.CreateCommand.Execute();

            var veRows6 = VeAuditRows();
            Check("没有打开的方案时新建失败：给一句中文原因，且失败也落一条审计（「试着建但没建成」与「建成了」是两条不同的线索）",
                orphanVm.HasError && (orphanVm.ErrorMessage ?? string.Empty).Contains("没有打开的方案")
                && veRows6.Length == 6 && veRows6[5].Contains("新建配置") && veRows6[5].Contains("失败")
                && veRows6[5].Contains("没有打开的方案"),
                $"提示=「{orphanVm.ErrorMessage}」/ 审计={veRows6.LastOrDefault() ?? "（无）"}");

            Check("取不到「此刻登录着谁」时署名回落「未登录」，而不是留一格空白（有人没登录就改了配置，这本身是线索）",
                veRows6.Length == 6 && veRows6[5].Contains("未登录"), veRows6[5]);
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
            var originalMonitor = settings.Current.RunWindowMonitor;
            try
            {
                settings.Current.RunWindowMode = ScadaRunWindowMode.AttachedToMainWindow;
                settings.Current.RunWindowMonitor = "";
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

                // ---------------- ②b 目标屏：下拉清单 / 写盘 / 认不出的值 ----------------

                Check("落屏下拉：第一项固定是「跟随主屏」且设备名为空串（空串就是配置里的默认值）",
                    reopen.Monitors.Count >= 1
                    && reopen.Monitors[0].DeviceName.Length == 0
                    && reopen.Monitors[0].Label.Contains("跟随主屏"),
                    reopen.Monitors.Count > 0 ? $"{reopen.Monitors[0].Label} / 「{reopen.Monitors[0].DeviceName}」" : "清单为空");

                Check("落屏下拉：每项的设备名互不相同（重名会让 SelectedValue 永远选中第一项）",
                    reopen.Monitors.Select(o => o.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == reopen.Monitors.Count,
                    string.Join(" | ", reopen.Monitors.Select(o => $"「{o.DeviceName}」")));

                // 清单里可能有真显示器（单屏机器上就只有"跟随主屏"一项），
                // 所以下面用一个不在清单里的设备名来钉"认不出就归空串"这条规则——
                // 否则下拉会停在一个列表里根本没有的值上，界面看着像"没选"，确定时写下去的却是别的。
                reopen.SelectedMonitorDeviceName = "";
                reopen.RequestClose = MakeCloseListener(_ => { });
                reopen.ConfirmCommand.Execute();
                var rereadMonitor = new AppSettingsService();
                Check("确定：目标屏写进 AppConfig.json（空串也照样写，重新加载读回来仍是空串）",
                    rereadMonitor.Current.RunWindowMonitor == "",
                    $"「{rereadMonitor.Current.RunWindowMonitor}」");

                // 有真显示器时再钉一遍"非空设备名原样落盘"：空串落盘只能证明"写了"，
                // 证明不了"写对了"——现场要的恰恰是副屏那一串设备名能存下来、读回来还是它。
                var realMonitor = reopen.Monitors.FirstOrDefault(o => o.DeviceName.Length > 0);
                if (realMonitor != null)
                {
                    var pick = new ScadaRunWindowSettingsVM(settings);
                    pick.OnDialogOpened(new DialogParameters());
                    pick.SelectedMonitorDeviceName = realMonitor.DeviceName;
                    pick.RequestClose = MakeCloseListener(_ => { });
                    pick.ConfirmCommand.Execute();

                    var rereadPick = new AppSettingsService();
                    Check("确定：选中的显示器设备名原样落进 AppConfig.json，重新加载读回来还是它",
                        string.Equals(rereadPick.Current.RunWindowMonitor, realMonitor.DeviceName, StringComparison.OrdinalIgnoreCase),
                        $"写入「{realMonitor.DeviceName}」→ 读回「{rereadPick.Current.RunWindowMonitor}」");

                    var reopenPick = new ScadaRunWindowSettingsVM(settings);
                    reopenPick.OnDialogOpened(new DialogParameters());
                    Check("再次打开：下拉选中的就是配置里那台显示器（现读，不是上一轮残留）",
                        string.Equals(reopenPick.SelectedMonitorDeviceName, realMonitor.DeviceName, StringComparison.OrdinalIgnoreCase),
                        $"选中「{reopenPick.SelectedMonitorDeviceName}」");
                }

                // 配置里存着一个当前不在线的设备名：打开弹窗必须显示「跟随主屏」，
                // 与运行时真正的回落结果一致，不会出现"看着选了副屏、实际跑在主屏"。
                settings.Current.RunWindowMonitor = @"\\.\DISPLAY_NOT_ONLINE";
                settings.Save();
                var offline = new ScadaRunWindowSettingsVM(settings);
                offline.OnDialogOpened(new DialogParameters());
                Check("落屏下拉：配置里那台显示器不在线时显示「跟随主屏」（与运行时的回落结果一致）",
                    offline.SelectedMonitorDeviceName.Length == 0,
                    $"「{offline.SelectedMonitorDeviceName}」");

                // 取消不写盘这条规则对目标屏同样成立
                offline.SelectedMonitorDeviceName = offline.Monitors[offline.Monitors.Count - 1].DeviceName;
                offline.RequestClose = MakeCloseListener(_ => { });
                offline.CancelCommand.Execute();
                Check("取消：目标屏也不写盘，配置保持原值（改到一半放弃不该留痕）",
                    settings.Current.RunWindowMonitor == @"\\.\DISPLAY_NOT_ONLINE",
                    $"「{settings.Current.RunWindowMonitor}」");

                // ---------------- ②c 改显示设置落审计（S13-f） ----------------
                // 这一项决定"现场跑生产时看到的是不是只有运行画面"：从依附切到独立，
                // 主界面（含图像、日志）就重新露出来了。属于"改了没人在意、出事时又要问是谁改的"那一类。

                // 先把配置归到一个"打开弹窗后一处都不用改"的干净状态：上一小节特意在配置里留了
                // 一个不在线的设备名，不清掉的话下面第一条断言就会变成"改过"（弹窗打开时会把它归成空串）。
                settings.Current.RunWindowMode = ScadaRunWindowMode.AttachedToMainWindow;
                settings.Current.RunWindowMonitor = "";
                settings.Save();

                string runAuditDir = Path.Combine(Path.GetTempPath(), "ScadaRunAudit_" + Guid.NewGuid().ToString("N"));
                var runAudit = new ScadaAuditWriter(runAuditDir);

                // 读回全部审计行（跳过每份文件的表头）。按目录里所有 Audit-*.csv 汇总，
                // 这样断言不会因为"正好跑过午夜"而假红。
                string[] RunAuditRows()
                {
                    if (!Directory.Exists(runAuditDir)) return Array.Empty<string>();
                    return Directory.GetFiles(runAuditDir, "Audit-*.csv")
                        .OrderBy(p => p, StringComparer.Ordinal)
                        .SelectMany(p => File.ReadAllLines(p).Skip(1))
                        .ToArray();
                }

                string runActor = "王工";
                var audited = new ScadaRunWindowSettingsVM(settings, runAudit, () => runActor);
                audited.OnDialogOpened(new DialogParameters());
                audited.RequestClose = MakeCloseListener(_ => { });

                audited.ConfirmCommand.Execute();
                var runRows = RunAuditRows();
                Check("改显示设置：一处没动就确定也落一条，说明写明「未写盘」（事后问「他到底改过没有」，答案是「点过，但没改」）",
                    runRows.Length == 1 && runRows[0].Contains("运行窗口") && runRows[0].Contains("修改显示设置")
                    && runRows[0].Contains("成功") && runRows[0].Contains("未写盘"),
                    string.Join(" | ", runRows));

                Check("署名来自注入的提供者（本类不认识会话，宿主把它接到「此刻登录着谁」上）",
                    runRows.Length == 1 && runRows[0].Contains("王工"), runRows[0]);

                runActor = "李工";
                audited.IsIndependent = true;
                audited.ConfirmCommand.Execute();
                runRows = RunAuditRows();
                Check("改形态落一条，说明写「旧值 → 新值」且用中文档位：从依附切到独立，主界面就重新露出来了",
                    runRows.Length == 2 && runRows[1].Contains("形态：依附主窗口 → 独立窗口")
                    && runRows[1].Contains("成功") && runRows[1].Contains("李工"),
                    runRows[1]);

                // 目标屏那一半：用一个（几乎必然）不存在的设备名，断言就不依赖这台机器上恰好接着几块屏
                audited.SelectedMonitorDeviceName = @"\\.\DISPLAY9";
                audited.ConfirmCommand.Execute();
                runRows = RunAuditRows();
                Check("改目标屏落一条：设备名原样写进说明（它才是与系统「显示设置」对得上的那个值，换成「显示器 2」要多绕一层）",
                    runRows.Length == 3 && runRows[2].Contains(@"目标屏：跟随主屏 → \\.\DISPLAY9"),
                    runRows[2]);

                audited.CancelCommand.Execute();
                Check("取消不落审计（改到一半放弃不该留痕，与「不写盘」同一口径）",
                    RunAuditRows().Length == 3, string.Join(" | ", RunAuditRows()));
            }
            finally
            {
                settings.Current.RunWindowMode = original;
                settings.Current.RunWindowMonitor = originalMonitor;
                settings.Save();
            }

            // ---------------- ③ 窗口形态：属性组合 ----------------

            RunWindowModeWindowChecks();

            // ---------------- ④ 落屏：显示器清单 + 换算纯函数 + 目标屏落点 ----------------

            RunWindowMonitorChecks();
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

        /// <summary>
        /// 与 <see cref="MakeCloseListener"/> 同源，但把整个 <see cref="IDialogResult"/> 交出来，
        /// 好断言"关窗时回传的参数里带了什么"（画面选择器要带 (Id, 名字)）。
        /// </summary>
        private static DialogCloseListener MakeDialogCloseListener(Action<IDialogResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException(
                    "Prism.Dialogs.DialogCloseListener", ".ctor(Action<IDialogResult>)");

            return (DialogCloseListener)ctor.Invoke(new object[] { onClosed });
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

            // 传 null 目标屏 = 走 S13-e 之前那条路径（SystemParameters.WorkArea）。这一组是历史行为守卫：
            // "跟随主屏"这个默认配置必须与升级前逐字一致。
            window.ApplyRunWindowMode(ScadaRunWindowMode.IndependentWindow, null);
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

            window.ApplyRunWindowMode(ScadaRunWindowMode.AttachedToMainWindow, null);
            Check("依附形态：无边框 + 最大化 + 不占任务栏（现场运行形态，回到历史行为）",
                window.WindowStyle == WindowStyle.None
                && window.ResizeMode == ResizeMode.NoResize
                && window.WindowState == WindowState.Maximized
                && !window.ShowInTaskbar,
                $"WindowStyle={window.WindowStyle} / ResizeMode={window.ResizeMode} / ShowInTaskbar={window.ShowInTaskbar}");

            Check("窗口自身不碰 Owner：归谁管是宿主的策略（独立形态一挂 Owner 就没法并排看）",
                window.Owner == null, window.Owner?.GetType().Name ?? "null");
        }

        // ==================================================================
        //  [DS] 运行窗口落在哪块屏：显示器清单 + DPI 换算 + 落点几何
        //
        //  这一节钉的是「多显示器现场要把运行画面单独放到操作员正对那块屏」这件事。
        //  与 [AB] 的分工：[AB] 管"窗口长什么样"（形态），[DS] 管"窗口出现在哪块屏"。
        //  两者独立——依附形态也能指定副屏（现场触摸屏一体机常见主屏给工程师、副屏给操作员）。
        //
        //  断言分两半：
        //    · 纯函数半（本节）：DPI 换算、物理→DIP、落点几何、方位描述、设备名解析。
        //      全是确定的算式，不需要真显示器，单屏机器上也全绿；
        //    · 窗口半（RunWindowMonitorWindowChecks）：手工造一块假副屏，钉住窗口真的落到它上面。
        // ==================================================================
        private static void RunWindowMonitorChecks()
        {
            Section("[DS] 运行窗口落屏：DPI 换算 + 落点几何 + 显示器解析（纯函数）");

            // ---------------- ① DPI 换算：物理像素 ÷ 因子 = DIP ----------------

            Check("DPI 换算：主屏物理宽 = WPF 认的宽 → 1.0（100% 缩放）",
                Math.Abs(ScadaMonitors.ComputeDpiScale(1920, 1920) - 1.0) < 1e-9,
                ScadaMonitors.ComputeDpiScale(1920, 1920).ToString("0.###"));

            Check("DPI 换算：物理 2880 / DIP 1920 → 1.5（150% 缩放）",
                Math.Abs(ScadaMonitors.ComputeDpiScale(2880, 1920) - 1.5) < 1e-9,
                ScadaMonitors.ComputeDpiScale(2880, 1920).ToString("0.###"));

            Check("DPI 换算：任一来源 <= 0 一律按 1.0（拿 0 当除数会得到无穷大，窗口会被摆到屏幕外）",
                Math.Abs(ScadaMonitors.ComputeDpiScale(0, 1920) - 1.0) < 1e-9
                && Math.Abs(ScadaMonitors.ComputeDpiScale(1920, 0) - 1.0) < 1e-9
                && Math.Abs(ScadaMonitors.ComputeDpiScale(-5, 1920) - 1.0) < 1e-9,
                $"{ScadaMonitors.ComputeDpiScale(0, 1920):0.###} / {ScadaMonitors.ComputeDpiScale(1920, 0):0.###} / {ScadaMonitors.ComputeDpiScale(-5, 1920):0.###}");

            Check("DPI 换算：两个来源差得离谱（0.4 / 8.0 倍）时按 1.0，不拿错因子把窗口摆到屏外",
                Math.Abs(ScadaMonitors.ComputeDpiScale(400, 1000) - 1.0) < 1e-9
                && Math.Abs(ScadaMonitors.ComputeDpiScale(8000, 1000) - 1.0) < 1e-9,
                $"{ScadaMonitors.ComputeDpiScale(400, 1000):0.###} / {ScadaMonitors.ComputeDpiScale(8000, 1000):0.###}");

            Check("DPI 换算：区间两端 0.5 / 4.0 本身是合法的（护栏只在区间之外生效）",
                Math.Abs(ScadaMonitors.ComputeDpiScale(500, 1000) - 0.5) < 1e-9
                && Math.Abs(ScadaMonitors.ComputeDpiScale(4000, 1000) - 4.0) < 1e-9,
                $"{ScadaMonitors.ComputeDpiScale(500, 1000):0.###} / {ScadaMonitors.ComputeDpiScale(4000, 1000):0.###}");

            // ---------------- ② 物理矩形 → DIP ----------------

            var dip = ScadaMonitors.ToDip(new Rect(1920, 0, 1920, 1040), 1.5);
            Check("物理矩形 → DIP：原点与尺寸一起除（只除尺寸会让副屏坐标整体偏移）",
                Math.Abs(dip.X - 1280) < 1e-9
                && Math.Abs(dip.Y) < 1e-9
                && Math.Abs(dip.Width - 1280) < 1e-9
                && Math.Abs(dip.Height - 1040.0 / 1.5) < 1e-9,
                $"{dip.X:0.##},{dip.Y:0.##} {dip.Width:0.##}×{dip.Height:0.##}");

            Check("物理矩形 → DIP：缩放因子 <= 0 按 1.0 原样返回（不会除出 NaN / 无穷）",
                ScadaMonitors.ToDip(new Rect(0, 0, 100, 50), 0) == new Rect(0, 0, 100, 50)
                && ScadaMonitors.ToDip(new Rect(0, 0, 100, 50), -2) == new Rect(0, 0, 100, 50),
                ScadaMonitors.ToDip(new Rect(0, 0, 100, 50), 0).ToString());

            // ---------------- ③ 落点几何（独立形态的 Left/Top/Width/Height） ----------------

            var work = new Rect(0, 0, 1920, 1040);
            var placed = ScadaMonitors.PlaceIndependent(work, 1280, 800);

            Check("落点：尺寸按工作区 62% / 72% 收（想要 1280×800，实得 1190.4×748.8）",
                Math.Abs(placed.Width - 1920 * 0.62) < 1e-9
                && Math.Abs(placed.Height - 1040 * 0.72) < 1e-9,
                $"{placed.Width:0.##} × {placed.Height:0.##}");

            Check("落点：贴工作区右下角、留 24 边（居中会正好压住主界面的图像区，等于没解决问题）",
                Math.Abs(placed.Right - (work.Right - 24)) < 1e-9
                && Math.Abs(placed.Bottom - (work.Bottom - 24)) < 1e-9,
                $"右下角 {placed.Right:0.##},{placed.Bottom:0.##} / 工作区右下 {work.Right:0},{work.Bottom:0}");

            Check("落点：想要的尺寸比比例上限还小就照原样用（用户拉小了不该被放大回去）",
                Math.Abs(ScadaMonitors.PlaceIndependent(work, 600, 400).Width - 600) < 1e-9
                && Math.Abs(ScadaMonitors.PlaceIndependent(work, 600, 400).Height - 400) < 1e-9,
                $"{ScadaMonitors.PlaceIndependent(work, 600, 400).Width:0.##} × {ScadaMonitors.PlaceIndependent(work, 600, 400).Height:0.##}");

            var tiny = ScadaMonitors.PlaceIndependent(new Rect(0, 0, 600, 400), 1280, 800);
            Check("落点：工作区很小时尺寸夹在 480×360 以上（再小就没法看画面了）",
                Math.Abs(tiny.Width - 480) < 1e-9 && Math.Abs(tiny.Height - 360) < 1e-9,
                $"{tiny.Width:0.##} × {tiny.Height:0.##}");

            // ---------------- ④ 方位描述（下拉框里区分两块同型号屏靠它） ----------------

            var primaryFake = new ScadaMonitor(@"\\.\DISPLAY1", true, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040));
            var rightFake = new ScadaMonitor(@"\\.\DISPLAY2", false, new Rect(1920, 0, 1920, 1080), new Rect(1920, 0, 1920, 1040));
            var leftFake = new ScadaMonitor(@"\\.\DISPLAY3", false, new Rect(-1920, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1040));
            var belowFake = new ScadaMonitor(@"\\.\DISPLAY4", false, new Rect(0, 1080, 1920, 1080), new Rect(0, 1080, 1920, 1040));
            var aboveFake = new ScadaMonitor(@"\\.\DISPLAY5", false, new Rect(0, -1080, 1920, 1080), new Rect(0, -1080, 1920, 1040));

            Check("方位描述：主屏自己说「主显示器」",
                ScadaMonitors.DescribePosition(primaryFake, primaryFake) == "主显示器",
                ScadaMonitors.DescribePosition(primaryFake, primaryFake));

            Check("方位描述：右 / 左 / 下 / 上 四个方位各自认得出来（横向差更大算左右，否则算上下）",
                ScadaMonitors.DescribePosition(rightFake, primaryFake) == "在主屏右侧"
                && ScadaMonitors.DescribePosition(leftFake, primaryFake) == "在主屏左侧"
                && ScadaMonitors.DescribePosition(belowFake, primaryFake) == "在主屏下方"
                && ScadaMonitors.DescribePosition(aboveFake, primaryFake) == "在主屏上方",
                $"{ScadaMonitors.DescribePosition(rightFake, primaryFake)} / {ScadaMonitors.DescribePosition(leftFake, primaryFake)} / {ScadaMonitors.DescribePosition(belowFake, primaryFake)} / {ScadaMonitors.DescribePosition(aboveFake, primaryFake)}");

            Check("方位描述：一块都没标主屏时说「附加显示器」，不说「主显示器」（否则两块屏同名，等于没说）",
                ScadaMonitors.DescribePosition(rightFake, null) == "附加显示器",
                ScadaMonitors.DescribePosition(rightFake, null));

            // ---------------- ⑤ 本机显示器清单 + 设备名解析 ----------------

            var monitors = ScadaMonitors.All();

            if (monitors.Count > 0)
            {
                var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

                Check("显示器清单：主屏排第一（下拉框的次序必须每次一样，否则肌肉记忆会漂）",
                    monitors[0].IsPrimary, monitors[0].ToString());

                Check("显示器清单：每块屏都有非空设备名（空名字会让配置存不下去、也认不回来）",
                    monitors.All(m => m.DeviceName.Length > 0),
                    string.Join(" | ", monitors.Select(m => m.ToString())));

                Check("显示器清单：工作区落在全幅之内（工作区跑出屏外说明 MONITORINFOEX 的结构体布局串了）",
                    monitors.All(m => m.Work.Left >= m.Bounds.Left - 1
                                  && m.Work.Right <= m.Bounds.Right + 1
                                  && m.Work.Top >= m.Bounds.Top - 1
                                  && m.Work.Bottom <= m.Bounds.Bottom + 1),
                    string.Join(" | ", monitors.Select(m => $"{m.DeviceName} 全幅{m.Bounds.Width:0}×{m.Bounds.Height:0} 工作区{m.Work.Width:0}×{m.Work.Height:0}")));

                var resolvedEmpty = ScadaMonitors.Resolve("", out var fellBackEmpty);
                Check("解析：空串 → 主屏，且不算回落（默认配置走的就是这条路，升级行为逐字不变）",
                    resolvedEmpty != null && resolvedEmpty.IsPrimary && !fellBackEmpty,
                    $"命中={resolvedEmpty?.ToString() ?? "null"} / 回落={fellBackEmpty}");

                var resolvedHit = ScadaMonitors.Resolve(primary.DeviceName, out var fellBackHit);
                Check("解析：命中的设备名 → 原样返回那一块，不算回落",
                    resolvedHit != null
                    && string.Equals(resolvedHit.DeviceName, primary.DeviceName, StringComparison.OrdinalIgnoreCase)
                    && !fellBackHit,
                    $"命中={resolvedHit?.ToString() ?? "null"} / 回落={fellBackHit}");

                // 这一条是"现场配了副屏却跑在主屏上"那个问题的可见性保证：
                // 回落本身是对的（总比把画面摆到屏外强），但必须让日志说得出原因。
                var resolvedMiss = ScadaMonitors.Resolve(@"\\.\DISPLAY_NOT_ONLINE", out var fellBackMiss);
                Check("解析：认不出的设备名 → 回落主屏 + fellBack=true（日志据此说明「我配的屏不在线」）",
                    resolvedMiss != null && resolvedMiss.IsPrimary && fellBackMiss,
                    $"命中={resolvedMiss?.ToString() ?? "null"} / 回落={fellBackMiss}");
            }
            else
            {
                // 受限桌面（无交互会话 / 服务账户）枚举不出显示器。这条路径必须存在且安全：
                // Resolve 返回 null，窗口退回 SystemParameters.WorkArea，也就是 S13-e 之前那条路径。
                var resolvedNone = ScadaMonitors.Resolve("", out var fellBackNone);
                Check("解析：本机枚举不出显示器时返回 null 且不算回落（调用方退回 SystemParameters.WorkArea）",
                    resolvedNone == null && !fellBackNone,
                    $"命中={resolvedNone?.ToString() ?? "null"} / 回落={fellBackNone}");
            }

            var scale = ScadaMonitors.DpiScale();
            Check("本机 DPI 缩放因子落在合理区间 [0.5, 4.0]（超出说明两个来源说的不是同一块屏）",
                scale >= 0.5 && scale <= 4.0, scale.ToString("0.###"));

            // ---------------- ⑥ 窗口真的落到目标屏上 ----------------

            RunWindowMonitorWindowChecks();
        }

        /// <summary>
        /// [DS] 的第二半：<b>指定了目标屏</b>时窗口落到哪。
        ///
        /// 这里的目标屏是**手工造的一块假副屏**（物理 1920×1080 摆在主屏右边），不是本机真有的屏：
        /// 断言要钉的是"按目标屏算落点"这条规则，而"本机是不是双屏"是随机器变的。
        /// 用假屏，单屏机器上也能把多屏逻辑钉住。
        ///
        /// 每一条都新造窗口，不复用：<see cref="Window.Left"/> / <see cref="Window.WindowStartupLocation"/>
        /// 是"设过就留着"的，复用会让上一条的形态污染下一条（独立形态设过 Manual 之后，
        /// 依附形态里"跟随主屏不设 Left/Top"就永远看不出来了）。
        ///
        /// 与 <see cref="RunWindowModeWindowChecks"/> 同样单开 STA 线程：<see cref="ScadaRuntimeWindow"/>
        /// 是 <c>Window</c>（DispatcherObject 血统），在 MTA 的 Main 线程上构造会直接抛
        /// "The calling thread must be STA"，表现为整个断言进程崩掉而不是一条红的。
        /// </summary>
        private static void RunWindowMonitorWindowChecks()
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunWindowMonitorWindowChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("落屏断言（指定目标屏）全程未抛异常", false, failure.ToString());
        }

        private static void RunWindowMonitorWindowChecksCore()
        {
            // 摆在主屏右侧的一块副屏：全幅 1920×1080，工作区扣掉底部任务栏 40px
            var second = new ScadaMonitor(
                @"\\.\DISPLAY2", false,
                new Rect(1920, 0, 1920, 1080),
                new Rect(1920, 0, 1920, 1040));

            var secondDip = second.WorkDip(ScadaMonitors.DpiScale());

            // ---------------- ① 独立形态 + 指定副屏 ----------------

            var independent = new ScadaRuntimeWindow(new ScadaRuntime(new ScadaDocument()));
            independent.ApplyRunWindowMode(ScadaRunWindowMode.IndependentWindow, second);

            Check("独立形态 + 指定副屏：落点算在副屏工作区上，不回主屏（Left 明显在主屏宽度之外）",
                independent.WindowStartupLocation == WindowStartupLocation.Manual
                && Math.Abs(independent.Left + independent.Width - (secondDip.Right - 24)) < 0.5
                && Math.Abs(independent.Top + independent.Height - (secondDip.Bottom - 24)) < 0.5
                && independent.Left >= secondDip.Left - 0.5
                && independent.Top >= secondDip.Top - 0.5,
                $"Left={independent.Left:0} Top={independent.Top:0} / 副屏工作区 {secondDip.Left:0},{secondDip.Top:0} {secondDip.Width:0}×{secondDip.Height:0}");

            // ---------------- ② 依附形态 + 指定副屏 ----------------

            var attached = new ScadaRuntimeWindow(new ScadaRuntime(new ScadaDocument()));
            attached.ApplyRunWindowMode(ScadaRunWindowMode.AttachedToMainWindow, second);

            Check("依附形态 + 指定副屏：先挪到副屏再最大化（WPF 的最大化落在窗口当前所在的那块屏）",
                attached.WindowStyle == WindowStyle.None
                && attached.WindowState == WindowState.Maximized
                && attached.WindowStartupLocation == WindowStartupLocation.Manual
                && Math.Abs(attached.Left - secondDip.Left) < 0.5
                && Math.Abs(attached.Top - secondDip.Top) < 0.5,
                $"Left={attached.Left:0} Top={attached.Top:0} / 副屏工作区左上 {secondDip.Left:0},{secondDip.Top:0}");

            // ---------------- ③ 依附形态 + 主屏：一个字节都不设 ----------------

            var followPrimary = new ScadaRuntimeWindow(new ScadaRuntime(new ScadaDocument()));
            var primary = ScadaMonitors.Resolve("", out _);
            followPrimary.ApplyRunWindowMode(ScadaRunWindowMode.AttachedToMainWindow, primary);

            Check("依附形态 + 跟随主屏：不设 Left/Top，保持 XAML 的 CenterScreen（升级不动历史行为）",
                followPrimary.WindowStartupLocation == WindowStartupLocation.CenterScreen
                && double.IsNaN(followPrimary.Left)
                && double.IsNaN(followPrimary.Top)
                && followPrimary.WindowState == WindowState.Maximized,
                $"StartupLocation={followPrimary.WindowStartupLocation} / Left={followPrimary.Left} Top={followPrimary.Top}");
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
        /// 假的回写通道：把每一次写请求记下来（写的是哪个属性、交出去的原文是什么），
        /// 按预置结果回报成败。
        ///
        /// 图元只认 <see cref="IScadaValueWriter"/> 这一个方法（它够不着变量注册表，也不该够着），
        /// 所以断言工程能在控制台里把"操作员敲数 → 回车 → 写回变量"这条链整条走完；
        /// 真机那条链上换的是 <c>ScadaValueWriter</c>（转发到变量句柄），调用方一行不动。
        /// </summary>
        private sealed class FakeValueWriter : IScadaValueWriter
        {
            /// <summary>每一次写请求：目标属性键 + 交出去的文本（按发生顺序）</summary>
            public List<(string Target, string? Text)> Writes { get; } = new();

            /// <summary>预置的下一次失败原因（null = 写成功）。用来演"设备离线写不进"那一档。</summary>
            public string? FailReason { get; set; }

            public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
            {
                Writes.Add((targetProperty, text));

                error = FailReason;
                return FailReason == null;
            }
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
        /// 假的「切换画面」出口：不碰任何运行态会话，只把收到的目标记下来、按预置结果回报。
        ///
        /// 分发器只认 <see cref="IScadaNavigator"/> 这一个方法（它拿不到会话，也不该拿到），
        /// 所以断言工程能在控制台里把"切换画面"这条动作整条走完——与 <see cref="FakeVariablePicker"/>
        /// 同一个角色。真机那条链上换的是 <c>ScadaNavigator</c>（转发到运行态会话），调用方一行不动。
        /// </summary>
        private sealed class FakeNavigator : IScadaNavigator
        {
            /// <summary>预置的下一次回报原因；<c>null</c> = 切成功（语义见 <see cref="IScadaNavigator"/>）</summary>
            public string? FailReason { get; set; }

            /// <summary>被调了几次（用来证明"没选画面"那一档根本没走到导航出口）</summary>
            public int NavigateCalls { get; private set; }

            public Guid LastPageId { get; private set; }

            public string? LastPageName { get; private set; }

            public bool Navigate(Guid pageId, string? fallbackName, out string? reason)
            {
                NavigateCalls++;
                LastPageId = pageId;
                LastPageName = fallbackName;

                reason = FailReason;
                return FailReason == null;
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
        /// 假的「选画面」选择器：与 <see cref="FakeVariablePicker"/> 同构，只是清单来源不同
        /// （画面来自当前方案，变量来自工程变量表），所以断言里也另立一个替身——
        /// 合成一个"万能选择器"会让"面板到底把哪一类目标交出去了"这件事失去独立的证据。
        /// </summary>
        private sealed class FakePagePicker : IScadaPagePicker
        {
            /// <summary>下一次 Pick 要回传的画面 Id；Empty 表示"用户点了取消"（回调一次都不触发）</summary>
            public Guid NextId { get; set; }

            public string? NextName { get; set; }

            /// <summary>被点了几次「选择画面」</summary>
            public int PickCalls { get; private set; }

            /// <summary>最后一次收到的"当前目标画面"，用来核对预选参数确实是这条动作的现状</summary>
            public Guid LastCurrentId { get; private set; }

            public string? LastCurrentName { get; private set; }

            public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked)
            {
                PickCalls++;
                LastCurrentId = currentId;
                LastCurrentName = currentName;

                // 取消的语义是"回调一次都不触发"，不是"回传 Empty 让调用方自己判断"。
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

        // ==================================================================
        //  [AD] D3 统一写入口：BeginEdit 作用域 + 值级 diff + 撤销/重做栈
        // ==================================================================

        /// <summary>
        /// S9 的核心验收：把"改模型"收敛成一个入口（<see cref="ScadaPage.BeginEdit"/>），
        /// 撤销只是这个入口的副产品。本段盯四件事——
        /// ① 作用域 = 用户视角的一次操作（拖 10 个图元只占 1 条记录，不是 20 步）；
        /// ② 撤销/重做的回写不再产生新记录（否则越撤越多）；
        /// ③ 直接写模型在严格模式下被拒（把 D3 从"约定"变成"机制"）；
        /// ④ 撤销后<b>落盘内容</b>与操作前逐字节一致——<c>Version</c> 是 [JsonIgnore] 的
        ///    脏标记，语义是单调递增，它不回滚，所以验收改用序列化比对。
        ///
        /// <b>两个静态状态必须先备份再还原</b>：<see cref="ScadaEditHistory"/> 是全局单栈、
        /// <see cref="ScadaWriteGuard.Strict"/> 是线程静态开关（断言全程跑在 Main 线程）。
        /// 任一残留都会污染后面的断言段，所以进来先清、出去必还原。
        /// </summary>
        private static void EditHistoryChecks()
        {
            Section("[AD] 统一写入口（BeginEdit 作用域）与撤销/重做栈");

            ScadaEditHistory.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            try
            {
                // ----------------------------------------------------------
                // ① 采集点靠"有没有活动作用域"判定：作用域外的写不压栈。
                //    反序列化、运行时数据泵、断言夹具都靠这条保持安静。
                // ----------------------------------------------------------
                var page = new ScadaPage();
                var element = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "矩形1" });
                page.Elements.Add(element);

                ScadaEditHistory.Clear();
                element.X = 50; // 裸写：既没有作用域可记，也不该压栈
                Check("作用域外的写不进撤销栈（没有活动作用域就没有记录落点）",
                    ScadaEditHistory.UndoCount == 0, $"UndoCount={ScadaEditHistory.UndoCount}");

                // ----------------------------------------------------------
                // ② 一次 BeginEdit = 用户视角的一次操作 = 一条记录
                // ----------------------------------------------------------
                using (page.BeginEdit("移动图元"))
                {
                    element.X = 111;
                    element.Y = 222;
                }

                Check("一次 BeginEdit 内的多次属性写合并成一条记录",
                    ScadaEditHistory.UndoCount == 1, $"UndoCount={ScadaEditHistory.UndoCount}");
                var labelAfterEdit = ScadaEditHistory.NextUndoLabel;
                Check("记录名就是 BeginEdit 传入的操作名（撤销按钮据此显示提示）",
                    labelAfterEdit == "移动图元", labelAfterEdit ?? "null");

                // ③ 撤销 / 重做：一份 Apply 逻辑，两个方向
                var undone = ScadaEditHistory.Undo();
                Check("撤销一次把整条记录里的写全部回退（X 回 50、Y 回 0）",
                    undone && element.X == 50 && element.Y == 0, $"X={element.X} / Y={element.Y}");

                var redone = ScadaEditHistory.Redo();
                Check("重做一次把新值原样写回（撤销/重做共用同一份回写逻辑）",
                    redone && element.X == 111 && element.Y == 222, $"X={element.X} / Y={element.Y}");

                // ----------------------------------------------------------
                // ④ 零改动不产记录：点一下没真改，不该占一次撤销位
                // ----------------------------------------------------------
                var baseline = ScadaEditHistory.UndoCount;
                using (page.BeginEdit("点了一下但没改"))
                {
                }

                Check("空作用域不产记录（撤销栈里不该堆满'什么都没做'）",
                    ScadaEditHistory.UndoCount == baseline, $"UndoCount={ScadaEditHistory.UndoCount}");

                using (var idle = page.BeginEdit("写回原值"))
                {
                    element.X = 111; // 与当前值相同：SetProperty 同值短路，采集点根本到不了
                    Check("作用域 HasChanges 只在真有改动时为 true", !idle.HasChanges,
                        $"HasChanges={idle.HasChanges}");
                }

                Check("同值写被 SetProperty 短路，同样不产记录",
                    ScadaEditHistory.UndoCount == baseline, $"UndoCount={ScadaEditHistory.UndoCount}");

                // ----------------------------------------------------------
                // ⑤ 路线图核心验收：拖 10 个图元 → Ctrl+Z 一次全部回去
                //    （批量操作必须是一次撤销，不能拆成 20 步）
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var batchPage = new ScadaPage();
                var batch = new List<ScadaElement>();
                for (var i = 0; i < 10; i++)
                {
                    var ordinal = i + 1;
                    var item = ScadaChangeScope.Detached(
                        () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = $"图元{ordinal}" });
                    batchPage.Elements.Add(item);
                    batch.Add(item);
                }

                using (batchPage.BeginEdit("批量移动 10 个图元"))
                {
                    foreach (var item in batch)
                    {
                        item.X += 100;
                        item.Y += 50;
                    }
                }

                Check("拖 10 个图元只产生 1 条记录（批量操作 = 一次撤销）",
                    ScadaEditHistory.UndoCount == 1, $"UndoCount={ScadaEditHistory.UndoCount}");

                ScadaEditHistory.Undo();
                var batchXsAfterUndo = string.Join(",", batch.Select(b => b.X));
                Check("Ctrl+Z 一次，10 个图元全部回到原位",
                    batch.All(b => b.X == 0 && b.Y == 0), $"X=[{batchXsAfterUndo}]");

                ScadaEditHistory.Redo();
                var batchXsAfterRedo = string.Join(",", batch.Select(b => b.X));
                Check("重做一次，10 个图元全部回到新位",
                    batch.All(b => b.X == 100 && b.Y == 50), $"X=[{batchXsAfterRedo}]");

                // ----------------------------------------------------------
                // ⑥ 属性袋（Properties）走的是普通方法而不是属性 setter，
                //    反射找不到它，所以按键级单独记——这条必须有断言钉住
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var bagPage = new ScadaPage();
                var bagElement = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Label" });
                bagPage.Elements.Add(bagElement);

                using (bagPage.BeginEdit("改属性袋"))
                {
                    bagElement.SetProperty("Text", "运行中");
                }

                Check("属性袋的写也进撤销栈（键级 diff，独立于属性 setter）",
                    ScadaEditHistory.UndoCount == 1, $"UndoCount={ScadaEditHistory.UndoCount}");

                ScadaEditHistory.Undo();
                var bagValueAfterUndo = bagElement.GetProperty("Text");
                Check("撤销后属性袋里的键被整条删掉（空值 = 没配过，文件里不留空串）",
                    bagValueAfterUndo.Length == 0, $"'{bagValueAfterUndo}'");

                // ----------------------------------------------------------
                // ⑦ Version 是脏标记（缩略图缓存只认"变过"），语义是单调递增，撤销不回滚它
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var versionPage = new ScadaPage();
                var versionElement = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle" });
                versionPage.Elements.Add(versionElement);

                using (versionPage.BeginEdit("改尺寸"))
                {
                    versionElement.Width = 300;
                }

                var versionAfterEdit = versionPage.Version;
                ScadaEditHistory.Undo();
                Check("撤销把 Width 回退到描述符默认值",
                    Math.Abs(versionElement.Width - 120) < 0.001, $"Width={versionElement.Width}");
                Check("撤销不回滚 Version（单调递增；回滚它会让缩略图缓存以为'没变过'）",
                    versionPage.Version >= versionAfterEdit,
                    $"撤销前={versionAfterEdit} / 撤销后={versionPage.Version}");

                // ----------------------------------------------------------
                // ⑧ 路线图验收"版本号与磁盘内容一致" → 改用序列化比对。
                //    Version / DefaultLayer / EnableLoadedEvent 都标了 [JsonIgnore]，天然不参与。
                //    只序列化画面本身，避开 SolutionModel.solutionName 那类时间戳字段。
                // ----------------------------------------------------------
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.Auto,
                };

                ScadaEditHistory.Clear();
                var jsonPage = new ScadaPage();
                var jsonElement = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "矩形A" });
                jsonPage.Elements.Add(jsonElement);

                var jsonBefore = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                using (jsonPage.BeginEdit("移动并改名"))
                {
                    jsonElement.X = 300;
                    jsonElement.Y = 400;
                    jsonElement.Name = "矩形B";
                }

                var jsonAfterEdit = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                ScadaEditHistory.Undo();
                var jsonAfterUndo = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                Check("撤销后的落盘内容与操作前逐字节一致（这就是'版本号一致'的验收口径）",
                    jsonAfterUndo == jsonBefore, FirstDiff(jsonBefore, jsonAfterUndo));

                ScadaEditHistory.Redo();
                var jsonAfterRedo = JsonConvert.SerializeObject(jsonPage, jsonSettings);
                Check("重做后的落盘内容与操作后逐字节一致",
                    jsonAfterRedo == jsonAfterEdit, FirstDiff(jsonAfterEdit, jsonAfterRedo));

                Check("操作前后的落盘内容确实不同（证明上面两条不是恒真的空断言）",
                    jsonAfterEdit != jsonBefore, "");

                // ----------------------------------------------------------
                // ⑨ 嵌套自动合并：内层作用域不产记录，改动一律汇进最外层。
                //    这就是"批量对齐 10 个图元"只需外层包一次的实现基础。
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var nestPage = new ScadaPage();
                using (nestPage.BeginEdit("批量建图层"))
                {
                    nestPage.AddLayer(); // AddLayer 内部自开作用域
                    nestPage.AddLayer();
                }

                Check("嵌套作用域合并进最外层（内层自开的两个作用域不各占一条）",
                    ScadaEditHistory.UndoCount == 1, $"UndoCount={ScadaEditHistory.UndoCount}");
                var nestLabel = ScadaEditHistory.NextUndoLabel;
                Check("合并后的记录用最外层的操作名",
                    nestLabel == "批量建图层", nestLabel ?? "null");

                ScadaEditHistory.Undo();
                Check("撤销一次把嵌套里建的图层全部回退",
                    nestPage.Layers.Count == 0, $"图层数={nestPage.Layers.Count}");

                // ----------------------------------------------------------
                // ⑩ 单栈双向纪律：新分支一旦产生，原来的重做路径就不再成立
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var branchPage = new ScadaPage();
                var branchElement = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle" });
                branchPage.Elements.Add(branchElement);

                using (branchPage.BeginEdit("第一步")) branchElement.X = 1;
                using (branchPage.BeginEdit("第二步")) branchElement.Y = 1;

                ScadaEditHistory.Undo();
                Check("撤销后重做栈里有 1 条",
                    ScadaEditHistory.RedoCount == 1, $"RedoCount={ScadaEditHistory.RedoCount}");

                using (branchPage.BeginEdit("另起一步")) branchElement.Width = 200;
                Check("新操作压栈即清空重做栈（单栈双向的核心纪律）",
                    ScadaEditHistory.RedoCount == 0 && !ScadaEditHistory.CanRedo,
                    $"RedoCount={ScadaEditHistory.RedoCount}");

                // ----------------------------------------------------------
                // ⑪ 栈空返回 false：调用方据此置灰按钮 / 响铃
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                Check("栈空时 Undo() 返回 false 且 CanUndo 为假",
                    !ScadaEditHistory.Undo() && !ScadaEditHistory.CanUndo, "");
                Check("栈空时 Redo() 返回 false 且 CanRedo 为假",
                    !ScadaEditHistory.Redo() && !ScadaEditHistory.CanRedo, "");
                Check("栈空时两个 Label 都是 null（按钮提示回落成'没有可撤销的操作'）",
                    ScadaEditHistory.NextUndoLabel == null && ScadaEditHistory.NextRedoLabel == null, "");

                // ----------------------------------------------------------
                // ⑫ 容量封顶：撤销栈不无界增长（对齐 Flow 侧的 MaxUndoStack）
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var depthPage = new ScadaPage();
                var depthElement = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle" });
                depthPage.Elements.Add(depthElement);

                var steps = ScadaEditHistory.MaxDepth + 5;
                for (var i = 1; i <= steps; i++)
                {
                    using (depthPage.BeginEdit($"第 {i} 步"))
                    {
                        depthElement.X = i;
                    }
                }

                Check($"撤销栈封顶 {ScadaEditHistory.MaxDepth} 条（超出丢最早的一条）",
                    ScadaEditHistory.UndoCount == ScadaEditHistory.MaxDepth,
                    $"UndoCount={ScadaEditHistory.UndoCount}");
                var topLabel = ScadaEditHistory.NextUndoLabel;
                Check("栈顶始终是最新一步（丢的是最早的，不是最新的）",
                    topLabel == $"第 {steps} 步", topLabel ?? "null");

                // ----------------------------------------------------------
                // ⑬ Detached 的语义：构造 ≠ 编辑（这条分界线必须有断言，
                //    否则"新建图层"会先往撤销栈里塞几条 Name: "" → "图层_1" 的垃圾）
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var detachedPage = new ScadaPage();
                using (detachedPage.BeginEdit("作用域内只构造不入库"))
                {
                    ScadaChangeScope.Detached(() => new ScadaLayer { Name = "临时图层" });
                }

                Check("作用域内用 Detached 构造对象不产记录（构造不是编辑）",
                    ScadaEditHistory.UndoCount == 0, $"UndoCount={ScadaEditHistory.UndoCount}");

                // ----------------------------------------------------------
                // ⑭ 严格模式：把"编辑器必须走入口"从约定变成机制。
                //    在 Strict=true 下跑一遍编辑器真实写路径，跑通即证明
                //    这些路径确实全在作用域内（新加的"一键对齐"之类漏包会在这里炸）
                // ----------------------------------------------------------
                ScadaWriteGuard.Strict = true;

                var strictPage = new ScadaPage();
                var strictFailure = string.Empty;

                try
                {
                    using (strictPage.BeginEdit("严格模式：放置图元"))
                    {
                        var strictLayerNew = strictPage.AddLayer("严格图层");
                        var placed = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 20);
                        placed.Name = "严格图元";
                        placed.LayerId = strictLayerNew.LayerId;
                        strictPage.Elements.Add(placed);
                        placed.AddBinding("Value");
                    }

                    var strictElement = strictPage.Elements[0];
                    var strictLayer = strictPage.Layers[0];

                    // Try* 家族各自内部开作用域，调用方不需要再包一层
                    strictPage.TryRenameLayer(strictLayer, "严格图层_改", out _);
                    strictPage.TryAssignLayer(strictElement, strictLayer, out _);
                    strictPage.TryMoveElementZ(strictElement, ScadaZMove.ToFront, out _);
                    // 锁定/解锁也走这一条：属性面板顶栏那个复选框就从这条路过来了
                    //（它此前直绑 Element.IsLocked，是一条绕开 BeginEdit 的裸 setter，Strict 下会被当场拒掉）
                    strictPage.TrySetElementLocked(new[] { strictElement }, true, out _);
                    strictPage.TrySetElementLocked(new[] { strictElement }, false, out _);
                    strictPage.TryRemoveBinding(strictElement, "Value", out _);
                    strictPage.TryRemoveElement(strictElement, out _);
                    strictPage.AddLayer("严格图层2");
                }
                catch (Exception ex)
                {
                    strictFailure = ex.Message;
                }

                Check("Strict=true 下编辑器写路径全程不触发写守卫（D3 从约定变机制）",
                    strictFailure.Length == 0, strictFailure);

                // ----------------------------------------------------------
                // ⑮ 严格模式下：作用域外的裸写被拒；Suspend 放行；Detached 放行
                //
                //    边界（知情项）：守卫只覆盖"标量属性 setter"这一条路径。
                //    集合增删（Elements.Add 之类）在 CollectionChanged 回调里落账，
                //    事件到达时集合已经改完，此处再抛异常只会留下"改了但报了错"的半改状态，
                //    所以集合路径刻意不加守卫——下面这行裸 Add 在 Strict 下不报错是<b>设计如此</b>。
                // ----------------------------------------------------------
                var guardElement = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "守卫测试" });
                strictPage.Elements.Add(guardElement);

                var rejected = false;
                try
                {
                    guardElement.X = 5;
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }

                Check("Strict=true 下作用域外的直接 setter 被拒（编辑器路径一律禁止裸写）",
                    rejected && guardElement.X == 0, $"rejected={rejected} / X={guardElement.X}");

                var detachedOk = false;
                try
                {
                    var temp = ScadaChangeScope.Detached(() => new ScadaLayer { Name = "临时", IsVisible = false });
                    detachedOk = temp.Name == "临时" && !temp.IsVisible;
                }
                catch (InvalidOperationException)
                {
                }

                Check("Detached 下构造对象不被守卫拦（构造与编辑是两件事）", detachedOk, "");

                using (ScadaWriteGuard.Suspend())
                {
                    guardElement.X = 7;
                }

                Check("using (ScadaWriteGuard.Suspend()) 下放行（运行时数据泵 / 反序列化通道）",
                    guardElement.X == 7, $"X={guardElement.X}");

                ScadaWriteGuard.Strict = false;

                // ----------------------------------------------------------
                // ⑯ 旧数据自愈不进撤销栈：用户 Ctrl+Z 不该把补发的身份撤掉
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                var legacyPage = new ScadaPage { PageId = Guid.Empty, Name = "旧画面" };
                var legacyElement = ScadaChangeScope.Detached(
                    () => new ScadaElement { ElementId = Guid.Empty, TypeKey = "Hmi.Rectangle" });
                legacyPage.Elements.Add(legacyElement);

                var repaired = legacyPage.EnsureIdentity();
                Check("旧数据补发身份（画面 + 图层 + 图元 + 归属）",
                    repaired > 0 && legacyPage.PageId != Guid.Empty && legacyElement.ElementId != Guid.Empty,
                    $"repaired={repaired}");
                Check("补发身份不进撤销栈（自愈不是用户的编辑动作）",
                    ScadaEditHistory.UndoCount == 0, $"UndoCount={ScadaEditHistory.UndoCount}");

                // ----------------------------------------------------------
                // ⑰ 静态单栈的隔离语义：换文档必须清栈，
                //    否则新文档的 Ctrl+Z 会撤到旧文档的对象上（最难反查的一种崩法）
                // ----------------------------------------------------------
                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solutionA = new SolutionModel();
                solutionA.Flows.Clear();
                workspace.SwitchSolution(solutionA);

                var editor = new ScadaEditorVM(workspace, null!);
                try
                {
                    editor.AddPageCommand.Execute();
                    var editorPage = editor.SelectedPage!;
                    var editorElement = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle" });
                    editorPage.Elements.Add(editorElement);

                    using (editorPage.BeginEdit("编辑器路径改一下"))
                    {
                        editorElement.X = 9;
                    }

                    Check("编辑器写路径产出的记录进了静态撤销栈",
                        ScadaEditHistory.UndoCount >= 1, $"UndoCount={ScadaEditHistory.UndoCount}");

                    var solutionB = new SolutionModel();
                    solutionB.Flows.Clear();
                    workspace.SwitchSolution(solutionB);

                    Check("切换方案（换文档实例）清空撤销栈",
                        ScadaEditHistory.UndoCount == 0 && ScadaEditHistory.RedoCount == 0,
                        $"UndoCount={ScadaEditHistory.UndoCount} / RedoCount={ScadaEditHistory.RedoCount}");
                }
                finally
                {
                    editor.Deactivate();
                }
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [NU] 方向键微调：S9「拖动 + Ctrl+Z」真机欠债的机制等价物
        // ==================================================================

        /// <summary>
        /// 方向键微调（<see cref="ScadaEditorVM.NudgeCommand"/>）就是"用键盘做拖动"的那条路：
        /// 与鼠标拖动共用同一个写入口（<c>BeginEdit</c> → 模型侧改 <c>X</c>/<c>Y</c>）与同一条撤销链，
        /// 区别只在"位移量谁算"——鼠标按像素、方向键按步长。
        ///
        /// 为什么要单独钉它：S9 的真机验收里"拖动 + <c>Ctrl+Z</c>/<c>Ctrl+Y</c> + 关闭重开"三项
        /// 因现场屏幕无输出、截不了图而走不成真机。本段把其中<b>能用机制证明的部分</b>钉死：
        /// ① 方向表；② 每次按键一条撤销位（预测性优先，不按时间合并）；
        /// ③ 连按 N 次、撤 N 次<b>精确回原位</b>——这就是"拖动之后按 Ctrl+Z"的机制等价；
        /// ④ 锁定图元不动（口径与拖动一致）；⑤ 严格模式下放行（证明确实走正规写入口）。
        ///
        /// 样板与 [AE]/[AF] 一致：要挂真编辑器（<c>ScadaEditorViewModel</c>，DispatcherObject 血统），
        /// 单开 STA 线程，不把 <c>Main</c> 标成 STAThread 去改 S0/S1 的运行环境。
        /// </summary>
        private static void NudgeChecks()
        {
            Section("[NU] 方向键微调：方向表 / 每键一条撤销位 / 撤销回原位 / 锁定不动 / 严格模式放行");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunNudgeChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("方向键微调断言全程未抛异常", false, failure.ToString());
        }

        private static void RunNudgeChecks()
        {
            // 撤销栈是全局静态的（单栈双向），本段自己压栈、出段必清
            ScadaEditHistory.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            try
            {
                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                workspace.SwitchSolution(solution);

                var editor = new ScadaEditorVM(workspace, null!);

                try
                {
                    editor.AddPageCommand.Execute();
                    var page = editor.SelectedPage!;

                    // ----------------------------------------------------------
                    // ① 门控：空选 / 方向不合法 —— 命令必须是灰的
                    // ----------------------------------------------------------
                    editor.SelectedElement = null;
                    ScadaEditHistory.Clear();

                    Check("没有选中图元时四个方向都不可执行（方向键不该在空选下亮着）",
                        !editor.NudgeCommand.CanExecute("Left") && !editor.NudgeCommand.CanExecute("Right")
                        && !editor.NudgeCommand.CanExecute("Up") && !editor.NudgeCommand.CanExecute("Down"),
                        "");

                    // 大小写也算"不合法"：KeyBinding 给的是常量 "Left"，用户输入永远进不来，
                    // 所以这里宁可灰着，也不要"大小写写错就静默当成不动"。
                    Check("方向表以外的参数一律不可执行（XAML 里写错字时宁可灰着，也不该走一遍空作用域）",
                        !editor.NudgeCommand.CanExecute("左上") && !editor.NudgeCommand.CanExecute("")
                        && !editor.NudgeCommand.CanExecute(null!) && !editor.NudgeCommand.CanExecute("left"),
                        "");

                    // ----------------------------------------------------------
                    // ② 落子：走编辑器真正的放置入口（与鼠标拖放落下同一条路）
                    // ----------------------------------------------------------
                    var placed = editor.AddElement("Hmi.Rectangle", new Point(100, 200));
                    if (placed == null)
                    {
                        Check("前提：Hmi.Rectangle 已注册且落子成功（否则本段全部无意义）",
                            false, "AddElement 返回 null");
                        return;
                    }

                    ScadaElement target = placed;

                    Check("放下之后自动选中，四个方向立刻都可执行",
                        ReferenceEquals(editor.SelectedElement, target)
                        && editor.NudgeCommand.CanExecute("Left") && editor.NudgeCommand.CanExecute("Right")
                        && editor.NudgeCommand.CanExecute("Up") && editor.NudgeCommand.CanExecute("Down"),
                        editor.SelectedElement?.Name ?? "null");

                    // ----------------------------------------------------------
                    // ③ 方向表：四个方向各挪一次，量完位移立刻撤回原位
                    //
                    // 为什么量完就撤：四个方向要"从同一处起步"才可比；顺带把"撤销能逐字还原"
                    // 这件事在每一步都顺路验一遍，后面第 ④ 组才不是孤证。
                    //
                    // 步长为什么不写死 1：OnNudge 读的是 Keyboard.Modifiers（按住 Shift 走粗调 10），
                    // 那是进程级真实键盘状态，断言无法注入。所以这里钉"方向 + 另一轴不动 + 步长
                    // 只可能是 1 或 10 且四次一致"，把不确定的那一维留给机器，其余全部钉死。
                    // ----------------------------------------------------------
                    (double dx, double dy) NudgeOnce(string direction)
                    {
                        double x0 = target.X, y0 = target.Y;
                        editor.NudgeCommand.Execute(direction);
                        var delta = (target.X - x0, target.Y - y0);
                        editor.UndoCommand.Execute();
                        ScadaEditHistory.Clear();
                        return delta;
                    }

                    var dLeft = NudgeOnce("Left");
                    double step = Math.Abs(dLeft.dx);

                    Check("Left：X 减一个步长、Y 一动不动（方向表查错的表现是「按左往右跑」）",
                        dLeft.dx < 0 && dLeft.dy == 0 && (step == 1 || step == 10),
                        $"Δ=({dLeft.dx},{dLeft.dy})");

                    var dRight = NudgeOnce("Right");
                    Check("Right：X 加一个步长、Y 一动不动，且步长与 Left 一致",
                        dRight.dx > 0 && dRight.dy == 0 && Math.Abs(dRight.dx) == step,
                        $"Δ=({dRight.dx},{dRight.dy})");

                    var dUp = NudgeOnce("Up");
                    Check("Up：Y 减一个步长、X 一动不动（X/Y 互换是这张表最容易抄错的一处）",
                        dUp.dy < 0 && dUp.dx == 0 && Math.Abs(dUp.dy) == step,
                        $"Δ=({dUp.dx},{dUp.dy})");

                    var dDown = NudgeOnce("Down");
                    Check("Down：Y 加一个步长、X 一动不动",
                        dDown.dy > 0 && dDown.dx == 0 && Math.Abs(dDown.dy) == step,
                        $"Δ=({dDown.dx},{dDown.dy})");

                    // ----------------------------------------------------------
                    // ④ 连按 5 次 = 5 条撤销位，撤 5 次精确回原位
                    // ----------------------------------------------------------
                    double originX = target.X, originY = target.Y;
                    ScadaEditHistory.Clear();

                    for (int i = 0; i < 5; i++)
                        editor.NudgeCommand.Execute("Left");

                    Check("连按 5 次 Left：位移正好 5 个步长（每次按键都真的落了笔，没被吸附/合并吃掉）",
                        target.X == originX - 5 * step && target.Y == originY,
                        $"X={target.X}（期望 {originX - 5 * step}）/ Y={target.Y}");

                    Check("连按 5 次留下 5 条撤销位（预测性优先：按了 5 次就知道要撤 5 次，不按时间合并）",
                        ScadaEditHistory.UndoCount == 5, $"UndoCount={ScadaEditHistory.UndoCount}");

                    Check("撤销文案读得出撤的是哪一步（不是光秃秃的「撤销」两个字）",
                        ScadaEditHistory.NextUndoLabel == "微调图元位置",
                        ScadaEditHistory.NextUndoLabel ?? "null");

                    for (int i = 0; i < 5; i++)
                        editor.UndoCommand.Execute();

                    // 这一条就是 S9 真机欠债里"拖动 + Ctrl+Z"的机制等价：
                    // 写入走 BeginEdit 进静态栈、撤销走同一条链回写，位置逐字还原。
                    Check("撤 5 次精确回到原位——「拖动之后 Ctrl+Z」的机制等价（同一条撤销链、逐字还原）",
                        target.X == originX && target.Y == originY && ScadaEditHistory.UndoCount == 0,
                        $"X={target.X} / Y={target.Y} / UndoCount={ScadaEditHistory.UndoCount}");

                    for (int i = 0; i < 5; i++)
                        editor.RedoCommand.Execute();

                    Check("再重做 5 次回到按完的位置（Ctrl+Y 与 Ctrl+Z 共用同一份回写逻辑）",
                        target.X == originX - 5 * step && target.Y == originY,
                        $"X={target.X}（期望 {originX - 5 * step}）");

                    // ----------------------------------------------------------
                    // ⑤ 多选：整组一起挪，且只留一条撤销位
                    // ----------------------------------------------------------
                    var a = editor.AddElement("Hmi.Rectangle", new Point(10, 10))!;
                    var b = editor.AddElement("Hmi.Rectangle", new Point(20, 20))!;
                    var c = editor.AddElement("Hmi.Rectangle", new Point(30, 30))!;

                    editor.SelectedElements = new[] { a, b, c };
                    ScadaEditHistory.Clear();

                    editor.NudgeCommand.Execute("Down");

                    Check("多选三个整组一起挪（只挪主选中那一个，用户会以为方向键卡了）",
                        a.Y == 10 + step && b.Y == 20 + step && c.Y == 30 + step,
                        $"{a.Y}/{b.Y}/{c.Y}");

                    Check("整组只留一条撤销位，操作名带数量（一次 Ctrl+Z 整组回去）",
                        ScadaEditHistory.UndoCount == 1
                        && ScadaEditHistory.NextUndoLabel == "微调 3 个图元位置",
                        $"{ScadaEditHistory.UndoCount} / {ScadaEditHistory.NextUndoLabel ?? "null"}");

                    editor.UndoCommand.Execute();

                    Check("一次 Ctrl+Z，三个一起回原位（不是撤三次）",
                        a.Y == 10 && b.Y == 20 && c.Y == 30, $"{a.Y}/{b.Y}/{c.Y}");

                    // ----------------------------------------------------------
                    // ⑥ 锁定：不挪，且判灰 —— 口径与鼠标拖动一致
                    // ----------------------------------------------------------
                    bool lockedOk = page.TrySetElementLocked(new[] { b }, true, out _);
                    editor.SelectedElements = new[] { a, b };
                    ScadaEditHistory.Clear();

                    editor.NudgeCommand.Execute("Right");

                    Check("混选里锁定的那个不跟着挪（口径与鼠标拖动一致：锁 = 不可编辑，跳过而不是整批失败）",
                        lockedOk && a.X == 10 + step && b.X == 20, $"a.X={a.X} / b.X={b.X}");

                    editor.SelectedElements = new[] { b };

                    Check("只选中锁定图元时方向键判灰（不是「亮着、按下去什么也不发生」）",
                        !editor.NudgeCommand.CanExecute("Left"),
                        $"CanExecute={editor.NudgeCommand.CanExecute("Left")}");

                    // ----------------------------------------------------------
                    // ⑦ 严格模式放行：证明确实走 BeginEdit 正规写入口，不是裸写 setter
                    //
                    // 这条是 D3 的"机器检查"：ScadaWriteGuard.Strict 一开，任何绕过
                    // Try*/BeginEdit 的裸写都会抛。方向键在这里照常放行 + 恰好一条记录，
                    // 等于同时证明了"走了作用域"和"没有多压栈"。
                    // ----------------------------------------------------------
                    ScadaWriteGuard.Strict = true;

                    try
                    {
                        editor.SelectedElements = new[] { c };
                        ScadaEditHistory.Clear();

                        double beforeX = c.X;
                        Exception? writeFailure = null;

                        try { editor.NudgeCommand.Execute("Right"); }
                        catch (Exception ex) { writeFailure = ex; }

                        Check("严格模式（ScadaWriteGuard.Strict）下方向键照常放行 + 恰好一条记录"
                            + "——证明它走的是 BeginEdit 正规写入口，不是裸写 setter",
                            writeFailure == null && c.X == beforeX + step && ScadaEditHistory.UndoCount == 1,
                            $"ex={writeFailure?.GetType().Name ?? "无"} / ΔX={c.X - beforeX}"
                            + $" / UndoCount={ScadaEditHistory.UndoCount}");
                    }
                    finally
                    {
                        ScadaWriteGuard.Strict = strictBackup;
                    }

                    // ----------------------------------------------------------
                    // ⑧ 热键的用户可见证据：按钮文案里就写着 Ctrl+Z / Ctrl+Y
                    //
                    // 与 ScadaEditorView.xaml 的 KeyBinding 同口径。真机热键本身需要键盘输入
                    // （本机屏幕无输出走不成），这里钉住"文案与绑定说的是同一组键"这一半。
                    // ----------------------------------------------------------
                    Check("撤销/重做文案里带出 Ctrl+Z / Ctrl+Y（热键从界面就读得到，不用翻手册）",
                        editor.UndoLabel.Contains("Ctrl+Z") && editor.RedoLabel.Contains("Ctrl+Y"),
                        $"{editor.UndoLabel} / {editor.RedoLabel}");
                }
                finally
                {
                    editor.Deactivate();
                }
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [AE] 图层面板：行快照 / 值不变挡刷 / 端点判灰 / 显隐锁定各一条撤销位
        // ==================================================================

        /// <summary>
        /// 这一段要挂真编辑器（<c>ScadaEditorViewModel</c>）并走图元注册表，
        /// 样板与 [U] 一致：单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
        /// </summary>
        private static void LayerPanelChecks()
        {
            Section("[AE] 图层面板：行快照 / 值不变挡刷 / 端点判灰 / 显隐锁定一条撤销位");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunLayerPanelChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("图层面板断言全程未抛异常", false, failure.ToString());
        }

        private static void RunLayerPanelChecks()
        {
            // 撤销栈是全局静态的（单栈双向），本段自己压栈、出段必清
            ScadaEditHistory.Clear();

            try
            {
                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                workspace.SwitchSolution(solution);

                var editor = new ScadaEditorVM(workspace, null!);

                try
                {
                    var panel = new ScadaLayerVM(editor);

                    try
                    {
                        // ------------------------------------------------
                        // ① 没有画面：空态两句话里的第一句
                        // ------------------------------------------------
                        Check("没有画面：HasPage 为假、行表为空、标题回落成不带画面名的「图层」",
                            !panel.HasPage && panel.Items.Count == 0 && panel.HeaderText == "图层",
                            panel.HeaderText);
                        Check("没有画面：空态提示指向「打开方案并选中画面」（这是用户要做对的那一步）",
                            panel.EmptyHint.Contains("打开方案"), panel.EmptyHint);
                        Check("没有画面：新建图层按钮置灰（可用性就是 _page != null 一条判据）",
                            !panel.AddLayerCommand.CanExecute(), "");
                        Check("没有画面：计数文案是「共 0 层」而不是空白",
                            panel.LayerCountText == "共 0 层", panel.LayerCountText);
                        Check("没有画面：未分层计数为 0（没有画面就没有「未分层」这回事）",
                            panel.UnassignedCount == 0 && !panel.HasUnassigned, "");

                        // ------------------------------------------------
                        // ② 新建画面 → 面板自动跟上（靠 SelectedPage 通知，不靠外部喊一声）
                        // ------------------------------------------------
                        editor.AddPageCommand.Execute();
                        var page = editor.SelectedPage!;
                        var layerA = page.Layers[0];

                        Check("新建画面后面板自动跟上（订阅 SelectedPage，不需要外部推动）",
                            panel.HasPage && panel.Items.Count == 1 && panel.HeaderText == $"图层 · {page.Name}",
                            panel.HeaderText);
                        Check("有画面后新建图层按钮转为可用",
                            panel.AddLayerCommand.CanExecute(), "");

                        var rowA = panel.Items[0];
                        Check("行快照与模型对齐：空层数 0、文案写「空」、只剩一层不能删",
                            rowA.ElementCount == 0 && rowA.ElementCountText == "空"
                            && !rowA.CanRemove && ReferenceEquals(rowA.Layer, layerA),
                            $"{rowA.ElementCountText} / CanRemove={rowA.CanRemove}");
                        Check("单层时上移下移都判灰（端点即越界，判据只有下标一条）",
                            !rowA.CanMoveUp && !rowA.CanMoveDown
                            && !panel.MoveLayerUpCommand.CanExecute(rowA)
                            && !panel.MoveLayerDownCommand.CanExecute(rowA), "");
                        Check("行上的图标与提示取自图层状态：可见=睁眼、未锁=开锁",
                            rowA.VisibilityIcon == ScadaLayerItem.EyeGlyph
                            && rowA.LockIcon == ScadaLayerItem.UnlockGlyph
                            && rowA.VisibilityTip.Contains("隐藏")
                            && rowA.LockTip.Contains("锁定"),
                            $"{rowA.VisibilityTip} / {rowA.LockTip}");

                        // ------------------------------------------------
                        // ③ 加第二层：端点判灰换边
                        // ------------------------------------------------
                        page.AddLayer("标注层");

                        Check("画面加一层，面板行表跟着长（Version 一条订阅覆盖图层增删）",
                            panel.Items.Count == 2 && panel.LayerCountText == "共 2 层",
                            panel.LayerCountText);

                        var row0 = panel.Items[0];
                        var row1 = panel.Items[1];
                        Check("列表首项不能上移能下移、末项反之（端点判灰按列表位置算）",
                            !row0.CanMoveUp && row0.CanMoveDown && row1.CanMoveUp && !row1.CanMoveDown,
                            $"[0]↑{row0.CanMoveUp}/↓{row0.CanMoveDown}  [1]↑{row1.CanMoveUp}/↓{row1.CanMoveDown}");
                        Check("端点判灰与命令可用性同源（不在两处各判一遍）",
                            !panel.MoveLayerUpCommand.CanExecute(row0)
                            && panel.MoveLayerUpCommand.CanExecute(row1)
                            && !panel.MoveLayerDownCommand.CanExecute(row1), "");

                        // ------------------------------------------------
                        // ④ 能删的唯一前提：空层 + 画面还剩别的层
                        // ------------------------------------------------
                        Check("两层都空：两行都可删（空层 + 至少还剩一层）",
                            row0.CanRemove && row1.CanRemove && panel.RemoveLayerCommand.CanExecute(row0), "");

                        var boxedElement = ScadaChangeScope.Detached(
                            () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "料箱", LayerId = layerA.LayerId });
                        page.Elements.Add(boxedElement);

                        Check("层里放了图元：该行立刻变成不可删，另一行不受影响",
                            !panel.Items[0].CanRemove && panel.Items[1].CanRemove
                            && !panel.RemoveLayerCommand.CanExecute(panel.Items[0]),
                            $"Count={panel.Items[0].ElementCount}");
                        Check("图元数文案从「空」变成「1 个图元」",
                            panel.Items[0].ElementCountText == "1 个图元", panel.Items[0].ElementCountText);

                        // ------------------------------------------------
                        // ⑤ 拖动逐帧抬 Version，但行快照值没变 → 列表不重建
                        // ------------------------------------------------
                        int itemsRaised = 0;
                        int canExecuteRaised = 0;

                        void OnPanelChanged(object? s, PropertyChangedEventArgs e)
                        {
                            if (e.PropertyName == nameof(ScadaLayerVM.Items)) itemsRaised++;
                        }

                        void OnCanExecuteChanged(object? s, EventArgs e) => canExecuteRaised++;

                        panel.PropertyChanged += OnPanelChanged;
                        panel.MoveLayerUpCommand.CanExecuteChanged += OnCanExecuteChanged;

                        var versionBefore = page.Version;
                        boxedElement.X += 10; // 模拟拖动中的一帧

                        Check("改图元属性会抬画面 Version（脏标记的递增规则覆盖图元属性写）",
                            page.Version > versionBefore, $"{versionBefore} → {page.Version}");
                        Check("行快照值没变就不通知 Items（拖动不会逐帧重建行容器）",
                            itemsRaised == 0, $"Items 通知 {itemsRaised} 次");
                        Check("挡刷只挡列表、不挡按钮：命令可用性照样重算",
                            canExecuteRaised > 0, $"CanExecuteChanged {canExecuteRaised} 次");

                        panel.PropertyChanged -= OnPanelChanged;
                        panel.MoveLayerUpCommand.CanExecuteChanged -= OnCanExecuteChanged;

                        // ------------------------------------------------
                        // ⑥ 行高亮跟着主选中走
                        // ------------------------------------------------
                        editor.SelectedElement = boxedElement;
                        Check("选中图元落在本层：只有该行高亮（省得用户自己在列表里找）",
                            panel.Items[0].ContainsSelection && !panel.Items[1].ContainsSelection, "");

                        editor.SelectedElement = null;
                        Check("取消选中：所有行都不高亮",
                            panel.Items.All(i => !i.ContainsSelection), "");

                        var stray = ScadaChangeScope.Detached(
                            () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "流浪" });
                        page.Elements.Add(stray); // LayerId 默认 Guid.Empty

                        editor.SelectedElement = stray;
                        Check("选中未分层图元：没有任何一行高亮（Guid.Empty 不代表任何一层）",
                            panel.Items.All(i => !i.ContainsSelection), "");

                        // ------------------------------------------------
                        // ⑦ 未分层计数：Guid.Empty 与「归属指向已删图层」都算
                        // ------------------------------------------------
                        Check("未分层图元计入 UnassignedCount 并给出人话提示",
                            panel.UnassignedCount == 1 && panel.HasUnassigned
                            && panel.UnassignedHint.Contains("1"),
                            panel.UnassignedHint);

                        var doomedLayer = page.AddLayer("待删层");
                        var doomedId = doomedLayer.LayerId;
                        page.TryRemoveLayer(doomedLayer, out _); // 空层才允许删

                        var ghost = ScadaChangeScope.Detached(
                            () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "幽灵", LayerId = doomedId });
                        page.Elements.Add(ghost);

                        Check("归属指向已删图层的图元同样计入未分层（ResolveLayer 为 null）",
                            panel.UnassignedCount == 2, $"UnassignedCount={panel.UnassignedCount}");

                        editor.SelectedElement = ghost;
                        Check("选中幽灵图元也不高亮任何一行（指向已删图层的归属不算归属）",
                            panel.Items.All(i => !i.ContainsSelection), "");

                        // ------------------------------------------------
                        // ⑧ 显隐/锁定：一次点按 = 一条撤销位，且 Ctrl+Z 能回去
                        // ------------------------------------------------
                        editor.SelectedElement = null;
                        ScadaEditHistory.Clear();

                        var eyeTarget = panel.Items[1]; // 标注层
                        panel.ToggleVisibleCommand.Execute(eyeTarget);

                        Check("眼睛点一下：图层真隐藏了（写的是模型上的 IsVisible）",
                            !eyeTarget.Layer.IsVisible, $"IsVisible={eyeTarget.Layer.IsVisible}");
                        Check("显隐产出一条撤销记录，操作名带图层名（撤销按钮据此显示）",
                            ScadaEditHistory.UndoCount == 1
                            && ScadaEditHistory.NextUndoLabel == $"隐藏图层 [{eyeTarget.Layer.Name}]",
                            ScadaEditHistory.NextUndoLabel ?? "null");
                        Check("行快照跟着翻转：图标变斜杠眼、提示变成「显示图层」",
                            panel.Items[1].VisibilityIcon == ScadaLayerItem.EyeSlashGlyph
                            && panel.Items[1].VisibilityTip.Contains("显示"),
                            panel.Items[1].VisibilityTip);

                        ScadaEditHistory.Undo();
                        Check("Ctrl+Z 把显隐状态撤回去（图层状态走的是同一条值级 diff 通道）",
                            eyeTarget.Layer.IsVisible, $"IsVisible={eyeTarget.Layer.IsVisible}");

                        ScadaEditHistory.Clear();
                        var lockTarget = panel.Items[1];
                        panel.ToggleLockCommand.Execute(lockTarget);

                        Check("锁图标点一下：图层真锁上了（同样绕不过 BeginEdit）",
                            lockTarget.Layer.IsLocked, "");
                        Check("锁定同样只产出一条撤销位，操作名是「锁定图层」",
                            ScadaEditHistory.UndoCount == 1
                            && ScadaEditHistory.NextUndoLabel == $"锁定图层 [{lockTarget.Layer.Name}]",
                            ScadaEditHistory.NextUndoLabel ?? "null");
                        Check("行快照跟着翻转：图标变闭锁、提示变成「解锁图层」",
                            panel.Items[1].LockIcon == ScadaLayerItem.LockGlyph
                            && panel.Items[1].LockTip.Contains("解锁"),
                            panel.Items[1].LockTip);

                        ScadaEditHistory.Undo();
                        Check("Ctrl+Z 把锁定状态撤回去", !lockTarget.Layer.IsLocked, "");

                        // ------------------------------------------------
                        // ⑨ 空态的第二句话：有画面但没图层（与「没有画面」是两回事）
                        // ------------------------------------------------
                        var bare = new ScadaPage { Name = "空画面" };
                        editor.SelectedPage = bare;

                        Check("有画面但没图层：HasPage 仍为真、行表为空（不是「没有画面」那种空）",
                            panel.HasPage && panel.Items.Count == 0, $"HasPage={panel.HasPage}");
                        Check("此时空态提示改成「点新建图层」（两种空态必须给两句不同的话）",
                            panel.EmptyHint.Contains("新建图层") && !panel.EmptyHint.Contains("打开方案"),
                            panel.EmptyHint);
                        Check("没图层的画面照样能点新建图层（AddLayerCommand 只看有没有画面）",
                            panel.AddLayerCommand.CanExecute(), "");
                        Check("面板底部那句防误判文案指到画布右键菜单（图层次序不参与叠放）",
                            panel.OrderHintDetail.Contains("叠放次序"), panel.OrderHintDetail);

                        editor.SelectedPage = page;
                        Check("切回原画面：行表立刻对齐（换画面靠 SelectedPage 通知重新订阅）",
                            panel.Items.Count == page.Layers.Count, $"{panel.Items.Count} vs {page.Layers.Count}");

                        // ------------------------------------------------
                        // ⑩ Deactivate/Activate：离树要摘干净，重复挂接不能变成双份订阅
                        // ------------------------------------------------
                        panel.Deactivate();
                        var frozenCount = panel.Items.Count;
                        page.AddLayer("静默层");

                        Check("Deactivate 后不再跟随画面变化（旧画面不被已经离树的面板钉住）",
                            panel.Items.Count == frozenCount, $"{panel.Items.Count} vs {frozenCount}");

                        panel.Activate();
                        Check("Activate 一次补齐当前画面",
                            panel.Items.Count == page.Layers.Count, $"{panel.Items.Count} vs {page.Layers.Count}");

                        // 数的是 LayerCountText：它在 Rebuild 里无条件通知，
                        // 于是"一次画面变化重算了几轮"可以直接量出来（数 Items 会被 ItemsEqual 挡住而失真）
                        int rebuildRounds = 0;

                        void OnPanelChangedAgain(object? s, PropertyChangedEventArgs e)
                        {
                            if (e.PropertyName == nameof(ScadaLayerVM.LayerCountText)) rebuildRounds++;
                        }

                        panel.PropertyChanged += OnPanelChangedAgain;
                        panel.Activate(); // 第二次必须是空操作
                        page.AddLayer("再来一层");

                        Check("重复 Activate 不重复订阅（一次画面变化只重算一轮）",
                            rebuildRounds == 1, $"重算 {rebuildRounds} 轮");
                    }
                    finally
                    {
                        panel.Deactivate();
                    }
                }
                finally
                {
                    editor.Deactivate();
                }
            }
            finally
            {
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [AF] 图元锁定：唯一写入口 / 撤销一条 / 解锁不被筛空 / 端点空操作 / 菜单二态
        // ==================================================================

        /// <summary>
        /// S10-c 的核心验收：把"图元锁定"从一条裸 setter（属性面板顶栏此前直绑
        /// <c>Element.IsLocked</c>）收敛成 Try 家族的一员，并给整批锁定一个入口（右键菜单那一项）。
        ///
        /// 本段盯四件事——
        /// ① 写入口 <see cref="ScadaPage.TrySetElementLocked"/> 罩在 <c>BeginEdit</c> 里，一批只产一条记录；
        /// ② <b>解锁不能被 <see cref="ScadaPage.IsElementEditable"/> 筛空</b>——已锁的图元恰恰是解锁唯一的目标，
        ///    照抄别的批量入口那条过滤，"锁了之后再也解不开"会以"菜单亮着、点下去什么也不发生"的形式出现；
        /// ③ 全员已是目标状态时是空操作（返回 true 且不产记录），撤销栈里不该堆一串"什么都没干"；
        /// ④ 菜单那一项是二态的（标题/图标跟主选中走），且整批锁定时仍可点（判灰<b>不</b>抄删除那条"可编辑"判据）。
        ///
        /// 样板与 [AE] 一致：要挂真编辑器（<c>ScadaEditorViewModel</c>，DispatcherObject 血统），
        /// 单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
        /// </summary>
        private static void ElementLockChecks()
        {
            Section("[AF] 图元锁定：唯一写入口 / 撤销一条 / 解锁不被筛空 / 端点空操作 / 菜单二态");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunElementLockChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("图元锁定断言全程未抛异常", false, failure.ToString());
        }

        private static void RunElementLockChecks()
        {
            // 撤销栈是全局静态的（单栈双向），本段自己压栈、出段必清
            ScadaEditHistory.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            try
            {
                // ----------------------------------------------------------
                // ① 领域层：锁一个 —— 一条记录、操作名带图元名
                // ----------------------------------------------------------
                var page = new ScadaPage();

                var a = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "底图" });
                var b = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "料箱" });
                var c = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "罐体" });

                page.Elements.Add(a);
                page.Elements.Add(b);
                page.Elements.Add(c);

                ScadaEditHistory.Clear();

                bool lockedOne = page.TrySetElementLocked(new[] { a }, true, out var oneError);

                Check("锁一个：返回 true、IsLocked 落上、只产出一条撤销位，操作名是「锁定图元 [名字]」",
                    lockedOne && a.IsLocked
                    && ScadaEditHistory.UndoCount == 1
                    && ScadaEditHistory.NextUndoLabel == "锁定图元 [底图]",
                    $"{ScadaEditHistory.NextUndoLabel ?? "null"} / {oneError}");

                // 这一条是下面"解锁不能被筛空"那个决策的前提，必须先钉住：
                // IsElementEditable 把"已锁"判成不可编辑，所以任何照抄它过滤的批量入口
                // 都会把解锁的目标筛成空集。
                Check("前提：锁定之后 IsElementEditable 立刻判假（这正是「解锁不能被它筛空」的原因）",
                    !page.IsElementEditable(a) && page.IsElementEditable(b),
                    $"a 可编辑={page.IsElementEditable(a)} / b 可编辑={page.IsElementEditable(b)}");

                Check("锁定是模型的编辑保护位、不是描述符声明的属性（所以它不占属性面板的行，通知要单独补）",
                    ElementRegistry.Find("Hmi.Rectangle")!.Properties.All(p => p.Key != "IsLocked"), "");

                // ----------------------------------------------------------
                // ② 解锁：目标正是那个已经锁上的图元
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();

                bool unlockedOne = page.TrySetElementLocked(new[] { a }, false, out var unlockError);

                Check("解锁：目标就是已经锁上的那个图元，返回 true 且状态真的回去了"
                    + "（照抄 IsElementEditable 过滤会在这里变成永远的空操作）",
                    unlockedOne && !a.IsLocked && ScadaEditHistory.UndoCount == 1,
                    $"IsLocked={a.IsLocked} / UndoCount={ScadaEditHistory.UndoCount} / {unlockError}");

                Check("解锁的操作名是「解锁图元 [名字]」（撤销按钮上要能读出撤的是哪一步）",
                    ScadaEditHistory.NextUndoLabel == "解锁图元 [底图]",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                // ----------------------------------------------------------
                // ③ 批量：一次 Ctrl+Z 全部回去
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();

                bool lockedBatch = page.TrySetElementLocked(new[] { a, b, c }, true, out _);

                Check("批量锁三个：只产出一条撤销位，操作名是「锁定 3 个图元」（批量 = 一次 Ctrl+Z）",
                    lockedBatch && a.IsLocked && b.IsLocked && c.IsLocked
                    && ScadaEditHistory.UndoCount == 1
                    && ScadaEditHistory.NextUndoLabel == "锁定 3 个图元",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                ScadaEditHistory.Undo();
                Check("Ctrl+Z 一次，三个图元全部解锁（不是撤三次）",
                    !a.IsLocked && !b.IsLocked && !c.IsLocked,
                    $"{a.IsLocked}/{b.IsLocked}/{c.IsLocked}");

                ScadaEditHistory.Redo();
                Check("重做一次，三个图元全部锁回（撤销/重做共用同一份回写逻辑）",
                    a.IsLocked && b.IsLocked && c.IsLocked,
                    $"{a.IsLocked}/{b.IsLocked}/{c.IsLocked}");

                // ----------------------------------------------------------
                // ④ 端点口径：全员已是目标状态 = 空操作，不产记录
                // ----------------------------------------------------------
                int beforeNoop = ScadaEditHistory.UndoCount;
                bool noop = page.TrySetElementLocked(new[] { a, b, c }, true, out _);

                Check("全员已是锁定态：再点一次「锁定」是空操作——返回 true 但不产记录（撤销栈里不该堆「什么都没干」）",
                    noop && ScadaEditHistory.UndoCount == beforeNoop,
                    $"UndoCount={ScadaEditHistory.UndoCount} vs {beforeNoop}");

                // 数量口径按"真正发生变化的个数"算：a 已锁、b 未锁，一起锁只算 b 一个
                ScadaEditHistory.Clear();
                page.TrySetElementLocked(new[] { a }, false, out _); // a 解锁，b/c 保持锁定
                ScadaEditHistory.Clear();

                bool partial = page.TrySetElementLocked(new[] { a, b }, true, out _);

                Check("混合状态：数量按「真正要变的个数」算（a 未锁、b 已锁，一起锁只算 a 一个 → 「锁定图元 [底图]」）",
                    partial && a.IsLocked && b.IsLocked
                    && ScadaEditHistory.NextUndoLabel == "锁定图元 [底图]",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                // ----------------------------------------------------------
                // ⑤ 过滤：重复项去重、外来对象剔除、空输入给人话
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                page.TrySetElementLocked(new[] { a, b, c }, false, out _); // 全部解锁，回到干净状态
                ScadaEditHistory.Clear();

                page.TrySetElementLocked(new[] { c, c, c }, true, out _);

                Check("同一个图元传三次只算一个（去重之后数量口径才对得上：是名字而不是「3 个图元」）",
                    ScadaEditHistory.NextUndoLabel == "锁定图元 [罐体]",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                ScadaEditHistory.Clear();

                var foreign = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "外来户" });

                bool mixedForeign = page.TrySetElementLocked(new[] { foreign, b }, true, out _);

                Check("不属于本画面的图元被剔掉、剩下那个照常生效（撤销之后已经不在的图元也走这条）",
                    mixedForeign && b.IsLocked && !foreign.IsLocked
                    && ScadaEditHistory.NextUndoLabel == "锁定图元 [料箱]",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                bool onlyForeign = page.TrySetElementLocked(new[] { foreign }, true, out var foreignError);

                Check("全是外来对象：拒绝并给人话（不能静默成功，否则调用方以为锁上了）",
                    !onlyForeign && foreignError.Contains("不属于本画面"), foreignError);

                bool none = page.TrySetElementLocked(Array.Empty<ScadaElement>(), true, out var noneError);

                Check("空集合：拒绝并给出「至少要选中 1 个图元」",
                    !none && noneError.Contains("至少"), noneError);

                bool nullInput = page.TrySetElementLocked(null, true, out var nullError);

                Check("null 输入同样被拒（不抛异常，走同一条人话通道）",
                    !nullInput && nullError.Contains("至少"), nullError);

                // ----------------------------------------------------------
                // ⑥ 落盘：IsLocked 要真进文件，且撤销后 JSON 逐字节回退（D3 的验收口径）
                // ----------------------------------------------------------
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.Auto,
                };

                ScadaEditHistory.Clear();
                var jsonPage = new ScadaPage();
                var jsonElement = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "矩形A" });
                jsonPage.Elements.Add(jsonElement);

                var jsonBefore = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                jsonPage.TrySetElementLocked(new[] { jsonElement }, true, out _);
                var jsonAfterLock = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                ScadaEditHistory.Undo();
                var jsonAfterUndo = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                Check("锁定真的落盘（操作前后 JSON 不同——不是只活在内存里的一个标志位）",
                    jsonAfterLock != jsonBefore, "");

                Check("撤销后的落盘内容与操作前逐字节一致（IsLocked 走的是值级 diff，能整条回退）",
                    jsonAfterUndo == jsonBefore, FirstDiff(jsonBefore, jsonAfterUndo));

                // ----------------------------------------------------------
                // ⑦ 编辑器视图模型：菜单那一项二态、判灰、整批生效
                // ----------------------------------------------------------
                const string lockGlyph = "\uF023";   // Font Awesome 6 Pro Solid: lock
                const string unlockGlyph = "\uF3C1"; // Font Awesome 6 Pro Solid: lock-open

                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                workspace.SwitchSolution(solution);

                var editor = new ScadaEditorVM(workspace, null!);

                try
                {
                    editor.AddPageCommand.Execute();

                    var e1 = editor.AddElement("Hmi.Rectangle", new Point(0, 0))!;
                    var e2 = editor.AddElement("Hmi.Rectangle", new Point(50, 0))!;
                    var e3 = editor.AddElement("Hmi.Rectangle", new Point(100, 0))!;

                    editor.SelectedElements = new[] { e1, e2, e3 };

                    var lockMenu = editor.BuildElementContextMenu();

                    Check("主选中未锁：菜单那一项写「锁定图元」、图标是闭锁（图标跟动作走，免得与标题打架）",
                        lockMenu[7].Name == "锁定图元" && lockMenu[7].Icon == lockGlyph,
                        $"{lockMenu[7].Name} / U+{(lockMenu[7].Icon is string s && s.Length > 0 ? ((int)s[0]).ToString("X4") : "----")}");

                    Check("整批都亮着的时候那一项也能点（判灰只看「有没有选中」，不抄删除那条「可编辑」判据）",
                        editor.CanSetSelectedLocked() && lockMenu[7].Command!.CanExecute(null), "");

                    lockMenu[7].Command!.Execute(null);

                    Check("点一下：整批拉齐到主选中的相反状态（三个一起锁上，不是只锁主选中那一个）",
                        e1.IsLocked && e2.IsLocked && e3.IsLocked,
                        $"{e1.IsLocked}/{e2.IsLocked}/{e3.IsLocked}");

                    var unlockMenu = editor.BuildElementContextMenu();

                    Check("锁上之后菜单自己翻成「解锁图元」+ 开锁图标（标题与图标都跟主选中走，菜单每次弹出即重建）",
                        unlockMenu[7].Name == "解锁图元" && unlockMenu[7].Icon == unlockGlyph,
                        $"{unlockMenu[7].Name} / U+{(unlockMenu[7].Icon is string u && u.Length > 0 ? ((int)u[0]).ToString("X4") : "----")}");

                    Check("整批已锁定时那一项仍是亮的——解锁恰是此刻唯一该亮的动作（抄删除的判据会让它永远灰着）",
                        editor.CanSetSelectedLocked() && unlockMenu[7].Command!.CanExecute(null), "");

                    unlockMenu[7].Command!.Execute(null);

                    Check("再点一下：整批解锁（锁了之后必须解得开）",
                        !e1.IsLocked && !e2.IsLocked && !e3.IsLocked,
                        $"{e1.IsLocked}/{e2.IsLocked}/{e3.IsLocked}");

                    // 二态判据读的是"主选中"，不是"只要有一个没锁就锁上"：
                    // 主选中已锁、同批里另一个没锁时，点下去是「解锁」——把整批拉齐到主选中的相反值。
                    editor.SelectedElement = e1;
                    editor.SetSelectedLocked(true);
                    editor.SelectedElements = new[] { e1, e2 };

                    var soloMenu = editor.BuildElementContextMenu();

                    Check("二态判据读的是主选中：主选中已锁时菜单写「解锁图元」（哪怕同批里还有没锁的）",
                        soloMenu[7].Name == "解锁图元" && editor.IsMainSelectionLocked,
                        $"{soloMenu[7].Name} / 主选中已锁={editor.IsMainSelectionLocked}");

                    editor.ToggleSelectedLock();

                    Check("主选中已锁、同批另一个没锁：点一下是「解锁」（拉齐到主选中的相反状态），不是把没锁的那个也锁上",
                        !e1.IsLocked && !e2.IsLocked,
                        $"{e1.IsLocked}/{e2.IsLocked}");

                    // ----------------------------------------------------------
                    // ⑧ 属性面板顶栏那个复选框：改走 Try 家族后在严格模式下也走得通
                    //    （此前它直绑 Element.IsLocked，是一条绕开 BeginEdit 的裸 setter）
                    // ----------------------------------------------------------
                    // 面板编辑的恒是"主选中那一个"，而锁定那一项作用于整批——
                    // 这里先把选中收成单选，免得撤销记录里写的是"锁定 2 个图元"（那是上一条断言的事）。
                    editor.SelectedElements = new[] { e1 };

                    var panel = new ScadaPropertyVM(
                        editor, new RecordingNotifier(), new FakeVariablePicker(), new FakePagePicker());

                    ScadaEditHistory.Clear();
                    ScadaWriteGuard.Strict = true;

                    string panelError = string.Empty;

                    try
                    {
                        panel.IsElementLocked = true;
                    }
                    catch (Exception ex)
                    {
                        panelError = ex.Message;
                    }
                    finally
                    {
                        ScadaWriteGuard.Strict = strictBackup;
                    }

                    Check("属性面板顶栏那个复选框走的是 TrySetElementLocked：Strict=true 下不触发写守卫（D3 的漏口堵上了）",
                        panelError.Length == 0 && e1.IsLocked, panelError);

                    Check("面板写入同样产出一条带操作名的记录（撤销按钮上显示「锁定图元 [名字]」，不是含糊的「编辑」）",
                        ScadaEditHistory.NextUndoLabel == $"锁定图元 [{e1.Name}]",
                        ScadaEditHistory.NextUndoLabel ?? "null");

                    Check("面板回读：复选框跟着模型走（写成功之后读得到当前值）",
                        panel.IsElementLocked, $"IsElementLocked={panel.IsElementLocked}");

                    ScadaEditHistory.Clear();
                    panel.IsElementLocked = false;

                    Check("面板也能解锁：同一个入口管两向，撤销位写的是「解锁图元 [名字]」",
                        !e1.IsLocked && ScadaEditHistory.NextUndoLabel == $"解锁图元 [{e1.Name}]",
                        ScadaEditHistory.NextUndoLabel ?? "null");

                    // 写失败也必须回抛一次通知：模型值没变、模型也不会发通知，
                    // 而绑定是 TwoWay 的——不回抛，复选框就停在用户刚点出来的那个假状态上。
                    editor.SelectedElement = null;

                    int lockNotices = 0;

                    void OnLockNotice(object? s, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == nameof(ScadaPropertyVM.IsElementLocked)) lockNotices++;
                    }

                    panel.PropertyChanged += OnLockNotice;
                    panel.IsElementLocked = true;
                    panel.PropertyChanged -= OnLockNotice;

                    Check("没有选中图元时写锁定失败：面板照样回抛一次通知（否则 TwoWay 绑定会停在假状态上）",
                        lockNotices == 1 && !panel.IsElementLocked,
                        $"通知 {lockNotices} 次 / IsElementLocked={panel.IsElementLocked}");
                }
                finally
                {
                    editor.Deactivate();
                }
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [AG] 图元成组：只写一个 Guid / 一条撤销 / 选中即整组 / 跨层 / 菜单二态
        // ==================================================================

        /// <summary>
        /// S10-d 的核心验收：把"成组"做成模型里的一条关系（<see cref="ScadaElement.GroupId"/>），
        /// 而不是画布上的一堆坐标联动——组员之间没有几何约束，只是"选中时一起被选中"。
        ///
        /// 本段盯五件事——
        /// ① 写入口 <see cref="ScadaPage.TryGroupElements"/> 只写一个 Guid，一批只产一条记录；
        /// ② <b>选中即整组</b>：点中一个组员要把整组撑进选中集合，且被点中的那个排首位
        ///    （否则"点的是 B、属性面板显示 A"）；而程序化收窄选中<b>不</b>展开，
        ///    否则用户再也没法只挑出组里的一个成员；
        /// ③ 全员已同组是空操作（返回 true 不产记录）——"选中一个组、再点一次组合"是最自然的误操作；
        /// ④ <see cref="ScadaElement.LayerId"/> 与 <c>GroupId</c> 是两把互不干涉的关系：
        ///    <c>LayerId</c> 管渲染、<c>GroupId</c> 管编辑，同组图元可以跨图层；
        /// ⑤ 菜单那一项二态（标题/图标跟主选中走），判灰按"要走的那一边"算。
        ///
        /// 样板与 [AF] 一致：要挂真编辑器（<c>ScadaEditorViewModel</c>，DispatcherObject 血统），
        /// 单开 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 的运行环境。
        /// </summary>
        private static void ElementGroupChecks()
        {
            Section("[AG] 图元成组：只写一个 Guid / 一条撤销 / 选中即整组 / 跨层 / 菜单二态");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunElementGroupChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("图元成组断言全程未抛异常", false, failure.ToString());
        }

        private static void RunElementGroupChecks()
        {
            // 撤销栈是全局静态的（单栈双向），本段自己压栈、出段必清
            ScadaEditHistory.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            try
            {
                // ----------------------------------------------------------
                // ① 领域层：组合两个 —— 一个 Guid、一条记录、没参与的不动
                // ----------------------------------------------------------
                var page = new ScadaPage();

                var a = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "底图" });
                var b = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "料箱" });
                var c = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "罐体" });

                page.Elements.Add(a);
                page.Elements.Add(b);
                page.Elements.Add(c);

                ScadaEditHistory.Clear();

                bool grouped = page.TryGroupElements(new[] { a, b }, out var groupError);

                Check("组合两个：返回 true、两个图元拿到同一个非空 GroupId、只产出一条撤销位，操作名是「组合 2 个图元」",
                    grouped && a.GroupId != Guid.Empty && a.GroupId == b.GroupId
                    && ScadaEditHistory.UndoCount == 1
                    && ScadaEditHistory.NextUndoLabel == "组合 2 个图元",
                    $"{ScadaEditHistory.NextUndoLabel ?? "null"} / {groupError}");

                Check("没参与组合的第三个仍是未分组（组合只写递进来的那些，不是「把周围的一起收了」）",
                    c.GroupId == Guid.Empty, $"{c.GroupId}");

                // ----------------------------------------------------------
                // ② GetGroupMembers：唯一判据来源（画布、视图模型都读它）
                // ----------------------------------------------------------
                Check("GetGroupMembers：未分组的图元返回空表（调用方据此判定「它不是组员」）",
                    page.GetGroupMembers(c).Count == 0 && page.GetGroupMembers(null).Count == 0,
                    $"{page.GetGroupMembers(c).Count}");

                var members = page.GetGroupMembers(a);

                Check("GetGroupMembers：组员查回来是「含自己」的两件（选中即整组靠它一张表铺成集合）",
                    members.Count == 2 && members.Contains(a) && members.Contains(b),
                    $"{members.Count}");

                // ----------------------------------------------------------
                // ③ 端点口径：全员已同组 = 空操作，不产记录
                // ----------------------------------------------------------
                int beforeNoop = ScadaEditHistory.UndoCount;
                bool noop = page.TryGroupElements(new[] { b, a }, out _);

                Check("全员已同属一组：再点一次「组合」是空操作——返回 true 但不产记录"
                    + "（点中一个组员就整组选中，于是「选中一个组再点组合」是最自然的误操作）",
                    noop && ScadaEditHistory.UndoCount == beforeNoop,
                    $"UndoCount={ScadaEditHistory.UndoCount} vs {beforeNoop}");

                // 一个组员 + 一个组外：不是空操作，而是"换一个新 Guid"（不嵌套），
                // 且组里没被选中的成员留在原组——这条钉住 G-4「只一层、两组再组合即合并」的口径。
                var firstGroup = a.GroupId;
                ScadaEditHistory.Clear();

                bool merged = page.TryGroupElements(new[] { a, c }, out _);

                Check("把组员和组外图元再组合：换成一个新 Guid（不套一层），组里没被选中的成员留在原组",
                    merged && a.GroupId == c.GroupId && a.GroupId != firstGroup && b.GroupId == firstGroup,
                    $"a==c:{a.GroupId == c.GroupId} / 新:{a.GroupId != firstGroup} / b 留原组:{b.GroupId == firstGroup}");

                // ----------------------------------------------------------
                // ④ 前置校验与过滤：少于 2 个 / null / 外来对象 / 去重
                // ----------------------------------------------------------
                var guardPage = new ScadaPage();

                var g1 = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "甲" });
                var g2 = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "乙" });
                var g3 = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "丙" });

                guardPage.Elements.Add(g1);
                guardPage.Elements.Add(g2);
                guardPage.Elements.Add(g3);

                ScadaEditHistory.Clear();

                bool tooFew = guardPage.TryGroupElements(new[] { g1 }, out var fewError);

                Check("只给 1 个：拒绝并给出「至少要选中 2 个图元才能组合」"
                    + "（一个成员的组与未分组没有任何行为差别，允许它只会让用户以为自己建了个组）",
                    !tooFew && fewError.Contains("至少") && fewError.Contains("2"), fewError);

                bool nullInput = guardPage.TryGroupElements(null, out var nullError);

                Check("null 输入同样被拒（不抛异常，走同一条人话通道）",
                    !nullInput && nullError.Contains("至少"), nullError);

                var foreign = ScadaChangeScope.Detached(
                    () => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "外来户" });

                bool mixedForeign = guardPage.TryGroupElements(new[] { foreign, g1, g2 }, out _);

                Check("不属于本画面的图元被剔掉、剩下两个照常进组（撤销之后已经不在的图元也走这条）",
                    mixedForeign && g1.GroupId != Guid.Empty && g1.GroupId == g2.GroupId
                    && foreign.GroupId == Guid.Empty
                    && ScadaEditHistory.NextUndoLabel == "组合 2 个图元",
                    $"{ScadaEditHistory.NextUndoLabel ?? "null"} / 外来户={foreign.GroupId}");

                ScadaEditHistory.Clear();
                guardPage.TryUngroupElements(new[] { g1, g2, g3 }, out _); // 回到全员未分组
                ScadaEditHistory.Clear();

                bool duplicated = guardPage.TryGroupElements(new[] { g1, g1, g1 }, out var dupError);

                Check("同一个图元传三次只算一个 → 去重后不足 2 个，照样拒绝（数量口径按去重后算）",
                    !duplicated && dupError.Contains("不足"), dupError);

                bool onlyForeign = guardPage.TryGroupElements(new[] { foreign, foreign }, out var onlyForeignError);

                Check("全是外来对象：拒绝并说明「可组合的图元不足 2 个」（不能静默成功）",
                    !onlyForeign && onlyForeignError.Contains("不足"), onlyForeignError);

                // ----------------------------------------------------------
                // ⑤ 取消组合：只对确实有组的动手，数量口径与撤销文案
                // ----------------------------------------------------------
                ScadaEditHistory.Clear();
                guardPage.TryGroupElements(new[] { g1, g2 }, out _);
                var pairGroup = g1.GroupId;
                ScadaEditHistory.Clear();

                bool ungrouped = guardPage.TryUngroupElements(new[] { g1, g2, g3 }, out var ungroupError);

                Check("取消组合：只对确实有组的动手——g1/g2 清回 Empty，本来就没组的 g3 不算数，"
                    + "操作名是「取消组合 2 个图元」",
                    ungrouped && g1.GroupId == Guid.Empty && g2.GroupId == Guid.Empty
                    && ScadaEditHistory.UndoCount == 1
                    && ScadaEditHistory.NextUndoLabel == "取消组合 2 个图元",
                    $"{ScadaEditHistory.NextUndoLabel ?? "null"} / {ungroupError}");

                ScadaEditHistory.Undo();

                Check("Ctrl+Z 一次：两个组员各自回组（逐图元回写 Guid，不是整组回滚）",
                    g1.GroupId == pairGroup && g2.GroupId == pairGroup && g3.GroupId == Guid.Empty,
                    $"{g1.GroupId == pairGroup}/{g2.GroupId == pairGroup}");

                ScadaEditHistory.Clear();
                guardPage.TryUngroupElements(new[] { g1 }, out _);

                Check("只取消一个时操作名写的是名字（「取消组合图元 [甲]」，与锁定那一对的文案口径同源）",
                    g1.GroupId == Guid.Empty && ScadaEditHistory.NextUndoLabel == "取消组合图元 [甲]",
                    ScadaEditHistory.NextUndoLabel ?? "null");

                int beforeUngroupNoop = ScadaEditHistory.UndoCount;
                bool ungroupNoop = guardPage.TryUngroupElements(new[] { g1, g3 }, out _);

                Check("全都没组：取消组合是空操作——返回 true 但不产记录（与锁定那一对的端点口径一致）",
                    ungroupNoop && ScadaEditHistory.UndoCount == beforeUngroupNoop,
                    $"UndoCount={ScadaEditHistory.UndoCount} vs {beforeUngroupNoop}");

                bool emptyInput = guardPage.TryUngroupElements(Array.Empty<ScadaElement>(), out var emptyError);

                Check("空集合：拒绝并给出「至少要选中 1 个图元」",
                    !emptyInput && emptyError.Contains("至少"), emptyError);

                // ----------------------------------------------------------
                // ⑥ 跨层：LayerId 管渲染、GroupId 管编辑，同组可以跨图层
                // ----------------------------------------------------------
                var layerPage = new ScadaPage();

                var under = layerPage.AddLayer("底层");
                var over = layerPage.AddLayer("顶层");

                var la = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "跨层甲" });
                var lb = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "跨层乙" });

                layerPage.Elements.Add(la);
                layerPage.Elements.Add(lb);

                layerPage.TryAssignLayer(la, under, out _);
                layerPage.TryAssignLayer(lb, over, out _);

                ScadaEditHistory.Clear();

                bool crossLayer = layerPage.TryGroupElements(new[] { la, lb }, out _);

                Check("同组图元可以跨图层（两把关系互不干涉：分组不改渲染，改归属也不拆组）",
                    crossLayer && la.GroupId == lb.GroupId && la.LayerId != lb.LayerId,
                    $"同组:{la.GroupId == lb.GroupId} / 不同层:{la.LayerId != lb.LayerId}");

                // ----------------------------------------------------------
                // ⑦ 写守卫：GroupId 的写入罩在 BeginEdit 里，Strict 下不抛
                // ----------------------------------------------------------
                ScadaWriteGuard.Strict = true;

                string strictError = string.Empty;

                try
                {
                    guardPage.TryGroupElements(new[] { g1, g3 }, out _);
                }
                catch (Exception ex)
                {
                    strictError = ex.Message;
                }
                finally
                {
                    ScadaWriteGuard.Strict = strictBackup;
                }

                Check("Strict 写守卫下 TryGroupElements 不抛异常（GroupId 的写入走 BeginEdit，D3 没有漏口）",
                    strictError.Length == 0 && g1.GroupId != Guid.Empty && g1.GroupId == g3.GroupId,
                    strictError);

                // ----------------------------------------------------------
                // ⑧ 落盘：GroupId 要真进文件，撤销/重做后 JSON 逐字节往返（D3 的验收口径）
                // ----------------------------------------------------------
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.Auto,
                };

                ScadaEditHistory.Clear();

                var jsonPage = new ScadaPage();

                var j1 = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "甲" });
                var j2 = ScadaChangeScope.Detached(() => new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "乙" });

                jsonPage.Elements.Add(j1);
                jsonPage.Elements.Add(j2);

                var jsonBefore = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                jsonPage.TryGroupElements(new[] { j1, j2 }, out _);
                var jsonAfterGroup = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                ScadaEditHistory.Undo();
                var jsonAfterUndo = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                ScadaEditHistory.Redo();
                var jsonAfterRedo = JsonConvert.SerializeObject(jsonPage, jsonSettings);

                Check("GroupId 真的落盘（操作前后 JSON 不同——组是模型里的关系，不是只活在内存里的编辑态）",
                    jsonAfterGroup != jsonBefore, "");

                Check("撤销后的落盘内容与操作前逐字节一致（GroupId 走值级 diff，能整条回退）",
                    jsonAfterUndo == jsonBefore, FirstDiff(jsonBefore, jsonAfterUndo));

                Check("重做后又回到分组后的落盘内容（撤销/重做共用同一份回写逻辑）",
                    jsonAfterRedo == jsonAfterGroup, FirstDiff(jsonAfterGroup, jsonAfterRedo));

                // ----------------------------------------------------------
                // ⑨ 编辑器视图模型：选中即整组 / 菜单二态 / 判灰
                // ----------------------------------------------------------
                const string groupGlyph = "\uF247";   // Font Awesome 6 Pro Solid: object-group
                const string ungroupGlyph = "\uF248"; // Font Awesome 6 Pro Solid: object-ungroup

                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                workspace.SwitchSolution(solution);

                var editor = new ScadaEditorVM(workspace, null!);

                try
                {
                    editor.AddPageCommand.Execute();

                    var e1 = editor.AddElement("Hmi.Rectangle", new Point(0, 0))!;
                    var e2 = editor.AddElement("Hmi.Rectangle", new Point(50, 0))!;
                    var e3 = editor.AddElement("Hmi.Rectangle", new Point(100, 0))!;

                    editor.SelectedElements = new[] { e1, e2, e3 };

                    var groupMenu = editor.BuildElementContextMenu();

                    Check("主选中未分组：菜单那一项写「组合」、图标是成组（排在锁定之后、删除之前——末位留给唯一不可逆的那一项）",
                        groupMenu[8].Name == "组合" && groupMenu[8].Icon == groupGlyph,
                        $"{groupMenu[8].Name} / U+{(groupMenu[8].Icon is string s && s.Length > 0 ? ((int)s[0]).ToString("X4") : "----")}");

                    Check("选中 3 个时「组合」可点（判灰只数「确实在本画面上的选中」≥2，与领域层前置校验同源）",
                        editor.CanGroupSelected() && editor.CanToggleSelectedGroup()
                        && groupMenu[8].Command!.CanExecute(null), "");

                    groupMenu[8].Command!.Execute(null);

                    Check("点一下：三个图元进同一个组（判据与动作传的是同一份集合，不会出现「亮着但点了没反应」）",
                        e1.GroupId != Guid.Empty && e1.GroupId == e2.GroupId && e2.GroupId == e3.GroupId,
                        $"{e1.GroupId != Guid.Empty}/{e1.GroupId == e2.GroupId}/{e2.GroupId == e3.GroupId}");

                    var ungroupMenu = editor.BuildElementContextMenu();

                    Check("组上之后菜单自己翻成「取消组合」+ 拆组图标（标题与图标都跟主选中走，菜单每次弹出即重建）",
                        ungroupMenu[8].Name == "取消组合" && ungroupMenu[8].Icon == ungroupGlyph,
                        $"{ungroupMenu[8].Name} / U+{(ungroupMenu[8].Icon is string u && u.Length > 0 ? ((int)u[0]).ToString("X4") : "----")}");

                    Check("已是组员时「取消组合」仍可点（判灰数的是「确实有组的」，不是「选中的」——否则空操作也会亮着）",
                        editor.CanUngroupSelected() && ungroupMenu[8].Command!.CanExecute(null), "");

                    // 选中即整组：清空选中之后"点中一个组员"（走 SelectedElement setter 那条路）
                    editor.SelectedElements = Array.Empty<ScadaElement>();
                    editor.SelectedElement = e2;

                    Check("点中一个组员：选中集合被撑成整组，且被点中的那个排在首位"
                        + "（首位就是主选中——否则会出现「点的是 B、属性面板显示 A」）",
                        editor.SelectedElements.Count == 3
                        && ReferenceEquals(editor.SelectedElements[0], e2)
                        && ReferenceEquals(editor.SelectedElement, e2),
                        $"Count={editor.SelectedElements.Count} / 首位是 e2={editor.SelectedElements.Count > 0 && ReferenceEquals(editor.SelectedElements[0], e2)}");

                    // 反过来：程序化收窄选中不展开。这条是"框选还得能只挑出组里一个"的前提。
                    editor.SelectedElements = new[] { e1 };

                    Check("SelectedElements 直接赋值不展开（框选与程序化收窄的语义是「给谁就是谁」，"
                        + "在集合入口里展开会让用户再也没法只挑出组里的一个）",
                        editor.SelectedElements.Count == 1 && editor.IsMainSelectionGrouped,
                        $"Count={editor.SelectedElements.Count} / 主选中在组里={editor.IsMainSelectionGrouped}");

                    // 此处主选中是 e1（它已在组里），所以菜单那一项该翻成「取消组合」，
                    // CanToggleSelectedGroup 走的就是"取消组合"那一边——它是亮着的，别错当成判灰。
                    Check("单选一个组员时「组合」判灰（可组合的选中不足 2 个），而这一项翻成「取消组合」且亮着",
                        !editor.CanGroupSelected() && editor.CanToggleSelectedGroup() && editor.CanUngroupSelected(),
                        $"组合={editor.CanGroupSelected()} / 可切换={editor.CanToggleSelectedGroup()} / 取消组合={editor.CanUngroupSelected()}");

                    // 造一个真正的混合批次：e1/e2 一组、e3 在组外
                    editor.SelectedElements = new[] { e1, e2, e3 };
                    editor.UngroupSelected();
                    editor.SelectedElements = new[] { e1, e2 };
                    editor.GroupSelected();

                    editor.SelectedElements = new[] { e1, e3 };

                    Check("主选中在组里、同批还有组外的：那一项写「取消组合」（走哪边由主选中决定，"
                        + "标题与实际动作必须是同一个判据）",
                        editor.IsMainSelectionGrouped && editor.BuildElementContextMenu()[8].Name == "取消组合", "");

                    editor.ToggleSelectedGroup();

                    Check("点下去只动确实有组的那个：e1 出组，组外的 e3 与没被选中的 e2 都不受影响",
                        e1.GroupId == Guid.Empty && e3.GroupId == Guid.Empty && e2.GroupId != Guid.Empty,
                        $"e1={e1.GroupId}/ e2 仍在组={e2.GroupId != Guid.Empty}/ e3={e3.GroupId}");

                    editor.SelectedElements = new[] { e2 };
                    editor.UngroupSelected();

                    editor.SelectedElements = new[] { e2, e3 };

                    Check("全都没组时「取消组合」判灰（空操作却亮着，用户只会记成「这一项坏了」）",
                        !editor.CanUngroupSelected() && editor.CanGroupSelected() && editor.CanToggleSelectedGroup(),
                        $"取消组合={editor.CanUngroupSelected()} / 组合={editor.CanGroupSelected()}");

                    editor.GroupSelected();

                    Check("清干净之后再点「组合」：两个一起进组（同一个入口管两向，反复来回都走得通）",
                        e2.GroupId != Guid.Empty && e2.GroupId == e3.GroupId && e1.GroupId == Guid.Empty,
                        $"e2==e3:{e2.GroupId == e3.GroupId} / e1 未分组:{e1.GroupId == Guid.Empty}");
                }
                finally
                {
                    editor.Deactivate();
                }
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [CP] 复制 / 粘贴 / 再制 / 存为模板（S13）
        //
        //  为什么整段要 STA：④⑤ 的编辑器视图模型是 BindableBase + DelegateCommand，与 [AG] 同款。
        //  ①②③⑥⑦ 本身是纯 .NET（快照与模板库零 WPF 依赖，D1），搭同一根线程只为少写一份样板。
        // ==================================================================
        private static void ClipboardAndTemplateChecks()
        {
            Section("[CP] 复制 / 粘贴 / 再制 / 存为模板（S13）：快照往返 / 身份重映射 / 组槽位 / 跨层名回查 / 阶梯错开 / 模板库");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunClipboardAndTemplateChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("复制粘贴与模板断言全程未抛异常", false, failure.ToString());
        }

        private static void RunClipboardAndTemplateChecks()
        {
            // 撤销栈与剪贴板都是全局静态的，本段自己压栈、出段必清
            ScadaEditHistory.Clear();
            ScadaClipboard.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            try
            {
                // ----------------------------------------------------------
                // ① 领域层：Capture 抓快照（组槽位编号 / 空表 / 层名而非 LayerId）
                // ----------------------------------------------------------
                var page = new ScadaPage();
                page.AddLayer("设备层");

                var devLayer = page.DefaultLayer!;

                var srcA = ScadaChangeScope.Detached(() => new ScadaElement
                { TypeKey = "Hmi.Rectangle", Name = "底图", X = 100, Y = 200, Width = 60, Height = 30, ZIndex = 1, LayerId = devLayer.LayerId });

                var srcB = ScadaChangeScope.Detached(() => new ScadaElement
                { TypeKey = "Hmi.Rectangle", Name = "阀门", X = 300, Y = 200, Width = 40, Height = 40, ZIndex = 2, LayerId = devLayer.LayerId });

                var srcC = ScadaChangeScope.Detached(() => new ScadaElement
                { TypeKey = "Hmi.Rectangle", Name = "泵", X = 300, Y = 400, Width = 40, Height = 40, ZIndex = 3, LayerId = devLayer.LayerId });

                srcA.Properties["Fill"] = "#FF8800";

                page.Elements.Add(srcA);
                page.Elements.Add(srcB);
                page.Elements.Add(srcC);

                page.TryGroupElements(new[] { srcA, srcB }, out _);
                ScadaEditHistory.Clear();

                var payload = ScadaClipboard.Capture(page, new[] { srcA, srcB, srcC });

                Check("Capture 组槽位：整组两个拿到同一个非负槽位、组外那个是 NoGroupSlot(-1)"
                    + "（快照搬的是「哪几个是一伙的」这个语义，不是 GroupId——那个 Guid 出了源画面就失效）",
                    payload.Items.Count == 3
                    && payload.Items[0].GroupSlot >= 0
                    && payload.Items[0].GroupSlot == payload.Items[1].GroupSlot
                    && payload.Items[2].GroupSlot == ScadaElementSnapshot.NoGroupSlot,
                    string.Join("/", payload.Items.Select(i => i.GroupSlot)));

                var singleCapture = ScadaClipboard.Capture(page, new[] { srcA });

                Check("只拷组里一个成员：槽位回落成 NoGroupSlot——否则粘出来会冒出一个「只有自己的组」，"
                    + "右键菜单跟着显示「取消组合」，用户只会困惑自己什么时候组合过",
                    singleCapture.Items.Count == 1
                    && singleCapture.Items[0].GroupSlot == ScadaElementSnapshot.NoGroupSlot,
                    $"{singleCapture.Items[0].GroupSlot}");

                Check("Capture 空表 / null 画面：返回空负载而不是 null"
                    + "（调用方可以无脑塞进剪贴板，HasPayload 会让「复制了个寂寞」自然表现为粘贴不可用）",
                    ScadaClipboard.Capture(page, new List<ScadaElement>()).IsEmpty
                    && ScadaClipboard.Capture(null, null).IsEmpty
                    && !ScadaClipboard.Capture(null, null).IsEmpty == false,
                    "");

                Check("快照记的是图层「名字」而不是 LayerId：跨画面粘贴时那个 Guid 在新画面里必然不存在，"
                    + "搬过去只会得到一批指向已删除图层的悬空图元（渲染上等同未分层，图层列表里却数不出来）",
                    payload.Items[0].LayerName == "设备层" && payload.Items[0].Properties.TryGetValue("Fill", out string? fill) && fill == "#FF8800",
                    $"{payload.Items[0].LayerName} / {payload.Items[0].Properties.Count} 个属性");

                Check("快照是纯数据：序列化后一个 $type 都不带——模板文件读盘就不需要类型白名单，"
                    + "也就没有那道让人不敢随手放宽的口子（SolutionService 上那一道是刻意留着的，不该为模板再开一张）",
                    !JsonConvert.SerializeObject(payload).Contains("$type")
                    && JsonConvert.SerializeObject(payload).Contains("\"GroupSlot\""),
                    "");

                Check("剪贴板可用性只看「有没有货」：空负载塞进去 HasPayload 为 false，Clear 之后连负载都没有",
                    ScadaClipboard.Capture(null, null) is { } emptyProbe
                    && !(emptyProbe is { IsEmpty: false })
                    && ScadaClipboard.Payload == null && !ScadaClipboard.HasPayload,
                    "");

                // ----------------------------------------------------------
                // ② Materialize 物化：三条重映射纪律 + Z 序重排 + 一条撤销位
                // ----------------------------------------------------------
                var target = new ScadaPage();
                target.AddLayer("设备层");

                var home = ScadaChangeScope.Detached(() => new ScadaElement
                { TypeKey = "Hmi.Rectangle", Name = "阀门", X = 0, Y = 0, Width = 120, Height = 40, ZIndex = 7 });

                target.Elements.Add(home);
                ScadaEditHistory.Clear();

                var created = ScadaClipboard.Materialize(target, payload, 10, 20, "粘贴 3 个图元");

                Check("物化：三件都进了画面，ElementId 全部重生成（与源图元、彼此都不同）——"
                    + "活对象只有一个 Id，粘第二遍画面上就有两个同 Id 的图元，而 FindElement 按「保留靠前者」解析，"
                    + "第二个从此永远选不中、绑定也刷不到它",
                    created.Count == 3
                    && created.All(e => e.ElementId != Guid.Empty)
                    && created.Select(e => e.ElementId).Distinct().Count() == 3
                    && created.All(e => e.ElementId != srcA.ElementId && e.ElementId != srcB.ElementId && e.ElementId != srcC.ElementId),
                    string.Join("/", created.Select(e => e.Name)));

                Check("名字就地查重：目标画面已有一个「阀门」，物化出来的那个变成「阀门_2」——"
                    + "照搬原名会在画面上出现两个同名图元，属性面板标题跟着失去分辨力",
                    created.Any(e => e.Name == "阀门_2") && created.All(e => e.Name != "阀门"),
                    string.Join("/", created.Select(e => e.Name)));

                Check("ZIndex 排到目标画面现有最大之后（源 1/2/3 → 目标 8/9/10，底图 ZIndex=7 不动），"
                    + "副本彼此的相对先后照源 Z 序保住——直接照搬源 Z 序会让粘出来的东西生在底图下面，用户看到的是「粘了没反应」",
                    created.Select(e => e.ZIndex).SequenceEqual(new[] { 8, 9, 10 }) && home.ZIndex == 7,
                    string.Join("/", created.Select(e => e.ZIndex)));

                Check("偏移量按调用方给的落点整体平移：源 (100,200)/(300,200)/(300,400) → 目标 (110,220)/(310,220)/(310,420)",
                    created[0].X == 110 && created[0].Y == 220
                    && created[1].X == 310 && created[1].Y == 220
                    && created[2].X == 310 && created[2].Y == 420,
                    $"({created[0].X},{created[0].Y}) ({created[1].X},{created[1].Y}) ({created[2].X},{created[2].Y})");

                Check("组槽位重映射：粘出来那两个是同一个「新」Guid（与源组不同），组外那个是 Empty——"
                    + "既不与画面里任何现存组串味，又保住「拷一整组、粘出来还是一整组」",
                    created[0].GroupId != Guid.Empty && created[0].GroupId == created[1].GroupId
                    && created[0].GroupId != srcA.GroupId
                    && created[2].GroupId == Guid.Empty,
                    $"{created[0].GroupId} / 源 {srcA.GroupId} / {created[2].GroupId}");

                Check("跨层名回查：源图元在「设备层」，目标画面也有同名层 → 落进那一层"
                    + "（LayerId 是目标画面自己的那一个，不是源画面的）",
                    created.All(e => e.LayerId == target.DefaultLayer!.LayerId)
                    && target.DefaultLayer!.LayerId != devLayer.LayerId,
                    $"{created[0].LayerId}");

                Check("一次物化 = 一条撤销位、操作名是调用方给的那一个——粘贴 / 再制 / 插模板 / 拖模板四条路共用 Materialize，"
                    + "这条不变量就只在一处负责，不会出现「按一次 Ctrl+Z 只回退一个图元」",
                    ScadaEditHistory.UndoCount == 1 && ScadaEditHistory.NextUndoLabel == "粘贴 3 个图元",
                    $"{ScadaEditHistory.UndoCount} 条 / {ScadaEditHistory.NextUndoLabel}");

                ScadaEditHistory.Undo();

                Check("撤销一次：三件一起消失（不是只回退一个），原有图元不受影响",
                    target.Elements.Count == 1 && ReferenceEquals(target.Elements[0], home),
                    $"{target.Elements.Count} 件");

                var otherPage = new ScadaPage();
                otherPage.AddLayer("别的层");

                var landed = ScadaClipboard.Materialize(otherPage, payload, 0, 0, "插入模板 [阀门组]");

                Check("目标画面没有同名层 → 落默认图层（与「新拖出来的图元归默认层」同一条口径），"
                    + "而不是留一个指向已删除图层的悬空 Id",
                    landed.Count == 3 && landed.All(e => e.LayerId == otherPage.DefaultLayer!.LayerId),
                    $"{landed.Count} 件 → {otherPage.DefaultLayer!.Name}");

                ScadaEditHistory.Clear();

                Check("空负载 / null 画面：返回空表且不产记录（放下是失败，不该崩）",
                    ScadaClipboard.Materialize(target, ScadaClipboard.Capture(null, null), 0, 0, "粘贴 0 个图元").Count == 0
                    && ScadaClipboard.Materialize(null, payload, 0, 0, "粘贴").Count == 0
                    && ScadaEditHistory.UndoCount == 0,
                    "");

                // ----------------------------------------------------------
                // ③ GetBounds：模板拖放靠它把整块内容的左上角摆到鼠标处
                // ----------------------------------------------------------
                ScadaClipboard.GetBounds(payload, out double left, out double top, out double width, out double height);

                Check("GetBounds：外接框是全部快照的并集（left=100 / top=200 / width=240 / height=240）",
                    left == 100 && top == 200 && width == 240 && height == 240,
                    $"{left}/{top}/{width}/{height}");

                ScadaClipboard.GetBounds(null, out double zeroLeft, out double zeroTop, out double zeroWidth, out double zeroHeight);

                Check("GetBounds 空负载 / null：返回全 0（拖放算法据此判定「没内容可摆」，不必先判空）",
                    zeroLeft == 0 && zeroTop == 0 && zeroWidth == 0 && zeroHeight == 0,
                    "");

                // ----------------------------------------------------------
                // ④ 编辑器视图模型：复制 / 粘贴 / 再制 / 阶梯错开
                //
                //  Strict 打开跑这一段：这是 D3「改模型只有一个入口」的机器检查——
                //  新加的复制粘贴路径若有一步落在作用域外，这里会直接抛。
                // ----------------------------------------------------------
                var workspace = new WorkspaceContext();
                workspace.GlobalVariables.Clear();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                workspace.SwitchSolution(solution);

                var editor = new ScadaEditorVM(workspace, null!);

                ScadaWriteGuard.Strict = true;

                try
                {
                    ScadaClipboard.Clear();
                    editor.AddPageCommand.Execute();

                    var e1 = editor.AddElement("Hmi.Rectangle", new Point(0, 0))!;
                    var e2 = editor.AddElement("Hmi.Rectangle", new Point(50, 0))!;

                    editor.SelectedElements = new[] { e1, e2 };
                    ScadaEditHistory.Clear();

                    bool copied = editor.CopySelection();

                    Check("复制：选中两个 → 剪贴板有货、快照两件；且不产撤销位"
                        + "（复制是读操作，Ctrl+Z 不该把「复制」撤掉）",
                        copied && ScadaClipboard.HasPayload && ScadaClipboard.Payload!.Items.Count == 2
                        && ScadaEditHistory.UndoCount == 0,
                        $"{ScadaClipboard.Payload?.Items.Count} 件 / UndoCount={ScadaEditHistory.UndoCount}");

                    bool canPasteLoaded = editor.CanPasteClipboard();
                    ScadaClipboard.Clear();
                    bool canPasteEmpty = editor.CanPasteClipboard();
                    editor.CopySelection();

                    Check("「粘贴」的判据是两半：手上有货 + 当前有画面才可点；剪贴板一空立刻判灰"
                        + "（两半各有一处刷新点，缺一处就会留下一个灰错的按钮）",
                        canPasteLoaded && !canPasteEmpty,
                        $"有货={canPasteLoaded} / 空={canPasteEmpty}");

                    bool pasted = editor.PasteClipboard();

                    Check("粘贴：物化两件、整批选中（粘完紧接着十有八九是拖到位置上，让他再框选一次是白费一步）、"
                        + "一次操作一条撤销位，操作名带件数",
                        pasted && editor.SelectedPage!.Elements.Count == 4
                        && editor.SelectedElements.Count == 2
                        && ScadaEditHistory.UndoCount == 1
                        && ScadaEditHistory.NextUndoLabel == "粘贴 2 个图元",
                        $"{editor.SelectedPage!.Elements.Count} 件 / 选中 {editor.SelectedElements.Count} / {ScadaEditHistory.NextUndoLabel}");

                    Check("阶梯错开第一格：偏移 12（PasteStep × 1）——不给偏移的话副本与原件像素级重合，"
                        + "用户看到的是「按了 Ctrl+V 没反应」，直到拖开上面那个才会发现下面还压着一个",
                        editor.SelectedElements[0].X == 12 && editor.SelectedElements[0].Y == 12,
                        $"({editor.SelectedElements[0].X},{editor.SelectedElements[0].Y})");

                    editor.PasteClipboard();

                    Check("阶梯错开第二格：偏移 24（步长 × 次数）——连续 Ctrl+V 阶梯式排开，而不是叠在同一格上",
                        editor.SelectedElements[0].X == 24 && editor.SelectedElements[0].Y == 24,
                        $"({editor.SelectedElements[0].X},{editor.SelectedElements[0].Y})");

                    editor.SelectedElements = new[] { e1 };
                    editor.CopySelection();
                    editor.PasteClipboard();

                    Check("重新复制一次就归零：偏移回到第一格 12——不归零的话复制完再粘会莫名其妙跳到 (36,36)",
                        editor.SelectedElements[0].X == 12,
                        $"({editor.SelectedElements[0].X},{editor.SelectedElements[0].Y})");

                    var clipboardBeforeDuplicate = ScadaClipboard.Payload;

                    editor.SelectedElements = new[] { e2 };
                    ScadaEditHistory.Clear();

                    bool duplicated = editor.DuplicateSelection();

                    Check("再制：复制一份立刻贴到本画面、整批选中、一条撤销位，操作名是「再制 [名字]」",
                        duplicated && editor.SelectedElements.Count == 1
                        && ScadaEditHistory.UndoCount == 1
                        && ScadaEditHistory.NextUndoLabel == $"再制 [{e2.Name}]",
                        $"{ScadaEditHistory.NextUndoLabel}");

                    Check("再制刻意不碰剪贴板（连引用都没换）——否则用户在别的画面复制了一批东西，"
                        + "回来按一次 Ctrl+D 剪贴板就变成这几个图元了，待会儿切回去想粘贴时粘出来的是错的",
                        ReferenceEquals(ScadaClipboard.Payload, clipboardBeforeDuplicate),
                        "");

                    editor.SelectedElements = Array.Empty<ScadaElement>();

                    Check("没选中任何图元：复制 / 再制 / 存模板三个命令一起判灰"
                        + "（三条共用 CanCopySelection 一个判据，所以一起刷）",
                        !editor.CanCopySelection()
                        && !editor.CopyCommand.CanExecute()
                        && !editor.DuplicateCommand.CanExecute()
                        && !editor.SaveTemplateCommand.CanExecute(),
                        "");

                    // ----------------------------------------------------------
                    // ⑤ 锁定图元的过滤口径：读的不挡（复制 / 再制），写的不放（删除）
                    // ----------------------------------------------------------
                    var locked = editor.AddElement("Hmi.Rectangle", new Point(200, 0))!;

                    editor.SelectedPage!.TrySetElementLocked(new[] { locked }, true, out _);
                    editor.SelectedElements = new[] { locked };
                    ScadaEditHistory.Clear();

                    bool copiedLocked = editor.CopySelection();

                    Check("复制读得出锁定的图元，连 IsLocked 一起搬进快照（走 CollectPresent，不筛锁）——"
                        + "「照这张锁住的底图再做一个」是现场真实诉求，被锁挡住只会逼用户先解锁、复制、再锁回去，"
                        + "而这三步里任何一步忘了，画面上就多出一张没锁的底图",
                        copiedLocked && ScadaClipboard.Payload!.Items.Count == 1
                        && ScadaClipboard.Payload.Items[0].IsLocked,
                        $"{ScadaClipboard.Payload?.Items.Count} 件 / 锁={ScadaClipboard.Payload?.Items[0].IsLocked}");

                    Check("同一批选中下，「删除」判灰而「复制 / 再制 / 存模板 / 解锁」亮着——两条漏斗的分界线就在这里："
                        + "CollectEditable 管写（删除、对齐、拖动），CollectPresent 管读（复制、再制、存模板）与解锁",
                        !editor.CanRemoveSelectedElement() && editor.CanCopySelection()
                        && editor.CopyCommand.CanExecute() && editor.DuplicateCommand.CanExecute()
                        && editor.SaveTemplateCommand.CanExecute() && editor.LockElementCommand.CanExecute(),
                        $"删除={editor.CanRemoveSelectedElement()} / 复制={editor.CanCopySelection()}");

                    ScadaEditHistory.Clear();

                    bool duplicatedLocked = editor.DuplicateSelection();

                    Check("再制一个锁定的图元：副本也带着锁（锁是图元自身的属性，照搬才是「一模一样的一份」），"
                        + "且全程不碰剪贴板——上一格复制进去的那份快照原封不动",
                        duplicatedLocked && editor.SelectedElements.Count == 1
                        && editor.SelectedElements[0].IsLocked
                        && editor.SelectedElements[0].ElementId != locked.ElementId
                        && ScadaClipboard.Payload!.Items.Count == 1,
                        $"{editor.SelectedElements.Count} 件 / 锁={editor.SelectedElements[0].IsLocked}");
                }
                finally
                {
                    editor.Deactivate();
                }

                // ----------------------------------------------------------
                // ⑥ 模板库：用临时目录里的实例，绝不碰程序目录那份 Shared
                //
                //  为什么不用 ScadaTemplateStore.Shared：断言跑在开发机上，写进程序目录
                //  就等于把开发者自己的模板库改掉，而且用例之间会互相串味（前一个用例
                //  存下的模板会出现在后一个用例的清单里）。
                // ----------------------------------------------------------
                var storeDir = Path.Combine(
                    Path.GetTempPath(), "ScadaChecks_Templates_" + Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(storeDir);

                try
                {
                    int changed = 0;

                    var store = new ScadaTemplateStore(Path.Combine(storeDir, ScadaTemplateStore.DefaultFileName));

                    store.Changed += (_, _) => changed++;

                    Check("首次运行：文件不存在 → 空清单、非只读、无错误，且不落盘"
                        + "（只是打开过一次软件不该留下一个空文件，与 ScadaUserStore 同口径）",
                        store.Templates.Count == 0 && !store.IsReadOnly && store.LastLoadError == null
                        && !File.Exists(store.StorePath),
                        $"{store.Templates.Count} 条 / 只读={store.IsReadOnly} / 存在={File.Exists(store.StorePath)}");

                    bool savedA = store.TrySave("三通阀", payload, out var savedAInfo, out string? saveAError);

                    Check("存模板：返回 true、给回落库后的那一行（名字 / 件数 / Id 都齐），广播一次 Changed，文件真的落盘了",
                        savedA && savedAInfo != null && savedAInfo.Name == "三通阀"
                        && savedAInfo.ItemCount == 3 && savedAInfo.TemplateId != Guid.Empty
                        && changed == 1 && File.Exists(store.StorePath),
                        $"{savedAInfo?.Name} / {savedAInfo?.ItemCount} 件 / Changed={changed} / {saveAError}");

                    store.TrySave("三通阀", payload, out var savedBInfo, out _);

                    Check("重名自动避让成「三通阀_2」——存模板不该因为撞名就失败（用户只是想再存一个差不多的），"
                        + "更不该悄悄覆盖掉原来那一个",
                        savedBInfo?.Name == "三通阀_2" && changed == 2,
                        $"{savedBInfo?.Name} / Changed={changed}");

                    string? longNameError = null;
                    string? emptyPayloadError = null;

                    Check("空名 / 超长名各给一句人话：Trim 后为空 → 「模板名不能为空」；超 32 字 → 「模板名不能超过 32 个字符」",
                        !store.TrySave("   ", payload, out _, out string? emptyNameError)
                        && emptyNameError == "模板名不能为空"
                        && !store.TrySave(new string('长', ScadaTemplateStore.MaxNameLength + 1), payload, out _, out longNameError)
                        && longNameError != null && longNameError.Contains("32"),
                        $"{emptyNameError} / {longNameError}");

                    Check("空负载一律拒收，给出「没有可保存的图元」——存进去在工具箱里是一行点不动的项，用户会当成软件坏了",
                        !store.TrySave("空壳", null, out _, out string? nullPayloadError)
                        && nullPayloadError == "没有可保存的图元"
                        && !store.TrySave("空壳", ScadaClipboard.Capture(null, null), out _, out emptyPayloadError)
                        && emptyPayloadError == "没有可保存的图元",
                        $"{nullPayloadError} / {emptyPayloadError}");

                    Check("被拒的这几次一个 Changed 都没发、清单也没多——校验全部排在写盘之前",
                        changed == 2 && store.Templates.Count == 2,
                        $"Changed={changed} / {store.Templates.Count} 条");

                    var names = store.Templates.Select(t => t.Name).ToArray();

                    Check("清单按名字排序（忽略大小写）：「三通阀」排在「三通阀_2」前面——"
                        + "现场没人记得住当初存的是 Valve 还是 valve，排序口径统一了，找东西就只靠眼睛扫一遍",
                        names.Length == 2 && names[0] == "三通阀" && names[1] == "三通阀_2",
                        string.Join("/", names));

                    var fetched = store.GetPayload(savedAInfo!.TemplateId);

                    Check("GetPayload 按 Id 取回负载（3 件、头一件就是当初那件「底图」），取一个不存在的 Id 返回 null",
                        fetched != null && fetched.Items.Count == 3 && fetched.Items[0].Name == "底图"
                        && store.GetPayload(Guid.NewGuid()) == null,
                        $"{fetched?.Items.Count} 件");

                    int beforeNoopRename = changed;

                    bool sameName = store.TryRename(savedAInfo.TemplateId, "  三通阀  ", out string? sameNameError);

                    Check("改成同名（Trim 之后相同）：算成功但是空操作——不写盘、不发 Changed"
                        + "（在改名框里按一下回车、没改字，是最常见的一次误操作，不该换来一次无意义的文件替换）",
                        sameName && sameNameError == null && changed == beforeNoopRename
                        && store.Templates[0].Name == "三通阀",
                        $"{sameNameError ?? "null"} / Changed={changed}");

                    bool dupRename = store.TryRename(savedAInfo.TemplateId, "三通阀_2", out string? dupRenameError);

                    Check("改成另一个已存在的名字：拒绝并给出「已有名为「三通阀_2」的模板」——"
                        + "改名不像存新模板那样可以避让：用户明确输入的名字被悄悄改成别的，才是真正糟糕的事",
                        !dupRename && dupRenameError != null && dupRenameError.Contains("已有名为"),
                        $"{dupRenameError}");

                    bool missingRename = store.TryRename(Guid.NewGuid(), "随便", out string? missingError);

                    Check("改一个不存在的 Id：拒绝并给出「模板不存在，可能已被删除」（界面上那一行可能已经过期了）",
                        !missingRename && missingError != null && missingError.Contains("模板不存在"),
                        $"{missingError}");

                    bool renamed = store.TryRename(savedBInfo!.TemplateId, "三通阀_备用", out _);

                    Check("正常改名：名字落库、广播一次 Changed",
                        renamed && changed == beforeNoopRename + 1
                        && store.Templates.Any(t => t.Name == "三通阀_备用"),
                        $"Changed={changed}");

                    var reloaded = new ScadaTemplateStore(store.StorePath);

                    Check("换个实例重读同一个文件：两条模板都在、件数与名字逐字一致"
                        + "（文件格式自洽，不靠内存里那一份的侥幸）",
                        reloaded.Templates.Count == 2
                        && reloaded.Templates.Any(t => t.Name == "三通阀" && t.ItemCount == 3)
                        && reloaded.Templates.Any(t => t.Name == "三通阀_备用" && t.ItemCount == 3)
                        && !reloaded.IsReadOnly && reloaded.LastLoadError == null,
                        string.Join("/", reloaded.Templates.Select(t => t.Name)));

                    int beforeRemove = changed;

                    bool removed = store.TryRemove(savedBInfo.TemplateId, out _);
                    bool removeAgain = store.TryRemove(savedBInfo.TemplateId, out string? removeAgainError);

                    Check("删除：清单少一条、广播一次 Changed；再删同一个 Id 给出「模板不存在，可能已被删除」",
                        removed && store.Templates.Count == 1 && changed == beforeRemove + 1
                        && !removeAgain && removeAgainError != null && removeAgainError.Contains("模板不存在"),
                        $"{store.Templates.Count} 条 / {removeAgainError}");

                    // 只读：磁盘上是更新版本（v99）写的文件。记录本身合法，所以照样读得出来，
                    // 只是三个写操作全得挡住——不然下次本版本一写盘，v99 的字段就永久丢了。
                    var readOnlyPath = Path.Combine(storeDir, "ReadOnly.json");

                    File.WriteAllText(readOnlyPath, JsonConvert.SerializeObject(new
                    {
                        Version = 99,
                        Templates = new object[]
                        {
                            new { TemplateId = Guid.NewGuid(), Name = "旧模板", CreatedUtc = DateTime.UtcNow, Payload = payload },
                        },
                    }));

                    var readOnlyStore = new ScadaTemplateStore(readOnlyPath);
                    var readOnlyRecord = readOnlyStore.Templates[0];

                    bool roSave = readOnlyStore.TrySave("新模板", payload, out _, out string? roSaveError);
                    bool roRename = readOnlyStore.TryRename(readOnlyRecord.TemplateId, "改个名", out string? roRenameError);
                    bool roRemove = readOnlyStore.TryRemove(readOnlyRecord.TemplateId, out string? roRemoveError);

                    Check("模板文件版本更高（v99）→ 只读：清单照常读得出来，但存 / 改名 / 删三个写操作全拒，"
                        + "且都说明白「为什么存不进去」——不说的话用户只会以为模板功能坏了，然后一遍遍重试",
                        readOnlyStore.IsReadOnly
                        && readOnlyStore.Templates.Count == 1 && readOnlyRecord.Name == "旧模板"
                        && readOnlyStore.LastLoadError != null && readOnlyStore.LastLoadError.Contains("更新版本")
                        && !roSave && roSaveError != null && roSaveError.Contains("只读")
                        && !roRename && roRenameError != null && roRenameError.Contains("只读")
                        && !roRemove && roRemoveError != null && roRemoveError.Contains("只读")
                        && File.ReadAllText(readOnlyPath).Contains("旧模板")
                        && !File.ReadAllText(readOnlyPath).Contains("改个名"),
                        $"{readOnlyStore.LastLoadError} / {roSaveError}");

                    // 残缺记录：手工编辑、写到一半、别的版本写的字段——逐条跳过
                    var brokenPath = Path.Combine(storeDir, "Broken.json");

                    File.WriteAllText(brokenPath, JsonConvert.SerializeObject(new
                    {
                        Version = 1,
                        Templates = new object[]
                        {
                            new { TemplateId = Guid.NewGuid(), Name = "好模板", CreatedUtc = DateTime.UtcNow, Payload = payload },
                            new { TemplateId = Guid.Empty, Name = "没身份", CreatedUtc = DateTime.UtcNow, Payload = payload },
                            new { TemplateId = Guid.NewGuid(), Name = "   ", CreatedUtc = DateTime.UtcNow, Payload = payload },
                            new { TemplateId = Guid.NewGuid(), Name = "没内容", CreatedUtc = DateTime.UtcNow, Payload = (ScadaClipboardPayload?)null },
                        },
                    }));

                    var brokenStore = new ScadaTemplateStore(brokenPath);

                    Check("残缺记录逐条跳过、好的那条照常读进来，并留一句「另有 N 条模板记录不可用，已跳过」——"
                        + "留着它们不会多出什么，但在工具箱里是一行点不动的项，用户只会觉得软件坏了",
                        brokenStore.Templates.Count == 1 && brokenStore.Templates[0].Name == "好模板"
                        && brokenStore.LastLoadError != null && brokenStore.LastLoadError.Contains("跳过")
                        && !brokenStore.IsReadOnly,
                        $"{brokenStore.Templates.Count} 条 / {brokenStore.LastLoadError}");

                    // 文件读坏：按空清单处理，但**允许写入**
                    var garbagePath = Path.Combine(storeDir, "Garbage.json");

                    File.WriteAllText(garbagePath, "{ 这不是 JSON");

                    var garbageStore = new ScadaTemplateStore(garbagePath);

                    Check("文件读坏：按空清单处理但允许写入——否则用户再也存不进任何模板，"
                        + "而那个坏文件本来也读不出东西（与 ScadaUserStore 读坏回落出厂账号同一取舍）",
                        garbageStore.Templates.Count == 0 && !garbageStore.IsReadOnly
                        && garbageStore.LastLoadError != null && garbageStore.LastLoadError.Contains("读取失败")
                        && garbageStore.TrySave("重来", payload, out _, out _),
                        $"{garbageStore.LastLoadError}");
                }
                finally
                {
                    // 临时目录删不掉（被占用之类）不该让断言红——它不是被测行为的一部分
                    try { Directory.Delete(storeDir, true); } catch { }
                }

                // ----------------------------------------------------------
                // ⑦ 工具箱：Attach / Detach 的可逆订阅（静态事件最容易漏的那一处）
                // ----------------------------------------------------------
                var toolbox = new ScadaToolboxVM();
                var notices = new List<string>();

                toolbox.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != null) notices.Add(e.PropertyName);
                };

                toolbox.Attach();
                toolbox.Attach();   // 视图的 Loaded 可能来第二次

                Check("Attach 幂等：连调两次只读一次库、只通知一遍"
                    + "（面板被 AvalonDock 重新挂回可视树时 Loaded 会再来一次，不幂等就会重复读盘）",
                    notices.Count(n => n == nameof(ScadaToolboxVM.Templates)) == 1,
                    string.Join("/", notices));

                Check("读一次就通知三个属性：Templates（清单）/ TemplateCount（分组标题上那个数字）/ HasTemplates（没有模板时那一栏整块收起来）",
                    notices.Contains(nameof(ScadaToolboxVM.Templates))
                    && notices.Contains(nameof(ScadaToolboxVM.TemplateCount))
                    && notices.Contains(nameof(ScadaToolboxVM.HasTemplates)),
                    string.Join("/", notices));

                notices.Clear();

                // Shared.Load() 只读盘、不写盘，拿它当"库广播了一次 Changed"的干净触发器
                ScadaTemplateStore.Shared.Load();

                Check("库广播 Changed → 工具箱整份重读：存模板的入口在画布右键菜单里、看模板的地方在这个面板上，"
                    + "两处谁都不认识谁，中间只搁这一个通知（刷新点只有一个，「某个入口忘了刷」在结构上就不可能发生）",
                    notices.Count(n => n == nameof(ScadaToolboxVM.Templates)) == 1,
                    string.Join("/", notices));

                toolbox.Detach();
                notices.Clear();

                ScadaTemplateStore.Shared.Load();

                Check("Detach 之后摘干净：库再广播也打不到这个视图模型上——Shared 是静态的，"
                    + "订阅了不摘，那个订阅者就再也回收不了；而工具箱面板正是随 AvalonDock 反复装卸的那一种，"
                    + "几轮下来就会攒下一串永远收不到通知的僵尸",
                    notices.Count == 0,
                    string.Join("/", notices));

                // 搜索框也管模板：与图元分组同一口径（命中数进标题、一条都不命中就整块收起来）
                toolbox.SearchText = "\u0001";

                Check("搜索词一个模板都没命中 → 那一栏整块收起来、计数归零（与图元分组同一口径：空组不占位）——"
                    + "现场攒到几十份模板时，搜索框就在正上方，模板若不过滤就只能一行行扫",
                    !toolbox.HasTemplates && toolbox.Templates.Count == 0 && toolbox.TemplateCount == 0,
                    $"{toolbox.Templates.Count} 份 / HasTemplates={toolbox.HasTemplates}");

                toolbox.SearchText = string.Empty;

                Check("清空搜索词 → 模板清单恢复全量（逐一对齐库里的条数，不靠「清词时记得重读」这种约定）",
                    toolbox.TemplateCount == ScadaTemplateStore.Shared.Templates.Count
                    && toolbox.HasTemplates == (ScadaTemplateStore.Shared.Templates.Count > 0),
                    $"{toolbox.TemplateCount} 份 / 库里 {ScadaTemplateStore.Shared.Templates.Count} 份");
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaClipboard.Clear();
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [AH] 报警引擎（S11）
        //
        //  为什么这一整段不需要 STA 线程：ScadaDocument 与 ScadaAlarmEngine 都是纯 .NET，
        //  零 WPF 依赖（D1）。这也是把报警从 ScadaRuntime 里拆出来的收益之一——
        //  最讲时间的那部分逻辑，反而能在控制台里一口气把十分钟推过去。
        // ==================================================================
        private static void AlarmChecks()
        {
            Section("[AH] 报警引擎（S11）");

            // 撤销栈是全局静态的（单栈双向），本段自己压栈、出段必清
            ScadaEditHistory.Clear();
            var strictBackup = ScadaWriteGuard.Strict;

            // 假时钟：所有"现在几点"都从它取。断言因此能把十分钟一口气推过去，
            // 不必靠 Thread.Sleep 去等真实时间（那种"偶尔红一次"的断言最后一定会被当噪声忽略）。
            DateTime now = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;

            try
            {
                // ----------------------------------------------------------
                // ① 定义的身份与默认值（钉 s11-a4：新建报警必须自带身份）
                // ----------------------------------------------------------
                var doc = new ScadaDocument();

                var unnamed = doc.AddAlarm();
                Check("新建报警自带非空身份（否则运行中点确认会被引擎按 Guid.Empty 拒掉，表现是「点了没反应」）",
                    unnamed.AlarmId != Guid.Empty, $"Id={unnamed.AlarmId}");

                Check("报警名留空时按「报警_N」自动命名（N 取当前未被占用的最小序号）",
                    unnamed.Name == "报警_1", unnamed.Name);

                var highHigh = doc.AddAlarm("高高", ScadaAlarmKind.HighHigh);
                var low = doc.AddAlarm("低限", ScadaAlarmKind.Low);
                Check("默认严重度由种类给一次：高高限=严重、低限=警告",
                    highHigh.Severity == ScadaAlarmSeverity.Critical
                    && low.Severity == ScadaAlarmSeverity.Warning,
                    $"{highHigh.Severity.DisplayName()} / {low.Severity.DisplayName()}");

                highHigh.Kind = ScadaAlarmKind.High;
                Check("改种类不联动严重度（用户特意调过的严重度不该被静默推翻）",
                    highHigh.Severity == ScadaAlarmSeverity.Critical, highHigh.Severity.DisplayName());

                int undoBeforeRename = ScadaEditHistory.UndoCount;
                bool emptyNameRejected = !doc.TryRenameAlarm(unnamed, "   ", out var emptyNameError);
                bool sameNameIdempotent = doc.TryRenameAlarm(unnamed, "报警_1", out _)
                                          && ScadaEditHistory.UndoCount == undoBeforeRename;
                bool dupNameRejected = !doc.TryRenameAlarm(unnamed, "高高", out var dupNameError);
                bool renamedOk = doc.TryRenameAlarm(unnamed, "炉温高", out _);
                Check("报警改名：空名拒、与原值等值幂等放行（不刷一次变更）、重名拒",
                    emptyNameRejected && sameNameIdempotent && dupNameRejected && renamedOk
                    && unnamed.Name == "炉温高",
                    $"{emptyNameError} / {dupNameError}");

                Check("按 Id / 按名都能找回定义（Id 是权威键，名字只用于兼容旧数据与手输）",
                    ReferenceEquals(doc.FindAlarm(unnamed.AlarmId), unnamed)
                    && ReferenceEquals(doc.FindAlarmByName("炉温高"), unnamed)
                    && doc.FindAlarm(Guid.Empty) == null,
                    "");

                while (doc.Alarms.Count > 0)
                    doc.TryRemoveAlarm(doc.Alarms[0], out _);

                Check("删报警允许删到一条不剩（与「至少留一个画面」刻意不同：不需要报警是完全正常的组态结果）",
                    doc.Alarms.Count == 0, $"alarms={doc.Alarms.Count}");

                // ----------------------------------------------------------
                // ② 四限 + 回差：进来按裸阈值判，出去要多让开一个回差
                // ----------------------------------------------------------
                var limitDoc = new ScadaDocument();
                var speedId = Guid.NewGuid();
                var speedHandle = new FakeValueHandle
                {
                    VariableId = speedId,
                    Name = "Speed",
                    DataType = typeof(double),
                    Value = 70d
                };
                var limitSource = new FakeValueSource();
                limitSource.ById[speedId] = speedHandle;

                var highAlarm = limitDoc.AddAlarm("转速高", ScadaAlarmKind.High);
                highAlarm.Bind(speedId, "Speed");
                highAlarm.Threshold = 80;
                highAlarm.Deadband = 5;

                var engine = new ScadaAlarmEngine(limitDoc, limitSource, clock);

                int raisedCount = 0, changedCount = 0, clearedCount = 0;
                engine.AlarmRaised += _ => raisedCount++;
                engine.AlarmChanged += _ => changedCount++;
                engine.AlarmCleared += _ => clearedCount++;

                engine.Attach();

                Check("挂载后按定义条数建槽、并订阅上变量（通道订阅者数就是「挂上了」的证据）",
                    engine.IsAttached && engine.SlotCount == 1 && engine.SubscribedCount == 1
                    && speedHandle.Subscribers == 1,
                    $"槽={engine.SlotCount} / 订阅={engine.SubscribedCount} / 通道订阅者={speedHandle.Subscribers}");

                Check("初判：挂载时值 70 未越限 → 一条报警都没有",
                    engine.ActiveAlarms.Count == 0 && engine.HighestActiveSeverity == null,
                    $"active={engine.ActiveAlarms.Count}");

                speedHandle.Raise(85d);
                Check("值越限 → 立即激活（延时 0），记录里带着「报的时候是多少」",
                    engine.ActiveAlarms.Count == 1
                    && engine.ActiveAlarms[0].State == ScadaAlarmState.Active
                    && engine.ActiveAlarms[0].TriggerValue == "85",
                    $"{engine.ActiveAlarms.FirstOrDefault()?.State.DisplayName()} / 值={engine.ActiveAlarms.FirstOrDefault()?.TriggerValue}");

                Check("激活时发两条事件：AlarmRaised（落盘/弹窗挂它）+ AlarmChanged（列表刷新挂它）",
                    raisedCount == 1 && changedCount == 1, $"raised={raisedCount} / changed={changedCount}");

                speedHandle.Raise(78d);
                Check("值落进回差带内（78 > 80-5）→ 不算恢复：回差吸收抖动的地方就在这一段宽度里",
                    engine.ActiveAlarms.Count == 1
                    && engine.ActiveAlarms[0].State == ScadaAlarmState.Active
                    && changedCount == 1,
                    $"{engine.ActiveAlarms.FirstOrDefault()?.State.DisplayName()} / changed={changedCount}");

                speedHandle.Raise(70d);
                var recoveredAt = engine.ActiveAlarms.FirstOrDefault()?.RecoveredAtUtc;
                Check("值退出回差带（70 < 75）→ 未确认就恢复，停在「已恢复未确认」等一次确认",
                    engine.ActiveAlarms.Count == 1
                    && engine.ActiveAlarms[0].State == ScadaAlarmState.Recovered
                    && engine.ActiveAlarms[0].NeedsAcknowledge
                    && engine.ActiveAlarms[0].RecoveredAtUtc != null
                    && engine.UnacknowledgedCount == 1,
                    $"{engine.ActiveAlarms.FirstOrDefault()?.State.DisplayName()}");

                int changedBefore = changedCount;
                engine.Tick();
                Check("已经处于「已恢复未确认」再判一次：不重复发事件、恢复时刻不被一路往后推",
                    changedCount == changedBefore
                    && engine.ActiveAlarms[0].RecoveredAtUtc == recoveredAt,
                    $"changed {changedBefore} → {changedCount}");

                int acked = engine.Acknowledge(engine.ActiveAlarms[0].AlarmId);
                Check("确认「已恢复未确认」→ 彻底了结：从实时列表消失、记录补上清除时刻、发一条 AlarmCleared",
                    acked == 1 && engine.ActiveAlarms.Count == 0
                    && engine.History.Count == 1
                    && engine.History[0].State == ScadaAlarmState.Normal
                    && engine.History[0].ClearedAtUtc != null
                    && clearedCount == 1,
                    $"确认 {acked} 条 / cleared={clearedCount}");

                Check("确认空身份 / 一条已经了结的报警 → 返回 0（「全部确认」的条数不能虚高）",
                    engine.Acknowledge(Guid.Empty) == 0
                    && engine.Acknowledge(engine.History[0].AlarmId) == 0, "");

                // ----------------------------------------------------------
                // ③ 态机的另一条路：先确认（报警还挂着）→ 条件恢复 → 直接了结
                // ----------------------------------------------------------
                speedHandle.Raise(90d);
                var second = engine.ActiveAlarms[0];
                Check("重新越限 → 同一条定义产生新的一次报警记录（记录是「一次激活→清除」的完整过程）",
                    engine.ActiveAlarms.Count == 1
                    && second.State == ScadaAlarmState.Active
                    && !ReferenceEquals(second, engine.History[0])
                    && engine.History.Count == 2,
                    $"history={engine.History.Count}");

                engine.Acknowledge(second.AlarmId);
                Check("确认仍在报警的那条 → 转「已确认」：报警还挂着，但不再等确认",
                    second.State == ScadaAlarmState.Acknowledged
                    && second.IsActive && !second.NeedsAcknowledge
                    && engine.UnacknowledgedCount == 0,
                    $"{second.State.DisplayName()} / IsActive={second.IsActive}");

                speedHandle.Raise(60d);
                Check("已确认的那条条件恢复 → 直接了结（操作员早知道并处理完了，不该再拦他一次确认）",
                    second.State == ScadaAlarmState.Normal
                    && engine.ActiveAlarms.Count == 0
                    && second.ClearedAtUtc != null,
                    $"{second.State.DisplayName()}");

                // ----------------------------------------------------------
                // ④ 「已恢复未确认」时条件又成立 → 退回激活
                // ----------------------------------------------------------
                speedHandle.Raise(90d);
                speedHandle.Raise(60d);
                var roundTrip = engine.ActiveAlarms[0];
                Check("构造出「已恢复未确认」这一态",
                    roundTrip.State == ScadaAlarmState.Recovered && roundTrip.RecoveredAtUtc != null, "");

                speedHandle.Raise(90d);
                Check("已恢复未确认时条件再次成立 → 退回激活并清掉恢复时刻（时长继续累计，不装没事）",
                    roundTrip.State == ScadaAlarmState.Active && roundTrip.RecoveredAtUtc == null,
                    $"{roundTrip.State.DisplayName()} / RecoveredAt={roundTrip.RecoveredAtUtc?.ToString() ?? "null"}");

                engine.AcknowledgeAll();
                speedHandle.Raise(60d);

                // ----------------------------------------------------------
                // ⑤ 布尔量：BoolOn / BoolOff，含「拿不到值时不误报」
                // ----------------------------------------------------------
                var boolDoc = new ScadaDocument();
                var runId = Guid.NewGuid();
                var nullId = Guid.NewGuid();
                var runHandle = new FakeValueHandle
                {
                    VariableId = runId, Name = "Run", DataType = typeof(bool), Value = true
                };
                var nullHandle = new FakeValueHandle
                {
                    VariableId = nullId, Name = "NullVar", DataType = typeof(object), Value = null
                };
                var boolSource = new FakeValueSource();
                boolSource.ById[runId] = runHandle;
                boolSource.ById[nullId] = nullHandle;

                var stopAlarm = boolDoc.AddAlarm("停机", ScadaAlarmKind.BoolOff);
                stopAlarm.Bind(runId, "Run");

                var nullAlarm = boolDoc.AddAlarm("空值", ScadaAlarmKind.BoolOff);
                nullAlarm.Bind(nullId, "NullVar");

                var boolEngine = new ScadaAlarmEngine(boolDoc, boolSource, clock);
                boolEngine.Attach();

                Check("初判：真值不触发 BoolOff；空值判不了 → 按「没触发」处理（判不了就报会刷一堆假报警）",
                    boolEngine.ActiveAlarms.Count == 0, $"active={boolEngine.ActiveAlarms.Count}");

                runHandle.Raise(false);
                Check("BoolOff：值变假 → 报警",
                    boolEngine.ActiveAlarms.Any(r => r.AlarmId == stopAlarm.AlarmId),
                    $"{boolEngine.ActiveAlarms.FirstOrDefault()?.State.DisplayName()}");

                nullHandle.Raise(0);
                Check("布尔量存成 0/1 也照收（PLC 侧本来就是位）：0 视为假 → BoolOff 报警",
                    boolEngine.ActiveAlarms.Any(r => r.AlarmId == nullAlarm.AlarmId),
                    $"active={boolEngine.ActiveAlarms.Count}");

                runHandle.Raise(true);
                Check("值变真 → 恢复（未确认 → 已恢复未确认）",
                    boolEngine.ActiveAlarms.FirstOrDefault(r => r.AlarmId == stopAlarm.AlarmId)
                        is { State: ScadaAlarmState.Recovered }, "");

                // ----------------------------------------------------------
                // ⑥ 通信断线：多久没收到新值算断线；断线不吃延时叠层
                // ----------------------------------------------------------
                var staleDoc = new ScadaDocument();
                var staleId = Guid.NewGuid();
                var staleHandle = new FakeValueHandle
                {
                    VariableId = staleId, Name = "Temp", DataType = typeof(double), Value = 25d
                };
                var staleSource = new FakeValueSource();
                staleSource.ById[staleId] = staleHandle;

                var staleAlarm = staleDoc.AddAlarm("温度断线", ScadaAlarmKind.Stale);
                staleAlarm.Bind(staleId, "Temp");
                staleAlarm.StaleSeconds = 10;
                staleAlarm.DelaySeconds = 30; // 故意配一个很大的延时：断线不该再叠这一层

                var staleEngine = new ScadaAlarmEngine(staleDoc, staleSource, clock);
                staleEngine.Attach();

                Check("刚挂载时算「刚收到过值」→ 不算断线", staleEngine.ActiveAlarms.Count == 0, "");

                now = now.AddSeconds(11);
                staleEngine.Tick();
                Check("超过超时 11s 没收到新值 → 断线报警；且不被 30s 延时挡住（超时本身就是「等够了」的结论，再叠一层等于把超时翻倍）",
                    staleEngine.ActiveAlarms.Count == 1
                    && staleEngine.ActiveAlarms[0].Kind == ScadaAlarmKind.Stale
                    && staleEngine.ActiveAlarms[0].State == ScadaAlarmState.Active,
                    $"{staleEngine.ActiveAlarms.FirstOrDefault()?.ConditionText}");

                staleHandle.Raise(26d);
                Check("收到新值 → 断线恢复（未确认 → 已恢复未确认）",
                    staleEngine.ActiveAlarms.FirstOrDefault() is { State: ScadaAlarmState.Recovered }, "");

                // ----------------------------------------------------------
                // ⑦ 激活延时：条件必须连续成立够久；中途断一次要重新计时
                // ----------------------------------------------------------
                var delayDoc = new ScadaDocument();
                var delayId = Guid.NewGuid();
                var delayHandle = new FakeValueHandle
                {
                    VariableId = delayId, Name = "Pressure", DataType = typeof(double), Value = 0d
                };
                var delaySource = new FakeValueSource();
                delaySource.ById[delayId] = delayHandle;

                var delayAlarm = delayDoc.AddAlarm("压力高", ScadaAlarmKind.High);
                delayAlarm.Bind(delayId, "Pressure");
                delayAlarm.Threshold = 10;
                delayAlarm.DelaySeconds = 5;

                var delayEngine = new ScadaAlarmEngine(delayDoc, delaySource, clock);
                delayEngine.Attach();

                delayHandle.Raise(20d);
                Check("条件刚成立 → 不报（延时 5s 还没到）", delayEngine.ActiveAlarms.Count == 0, "");

                now = now.AddSeconds(3);
                delayEngine.Tick();
                Check("过了 3s 仍不报", delayEngine.ActiveAlarms.Count == 0, "");

                delayHandle.Raise(0d); // 条件断了
                now = now.AddSeconds(1);
                delayEngine.Tick();
                delayHandle.Raise(20d); // 又成立：计时必须从这一刻重新开始
                now = now.AddSeconds(3);
                delayEngine.Tick();
                Check("中途断一次 → 计时重置：重新成立后再过 3s 仍不报（否则会误报成「累计 6s」）",
                    delayEngine.ActiveAlarms.Count == 0, "");

                now = now.AddSeconds(2.1);
                delayEngine.Tick();
                Check("重新成立后连续满 5s → 报警",
                    delayEngine.ActiveAlarms.Count == 1
                    && delayEngine.ActiveAlarms[0].State == ScadaAlarmState.Active, "");

                // ----------------------------------------------------------
                // ⑧ 实时列表排序：严重度降序 → 同级按激活时间降序
                // ----------------------------------------------------------
                var orderDoc = new ScadaDocument();
                var idA = Guid.NewGuid();
                var idB = Guid.NewGuid();
                var idC = Guid.NewGuid();
                var hA = new FakeValueHandle { VariableId = idA, Name = "A", DataType = typeof(double), Value = 0d };
                var hB = new FakeValueHandle { VariableId = idB, Name = "B", DataType = typeof(double), Value = 0d };
                var hC = new FakeValueHandle { VariableId = idC, Name = "C", DataType = typeof(double), Value = 0d };
                var orderSource = new FakeValueSource();
                orderSource.ById[idA] = hA;
                orderSource.ById[idB] = hB;
                orderSource.ById[idC] = hC;

                var alarmA = orderDoc.AddAlarm("A 警告", ScadaAlarmKind.High);
                alarmA.Bind(idA, "A");
                alarmA.Threshold = 10;

                var alarmB = orderDoc.AddAlarm("B 严重", ScadaAlarmKind.HighHigh);
                alarmB.Bind(idB, "B");
                alarmB.Threshold = 10;

                var alarmC = orderDoc.AddAlarm("C 警告", ScadaAlarmKind.High);
                alarmC.Bind(idC, "C");
                alarmC.Threshold = 10;

                var orderEngine = new ScadaAlarmEngine(orderDoc, orderSource, clock);
                orderEngine.Attach();

                now = now.AddSeconds(1);
                hA.Raise(20d);
                now = now.AddSeconds(1);
                hB.Raise(20d);
                now = now.AddSeconds(1);
                hC.Raise(20d);

                var ordered = orderEngine.ActiveAlarms;
                Check("排序：严重度降序 → 同级按激活时间降序（B 严重最前；C 与 A 同为警告，C 晚报所以排在前）",
                    ordered.Count == 3
                    && ordered[0].AlarmId == alarmB.AlarmId
                    && ordered[1].AlarmId == alarmC.AlarmId
                    && ordered[2].AlarmId == alarmA.AlarmId,
                    string.Join(" / ", ordered.Select(r => r.Name)));

                // ----------------------------------------------------------
                // ⑨ 配置热更新：运行中改阈值 / 停用 / 增删 → 下一拍自动重挂
                // ----------------------------------------------------------
                alarmA.Threshold = 25; // 值 20 不再越限
                orderEngine.Tick();
                Check("运行中把阈值 10 改成 25 → 下一次 Tick 自动重挂并重新判定（值 20 不再越限，A 自己消失）",
                    orderEngine.ActiveAlarms.Count == 2
                    && orderEngine.ActiveAlarms.All(r => r.AlarmId != alarmA.AlarmId),
                    $"active={orderEngine.ActiveAlarms.Count}");

                alarmA.IsEnabled = false;
                orderEngine.Tick();
                Check("停用一条 → 连变量都不订阅（通道订阅者掉到 0）：槽还在但整条空转",
                    hA.Subscribers == 0
                    && orderEngine.SlotCount == 3 && orderEngine.SubscribedCount == 2,
                    $"订阅者={hA.Subscribers} / 槽={orderEngine.SlotCount} / 订阅={orderEngine.SubscribedCount}");

                var newAlarm = orderDoc.AddAlarm("运行中新增", ScadaAlarmKind.Low);
                newAlarm.Bind(Guid.NewGuid(), "Missing"); // 解析不到变量
                orderEngine.Tick();
                Check("运行中新增一条报警 → 自动进槽；变量解析不到的槽静默跳过（不订阅、也不产生「配置错误」报警刷屏）",
                    orderEngine.SlotCount == 4 && orderEngine.SubscribedCount == 2
                    && orderEngine.ActiveAlarms.All(r => r.AlarmId != newAlarm.AlarmId),
                    $"槽={orderEngine.SlotCount} / 订阅={orderEngine.SubscribedCount}");

                orderEngine.Detach();
                Check("卸载后订阅全摘干净（否则每开一次画面就漏一批回调，跑几百个画面后内存悄悄涨）",
                    hB.Subscribers == 0 && hC.Subscribers == 0
                    && !orderEngine.IsAttached && orderEngine.SlotCount == 0,
                    $"B={hB.Subscribers} / C={hC.Subscribers}");
                Check("卸载保留历史（宿主可能还要把它落盘）", orderEngine.History.Count > 0,
                    $"history={orderEngine.History.Count}");

                // ----------------------------------------------------------
                // ⑩ 内存历史上限：组态软件要连开几个月，流水不能无限涨
                // ----------------------------------------------------------
                var capDoc = new ScadaDocument();
                var capId = Guid.NewGuid();
                var capHandle = new FakeValueHandle
                {
                    VariableId = capId, Name = "Cap", DataType = typeof(double), Value = 0d
                };
                var capSource = new FakeValueSource();
                capSource.ById[capId] = capHandle;

                var capAlarm = capDoc.AddAlarm("上限", ScadaAlarmKind.High);
                capAlarm.Bind(capId, "Cap");
                capAlarm.Threshold = 1;

                var capEngine = new ScadaAlarmEngine(capDoc, capSource, clock);
                capEngine.Attach();

                int cycles = ScadaAlarmEngine.MaxHistoryRecords + 20;
                for (int i = 0; i < cycles; i++)
                {
                    capHandle.Raise(2d);
                    capHandle.Raise(0d);
                    capEngine.Acknowledge(capAlarm.AlarmId);
                }

                Check($"产生 {cycles} 条流水后内存里只留最近 {ScadaAlarmEngine.MaxHistoryRecords} 条（更早的该去查落盘的历史文件，内存不是历史库）",
                    capEngine.History.Count == ScadaAlarmEngine.MaxHistoryRecords,
                    $"history={capEngine.History.Count}");

                var recent = capEngine.RecentRecords(3);
                Check("RecentRecords 返回「最新在前」的一截，条数不超过可用条数；max<=0 返回空表",
                    recent.Count == 3 && capEngine.RecentRecords(0).Count == 0
                    && recent[0].ActivatedAtUtc >= recent[2].ActivatedAtUtc,
                    $"recent={recent.Count}");

                // ----------------------------------------------------------
                // ⑪ 改名级联修到报警：Id 优先、旧数据按名命中补 Id，且不被写守卫拦
                // ----------------------------------------------------------
                var renameDoc = new ScadaDocument();
                var renameId = Guid.NewGuid();

                var byIdAlarm = renameDoc.AddAlarm("按 Id 寻址");
                byIdAlarm.Bind(renameId, "OldName");

                var legacyAlarm = renameDoc.AddAlarm("旧数据只有名字");
                legacyAlarm.VariableName = "OldName"; // VariableId 保持 Guid.Empty = 「只能按名字找」

                var unrelatedAlarm = renameDoc.AddAlarm("无关变量");
                unrelatedAlarm.Bind(Guid.NewGuid(), "Other");

                string renameFailure = string.Empty;
                int renamed = 0;
                ScadaWriteGuard.Strict = true;
                try
                {
                    renamed = renameDoc.RefreshVariableReferences(renameId, "OldName", "NewName");
                }
                catch (Exception ex)
                {
                    renameFailure = ex.Message;
                }
                finally
                {
                    ScadaWriteGuard.Strict = strictBackup;
                }

                Check("改名级联命中报警：有 Id 的按 Id 认（名字过期也不影响）、旧数据按名命中并把 Id 补回来、无关变量不动",
                    renamed == 2
                    && byIdAlarm.VariableName == "NewName"
                    && legacyAlarm.VariableName == "NewName" && legacyAlarm.VariableId == renameId
                    && unrelatedAlarm.VariableName == "Other",
                    $"改动 {renamed} 条");

                Check("改名级联是数据自愈而不是画面编辑：严格写守卫下不抛（漏掉 Suspend 会留下「画面绑定改了、报警没改」的半成品）",
                    renameFailure.Length == 0, renameFailure);

                // ----------------------------------------------------------
                // ⑫ 报警配置随方案落盘往返 + 旧文件补身份
                // ----------------------------------------------------------
                string dir = Path.Combine(Path.GetTempPath(), "ScadaChecks_" + Guid.NewGuid().ToString("N"));
                string path = Path.Combine(dir, "alarm.vms");

                try
                {
                    var service = new SolutionService();

                    var solution = new SolutionModel();
                    solution.Flows.Clear();

                    var saveVariableId = Guid.NewGuid();
                    var persistAlarm = solution.Scada.AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
                    persistAlarm.Bind(saveVariableId, "FurnaceTemp");
                    persistAlarm.Threshold = 1200;
                    persistAlarm.Deadband = 20;
                    persistAlarm.DelaySeconds = 3;
                    persistAlarm.Message = "炉温超过安全上限";
                    solution.Scada.AddAlarm("断线", ScadaAlarmKind.Stale).StaleSeconds = 15;

                    var save = service.SaveAsync(solution, path).GetAwaiter().GetResult();
                    Check("含报警配置的方案保存成功", save.Success, save.Message);

                    string json = File.ReadAllText(path);
                    Check("落盘 JSON 含 Alarms 节点（报警配置确实随方案走，而不是活在内存里）",
                        json.Contains("\"Alarms\"") && json.Contains("炉温高高"), "");

                    var load = service.LoadAsync(path).GetAwaiter().GetResult();
                    Check("含报警配置的方案加载成功（$type 白名单未挡下 VisionMaster.Scada）",
                        load.Success, load.Message);

                    var loadedAlarms = load.Data.Scada.Alarms;
                    var loadedAlarm = load.Data.Scada.FindAlarm(persistAlarm.AlarmId);
                    Check("报警身份往返一致（按 Id 能找回同一条定义）",
                        loadedAlarms.Count == 2 && loadedAlarm != null, $"alarms={loadedAlarms.Count}");

                    Check("报警字段往返一致（种类 / 阈值 / 回差 / 延时 / 文本 / 变量寻址 / 自动严重度）",
                        loadedAlarm is
                        {
                            Kind: ScadaAlarmKind.HighHigh,
                            Threshold: 1200,
                            Deadband: 20,
                            DelaySeconds: 3,
                            Severity: ScadaAlarmSeverity.Critical
                        }
                        && loadedAlarm.DisplayText == "炉温超过安全上限"
                        && loadedAlarm.VariableId == saveVariableId
                        && loadedAlarm.VariableName == "FurnaceTemp",
                        $"Id={loadedAlarm?.AlarmId}");

                    Check("条件人话描述由配置算出来（面板与 CSV 导出共用同一份文案，不各写一遍）",
                        loadedAlarm?.ConditionText == "高高限 > 1200（回差 20）",
                        loadedAlarm?.ConditionText ?? "null");

                    // 旧文件（报警还没有 AlarmId 的年代）：加载期由 EnsureIdentity 补齐
                    var legacyDoc = new ScadaDocument();
                    var legacyDef = legacyDoc.AddAlarm("老报警");
                    legacyDef.Bind(saveVariableId, "FurnaceTemp");
                    legacyDef.AlarmId = Guid.Empty; // 演旧数据

                    int repaired = legacyDoc.EnsureIdentity();
                    Check("旧数据报警缺身份 → 加载期补齐（幂等：再补一次返回 0）；不补 VariableId（Guid.Empty 是有含义的）",
                        repaired == 1 && legacyDef.AlarmId != Guid.Empty
                        && legacyDoc.EnsureIdentity() == 0
                        && legacyDef.VariableId == saveVariableId,
                        $"补发 {repaired} 处");
                }
                finally
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
            }
            finally
            {
                ScadaWriteGuard.Strict = strictBackup;
                ScadaEditHistory.Clear();
            }
        }

        // ==================================================================
        //  [AH2] 报警历史落盘 + 全画面统一节拍源（S11-b 运行时接线）
        // ==================================================================
        private static void AlarmHistoryChecks()
        {
            Section("[AH2] 报警历史落盘 + 全画面统一节拍源（S11-b）");

            CheckAlarmCsvWriter();

            // 节拍源里包着一个 DispatcherTimer，必须在 STA 线程上建（与图元控件同一房间要求）。
            // 主线程不是 STA，所以照 ElementControlChecks 的样板另起一条 STA 线程。
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunBeatSourceChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("节拍源断言全程未抛异常", false, failure.ToString());
        }

        // ==================================================================
        // [AH3] 实时报警条图元（S11-c2）
        //
        // 这一段把"只有运行态才存在"的三样东西串成一条链来测：
        // 假变量源 → 报警引擎 → 被宿主注入的运行上下文 → 报警条控件。
        // 控件与 DispatcherTimer 都有 STA 房间要求，所以照 [AH2] 的样板另起一条 STA 线程。
        //
        // 覆盖四件事：
        //   ① 描述符：工具箱里找得到、属性键指向真的依赖属性、默认值真的落到了依赖属性上；
        //   ② 行视图模型的换算：枚举 → 人话、UTC → 本地时刻、条件与触发值拼成一行；
        //   ③ 控件的实时路径：装上上下文立刻补读一次、引擎事件驱动整表重读、
        //      只有"严重且未确认"才闪、闪烁相位从节拍源的累计时长算；
        //   ④ 订阅成对：装上挂 4 个、摘掉退 4 个——这件事行为上看不出来（重读永远读"当前那个上下文"），
        //      代价是泄漏，所以只能在这一层钉住。
        // ==================================================================
        private static void AlarmBannerChecks()
        {
            Section("[AH3] 实时报警条图元（S11-c2）");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunAlarmBannerChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("报警条断言全程未抛异常", false, failure.ToString());
        }

        private static void RunAlarmBannerChecks()
        {
            ScadaEditHistory.Clear();

            // ----------------------------------------------------------
            // ① 描述符：工具箱能找到它，每个键都指向真的依赖属性
            // ----------------------------------------------------------
            var descriptor = ElementRegistry.Find("Hmi.AlarmBanner");

            Check("工具箱里有「报警条」这一类图元",
                descriptor != null && descriptor.ControlType == typeof(AlarmBannerElement),
                descriptor?.ControlType.Name ?? "未注册");

            Check("单独归到「报警」分类（塞进「指示」那一组的话，找报警条要翻遍十几个同类项）",
                descriptor?.Category == "报警", descriptor?.Category ?? "null");

            Check("默认尺寸 320×140：装得下五行「徽标+时间+名字+条件+状态」",
                descriptor?.DefaultWidth == 320 && descriptor?.DefaultHeight == 140,
                $"{descriptor?.DefaultWidth}×{descriptor?.DefaultHeight}");

            Check("属性键 = 6 个几何 + 14 个自有（行数 / 空态 / 四档色 / 外观四件 / 文字五件）",
                descriptor?.Properties.Count == 20, $"{descriptor?.Properties.Count} 个");

            Check("报警条自己的键落在自己的依赖属性上（不是把值塞进基类的同名属性）",
                ReferenceEquals(ElementRegistry.FindProperty("Hmi.AlarmBanner", "MaxRows")?.TargetProperty,
                    AlarmBannerElement.MaxRowsProperty)
                && ReferenceEquals(ElementRegistry.FindProperty("Hmi.AlarmBanner", "EmptyText")?.TargetProperty,
                    AlarmBannerElement.EmptyTextProperty)
                && ReferenceEquals(ElementRegistry.FindProperty("Hmi.AlarmBanner", "NormalColor")?.TargetProperty,
                    AlarmBannerElement.NormalColorProperty)
                && ReferenceEquals(ElementRegistry.FindProperty("Hmi.AlarmBanner", "CriticalColor")?.TargetProperty,
                    AlarmBannerElement.CriticalColorProperty),
                "");

            // ----------------------------------------------------------
            // ② 设计态：不挂运行上下文，就是组态时看到的预览
            // ----------------------------------------------------------
            var design = (AlarmBannerElement)ElementRegistry.CreateControl(
                ElementRegistry.CreateElement("Hmi.AlarmBanner", 10, 20));

            Check("描述符默认值落到了依赖属性上（行数 5、空态「系统正常」）",
                design.MaxRows == 5 && design.EmptyText == "系统正常",
                $"行数={design.MaxRows} / 空态={design.EmptyText}");

            Check("四档默认色与多态灯同一套语汇（整幅画面的红黄绿蓝只有一个来源）",
                design.NormalColor is SolidColorBrush { Color: var dn } && dn == Color.FromRgb(0x34, 0xC7, 0x59)
                && design.InfoColor is SolidColorBrush { Color: var di } && di == Color.FromRgb(0x3B, 0x82, 0xF6)
                && design.WarningColor is SolidColorBrush { Color: var dw } && dw == Color.FromRgb(0xFF, 0xB0, 0x20)
                && design.CriticalColor is SolidColorBrush { Color: var dc } && dc == Color.FromRgb(0xE0, 0x3A, 0x2B),
                $"{design.NormalColor} / {design.WarningColor} / {design.CriticalColor}");

            Check("设计态零行、不闪、色条是正常绿、计数为空串（没有引擎时这条分支是常态，不是异常）",
                design.Rows.Count == 0 && !design.HasAlarms && !design.IsFlashing && design.BlinkOn
                && design.AccentBrush is SolidColorBrush { Color: var accentDesign }
                && accentDesign == Color.FromRgb(0x34, 0xC7, 0x59)
                && design.SummaryText.Length == 0,
                $"rows={design.Rows.Count} / summary=\"{design.SummaryText}\"");

            // ----------------------------------------------------------
            // ③ 把链串起来：假变量源 → 引擎 → 上下文 → 报警条
            // ----------------------------------------------------------
            // 假时钟：激活时刻固定住，行上的时间文本才可预期。
            DateTime now = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;

            var tempId = Guid.NewGuid();
            var pressId = Guid.NewGuid();

            var temp = new FakeValueHandle
            {
                VariableId = tempId, Name = "FurnaceTemp", DataType = typeof(double), Value = 20d
            };
            var press = new FakeValueHandle
            {
                VariableId = pressId, Name = "AirPressure", DataType = typeof(double), Value = 60d
            };
            var source = new FakeValueSource();
            source.ById[tempId] = temp;
            source.ById[pressId] = press;

            var doc = new ScadaDocument();

            var highTemp = doc.AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
            highTemp.Bind(tempId, "FurnaceTemp");
            highTemp.Threshold = 100;
            highTemp.Deadband = 5;

            var lowPressure = doc.AddAlarm("气压低", ScadaAlarmKind.Low);
            lowPressure.Bind(pressId, "AirPressure");
            lowPressure.Threshold = 50;
            lowPressure.Deadband = 5;

            var engine = new ScadaAlarmEngine(doc, source, clock);
            engine.Attach();

            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var context = new ScadaRuntimeContext(engine, beat);

            // 刻意先造报警、再装上下文——这正是现场的顺序：运行窗口一开，第一拍就可能已经判出报警了。
            temp.Raise(120d);    // 高高限 > 100 → 严重
            press.Raise(30d);    // 低限 < 50 → 警告

            Check("先造出两条报警（严重一条 + 警告一条），引擎已按严重度降序排好",
                engine.ActiveAlarms.Count == 2
                && engine.ActiveAlarms[0].Severity == ScadaAlarmSeverity.Critical
                && engine.ActiveAlarms[1].Severity == ScadaAlarmSeverity.Warning,
                string.Join(" / ", engine.ActiveAlarms.Select(r => $"{r.Name}:{r.Severity.DisplayName()}")));

            var banner = (AlarmBannerElement)ElementRegistry.CreateControl(
                ElementRegistry.CreateElement("Hmi.AlarmBanner", 0, 0));

            banner.RuntimeContext = context;

            Check("装上上下文立刻补读一次：已经存在的报警一条不落（不必等下一次变量变化才显示）",
                banner.Rows.Count == 2 && banner.HasAlarms, $"rows={banner.Rows.Count}");

            Check("色条取最高严重度（列表已排好序，第一条就是它）",
                banner.AccentBrush is SolidColorBrush { Color: var accentTop }
                && accentTop == Color.FromRgb(0xE0, 0x3A, 0x2B),
                banner.AccentBrush?.ToString() ?? "null");

            Check("表头同时报「未确认几条」与「共几条」（只给一个数，操作员判断不了自己能不能走开）",
                banner.SummaryText == "未确认 2 / 共 2", banner.SummaryText);

            Check("每行按自己的严重度挑徽标底色（不是整表一个色）",
                banner.Rows[0].ChipBrush is SolidColorBrush { Color: var chip0 }
                && chip0 == Color.FromRgb(0xE0, 0x3A, 0x2B)
                && banner.Rows[1].ChipBrush is SolidColorBrush { Color: var chip1 }
                && chip1 == Color.FromRgb(0xFF, 0xB0, 0x20),
                $"{banner.Rows[0].ChipBrush} / {banner.Rows[1].ChipBrush}");

            Check("存在「严重且未确认」的报警 → 该闪（严重但已确认、或只是警告，都不闪）",
                banner.IsFlashing, $"flashing={banner.IsFlashing}");

            var topRow = banner.Rows[0];
            Check("行是人话：严重度 / 本地时刻 / 报警名 / 条件+触发值 / 状态短写法",
                topRow.SeverityText == "严重"
                && topRow.Name == "炉温高高"
                && topRow.Detail == "高高限 > 100（回差 5）｜触发值 120"
                && topRow.StateText == "激活"
                && !topRow.IsAcknowledged
                && topRow.TimeText == topRow.Record.ActivatedAtLocal.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                $"{topRow.SeverityText} / {topRow.TimeText} / {topRow.Detail} / {topRow.StateText}");

            Check("行是快照但攥着来源记录（将来「点这一行去确认」要按它找回定义）",
                ReferenceEquals(topRow.Record, engine.ActiveAlarms[0]), "");

            Check("警告级那条的条件与触发值同样拼全（低限用 < 号）",
                banner.Rows[1].Detail == "低限 < 50（回差 5）｜触发值 30",
                banner.Rows[1].Detail);

            // 行数截断：超出的不画，但表头计数仍报全量
            banner.MaxRows = 1;
            Check("最多显示 1 行时只画 1 行，表头计数仍报全量（截断的是画面，不是事实）",
                banner.Rows.Count == 1 && banner.SummaryText == "未确认 2 / 共 2",
                $"rows={banner.Rows.Count} / summary=\"{banner.SummaryText}\"");

            banner.MaxRows = 0;
            Check("行数配成 0 被钳到 1（0 行等于图元隐形，那是「看不见也没人知道」的组态事故）",
                banner.Rows.Count == 1, $"rows={banner.Rows.Count}");

            banner.MaxRows = 5;

            // ----------------------------------------------------------
            // ④ 事件驱动：引擎上任何动静都整表重读
            // ----------------------------------------------------------
            temp.Raise(20d);   // 退出回差带 → 已恢复未确认

            Check("条件恢复 → 引擎发 AlarmChanged → 报警条跟着刷新（行还在，状态变「已恢复」）",
                banner.Rows.Count == 2
                && banner.Rows[0].StateText == "已恢复"
                && banner.Rows[0].Record.State == ScadaAlarmState.Recovered,
                banner.Rows[0].StateText);

            Check("「已恢复未确认」仍在等确认，所以继续闪（这时候不闪，等于告诉现场「没事了」）",
                banner.IsFlashing && banner.SummaryText == "未确认 2 / 共 2",
                $"flashing={banner.IsFlashing} / summary=\"{banner.SummaryText}\"");

            // ----------------------------------------------------------
            // ⑤ 闪烁相位：从节拍源的累计时长算，而不是「收到几拍就取反」
            // ----------------------------------------------------------
            Check("刚起表时相位是「亮」：色条常亮，不是先灭一下",
                banner.BlinkOn, $"BlinkOn={banner.BlinkOn}");

            beat.Start();
            PumpDispatcher(AlarmBannerElement.BlinkHalfPeriodMilliseconds + 150);

            Check("跑过半个周期后相位翻到「灭」（相位按累计时长算，漏拍只会跳一下、不会永久错开）",
                !banner.BlinkOn,
                $"BlinkOn={banner.BlinkOn} / 累计={beat.Elapsed.TotalMilliseconds:0}ms");

            PumpDispatcher(AlarmBannerElement.BlinkHalfPeriodMilliseconds);

            Check("再跑半个周期又亮回来（是周期闪烁，不是「闪一下就停」）",
                banner.BlinkOn,
                $"BlinkOn={banner.BlinkOn} / 累计={beat.Elapsed.TotalMilliseconds:0}ms");

            beat.Stop();

            // ----------------------------------------------------------
            // ⑥ 报警清空：相位必须归位到「亮」
            // ----------------------------------------------------------
            press.Raise(60d);            // 退出低限回差带 → 也恢复
            int cleared = engine.AcknowledgeAll();

            Check("两条都恢复后确认 → 从实时列表清空（引擎发 AlarmCleared）",
                cleared == 2 && engine.ActiveAlarms.Count == 0, $"确认 {cleared} 条");

            Check("报警清空 → 零行、空态、色条回到正常绿、计数清空",
                banner.Rows.Count == 0 && !banner.HasAlarms
                && banner.SummaryText.Length == 0
                && banner.AccentBrush is SolidColorBrush { Color: var accentBack }
                && accentBack == Color.FromRgb(0x34, 0xC7, 0x59),
                $"rows={banner.Rows.Count} / summary=\"{banner.SummaryText}\"");

            Check("不闪了相位归位到「亮」（停在上一条的暗相位上，看着像「报警还在但变灰了」，比不闪更误导）",
                !banner.IsFlashing && banner.BlinkOn, $"flashing={banner.IsFlashing} / BlinkOn={banner.BlinkOn}");

            temp.Raise(130d);
            Check("重新越限 → 又出现一行（记录是「一次激活→清除」的完整过程，清空不等于这条报警作废）",
                banner.Rows.Count == 1 && banner.HasAlarms && banner.IsFlashing,
                $"rows={banner.Rows.Count}");

            // ----------------------------------------------------------
            // ⑦ 订阅成对：装上挂 4 个（三个引擎事件 + 节拍），摘掉退 4 个
            // ----------------------------------------------------------
            // 行为上测不出来：RefreshFromEngine 读的永远是「当前那个上下文」，留着旧订阅只会让重读多做一次。
            // 代价是泄漏——上一轮的引擎攥着这一轮的控件。所以只能数订阅者。
            var probe = (AlarmBannerElement)ElementRegistry.CreateControl(
                ElementRegistry.CreateElement("Hmi.AlarmBanner", 0, 0));

            int beatIdle = SubscriberCount(beat, nameof(ScadaBeatSource.Beat));
            int raisedIdle = SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised));
            int changedIdle = SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged));
            int clearedIdle = SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared));

            probe.RuntimeContext = context;

            Check("装上上下文挂上 4 个订阅（三个引擎事件各一个 + 节拍一个）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == beatIdle + 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == raisedIdle + 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged)) == changedIdle + 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared)) == clearedIdle + 1,
                "");

            probe.RuntimeContext = null;

            Check("摘掉上下文成对退掉 4 个（只退节拍、把引擎那三个漏在外面，就是「上一轮的引擎攥着这一轮的控件」）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == beatIdle
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == raisedIdle
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged)) == changedIdle
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared)) == clearedIdle,
                "");

            Check("摘掉后回到设计态（停运行 = 回到组态时的样子，不需要任何「恢复现场」的备份表）",
                probe.Rows.Count == 0 && !probe.HasAlarms && probe.BlinkOn
                && probe.AccentBrush is SolidColorBrush { Color: var accentProbe }
                && accentProbe == Color.FromRgb(0x34, 0xC7, 0x59),
                $"rows={probe.Rows.Count}");

            // 重装同一个上下文：ReferenceEquals 短路，不该挂出第二份订阅
            probe.RuntimeContext = context;
            probe.RuntimeContext = context;

            Check("重复装同一个上下文不重复订阅（ReferenceEquals 短路：一次事件不该触发两遍重读）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == beatIdle + 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == raisedIdle + 1,
                "");

            probe.RuntimeContext = null;

            // 前面那条 banner 从头到尾挂在上下文上，收场时也得由宿主摘下来。
            // 这就是真实收场顺序（ScadaRuntimeHost.TeardownAlarms）：先摘图元的上下文（订阅方先退场），
            // 再停节拍、Detach 引擎。断言自己也按这个顺序走，否则留到最后的订阅就是自己制造的泄漏。
            banner.RuntimeContext = null;
            engine.Detach();
            beat.Stop();

            Check("收场后引擎与节拍的订阅者都回到零（断言自己不留尾巴）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared)) == 0,
                $"beat={SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} / engine={SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised))}");

            ScadaEditHistory.Clear();
        }

        private static void AlarmHistoryPanelChecks()
        {
            Section("[AH4] 报警历史面板（S11-c3/c4：行投影 / 筛选 / 确认 / 导出）");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunAlarmHistoryPanelChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("历史面板断言全程未抛异常", false, failure.ToString());
        }

        private static void RunAlarmHistoryPanelChecks()
        {
            string exportDir = Path.Combine(Path.GetTempPath(), "ScadaAlarmPanel_" + Guid.NewGuid().ToString("N"));

            try
            {
                RunAlarmHistoryPanelChecksCore(exportDir);
            }
            finally
            {
                if (Directory.Exists(exportDir))
                    Directory.Delete(exportDir, true);
            }
        }

        private static void RunAlarmHistoryPanelChecksCore(string exportDir)
        {
            ScadaEditHistory.Clear();

            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var activatedUtc = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc);

            // ----------------------------------------------------------
            // ① 行投影：记录里是事实（枚举 / UTC / 枚举），行上是画法（色 / 文案 / 能不能点）
            // ----------------------------------------------------------
            var projDef = new ScadaDocument().AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
            projDef.Bind(Guid.NewGuid(), "FurnaceTemp");
            projDef.Threshold = 100;
            projDef.Deadband = 5;
            projDef.Message = "冷却水异常";

            var projRecord = new ScadaAlarmRecord(projDef, activatedUtc, "120");
            var projRow = new ScadaAlarmHistoryRow(projRecord);

            Check("行投影：严重度是中文词汇（不是枚举名——现场不认识 Critical）",
                projRow.SeverityText == "严重" && projRow.Severity == ScadaAlarmSeverity.Critical,
                $"{projRow.SeverityText} / {projRow.Severity}");

            Check("行投影：时间带日期、格式钉死 MM-dd HH:mm:ss（历史横跨一整天，只给 HH:mm:ss 分不清是哪天报的）",
                projRow.ActivatedAt == activatedUtc.ToLocalTime()
                && projRow.TimeText == activatedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", invariant)
                && projRow.TimeText.Length == 14
                && projRow.TimeText[2] == '-' && projRow.TimeText[5] == ' '
                && projRow.FullTimeText == activatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", invariant)
                && projRow.FullTimeText.Length == 19,
                $"{projRow.TimeText} / {projRow.FullTimeText}");

            Check("行投影：条件+触发值就是记录上那一句（与报警条共用 DetailText——两处措辞一分叉，操作员就以为说的是两件事）",
                projRow.Detail == "高高限 > 100（回差 5）｜触发值 120"
                && projRow.Detail == projRecord.DetailText,
                projRow.Detail);

            Check("行投影：状态用完整词汇（历史面板一行够宽，「已恢复未确认」正是最该一眼看出来的事）",
                projRow.StateText == "激活" && projRow.State == ScadaAlarmState.Active
                && !projRow.IsAcknowledged && projRow.CanAcknowledge,
                $"{projRow.StateText} / canAck={projRow.CanAcknowledge}");

            Check("行投影：变量名落在行上（关键字搜索要用它）",
                projRow.VariableName == "FurnaceTemp", projRow.VariableName);

            Check("行投影：色板与报警条同一套语汇（同一条报警在条上红、在历史里黄，现场会以为严重度变了）",
                projRow.SeverityBrush is SolidColorBrush { Color: var bc } && bc == Color.FromRgb(0xE0, 0x3A, 0x2B)
                && ScadaAlarmHistoryRow.BrushFor(ScadaAlarmSeverity.Warning) is SolidColorBrush { Color: var bw } && bw == Color.FromRgb(0xFF, 0xB0, 0x20)
                && ScadaAlarmHistoryRow.BrushFor(ScadaAlarmSeverity.Info) is SolidColorBrush { Color: var bi } && bi == Color.FromRgb(0x3B, 0x82, 0xF6),
                projRow.SeverityBrush.ToString());

            Check("行投影：未知严重度回落「提示」色（宁可低估，也不谎报严重）",
                ScadaAlarmHistoryRow.BrushFor((ScadaAlarmSeverity)99) is SolidColorBrush { Color: var bu }
                && bu == Color.FromRgb(0x3B, 0x82, 0xF6),
                "");

            // 持续时长：唯一会随钟走的字段，靠单点刷新而不是整表重建
            var durationNotifications = new List<string?>();
            projRow.PropertyChanged += (_, e) => durationNotifications.Add(e.PropertyName);

            projRow.RefreshDuration();
            bool quietWhenUnchanged = durationNotifications.Count == 0;

            projRecord.RecoveredAtUtc = activatedUtc.AddMinutes(3);
            projRow.RefreshDuration();

            Check("持续时长单点刷新：没变时不发通知（SetProperty 判等），变了只动 DurationText 一个属性——不重建集合、不动选中",
                quietWhenUnchanged
                && durationNotifications.Count == 1
                && durationNotifications[0] == nameof(ScadaAlarmHistoryRow.DurationText)
                && projRow.DurationText == "3分0秒",
                $"通知={string.Join("/", durationNotifications)} / 时长={projRow.DurationText}");

            Check("关键字命中四样：报警名 / 报警文本 / 变量名 / 条件描述（现场手里可能只有其中任何一样）",
                projRow.Matches("炉温") && projRow.Matches("冷却水")
                && projRow.Matches("FurnaceTemp") && projRow.Matches("高高限")
                && projRow.Matches("120"),
                "");

            Check("关键字忽略大小写与首尾空格；空关键字一律算命中（等价于不过滤）",
                projRow.Matches("furnaceTEMP") && projRow.Matches("  FurnaceTemp  ")
                && projRow.Matches("") && projRow.Matches(null) && projRow.Matches("   ")
                && !projRow.Matches("不存在的东西"),
                "");

            // 四种态各造一行：确认按钮的有无就是按态分的
            ScadaAlarmHistoryRow MakeRow(ScadaAlarmState state, ScadaAlarmSeverity severity)
            {
                var def = new ScadaDocument().AddAlarm("样本", ScadaAlarmKind.High);
                def.Severity = severity;
                def.Bind(Guid.NewGuid(), "Sample");

                var record = new ScadaAlarmRecord(def, activatedUtc, "1");
                record.State = state;
                return new ScadaAlarmHistoryRow(record);
            }

            var activeRow = MakeRow(ScadaAlarmState.Active, ScadaAlarmSeverity.Warning);
            var recoveredRow = MakeRow(ScadaAlarmState.Recovered, ScadaAlarmSeverity.Warning);
            var ackedRow = MakeRow(ScadaAlarmState.Acknowledged, ScadaAlarmSeverity.Warning);
            var clearedRow = MakeRow(ScadaAlarmState.Normal, ScadaAlarmSeverity.Warning);
            var criticalRow = MakeRow(ScadaAlarmState.Active, ScadaAlarmSeverity.Critical);
            var infoRow = MakeRow(ScadaAlarmState.Active, ScadaAlarmSeverity.Info);

            Check("「激活」与「已恢复未确认」都在等确认——这条跨两态，正是筛选必须用谓词而不是等值比较的原因",
                activeRow.CanAcknowledge && recoveredRow.CanAcknowledge
                && !ackedRow.CanAcknowledge && !clearedRow.CanAcknowledge,
                "");

            Check("状态文案落到行上：「已确认」不再等确认、「正常」不算确认过（模板据此淡化整行）",
                ackedRow.StateText == "已确认" && ackedRow.IsAcknowledged
                && clearedRow.StateText == "正常" && !clearedRow.IsAcknowledged
                && recoveredRow.StateText == "已恢复未确认",
                $"{ackedRow.StateText} / {recoveredRow.StateText} / {clearedRow.StateText}");

            // ----------------------------------------------------------
            // ② 筛选档位：一档 = 文本 + 谓词
            // ----------------------------------------------------------
            Check("筛选档位：状态 5 档、严重度 4 档，首档都是「全部」",
                ScadaAlarmHistoryVM.StateFilters.Count == 5
                && ScadaAlarmHistoryVM.SeverityFilters.Count == 4
                && ScadaAlarmHistoryVM.StateFilters[0].Text == "全部状态"
                && ScadaAlarmHistoryVM.SeverityFilters[0].Text == "全部级别",
                $"{ScadaAlarmHistoryVM.StateFilters.Count} / {ScadaAlarmHistoryVM.SeverityFilters.Count}");

            var fAllState = ScadaAlarmHistoryVM.StateFilters[0];
            var fPending = ScadaAlarmHistoryVM.StateFilters[1];
            var fActive = ScadaAlarmHistoryVM.StateFilters[2];
            var fRecovered = ScadaAlarmHistoryVM.StateFilters[3];
            var fCleared = ScadaAlarmHistoryVM.StateFilters[4];
            var fAllSeverity = ScadaAlarmHistoryVM.SeverityFilters[0];
            var fCritical = ScadaAlarmHistoryVM.SeverityFilters[1];
            var fWarning = ScadaAlarmHistoryVM.SeverityFilters[2];
            var fInfo = ScadaAlarmHistoryVM.SeverityFilters[3];

            Check("「全部」恒真（它不该是「筛掉了什么」的那一档）",
                fAllState.Matches(activeRow) && fAllState.Matches(clearedRow)
                && fAllSeverity.Matches(activeRow) && fAllSeverity.Matches(infoRow),
                "");

            Check("「未确认」跨 激活 / 已恢复未确认 两态（等值比较表达不了这条规则）",
                fPending.Matches(activeRow) && fPending.Matches(recoveredRow)
                && !fPending.Matches(ackedRow) && !fPending.Matches(clearedRow),
                "");

            Check("「报警中」= 条件仍成立（激活 + 已确认），已恢复与已清除都不算",
                fActive.Matches(activeRow) && fActive.Matches(ackedRow)
                && !fActive.Matches(recoveredRow) && !fActive.Matches(clearedRow),
                "");

            Check("「已恢复」只认 Recovered、「已清除」只认 Normal（互不串门）",
                fRecovered.Matches(recoveredRow) && !fRecovered.Matches(clearedRow) && !fRecovered.Matches(activeRow)
                && fCleared.Matches(clearedRow) && !fCleared.Matches(recoveredRow),
                "");

            Check("严重度三档按枚举判、不按显示文本判（文本是可改的词汇表，改名不该把筛选改坏）",
                fCritical.Matches(criticalRow) && !fCritical.Matches(activeRow)
                && fWarning.Matches(activeRow) && !fWarning.Matches(criticalRow)
                && fInfo.Matches(infoRow) && !fInfo.Matches(activeRow),
                "");

            bool nullTextRejected = false;
            bool nullMatchRejected = false;

            try { new ScadaAlarmHistoryFilter(null!, _ => true); }
            catch (ArgumentNullException) { nullTextRejected = true; }

            try { new ScadaAlarmHistoryFilter("x", null!); }
            catch (ArgumentNullException) { nullMatchRejected = true; }

            Check("档位缺文本或缺谓词当场抛（一个「选了没反应」的档位，用户会以为是软件卡了）",
                nullTextRejected && nullMatchRejected, "");

            // ----------------------------------------------------------
            // ③ 把链串起来：假变量源 → 引擎 → 上下文 → 面板
            // ----------------------------------------------------------
            DateTime now = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;

            var tempId = Guid.NewGuid();
            var pressId = Guid.NewGuid();

            var temp = new FakeValueHandle
            {
                VariableId = tempId, Name = "FurnaceTemp", DataType = typeof(double), Value = 20d
            };
            var press = new FakeValueHandle
            {
                VariableId = pressId, Name = "AirPressure", DataType = typeof(double), Value = 60d
            };
            var source = new FakeValueSource();
            source.ById[tempId] = temp;
            source.ById[pressId] = press;

            var doc = new ScadaDocument();

            var highTemp = doc.AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
            highTemp.Bind(tempId, "FurnaceTemp");
            highTemp.Threshold = 100;
            highTemp.Deadband = 5;

            var lowPressure = doc.AddAlarm("气压低", ScadaAlarmKind.Low);
            lowPressure.Bind(pressId, "AirPressure");
            lowPressure.Threshold = 50;
            lowPressure.Deadband = 5;

            var engine = new ScadaAlarmEngine(doc, source, clock);
            engine.Attach();

            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var context = new ScadaRuntimeContext(engine, beat);

            now = now.AddMinutes(1);
            temp.Raise(120d);      // 高高限 > 100 → 严重

            now = now.AddMinutes(1);
            press.Raise(30d);      // 低限 < 50 → 警告

            // 二次确认口：视图层注入（弹 MessageBox），断言里换成"记账 + 按开关回答"
            var confirmQuestions = new List<string>();
            bool confirmAnswer = true;
            Func<string, bool> confirm = question =>
            {
                confirmQuestions.Add(question);
                return confirmAnswer;
            };

            bool nullDispatcherRejected = false;
            try { new ScadaAlarmHistoryVM(null!); }
            catch (ArgumentNullException) { nullDispatcherRejected = true; }

            Check("构造必须注入 Dispatcher（断言宿主里根本没有 Application.Current，而节拍与重建都必须落在同一个 UI 线程上）",
                nullDispatcherRejected, "");

            var panel = new ScadaAlarmHistoryVM(Dispatcher.CurrentDispatcher, confirm);
            panel.Attach(context);

            Check("挂上上下文立刻读一次引擎历史（不是等下一次报警事件才显示——打开面板却空空如也，用户会以为没报过）",
                panel.Rows.Count == 2 && panel.HasRows, $"rows={panel.Rows.Count}");

            Check("最新在前（翻开面板第一眼要看到的是「刚刚又报了什么」，不是两小时前那条）",
                panel.Rows[0].Name == "气压低" && panel.Rows[1].Name == "炉温高高",
                string.Join(" / ", panel.Rows.Select(r => r.Name)));

            Check("底部计数把「共几条 / 未确认几条 / 筛剩几条」一起说（只给一个数，操作员判断不了自己能不能走开）",
                panel.SummaryText == "共 2 条 · 未确认 2 · 显示 2",
                panel.SummaryText);

            Check("顶条徽标计数与「未确认」判定同源（面板收起时也靠它亮起来）",
                panel.UnacknowledgedCount == 2 && panel.HasUnacknowledged && panel.UnacknowledgedText == "2",
                panel.UnacknowledgedText);

            Check("列表有内容时空态提示是空串（空态文案常驻，看着像「哪里配错了」）",
                panel.EmptyHint.Length == 0, $"\"{panel.EmptyHint}\"");

            panel.Keyword = "炉温";
            Check("关键字筛得动：命中报警名，计数跟着重算",
                panel.Rows.Count == 1 && panel.Rows[0].Name == "炉温高高"
                && panel.SummaryText == "共 2 条 · 未确认 2 · 显示 1",
                panel.SummaryText);

            panel.Keyword = "FurnaceTemp";
            Check("关键字也命中变量名（现场找报警时手里可能只有变量名）",
                panel.Rows.Count == 1 && panel.Rows[0].Name == "炉温高高", $"{panel.Rows.Count}");

            panel.Keyword = "不存在的东西";
            Check("筛不中时的空态与「一条都没报过」分开说（两种空态指向完全不同的处理动作）",
                panel.Rows.Count == 0 && !panel.HasRows
                && panel.EmptyHint == "没有符合当前筛选条件的报警。",
                $"\"{panel.EmptyHint}\"");

            panel.ResetFilterCommand.Execute();
            Check("重置筛选：关键字与两个下拉一起回默认，列表回到全量",
                panel.Keyword.Length == 0
                && ReferenceEquals(panel.StateFilter, ScadaAlarmHistoryVM.StateFilters[0])
                && ReferenceEquals(panel.SeverityFilter, ScadaAlarmHistoryVM.SeverityFilters[0])
                && panel.Rows.Count == 2,
                $"rows={panel.Rows.Count}");

            // ----------------------------------------------------------
            // ④ 订阅账：挂上就是四条（引擎三条事件 + 节拍一条）
            // ----------------------------------------------------------
            Check("挂着的订阅正好四条：引擎三条事件 + 节拍一条（多一条就是重挂时漏摘）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged)) == 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared)) == 1,
                $"beat={SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} / raised={SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised))}");

            // ----------------------------------------------------------
            // ⑤ 确认：单条 / 全部（二次确认口由视图层注入）
            // ----------------------------------------------------------
            panel.AcknowledgeCommand.Execute(panel.Rows[0]);      // 气压低（最新在前）

            Check("确认单条后立刻给一句回执（按钮点下去毫无反应，人会再点一次）",
                panel.NoticeText == "已确认「气压低」", panel.NoticeText);

            // RequestRefresh 走的是 BeginInvoke(Background)：不推消息循环就看不到重建结果
            PumpDispatcher(50);

            Check("确认落在引擎上：那一条转「已确认」，同表其余各条不受影响",
                panel.Rows.Count == 2
                && panel.Rows[0].StateText == "已确认" && !panel.Rows[0].CanAcknowledge
                && panel.Rows[1].Name == "炉温高高" && panel.Rows[1].CanAcknowledge,
                string.Join(" / ", panel.Rows.Select(r => r.Name + "=" + r.StateText)));

            Check("计数与底部文案跟着重算（顶条徽标与面板读的是同一份数）",
                panel.UnacknowledgedCount == 1 && panel.UnacknowledgedText == "1"
                && panel.SummaryText == "共 2 条 · 未确认 1 · 显示 2",
                panel.SummaryText);

            // 二次确认口说「不」：一条都不该被确认
            confirmAnswer = false;
            panel.AcknowledgeAllCommand.Execute();
            PumpDispatcher(50);

            Check("「全部确认」先问一句，问的正是当前列表里待确认的条数（问得含糊，人只会盲点「是」）",
                confirmQuestions.Count == 1
                && confirmQuestions[0].Contains("1 条报警")
                && confirmQuestions[0].Contains("不可撤销"),
                confirmQuestions.Count == 0 ? "(没问)" : confirmQuestions[0].Replace("\n\n", " ⏎ "));

            Check("二次确认说「不」→ 一条都不动（框弹了、按钮也点了，结果却确认掉了，是最难解释的一类事故）",
                panel.UnacknowledgedCount == 1 && panel.Rows.Count(r => r.CanAcknowledge) == 1,
                $"未确认={panel.UnacknowledgedCount}");

            confirmAnswer = true;
            panel.AcknowledgeAllCommand.Execute();
            PumpDispatcher(50);

            Check("二次确认说「是」→ 确认掉当前列表里全部待确认的（所见即所确认）",
                panel.UnacknowledgedCount == 0 && !panel.HasUnacknowledged
                && panel.UnacknowledgedText == "0"
                && panel.Rows.All(r => !r.CanAcknowledge)
                && panel.NoticeText == "已确认 1 条报警",
                $"未确认={panel.UnacknowledgedCount} / notice=\"{panel.NoticeText}\"");

            Check("没有待确认的之后「全部确认」自己灰掉（能点但点了没反应，比灰掉更让人困惑）",
                !panel.AcknowledgeAllCommand.CanExecute(), "");

            // ----------------------------------------------------------
            // ⑥ 导出：导的是「当前可见」，格式与按天流水账逐字节同构
            // ----------------------------------------------------------
            Directory.CreateDirectory(exportDir);

            var visiblePath = Path.Combine(exportDir, "visible.csv");
            var referencePath = Path.Combine(exportDir, "reference.csv");

            bool visibleOk = panel.Export(visiblePath);
            ScadaAlarmHistoryWriter.Export(referencePath, panel.Rows.Select(r => r.Record).ToList(), out var referenceError);

            var visibleBytes = File.ReadAllBytes(visiblePath);
            var referenceBytes = File.ReadAllBytes(referencePath);

            Check("导出与按天流水账逐字节同构（两份文件拼起来看，不会有一列对不上）",
                visibleOk && referenceError == null && visibleBytes.SequenceEqual(referenceBytes),
                $"导出={visibleBytes.Length}B / 参照={referenceBytes.Length}B / err={referenceError}");

            var exportedLines = File.ReadAllLines(visiblePath);

            Check("第一行是表头、共 13 列（列错位在 Excel 里表现为「数据莫名其妙对不上」）",
                exportedLines.Length == 3 && exportedLines[0].Split(',').Length == 13,
                $"{exportedLines.Length} 行 / {exportedLines[0].Split(',').Length} 列");

            Check("带 BOM（不带 BOM 的 UTF-8 CSV 用 Excel 打开中文必乱码）",
                visibleBytes.Length >= 3
                && visibleBytes[0] == 0xEF && visibleBytes[1] == 0xBB && visibleBytes[2] == 0xBF,
                "");

            Check("导出成功在底部留一句回执，并说清导了几条（导完一片安静，人不知道自己导没导成）",
                panel.NoticeText == $"已导出 {panel.Rows.Count} 条 → {visiblePath}",
                panel.NoticeText);

            panel.Keyword = "炉温";
            var filteredPath = Path.Combine(exportDir, "filtered.csv");
            bool filteredOk = panel.Export(filteredPath);
            int filteredLines = File.ReadAllLines(filteredPath).Length;

            Check("导出的是「当前可见」而不是全量：筛出 1 条就只导 1 条（现场要的是「筛出这个班次的严重报警，交班」）",
                filteredOk && filteredLines == 2 && panel.Rows.Count == 1,
                $"{filteredLines} 行 / 可见 {panel.Rows.Count} 条");

            panel.ResetFilterCommand.Execute();

            bool blankRejected = panel.Export("   ");

            Check("导出失败不抛、只在底部留一句原因（异常从命令里漏出去会变成未处理异常弹框，而现场只需要一句提示）",
                !blankRejected
                && panel.NoticeText.StartsWith("导出失败：", StringComparison.Ordinal)
                && panel.NoticeText.Contains("导出路径为空"),
                panel.NoticeText);

            // ----------------------------------------------------------
            // ⑦ 换挂与收场：一轮运行一套引擎，面板跟着走
            // ----------------------------------------------------------
            panel.Attach(context);      // 重挂同一个上下文：ReferenceEquals 短路

            Check("重挂同一个上下文不重复订阅（短路：一次报警不该触发两遍重建）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 1
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == 1,
                "");

            // 这一轮的时钟刻意贴着真实此刻（往前 90 秒）：假时钟会把记录钉在 6 小时前，
            // 而 DurationText 在「≥1 小时」那一档只渲染到分钟——「节拍把时长推着走」这条断言
            // 在那个档位上根本看不见（一秒的增量落在被舍掉的那两位上）。90 秒落在「X分Y秒」档，每秒都变。
            Func<DateTime> liveClock = () => DateTime.UtcNow.AddSeconds(-90);

            var engine2 = new ScadaAlarmEngine(doc, source, liveClock);
            engine2.Attach();
            var beat2 = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var context2 = new ScadaRuntimeContext(engine2, beat2);

            panel.Attach(context2);

            Check("换挂到新一轮上下文：上一轮那一套一条订阅都不留（留着就是上一轮的引擎攥着这一轮的面板）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmChanged)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmCleared)) == 0
                && SubscriberCount(beat2, nameof(ScadaBeatSource.Beat)) == 1
                && SubscriberCount(engine2, nameof(ScadaAlarmEngine.AlarmRaised)) == 1,
                $"旧={SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised))} / 新={SubscriberCount(engine2, nameof(ScadaAlarmEngine.AlarmRaised))}");

            Check("换挂后表里是新一轮的记录，不是上一轮留下的（「读的是本轮引擎的历史」这件事必须真成立）",
                panel.Rows.Count == 2 && panel.UnacknowledgedCount == 2,
                $"rows={panel.Rows.Count} / 未确认={panel.UnacknowledgedCount}");

            // ⑦-b 节拍把「持续时长」推着走：整表里唯一一格会随钟走的内容
            var tickingRow = panel.Rows[0];
            string durationBeforeTick = tickingRow.DurationText;

            beat2.Start();
            PumpDispatcher(1300);       // 跨过面板那个 1 秒的时长刷新周期
            string durationAfterTick = tickingRow.DurationText;

            Check("节拍把「持续时长」推着走：还没恢复那条的时长自己会长（这一格不刷新，现场看到的就是「卡住了」）",
                durationBeforeTick.EndsWith("秒", StringComparison.Ordinal)
                && durationAfterTick.EndsWith("秒", StringComparison.Ordinal)
                && durationBeforeTick != durationAfterTick,
                $"{durationBeforeTick} → {durationAfterTick}");

            Check("时长是单点刷新：整表没重建（行对象还是原来那个，选中与滚动位置都不会被冲掉）",
                ReferenceEquals(tickingRow, panel.Rows[0])
                && panel.Rows.Count == 2
                && tickingRow.Record.RecoveredAtUtc == null,
                $"same={ReferenceEquals(tickingRow, panel.Rows[0])} / rows={panel.Rows.Count}");

            Check("节拍只碰时长：报警名、状态、计数一个都没被这一秒的刷新改动",
                tickingRow.Name == "气压低" && tickingRow.CanAcknowledge
                && panel.UnacknowledgedCount == 2
                && panel.SummaryText == "共 2 条 · 未确认 2 · 显示 2",
                $"{tickingRow.Name} / {panel.SummaryText}");

            beat2.Stop();

            panel.Attach(null);

            Check("退挂：表清空，回到「本次运行还没有报警记录。」这一态（与「筛没了」分开说）",
                panel.Rows.Count == 0 && !panel.HasRows
                && panel.EmptyHint == "本次运行还没有报警记录。"
                && panel.SummaryText == "共 0 条 · 未确认 0 · 显示 0",
                $"\"{panel.EmptyHint}\"");

            engine2.Detach();
            engine.Detach();
            beat2.Stop();
            beat.Stop();

            Check("收场后引擎与节拍的订阅者都回到零（断言自己不留尾巴）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == 0
                && SubscriberCount(beat2, nameof(ScadaBeatSource.Beat)) == 0
                && SubscriberCount(engine, nameof(ScadaAlarmEngine.AlarmRaised)) == 0
                && SubscriberCount(engine2, nameof(ScadaAlarmEngine.AlarmRaised)) == 0
                && SubscriberCount(engine2, nameof(ScadaAlarmEngine.AlarmChanged)) == 0
                && SubscriberCount(engine2, nameof(ScadaAlarmEngine.AlarmCleared)) == 0,
                $"beat={SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} / beat2={SubscriberCount(beat2, nameof(ScadaBeatSource.Beat))}");

            ScadaEditHistory.Clear();
        }

        /// <summary>
        /// 为什么非要用反射：C# 的事件只对外开 += / -=，读不到订阅表。
        /// 而"装上挂订阅、摘掉退订阅"这件事在行为上是<b>看不出来</b>的——控件每次重读的都是
        /// 「当前那个上下文」，留着一份旧订阅只是让重读多做一次。它的真实代价是泄漏
        /// （上一轮的引擎攥着这一轮的控件），所以只能数订阅者来钉。
        ///
        /// 走的是编译器的后备字段（字段式事件的字段名就是事件名），这是编译器保证的命名，
        /// 不是实现细节；事件改成 add/remove 访问器形式的话这里会读成 0，届时断言会红——
        /// 那是好事，说明该改成显式记账了。
        /// </summary>
        private static int SubscriberCount(object owner, string eventName)
        {
            var field = owner.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);

            return (field?.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;
        }

        /// <summary>CSV 落盘：纯 .NET，无 WPF 依赖，可直接在主线程跑。</summary>
        private static void CheckAlarmCsvWriter()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ScadaAlarmHistory_" + Guid.NewGuid().ToString("N"));

            try
            {
                Check("默认目录落在软件目录下的 Alarms（与 Logs 刻意分开：报警历史是资产，运行日志是可随时清掉的过程记录）",
                    Path.GetFileName(ScadaAlarmHistoryWriter.DefaultDirectory) == "Alarms",
                    ScadaAlarmHistoryWriter.DefaultDirectory);

                var diagnostics = new List<string>();
                var writer = new ScadaAlarmHistoryWriter(
                    dir, (level, text) => diagnostics.Add($"{level}|{text}"));

                // 造一条"刚激活、还没恢复"的记录。文本里故意塞逗号与引号，验 RFC 4180。
                var def = new ScadaDocument().AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
                def.Bind(Guid.NewGuid(), "FurnaceTemp");
                def.Threshold = 1200;
                def.Deadband = 20;
                def.DelaySeconds = 3;
                def.Message = "炉温超限,请检查\"冷却\"";

                var activatedUtc = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc);
                var record = new ScadaAlarmRecord(def, activatedUtc, "1234.5");

                var day1 = new DateTime(2026, 9, 20, 9, 0, 0);
                bool firstWrite = writer.Append(record, day1);
                string path1 = Path.Combine(dir, "AlarmHistory-2026-09-20.csv");

                Check("按本地日期滚动出文件名（现场是按本地时间查「昨天那次报警」的）",
                    firstWrite && File.Exists(path1), Path.GetFileName(path1));

                byte[] bytes = File.ReadAllBytes(path1);
                Check("首三个字节是 UTF-8 BOM（不带 BOM 的 CSV 用 Excel 打开中文必乱码）",
                    bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    $"前 3 字节 {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2}");

                var lines1 = File.ReadAllLines(path1);
                const string expectedHeader = "激活时间,报警名称,报警文本,严重度,报警类型,变量名,条件描述,触发值,状态,确认时间,恢复时间,清除时间,持续时长";
                Check("表头 13 列、列序就是现场阅读顺序（先说是什么 → 再说多严重 → 最后是时间线）",
                    lines1[0] == expectedHeader, lines1[0]);

                string row1 = lines1[1];
                Check("RFC 4180 转义：含逗号的文本被双引号包住、内部引号翻倍（不转义会把一行劈成两列，在 Excel 里表现为「数据莫名其妙对不上」）",
                    row1.Contains("\"炉温超限,请检查\"\"冷却\"\"\""), row1);

                Check("行内事实来自记录快照：激活时间用本地时间、严重度/类型/条件都是人话（与面板共用同一份文案）",
                    row1.StartsWith(record.ActivatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal)
                    && row1.Contains("严重") && row1.Contains("高高限")
                    && row1.Contains("高高限 > 1200（回差 20）") && row1.Contains("1234.5")
                    && row1.Contains("激活"),
                    row1);

                Check("还没恢复的记录：持续时长列留空（把「到此刻为止」写进文件，过一天再翻就是一句假话）",
                    row1.EndsWith(","), row1);

                // 态迁移：确认 + 恢复，再追加一行 → 同一份文件里就能读出这条报警的完整时间线
                record.AcknowledgedAtUtc = activatedUtc.AddMinutes(2);
                record.RecoveredAtUtc = activatedUtc.AddMinutes(5);
                record.State = ScadaAlarmState.Recovered;
                writer.Append(record, day1);

                var lines2 = File.ReadAllLines(path1);
                Check("每次态迁移追加一行（文件本身可重放：按激活时间分组就能还原从生到死的全过程）",
                    lines2.Length == 3 && lines2[2].Contains("已恢复未确认"),
                    $"行数={lines2.Length}");

                Check("恢复之后才写持续时长，且算到恢复而非清除（清除时刻里含「人多久才来确认」，那是人慢不是报警持续）",
                    lines2[2].EndsWith("5分0秒")
                    && lines2[2].Contains(record.AcknowledgedAtLocal!.Value.ToString("yyyy-MM-dd HH:mm:ss"))
                    && lines2[2].Contains(record.RecoveredAtLocal!.Value.ToString("yyyy-MM-dd HH:mm:ss")),
                    lines2[2]);

                // 注意：判 BOM 必须用 Ordinal。默认的 StartsWith 走文化相关比较，而 U+FEFF 是"可忽略字符"，
                // 于是 "任何字符串".StartsWith("\uFEFF") 都会返回 true——这种断言写错了还永远绿。
                Check("追加不重写表头、也不重复插 BOM（表头只在文件为空时补一次）",
                    lines2[1][0] != '\uFEFF' && lines2[2][0] != '\uFEFF'
                    && lines2[1].StartsWith(record.ActivatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal),
                    $"行1={lines2[1].Substring(0, Math.Min(24, lines2[1].Length))}");

                Check("传 null 记录：不写、不抛、也不进故障态（调用方的空引用不该被记成磁盘故障）",
                    !writer.Append(null!, day1) && !writer.IsFaulted && writer.LastError == null,
                    writer.LastError ?? "");

                // 按天滚动：换一天就该是另一份文件，且新文件自带 BOM + 表头
                var day2 = new DateTime(2026, 9, 21, 9, 0, 0);
                writer.Append(record, day2);
                string path2 = Path.Combine(dir, "AlarmHistory-2026-09-21.csv");

                Check("跨日期落到另一份文件，新文件自带表头（按天滚动：单份文件不会涨到打不开）",
                    File.Exists(path2) && File.ReadAllLines(path2).Length == 2
                    && File.ReadAllLines(path2)[0] == expectedHeader,
                    File.Exists(path2) ? $"行数={File.ReadAllLines(path2).Length}" : "文件不存在");

                // 写不成：拿一个文件占住本该是目录的路径
                string blocked = Path.Combine(dir, "blocked");
                File.WriteAllText(blocked, "占位");

                var faultDiag = new List<string>();
                var faulty = new ScadaAlarmHistoryWriter(
                    blocked, (level, text) => faultDiag.Add($"{level}|{text}"));

                bool f1 = faulty.Append(record, day1);
                bool f2 = faulty.Append(record, day1);

                Check("写不成不抛：返回 false 并进入故障态（报警系统因为写不了历史文件而崩掉，比丢几行历史严重得多）",
                    !f1 && !f2 && faulty.IsFaulted && faulty.LastError != null,
                    faulty.LastError ?? "无错误信息");

                Check("故障只上报一次（磁盘故障时每行都报会把日志刷满，真正的原因反而被埋掉）",
                    faultDiag.Count == 1 && faultDiag[0].StartsWith("Error"),
                    string.Join(" / ", faultDiag));

                File.Delete(blocked);
                bool f3 = faulty.Append(record, day1);

                Check("故障恢复后回到正常态并补报一次（不再刷日志，但要让现场知道「又能写了」）",
                    f3 && !faulty.IsFaulted && faulty.LastError == null
                    && faultDiag.Count == 2 && faultDiag[1].StartsWith("Warning"),
                    string.Join(" / ", faultDiag));

                Check("成功的 writer 全程没报过诊断（诊断口只报故障翻转，不是写一行报一次）",
                    diagnostics.Count == 0, string.Join(" / ", diagnostics));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 节拍源：一屏只养一个定时器，所有"随时间流逝才会发生"的运行态行为都挂它上面。
        /// 这里既验"定时器真在跳"，也验"坏掉一个订阅者不连带打死其余订阅者"。
        /// </summary>
        private static void RunBeatSourceChecks()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;

            var errors = new List<string>();
            var beat = new ScadaBeatSource(dispatcher, null, (level, text) => errors.Add(text));

            Check("节拍周期缺省 200ms（报警引擎建议的 100~500ms，取中间值）",
                beat.Interval == TimeSpan.FromMilliseconds(200),
                $"{beat.Interval.TotalMilliseconds:0}ms");

            Check("周期下限钳在 10ms：再密就是拿 UI 线程烧 CPU，报警的延时分辨率也不会因此更准",
                new ScadaBeatSource(dispatcher, TimeSpan.FromMilliseconds(1)).Interval
                    == TimeSpan.FromMilliseconds(10),
                "");

            Check("起表前不在跳；Stop 可重复调用不炸", !beat.IsRunning, $"running={beat.IsRunning}");
            beat.Stop();
            beat.Start();
            beat.Start();   // 幂等：重复 Start 不该让闪烁回到起点
            Check("Start 幂等且起表后 IsRunning 为真（重复 Start 不重置相位）",
                beat.IsRunning, $"running={beat.IsRunning}");

            int ticks = 0;
            TimeSpan lastElapsed = TimeSpan.Zero;
            beat.Beat += e => { ticks++; lastElapsed = e; };

            beat.Interval = TimeSpan.FromMilliseconds(50);   // 运行中改周期立即生效，且不重置相位
            PumpDispatcher(400);

            Check("DispatcherTimer 确实在跳，且每拍都带着「累计时长」而不是「拍到了」"
                + "（漏一拍时消费者自己算得出此刻该亮该灭，相位不会永久错开）",
                ticks >= 3 && lastElapsed > TimeSpan.Zero,
                $"拍数={ticks} / 累计={lastElapsed.TotalMilliseconds:0}ms");

            Check("全部订阅者正常时不上报任何诊断（上报口只报真出事的那一拍）",
                errors.Count == 0, string.Join(" / ", errors));

            // 坏订阅者：抛异常的那一个不该连带打死排在它后面的
            int goodCalls = 0;
            beat.Beat += _ => throw new InvalidOperationException("故意的");
            beat.Beat += _ => goodCalls++;

            int ticksBefore = ticks;
            errors.Clear();
            PumpDispatcher(300);

            Check("一个订阅者抛异常不连带打死其余订阅者（DispatcherTimer.Tick 里漏出异常是进程级崩溃）",
                goodCalls >= 2 && ticks > ticksBefore,
                $"后续订阅者收到 {goodCalls} 拍 / 总拍数 {ticks}");

            Check("订阅者异常必须上报（报警系统悄悄不工作了，比崩掉更危险）",
                errors.Count >= 2 && errors.All(t => t.Contains("节拍")),
                $"上报 {errors.Count} 次：{errors.FirstOrDefault()}");

            beat.Stop();
            beat.Stop();
            Check("停表后可重复 Stop；累计时长保留最后一次的值不归零（停表不等于相位丢失）",
                !beat.IsRunning && beat.Elapsed > TimeSpan.Zero,
                $"running={beat.IsRunning} / 累计={beat.Elapsed.TotalMilliseconds:0}ms");
        }

        /// <summary>把当前 Dispatcher 的消息循环推起来指定毫秒数（让 DispatcherTimer 真的跳几拍）。</summary>
        private static void PumpDispatcher(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var stopper = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// JSON 比对失败时的定位辅助：返回第一处差异的上下文。
        /// 逐字节比对只说"不一致"没用——得知道是哪个字段先岔开的。
        /// </summary>
        private static string FirstDiff(string expected, string actual)
        {
            var shorter = Math.Min(expected.Length, actual.Length);

            for (var i = 0; i < shorter; i++)
            {
                if (expected[i] != actual[i])
                {
                    var from = Math.Max(0, i - 40);
                    var expectedSlice = expected.Substring(from, Math.Min(80, expected.Length - from));
                    var actualSlice = actual.Substring(from, Math.Min(80, actual.Length - from));
                    return $"首处差异 @{i}：期望…{expectedSlice}… / 实际…{actualSlice}…";
                }
            }

            return expected.Length == actual.Length
                ? string.Empty
                : $"长度不同：期望 {expected.Length} / 实际 {actual.Length}";
        }

        // ==================================================================
        //  [US] 账号存储（S12-c3）：验证出口 / 落盘形态 / 出厂账号 / 不变式
        // ==================================================================

        /// <summary>
        /// 账号存储断言。<b>不需要 STA 线程</b>：本类不碰 WPF，只做文件与密码学，
        /// 在断言进程的主线程上直接跑即可（<see cref="ScadaAccessPolicy"/> 那一份要
        /// <see cref="DispatcherTimer"/>，才必须挂 STA）。
        /// </summary>
        private static void UserStoreChecks()
        {
            Section("[US] 账号存储（S12-c3：PBKDF2 / 原子写 / 出厂账号 / 管理员不可清零）");

            string dir = Path.Combine(Path.GetTempPath(), "ScadaUsers_" + Guid.NewGuid().ToString("N"));

            try
            {
                RunUserStoreChecksCore(dir);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static void RunUserStoreChecksCore(string dir)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "ScadaUsers.json");

            // ---------------- ① 出厂账号：全新机器必须有人进得来 ----------------
            var store = new ScadaUserStore(path);

            Check("全新机器（没有账号文件）给一个内置管理员：否则现场谁也登不进去，权限功能等于把软件锁死",
                store.Users.Count == 1
                && store.Users[0].Name == ScadaUserStore.DefaultAdminName
                && store.Users[0].Role == ScadaRole.Administrator
                && store.LastLoadError == null,
                string.Join("/", store.Users.Select(u => $"{u.Name}:{u.Role.DisplayName()}")));

            Check("出厂账号当场可用：admin/admin123 → 管理员",
                store.TryValidate("admin", "admin123", out var seedRole, out _) && seedRole == ScadaRole.Administrator,
                seedRole.DisplayName());

            Check("登录框一开就知道该不该提醒改密：认得出「密码还停在出厂值」",
                store.IsUsingDefaultPassword("admin") && !store.IsUsingDefaultPassword("张三"),
                "");

            Check("只是打开过一次软件不留文件：账号文件只在真建了账号或改了密码之后才出现",
                !File.Exists(path),
                path);

            // ---------------- ② 验证出口：不区分「没这个账号」与「密码不对」 ----------------
            bool pwdWrong = !store.TryValidate("admin", "wrong", out _, out string wrongPwd);
            bool noSuchUser = !store.TryValidate("查无此人", "whatever", out _, out string noSuch);
            Check("密码错与账号不存在报同一句话：登录界面上的提示不泄漏「哪些账号存在」",
                pwdWrong && noSuchUser && wrongPwd == noSuch,
                $"{wrongPwd} / {noSuch}");

            bool noName = !store.TryValidate("   ", "admin123", out _, out string nameErr);
            bool noPwd = !store.TryValidate("admin", "", out _, out string pwdErr);
            Check("空用户名与空密码各自报清是哪一格没填（合成一句「登录失败」会让人把两格都重打一遍）",
                noName && nameErr.Contains("用户名") && noPwd && pwdErr.Contains("密码"),
                $"{nameErr} / {pwdErr}");

            Check("用户名忽略大小写：现场没人记得住当初建的是 Admin 还是 admin",
                store.TryValidate("ADMIN", "admin123", out var upperRole, out _) && upperRole == ScadaRole.Administrator,
                upperRole.DisplayName());

            // ---------------- ③ 落盘形态：明文不落盘、原子写不留渣 ----------------
            bool added1 = store.TryAddUser("张三", "zhang123", ScadaRole.Engineer, out string addErr1);
            bool added2 = store.TryAddUser("李四", "zhang123", ScadaRole.Operator, out string addErr2);

            Check("新建账号会落盘，且写的是原子写（目录里不留 .tmp 残渣）",
                added1 && added2 && File.Exists(path) && !File.Exists(path + ".tmp"),
                addErr1 ?? addErr2 ?? "");

            string json = File.ReadAllText(path);
            Check("密码不落盘：文件里没有明文密码，只有随机盐 + PBKDF2 哈希（拿到文件也只能逐个暴力试）",
                !json.Contains("admin123") && !json.Contains("zhang123")
                && json.Contains("\"Salt\"") && json.Contains("\"Hash\""),
                json.Replace("\r", "").Replace("\n", " ").Substring(0, Math.Min(110, json.Length)) + "…");

            var records = JArray.Parse(json);
            var salts = records.Select(r => (string?)r["Salt"]).ToArray();
            Check("同一个密码在不同账号上盐不同 → 哈希也不同：一张彩虹表没法通杀全厂账号",
                salts.Length == 3
                && salts.All(s => !string.IsNullOrEmpty(s))
                && salts.Distinct(StringComparer.Ordinal).Count() == 3,
                string.Join(" / ", salts));

            // ---------------- ④ 往返：换个实例读回来 ----------------
            var reloaded = new ScadaUserStore(path);
            Check("换个实例读回来（等于重启软件）：账号、角色、密码都对得上",
                reloaded.TryValidate("张三", "zhang123", out var reloadRole, out _)
                && reloadRole == ScadaRole.Engineer
                && !reloaded.TryValidate("张三", "admin123", out _, out _)
                && reloaded.Users.Count == 3,
                $"{reloadRole.DisplayName()} / {reloaded.Users.Count} 个账号");

            // ---------------- ⑤ 改密：必须报旧密码，且新旧交替干净 ----------------
            Check("改密必须报旧密码：只报新密码就能改，等于任何人都能改掉管理员的密码",
                !reloaded.TryChangePassword("张三", "wrong", "newpass", out string badOld)
                && reloaded.TryValidate("张三", "zhang123", out _, out _),
                badOld ?? "");

            Check("新密码太短不收（四位数够拦住空密码与手滑，又不至于让现场骂人）",
                !reloaded.TryChangePassword("张三", "zhang123", "12", out string shortErr) && shortErr.Contains("至少"),
                shortErr);

            Check("改密成功：新密码立即可用、旧密码立即失效",
                reloaded.TryChangePassword("张三", "zhang123", "newpass", out string chgErr)
                && reloaded.TryValidate("张三", "newpass", out _, out _)
                && !reloaded.TryValidate("张三", "zhang123", out _, out _),
                chgErr ?? "");

            Check("改密后出厂密码的提醒自动熄灭",
                reloaded.TryChangePassword("admin", "admin123", "admpass", out _)
                && !reloaded.IsUsingDefaultPassword("admin"),
                "");

            Check("改密落盘且仍然不存明文：换个实例读回来用的是新密码，文件里也找不到它",
                new ScadaUserStore(path).TryValidate("admin", "admpass", out _, out _)
                && !File.ReadAllText(path).Contains("admpass"),
                "");

            // ---------------- ⑥ 用户管理的不变式：管理员不能被清零 ----------------
            Check("账号重名（忽略大小写）直接拒：Admin 与 admin 是同一个人的两种写法，不能各建一个",
                !reloaded.TryAddUser("ADMIN", "whatever", ScadaRole.Operator, out string dupErr)
                && dupErr.Contains("已存在"),
                dupErr);

            bool badAdd = !reloaded.TryAddUser("王五", "wang1234", ScadaRole.Undefined, out string undefAdd);
            bool badSet = !reloaded.TrySetRole("张三", ScadaRole.Undefined, out string undefSet);
            Check("非法角色既建不出账号、也改不上去：Undefined 是「文件坏了」的哨兵，不是一档角色",
                badAdd && badSet
                && !reloaded.Users.Any(u => u.Name == "王五")
                && reloaded.Users.First(u => u.Name == "张三").Role == ScadaRole.Engineer,
                $"{undefAdd} / {undefSet}");

            Check("删掉唯一的管理员会被拦下：删了他，用户管理这条路就从软件里彻底消失，只能删文件重来",
                !reloaded.TryRemoveUser("admin", out string lastAdmin) && lastAdmin.Contains("至少"),
                lastAdmin);

            Check("把唯一的管理员降级同样拦下：降级的后果与删除一模一样",
                !reloaded.TrySetRole("admin", ScadaRole.Engineer, out string demote)
                && demote.Contains("至少")
                && reloaded.TryValidate("admin", "admpass", out var stillAdmin, out _)
                && stillAdmin == ScadaRole.Administrator,
                demote);

            Check("角色没变是空操作：返回成功、不报错，也不做一次没意义的文件替换",
                reloaded.TrySetRole("admin", ScadaRole.Administrator, out string sameRole) && sameRole == null,
                sameRole ?? "null");

            bool addedAdmin = reloaded.TryAddUser("李管理", "lim1234", ScadaRole.Administrator, out _);
            bool demoted = reloaded.TrySetRole("admin", ScadaRole.Engineer, out string okDemote);
            Check("另建一个管理员之后，原来那个就能降级了：不变式守的是「至少一个」，不是「某一个」",
                addedAdmin && demoted
                && reloaded.Users.First(u => u.Name == "admin").Role == ScadaRole.Engineer,
                okDemote ?? "");

            Check("删账号：删掉之后密码再对也登不进来",
                reloaded.TryRemoveUser("李四", out string rmErr)
                && !reloaded.TryValidate("李四", "zhang123", out _, out _),
                rmErr ?? "");

            // ---------------- ⑦ 坏文件：不装看不见，也不让人进不去 ----------------
            // 拿一份真文件把某个账号的 Role 改成 9（高版本存、低版本读），密码不动——
            // 这样才能确定失败原因是「角色非法」而不是「密码不对」。
            string badRolePath = Path.Combine(dir, "BadRole.json");
            var doc = JArray.Parse(File.ReadAllText(path));
            var adminRecord = doc.Children<JObject>().First(r => (string?)r["Name"] == "admin");
            adminRecord["Role"] = 9;
            File.WriteAllText(badRolePath, doc.ToString());

            var badRole = new ScadaUserStore(badRolePath);
            Check("角色数值不认识时报的是「角色值非法」而不是「密码不正确」：照后者去反复重输密码，永远解决不了",
                !badRole.TryValidate("admin", "admpass", out _, out string badRoleErr)
                && badRoleErr.Contains("角色值非法"),
                badRoleErr);

            Check("坏角色的账号不被悄悄丢掉：留在清单里，用户管理页才看得见它、修得动它",
                badRole.Users.Any(u => u.Name == "admin"),
                string.Join("/", badRole.Users.Select(u => $"{u.Name}:{(int)u.Role}")));

            string brokenPath = Path.Combine(dir, "Broken.json");
            File.WriteAllText(brokenPath, "{ 这不是 JSON");
            var broken = new ScadaUserStore(brokenPath);
            Check("账号文件读坏了不阻断启动：回落到出厂账号，并把原因留在 LastLoadError（否则现场以为自己在用自己的账号）",
                broken.TryValidate("admin", "admin123", out _, out _)
                && broken.LastLoadError != null
                && broken.LastLoadError.Contains("回落到出厂账号"),
                broken.LastLoadError ?? "null");

            string emptyPath = Path.Combine(dir, "Empty.json");
            File.WriteAllText(emptyPath, "[]");
            var empty = new ScadaUserStore(emptyPath);
            Check("空清单与读坏同样处理：一个「有账号文件但一条账号都没有」的软件，等于谁也进不去",
                empty.TryValidate("admin", "admin123", out _, out _) && empty.LastLoadError != null,
                empty.LastLoadError ?? "null");

            string partialPath = Path.Combine(dir, "Partial.json");
            File.WriteAllText(partialPath,
                "[{\"Name\":\"残缺\"},{\"Name\":\"\",\"Salt\":\"AA==\",\"Hash\":\"AA==\",\"Iterations\":1000}]");
            var partial = new ScadaUserStore(partialPath);
            Check("残缺记录（缺盐缺哈希）被剔除：不留一条「列得出来、却永远改不了密码」的僵尸账号",
                partial.Users.All(u => u.Name != "残缺") && partial.Users.Count == 1,
                string.Join("/", partial.Users.Select(u => u.Name)));

            // ---------------- ⑧ 写盘失败要回滚内存 ----------------
            // 把「文件路径」指到一个同名的已存在目录上：临时文件写得下去，替换那一步必然失败，
            // 等价于磁盘满 / 目录只读。临时文件落在 dir 内，随 finally 一起清掉。
            string blockedDir = Path.Combine(dir, "sub");
            Directory.CreateDirectory(blockedDir);
            string blockedPath = Path.Combine(blockedDir, "ScadaUsers.json");
            Directory.CreateDirectory(blockedPath);

            var unwritable = new ScadaUserStore(blockedPath);
            Check("写盘失败时内存态回滚：否则界面显示改成功了、重启又变回去，而真正的报错早滚出屏幕了",
                !unwritable.TryAddUser("写不进去的人", "pw1234", ScadaRole.Operator, out string saveErr)
                && saveErr != null && saveErr.Contains("保存失败")
                && unwritable.Users.All(u => u.Name != "写不进去的人"),
                saveErr ?? "null");

            // ---------------- ⑨ 账号操作落审计（S13-c）：谁把谁的密码/角色改成了什么 ----------------
            // 这一问是权限体系里唯一真正致命的一问，而它必须三个月后还答得上来——
            // 所以落笔点选在 ScadaUserStore（五个方法是账号数据的全部出口），而不是两个弹窗的 VM。
            string auditDir = Path.Combine(dir, "audit");
            var auditWriter = new ScadaAuditWriter(auditDir);

            // 读回全部审计行（跳过每份文件的表头）。按目录里所有 Audit-*.csv 汇总，
            // 这样断言不会因为"正好跑过午夜"而假红。
            string[] AuditRows()
            {
                if (!Directory.Exists(auditDir)) return Array.Empty<string>();
                return Directory.GetFiles(auditDir, "Audit-*.csv")
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .SelectMany(p => File.ReadAllLines(p).Skip(1))
                    .ToArray();
            }

            string actor = "李管理";
            var audited = new ScadaUserStore(
                Path.Combine(dir, "Audited.json"), auditWriter, () => actor);

            audited.TryAddUser("赵六", "zhao1234", ScadaRole.Engineer, out _);
            var rows = AuditRows();

            Check("建账号落一条审计：动作=创建账号、结果=成功，说明里带上给了几档角色（光有名字答不上最要紧的那一半）",
                rows.Length == 1
                && rows[0].Contains("账号管理") && rows[0].Contains("创建账号")
                && rows[0].Contains("成功") && rows[0].Contains("角色：工程师"),
                string.Join(" | ", rows));

            Check("署名来自注入的提供者（本类不认识会话，宿主把它接到「此刻登录着谁」上）",
                rows.Length == 1 && rows[0].Contains("李管理"), rows[0]);

            actor = "张三";
            audited.TryChangePassword("赵六", "zhao1234", "zhao5678", out _);
            rows = AuditRows();

            Check("改密成功落审计，说明写「密码已修改」，且绝不把新密码本身写进去（凭证不该成为第二份密码泄漏点）",
                rows.Length == 2 && rows[1].Contains("修改密码") && rows[1].Contains("成功")
                && rows[1].Contains("密码已修改") && !rows[1].Contains("zhao5678"),
                rows[1]);

            audited.TryChangePassword("赵六", "wrong", "whatever", out _);
            rows = AuditRows();

            Check("失败也落一条，说明就是界面上弹的那句话：只记成功的那一半，事后问「他到底动过没有」答案会是「没有」",
                rows.Length == 3 && rows[2].Contains("修改密码") && rows[2].Contains("失败")
                && rows[2].Contains("用户名或密码不正确"),
                rows[2]);

            audited.TryResetPassword("赵六", "reset1234", out _);
            rows = AuditRows();

            Check("重置密码单独一个动作名（管理员路径、未校验旧密码）：审计里必须一眼分出这是哪条路走的",
                rows.Length == 4 && rows[3].Contains("重置密码") && rows[3].Contains("未校验旧密码"),
                rows[3]);

            audited.TrySetRole("赵六", ScadaRole.Administrator, out _);
            rows = AuditRows();

            Check("改角色把「从几档到几档」写进说明：提权最省事的办法不是破解哈希，而是让人把角色改一下",
                rows.Length == 5 && rows[4].Contains("修改角色") && rows[4].Contains("工程师 → 管理员"),
                rows[4]);

            audited.TryRemoveUser("赵六", out _);
            rows = AuditRows();

            Check("删号把「原来是什么身份」写进说明（先记下角色再摘除：摘掉之后就查不出来了）",
                rows.Length == 6 && rows[5].Contains("删除账号") && rows[5].Contains("原角色：管理员"),
                rows[5]);

            audited.TrySetRole("admin", ScadaRole.Administrator, out _);
            rows = AuditRows();

            Check("角色没变也记一条（写「角色未变…未写盘」）：不记的话，「他没动过」与「他动了但值没变」分不出来",
                rows.Length == 7 && rows[6].Contains("修改角色") && rows[6].Contains("未写盘"),
                rows[6]);

            int beforeValidate = AuditRows().Length;
            audited.TryValidate("admin", "admin123", out _, out _);

            Check("验证身份不落审计：它不改变任何数据，记进去只会让「账号管理」这一栏被登录动作刷满",
                AuditRows().Length == beforeValidate,
                $"{beforeValidate} → {AuditRows().Length}");

            var anonymous = new ScadaUserStore(
                Path.Combine(dir, "Anon.json"), auditWriter, () => null);
            anonymous.TryAddUser("无名", "anon1234", ScadaRole.Operator, out _);
            rows = AuditRows();

            Check("署名取不到时回落成「未登录」：「未登录就把账号改了」本身就是一条线索，而空白格分不清「没记」与「没署名」",
                rows.Last().Contains("未登录"), rows.Last());

            var silent = new ScadaUserStore(Path.Combine(dir, "Silent.json"));
            Check("不接审计（audit=null）时五个写操作一字不差地照跑：审计是旁路，不是账号操作的前置条件",
                silent.TryAddUser("安静", "quiet123", ScadaRole.Operator, out _)
                && silent.TrySetRole("安静", ScadaRole.Engineer, out _)
                && silent.TryChangePassword("安静", "quiet123", "quiet456", out _)
                && silent.TryResetPassword("安静", "quiet789", out _)
                && silent.TryRemoveUser("安静", out _),
                "");

            // 审计写不成不能反过来让账号操作失败：拿一个文件占住本该是目录的路径（等价于目录只读）
            string blockedAudit = Path.Combine(dir, "blocked-audit");
            File.WriteAllText(blockedAudit, "占位");
            var brokenAudit = new ScadaAuditWriter(blockedAudit);
            var resilient = new ScadaUserStore(
                Path.Combine(dir, "Resilient.json"), brokenAudit, () => "张三");

            Check("审计写不成时账号操作照样成功（凭证丢了可以补，密码改不进去是停线）：Audit 绝不抛、绝不影响返回值",
                resilient.TryAddUser("坚韧", "tough123", ScadaRole.Operator, out string toughErr)
                && brokenAudit.IsFaulted && resilient.Users.Any(u => u.Name == "坚韧"),
                toughErr ?? "");
        }

        /// <summary>
        /// [AU] 操作审计落盘（S12-d）。分两段看：
        ///
        /// ① <b>落盘器本身</b>：CSV 的形状（BOM / 表头 / RFC 4180 转义 / 毫秒时间戳）、
        /// 按天滚动、四档结果的中文名、空署名与空对象的回落、写不成不抛 + 故障去重。
        ///
        /// ② <b>接线</b>：让分发器真跑一条"写变量"动作，审计文件里就该出现一行，
        /// 且"说明"列写着「旧值 → 新值」。<b>这一段才是重点</b>——①全绿而②断了
        /// （比如 App 里注册分发器时忘了把 audit 传进去），现场表现是"审计文件一直空着、
        /// 但谁也不报错"，只有走一遍真链路才看得出来。
        ///
        /// 越权拦截那一档（宿主 <c>OnElementAccessDenied</c>）不在这里跑真宿主：那要开窗口（GUI）。
        /// 这里照它的口径直接造一条记录，锁住"写出来长什么样"——两边用的是同一个构造函数与同一个枚举。
        ///
        /// ③ <b>保留策略</b>（S13-c）：按天滚动只解决"单份文件不会涨到打不开"，不解决"目录会一直涨"。
        /// 这一段用假日期测 <c>PurgeExpired</c>，锁住四条：删哪些、绝不误删别人的文件、
        /// <c>&lt;= 0</c> 表示永不清理、跨天只清一次；外加"写不成时不做清理"这条顺序约束。
        /// </summary>
        private static void AuditWriterChecks()
        {
            Section("[AU] 操作审计落盘（S12-d：CSV 凭证 / 毫秒时间戳 / 按天滚动 / 四档结果 / 写不成不抛 / S13-c：保留策略）");

            string dir = Path.Combine(Path.GetTempPath(), "ScadaAudit_" + Guid.NewGuid().ToString("N"));

            try
            {
                Check("默认目录落在软件目录下的 Audit（与 Logs「可随时清」、Alarms「报警资产」刻意三分：凭证不能被「清理日志」一起删掉）",
                    Path.GetFileName(ScadaAuditWriter.DefaultDirectory) == "Audit",
                    ScadaAuditWriter.DefaultDirectory);

                var diagnostics = new List<string>();
                var writer = new ScadaAuditWriter(dir, (level, text) => diagnostics.Add($"{level}|{text}"));

                // 造一条"改配方"的操作记录。字段里故意塞逗号与引号，验 RFC 4180。
                var day1 = new DateTime(2026, 9, 20, 9, 0, 0);
                var entry = new ScadaAuditEntry(
                    "张三", "配方按钮", "按下", "写变量 配方 := 2,3\"A\"",
                    ScadaAuditOutcome.Success, "配方：1 → 2,3\"A\"");

                bool firstWrite = writer.Append(entry, day1);
                string path1 = Path.Combine(dir, "Audit-2026-09-20.csv");

                Check("按本地日期滚动出文件名（现场是按本地时间查「昨天那次操作」的）",
                    firstWrite && File.Exists(path1), Path.GetFileName(path1));

                byte[] bytes = File.ReadAllBytes(path1);
                Check("首三个字节是 UTF-8 BOM（不带 BOM 的 CSV 用 Excel 打开中文必乱码）",
                    bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    $"前 3 字节 {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2}");

                var lines1 = File.ReadAllLines(path1);
                const string expectedHeader = "时间,操作者,对象,事件,动作,结果,说明";
                Check("表头 7 列、列序就是现场阅读顺序（谁 → 对谁 → 什么事件 → 干了什么 → 成没成 → 说明）",
                    lines1[0] == expectedHeader, lines1[0]);

                string row1 = lines1[1];
                Check("RFC 4180 转义：含逗号的字段被双引号包住、内部引号翻倍（不转义会把一行劈成两列，在 Excel 里表现为「数据莫名其妙对不上」）",
                    row1.Contains("\"写变量 配方 := 2,3\"\"A\"\"\"")
                    && row1.Contains("\"配方：1 → 2,3\"\"A\"\"\""),
                    row1);

                Check("时间戳精确到毫秒：审计是「人的操作」，同一秒里连点两下也得分得出先后（秒精度一经排序就丢次序）",
                    row1.StartsWith(day1.ToString("yyyy-MM-dd HH:mm:ss.fff"), StringComparison.Ordinal),
                    row1.Substring(0, Math.Min(30, row1.Length)));

                Check("行内事实来自记录：操作者/对象/事件/动作/结果都是人话，且与面板、日志共用同一份文案",
                    row1.Contains("张三") && row1.Contains("配方按钮") && row1.Contains("按下")
                    && row1.Contains("写变量 配方 := 2,3\"\"A\"\"") && row1.Contains("成功"),
                    row1);

                // 四档结果各自的中文名：现场查的人看到的必须是「被拒绝」，而不是「Denied」
                var outcomes = new[]
                {
                    ScadaAuditOutcome.Success, ScadaAuditOutcome.Skipped,
                    ScadaAuditOutcome.Failed, ScadaAuditOutcome.Denied,
                };
                Check("四档结果都有中文名：「被拒绝」与「失败」刻意分开——权限不足不是故障，是产品设计如此",
                    string.Join("/", outcomes.Select(o => o.DisplayName())) == "成功/未执行/失败/被拒绝",
                    string.Join("/", outcomes.Select(o => o.DisplayName())));

                Check("枚举值越界回「未知」而不抛：高版本存下的档位读进低版本时，该说「不认识」而不是崩掉",
                    ((ScadaAuditOutcome)99).DisplayName() == "未知",
                    ((ScadaAuditOutcome)99).DisplayName());

                // 空署名 / 空对象 / 没有说明：三列都不许留空白（空着分不清「没记」和「没有」）
                writer.Append(new ScadaAuditEntry(
                    "   ", null, "按下", "切换画面 → 主界面", ScadaAuditOutcome.Skipped), day1);

                string rowBlank = File.ReadAllLines(path1)[2];
                Check("空署名回落成「未登录」、空对象回落成「未命名对象」：一条没有署名/没有对象的记录等于白记",
                    rowBlank.Contains("未登录") && rowBlank.Contains("未命名对象"),
                    rowBlank);

                Check("说明列为 null 时留空、且不写「null」字样（文件是给人看的；这一档本来就没有说明）",
                    rowBlank.EndsWith(",", StringComparison.Ordinal) && !rowBlank.Contains("null"),
                    rowBlank);

                Check("追加不重写表头、也不重复插 BOM（表头只在文件为空时补一次）",
                    rowBlank[0] != '\uFEFF'
                    && rowBlank.StartsWith(day1.ToString("yyyy-MM-dd HH:mm:ss.fff"), StringComparison.Ordinal),
                    rowBlank.Substring(0, Math.Min(24, rowBlank.Length)));

                // 越权拦截那一档（宿主 OnElementAccessDenied 的口径，逐字）：这是权限系统里最该留痕的一条
                writer.Append(new ScadaAuditEntry(
                    "张三", "启动按钮", "权限校验", "（未执行）",
                    ScadaAuditOutcome.Denied, "需要工程师权限，当前是操作员"), day1);

                string deniedRow = File.ReadAllLines(path1).Last();
                Check("越权尝试单独记「被拒绝」档并带上原因：只记成功的那一半，事后查「他到底按过没有」答案会是「没有」",
                    deniedRow.Contains("权限校验") && deniedRow.Contains("被拒绝")
                    && deniedRow.EndsWith("需要工程师权限，当前是操作员", StringComparison.Ordinal),
                    deniedRow);

                Check("被拦下的那一条动作列写「（未执行）」而不是留空：空着分不清「没记」和「没有动作」",
                    deniedRow.Contains("（未执行）"), deniedRow);

                // 按天滚动：换一天就该是另一份文件，且新文件自带 BOM + 表头
                var day2 = new DateTime(2026, 9, 21, 9, 0, 0);
                writer.Append(entry, day2);
                string path2 = Path.Combine(dir, "Audit-2026-09-21.csv");

                Check("跨日期落到另一份文件，新文件自带表头（按天滚动：单份文件不会涨到打不开）",
                    File.Exists(path2) && File.ReadAllLines(path2).Length == 2
                    && File.ReadAllLines(path2)[0] == expectedHeader,
                    File.Exists(path2) ? $"行数={File.ReadAllLines(path2).Length}" : "文件不存在");

                Check("传 null 记录：不写、不抛、也不进故障态（调用方的空引用不该被记成磁盘故障）",
                    !writer.Append(null!, day1) && !writer.IsFaulted && writer.LastError == null,
                    writer.LastError ?? "");

                // 写不成：拿一个文件占住本该是目录的路径（等价于目录只读 / 路径被占）
                string blocked = Path.Combine(dir, "blocked");
                File.WriteAllText(blocked, "占位");

                var faultDiag = new List<string>();
                var faulty = new ScadaAuditWriter(blocked, (level, text) => faultDiag.Add($"{level}|{text}"));

                bool f1 = faulty.Append(entry, day1);
                bool f2 = faulty.Append(entry, day1);

                Check("写不成不抛：返回 false 并进入故障态（审计写不成反过来让操作失败，现场看到的就是「点按钮程序崩了」）",
                    !f1 && !f2 && faulty.IsFaulted && faulty.LastError != null,
                    faulty.LastError ?? "无错误信息");

                Check("故障只上报一次（磁盘故障时每行都报会把日志刷满，真正的原因反而被埋掉）",
                    faultDiag.Count == 1 && faultDiag[0].StartsWith("Error"),
                    string.Join(" / ", faultDiag));

                File.Delete(blocked);
                bool f3 = faulty.Append(entry, day1);

                Check("故障恢复后回到正常态并补报一次（不再刷日志，但要让现场知道「又能写了」）",
                    f3 && !faulty.IsFaulted && faulty.LastError == null
                    && faultDiag.Count == 2 && faultDiag[1].StartsWith("Warning"),
                    string.Join(" / ", faultDiag));

                Check("成功的 writer 全程没报过诊断（诊断口只报故障翻转，不是写一行报一次）",
                    diagnostics.Count == 0, string.Join(" / ", diagnostics));

                // ---------------- ② 接线：分发器真跑一条动作，审计里就该有一行 ----------------

                string wireDir = Path.Combine(dir, "wire");
                var wired = new ScadaAuditWriter(wireDir);
                var log = new LoggerStub();
                var values = new FakeValueSource();
                var startVar = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(), Name = "启动", DataType = typeof(int), Value = 0,
                };
                values.ById[startVar.VariableId] = startVar;
                values.ByName[startVar.Name] = startVar;

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction
                {
                    Type = ScadaActionType.WriteVariable,
                    VariableId = startVar.VariableId,
                    VariableName = "启动",
                    Value = "1",
                });

                new ScadaActionDispatcher(log, values, new FakeNavigator(), wired)
                    .Dispatch(hook, "启动按钮", "张三");

                // 分发器不带日期调 Append（时间戳由落盘端在落笔那一刻盖），文件名按当天走——
                // 用 SuggestFileName 现算，断言就不会因为"跑的时候正好跨了午夜"而假红。
                string wirePath = Path.Combine(wireDir, ScadaAuditWriter.SuggestFileName(DateTime.Now));

                Check("接线走通：一次点击 → 审计文件里真出现一行（这是「到底接没接上」的唯一证据）",
                    File.Exists(wirePath) && File.ReadAllLines(wirePath).Length == 2,
                    File.Exists(wirePath) ? $"行数={File.ReadAllLines(wirePath).Length}" : "文件不存在");

                string wireRow = File.ReadAllLines(wirePath)[1];
                Check("那一行把「谁 · 对谁 · 什么事件 · 干了什么 · 成没成」都写全了",
                    wireRow.Contains("张三") && wireRow.Contains("启动按钮") && wireRow.Contains("按下")
                    && wireRow.Contains("写变量 启动 := 1") && wireRow.Contains("成功"),
                    wireRow);

                Check("「说明」列写着净效果「旧值 → 新值」：审计独有的那一问（日志只写了目标值），而它只有执行侧拿得到",
                    wireRow.EndsWith("启动：0 → 1", StringComparison.Ordinal),
                    wireRow);

                // 不接 audit（可选参数默认 null）：动作照跑、日志照记——审计是旁路，不是执行的前置条件。
                // 断言工程里那 16 处 new ScadaActionDispatcher(...) 全靠这条默认参数不碰文件系统。
                startVar.Value = 0;
                var plainLog = new LoggerStub();
                new ScadaActionDispatcher(plainLog, values, new FakeNavigator())
                    .Dispatch(hook, "启动按钮", "张三");

                Check("不接审计时动作一字不差地照跑（审计是旁路：写不成审计不该让操作失败）",
                    plainLog.Lines.Count == 1 && Equals(startVar.Value, 1),
                    $"{plainLog.Lines.FirstOrDefault() ?? "无"} / 变量值 {startVar.Value ?? "null"}");

                // ---------------- ③ 保留策略：按天滚动只解决「单份文件打不开」，不解决「目录一直涨」 ----------------

                Check("默认保留 90 天，且能从外面读到（现场要能回答「这份凭证能留多久」）",
                    ScadaAuditWriter.DefaultRetentionDays == 90
                    && new ScadaAuditWriter(Path.Combine(dir, "retain")).RetentionDays == 90,
                    ScadaAuditWriter.DefaultRetentionDays.ToString());

                string retainDir = Path.Combine(dir, "retain");
                Directory.CreateDirectory(retainDir);

                var today = new DateTime(2026, 9, 20, 10, 0, 0);

                // 造一批「本类自己会建的文件」：截止日前后都放几份。
                // 截止日 = 今天 - (90 - 1) = 今天 - 89 天；早于它的才删。
                for (int back = 0; back <= 100; back += 5)
                    File.WriteAllText(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-back))), "x");

                // 边界：正好卡在截止日那一天的必须留下（保留期是「最近 90 天含今天」）
                string cutoffPath = Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-89)));
                File.WriteAllText(cutoffPath, "x");

                // 现场往这个目录里丢的东西：清理功能一个都不许碰。
                // 「Audit-2026-13-45.csv」故意长得像日期却解析不出来——宁可漏清一个，也不能误删一个。
                string[] mustSurvive =
                {
                    "Audit-manual.csv",
                    "Audit-2026-13-45.csv",
                    "Audit-2026-09-20-backup.csv",
                    "notes.txt",
                    "AlarmHistory-2020-01-01.csv",
                };
                foreach (var name in mustSurvive)
                    File.WriteAllText(Path.Combine(retainDir, name), "x");

                var retain = new ScadaAuditWriter(retainDir);
                int removed = retain.PurgeExpired(today);

                Check("超出保留期的按天文件被清掉（back=90/95/100 三份），保留期内的一份不少",
                    removed == 3
                    && !File.Exists(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-90))))
                    && !File.Exists(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-95))))
                    && !File.Exists(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-100))))
                    && File.Exists(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-85))))
                    && File.Exists(Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today))),
                    $"删除 {removed} 份");

                Check("正好卡在截止日那一天的不删（保留期是「最近 90 天含今天」，差一天就是差一天）",
                    File.Exists(cutoffPath), Path.GetFileName(cutoffPath));

                var survivors = mustSurvive.Where(n => !File.Exists(Path.Combine(retainDir, n))).ToArray();
                Check("只删本类自建的文件：手工放进这个目录的任何东西（备份/汇总/备注/别的模块的文件）一个都不碰——清理功能删掉用户的文件是这类功能最不能犯的错",
                    survivors.Length == 0,
                    survivors.Length == 0 ? "一个都没碰" : "被误删：" + string.Join("/", survivors));

                // 传 0/负数 = 永不清理：留给「法规要求留档、磁盘也够」的现场。
                // 这条必须在算截止日之前先判，否则截止日会算到「今天之后」，把今天刚写的那份也一起删掉。
                string ancientPath = Path.Combine(retainDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-400)));
                File.WriteAllText(ancientPath, "x");

                var keeper = new ScadaAuditWriter(retainDir, retentionDays: 0);
                int keptCount = keeper.PurgeExpired(today);

                Check("传 0 = 永不清理（现场要留档）；且这条判定在算截止日之前——否则截止日落到今天之后，会把刚写进去的凭证也删掉",
                    keptCount == 0 && keeper.RetentionDays == 0 && File.Exists(ancientPath),
                    $"删除 {keptCount} 份 / 过期文件还在={File.Exists(ancientPath)}");

                // 跨天那一刻清一次：同一天里反复追加不该反复扫目录
                string onceDir = Path.Combine(dir, "once");
                Directory.CreateDirectory(onceDir);
                var once = new ScadaAuditWriter(onceDir);

                once.Append(entry, today);   // 当天第一次写入会清一次（_lastPurgedDay 为空）

                string stalePath = Path.Combine(onceDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-200)));
                File.WriteAllText(stalePath, "x");

                once.Append(entry, today.AddHours(1));   // 同一天再写一次

                Check("同一天里再写入不会重复清理（_lastPurgedDay 在清理之前记下：某份删不掉时不该每次追加都重扫目录、重报警告）",
                    File.Exists(stalePath),
                    File.Exists(stalePath) ? "没被清（对）" : "被清了（说明每次追加都在扫目录）");

                once.Append(entry, today.AddDays(1));    // 跨到新的一天

                Check("跨到新的一天会再清一次（需要清的文件一天只多一份，跨天清一次刚好；进程刚起来当天第一次写入也会清）",
                    !File.Exists(stalePath), File.Exists(stalePath) ? "还在（不对）" : "已清（对）");

                Check("清理绝不把落盘端标成故障：旧文件删不掉（多半正被 Excel 开着）与「新记录写不进去」是两件事，混一档会产生误导性告警",
                    !retain.IsFaulted && retain.LastError == null,
                    retain.LastError ?? "无错误信息");

                Check("目录还不存在时清理不抛、返回 0（进程刚起来、第一次写入之前就可能被调用）",
                    new ScadaAuditWriter(Path.Combine(dir, "never-created")).PurgeExpired(today) == 0,
                    "");

                // 清理放在写成功之后：反过来的话，磁盘满时先删掉旧凭证，等于把唯一能作证的那份赔进去。
                // 这里用一个同名目录占住「今天」那份文件的路径 → 追加必然失败。
                string noWriteDir = Path.Combine(dir, "nowrite");
                Directory.CreateDirectory(noWriteDir);
                string stale2 = Path.Combine(noWriteDir, ScadaAuditWriter.SuggestFileName(today.AddDays(-300)));
                File.WriteAllText(stale2, "x");
                Directory.CreateDirectory(Path.Combine(noWriteDir, ScadaAuditWriter.SuggestFileName(today)));

                var cantWrite = new ScadaAuditWriter(noWriteDir);
                bool wrote = cantWrite.Append(entry, today);

                Check("写不成时不做清理（清理排在写成功之后）：磁盘满时先删旧凭证，等于把唯一能作证的赔进去",
                    !wrote && cantWrite.IsFaulted && File.Exists(stale2),
                    $"写成功={wrote} / 过期文件还在={File.Exists(stale2)}");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        // ==================================================================
        //  [SS] 系统参数设置（S13-d）：空闲超时 / 审计保留 / 报警历史保留 从常量变配置
        //
        //  这一节钉的是路线图风险登记里那条：「空闲超时 10 分钟写死不可配，现场有
        //  『半小时才走开』的场景，客户会提」。根因不是数字选错了，而是三个数字
        //  都写成了编译期常量——每换一个现场就得改源码重编软件。
        //
        //  断言分四层，对应"做成配置项"这句话真正要成立的四个环节：
        //    ① 默认值：三个数字只有一个来源（AppConfigModel 的常量），升级不改现场行为；
        //    ② 派生量：分钟 → TimeSpan、"0 表示永不"两条规则只写在 IdleTimeout 一处；
        //    ③ 读取口：三个使用方都"每次现读"，改完立刻生效不靠谁记得同步；
        //    ④ 弹窗：非法输入整体拒绝并说明原因、确定才落盘、再次打开现读。
        //
        //  弹窗那一半用真的 AppSettingsService（路径写死在程序目录，没法注入临时目录），
        //  所以 finally 里把三个原值写回——不给下一次运行留一份脏配置。
        // ==================================================================
        private static void SystemParametersChecks()
        {
            Section("[SS] 系统参数设置（S13-d：空闲超时 / 审计保留 / 报警历史保留 全部可配）");

            // ---------------- ① 默认值：三个数字只有一个来源 ----------------

            Check("默认空闲超时 10 分钟 / 审计留 90 天 / 报警历史留 365 天（常量集中在 AppConfigModel，不再散落在使用方）",
                AppConfigModel.DefaultIdleTimeoutMinutes == 10
                && AppConfigModel.DefaultAuditRetentionDays == 90
                && AppConfigModel.DefaultAlarmHistoryRetentionDays == 365,
                $"{AppConfigModel.DefaultIdleTimeoutMinutes} / {AppConfigModel.DefaultAuditRetentionDays} / {AppConfigModel.DefaultAlarmHistoryRetentionDays}");

            Check("两个落盘端的构造兜底值与配置模型同源（两处各写一个 90，早晚改一处忘一处）",
                ScadaAuditWriter.DefaultRetentionDays == AppConfigModel.DefaultAuditRetentionDays
                && ScadaAlarmHistoryWriter.DefaultRetentionDays == AppConfigModel.DefaultAlarmHistoryRetentionDays,
                $"审计 {ScadaAuditWriter.DefaultRetentionDays} / 报警历史 {ScadaAlarmHistoryWriter.DefaultRetentionDays}");

            Check("ScadaAccessPolicy 的兜底超时与配置模型同源（保证「没有配置时」与「配置默认」是同一个数）",
                ScadaAccessPolicy.DefaultIdleTimeout == TimeSpan.FromMinutes(AppConfigModel.DefaultIdleTimeoutMinutes),
                ScadaAccessPolicy.DefaultIdleTimeout.ToString());

            // ---------------- ② 派生量：分钟 → TimeSpan，"0 表示永不"只写一处 ----------------

            var fresh = new AppConfigModel();
            Check("新建配置的 IdleTimeout 就是 10 分钟（换算口不额外引入偏差）",
                fresh.IdleTimeout == TimeSpan.FromMinutes(10), fresh.IdleTimeout.ToString());

            fresh.IdleTimeoutMinutes = 0;
            Check("分钟数填 0 → TimeSpan.Zero（「0 表示不自动登出」这条规则只写在 IdleTimeout 一处）",
                fresh.IdleTimeout == TimeSpan.Zero, fresh.IdleTimeout.ToString());

            fresh.IdleTimeoutMinutes = -5;
            Check("分钟数填负数 → TimeSpan.Zero，而不是一个负的 TimeSpan（负值会让 OnIdleTick 里的 <= 判定读起来像巧合）",
                fresh.IdleTimeout == TimeSpan.Zero, fresh.IdleTimeout.ToString());

            fresh.IdleTimeoutMinutes = 45;
            Check("分钟数填 45 → 45 分钟",
                fresh.IdleTimeout == TimeSpan.FromMinutes(45), fresh.IdleTimeout.ToString());

            var json = JsonConvert.SerializeObject(fresh);
            Check("IdleTimeout 是派生量、不进 JSON（落盘的只有分钟数那个原始值，写进去只会多一个可能与真值矛盾的数字）",
                !json.Contains("\"IdleTimeout\":") && json.Contains("\"IdleTimeoutMinutes\":"),
                json);

            // ---------------- ③ 读取口：三个使用方都"每次现读" ----------------

            // 审计落盘端：保留期每次清理时现读，所以"改完立刻生效"是结构上保证的
            int auditDays = 30;
            var auditWriter = new ScadaAuditWriter(
                Path.Combine(Path.GetTempPath(), "ScadaParams_" + Guid.NewGuid().ToString("N")),
                retentionProvider: () => auditDays);
            Check("审计落盘端注入读取口后 RetentionDays 现读（不是构造那一刻定死）",
                auditWriter.RetentionDays == 30, auditWriter.RetentionDays.ToString());

            auditDays = 7;
            Check("读取口的值一改，同一个实例的 RetentionDays 立刻跟着变（不靠谁记得把新值推过来）",
                auditWriter.RetentionDays == 7, auditWriter.RetentionDays.ToString());

            // 报警历史落盘端：同一套口径。报警攒得比审计快得多，现场"这台机半年没人翻"与
            // "故障复盘要翻一年"两种诉求都得容得下。
            int alarmDays = 365;
            var alarmWriter = new ScadaAlarmHistoryWriter(
                Path.Combine(Path.GetTempPath(), "ScadaParams_" + Guid.NewGuid().ToString("N")),
                retentionProvider: () => alarmDays);
            Check("报警历史落盘端同一口径：注入读取口后 RetentionDays 现读",
                alarmWriter.RetentionDays == 365, alarmWriter.RetentionDays.ToString());

            alarmDays = 90;
            Check("报警历史读取口的值一改也立刻跟着变（与审计端逐条对齐，不留「半新半旧」的口子）",
                alarmWriter.RetentionDays == 90, alarmWriter.RetentionDays.ToString());

            Check("没注入读取口时两个落盘端都回落到各自的构造兜底值（断言 / 单机调试直接 new 的路径）",
                new ScadaAuditWriter(Path.Combine(Path.GetTempPath(), "ScadaParams_" + Guid.NewGuid().ToString("N"))).RetentionDays
                    == ScadaAuditWriter.DefaultRetentionDays
                && new ScadaAlarmHistoryWriter(Path.Combine(Path.GetTempPath(), "ScadaParams_" + Guid.NewGuid().ToString("N"))).RetentionDays
                    == ScadaAlarmHistoryWriter.DefaultRetentionDays,
                $"{ScadaAuditWriter.DefaultRetentionDays} / {ScadaAlarmHistoryWriter.DefaultRetentionDays}");

            // 空闲超时那一份（ScadaAccessPolicy 要 DispatcherTimer，必须挂 STA）
            SystemParametersAccessPolicyChecks();

            // ---------------- ④ 弹窗：非法输入拒绝 / 确定才落盘 / 再次打开现读 ----------------

            var settings = new AppSettingsService();
            var originalIdle = settings.Current.IdleTimeoutMinutes;
            var originalAudit = settings.Current.AuditRetentionDays;
            var originalAlarm = settings.Current.AlarmHistoryRetentionDays;

            try
            {
                settings.Current.IdleTimeoutMinutes = 10;
                settings.Current.AuditRetentionDays = 90;
                settings.Current.AlarmHistoryRetentionDays = 365;
                settings.Save();

                var vm = new ScadaSystemParametersVM(settings);
                vm.OnDialogOpened(new DialogParameters());
                Check("打开弹窗显示的是当前生效的三个值（不是默认常量、也不是上次点过的）",
                    vm.IdleTimeoutText == "10" && vm.AuditRetentionText == "90" && vm.AlarmRetentionText == "365",
                    $"{vm.IdleTimeoutText} / {vm.AuditRetentionText} / {vm.AlarmRetentionText}");

                ButtonResult? closed = null;
                vm.RequestClose = MakeCloseListener(result => closed = result);

                // 直接把 int 绑到 TextBox.Text 的话，这里 WPF 会静默把绑定置无效、属性保持旧值，
                // 界面上什么都看不出来、点确定还写盘成功——保存的却是上一次的值。
                // 所以弹窗收字符串、在点确定时统一解析并给出明确原因。
                vm.IdleTimeoutText = "3O";   // 字母 O，不是零
                vm.ConfirmCommand.Execute();
                Check("填了非整数（3O，字母 O）：不关窗、内存配置不动，并说明是哪个字段、填的是什么",
                    closed == null && vm.HasError && settings.Current.IdleTimeoutMinutes == 10,
                    $"关闭={closed?.ToString() ?? "未关"} / 提示={vm.ErrorMessage}");

                vm.IdleTimeoutText = "  ";   // 只有空白
                vm.ConfirmCommand.Execute();
                Check("留空不当作 0（留空被猜成「永不清理 / 不自动登出」正好是最不该猜错的那个值）",
                    closed == null && vm.HasError, vm.ErrorMessage);

                vm.IdleTimeoutText = "45";
                vm.AuditRetentionText = "0";
                vm.AlarmRetentionText = "730";
                vm.ConfirmCommand.Execute();
                Check("确定：关窗返回 OK，三项一起写进内存配置（0 是合法值：审计永不清理）",
                    closed == ButtonResult.OK
                    && settings.Current.IdleTimeoutMinutes == 45
                    && settings.Current.AuditRetentionDays == 0
                    && settings.Current.AlarmHistoryRetentionDays == 730,
                    $"关闭={closed} / {settings.Current.IdleTimeoutMinutes} / {settings.Current.AuditRetentionDays} / {settings.Current.AlarmHistoryRetentionDays}");

                // 重新读一份服务（构造函数会从磁盘 Load）：这一步才证明真写了盘，而不只是改了内存对象
                var reread = new AppSettingsService();
                Check("确定：三个值真的落进 AppConfig.json，重新加载读回来还是新值",
                    reread.Current.IdleTimeoutMinutes == 45
                    && reread.Current.AuditRetentionDays == 0
                    && reread.Current.AlarmHistoryRetentionDays == 730,
                    $"{reread.Current.IdleTimeoutMinutes} / {reread.Current.AuditRetentionDays} / {reread.Current.AlarmHistoryRetentionDays}");

                // 再次打开：编辑态必须从配置现读，不能带上一轮的编辑残留
                var reopen = new ScadaSystemParametersVM(settings);
                reopen.OnDialogOpened(new DialogParameters());
                Check("再次打开：编辑态从配置现读，上一轮的编辑残留不会带进来",
                    reopen.IdleTimeoutText == "45"
                    && reopen.AuditRetentionText == "0"
                    && reopen.AlarmRetentionText == "730",
                    $"{reopen.IdleTimeoutText} / {reopen.AuditRetentionText} / {reopen.AlarmRetentionText}");

                // ---------------- ⑤ 改参数落审计（S13-f） ----------------
                // 这三个数字是"这台机器的安全策略"：把空闲登出改成 0 等于**关掉自动登出**，
                // 把审计保留改成 1 等于**自己给自己缩短追责窗口**。都是"改完现场看不出来、
                // 事后才想起来要问"的一类操作，正是审计存在的理由。

                string paramAuditDir = Path.Combine(Path.GetTempPath(), "ScadaParamAudit_" + Guid.NewGuid().ToString("N"));
                var paramAudit = new ScadaAuditWriter(paramAuditDir);

                // 读回全部审计行（跳过每份文件的表头）。按目录里所有 Audit-*.csv 汇总，
                // 这样断言不会因为"正好跑过午夜"而假红。
                string[] ParamAuditRows()
                {
                    if (!Directory.Exists(paramAuditDir)) return Array.Empty<string>();
                    return Directory.GetFiles(paramAuditDir, "Audit-*.csv")
                        .OrderBy(p => p, StringComparer.Ordinal)
                        .SelectMany(p => File.ReadAllLines(p).Skip(1))
                        .ToArray();
                }

                string paramActor = "王工";
                var audited = new ScadaSystemParametersVM(settings, paramAudit, () => paramActor);
                audited.OnDialogOpened(new DialogParameters());
                audited.RequestClose = MakeCloseListener(_ => { });

                audited.ConfirmCommand.Execute();
                var paramRows = ParamAuditRows();
                Check("改参数：三项都没动就确定也落一条，说明写明「未写盘」（事后问「他到底改过没有」，答案是「点过，但没改」）",
                    paramRows.Length == 1 && paramRows[0].Contains("系统参数") && paramRows[0].Contains("修改参数")
                    && paramRows[0].Contains("成功") && paramRows[0].Contains("未写盘"),
                    string.Join(" | ", paramRows));

                Check("署名来自注入的提供者（本类不认识会话，宿主把它接到「此刻登录着谁」上）",
                    paramRows.Length == 1 && paramRows[0].Contains("王工"), paramRows[0]);

                paramActor = "李工";
                audited.IdleTimeoutText = "0";
                audited.ConfirmCommand.Execute();
                paramRows = ParamAuditRows();
                Check("改参数成功落一条，说明写「旧值 → 新值」：把空闲登出改成 0 等于关掉自动登出，事后要答得出是谁关的",
                    paramRows.Length == 2 && paramRows[1].Contains("空闲自动登出：45 → 0 分钟")
                    && paramRows[1].Contains("成功") && paramRows[1].Contains("李工"),
                    paramRows[1]);

                audited.CancelCommand.Execute();
                Check("取消不落审计（改到一半放弃不该留痕，与「不写盘」同一口径）",
                    ParamAuditRows().Length == 2, string.Join(" | ", ParamAuditRows()));

                // 取不到「此刻登录着谁」时署名回落「未登录」——那本身是一条线索（有人没登录就改了参数）
                var anonymous = new ScadaSystemParametersVM(settings, paramAudit, () => null);
                anonymous.OnDialogOpened(new DialogParameters());
                anonymous.RequestClose = MakeCloseListener(_ => { });
                anonymous.ConfirmCommand.Execute();
                paramRows = ParamAuditRows();
                Check("取不到「此刻登录着谁」时署名回落「未登录」，而不是留一格空白（有人没登录就改了参数，这本身是线索）",
                    paramRows.Length == 3 && paramRows[2].Contains("未登录"), paramRows[2]);
            }
            finally
            {
                settings.Current.IdleTimeoutMinutes = originalIdle;
                settings.Current.AuditRetentionDays = originalAudit;
                settings.Current.AlarmHistoryRetentionDays = originalAlarm;
                settings.Save();
            }
        }

        /// <summary>
        /// 「方案清单」弹窗改配置落审计（S13-f 的第三处落点，也是最后认领的一处）。
        ///
        /// 为什么单开一节、不并进 <see cref="SystemParametersChecks"/>：
        /// 它改的是另一份东西（AppConfig.json 里的方案清单与默认启动方案），与那三个数字不是一回事，
        /// 混在一节里失败时的定位成本更高。
        ///
        /// 为什么这一项值得留凭证：清单里的"默认启动方案"在无人值守的机台上直接决定
        /// "上电后跑的是哪一套程序"——改错了现场要到第二天开机才发现，那时更要知道是谁改的。
        ///
        /// 用真的 <see cref="AppSettingsService"/>（路径写死在程序目录，没法注入临时目录），
        /// 所以 finally 里把清单与默认启动方案都写回——不给下一次运行留一份脏配置。
        /// </summary>
        private static void SolutionListAuditChecks()
        {
            Section("[SL] 方案清单改配置落审计（S13-f：改的是「开机自动打开哪份方案」）");

            var settings = new AppSettingsService();
            var originalStartup = settings.Current.StartupSolutionPath;
            var originalSolutions = settings.Current.Solutions;

            string auditDir = Path.Combine(Path.GetTempPath(), "ScadaListAudit_" + Guid.NewGuid().ToString("N"));
            var auditWriter = new ScadaAuditWriter(auditDir);

            // 读回全部审计行（跳过每份文件的表头）。按目录里所有 Audit-*.csv 汇总，
            // 这样断言不会因为"正好跑过午夜"而假红。
            string[] AuditRows()
            {
                if (!Directory.Exists(auditDir)) return Array.Empty<string>();
                return Directory.GetFiles(auditDir, "Audit-*.csv")
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .SelectMany(p => File.ReadAllLines(p).Skip(1))
                    .ToArray();
            }

            try
            {
                // 清单与默认启动方案都清空：断言不依赖这台机器上恰好配过什么。
                settings.Current.StartupSolutionPath = "";
                settings.Current.Solutions = new List<AppSolutionEntry>();
                settings.Save();

                string actor = "王工";
                var vm = new SolutionListVM(
                    new SolutionService(), new WorkspaceContext(), settings, auditWriter, () => actor);
                vm.OnDialogOpened(new DialogParameters());
                vm.RequestClose = MakeCloseListener(_ => { });

                vm.ConfirmCommand.Execute();
                var rows = AuditRows();
                Check("方案清单：一处没动就确定也落一条，说明写明「未写盘」（事后问「他到底改过没有」，答案是「点过，但没改」）",
                    rows.Length == 1 && rows[0].Contains("方案清单") && rows[0].Contains("修改方案清单")
                    && rows[0].Contains("成功") && rows[0].Contains("未写盘"),
                    string.Join(" | ", rows));

                Check("署名来自注入的提供者（本类不认识会话，宿主把它接到「此刻登录着谁」上）",
                    rows.Length == 1 && rows[0].Contains("王工"), rows[0]);

                actor = "李工";
                vm.StartupSolutionPath = @"C:\scada\line1.vms";
                vm.ConfirmCommand.Execute();
                rows = AuditRows();
                Check("改默认启动方案落一条，说明写「旧值 → 新值」：它决定上电后跑的是哪一套程序，改错了要到第二天开机才发现",
                    rows.Length == 2 && rows[1].Contains(@"默认启动方案：（未设置） → C:\scada\line1.vms")
                    && rows[1].Contains("成功") && rows[1].Contains("李工"),
                    rows[1]);

                // 清单加一项：项数变了就报项数（最要紧的那个事实，比"3 项 → 3 项"有用）
                var reopened = new SolutionListVM(
                    new SolutionService(), new WorkspaceContext(), settings, auditWriter, () => actor);
                reopened.OnDialogOpened(new DialogParameters());
                reopened.RequestClose = MakeCloseListener(_ => { });
                reopened.Solutions.Add(new AppSolutionEntry
                {
                    Name = "line2",
                    Comment = "",
                    Path = @"C:\scada\line2.vms"
                });
                reopened.ConfirmCommand.Execute();
                rows = AuditRows();
                Check("改方案清单落一条，说明写「几项 → 几项」：清单里有哪些方案决定现场能切到哪几套程序",
                    rows.Length == 3 && rows[2].Contains("方案清单：0 项 → 1 项") && rows[2].Contains("成功"),
                    rows[2]);

                reopened.CancelCommand.Execute();
                Check("取消不落审计（改到一半放弃不该留痕，与「不写盘」同一口径）",
                    AuditRows().Length == 3, string.Join(" | ", AuditRows()));

                // 不接审计（宿主没注入 / 单机调试直接 new）时照常写盘：
                // 审计不是"能不能改配置"的前提——它写不成，配置照样得改得成。
                var plain = new SolutionListVM(
                    new SolutionService(), new WorkspaceContext(), settings);
                plain.OnDialogOpened(new DialogParameters());
                plain.RequestClose = MakeCloseListener(_ => { });
                plain.StartupSolutionPath = @"C:\scada\line3.vms";
                plain.ConfirmCommand.Execute();
                Check("没接审计时照常写盘（审计写不成不该让「清单改没改成」这件事变样）",
                    new AppSettingsService().Current.StartupSolutionPath == @"C:\scada\line3.vms",
                    new AppSettingsService().Current.StartupSolutionPath);
            }
            finally
            {
                settings.Current.StartupSolutionPath = originalStartup;
                settings.Current.Solutions = originalSolutions;
                settings.Save();
            }
        }

        /// <summary>
        /// <see cref="ScadaAccessPolicy"/> 那一份断言必须挂 STA：它的构造函数会建
        /// <see cref="DispatcherTimer"/>（理由见该类型自身的注释）。样板与 [AB] 的窗口断言一致——
        /// 单开一条 STA 线程，不把 Main 标成 STAThread 去改 S0/S1 那批断言的既有运行环境。
        /// </summary>
        private static void SystemParametersAccessPolicyChecks()
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { SystemParametersAccessPolicyChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("空闲超时读取口断言全程未抛异常", false, failure.ToString());
        }

        private static void SystemParametersAccessPolicyChecksCore()
        {
            // 宿主就是这么接的：把配置模型上的换算口直接交给它（App.xaml.cs 里那行工厂）
            int minutes = 10;
            var policy = new ScadaAccessPolicy(
                () => minutes <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(minutes));

            Check("空闲超时注入了读取口后现读（宿主把它接到 AppConfigModel.IdleTimeout 上）",
                policy.IdleTimeout == TimeSpan.FromMinutes(10), policy.IdleTimeout.ToString());

            minutes = 45;
            Check("读取口的值一改，同一个实例的 IdleTimeout 立刻跟着变（改完立刻生效，不用重启、不用等下次登录）",
                policy.IdleTimeout == TimeSpan.FromMinutes(45), policy.IdleTimeout.ToString());

            minutes = 0;
            Check("读取口给 0 → IdleTimeout 为 TimeSpan.Zero（= 不自动登出，OnIdleTick 靠这条跳过踢人）",
                policy.IdleTimeout == TimeSpan.Zero, policy.IdleTimeout.ToString());

            Check("没注入读取口时回落到 DefaultIdleTimeout（断言 / 单机调试直接 new 的路径）",
                new ScadaAccessPolicy().IdleTimeout == ScadaAccessPolicy.DefaultIdleTimeout,
                new ScadaAccessPolicy().IdleTimeout.ToString());
        }

        // ==================================================================
        //  [PI] 打包与交付（S13-f）
        //   原子写 / 老方案文件迁移 / 崩溃现场留档 / 未保存草稿转存
        // ==================================================================
        private static void PackagingChecks()
        {
            Section("[PI] 打包与交付（S13-f）");

            string root = Path.Combine(Path.GetTempPath(), "ScadaPack_" + Guid.NewGuid().ToString("N"));
            string crashDir = Path.Combine(root, "crash_reports");
            string draftDir = Path.Combine(root, "Autosave");
            Directory.CreateDirectory(root);

            try
            {
                // ---------- 1. 落盘口径唯一：Serialize 与 SaveAsync 写出的文本逐字一致 ----------
                //
                // 草稿转存的判据全押在"内存序列化 == 磁盘文本"这一条上：两边只要有一处口径不同
                // （缩进、字段顺序、某字段写不写），判据就退化成"永远说有改动"——
                // 每次退出都多存一份草稿，用户还以为是软件在乱写文件。所以先钉住这个前提。
                var service = new SolutionService();

                var solution = new SolutionModel();
                solution.Flows.Clear();
                var page = solution.Scada.AddPage("打包画面");

                // 与编辑器 AddElement 同一口径：新图元一律先归到首个图层。
                // 裸 Add 一个 LayerId=Empty 的图元是"脏数据"，读回时 EnsureIdentity 会把它归层，
                // 于是磁盘与内存的文本必然不同——那是迁移在按预期工作，不是往返不稳。
                var rect = ElementRegistry.CreateElement("Hmi.Rectangle", 10, 10);
                rect.Name = "矩形1";
                rect.LayerId = page.DefaultLayer!.LayerId;
                page.Elements.Add(rect);

                string vmsPath = Path.Combine(root, "pack.vms");
                var save1 = service.SaveAsync(solution, vmsPath).GetAwaiter().GetResult();
                Check("方案保存成功", save1.Success, save1.Message);

                Check("Serialize 与落盘文本逐字一致（草稿判据的唯一依据）",
                    string.Equals(SolutionService.Serialize(solution), File.ReadAllText(vmsPath), StringComparison.Ordinal),
                    $"内存 {SolutionService.Serialize(solution).Length} 字符 / 磁盘 {File.ReadAllText(vmsPath).Length} 字符");

                Check("原子写不留 .tmp 残渣", !File.Exists(vmsPath + ".tmp"), vmsPath + ".tmp");

                // 覆盖写走 File.Replace 分支：必须整体换掉，不能新旧混写
                page.Width = 1920;
                var save2 = service.SaveAsync(solution, vmsPath).GetAwaiter().GetResult();
                Check("覆盖写成功（目标已存在 → 走 File.Replace 分支）", save2.Success, save2.Message);
                Check("覆盖写后磁盘就是第二次的内容（没有新旧混写）",
                    string.Equals(SolutionService.Serialize(solution), File.ReadAllText(vmsPath), StringComparison.Ordinal)
                    && File.ReadAllText(vmsPath).Contains("1920"), "");
                Check("覆盖写同样不留 .tmp 残渣", !File.Exists(vmsPath + ".tmp"), "");

                // SolutionFilePath 不落盘：它是"保存成功之后"才拿得到的运行期提示，
                // 若参与序列化，磁盘那份永远比内存少一个字段 → 没改过的方案也会被误判成有未保存改动
                solution.SolutionFilePath = vmsPath;
                Check("SolutionFilePath 不参与落盘（运行期 UI 提示，不属于方案内容）",
                    !SolutionService.Serialize(solution).Contains("SolutionFilePath"), "");

                // ---------- 2. 老方案文件迁移：身份字段缺失的文件读入后补发，且幂等 ----------
                //
                // 造法：先按当前版本序列化一份，再把身份字段从 JSON 里剥掉——这样"老文件"
                // 与真实历史文件的差别只剩身份字段本身，不掺其它假设，测的就是迁移这一件事。
                var legacyDoc = new ScadaDocument();
                var legacyPage = legacyDoc.AddPage("老画面");
                legacyPage.Elements.Add(ElementRegistry.CreateElement("Hmi.Rectangle", 5, 5));

                var legacySolution = new SolutionModel { Scada = legacyDoc };
                legacySolution.Flows.Clear();

                var legacyNode = JObject.Parse(SolutionService.Serialize(legacySolution));
                int stripped = 0;
                foreach (var obj in legacyNode.DescendantsAndSelf().OfType<JObject>())
                    foreach (string idName in new[] { "PageId", "ElementId", "LayerId" })
                        if (obj.Remove(idName)) stripped++;

                Check("老方案文件已剥掉全部身份字段（模拟 Id 引入之前写出的 .vms）",
                    stripped >= 3, $"剥掉 {stripped} 个字段");

                string legacyPath = Path.Combine(root, "legacy.vms");
                File.WriteAllText(legacyPath, legacyNode.ToString());

                var legacyLoad = service.LoadAsync(legacyPath).GetAwaiter().GetResult();
                Check("缺身份字段的老文件照样能打开（迁移不挡老文件）", legacyLoad.Success, legacyLoad.Message);

                var migrated = legacyLoad.Data?.Scada?.FindPageByName("老画面");
                Check("迁移：画面身份被补发", migrated != null && migrated.PageId != Guid.Empty,
                    migrated?.PageId.ToString() ?? "(画面未找到)");
                Check("迁移：图元身份被补发",
                    migrated != null && migrated.Elements.Count == 1 && migrated.Elements[0].ElementId != Guid.Empty, "");
                Check("迁移：图元归属到默认图层（未分层的老图元不许悬空）",
                    migrated != null && migrated.Layers.Count > 0
                    && migrated.Elements[0].LayerId == migrated.Layers[0].LayerId, "");

                var keptPageId = migrated!.PageId;
                var keptElementId = migrated.Elements[0].ElementId;
                var keptLayerId = migrated.Layers[0].LayerId;
                Check("迁移幂等：已有身份再补一次原样保留（重复加载不换 Id，绑定不失联）",
                    migrated.EnsureIdentity() == 0
                    && migrated.PageId == keptPageId
                    && migrated.Elements[0].ElementId == keptElementId
                    && migrated.Layers[0].LayerId == keptLayerId, "");

                // 打开一份刚存好的方案、什么都不改就退出 → 必须判成"没改动"。
                // 这条能成立的前提是"读回来再序列化"与磁盘逐字一致，故单钉一条，
                // 失败时一眼看出是往返文本不稳，而不是草稿判据写错了。
                var opened = service.LoadAsync(vmsPath).GetAwaiter().GetResult().Data!;
                string diskText = File.ReadAllText(vmsPath);
                string roundText = SolutionService.Serialize(opened);
                Check("往返文本稳定：读回再序列化与磁盘逐字一致（否则打开就退也会误判成有改动）",
                    string.Equals(diskText, roundText, StringComparison.Ordinal), FirstDiff(diskText, roundText));

                // ---------- 3. 崩溃现场留档（不依赖日志服务的最短路径） ----------
                Check("目录还不存在时 ListReports 回空集合（不是抛异常）",
                    CrashReportWriter.ListReports(crashDir).Count == 0, "");

                var report1 = CrashReportWriter.Write(new InvalidOperationException("模拟崩溃一"), "严重异常", "方案: 打包测试", crashDir);
                Check("Write 返回路径且文件真的落地", report1 != null && File.Exists(report1), report1 ?? "(null)");
                Check("报告里有现场摘要与异常全文（排障要的两样都在）",
                    report1 != null && File.ReadAllText(report1).Contains("方案: 打包测试")
                    && File.ReadAllText(report1).Contains("模拟崩溃一"), "");

                // 文件名是毫秒时间戳，两次写要拉开一点，否则同一毫秒会互相覆盖成一份
                System.Threading.Thread.Sleep(30);
                var report2 = CrashReportWriter.Write(null, "AppDomain", null, crashDir);
                Check("没有异常对象也照常留档（非异常路径退出同样留现场）",
                    report2 != null && File.ReadAllText(report2).Contains("(无异常对象"), report2 ?? "(null)");

                var listed = CrashReportWriter.ListReports(crashDir);
                Check("ListReports 新 → 旧（按文件名里的定宽时间戳，不看文件系统时间戳）",
                    listed.Count == 2 && listed[0] == report2 && listed[1] == report1,
                    string.Join(" | ", listed.Select(Path.GetFileName)));

                Check("TakeLatestUnacknowledged 取的是最新那份",
                    CrashReportWriter.TakeLatestUnacknowledged(crashDir) == report2, "");
                Check("取过即挂 .acked（同一份不重复提示）",
                    File.Exists(report2 + CrashReportWriter.AcknowledgedExtension) && !File.Exists(report2), "");
                Check("已提示的那份从待提示列表消失",
                    CrashReportWriter.ListReports(crashDir).SequenceEqual(new[] { report1! }), "");
                Check("再取一次拿到剩下那份", CrashReportWriter.TakeLatestUnacknowledged(crashDir) == report1, "");
                Check("全提示过之后回 null（下次启动不再弹）",
                    CrashReportWriter.TakeLatestUnacknowledged(crashDir) == null, "");

                // 保留策略：直接造定宽时间戳文件，不靠 Write 的毫秒时间戳（避免同一毫秒互相覆盖）
                string pruneDir = Path.Combine(root, "crash_prune");
                Directory.CreateDirectory(pruneDir);
                for (int i = 0; i < 25; i++)
                    File.WriteAllText(Path.Combine(pruneDir, $"crash-20200101-000000-{i:000}.txt"), "x");

                int removedReports = CrashReportWriter.PruneOldReports(CrashReportWriter.DefaultKeep, pruneDir);
                var keptReports = Directory.GetFiles(pruneDir, "crash-*")
                    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
                Check("保留策略：25 份裁到 20 份",
                    keptReports.Count == 20 && removedReports == 5, $"留 {keptReports.Count} / 删 {removedReports}");
                Check("裁掉的是最早的 5 份、留的是最近的",
                    keptReports.Count == 20
                    && Path.GetFileName(keptReports[0]) == "crash-20200101-000000-005.txt", "");

                // ---------- 4. 未保存草稿转存与现场恢复 ----------
                var drafts = new SolutionDraftService(draftDir);

                Check("没有打开的方案 → Skipped（退出链上什么都不做）",
                    drafts.Capture(null).Status == DraftCaptureStatus.Skipped, "");

                var neverSaved = new SolutionModel();
                neverSaved.Flows.Clear();
                Check("方案从未落过盘 → Skipped（没有对照物，存下来也无从比对）",
                    drafts.Capture(neverSaved).Status == DraftCaptureStatus.Skipped, "");

                opened.SolutionFilePath = vmsPath;
                var noChange = drafts.Capture(opened);
                Check("内存与磁盘逐字一致 → NoChange，且一份文件都不写（没改动就不该产生草稿）",
                    noChange.Status == DraftCaptureStatus.NoChange && drafts.ListDrafts().Count == 0,
                    $"{noChange.Status} / 草稿 {drafts.ListDrafts().Count} 份");

                opened.Scada.FindPageByName("打包画面")!.Width = 1024;
                var captured = drafts.Capture(opened);
                Check("改过一处 → Captured，草稿真的落地",
                    captured.Status == DraftCaptureStatus.Captured && captured.Path != null && File.Exists(captured.Path),
                    captured.Path ?? captured.Detail ?? "");
                Check("草稿内容就是内存里那份（改后的 1024 在里面）",
                    captured.Path != null && File.ReadAllText(captured.Path).Contains("1024"), "");

                var archived = drafts.TakePendingDrafts();
                string archiveDir = Path.Combine(draftDir, SolutionDraftService.RecoveredDirectoryName);
                Check("提示过的草稿被归档到 recovered\\（不是删除：那是用户唯一一份未保存内容）",
                    archived.Count == 1
                    && archived[0].StartsWith(archiveDir, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(archived[0]),
                    archived.FirstOrDefault() ?? "");
                Check("归档后待提示区清空（下次启动不再重复弹）", drafts.ListDrafts().Count == 0, "");

                for (int i = 0; i < 12; i++)
                    File.WriteAllText(Path.Combine(draftDir, $"草稿-20200101-0000{i:00}.vms"), "x");

                int removedDrafts = drafts.PruneDrafts(SolutionDraftService.DefaultKeep);
                Check("草稿保留策略：12 份裁到 10 份",
                    drafts.ListDrafts().Count == 10 && removedDrafts == 2,
                    $"留 {drafts.ListDrafts().Count} / 删 {removedDrafts}");
                Check("归档区不受保留策略影响（用户成果归用户自己管）",
                    Directory.GetFiles(archiveDir, "*.vms").Length == 1, "");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { /* 临时目录删不掉不影响断言结论 */ }
            }
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
