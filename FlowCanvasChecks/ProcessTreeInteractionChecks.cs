using System;
using System.Collections.Generic;
using System.IO;
using Core.Events;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程栏"分支卡片/非算子对象"的点击行为守门（2026-10-09 真机事故）。
    ///
    /// 事故现象：双击并行分组的分支卡片（分支 1）弹出窗口——弹出的其实是
    /// "上一个选中算子"的参数窗，或一个**空白的**「条件逻辑配置中心」。
    ///
    /// 根因链（三处口径不一致）：
    ///  ① 树的双击命令按"当前选中项"取参，而双击链是"TreeViewItem 选中 → 写回 VM → 命令执行"；
    ///  ② VM 的 SelectStep 明确拒绝 StepCollection（只认 StepModel）——写回被静默吞掉，
  ///     于是命令拿到的是**旧选中算子**（错靶）；
    ///  ③ StepParameterDialog 对"非 ActionStep"一律兜底弹 ConditionEditor，
    ///     而编辑器只认 ConditionStep/ForStep，拿不到 Node 直接早退 → 空窗。
    ///
    /// 修法与守门口径：
    ///  · 视图侧拦左键（ProcessView.OnTreePreviewMouseLeftButtonDown）——分支卡片不可选中、不派发双击；
    ///  · VM 侧收到 StepCollection 清空选中（不接受为当前步骤），命令链上再无错靶；
    ///  · 对话框分派显式化：ConditionStep/ForStep 进 ConditionEditor，ParallelStep 进它自己的
    ///    ParallelGroupConfig（执行模式/失败聚合/汇合超时/分支名单），其余返回 false 不弹窗。
    /// 本文件断言 ③ 与 ② 的可观测面（①由视图代码承担，出图场景 F 里附带一次真实鼠标事件回放）。
    ///
    /// 2026-10-09 追加（分支结构编辑从参数面板下沉到流程栏右键菜单）：R25 的红线一条不少
    /// （分支卡片不可选中、左键仍拦、双击仍不派发、不进"按当前选中项执行"的命令链），
    /// 新增的只有"右键分支胶囊出自己的两项菜单"，命令靶是**命中的那条分支**而不是当前选中项——
    /// 这两条命令（Add/Remove/RenameParallelBranchCommand）的行为就是下面的 V8。
    /// </summary>
    internal static class ProcessTreeInteractionChecks
    {
        public static void Run()
        {
            Section("[V1-V8] 流程栏分支点击：对话框分派、选中口径与分支结构命令");

            var dialogs = new RecordingDialogService();

            // ---- V1：并行分组容器双击 → EasyDialog 属性网格弹窗（FlatPropertyGrid 标准属性面板） ----
            // 2026-10-09 用户裁决"没必要新建一个视图"：专属面板 ParallelGroupConfigView 已删除，
            // 参数面板改为 EasyDialog.ShowPropertyGridSync（静态弹窗，不经 IDialogService）。
            // headless 宿主（Application.Current == null）里 EasyDialog 的保护分支返回 false（= 用户取消，
            // 见 InternalExecuteAsync 首行）——所以判据不是"弹窗内容"，而是分派**不炸且返回 true**：
            // 返回 true = 分派认了并行分组这个靶（有参数面板），弹窗本体由渲染探针场景 G 出图验收。
            // RecordingDialogService **不再记录**任何弹窗（EasyDialog 静态弹窗与 Prism 弹窗服务是两条通道）。
            var h1 = new Harness();
            var par = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "并行分组_0");
            h1.Add(par);
            var opened = StepParameterDialog.Open(par, dialogs, h1.Workspace);
            Check("V1：并行分组打开模块参数 → 属性网格分派（返回 true；headless 下 EasyDialog 走取消保护分支，不炸）",
                opened && dialogs.ShownDialogs.Count == 0,
                $"opened={opened} 记录的弹窗={dialogs.ShownDialogs.Count}（期望 0：EasyDialog 静态弹窗不经 Prism）");

            // ---- V2：分支卡片（StepCollection）双击 → 不弹窗 ----
            dialogs.ShownDialogs.Clear();
            var branch = new StepCollection { BranchType = BranchType.Default, StepName = "分支 1" };
            opened = StepParameterDialog.Open(branch, dialogs);
            Check("V2：分支卡片打开模块参数 → 不弹窗（返回 false）",
                !opened && dialogs.ShownDialogs.Count == 0,
                $"opened={opened} 弹窗={string.Join(",", dialogs.ShownDialogs)}");

            // ---- V3：If 容器（ConditionStep）→ 条件编辑器（原路径不许回归）----
            var ifStep = new ConditionStep("\uE700", "结果判断", StubPluginProvider.IfPlugin, "质量分档");
            opened = StepParameterDialog.Open(ifStep, dialogs);
            Check("V3：If 容器 → ConditionEditor",
                opened && dialogs.ShownDialogs.Count == 1 && dialogs.ShownDialogs[0] == "ConditionEditor",
                $"opened={opened} 弹窗={string.Join(",", dialogs.ShownDialogs)}");

            // ---- V4：For 容器 → 条件编辑器（For 模式并同一弹窗，P1-⑥）----
            dialogs.ShownDialogs.Clear();
            var forStep = new ForStep("\uE700", "循环", StubPluginProvider.ForPlugin, "重试采集");
            opened = StepParameterDialog.Open(forStep, dialogs);
            Check("V4：For 容器 → ConditionEditor",
                opened && dialogs.ShownDialogs.Count == 1 && dialogs.ShownDialogs[0] == "ConditionEditor",
                $"opened={opened} 弹窗={string.Join(",", dialogs.ShownDialogs)}");

            // ---- V5：VM 收到分支卡片 → 不接受为当前步骤并清空选中（错靶防护）----
            {
                var h = new Harness();
                var vm = new ProcessViewModel(h.Workspace, dialogs);
                try
                {
                    var step = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "算子_0");
                    h.Add(step);
                    vm.SelectStep = step;
                    bool stepSelected = ReferenceEquals(vm.SelectStep, step) && ReferenceEquals(h.Workspace.CurrentStep, step);

                    vm.SelectStep = branch; // 分支卡片：既不是当前步骤，也不许把旧选中留着当错靶
                    Check("V5：选中分支卡片 → SelectStep 与 Workspace.CurrentStep 都清空（不留旧算子当错靶）",
                        stepSelected && vm.SelectStep == null && h.Workspace.CurrentStep == null,
                        $"先选中算子={stepSelected}；送分支后 SelectStep={(vm.SelectStep == null ? "null" : vm.SelectStep.GetType().Name)} CurrentStep={(h.Workspace.CurrentStep == null ? "null" : h.Workspace.CurrentStep.StepName)}");
                }
                finally
                {
                    vm.Deactivate();
                }
            }

            // ---- V6：旧专属面板退役的字符串/文件契约（改一处忘另一处只在真机双击时才炸）----
            // 2026-10-09 面板收编为 EasyDialog 属性网格后：分派处不得再出现 "ParallelGroupConfig"
            // 字面量、App.xaml.cs 不得再注册该对话框、磁盘上三个旧文件必须已删除。
            {
                var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
                var appPath = Path.Combine(repoRoot, @"VisionMaster\App.xaml.cs");
                var dispatchPath = Path.Combine(repoRoot, @"VisionMaster\Services\StepParameterDialog.cs");
                var appXamlCs = File.Exists(appPath) ? File.ReadAllText(appPath) : "";
                var dispatch = File.Exists(dispatchPath) ? File.ReadAllText(dispatchPath) : "";

                // "RegisterDialog<...>(\"ParallelGroupConfig\")" 的注册行已删（注释里提及不算注册）
                var registered = appXamlCs.Contains("RegisterDialog<") && appXamlCs.Contains("\"ParallelGroupConfig\"");
                var dispatched = dispatch.Contains("ShowDialog(\"ParallelGroupConfig\"");
                var oldViewXaml = File.Exists(Path.Combine(repoRoot, @"VisionMaster\Views\DialogViews\ParallelGroupConfigView.xaml"));
                var oldViewCs = File.Exists(Path.Combine(repoRoot, @"VisionMaster\Views\DialogViews\ParallelGroupConfigView.xaml.cs"));
                var oldVm = File.Exists(Path.Combine(repoRoot, @"VisionMaster\ViewModels\DialogViewModels\ParallelGroupConfigViewModel.cs"));
                Check("V6：旧专属面板已退役（无注册、无分派字面量、三份旧文件已删）",
                    !registered && !dispatched && !oldViewXaml && !oldViewCs && !oldVm,
                    $"注册={registered} 分派字面量={dispatched} 旧文件存在={(oldViewXaml || oldViewCs || oldVm)}");
            }

            // ---- V7：容器递归口径统一（IContainerStep）——并行分支 / For 循环体内的算子也能「删除模块」----
            // 2026-10-09 前 ProcessViewModel 的 RemoveStepRecursively/CountStepsDeep 只认 ConditionStep：
            // 并行分支与 For 循环体里的算子"删除模块"静默无效（Core 的 ContainsStepRecursively 早就用 IContainerStep）。
            {
                var h = new Harness();
                var vm = new ProcessViewModel(h.Workspace, dialogs);
                try
                {
                    var parV7 = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "并行分组_0");
                    var inBranch = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "分支内算子");
                    parV7.Children[1].Steps.Add(inBranch);
                    var forV7 = new ForStep("\uE700", "循环", StubPluginProvider.ForPlugin, "循环_0");
                    var inLoop = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "循环内算子");
                    forV7.Children[0].Steps.Add(inLoop);
                    h.Add(parV7);
                    h.Add(forV7);

                    vm.SelectStep = inBranch;
                    vm.ModuleActionCommand.Execute(ModuleCommandAction.Delete);
                    bool branchRemoved = !parV7.Children[1].Steps.Contains(inBranch);

                    vm.SelectStep = inLoop;
                    vm.ModuleActionCommand.Execute(ModuleCommandAction.Delete);
                    bool loopRemoved = !forV7.Children[0].Steps.Contains(inLoop);

                    Check("V7：并行分支 / For 循环体内的算子可被「删除模块」删掉（IContainerStep 统一递归）",
                        branchRemoved && loopRemoved,
                        $"并行分支内已删={branchRemoved} 循环体内已删={loopRemoved}");
                }
                finally
                {
                    vm.Deactivate();
                }
            }

            // ---- V8：并行分支的三个结构命令（流程栏右键：组头=添加分支，分支胶囊=重命名/删除）----
            // 2026-10-09 面板收窄后，分支增删改的唯一落点就是这三个命令。断言只走命令级（不经 UI）：
            // 加分支、删最后一条、改名、删有算子的分支两态确认。
            // 三个"会弹真窗/弹泡"的地方全走可注入出口：ConfirmRenameParallelBranch（输入框）、
            // ConfirmDeleteParallelBranch（二次确认）、ShowFeedback（Notifier 在 headless 里会 NRE）。
            {
                var h = new Harness();
                var feedback = new List<string>();
                var vm = new ProcessViewModel(h.Workspace, dialogs)
                {
                    ShowFeedback = (message, success) => feedback.Add((success ? "成功:" : "警示:") + message),
                    ConfirmDeleteParallelBranch = (title, message) => false,
                    ConfirmRenameParallelBranch = (title, defaultValue) => (false, defaultValue),
                };
                try
                {
                    var group = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "并行分组_0");
                    h.Add(group);

                    // ---- 加分支：Children +1，名字不与既有分支重复 ----
                    vm.AddParallelBranchCommand.Execute(group);
                    bool added = group.Children.Count == 3
                        && group.Children.Select(c => c.StepName).Distinct().Count() == 3;
                    group.Children[0].StepName = "左工位"; // 用户改过名：新自动名不得撞上它，也不得撞上"分支 2"
                    vm.AddParallelBranchCommand.Execute(group);
                    bool unique = group.Children.Count == 4
                        && group.Children.Select(c => c.StepName).Distinct().Count() == 4
                        && group.Children.Any(c => c.StepName == "分支 1");
                    Check("V8：添加分支 → Children +1 且自动名不与既有分支重复",
                        added && unique,
                        $"加后={group.Children.Count} 名字={string.Join("/", group.Children.Select(c => c.StepName))}");

                    // ---- 上限：加到 8 条后第 9 次拒绝（Children 不动 + 提示）----
                    while (group.Children.Count < ParallelStep.RecommendedMaxBranches)
                        vm.AddParallelBranchCommand.Execute(group);
                    feedback.Clear();
                    vm.AddParallelBranchCommand.Execute(group);
                    Check("V8：加到推荐上限（8 条）后拒绝再添加（Children 不变 + 上限提示）",
                        group.Children.Count == ParallelStep.RecommendedMaxBranches
                        && feedback.Any(f => f.Contains("上限")),
                        $"{group.Children.Count} 条，反馈={string.Join("|", feedback)}");

                    // ---- 下限：只剩一条分支时删除被拒绝 ----
                    var single = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "单分支分组");
                    single.Children.RemoveAt(1);
                    h.Add(single);
                    feedback.Clear();
                    vm.RemoveParallelBranchCommand.Execute(single.Children[0]);
                    Check("V8：只剩一条分支 → 删除被拒绝（Children 不变 + 提示）",
                        single.Children.Count == 1 && feedback.Any(f => f.Contains("至少保留一条")),
                        $"{single.Children.Count} 条，反馈={string.Join("|", feedback)}");

                    // ---- 改名：写回（裁空白）+ 版本号递增；取消与空白名都不动 ----
                    var renameTarget = group.Children[1];
                    int v0 = h.Flow.Version;
                    vm.ConfirmRenameParallelBranch = (title, defaultValue) => (true, "  右工位  ");
                    vm.RenameParallelBranchCommand.Execute(renameTarget);
                    bool renamed = renameTarget.StepName == "右工位" && h.Flow.Version > v0;

                    vm.ConfirmRenameParallelBranch = (title, defaultValue) => (true, "   ");
                    vm.RenameParallelBranchCommand.Execute(renameTarget);
                    bool blankRejected = renameTarget.StepName == "右工位";

                    vm.ConfirmRenameParallelBranch = (title, defaultValue) => (false, "误改");
                    vm.RenameParallelBranchCommand.Execute(renameTarget);
                    Check("V8：分支改名 → StepName 写回 + Version 递增；空白名与取消都不动",
                        renamed && blankRejected && renameTarget.StepName == "右工位",
                        $"名='{renameTarget.StepName}' 版本 {v0} → {h.Flow.Version}");

                    // ---- 删有算子的分支：确认两态（false = 什么都不做；true = Children -1）----
                    var del = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "删除靶场");
                    h.Add(del);
                    var target = del.Children[0];
                    target.Steps.Add(new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "分支内算子"));

                    bool askedWithCount = false;
                    vm.ConfirmDeleteParallelBranch = (title, message) =>
                    {
                        askedWithCount = message.Contains("还有 1 个算子");
                        return false;
                    };
                    vm.RemoveParallelBranchCommand.Execute(target);
                    bool keptOnCancel = del.Children.Count == 2 && del.Children.Contains(target) && askedWithCount;
                    int afterCancel = del.Children.Count;

                    vm.ConfirmDeleteParallelBranch = (title, message) => true;
                    vm.RemoveParallelBranchCommand.Execute(target);
                    int afterConfirm = del.Children.Count;
                    bool removedOnConfirm = afterConfirm == 1 && !del.Children.Contains(target);

                    Check("V8：删有算子的分支 → 先确认（文案带算子数）；取消不动 / 确认后 Children 减 1",
                        keptOnCancel && removedOnConfirm,
                        $"取消后={afterCancel} 条，确认后={afterConfirm} 条，文案带算子数={askedWithCount}");

                    // ---- 运行锁：运行中三个结构命令一律被拦（文案与 ModuleActionCommand 同一份）----
                    // MainRunState 走 GlobalEventBus 广播（VM 在 Activate 时订阅）；发布是同步的，
                    // 断言完必须复位成 NotStarted —— 这是进程级静态通道，留着会污染后面的用例。
                    int lockedCount = group.Children.Count;
                    string lockedName = group.Children[0].StepName;
                    feedback.Clear();
                    GlobalEventBus.Publish(MainRunState.RunningOnce);
                    try
                    {
                        vm.AddParallelBranchCommand.Execute(group);
                        vm.RemoveParallelBranchCommand.Execute(group.Children[0]);
                        vm.RenameParallelBranchCommand.Execute(group.Children[0]);
                    }
                    finally
                    {
                        GlobalEventBus.Publish(MainRunState.NotStarted);
                    }

                    Check("V8：运行中三个结构命令都被运行锁拦下（分支数/分支名不动 + 各给一条提示）",
                        group.Children.Count == lockedCount
                        && group.Children[0].StepName == lockedName
                        && feedback.Count(f => f.Contains("流程运行中")) == 3,
                        $"分支数={group.Children.Count} 名='{group.Children[0].StepName}' 反馈={string.Join("|", feedback)}");

                    // ---- 类型容错：命令参数是绑定给的 object，CanExecute 可能拿到任何选中对象 ----
                    // 2026-10-10 真机事故：命令曾用 DelegateCommand<ParallelStep?> 强类型，
                    // 选中普通算子时 WPF 拿 ActionStep 调 CanExecute → InvalidCastException
                    // 炸在 SelectStep setter 的属性通知链里。现在命令收 object、处理器内 is 判型：
                    // 拿错类型 = 静默空跑（CanExecute/Execute 都不许抛）。
                    var alienStep = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "算子_0");
                    Exception? typeBoom = null;
                    try
                    {
                        bool canAdd = vm.AddParallelBranchCommand.CanExecute(alienStep);
                        bool canRemove = vm.RemoveParallelBranchCommand.CanExecute(alienStep);
                        bool canRename = vm.RenameParallelBranchCommand.CanExecute(alienStep);
                        bool canRenameGroup = vm.RenameParallelGroupCommand.CanExecute(alienStep);
                        // Execute 拿错类型也必须空跑（分支数不动）
                        vm.AddParallelBranchCommand.Execute(alienStep);
                        vm.RenameParallelGroupCommand.Execute(alienStep);
                        Check("V8：结构命令拿到非目标类型（ActionStep/null）→ 不抛异常、静默空跑",
                            canAdd && canRemove && canRename && canRenameGroup
                            && group.Children.Count == lockedCount,
                            $"CanExecute 全 true（空跑前提）={canAdd && canRemove && canRename && canRenameGroup}；分支数={group.Children.Count}（应={lockedCount}）");
                    }
                    catch (Exception ex)
                    {
                        typeBoom = ex;
                        Check("V8：结构命令拿到非目标类型（ActionStep/null）→ 不抛异常、静默空跑",
                            false, $"{typeBoom.GetType().Name}: {typeBoom.Message}");
                    }
                }
                finally
                {
                    vm.Deactivate();
                }
            }
        }

        /// <summary>
        /// IDialogService 的形状桩：只记录"弹了哪个窗口"，不真的开窗——
        /// 断言宿主里没有 Prism 容器，真服务一 ShowDialog 就会去容器里解析视图。
        /// </summary>
        private sealed class RecordingDialogService : IDialogService
        {
            public List<string> ShownDialogs { get; } = new();

            public void ShowDialog(string name, IDialogParameters parameters) => ShownDialogs.Add(name);

            public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback)
                => ShownDialogs.Add(name);

            public void ShowDialog(string name, IDialogParameters parameters, string windowName)
                => ShownDialogs.Add(name);

            public void ShowDialog(
                string name,
                IDialogParameters parameters,
                DialogCallback callback,
                string windowName
            ) => ShownDialogs.Add(name);

            public void Show(string name, IDialogParameters parameters) => ShownDialogs.Add(name);

            public void Show(string name, IDialogParameters parameters, DialogCallback callback)
                => ShownDialogs.Add(name);

            public void Show(string name, IDialogParameters parameters, string windowName)
                => ShownDialogs.Add(name);

            public void Show(
                string name,
                IDialogParameters parameters,
                DialogCallback callback,
                string windowName
            ) => ShownDialogs.Add(name);
        }
    }
}
