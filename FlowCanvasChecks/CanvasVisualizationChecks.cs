using System;
using System.IO;
using System.Linq;
using System.Windows;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 评审清单 B 包第二批（画布可视化 + 几何/持久化/稳定性）的画布级断言。
    ///
    /// 覆盖六项（项 6 走线走渲染探针对比、项 8 走 CodeReaderChecks 内的用例内重试，不在本文件）：
    ///   [Z1] 泳道执行染色 —— 泳道 VM 对 StepCollection.IsRunning 的转发投影（引擎并行扇出打标）；
    ///   [Z2] 顺序链已执行染色 —— 两端步骤离开 Idle（终态口径）→ IsExecuted，State 订阅即时刷新；
    ///   [Z3] 连线中段标签 —— 单绑定显示「目标口 ← 源口」短文本、多绑定保持 ×N + ToolTip 明细；
    ///   [Z4] 必填参数黄叹号 —— 与 FlowCompiler [参数缺失] 同一判据的画布投影（真类型桩驱动）；
    ///   [Z5] 拖动对齐参考线 —— 主拖动节点与其它节点同名边 ≤6px 对齐即出线，拖离即收；
    ///   [Z7] 视图态持久化 —— FlowCanvasSettings 节的默认值 / 序列化往返 / Sync 写读一致。
    /// </summary>
    internal static class CanvasVisualizationChecks
    {
        public static void Run()
        {
            LaneRunningColor();
            OrderLinkExecutedColor();
            LinkBindingLabel();
            MissingRequiredBadge();
            DragAlignmentGuides();
            FlowCanvasSettingsPersistence();
            RunTimeLiveTick();
        }

        // ==================================================================
        //  [Z8] 画布运行耗时实时刷新（2026-10-12 真机反馈："画布不会实时更新插件的运行时间"）
        //
        //  病根：画布耗时徽章只绑 LastRunTimeMs（完成后冻结的终值），运行中的实时值
        //  （CurrentRunTimeMs）只有流程栏的 200ms tick 在刷——画布不跳秒。
        //  修法两半：① 徽章双态绑定（IsRunningFocus → CurrentRunTimeMs，结束后 LastRunTimeMs）；
        //  ② 画布自有 200ms tick（Attach 启动 / Detach 与空流程停止），不再依赖流程栏面板开着。
        // ==================================================================
        private static void RunTimeLiveTick()
        {
            Section("[Z8] 画布运行耗时实时刷新（自有 tick + 双态徽章数据源）");

            var h = new Harness();
            var step = h.Leaf("运行中步骤");
            h.Add(step);
            step.State = StepState.Running;
            step.IsRunningFocus = true;
            step.LastRunStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

            var canvas = h.Canvas;
            Check("Z8：挂接图纸后耗时 tick 在跑（Attach 启动生命周期）",
                canvas.RunTimeTimerEnabled, "");

            double before = step.CurrentRunTimeMs;
            System.Threading.Thread.Sleep(30);   // 让墙钟前进，保证可测差值
            canvas.RunTimeTick();
            Check("Z8：tick 后运行中步骤的 CurrentRunTimeMs 实时前进（画布徽章的数据源）",
                step.CurrentRunTimeMs > before,
                $"{before:0.#} → {step.CurrentRunTimeMs:0.#} ms");

            // 切到无图纸（null）→ Detach 停 tick；切回 → 恢复。
            // 注意：切到另一条"有效流程"时 tick 理应继续跑（有挂接就有刷新），所以这里用 null。
            var original = h.Workspace.CurrentFlow;
            h.Workspace.SwitchFlow(null);
            bool stoppedAfterSwitch = !canvas.RunTimeTimerEnabled;
            h.Workspace.SwitchFlow(original);

            Check("Z8：切到无图纸 tick 停止、切回恢复（Detach/Attach 生命周期对称）",
                stoppedAfterSwitch && canvas.RunTimeTimerEnabled,
                $"无图纸时停止={stoppedAfterSwitch} 切回后恢复={canvas.RunTimeTimerEnabled}");
        }

        // ==================================================================
        //  [Z1] 泳道执行染色
        // ==================================================================
        private static void LaneRunningColor()
        {
            Section("[Z1] 泳道执行染色（StepCollection.IsRunning → 泳道 IsBranchRunning）");

            var h = new Harness();
            var par = h.Parallel("并行分组");
            var branch1 = par.Children[0];
            var branch2 = par.Children[1];
            h.Add(par);

            var canvas = h.Canvas;
            var lane1 = h.Lane(branch1)!;
            var lane2 = h.Lane(branch2)!;

            // 引擎扇出打标是运行期行为：直接置 IsRunning（CompiledParallelNode.ExecuteBranch 同款写法），
            // 泳道 VM 靠对 StepCollection 的 PropertyChanged 订阅即时跟随（不经重建）
            branch1.IsRunning = true;
            Check("Z1 分支1置运行 → 对应泳道 IsBranchRunning=true、分支2泳道保持 false",
                lane1.IsBranchRunning && !lane2.IsBranchRunning,
                $"lane1={lane1.IsBranchRunning} lane2={lane2.IsBranchRunning}");

            branch1.IsRunning = false;
            Check("Z1 复位后 → 泳道 IsBranchRunning=false（订阅刷新，无需重建）",
                !lane1.IsBranchRunning,
                $"lane1={lane1.IsBranchRunning}");
        }

        // ==================================================================
        //  [Z2] 顺序链已执行染色
        // ==================================================================
        private static void OrderLinkExecutedColor()
        {
            Section("[Z2] 顺序链已执行染色（两端离开 Idle → IsExecuted）");

            var h = new Harness();
            var collect = h.Leaf("采集");
            var detect = h.Leaf("检测");
            h.Add(collect);
            h.Add(detect);

            var canvas = h.Canvas;
            var chain = h.OrderLink(collect, detect)!;

            Check("Z2 两端 Idle → IsExecuted=false（保持灰虚线口径）",
                !chain.IsExecuted,
                $"IsExecuted={chain.IsExecuted}");

            // 终态口径：两端步骤都离开 Idle 才算这段顺序"已走过"；
            // 状态由 SetRuntimeState 发通知，连线 VM 靠 State 订阅即时翻转（无需重建画布）
            collect.State = StepState.Success;
            detect.State = StepState.Success;
            Check("Z2 两端置 Success → IsExecuted=true（State 订阅即时刷新）",
                chain.IsExecuted,
                $"IsExecuted={chain.IsExecuted}");
        }

        // ==================================================================
        //  [Z3] 连线中段标签：单绑定短文本 / 多绑定 ×N
        // ==================================================================
        private static void LinkBindingLabel()
        {
            Section("[Z3] 连线中段标签（单绑定「目标口 ← 源口」/ 多绑定 ×N）");

            var h = new Harness();
            var producer = h.Leaf("采集");
            var consumer = h.Leaf("检测");
            h.Add(producer);
            h.Add(consumer);

            // 先连线后开画布（RawLink 只写模型、不触发画布重建——与既有断言同一条顺序纪律）
            Harness.RawLink(consumer, "In", producer, "Out");
            var canvas = h.Canvas;
            // 数据线默认隐藏（用户口径）：聚合线会被整体滤出 Links——本组断言读聚合线，先打开
            canvas.ShowDataLinks = true;

            var link = h.Link(producer, consumer)!;

            // Display 口径：DescribeInput(消费方, 键) ← 生产方标题.源端口名
            Check("Z3 单绑定 → 标签显示「目标口 ← 源口」短文本",
                link.LinkLabel == "In ← 采集.Out",
                $"'{link.LinkLabel}'");

            // 同一对模块的第二条绑定：聚合进同一根线 → 计数形态，明细退到 ToolTip
            Harness.RawLink(consumer, "In2", producer, "Out");
            canvas.RefreshLinks();
            link = h.Link(producer, consumer)!;
            Check("Z3 多绑定 → 保持 ×N 计数、ToolTip 逐行列明细（不回退）",
                link.LinkLabel == "×2"
                && link.Tooltip.Contains("In ← 采集.Out")
                && link.Tooltip.Contains("In2 ← 采集.Out"),
                $"Label='{link.LinkLabel}' Tooltip='{link.Tooltip.Replace("\r", "").Replace("\n", " / ")}'");
        }

        // ==================================================================
        //  [Z4] 必填参数黄叹号（与 FlowCompiler [参数缺失] 同一判据）
        //
        //  为什么新写专用桩：InputPort.IsRequired 默认 true，仓内既有桩为躲编译期
        //  [参数缺失] 全都一个输入口都不声明——本断言要的就是"有必填口且未配置"，
        //  改既有桩的契约会牵连执行层断言，专用桩最稳。
        // ==================================================================
        private static void MissingRequiredBadge()
        {
            Section("[Z4] 必填参数黄叹号（IsRequired 且无连线且无手填常量）");

            var h = new Harness();
            var producer = h.Leaf("上游");
            var stub = new ActionStep(
                "\uE700", "必填桩", typeof(RequiredPortStubPlugin).AssemblyQualifiedName, "必填参数步骤");
            h.Add(producer);
            h.Add(stub);

            var canvas = h.Canvas;
            var node = h.Node(stub)!;

            Check("Z4 未配置 → HasMissingRequired=true 且提示列出缺失端口名",
                node.HasMissingRequired && node.MissingRequiredTooltip.Contains("Data"),
                $"Has={node.HasMissingRequired} Tip='{node.MissingRequiredTooltip.Replace("\r", "").Replace("\n", " / ")}'");

            // 手填常量 = 编译器 hasConstant 口径：有值（非 null）即算已配置
            stub.SetInputValue("Data", "手填常量");
            Check("Z4 手填常量后 → false（与编译器 hasConstant 同口径）",
                !node.HasMissingRequired,
                $"Has={node.HasMissingRequired}");

            // 改接上游 = LinkedSource 口径：连线优先于常量，同样算已配置
            stub.RemoveInputValue("Data");
            Harness.RawLink(stub, "Data", producer, "Out");
            Check("Z4 连上游后 → false（LinkedSources 订阅即时刷新）",
                !node.HasMissingRequired,
                $"Has={node.HasMissingRequired}");
        }

        // ==================================================================
        //  [Z5] 拖动对齐参考线（纯视觉附加；断言只盯 VM 的 GuideX/GuideY/可见性）
        // ==================================================================
        private static void DragAlignmentGuides()
        {
            Section("[Z5] 拖动对齐参考线（同名边 ≤6px 出线、拖离即收）");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            h.Add(a);
            h.Add(b);

            var canvas = h.Canvas;
            var aNode = h.Node(a)!;
            var bNode = h.Node(b)!;

            // 拖动手势开始：OnItemsDragStarted 的回退路径按"选中集"取被拖节点，
            // 把 A 单选化后空参执行（与 Nodify 7.3 的实参形态一致——实参本是编辑器 DataContext）
            aNode.IsSelected = true;
            canvas.ItemsDragStartedCommand.Execute(null);

            // 先挪离对齐位（A 与 B 同列起排，X 本来就相等——先制造 50px 错位才有判定意义）
            aNode.Location = new Point(bNode.Location.X + 50, aNode.Location.Y);
            Check("Z5 未对齐（差 50px > 6px）→ 参考线隐藏",
                canvas.GuideVisibility == Visibility.Collapsed
                && canvas.GuideX == null && canvas.GuideY == null,
                $"vis={canvas.GuideVisibility} gx={canvas.GuideX} gy={canvas.GuideY}");

            // 左边对到 B 的左边（直接设 Location 模拟拖动中的位置回写）
            aNode.Location = new Point(bNode.Location.X, aNode.Location.Y);
            Check("Z5 左边对齐 → 竖向参考线可见且 X = 对齐目标的左缘",
                canvas.GuideVisibility == Visibility.Visible
                && canvas.GuideX != null && Math.Abs(canvas.GuideX.Value - bNode.Location.X) < 0.01,
                $"gx={canvas.GuideX} 期望={bNode.Location.X}");

            // 上缘对到 B 的上缘 → 横向参考线（此时 X 也对齐，竖线同样在场，但 Y 是本条判据）
            aNode.Location = new Point(bNode.Location.X, bNode.Location.Y);
            Check("Z5 上缘对齐 → 横向参考线可见且 Y = 对齐目标的上缘",
                canvas.GuideY != null && Math.Abs(canvas.GuideY.Value - bNode.Location.Y) < 0.01,
                $"gy={canvas.GuideY} 期望={bNode.Location.Y}");

            // 拖离（+100px，同名边差全部越界）→ 两条线都收
            aNode.Location = new Point(bNode.Location.X + 100, bNode.Location.Y + 100);
            Check("Z5 拖离 → 参考线隐藏（抬手前的实时口径）",
                canvas.GuideVisibility == Visibility.Collapsed
                && canvas.GuideX == null && canvas.GuideY == null,
                $"vis={canvas.GuideVisibility} gx={canvas.GuideX} gy={canvas.GuideY}");

            // 收尾：走一次真 Completed，钉住"挂摘成对 + 抬手即收"不因早退路径漏摘
            canvas.ItemsDragCompletedCommand.Execute(null);
            Check("Z5 抬手后 → 订阅已摘、参考线保持隐藏（挂摘成对）",
                canvas.GuideVisibility == Visibility.Collapsed
                && canvas.GuideX == null && canvas.GuideY == null,
                $"vis={canvas.GuideVisibility} gx={canvas.GuideX} gy={canvas.GuideY}");
        }

        // ==================================================================
        //  [Z7] 视图态持久化：AppConfigModel.FlowCanvasSettings
        // ==================================================================
        private static void FlowCanvasSettingsPersistence()
        {
            Section("[Z7] 视图态持久化（FlowCanvasSettings：默认值 / 往返 / Sync 写读一致）");

            // Z7-1 默认节非 null 且两值默认——口径必须与画布 VM 的代码默认逐字一致
            //（数据线默认关是用户定过的口径，顺序链默认开；初始化器 = 老配置缺节的 null 防御）
            var model = new AppConfigModel();
            Check("Z7 默认节非 null 且两值默认（数据线关 / 顺序链开，与画布代码默认一致）",
                model.FlowCanvasSettings != null
                && model.FlowCanvasSettings.ShowDataLinks == false
                && model.FlowCanvasSettings.ShowOrderLinks == true,
                $"node={model.FlowCanvasSettings != null} data={model.FlowCanvasSettings.ShowDataLinks} order={model.FlowCanvasSettings.ShowOrderLinks}");

            // Z7-2 序列化往返保值 + 老配置缺节反序列化的初始化器兜底
            var json = JsonConvert.SerializeObject(model);
            var back = JsonConvert.DeserializeObject<AppConfigModel>(json);
            var oldLayout = JsonConvert.DeserializeObject<AppConfigModel>("{}");
            Check("Z7 序列化往返保值；老配置缺节 → 初始化器兜底非 null",
                back?.FlowCanvasSettings != null
                && back.FlowCanvasSettings.ShowDataLinks == false
                && back.FlowCanvasSettings.ShowOrderLinks == true
                && oldLayout?.FlowCanvasSettings != null,
                $"back={back?.FlowCanvasSettings != null} old={oldLayout?.FlowCanvasSettings != null}");

            // Z7-3 改值 → SyncFlowCanvasSettings（写盘）→ 新实例重读一致。
            // 落盘目标 = 本进程 BaseDirectory 的 AppConfig.json（服务契约如此）；
            // 断言宿主的密闭性靠"原本没有 → 用完删掉"还原（原本有则不动、不删）
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppConfig.json");
            bool existedBefore = File.Exists(configPath);
            try
            {
                var service = new AppSettingsService();
                service.SyncFlowCanvasSettings(showDataLinks: true, showOrderLinks: false);
                var reloaded = new AppSettingsService();
                Check("Z7 改值 → SyncFlowCanvasSettings → 重读一致（含即时落盘）",
                    reloaded.Current?.FlowCanvasSettings != null
                    && reloaded.Current.FlowCanvasSettings.ShowDataLinks == true
                    && reloaded.Current.FlowCanvasSettings.ShowOrderLinks == false,
                    $"data={reloaded.Current?.FlowCanvasSettings?.ShowDataLinks} order={reloaded.Current?.FlowCanvasSettings?.ShowOrderLinks}");
            }
            finally
            {
                if (!existedBefore && File.Exists(configPath))
                    File.Delete(configPath);
            }
        }
    }

    /// <summary>
    /// [Z4] 专用桩：带一个必填输入口的真类型插件（IsRequired 用默认 true）。
    /// 走 FlowCanvasChecks 程序集，AssemblyQualifiedName 能被 Type.GetType 真解析——
    /// 这正是画布"按 PluginTypeName 反射原型读必填端口"要走的那条路。
    /// </summary>
    internal sealed class RequiredPortStubPlugin : VisionPluginBase
    {
        public InputPort<string> Data { get; } = new("Data", description: "必填数据");

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 断言桩不跑真算法
        }
    }
}
