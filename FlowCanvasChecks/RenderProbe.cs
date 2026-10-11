using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using Nodify;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;
// 隐式全局 using 里有 System.Linq，它也有个 ParallelExecutionMode（本文件要写并行组的模式，取模型侧）
using ParallelExecutionMode = VisionMaster.Models.ParallelExecutionMode;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程画布渲染探针：--render 模式下起 STA 线程，真实实例化 FlowCanvasView
    ///（App 级资源用 Colors.xaml 文本 + Icon 字体文件兜底），把两个典型图式渲染成 PNG。
    ///
    /// 为什么需要它：断言工程是 headless 的，逻辑断言绿 ≠ 视觉过关——2026-10-07 用户看真机截图
    /// 直接指出"连线太丑/意义不明"。视觉整改的前后对比必须出图，PNG 路径打印到 stdout 供主会话引用。
    ///
    /// 用法：dotnet run --no-build -- --render [输出目录]
    /// 输出：flowcanvas-render-A.png（纵向两步 + 数据线/顺序链，复刻真机反馈场景；**默认态**只画顺序链，
    ///                                     数据线默认隐藏——用户口径"端口之间的连线默认不显示"）
    ///       flowcanvas-render-A2-datalinks.png（同 A 图式、显式打开数据线——开关的视觉对照）
    ///       flowcanvas-render-B.png（If 容器 + 双分支 + 跨容器连线 + 折叠态）
    ///       flowcanvas-render-C-expand/-collapse.png（多条件多嵌套）
    ///       flowcanvas-render-D-10levels.png（十层嵌套压力图式）
    ///       flowcanvas-render-E-parallel.png（画布上的并行分组图式）
    ///       process-tree-render-F.png（左侧流程栏步骤树：并行分组/If 容器/分支胶囊，2026-10-09 类型全名事故对照；
    ///                                     兼自检分支卡片左键拦截与分支胶囊右键菜单——结构动作从面板下沉到右键）
    ///       parallel-group-params-render-G.png（并行分组参数面板：FlatPropertyGrid 标准属性面板本体，
    ///                                     2026-10-09 专属视图收编后改出属性网格；弹窗壳是 Popup 不出图）
    ///       flowcanvas-render-H-parallel-nested.png（并行分组 4 分支、分支 1 里嵌套 If/For/While 的真机图式，
    ///                                               2026-10-09 "流程画布太乱"互压缺陷的修复对照）
    /// </summary>
    internal static class RenderProbe
    {
        public static void Run(string outDir)
        {
            var thread = new Thread(() => RenderBoth(outDir));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static void RenderBoth(string outDir)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // FlowCanvasView 的 StaticResource 在 InitializeComponent 时解析，资源必须先行：
            // Colors.xaml 文本直载 + Icon 字体按文件路径补（App.xaml 的 pack URI 探针够不着）
            var colors = (ResourceDictionary)XamlReader.Parse(
                File.ReadAllText(@"D:\C#\VM\UI\Controls\Themes\Colors.xaml"));
            app.Resources.MergedDictionaries.Add(colors);
            // FA 字体的 pack URI 探针够不着：用系统自带的 MDL2 兜底 + MDL2 码位字形
            app.Resources["Icon"] = new FontFamily("Segoe MDL2 Assets");

            Directory.CreateDirectory(outDir);

            RenderScenarioA(Path.Combine(outDir, "flowcanvas-render-A.png"));
            RenderScenarioA2(Path.Combine(outDir, "flowcanvas-render-A2-datalinks.png"));
            RenderScenarioB(Path.Combine(outDir, "flowcanvas-render-B.png"));
            RenderScenarioC(Path.Combine(outDir, "flowcanvas-render-C-expand.png"), collapseDeepIf: false);
            RenderScenarioC(Path.Combine(outDir, "flowcanvas-render-C-collapse.png"), collapseDeepIf: true);
            RenderScenarioD(Path.Combine(outDir, "flowcanvas-render-D-10levels.png"));
            RenderScenarioE(Path.Combine(outDir, "flowcanvas-render-E-parallel.png"));
            RenderScenarioF(Path.Combine(outDir, "process-tree-render-F.png"));
            RenderScenarioG(Path.Combine(outDir, "parallel-group-params-render-G.png"));
            RenderScenarioH(Path.Combine(outDir, "flowcanvas-render-H-parallel-nested.png"));

            app.Shutdown();
        }

        /// <summary>场景 A：复刻真机反馈图式——纵向两步（采集→检测）。默认态：只见顺序链（数据线默认隐藏）</summary>
        private static void RenderScenarioA(string path)
            => Render(BuildScenarioA(), path, 640, 780);

        /// <summary>
        /// 场景 A2：与 A 同图式、显式打开数据线——工具条「数据线」勾选后的视觉对照。
        /// A（默认态）与 A2（打开态）两张图就是那条开关的亲眼前后证据。
        /// </summary>
        private static void RenderScenarioA2(string path)
            => Render(BuildScenarioA(), path, 640, 780, showDataLinks: true);

        private static Harness BuildScenarioA()
        {
            var h = new Harness();
            h.Flow.FlowName = "胶路检测";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Failed,
            };
            h.Add(capture);
            h.Add(bead);
            Harness.RawLink(bead, "In", capture, "Out");

            return h;
        }

        /// <summary>场景 B：If 容器双分支（一实一空）+ 容器内外连线，验证框体/泳道/连线观感</summary>
        private static void RenderScenarioB(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "带分支流程";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var judge = new ConditionStep("\uE7C1", "胶路检测", StubPluginProvider.IfPlugin, "结果判断");
            var okStep = new ActionStep("\uE73E", "合格出货", StubPluginProvider.LeafPlugin, "合格出货_0");
            var ngStep = new ActionStep("\uE711", "NG 处理", StubPluginProvider.LeafPlugin, "NG处理_0");
            judge.Children[0].Steps.Add(okStep);
            judge.Children[1].Steps.Add(ngStep);   // 第二分支给内容（两个分支都有泳道）

            h.Add(capture);
            h.Add(judge);
            Harness.RawLink(judge, "In", capture, "Out");
            Harness.RawLink(okStep, "In", capture, "Out");

            Render(h, path, 900, 900);
        }

        /// <summary>
        /// 场景 C：多条件多嵌套图式（视觉评审用）——
        /// 顶层：图像采集 → 图像预处理 → If「质量分档」→ 结果汇总；
        /// 「质量分档」的 If 分支：合格品复检 → 嵌套 If「深度检测」（精细测量 / 返修标记）；
        /// Else 分支：For「重试采集」（内嵌重拍图像）。
        /// 连线含跨层级长线（顶层采集直喂深层测量）与内层喂外层（测量→结果汇总）。
        /// collapseDeepIf=true 时把内层「深度检测」折叠，展示折叠框与角标观感。
        /// </summary>
        private static void RenderScenarioC(string path, bool collapseDeepIf)
        {
            var h = new Harness();
            h.Flow.FlowName = collapseDeepIf ? "胶路检测·复杂图式（深度检测已折叠）" : "胶路检测·复杂图式";

            // ---- 顶层 ----
            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "车间工位 3 面阵相机，500 万像素",
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Running,
                Description = "畸变校正后裁出胶路检测 ROI",
            };
            var qualityIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "质量分档")
            {
                Description = "按复检分数分档：≥90 进深度检测，否则重试",
            };
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            // ---- 质量分档 · If 分支：复检 → 嵌套 If「深度检测」 ----
            var recheck = new ActionStep("\uE73E", "合格品复检", StubPluginProvider.LeafPlugin, "合格品复检_0")
            {
                State = StepState.Success,
                Description = "二次模板匹配 + 分数闸门",
            };
            var deepIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "深度检测");
            var measure = new ActionStep("\uE722", "尺寸测量", StubPluginProvider.LeafPlugin, "精细测量_0")
            {
                State = StepState.Success,
                Description = "卡尺拟合轮廓，输出胶路宽度/高度",
            };
            var repair = new ActionStep("\uE711", "返修标记", StubPluginProvider.LeafPlugin, "返修标记_0")
            {
                State = StepState.Skipped,
            };
            deepIf.Children[0].Steps.Add(measure);
            deepIf.Children[1].Steps.Add(repair);
            qualityIf.Children[0].Steps.Add(recheck);
            qualityIf.Children[0].Steps.Add(deepIf);

            // ---- 质量分档 · Else 分支：For「重试采集」 ----
            var retryFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "重试采集");
            var recapture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "重拍图像_0")
            {
                State = StepState.Running,
            };
            retryFor.Children[0].Steps.Add(recapture);
            qualityIf.Children[1].Steps.Add(retryFor);

            h.Add(capture);
            h.Add(preprocess);
            h.Add(qualityIf);
            h.Add(summary);

            // ---- 连线（FlowTopology 口径内）----
            Harness.RawLink(preprocess, "In", capture, "Out");
            Harness.RawLink(qualityIf, "In", preprocess, "Out");
            Harness.RawLink(recheck, "In", preprocess, "Out");       // 预处理直喂分支内复检
            Harness.RawLink(deepIf, "In", recheck, "Out");
            Harness.RawLink(measure, "In", capture, "Out");          // 跨层级长线：顶层采集直喂深层测量
            Harness.RawLink(summary, "In", measure, "Out");          // 内层喂外层尾部
            Harness.RawLink(recapture, "In", capture, "Out");        // 跨层级：顶层采集喂 Else 分支内重拍

            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            if (collapseDeepIf)
            {
                // 画布构造时已 AutoLayout，布局落定后折叠内层容器再重画
                h.Flow.Layout.ToggleCollapsed(deepIf.StepID);
                canvas.RebuildInPlace();
            }

            RenderCore(canvas, path, 1000, 1000);
        }

        /// <summary>
        /// 场景 D（压力图式）：10 层 If 嵌套、33 个算子、21 条数据线 + 全量顺序链（110+ 画布项）。
        /// 嵌套链：分档_1 的 If 分支里放 测量_1A + 分档_2，递归到分档_10（最内层）；
        /// 每个容器的两个分支各一个叶子步骤（测量_xA / 测量_xB 共 20 个）；
        /// 数据线 = 顶层采集喂全部 20 个叶子 + 预处理喂最外层容器（跨层级长线压力）。
        /// 超深自动折叠（AutoCollapseDepth=3）生效后：1~3 层展开、4~10 层收成折叠框
        ///（图不再铺成一屏对角线；点折叠框头可逐层展开）。
        /// </summary>
        private static void RenderScenarioD(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "十层嵌套压力图式（自动折叠 3 层）";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Success,
            };
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            // 自内向外组装嵌套链：循环终点 inner = 分档_1（最外层）
            ConditionStep? inner = null;
            var leaves = new System.Collections.Generic.List<ActionStep>();
            var ifSteps = new System.Collections.Generic.List<ConditionStep>();
            for (int level = 10; level >= 1; level--)
            {
                var ifStep = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, $"分档_{level}");
                var leafA = new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, $"测量_{level}A")
                {
                    State = level % 3 == 0 ? StepState.Skipped : StepState.Success,
                };
                var leafB = new ActionStep("\uE711", "缺陷标记", StubPluginProvider.LeafPlugin, $"测量_{level}B")
                {
                    State = level % 4 == 0 ? StepState.Failed : StepState.Running,
                };
                ifStep.Children[0].Steps.Add(leafA);
                if (inner != null)
                    ifStep.Children[0].Steps.Add(inner);
                ifStep.Children[1].Steps.Add(leafB);

                leaves.Add(leafA);
                leaves.Add(leafB);
                ifSteps.Add(ifStep);
                inner = ifStep;
            }

            h.Add(capture);
            h.Add(preprocess);
            h.Add(inner!);
            h.Add(summary);
            ifSteps.Reverse();   // 分档_1 在前，便于引用

            // 数据线：采集喂全部叶子 + 预处理喂最外层容器（全部合法：生产方排在容器之前）
            foreach (var leaf in leaves)
                Harness.RawLink(leaf, "In", capture, "Out");
            Harness.RawLink(inner!, "In", preprocess, "Out");

            Render(h, path, 1280, 1560);
        }

        /// <summary>
        /// 场景 E：并行分组（第一期）——采集 → 预处理 → Parallel{双分支} → 汇总，
        /// 分支间接外层输入合法、分支间互取画成红虚线 + 告警条。
        /// </summary>
        private static void RenderScenarioE(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组图式";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "相机 1 与相机 2 交替采集",
            };
            var preprocess = new ActionStep("\uE7BA", "图像预处理", StubPluginProvider.LeafPlugin, "图像预处理_0")
            {
                State = StepState.Success,
            };
            var par = new ParallelStep("\uE7C1", "并行分组", "VM.CanvasStub.Parallel", "多工位并行")
            {
                Description = "左工位与右工位各自检测，跑完汇合",
            };
            var leftA = new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "左工位测量_0")
            {
                State = StepState.Success,
                Description = "左工位胶路宽度/高度",
            };
            var leftB = new ActionStep("\uE73E", "缺陷标记", StubPluginProvider.LeafPlugin, "左工位标记_0")
            {
                State = StepState.Success,
            };
            var rightA = new ActionStep("\uE711", "缺陷检测", StubPluginProvider.LeafPlugin, "右工位检测_0")
            {
                State = StepState.Running,
                Description = "右工位气泡/断胶",
            };
            par.Children[0].Steps.Add(leftA);
            par.Children[0].Steps.Add(leftB);
            par.Children[1].Steps.Add(rightA);
            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            h.Add(capture);
            h.Add(preprocess);
            h.Add(par);
            h.Add(summary);

            Harness.RawLink(preprocess, "In", capture, "Out");
            Harness.RawLink(par, "In", preprocess, "Out");
            Harness.RawLink(leftA, "In", capture, "Out");     // 分支接外层：合法
            Harness.RawLink(rightA, "In", capture, "Out");    // 分支接外层：合法
            Harness.RawLink(leftB, "In", leftA, "Out");       // 分支 1 内部：合法
            Harness.RawLink(summary, "In", leftB, "Out");
            Harness.RawLink(summary, "In", rightA, "Out");
            // 刻意一条跨分支非法线：左工位结果喂右工位（时序不确定）
            Harness.RawLink(rightA, "In", leftA, "Out");

            Render(h, path, 960, 900);
        }

        /// <summary>
        /// 场景 F：左侧流程栏（ProcessView 步骤树）——真机图式的对照图。
        /// 事故：树按 DataType 隐式选模型模板，ConditionStep/WhileStep/ForStep/StepCollection/ActionStep
        /// 都有、唯独 ParallelStep 没有 → WPF 回退默认模板把 ToString() 上屏，整行显示
        /// "VisionMaster.Models.ParallelStep"（分支也全丢）。
        ///
        /// 图式（2026-10-09 打磨后按用户真机截图重建，供"层级引导线 / 空分支提示 / 分支算子数 /
        /// 四类行左对齐 / 空分支瘦身"五处观感复核）：
        /// 顶层 图像采集_0(成功) → 胶路检测_0(运行) → 并行分组_0(4 分支) → 逻辑路由-[If_1]
        ///（If 带表达式 Score &gt; 80、Else）→ For 循环-[For_0]（循环体有料）→ While 循环-[While_0]
        ///（循环体空且条件未绑）→ Break_0；
        /// 并行分组_0 分支 1 = 逻辑路由-[If_0]（两分支空）/ For 循环-[For_0]（循环体空）/
        /// While 循环-[While_0]（循环体空、条件未绑，红卡）/ Break_0，分支 2~4 全空。
        /// </summary>
        private static void RenderScenarioF(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组图式";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "图像采集",
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Running,
                Description = "胶路检测",
            };

            // ---- 并行分组_0：4 条分支，只有分支 1 有内容 ----
            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "并行分组_0")
            {
                Description = "4 工位并行，跑完汇合",
            };
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });

            // 分支 1 = 真机图式：If_0（未绑定条件、两分支空）→ For_0（循环体空）→ While_0（循环体空、条件未绑）→ Break_0
            var branchIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_0");
            var branchFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            var branchWhile = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            // Break 落 ActionStep 承建：真机里工具箱 BuiltIn_Break 就是按非容器分派成 ActionStep 的
            //（模型类 BreakStep 无树模板，是 U2 例外清单成员）——这里若 new 模型类，出图会复现
            //"类型全名上屏"的假象，与真机不符。
            var branchBreak = new ActionStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");
            par.Children[0].Steps.Add(branchIf);
            par.Children[0].Steps.Add(branchFor);
            par.Children[0].Steps.Add(branchWhile);
            par.Children[0].Steps.Add(branchBreak);

            // ---- 顶层容器行：If（一实两空）对照分支胶囊的"N 个算子"与灰/蓝身份色 ----
            var topIf = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_1");
            topIf.Children[0].Expression = "Score > 80";
            topIf.Children[0]
                .Steps.Add(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "精细测量_0"));
            topIf.Children[1]
                .Steps.Add(new ActionStep("\uE711", "返修标记", StubPluginProvider.LeafPlugin, "返修标记_0"));

            // ---- 顶层容器行：For（循环体有料）/ While（循环体空且条件未绑）/ Break ----
            var topFor = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            topFor.Children[0]
                .Steps.Add(new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "重拍图像_0"));
            var topWhile = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            var topBreak = new ActionStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");

            h.Add(capture);
            h.Add(bead);
            h.Add(par);
            h.Add(topIf);
            h.Add(topFor);
            h.Add(topWhile);
            h.Add(topBreak);

            // 高度按真机图式给足：七个顶层节点 + 并行分组 4 分支 + 分支 1 内四层嵌套全展开约 1100px，
            // 460×900 会把顶层 For/While/Break 三行截在框外（看不到就没法对照容器行观感）。
            RenderTree(h.Workspace, path, 460, 1180);
        }

        /// <summary>
        /// 流程栏渲染：ProcessView 用到的 UI 分册资源只在本次渲染期间临时并入（出图毕即摘，A-E 的画布不受影响）。
        /// 并入的两册都要：Button.xaml（ToggleButton 基础样式）与 List.xaml——
        /// ExpandCollapseToggleStyle 已从 Button.xaml 搬到 List.xaml（2026-10-09 分册整理），
        /// 只并前者会让 ProcessView 在解析期抛"找不到 ExpandCollapseToggleStyle"（场景 F 整体出不了图）。
        /// DataContext 用最小桩：树只读 Workspace.CurrentFlow.Steps，drag-drop/命令绑定缺失时静默空跑。
        /// </summary>
        private static void RenderTree(WorkspaceContext workspace, string path, double width, double height)
        {
            var themeControls = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/Button.xaml"),
            };
            var themeList = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Controls/List.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(themeControls);
            Application.Current.Resources.MergedDictionaries.Add(themeList);
            try
            {
                var stub = new TreeProbeViewModel(workspace);
                Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(t =>
                    t == typeof(ProcessViewModel) ? stub : Activator.CreateInstance(t)!);

                var view = new ProcessView();
                var host = new Border
                {
                    Width = width,
                    Height = height,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = view,
                };
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                PumpFrames(6);

                // 根项默认收起：像真机那样层层展开，分支胶囊才可见（展开会生成子容器，多跑几轮收敛）
                for (int pass = 0; pass < 4; pass++)
                {
                    ExpandAll(view);
                    host.UpdateLayout();
                    PumpFrames(2);
                }

                // 分支卡片点击拦截自检（三条差异判据，事件回放到"树入口"）：PreviewMouseLeftButtonDown
                // 是 Direct 事件（按钮专用事件按元素逐个 crack，合成事件只能投给挂处理器的那个元素），
                // 所以按"处理器在树上看到的那份 args"回放：Source 指向被点元素。
                //  ① 分支卡片文字 → 必须被拦（不选中、不派发双击）；
                //  ② 分支卡片上的展开箭头（ToggleButton）→ **必须放行**（2026-10-09 审查抓到的回归：
                //     箭头就在分支卡片自己的容器模板里，判据若不豁免它，鼠标就点不开分支）；
                //  ③ 普通算子卡片 → 必须不拦（防误拦）。
                var tree = FindDescendant<TreeView>(view, _ => true);
                var branchText = FindDescendant<TextBlock>(
                    view,
                    d => d is TextBlock tb && tb.DataContext is StepCollection);
                var stepText = FindDescendant<TextBlock>(
                    view,
                    d => d is TextBlock tb && tb.DataContext is ActionStep);
                if (tree != null && branchText != null)
                {
                    var branchItem = FindVisualParent<TreeViewItem>(branchText);
                    bool branchBlocked = ReplayLeftDown(tree, branchText);
                    var expander = branchItem == null ? null : FindDescendant<ToggleButton>(branchItem, _ => true);
                    bool arrowBlocked = expander != null && ReplayLeftDown(tree, expander);
                    bool stepBlocked = stepText != null && ReplayLeftDown(tree, stepText);
                    Console.WriteLine(
                        $"[render][check] 分支卡片被拦截={branchBlocked}（期望 True）；分支箭头被拦截={arrowBlocked}（期望 False）；算子卡片被拦截={stepBlocked}（期望 False）");

                    // 分支级右键菜单自检（2026-10-09：分支结构编辑从参数面板下沉到流程栏右键菜单）：
                    //  ① 并行分组的分支胶囊自带菜单：两项、文案对、命令绑对、靶绑的是
                    //     PlacementTarget.DataContext（PlacementTarget = 胶囊 Border，其 DataContext 就是那条分支）；
                    //  ② 右键语义：胶囊上的右键不再被 Handled 拦（让 ContextMenuService 开胶囊那份菜单），
                    //     非并行分组的分支卡片仍被拦（放行只会误开父级菜单）。
                    var capsule = FindVisualParent<Border>(branchText);
                    var menu = capsule?.ContextMenu;
                    var menuItems = menu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();
                    var renameItem = menuItems.FirstOrDefault();
                    var removeItem = menuItems.Count > 1 ? menuItems[1] : null;
                    bool menuOk =
                        menu != null && menuItems.Count == 2
                        && (renameItem?.Header as string) == "重命名本条分支"
                        && (removeItem?.Header as string) == "删除本条分支"
                        && CommandPath(renameItem) == "RenameParallelBranchCommand"
                        && CommandPath(removeItem) == "RemoveParallelBranchCommand"
                        && ParameterPath(renameItem) == "PlacementTarget.DataContext"
                        && ParameterPath(removeItem) == "PlacementTarget.DataContext"
                        && capsule?.DataContext is StepCollection; // 靶机制落点：胶囊的 DataContext 就是这条分支
                    bool capsuleRightBlocked = ReplayRightUp(tree, branchText);

                    // 反向：非并行分组的分支胶囊（If/For/While 的分支）没挂菜单，右键仍被拦
                    var otherCapsuleText = FindDescendant<TextBlock>(
                        view,
                        d => d is TextBlock tb
                             && tb.DataContext is StepCollection
                             && FindVisualParent<Border>(tb)?.ContextMenu == null);
                    bool otherRightBlocked = otherCapsuleText != null && ReplayRightUp(tree, otherCapsuleText);
                    Console.WriteLine(
                        $"[render][check] 分支胶囊菜单={menuOk}（期望 True：项={menuItems.Count} 靶绑定={ParameterPath(renameItem)} DataContext={(menu?.DataContext == null ? "null" : "非空")}）；"
                        + $"右键胶囊被拦截={capsuleRightBlocked}（期望 False，放行开胶囊菜单）；右键非并行分支被拦截={otherRightBlocked}（期望 True{((otherCapsuleText == null) ? "，但未取到非并行分支胶囊" : "")}）");

                    // 组头菜单「添加分支」（同一功能的另一半）：靶是"当前选中的组头"（SelectStep），
                    // 只在选中项是并行分组时可见——与 If 的「添加 ElseIf/Else」同一口径。
                    var addItem = tree.ContextMenu?.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => (m.Header as string) == "添加分支");
                    string addVisibility = BindingPath(addItem, MenuItem.VisibilityProperty);
                    bool groupMenuOk = addItem != null
                        && CommandPath(addItem) == "AddParallelBranchCommand"
                        && ParameterPath(addItem) == "SelectStep"
                        && addVisibility == "IsParallelNodeSelected";
                    Console.WriteLine(
                        $"[render][check] 组头菜单「添加分支」={groupMenuOk}（期望 True：命令={CommandPath(addItem)} 靶={ParameterPath(addItem)} 可见性={addVisibility}）");
                }
                else
                {
                    Console.WriteLine("[render][check] 未找到流程树/分支卡片文字元素（场景 F 图式变了？）");
                }

                SavePng(host, path);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(themeList);
                Application.Current.Resources.MergedDictionaries.Remove(themeControls);
            }
        }

        private static void ExpandAll(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TreeViewItem item)
                    item.IsExpanded = true;
                ExpandAll(child);
            }
        }

        /// <summary>把左键按下事件投到树上（Source 指向被点元素），返回处理器有没有把它吃掉。</summary>
        private static bool ReplayLeftDown(TreeView tree, DependencyObject source)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = source,
            };
            tree.RaiseEvent(args);
            return args.Handled;
        }

        /// <summary>
        /// 把右键抬起事件投到树上（Source 指向被点元素），返回处理器有没有把它吃掉。
        /// 视图的右键处理器挂在 PreviewMouseRightButtonUp 上（方法名叫 ...RightButtonDown，历史命名，别被误导）；
        /// 这类"按键专用"事件是 Direct 路由，合成事件只能投给挂处理器的那个元素（= 树），Source 声明被点处。
        /// </summary>
        private static bool ReplayRightUp(TreeView tree, DependencyObject source)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            {
                RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent,
                Source = source,
            };
            tree.RaiseEvent(args);
            return args.Handled;
        }

        /// <summary>取元素上某属性的 Binding 路径（没绑或绑的不是 Binding 就返回空串）。</summary>
        private static string BindingPath(DependencyObject target, DependencyProperty property)
            => target == null ? string.Empty : BindingOperations.GetBinding(target, property)?.Path?.Path ?? string.Empty;

        /// <summary>菜单项 Command 的绑定路径（期望是 VM 上的命令名）</summary>
        private static string CommandPath(MenuItem item) => item == null ? string.Empty : BindingPath(item, MenuItem.CommandProperty);

        /// <summary>菜单项 CommandParameter 的绑定路径（期望是 PlacementTarget.DataContext = 命中分支）</summary>
        private static string ParameterPath(MenuItem item)
            => item == null ? string.Empty : BindingPath(item, MenuItem.CommandParameterProperty);

        /// <summary>深度优先找第一个命中的后代元素（流程栏自检用）。</summary>
        private static T? FindDescendant<T>(DependencyObject root, Func<DependencyObject, bool> match)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit && match(child))
                    return hit;
                var found = FindDescendant<T>(child, match);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>沿视觉树向上找指定类型的祖先（源可能是文本、图标等深层元素）。</summary>
        private static T? FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && source is not T)
                source = VisualTreeHelper.GetParent(source);
            return source as T;
        }

        /// <summary>流程栏（ProcessView）的最小 DataContext：树只绑 Workspace.CurrentFlow.Steps。</summary>
        private sealed class TreeProbeViewModel
        {
            public TreeProbeViewModel(WorkspaceContext workspace) => Workspace = workspace;

            public WorkspaceContext Workspace { get; }
        }

        /// <summary>
        /// 场景 G：并行分组参数面板——标准属性面板（FlatPropertyGrid + ParallelGroupEditModel）。
        /// 2026-10-09 用户裁决"没必要新建一个视图"后，专属视图 ParallelGroupConfigView 已删除，
        /// 出图对象改为 EasyDialog.ShowPropertyGridSync 弹窗里的那个 FlatPropertyGrid 本体
        ///（弹窗自身是 Popup/Window，RenderTargetBitmap 出不了它，出属性网格控件即所见内容）。
        /// 主题字典并入方式照旧：临时并入 /UI;component/Themes/Generic.xaml，出图毕即摘。
        /// </summary>
        private static void RenderScenarioG(string path)
        {
            var h = new Harness();
            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "左右工位并行")
            {
                ExecutionMode = ParallelExecutionMode.Parallel,
                FailFastMode = FailFastMode.On,
                JoinTimeoutMs = 8000,
            };
            par.Children[0].StepName = "左工位";
            par.Children[1].StepName = "右工位";
            par.Children[0].Steps.Add(new ActionStep("\uE73E", "尺寸测量", StubPluginProvider.LeafPlugin, "左工位测量_0"));
            par.Children[1].Steps.Add(new ActionStep("\uE711", "缺陷检测", StubPluginProvider.LeafPlugin, "右工位检测_0"));
            h.Add(par);

            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                // 与 StepParameterDialog 的 ParallelStep 分支同款构造（EasyDialog 弹窗里就是这么建的）
                var edit = new ParallelGroupEditModel(par);
                var grid = new UI.CustomControl.FlatPropertyGrid
                {
                    BindingObject = edit,
                    MinWidth = 450,
                    MaxHeight = 600,
                };
                var host = new Border
                {
                    Width = 760,
                    Height = 640,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Child = grid,
                };
                host.Measure(new Size(760, 640));
                host.Arrange(new Rect(0, 0, 760, 640));
                host.UpdateLayout();
                PumpFrames(6);

                SavePng(host, path);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        }

        private static void Render(Harness h, string path, double width, double height, bool showDataLinks = false)
        {
            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            // 数据线默认隐藏：需要"打开态"对照图时显式打开（setter 自带重建）
            if (showDataLinks)
                canvas.ShowDataLinks = true;

            RenderCore(canvas, path, width, height);
        }

        /// <summary>
        /// 场景 H：并行分组 4 条分支、只有分支 1 有内容——真机截图"流程画布太乱"的复原图式。
        /// 分支 1 里串起 If_0（未绑定条件、两分支空）→ For_0（循环体空）→ While_0（循环体未绑定条件）→ Break_0；
        /// 分支 2~4 全空。顶层：图像采集_0 → 胶路检测_0 → 并行分组_0 → 结果汇总_0。
        ///
        /// 旧布局（固定行高 90 / 列距 260）下这里必然互压：If 框宽 356 + 两侧内边距，右缘越过下一列起点 200px 以上，
        /// 直接压进分支 2 的泳道；3 条空泳道又统一贴到内容左上角、同址重叠在嵌套容器上。
        /// 修复后：4 条泳道按列依次排开，嵌套框完整落在分支 1 泳道内，框体/泳道/内容三层按深度叠放。
        /// </summary>
        private static void RenderScenarioH(string path)
        {
            var h = new Harness();
            h.Flow.FlowName = "并行分组·泳道内嵌套（真机图式）";

            var capture = new ActionStep("\uE722", "图像采集", StubPluginProvider.LeafPlugin, "图像采集_0")
            {
                State = StepState.Success,
                Description = "车间工位 3 面阵相机",
            };
            var bead = new ActionStep("\uE7C1", "胶路检测", StubPluginProvider.LeafPlugin, "胶路检测_0")
            {
                State = StepState.Running,
                Description = "左工位胶路宽度/高度",
            };

            var par = new ParallelStep("\uF0F7", "并行分组", "BuiltIn_Parallel", "并行分组_0")
            {
                Description = "4 工位并行，跑完汇合",
            };
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 3" });
            par.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 4" });

            // 分支 1 = 真机图式：If（未绑定条件、两分支空）→ For（循环体空）→ While（循环体未绑定条件）→ Break
            var ifStep = new ConditionStep("\uE7C1", "结果判断", StubPluginProvider.IfPlugin, "If_0");
            var forStep = new ForStep("\uE722", "循环", StubPluginProvider.ForPlugin, "For_0");
            var whileStep = new WhileStep("\uE722", "条件循环", StubPluginProvider.IfPlugin, "While_0");
            var breakStep = new BreakStep("\uE711", "跳出循环", "VM.CanvasStub.Break", "Break_0");
            par.Children[0].Steps.Add(ifStep);
            par.Children[0].Steps.Add(forStep);
            par.Children[0].Steps.Add(whileStep);
            par.Children[0].Steps.Add(breakStep);

            var summary = new ActionStep("\uE73E", "数据汇总", StubPluginProvider.LeafPlugin, "结果汇总_0");

            h.Add(capture);
            h.Add(bead);
            h.Add(par);
            h.Add(summary);

            Harness.RawLink(bead, "In", capture, "Out");
            Harness.RawLink(par, "In", bead, "Out");
            Harness.RawLink(ifStep, "In", capture, "Out");      // 顶层采集 → 分支内层：合法（进入前已有值）

            var canvas = new FlowCanvasViewModel(h.Workspace, new StubPluginProvider());
            WireViewModelFactory(canvas);

            var host = RenderCore(canvas, path, 1400, 1200);

            // 层次自检（不依赖视觉树，直接用 VM 的层次值）：
            // 嵌套容器框必须压在兄弟泳道之上——旧实现靠集合序，后入分支的泳道会盖住先入分支里的嵌套框
            var nestedFrame = canvas.Nodes.First(n => n.Model == ifStep);
            var siblingLane = canvas.Nodes.First(n => n.Kind == CanvasNodeKind.Lane && ReferenceEquals(n.Branch, par.Children[1]));
            Console.WriteLine(
                $"[render][check] 嵌套框 ZOrder={nestedFrame.ZOrder} > 兄弟泳道 ZOrder={siblingLane.ZOrder}（期望 True）= {nestedFrame.ZOrder > siblingLane.ZOrder}");

            ProbeItemZIndex(host);
            ProbeCanvasEditingEntries(host);
        }

        /// <summary>
        /// 画布"主编辑面"入口自检（视图侧证据）：
        ///  ① 节点右键菜单里有「模块参数…」且命令绑到 OpenModuleParametersCommand（与双击同一入口）；
        ///  ② 容器头带的折叠钮带约定的 x:Name=ContainerCollapseToggle —— 双击豁免的判据就是它，
        ///     改名（或删名）会让"双击折叠钮弹参数窗"的回归悄悄回来（R25 家族的教训）；
        ///  ③ 双击命中路由（反射直调 FlowCanvasView.FindNodeFromSource，与真机同一条代码）：
        ///     算子卡片 → 节点 VM；折叠钮内文字 / 连接点 → null（豁免，别把箭头与拖线抓手吃掉）。
        /// </summary>
        private static void ProbeCanvasEditingEntries(DependencyObject host)
        {
            var menus = new List<ContextMenu>();
            CollectNodeMenus(host, menus);
            var parameterItem = menus
                .SelectMany(m => m.Items.OfType<MenuItem>())
                .FirstOrDefault(m => (m.Header as string) == "模块参数…");
            var foldBorder = FindDescendant<FrameworkElement>(
                host, d => d is FrameworkElement { Name: "ContainerCollapseToggle" });

            // 双击命中路由：反射调视图的私有静态判定（断言宿主拿不到 internal，与 CheckLinkOrder 同款手法）
            var findNode = typeof(FlowCanvasView).GetMethod(
                "FindNodeFromSource", BindingFlags.Static | BindingFlags.NonPublic);
            var stepText = FindDescendant<TextBlock>(
                host, d => d is TextBlock tb && tb.DataContext is CanvasNodeViewModel { Kind: CanvasNodeKind.Step });
            var foldChild = foldBorder == null ? null : FindDescendant<TextBlock>(foldBorder, _ => true);
            var connector = FindDescendant<Connector>(host, _ => true);

            var hitStep = findNode?.Invoke(null, new object?[] { stepText }) as CanvasNodeViewModel;
            var hitFold = findNode?.Invoke(null, new object?[] { foldChild }) as CanvasNodeViewModel;
            var hitConnector = findNode?.Invoke(null, new object?[] { connector }) as CanvasNodeViewModel;

            Console.WriteLine(
                $"[render][check] 画布节点菜单「模块参数…」存在={parameterItem != null}（期望 True）；"
                + $"命令绑定={CommandPath(parameterItem)}（期望 OpenModuleParametersCommand）；"
                + $"折叠钮 x:Name=ContainerCollapseToggle 存在={foldBorder != null}（期望 True，双击豁免判据）");
            Console.WriteLine(
                $"[render][check] 双击命中路由：算子卡片→{(hitStep != null ? "节点 VM（开参数）" : "null")}（期望节点 VM）；"
                + $"折叠钮内文字→{(hitFold == null ? "null（豁免）" : "节点 VM（回归！）")}（期望 null）；"
                + $"连接点→{(hitConnector == null ? "null（豁免）" : "节点 VM（回归！）")}（期望 null）");
        }

        /// <summary>收集视觉树里出现过的 ContextMenu（节点模板内联菜单 = 每实例一份）</summary>
        private static void CollectNodeMenus(DependencyObject root, List<ContextMenu> into)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement { ContextMenu: { } menu } && !into.Contains(menu))
                    into.Add(menu);
                CollectNodeMenus(child, into);
            }
        }

        /// <summary>
        /// Panel.ZIndex 绑定自检：ItemContainer 上的 ZIndex 必须等于节点 VM 的 ZOrder（XAML 绑定真的生效），
        /// 且 items 宿主必须是 Panel（Panel.ZIndex 只对 Panel 的子元素排序）。
        /// </summary>
        private static void ProbeItemZIndex(DependencyObject host)
        {
            var items = new List<ItemContainer>();
            CollectDescendants(host, items);

            int mismatched = items.Count(c =>
                c.DataContext is CanvasNodeViewModel vm && Panel.GetZIndex(c) != vm.ZOrder);
            var panel = items.Count > 0 ? VisualTreeHelper.GetParent(items[0]) as Panel : null;

            Console.WriteLine(
                $"[render][check] 节点容器={items.Count}；Panel.ZIndex 与 VM.ZOrder 一致={items.Count > 0 && mismatched == 0}"
                + $"（不一致 {mismatched} 个）；items 宿主={panel?.GetType().Name ?? "?"}（Panel 才吃 Panel.ZIndex）");
        }

        /// <summary>深度优先收集全部命中的后代元素（ZIndex 自检用）</summary>
        private static void CollectDescendants<T>(DependencyObject root, List<T> into) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) into.Add(hit);
                CollectDescendants(child, into);
            }
        }

        /// <summary>FlowCanvasView 的 XAML 带 AutoWireViewModel=True 且在构造期触发——VM 无无参构造，
        /// 用 Prism 的兜底工厂钩子返回预建实例（Register 工厂在 Prism 9 的解析路径里被默认实现抢先）。</summary>
        private static void WireViewModelFactory(FlowCanvasViewModel canvas)
        {
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(t =>
                t == typeof(FlowCanvasViewModel)
                    ? canvas
                    : Activator.CreateInstance(t)!);
        }

        private static Border RenderCore(FlowCanvasViewModel canvas, string path, double width, double height)
        {
            var view = new FlowCanvasView();

            var host = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Child = view,
            };

            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            PumpFrames(8);

            var editor = FindEditor(view);
            if (editor != null)
            {
                editor.FitToScreen(null);
                if (editor.ViewportZoom > 1.0)
                    editor.ViewportZoom = 1.0;
                host.UpdateLayout();
                PumpFrames(6);
            }

            // 连线自检行：数据线/顺序链/非法/降级/折叠隐藏 五个计数，
            // 出图时一并落 stdout——"图上有告警条但没人知道是哪来的"属可诊断性缺口
            Console.WriteLine(
                $"[render][diag] {Path.GetFileName(path)} 数据线={canvas.DataLinkCount} 顺序链={canvas.OrderLinkCount}"
                + $" 非法={canvas.IllegalLinkCount} 降级={canvas.DeferredLinkCount} 折叠隐藏={canvas.HiddenLinkCount}");

            SavePng(host, path);
            return host;
        }

        /// <summary>把已布局的宿主逐像素渲染成 PNG（画布与流程栏出图共用）。</summary>
        private static void SavePng(FrameworkElement host, string path)
        {
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(host.ActualWidth),
                (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path))
                encoder.Save(fs);

            Console.WriteLine($"[render] {path}");
        }

        private static void PumpFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        private static NodifyEditor? FindEditor(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is NodifyEditor editor) return editor;
                var found = FindEditor(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
