using System;
using System.Linq;
using System.Text.RegularExpressions;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [PM] 流程栏右键菜单的入口完整性（2026-10-24）。
    ///
    /// 起因：用户指出"编辑绑定"只有画布右键有，流程栏没有——同一个步骤在两个编辑面上
    /// 能力不一致。本组钉两件事：
    ///  ① 流程栏菜单里确实挂着「编辑绑定…」，且命令接的是 OpenBindingCommand（不是空壳）；
    ///  ② 执行它真的打开 DataBindView，并且**带上了 TargetStep**（不带锚点的弹窗是
    ///     "完整绑定视图"，不会定位到当前步骤——那等于点了没效果）。
    ///
    /// 判据只认"弹了哪个窗 + 参数里有没有 TargetStep"，不依赖任何视图元素。
    /// </summary>
    internal static class ProcessMenuChecks
    {
        public static void Run()
        {
            Program.Section("[PM] 右键菜单对齐（流程栏 / 画布）");

            // ---- ① XAML：菜单项存在、绑的是 OpenBindingCommand ----
            string xamlPath = System.IO.Path.Combine(@"d:\C#\VM", "VisionMaster", "Views", "ProcessView.xaml");
            string canvasPath = System.IO.Path.Combine(@"d:\C#\VM", "VisionMaster", "Views", "FlowCanvasView.xaml");
            string stylePath = System.IO.Path.Combine(@"d:\C#\VM", "UI", "Controls", "Themes", "Controls", "StepContextMenu.xaml");
            if (!System.IO.File.Exists(xamlPath) || !System.IO.File.Exists(canvasPath) || !System.IO.File.Exists(stylePath))
            {
                Program.Check("PM1 前提：三份 XAML 都在", false, $"{xamlPath} / {canvasPath} / {stylePath}");
                return;
            }

            string text = System.IO.File.ReadAllText(xamlPath);
            string canvasText = System.IO.File.ReadAllText(canvasPath);
            string styleText = System.IO.File.ReadAllText(stylePath);

            // 菜单项必须同时满足：Header 是「编辑绑定…」、Command 是 OpenBindingCommand、
            // 且用了流程栏统一的 StepMenuItemStyle（漏了就是画布那种裸菜单项混进来）
            bool hasItem = Regex.IsMatch(text,
                @"<MenuItem[^>]*Command=""\{Binding OpenBindingCommand\}""[\s\S]{0,400}?Header=""编辑绑定…""",
                RegexOptions.Singleline)
                || Regex.IsMatch(text,
                @"<MenuItem[^>]*Header=""编辑绑定…""[\s\S]{0,400}?Command=""\{Binding OpenBindingCommand\}""",
                RegexOptions.Singleline);

            bool usesFlowStyle = Regex.IsMatch(text,
                @"Command=""\{Binding OpenBindingCommand\}""[\s\S]{0,400}?Style=""\{StaticResource StepMenuItemStyle\}""",
                RegexOptions.Singleline);

            Program.Check("PM1 流程栏菜单挂着「编辑绑定…」且接 OpenBindingCommand（不是空壳项）",
                hasItem, "ProcessView 的 TreeView.ContextMenu 里应有该项");

            Program.Check("PM1 该菜单项用流程栏统一菜单样式（不混入裸 MenuItem）",
                usesFlowStyle, "应带 Style=\"{StaticResource StepMenuItemStyle}\"");

            // ---- ② 两个放置面的命令集必须覆盖同一批核心项 ----
            // 核心项 = "同一个步骤在两处都该能做的事"。树专有（展开/折叠）与画布专有（粘贴）不在内。
            // 注意两边**命令面本来就不同**：流程栏是 ModuleActionCommand + 枚举参数，
            // 画布是节点 VM 上的独立命令。所以按"能力是否可达"判，不按命令名判。
            var flowCore = new (string Marker, string Label)[]
            {
                ("Binding OpenBindingCommand", "编辑绑定"),
                ("ModuleCommandAction.Rename", "重命名"),
                ("ModuleCommandAction.EditComment", "编辑注释"),
                ("ModuleCommandAction.ModuleParameters", "步骤参数"),
                ("ModuleCommandAction.ToggleDisable", "禁用"),
                ("ModuleCommandAction.Delete", "删除"),
            };

            var canvasCore = new (string Marker, string Label)[]
            {
                ("Binding OpenBindingCommand", "编辑绑定"),
                ("Binding RenameCommand", "重命名"),
                ("Binding EditCommentCommand", "编辑注释"),
                ("Binding OpenModuleParametersCommand", "步骤参数"),
                ("Binding ToggleDisableCommand", "禁用"),
                ("Binding DeleteCommand", "删除"),
            };

            var missingInFlow = flowCore.Where(c => !text.Contains(c.Marker)).Select(c => c.Label).ToList();
            var missingInCanvas = canvasCore.Where(c => !canvasText.Contains(c.Marker)).Select(c => c.Label).ToList();

            Program.Check("PM2 流程栏菜单覆盖核心命令集（编辑绑定/重命名/注释/参数/禁用/删除）",
                missingInFlow.Count == 0, "缺=" + string.Join(",", missingInFlow));

            Program.Check("PM2 画布菜单覆盖同一批核心命令（画布此前缺重命名/编辑注释）",
                missingInCanvas.Count == 0, "缺=" + string.Join(",", missingInCanvas));

            // ---- ③ 两处都用共享样式（下沉到 UI 主题，不许各写各的）----
            Program.Check("PM2 流程栏与画布都引用共享菜单样式（下沉到 UI 主题，不再各写一份）",
                text.Contains("StepContextMenu.xaml") && canvasText.Contains("StepContextMenu.xaml")
                && styleText.Contains("x:Key=\"StepContextMenuStyle\"")
                && styleText.Contains("x:Key=\"StepMenuItemStyle\"")
                && styleText.Contains("x:Key=\"StepMenuSeparatorStyle\""),
                "两个视图应各自就地 merge StepContextMenu.xaml");

            // 页脚语义必须各写各的：流程栏作用于"当前选中"，画布作用于"指针下的节点"
            Program.Check("PM2 页脚语义不照抄（流程栏=当前选中 / 画布=指针下的节点）",
                text.Contains("仅作用于当前选中步骤")
                && canvasText.Contains("作用于指针下的")
                && !canvasText.Contains("仅作用于当前选中步骤"),
                "画布若照抄流程栏页脚就是在说假话");

            // ---- ④ 流程栏「复制」接到共享剪贴板 ----
            Program.Check("PM3 流程栏菜单挂着「复制」且接 CopyStepCommand",
                Regex.IsMatch(text, @"<MenuItem[^>]*Command=""\{Binding CopyStepCommand\}""", RegexOptions.Singleline),
                "ProcessView 菜单里应有复制项");

            // 快捷键与菜单必须同一出口：Ctrl+C 也接 CopyStepCommand（否则菜单能复制、键盘不能，
            // 或者反过来——两套实现迟早漂移）
            Program.Check("PM3 流程栏 Ctrl+C 接到同一命令（快捷键与菜单同一出口）",
                HasCtrlC(text, "CopyStepCommand"),
                "TreeView.InputBindings 里应有 Ctrl+C → CopyStepCommand");

            Program.Check("PM3 反向：画布自己的 Ctrl+C 仍在（焦点在画布时复制画布选中，没被流程栏抢掉）",
                HasCtrlC(canvasText, "CopySelectedCommand"),
                "FlowCanvasView 的 Ctrl+C → CopySelectedCommand 不应被改动");

            var hCopy = new Harness();
            var dialogs2 = new SilentDialogService();
            var vmCopy = new ProcessViewModel(hCopy.Workspace, dialogs2);
            try
            {
                var step = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "复制源_0");
                hCopy.Add(step);

                FlowClipboard.Clear();
                vmCopy.CopyStepCommand.Execute();
                Program.Check("PM3 未选中时复制 → 剪贴板仍为空（不拿陈旧选中当靶）",
                    !FlowClipboard.HasPayload, $"HasPayload={FlowClipboard.HasPayload}");

                vmCopy.SelectStep = step;
                vmCopy.CopyStepCommand.Execute();
                Program.Check("PM3 选中后复制 → 进入共享剪贴板（画布右键可粘贴）",
                    FlowClipboard.HasPayload, $"HasPayload={FlowClipboard.HasPayload}");
            }
            finally
            {
                FlowClipboard.Clear();
                vmCopy.Deactivate();
            }

            // ---- ⑤ 画布「重命名 / 编辑注释」受运行锁（与流程栏同一道门禁）----
            var h = new Harness();
            var dialogs = new SilentDialogService();
            var vm = new ProcessViewModel(h.Workspace, dialogs);
            try
            {
                var step = new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "采集_0");
                h.Add(step);

                // 未选中：不许弹窗（菜单项不该有动作，也不能拿上一次的选中当靶）
                dialogs.ShownDialogs.Clear();
                vm.OpenBindingCommand.Execute();
                Program.Check("PM4 未选中步骤 → 不弹窗（不拿陈旧选中当靶）",
                    dialogs.ShownDialogs.Count == 0,
                    $"弹窗={string.Join(",", dialogs.ShownDialogs)}");

                // 选中后：弹 DataBindView
                vm.SelectStep = step;
                dialogs.ShownDialogs.Clear();
                vm.OpenBindingCommand.Execute();
                Program.Check("PM4 选中步骤 → 打开 DataBindView",
                    dialogs.ShownDialogs.Count == 1 && dialogs.ShownDialogs[0] == "DataBindView",
                    $"弹窗={string.Join(",", dialogs.ShownDialogs)}");

                // 参数必须带 TargetStep，且就是当前选中那个（不带 = 打开完整绑定视图，定位不到这一步）
                var parameters = dialogs.LastParameters;
                bool hasTarget = parameters != null
                                 && parameters.TryGetValue<StepModel>("TargetStep", out var target)
                                 && ReferenceEquals(target, step);

                Program.Check("PM4 弹窗参数带 TargetStep 且等于当前选中步骤（否则定位不到这一步）",
                    hasTarget,
                    parameters == null
                        ? "没有记录到参数（空参数 = 打开完整绑定视图）"
                        : $"参数个数={parameters.Count}");

                // 反向：选中被清空后立刻不再弹（不缓存）
                vm.SelectStep = null;
                dialogs.ShownDialogs.Clear();
                vm.OpenBindingCommand.Execute();
                Program.Check("PM4 反向：清空选中后不再弹窗（命令每次现读选中）",
                    dialogs.ShownDialogs.Count == 0,
                    $"弹窗={string.Join(",", dialogs.ShownDialogs)}");
            }
            finally
            {
                vm.Deactivate();
            }

            // ---- ⑥ 画布侧：重命名 / 编辑注释命令已注入，且受运行锁 ----
            var h5 = new Harness();
            var leaf5 = h5.Add(h5.Leaf("A"));
            var cv5 = h5.Canvas;
            var node5 = h5.Node(leaf5)!;

            Program.Check("PM5 画布节点注入了「重命名 / 编辑注释」命令（此前两个入口都不存在）",
                node5.RenameCommand != null && node5.EditCommentCommand != null,
                $"rename={node5.RenameCommand != null} comment={node5.EditCommentCommand != null}");

            Program.Check("PM5 画布节点暴露断点勾选态（菜单 IsChecked 绑它，随模型转发通知）",
                node5.IsBreakpoint == (leaf5.IsBreakpoint),
                $"node={node5.IsBreakpoint} model={leaf5.IsBreakpoint}");

            Core.Events.GlobalEventBus.Publish(MainRunState.RunningOnce);
            try
            {
                string before = leaf5.StepName;
                cv5.StatusHint = null;
                node5.RenameCommand!.Execute();

                Program.Check("PM5 运行中重命名被运行锁拦下（状态栏给的是运行锁文案，不是弹窗失败）",
                    leaf5.StepName == before && (cv5.StatusHint ?? "").Contains("运行中"),
                    $"名字='{leaf5.StepName}' 回执='{cv5.StatusHint}'");
            }
            finally
            {
                Core.Events.GlobalEventBus.Publish(MainRunState.NotStarted);
            }
        }
        /// <summary>
        /// XAML 里有没有"Ctrl+C → 指定命令"的 KeyBinding。
        /// 逐个自闭合 &lt;KeyBinding .../&gt; 块判，不做跨块匹配——否则"Delete 绑 A、Ctrl+C 绑 B"
        /// 这种把 Key 和 Command 拆在两个块里的写法会被误判成通过。
        /// </summary>
        private static bool HasCtrlC(string xaml, string command)
        {
            foreach (Match m in Regex.Matches(xaml, @"<KeyBinding[^>]*/>"))
            {
                string block = m.Value;
                if (block.Contains("Key=\"C\"")
                    && block.Contains("Modifiers=\"Control\"")
                    && block.Contains("Binding " + command))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
