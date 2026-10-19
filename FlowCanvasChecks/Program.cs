using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using Core.Events;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程画布（海康式一图全展开）的非 GUI 断言宿主。
    ///
    /// 为什么单独开一个工程：FlowCanvasViewModel 挂在 VisionMaster（WinExe）里，
    /// 而它的全部对外契约（Nodes / Links / 命令 / 计数）都是 public，
    /// 不需要为测试在产品程序里塞一个「自检开关」污染启动路径；
    /// 唯一的 internal 目标（FlowCompiler.CheckLinkOrder / PushUndo）用反射直调，
    /// 这与 CommTest 反射 HslHelper 的既有做法一致。
    ///
    /// 能这么跑的前提：FlowCanvasViewModel 及其依赖链不碰 Application.Current / Dispatcher，
    /// GlobalEventBus.Publish 也是同步 Invoke。所以控制台进程里 new 出来就是安全的。
    ///
    /// 画布 v2（扁平化）与旧断言的差异：
    ///   · 不再断言下钻/面包屑/层根——整棵树摊在同一张画布上，这些概念已删除；
    ///   · 连线断言从「端口级一根线」改为「模块级聚合线」（同一对模块的多条绑定合并）；
    ///   · 全局变量/常量绑定从「降级线」改为「节点角标」；
    ///   · 新增折叠/展开、容器框几何、解绑撤销、隐藏绑定计数等断言面。
    /// </summary>
    internal class Program
    {
        private static int _pass, _fail;

        private static int Main(string[] args)
        {
            // 离线工具模式：VMTOOL_VMSIF=<方案路径> → 向 MainTask 注入一个 If 容器（带条件表达式与空分支）后回写。
            // 用途：给 UI 实测准备"带逻辑分支"的图纸（画布容器框/泳道/折叠的实机验证素材），并兼验落盘往返链路。
            if (args.Length >= 2 && args[0] == "--inject-if")
            {
                InjectIfIntoMainTask(args[1]);
                return 0;
            }

            // 性能基准模式：--perf 只跑 A1/A2 基准，不跑常规断言（计时数字会抖，混进来会让"红了"说不清原因）。
            // 必须 Release 构建跑（dotnet build -c Release），Debug 的 JIT 不做优化，数字失真。
            if (args.Length >= 1 && args[0] == "--perf")
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                PerfChecks.Run();
                Finish();
                return Environment.ExitCode;
            }

            // 渲染探针模式：--render [输出目录] 把画布典型图式渲染成 PNG（视觉整改的前后对比素材）。
            // 逻辑断言绿 ≠ 视觉过关——2026-10-07 用户看真机截图直接指出连线形态问题，视觉必须出图。
            if (args.Length >= 1 && args[0] == "--render")
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                RenderProbe.Run(args.Length >= 2 ? args[1] : Path.GetTempPath());
                return 0;
            }

            // 逻辑暴力测试加力档：--brute [N] 只跑 B0 随机图对拍（默认 2000 张），不进常规 gate。
            // 定种子：同一条命令跑出来的图与结果完全确定（finding 的图可由 seed+i 复现）。
            if (args.Length >= 1 && args[0] == "--brute")
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                int bruteN = args.Length >= 2 && int.TryParse(args[1], out var parsed) ? parsed : 2000;
                LogicBruteforceChecks.Run(bruteCount: bruteN, randomOnly: true);
                Finish();
                return Environment.ExitCode;
            }

            // 码读取 flaky 调查：--qrprobe [N] 混合图 QR 解码连跑 N 次（默认 10）统计失败率，
            // 不进常规 gate、不做重试——就是要看原始引擎的稳定性（评审清单 B 包第 8 项的调查工具）
            if (args.Length >= 1 && args[0] == "--qrprobe")
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                int probeN = args.Length >= 2 && int.TryParse(args[1], out var probeParsed) ? probeParsed : 10;
                CodeReaderChecks.RunMixedProbe(probeN);
                return 0;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("========== 流程画布 v2（海康式扁平画布）断言（容器框/泳道/模块连线/折叠/联动/撤销栈） ==========");

            EmptyWorkspaceSafety();
            FlatRenderAndOrder();
            LaneAndFrameGeometry();
            CollapseAndExpand();
            ModuleLinkAggregation();
            LinkLegalityAndCounts();
            LinkGestureAndRejection();
            TopologyVersusCompiler();
            BranchDisplayNameNotification();
            NodePoolKeepsSelection();
            LayoutWriteBackDiscipline();
            OutsideSelectionDrivesCanvas();
            DragReorderAndVersion();
            CrossBranchMoveAndUndo();
            UnbindUndoRedo();
            UndoStackBoundary();
            RuntimeVariableModuleLinks();
            NodifyRealParameterShapes();
            DeleteDisableAndUndoSync();
            TenLevelNestingStress();

            // [X] 数据线显隐（默认隐藏 / 非法线例外）/ 画布编辑入口 / 工具箱落点与工厂
            DataLinkVisibilityAndCanvasEditing();

            // 2026-10-11 三视角评审收口第一批 + 第二批：
            // [G1] 画布编辑门禁（EditGate 豁免表 + 画布拦截点）
            // [G2] 撤销补口（引用型 Add/Delete 命令：拖入与删除可撤销、删除不清栈）
            // [G3] 流程栏搜索定位（含「上一个」，评审清单 B 包交互补齐）
            // [G4] 失败自动跳转（含开关）/ [G5] 展开·折叠全部 / [G6] 编译错误事件（分组合并）
            EditGateChecks();
            AddDeleteUndoChecks();
            SearchLocateChecks();
            FailureJumpChecks();
            CollapseExpandAllChecks();
            CompileErrorEventChecks();
            RunStatsChecks();

            // 2026-10-12 评审清单第三批：
            // [G7] 画布复制/粘贴（FlowClipboard：单节点深拷贝 + 五条重映射纪律 + 撤销/运行锁）
            // [G8] 画布压力图式构造（300/600 节点；性能只量化不优化，[perf] 数字见 --render）
            // [G9] 工具箱搜索过滤（ToolViewModel，评审清单 B 包交互补齐）
            FlowClipboardChecks();
            PerfGraphChecks();
            ToolboxFilterChecks();

            // 2026-10-12 评审清单 B 包第二批（画布可视化 + 几何/持久化/稳定性）：
            // [Z1] 泳道执行染色 / [Z2] 顺序链已执行染色 / [Z3] 连线绑定参数名标签 /
            // [Z4] 必填参数黄叹号 / [Z5] 拖动对齐参考线 / [Z7] 视图态持久化口径
            CanvasVisualizationChecks.Run();

            ParallelContainerChecks.Run();

            // 流程栏树模板覆盖守门（U1-U5：类型键模板缺 ParallelStep → "VisionMaster.Models.ParallelStep" 上屏事故）
            ProcessTreeTemplateChecks.Run();

            // 流程栏分支卡片点击守门（V1-V5：双击分支弹"上一个算子"参数窗 / 弹空白条件窗事故）
            ProcessTreeInteractionChecks.Run();

            // 并行分组配置面板（W1-W7：执行模式/失败聚合/汇合超时/分支增删的草稿→校验→写回）
            ParallelGroupConfigChecks.Run();

            // 流程引擎真并行执行二期（ExecutionMode/FailFast/门禁/影子合并/退化矩阵，P1-P30）
            ParallelExecutionChecks.Run();

            // 逻辑功能暴力测试（B0 随机图 × 参考解释器对拍 / B1 判断真值表 / B2 循环 / B3 并行 / B4 分支拓扑）
            LogicBruteforceChecks.Run();

            // 执行层断言（流程引擎优化 A1/A2/A3/B1/B2/B3）
            ExecutionChecks.IfInsideLoopExecutes();
            ExecutionChecks.WhileLoopRunsAndExits();
            ExecutionChecks.ContainerStatesReportedAndBlueprintLinked();
            ExecutionChecks.DuplicateStartIsIgnored();
            ExecutionChecks.RuntimeStateDoesNotInvalidateVersion();
            ExecutionChecks.RegistryLockDoesNotBlockCollection();
            ExecutionChecks.ForLoopCountReadsRuntimeVariable();
            ExecutionChecks.ConditionBranchFailureSemantics();
            ExecutionChecks.SessionLockAlwaysReturned();
            ExecutionChecks.CompileTimeSilentFailuresAreReported();
            ExecutionChecks.SessionStateTransitionSemantics();
            ExecutionChecks.ContractHygieneAndStepCollection();

            // 等待节点（BuiltIn_Wait）：编译门禁 / 先求值后等待 / 轮询 / 超时两种处置 / 输出端口 / 停止
            WaitNodeChecks.Run();

            // S3：FlowModel 版本链（嵌套 / 后加分支 / 改序 / 清空 / 替换集合 / 反序列化往返后订阅重扫）
            FlowModelVersionChecks.Run();

            // 调用方式（位集 + 迁移 + HTTP 门禁判定）与强制骨架流程（Home / Main / End）
            FlowInvokeChecks.Run();

            // 调用方式运行侧三位：定时调度 / 变量触发 / 子程序调用（含「调用流程」插件端到端）
            FlowAutomationChecks.Run();

            // 引擎全面审查·第一批修复的断言（收图槽回收 / 退出链超时 / 绑定写回 / 脚本引用过滤 / 运行中守卫）
            FlowEngineFixChecks.Run();

            // 引擎全面审查·第二批修复的断言（条件变量未绑定 / FlowID 新鲜度 / 锁定门禁 / 会话准备收口）
            FlowEngineFix2Checks.Run();

            // 逻辑分支容器四项修复（For 上限 / 容器失败上浮 / Break 状态 / 循环体分支数校验）
            FlowContainerChecks.Run();

            // 分支匹配（Case）容器：首中即选 / 默认分支兜底 / 判据类型归一 / 编译期硬错 / 判据版本链
            CaseBranchChecks.Run();

            // DWV 第 1 期：断点 / 单步 / 暂停继续（调试门；断点不落盘、HTTP / 试运行豁免）
            DebugChecks.Run();

            // OCR：字符分割 + 识别（真图 + 真模型）
            OcrChecks.Run();
            CodeReaderChecks.Run();
            YoloChecks.Run();

            // 深度学习分类：缩放几何 / 布局嗅探 / softmax 归一化 / TopK + 端口契约
            ClassifyChecks.Run();

            BlobDetectChecks.Run();
            PreProcessingChecks.Run();
            PortBindChecks.Run();
            GlobalVariableChecks.Run();
            MotionChecks.Run();
            MotionZMotionChecks.Run();
            MatchingChecks.Run();
            CreateRoiChecks.Run();

            // 胶路检测插件：路径生成 / bead 模型 / 7 图真值表（P4）/ 树直径合并 / 下限保护 / 贴合度
            //（真样图 Image\bead\adhesive_bead_01..07.png，断言 1~9、13、14、15）
            BeadInspectChecks.Run();

            // HTTP 收图端到端冒烟（真起服务端 + 真发 HTTP 图片）
            HttpImageSmoke.Run();

            // 线序颜色检测方案端到端（真加载 .vms + 真编译 + 真执行 + 断言线序）
            WireSequenceCheck.Run();

            // 变量绑定窗口：回显当前值 / 换端口不串值
            VariableBindingCheck.Run();

            // 变量赋值：运行时 / 全局两种作用域
            VariableAssignmentCheck.Run();

            // 颜色序列检查插件（第一刀）：ROI 采样区的画框 / 存盘 / 回填
            ColorCheckRoiCheck.Run();

            // 颜色序列检查插件（第二刀 / 第三刀）：自适应找线 → 配方判定 → 结果投射
            ColorCheckSequenceCheck.Run();

            // 区域颜色检查算子（第二个颜色算子，兼验颜色内核真的通用）
            ColorRegionCheck.Run();

            // 线序检测·插件版方案端到端（真加载 .vms + 真编译 + 真执行 + 断言结果与投射）
            WireSequencePluginCheck.Run();

            // 合成复杂颜色图生成器 + 流程式验证（采集 → 序列 → 区域×2，含 16 位高位深归一链路）
            ColorSyntheticChecks.Run();

            // 海康相机驱动：Modules 发现契约 + 缺 SDK 优雅降级 + 像素格式分类
            HikvisionChecks.Run();

            // 图像采集插件：端口面 + 原始像素→HImage（灰度/BGR/BGRA/守卫）+ 文件夹自然序
            ImageAcquisitionChecks.Run();

            // 预览帧捕获时机：发布线程同步拷贝（循环运行"图不被收纳"的回归）
            PreviewCaptureChecks.Run();

            // 缩略图快路径：先缩小再转换（长宽比/不放大/细线条可见/不改源图/量程回落）
            ThumbnailChecks.Run();

            // 共享控件库 Core.Halcon（P0-1/2/3 修复回归：DisplayImageInfo 实例隔离 /
            // 无图关十字不抛 / 十字状态与显示不脱节）
            HalconControlChecks.Run();

            // 共享控件库 Core.Halcon 的 ROI 画布交互（拖拽改大小：冷启动一次手势即可拖、
            // 偏出手柄不丢选中、点空白结束编辑、内部拖为移动；自带屏幕外真窗口 + HALCON 鼠标事件链路）
            RoiInteractionChecks.Run();

            // 共享离屏烘焙件 Core.Halcon.Helpers.AnnotationBaker（六条硬纪律的像素级/内存级回归网：
            // SetPart 画布保真 / 文字底色框 / 跨轮无残留 / 非 byte 不死白 / 4 通道不抛 / 坏元素降级 / 500 轮不泄漏）
            AnnotationBakerChecks.Run();

            // 标定插件：仿射求解/正反变换/质量闸门/端口面（合成数据，不依赖引擎）
            CalibrationChecks.Run();

            // 卡尺插件：像素当量直连「标定」（手填/上游/失效三态 + 真实测量链路）
            CaliperMeasureChecks.Run();

            // 几何工具：GeometryMath 纯函数 + GeometryParam 契约（合成数据，不依赖引擎/HALCON）
            GeometryChecks.Run();

            // 几何工具演示方案：真加载 .vms → 真编译 → 真执行（几何查找/几何计算端到端）
            GeoDemoCheck.Run();

            // 结果显示插件：绘制计划级（行主序/角度取反/预算/降级）+ 像素级（青色圆环/椭圆长短轴符号/文字底色框）
            // + 配置生命周期（OnConfirm→ApplyConfigValues 往返、动态端口增删、候选重算）+ 宁缺显示不停线
            // + 缩略图/导出 + 6 条静态扫描（离屏纪律不外泄）+ 真加载「结果显示演示.vms」端到端
            ResultDisplayCheck.Run();

            // 坐标变换插件：正反变换/角度/三类失配/位姿跟随（合成标定与合成区域，HALCON 真算子）
            PoseTransformChecks.Run();

            // 插件帮助手册：打包 / 按类型分册 / 页名契约 / 编码与体量（纯静态扫描，见 HelpManualCheck 类注释）
            HelpManualCheck.Run();

            // 停靠面板可见性恢复的唯一入口（AvalonDock 4.74.1 的 Show() 缺索引下界 → 真机闪退；
            // 运行时那 5 条在 UIThemeSmokeTest.RunPanelModeChecks，这里守"入口别被绕开"）
            LayoutVisibilityCheck.Run();

            // 图标字符按钮放进固定格子必须挂样式或显式 Padding（Fluent 隐式 Button 样式的 11,5,11,6
            // 内边距把 30×30 的「✕」切成残字符，真机三连报；编译期全绿，只有静态算术闸拦得住）
            GlyphButtonClipCheck.Run();

            // 属性网格的多态刷新 = 整表重建（RequireRefresh 的契约；曾被改成"只重建单页签"，
            // 导致"切换字段"与"被换掉的子对象"不在同一页签时界面永久停在旧协议字段上）
            PropertyGridRefreshCheck.Run();

            // 插件清单缓存（层 3 启动提速）：指纹失效三态 / 缓存产物与全量扫描逐项一致 /
            // 构造器无副作用前提（缓存路径跳过实例化，这个前提一破就必须当场红）
            PluginCacheChecks.Run();

            // 内参标定（标定板内参标定，二期③）：契约良构 + DTO 往返（纯托管）
            // + 合成自验证（sim_caltab 已知内参 → 完整标定链 → 比对恢复值）
            // + 去畸变正/反/往返与两模型分档 + 端到端串联（含「跳过去畸变结果必须不同」的反证）。
            // 需 HALCONROOT\calib 的标定板描述文件；缺失时按 R38 明确跳过并写反证依据。
            IntrinsicsChecks.Run();

            Finish();
            return Environment.ExitCode;
        }

        // ==================================================================
        //  [A] 空工作区构造安全（无方案、无流程时不得抛，也不得留下脏状态）
        // ==================================================================
        private static void EmptyWorkspaceSafety()
        {
            Section("[A] 空工作区与切换重建");

            var bare = new FlowCanvasViewModel(new WorkspaceContext(), new StubPluginProvider());
            Check("空 workspace 构造不抛", true, "");
            Check("空态：Nodes / Links 皆空", bare.Nodes.Count == 0 && bare.Links.Count == 0,
                $"Nodes={bare.Nodes.Count} Links={bare.Links.Count}");
            Check("空态：HasFlow=false 且提示可见", !bare.HasFlow && bare.EmptyHintVisibility == Visibility.Visible,
                $"EmptyHint={bare.EmptyHintVisibility}");
            Check("空态：两个计数与告警条归零",
                bare.DeferredLinkCount == 0 && bare.IllegalLinkCount == 0 && bare.HiddenLinkCount == 0
                && bare.WarningVisibility == Visibility.Collapsed,
                $"deferred={bare.DeferredLinkCount} illegal={bare.IllegalLinkCount} hidden={bare.HiddenLinkCount}");

            // 先建图纸后开画布：构造时应当直接反映当前流程
            var h = new Harness();
            var a = h.Leaf("A");
            h.Add(a);
            Check("未开画布前不应触碰 Layout（不生成坐标）", h.Flow.Layout.Count == 0, $"Layout.Count={h.Flow.Layout.Count}");

            var c = h.Canvas;
            Check("打开画布即呈现当前流程", c.HasFlow, $"Steps={h.Steps().Count}");
            Check("标题取流程名", c.FlowTitle == h.Flow.FlowName, $"'{c.FlowTitle}'");

            // 切流程 → 走 CurrentFlow 通知自动重建
            var other = new FlowModel { FlowName = "辅助流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后画布清空（订阅挂在 WorkspaceContext 上）",
                c.Nodes.Count == 0 && !c.HasFlow, $"Nodes={c.Nodes.Count}");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后节点回来", c.Nodes.Any(n => n.Kind == CanvasNodeKind.Step && n.StepId == a.StepID), "");
        }

        // ==================================================================
        //  [B] 扁平渲染：全深度一图 + 执行序号 + 容器身份
        // ==================================================================
        private static void FlatRenderAndOrder()
        {
            Section("[B] 扁平渲染与执行序号");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            var outer = h.For("外层循环");
            var e1 = h.Leaf("E1");
            outer.Children[0].Steps.Add(e1);
            h.Add(a); h.Add(cond); h.Add(outer);

            var c = h.Canvas;
            Check("全深度步骤都上画布（顶层 + 分支内 = 3）", h.Steps().Count == 3, $"实际 {h.Steps().Count}");
            Check("容器渲染成框节点（If + For = 2）", h.Containers().Count == 2, $"实际 {h.Containers().Count}");
            Check("顶层步骤 = 只有 a（容器另算）",
                h.MainSteps().Count == 1 && h.MainSteps()[0].StepId == a.StepID, $"顶层 {h.MainSteps().Count}");

            Check("容器框带类型徽标：If / For",
                h.Node(cond)!.TypeBadge == "If" && h.Node(outer)!.TypeBadge == "For"
                && h.Node(a)!.TypeBadge == "",
                $"'{h.Node(cond)!.TypeBadge}'/'{h.Node(outer)!.TypeBadge}'");
            Check("泳道按引用挂住分支",
                h.Lane(ifBranch) != null && h.Lane(outer.Children[0]) != null, "");
            Check("有内容的分支不算空泳道", h.Lane(ifBranch)!.IsLaneEmpty == false, "");
            Check("If 分支条件为空时标题带『未绑定条件』",
                h.Lane(ifBranch)!.Header.Contains("未绑定条件"), $"'{h.Lane(ifBranch)!.Header}'");

            // 执行序号按所属集合下标 1 基：顶层 a=1, cond=2, outer=3；分支内独立从 1 起
            Check("顶层序号：a=1 / cond=2 / outer=3（容器参与执行序）",
                h.Node(a)!.OrderIndex == 1 && h.Node(cond)!.OrderIndex == 2 && h.Node(outer)!.OrderIndex == 3,
                $"{h.Node(a)!.OrderIndex},{h.Node(cond)!.OrderIndex},{h.Node(outer)!.OrderIndex}");
            Check("分支内步骤序号独立从 1 起",
                h.Node(b1)!.OrderIndex == 1 && h.Node(e1)!.OrderIndex == 1,
                $"{h.Node(b1)!.OrderIndex},{h.Node(e1)!.OrderIndex}");

            // ---- 执行顺序链：同集合相邻两步一条纵向顺序线（容器框参与，分支内部各自成链） ----
            var b2 = h.Leaf("B2");
            ifBranch.Steps.Add(b2);
            Check("顺序链：顶层 a→cond→outer 共 2 条 + 分支内 b1→b2 共 1 条",
                c.Links.Count(l => l.IsOrderLink) == 3
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)) && ReferenceEquals(l.Target, h.Node(cond))) == 1
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(cond)) && ReferenceEquals(l.Target, h.Node(outer))) == 1
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(b1)) && ReferenceEquals(l.Target, h.Node(b2))) == 1,
                "顺序链 " + c.Links.Count(l => l.IsOrderLink) + " 条: "
                + string.Join(" | ", c.Links.Where(l => l.IsOrderLink).Select(l => l.Source.Header + "→" + l.Target.Header)));
            var firstChain = c.Links.First(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)));
            Check("顺序链端点 = 上一步底缘中点 → 下一步顶缘中点（纵向流动）",
                Math.Abs(firstChain.SourceAnchor.X - (h.Node(a)!.Location.X + FlowCanvasViewModel.NodeWidth / 2)) < 0.01
                && Math.Abs(firstChain.SourceAnchor.Y - (h.Node(a)!.Location.Y + FlowCanvasViewModel.NodeHeight)) < 0.01
                && Math.Abs(firstChain.TargetAnchor.Y - h.Node(cond)!.Location.Y) < 0.01,
                $"src={firstChain.SourceAnchor} tgt={firstChain.TargetAnchor}");

            // ---- 就窄轴拉直（2026-10-10 真机反馈：容器与上级算子做不到居中对齐）----
            // 算子→容器：两锚点共享同一 X（= 算子中心轴），链是垂直线，容器再宽也不拽弯；
            // 上一步底缘中点的 X 语义不变（旧断言仍然成立），变的是目标锚不再飘到容器中心。
            Check("★顺序链就窄轴拉直：算子→容器 两锚点共 X（与算子居中对齐）",
                Math.Abs(firstChain.SourceAnchor.X - firstChain.TargetAnchor.X) < 0.01,
                $"src={firstChain.SourceAnchor} tgt={firstChain.TargetAnchor}");

            // 容器→算子（反向）：箭头打在下游算子顶缘中点的正上方，同样垂直
            var tail = h.Leaf("T");
            h.Add(tail);
            var tailChain = c.Links.FirstOrDefault(l => l.IsOrderLink && ReferenceEquals(l.Target, h.Node(tail)));
            Check("★容器→算子 顺序链同样共 X（垂直落在算子中轴上）",
                tailChain != null
                && Math.Abs(tailChain.SourceAnchor.X - tailChain.TargetAnchor.X) < 0.01
                && Math.Abs(tailChain.TargetAnchor.X - (h.Node(tail)!.Location.X + FlowCanvasViewModel.NodeWidth / 2)) < 0.01,
                tailChain == null ? "链不存在" : $"src={tailChain.SourceAnchor} tgt={tailChain.TargetAnchor}");
            Check("顺序链悬停提示带执行次序", firstChain.Tooltip.Contains("执行顺序"), $"'{firstChain.Tooltip}'");
            Check("顺序链不计入非法/降级（它不是数据线）",
                c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0, "");

            // 折叠后：容器内部顺序链消失，容器本身仍在顶层链上
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);
            Check("折叠容器后其内部顺序链消失、容器仍在顶层链",
                c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(b1))) == 0
                && c.Links.Count(l => l.IsOrderLink && ReferenceEquals(l.Source, h.Node(a)) && ReferenceEquals(l.Target, h.Node(cond))) == 1,
                $"顺序链 {c.Links.Count(l => l.IsOrderLink)} 条");
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);

            // Z 序：容器框垫在它的泳道与子孙底下
            var frameIndex = c.Nodes.IndexOf(h.Node(cond)!);
            var laneIndex = c.Nodes.IndexOf(h.Lane(ifBranch)!);
            var childIndex = c.Nodes.IndexOf(h.Node(b1)!);
            Check("Z 序：容器框 → 泳道 → 子孙（框垫底）",
                frameIndex >= 0 && frameIndex < laneIndex && laneIndex < childIndex,
                $"frame={frameIndex} lane={laneIndex} child={childIndex}");

            // 叠放层次（Panel.ZIndex 绑定值）：不只靠集合序——集合序是"分支 1 整棵树 → 分支 2 泳道"，
            // 后入分支的泳道会盖住先入分支里的嵌套框；层次由深度 + 身份算，与分支序无关
            Check("叠放层次恒定：容器框 < 泳道 < 内容，跨层按深度整体抬高",
                h.Node(cond)!.ZOrder == 3 && h.Lane(ifBranch)!.ZOrder == 4 && h.Node(b1)!.ZOrder == 8
                && h.Node(a)!.ZOrder == 5,
                $"frame={h.Node(cond)!.ZOrder} lane={h.Lane(ifBranch)!.ZOrder} child={h.Node(b1)!.ZOrder} topStep={h.Node(a)!.ZOrder}");

            Check("禁用步骤标题带后缀（重建后刷新）",
                RenderDisabledSuffix(h), "");
        }

        private static bool TrackAnchorWhileMoving(Harness h, StepModel a, FlowCanvasViewModel c)
        {
            var link = c.Links.FirstOrDefault();
            if (link == null) return false;

            var orig = h.Node(a)!.Location;
            var before = link.SourceAnchor;

            h.Node(a)!.Location = new Point(orig.X + 55, orig.Y + 40);
            var after = link.SourceAnchor;
            bool followed = Math.Abs(after.X - (orig.X + 55 + FlowCanvasViewModel.NodeWidth)) < 0.01
                && Math.Abs(after.Y - (orig.Y + 40 + FlowCanvasViewModel.NodeHeight / 2)) < 0.01
                && after != before;

            h.Node(a)!.Location = orig;
            bool restored = link.SourceAnchor == new Point(orig.X + FlowCanvasViewModel.NodeWidth, orig.Y + FlowCanvasViewModel.NodeHeight / 2);

            return followed && restored;
        }

        private static bool RenderDisabledSuffix(Harness h)
        {
            var a = h.Flow.Steps[0];
            a.IsDisEnable = true;
            h.Canvas.RebuildInPlace();
            var ok = (h.Node(a)!.Header ?? "").Contains("已禁用");
            a.IsDisEnable = false;
            h.Canvas.RebuildInPlace();
            return ok;
        }

        // ==================================================================
        //  [C] 泳道与容器框几何：包住内容、不入坐标库
        // ==================================================================
        private static void LaneAndFrameGeometry()
        {
            Section("[C] 泳道与容器框几何");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var elseBranch = cond.Children[1];
            var b1 = h.Leaf("B1");
            ifBranch.Steps.Add(b1);
            h.Add(a); h.Add(cond);

            var c = h.Canvas;
            var frame = h.Node(cond)!;
            var lane = h.Lane(ifBranch)!;

            Check("框体尺寸为正", frame.LaneWidth > 0 && frame.LaneHeight > 0,
                $"{frame.LaneWidth:0}x{frame.LaneHeight:0}");
            Check("框体包住分支内步骤",
                frame.Location.X <= h.Node(b1)!.Location.X && frame.Location.Y <= h.Node(b1)!.Location.Y
                && frame.Location.X + frame.LaneWidth >= h.Node(b1)!.Location.X,
                $"frame=({frame.Location.X:0},{frame.Location.Y:0}) child=({h.Node(b1)!.Location.X:0},{h.Node(b1)!.Location.Y:0})");
            // 框/泳道/内容的包含链（尺寸感知布局后逐级收紧：泳道 = 内容 ± 内边距，框 = 泳道并集 ± 框内边距）
            Check("泳道完整包含分支内步骤（含内边距）",
                Inside(NodeBox(lane), NodeBox(h.Node(b1)!)),
                $"lane={NodeBox(lane)} b1={NodeBox(h.Node(b1)!)}");
            Check("框体完整包含所属泳道（含框内边距）",
                Inside(NodeBox(frame), NodeBox(lane)),
                $"frame={NodeBox(frame)} lane={NodeBox(lane)}");
            Check("空分支泳道给占位提示", h.Lane(elseBranch)!.IsLaneEmpty, "");
            Check("泳道外接框为正尺寸",
                lane.LaneWidth > 0 && lane.LaneHeight > 0, $"{lane.LaneWidth:0}x{lane.LaneHeight:0}");

            Check("泳道不可拖 / 不可选 / 属装饰",
                h.Lanes().All(l => !l.IsDraggable && !l.IsSelectable && l.IsDecorator), "");
            Check("泳道 StepId 为空 Guid（绝不写进坐标库）",
                h.Lanes().All(l => l.StepId == Guid.Empty), "");
            Check("容器框不可拖（位置由内容算出）但可选中（联动属性栏）",
                !frame.IsDraggable && frame.IsSelectable && !frame.IsDecorator, "");

            // 装饰节点与容器框的位置都不入库
            var before = h.Flow.Layout.Count;
            lane.Location = new Point(-7000, -7000);
            frame.Location = new Point(7000, 7000);
            Check("挪动泳道/容器框不新增、不污染坐标项",
                h.Flow.Layout.Count == before && h.Flow.Layout.Find(cond.StepID) != null
                && h.Flow.Layout.Find(cond.StepID)!.X != 7000,
                $"Layout.Count={h.Flow.Layout.Count}");

            // 真实步骤（含分支内）的坐标入库
            var b1Node = h.Node(b1)!;
            // 布局预留：容器分支内容之下才排后续兄弟（旧实现会把兄弟叠进容器框——
            // "展开逻辑分支后层级顺序乱了"的元凶）。判据用"兄弟在框体底缘之下"，
            // 不锁具体行高——行距现在是"上一行框体实际高 + RowGap"，数值随内容走。
            // X 口径（2026-10-11 本层居中）：尾部叶子按"本层最宽项的轴线"居中，
            // 不再与 a 左对齐——宽容器与窄算子共享一条中轴（真机反馈"延时_2 应在容器中间"）。
            var tail = h.Leaf("尾部");
            h.Add(tail);
            var tailNode = h.Node(tail)!;
            var frameBottom = frame.Location.Y + frame.LaneHeight;
            double axisX = new[] { h.Node(a)!, frame }
                .Max(n => n.Location.X + (n.Kind == CanvasNodeKind.Container ? n.LaneWidth : FlowCanvasViewModel.NodeWidth) / 2);
            Check("AutoLayout 预留容器分支高度 + 本层居中：后续兄弟排在框体底缘之下、且居最宽项轴线",
                tailNode.Location.Y >= frameBottom
                && Math.Abs(tailNode.Location.X + FlowCanvasViewModel.NodeWidth / 2 - axisX) < 0.01,
                $"tail=({tailNode.Location.X:0},{tailNode.Location.Y:0}) axisX={axisX:0} frameBottom={frameBottom:0}");

            b1Node.Location = new Point(999, 888);
            Check("分支内步骤拖动写回坐标库",
                h.Flow.Layout.Find(b1.StepID) != null
                && Math.Abs(h.Flow.Layout.Find(b1.StepID)!.X - 999) < 0.01,
                $"X={h.Flow.Layout.Find(b1.StepID)?.X}");
        }

        // ==================================================================
        //  [D] 折叠与展开：子孙隐藏、角标计数、持久化、连线隐藏计数
        // ==================================================================
        private static void CollapseAndExpand()
        {
            Section("[D] 折叠与展开");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var b1 = h.Leaf("B1");
            var inner = h.For("内层");
            var deep = h.Leaf("深层");
            cond.Children[0].Steps.Add(b1);
            cond.Children[0].Steps.Add(inner);
            inner.Children[0].Steps.Add(deep);
            h.Add(a); h.Add(cond);

            var tail = h.Leaf("尾部");
            Harness.WithInput(tail, "In");
            h.Add(tail);
            Harness.RawLink(tail, "In", deep, "Out");

            var c = h.Canvas;
            // 本图式的唯一数据线是"分支内叶子喂后续顶层步骤"（CrossBranch 非法）——默认态下非法线照常渲染，
            // 所以这里没有"默认 0 条"的对照；改为钉"非法例外在默认态可见"（显隐开关的零对照在 [X] 段）
            Check("默认态：唯一数据线是非法线（CrossBranch）→ 例外照常渲染",
                !c.ShowDataLinks && c.IllegalLinkCount == 1 && c.DataLinkCount == 1,
                $"数据线={c.DataLinkCount} 非法={c.IllegalLinkCount}");
            c.ShowDataLinks = true;   // 本组测折叠隐藏的连线计数，不测显隐开关
            var frame = h.Node(cond)!;
            Check("展开态：深层步骤可见且连线画到深层",
                h.Node(deep) != null && h.Link(deep, tail) != null, "");

            c.ToggleCollapseCommand.Execute(frame);
            Check("折叠后框标记翻转并落库",
                frame.IsCollapsed && h.Flow.Layout.Find(cond.StepID)!.Collapsed, "");
            Check("折叠后子孙节点全部摘下（含嵌套）",
                h.Node(b1) == null && h.Node(inner) == null && h.Node(deep) == null
                && h.Steps().Count == 2, $"步骤 {h.Steps().Count}");
            Check("折叠角标统计含嵌套子孙（b1+inner+deep = 3）",
                frame.CollapsedChildCount == 3 && frame.CollapsedBadgeText == "3 步",
                $"'{frame.CollapsedBadgeText}'");
            Check("折叠后框体尺寸回落到标称盒",
                frame.LaneWidth <= FlowCanvasViewModel.CollapsedFrameWidth + 1
                && frame.LaneHeight <= FlowCanvasViewModel.CollapsedFrameHeight + 1,
                $"{frame.LaneWidth:0}x{frame.LaneHeight:0}");
            Check("通往折叠子树的连线隐藏并计数（不告警、不删图纸）",
                c.HiddenLinkCount == 1 && c.DataLinkCount == 0
                && c.WarningVisibility == Visibility.Collapsed
                && deep.LinkedSources.ContainsKey("Out") == false
                && tail.LinkedSources.ContainsKey("In"),
                $"hidden={c.HiddenLinkCount}");

            c.ToggleCollapseCommand.Execute(frame);
            Check("再点展开：子孙回来、连线重画",
                !frame.IsCollapsed && h.Node(deep) != null && h.Link(deep, tail) != null, "");

            // 折叠态跨重建持久化（Layout.Collapsed 随图纸存盘）
            c.ToggleCollapseCommand.Execute(frame);
            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            h.Workspace.SwitchFlow(h.Flow);
            Check("切流程往返后折叠态保持（子孙仍隐藏）",
                h.Node(b1) == null && h.Flow.Layout.Find(cond.StepID)!.Collapsed, "");
            Check("折叠后外部选中隐藏步骤给出提示（不炸、给指引）",
                SelectHiddenAndCheckHint(h, deep), "");
            c.ToggleCollapseCommand.Execute(h.Node(cond)!);
            Check("再点展开后深层步骤回来", h.Node(deep) != null, "");
        }

        private static bool SelectHiddenAndCheckHint(Harness h, StepModel hidden)
        {
            h.Workspace.SwitchStep(hidden);
            var hint = h.Canvas.StatusHint ?? "";
            h.Workspace.SwitchStep(null);
            return hint.Contains("折叠");
        }

        // ==================================================================
        //  [E] 模块级连线聚合：一对多绑定合一根线；全局/常量折角标；野 Id 计降级
        // ==================================================================
        private static void ModuleLinkAggregation()
        {
            Section("[E] 模块连线聚合");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;
            Check("默认态：数据线一条不画（开关默认关）", !c.ShowDataLinks && c.DataLinkCount == 0, $"{c.DataLinkCount}");
            c.ShowDataLinks = true;   // 本组测聚合口径，不测显隐开关

            Check("单绑定：一根线、无 ×N 角标",
                c.DataLinkCount == 1 && c.Links.First(l => !l.IsOrderLink).Bindings.Count == 1 && c.Links.First(l => !l.IsOrderLink).BindingCountLabel == "", "");
            Check("线的两端 = 生产方/消费方节点",
                ReferenceEquals(c.Links[0].Source, h.Node(a)) && ReferenceEquals(c.Links[0].Target, h.Node(b)), "");
            Check("锚点 = 盒子边缘中点（源右缘 / 目标左缘，纯数学不依赖控件回报）",
                c.Links[0].SourceAnchor == new Point(h.Node(a)!.Location.X + FlowCanvasViewModel.NodeWidth, h.Node(a)!.Location.Y + FlowCanvasViewModel.NodeHeight / 2)
                && c.Links[0].TargetAnchor == new Point(h.Node(b)!.Location.X, h.Node(b)!.Location.Y + FlowCanvasViewModel.NodeHeight / 2),
                $"src={c.Links[0].SourceAnchor} tgt={c.Links[0].TargetAnchor}");
            Check("节点挪动后锚点实时跟随（连线跟随拖拽的机制）",
                TrackAnchorWhileMoving(h, a, c), "");

            // 同一对模块的第二条绑定 → 聚合进同一根线
            b.SetInputValue("In2", 0d);
            Harness.RawLink(b, "In2", a, "Out");
            c.RebuildInPlace();   // LinkedSources 是属性写回，画布只订阅集合增删 → 手动重算
            Check("同对模块第二条绑定聚合进同一根线（×2）",
                c.DataLinkCount == 1 && c.Links.First(l => !l.IsOrderLink).Bindings.Count == 2 && c.Links.First(l => !l.IsOrderLink).BindingCountLabel == "×2",
                $"{c.DataLinkCount}/{c.Links.First(l => !l.IsOrderLink).Bindings.Count}");
            Check("悬停提示逐条列出绑定",
                c.Links[0].Tooltip.Contains("In ←") && c.Links[0].Tooltip.Contains("In2 ←"),
                $"'{c.Links[0].Tooltip.Replace("\n", "|")}'");

            // 第二个消费方 → 独立一根线
            var d = h.Leaf("D");
            Harness.WithInput(d, "In");
            h.Add(d);
            Harness.RawLink(d, "In", a, "Out");
            c.RebuildInPlace();
            Check("不同消费方各一根线", c.DataLinkCount == 2 && h.Link(a, d) != null, $"实际 {c.DataLinkCount}");

            // 全局变量 / 常量：不画线，折成消费方角标
            var q = h.Leaf("Q");
            Harness.WithInput(q, "In");
            h.Add(q);
            q.LinkedSources["In"] = new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "GV", "全局.gv");
            var r = h.Leaf("R");
            Harness.WithInput(r, "In");
            h.Add(r);
            r.LinkedSources["In"] = new LinkReference(LinkKind.Constant, Guid.Empty, "5", "常量 5");
            c.RebuildInPlace();
            Check("全局变量/常量绑定不画线、不计非法与降级",
                c.DataLinkCount == 2 && c.IllegalLinkCount == 0 && c.DeferredLinkCount == 0,
                $"links={c.Links.Count}");
            Check("全局变量折成角标（悬停带变量名）",
                h.Node(q)!.ExternalLinkCount == 1 && h.Node(q)!.ExternalLinkTooltip.Contains("全局变量 GV"),
                $"'{h.Node(q)!.ExternalLinkTooltip.Replace("\n", "|")}'");
            Check("常量折成角标", h.Node(r)!.ExternalLinkCount == 1 && h.Node(r)!.ExternalLinkTooltip.Contains("常量 5"), "");

            // 野 Id（上游已删除）→ 降级计数，不画线不标红
            var h2 = new Harness();
            var y = h2.Leaf("Y");
            Harness.WithInput(y, "In");
            h2.Add(y);
            y.SetLink("In", new LinkReference(LinkKind.StepPort, Guid.NewGuid(), "Out", "幽灵.Out"));
            var c2 = h2.Canvas;
            c2.ShowDataLinks = true;   // 显式打开：让「Links.Count == 0」判的是降级逻辑，不是显示开关
            Check("上游已删 → 降级计数（编译会报致命断连）",
                c2.DeferredLinkCount == 1 && c2.Links.Count == 0 && c2.IllegalLinkCount == 0
                && c2.WarningVisibility == Visibility.Collapsed,
                $"deferred={c2.DeferredLinkCount}");
        }

        // ==================================================================
        //  [F] 连线合法性：倒序 / 跨分支 / 容器后 → 红虚线；祖先取数放行
        // ==================================================================
        private static void LinkLegalityAndCounts()
        {
            Section("[F] 连线合法性");

            // F1 改序把合法线变非法：自动重算 + 红虚线 + 告警条
            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;
            c.ShowDataLinks = true;   // 本节测连线合法性判定，普通线必须可见
            Check("合法线：0 告警", c.IllegalLinkCount == 0 && !c.Links[0].IsIllegal, "");

            h.Flow.Steps.Move(0, 1);
            Check("改序后自动重算并标记非法",
                c.IllegalLinkCount == 1 && c.WarningVisibility == Visibility.Visible, $"illegal={c.IllegalLinkCount}");
            Check("非法线带可读原因",
                c.Links.Single(l => !l.IsOrderLink).IsIllegal && c.Links.Single(l => !l.IsOrderLink).WarningText!.Contains("执行顺序倒序"),
                $"'{c.Links.Single(l => !l.IsOrderLink).WarningText}'");
            Check("告警条文案同时报非法与降级数量",
                c.WarningText.Contains("1 条连线结构非法"), $"'{c.WarningText}'");
            Check("画布不擅自删图纸引用", b.LinkedSources["In"].TargetStepId == a.StepID, "");

            // F2 跨分支（两端都可见 → 直接标红）
            var h4 = new Harness();
            var cond = h4.If("判断");
            var inIf = h4.Leaf("If内");
            cond.Children[0].Steps.Add(inIf);
            var inElse = h4.Leaf("Else内");
            Harness.WithInput(inElse, "In");
            cond.Children[1].Steps.Add(inElse);
            Harness.RawLink(inElse, "In", inIf, "Out");
            h4.Add(cond);
            var c4 = h4.Canvas;
            c4.ShowDataLinks = true;   // 与 F1 同口径：测的是合法性判定
            Check("跨分支连线标红且 Classify 判成跨分支",
                c4.IllegalLinkCount == 1 && c4.Links.Single(l => !l.IsOrderLink).IsIllegal
                && h4.Topology().Classify(inIf.StepID, inElse.StepID) == LinkLegality.CrossBranch, "");

            // F3 循环体取 For.Index：祖先取数放行（历史误判重灾区）
            var h5 = new Harness();
            var outer = h5.For("循环");
            var bodyStep = h5.Leaf("体内");
            Harness.WithInput(bodyStep, "In");
            outer.Children[0].Steps.Add(bodyStep);
            h5.Add(outer);
            Harness.RawLink(bodyStep, "In", outer, "Index");
            var c5 = h5.Canvas;
            c5.ShowDataLinks = true;   // 与 F1 同口径：测的是合法性判定
            c5.RebuildInPlace();
            Check("循环体取 For.Index 合法（ProducerIsAncestor）",
                c5.IllegalLinkCount == 0 && h5.Link(outer, bodyStep) != null && !h5.Link(outer, bodyStep)!.IsIllegal,
                $"illegal={c5.IllegalLinkCount} links={c5.Links.Count}");

            // F4 生产方排在容器之后 → 标红
            var h2 = new Harness();
            var roots2 = Harness.OuterBeforeInner(producerBefore: false,
                out var container2, out var consumer2, out var producer2);
            h2.Flow.Steps.Clear();
            foreach (var s in roots2) h2.Flow.Steps.Add(s);
            var c2 = h2.Canvas;
            c2.ShowDataLinks = true;   // 与 F1 同口径：非法线在默认态本就会渲染，这里显式打开保证口径一致
            Check("生产方排在包住消费方的容器之后 → 标红计 1",
                c2.IllegalLinkCount == 1 && c2.Links.Single(l => !l.IsOrderLink).IsIllegal, $"illegal={c2.IllegalLinkCount}");
            Check("非法文案含『生产方排在包住消费方的容器之后』",
                c2.Links.Single(l => !l.IsOrderLink).WarningText!.Contains("包住消费方的容器之后"),
                $"'{c2.Links.Single(l => !l.IsOrderLink).WarningText}'");
        }

        // ==================================================================
        //  [G] 建线手势：结构合法才发请求；端口对交绑定弹窗（画布不写连线）
        // ==================================================================
        private static void LinkGestureAndRejection()
        {
            Section("[G] 建线手势与拒绝");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            h.Add(a); h.Add(b);
            var c = h.Canvas;
            c.ShowDataLinks = true;   // 显式打开：让「画布不写连线」的 DataLinkCount==0 有意义（默认隐藏会让它恒真）

            // 顺向拖线：结构合法 → 发建线请求（消费方回传），图纸零写入
            var requested = c.RequestLink(h.Node(a)!, h.Node(b)!);
            Check("顺向拖线发出建线请求且带回消费方",
                ReferenceEquals(requested, b), $"'{requested?.StepName}'");
            Check("画布自身不写连线（端口对归绑定弹窗，顺序链除外）",
                b.LinkedSources.Count == 0 && c.DataLinkCount == 0, "");

            // 反向起拖（从输入脚拖向生产方）也要归一成同一条请求
            var requested2 = c.RequestLinkReverse(h.Node(a)!, h.Node(b)!);
            Check("反向起拖归一后同样请求消费方", ReferenceEquals(requested2, b), "");

            // 倒序 / 自连 / 泳道：拒绝并给出原因
            var tail = h.Leaf("尾部");
            h.Add(tail);
            var rejected = c.RequestLink(h.Node(tail)!, h.Node(a)!);
            Check("倒序拖线被拒并给出原因",
                rejected == null && (c.StatusHint ?? "").Contains("执行顺序倒序"), $"'{c.StatusHint}'");
            Check("自连被拒", c.RequestLink(h.Node(a)!, h.Node(a)!) == null, "");
            Check("拒绝后提示可见（状态栏）", c.StatusHint != null, "");
        }

        // ==================================================================
        //  [H] FlowTopology.Classify 与编译期 CheckLinkOrder 必须同进退
        // ==================================================================
        private static void TopologyVersusCompiler()
        {
            Section("[H] 拓扑判定 ⇄ 编译校验一致性");

            var method = typeof(FlowCompiler).GetMethod("CheckLinkOrder", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                Check("反射找到 FlowCompiler.CheckLinkOrder", false, "internal 方法改名或被删除");
                return;
            }
            Check("反射找到 FlowCompiler.CheckLinkOrder", true, "");

            void Assert(string label, List<StepModel> roots,
                        Guid producerId, Guid consumerId,
                        LinkLegality expect, bool expectError, string expectTag)
            {
                var topo = FlowTopology.Build(roots);
                var actual = topo.Classify(producerId, consumerId);
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });

                var ok = actual == expect
                         && (expectError ? errors.Count == 1 && errors[0].Message.Contains(expectTag) : errors.Count == 0);
                Check(label, ok, $"{actual} / 错误 {errors.Count} 条{(!ok && errors.Count > 0 ? " → " + errors[0].Message : "")}");
                if (expectError && errors.Count > 0)
                {
                    Check($"  └ 错误定位到消费方",
                        errors[0].StepId != null && errors[0].StepName != null,
                        $"{errors[0].StepName}");
                }
            }

            // 1. 同层前→后：合法
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { p, c1 };
                Assert("同层前→后合法", roots, p.StepID, c1.StepID,
                    LinkLegality.SameListBefore, false, "");
            }

            // 2. 外层排前 → 内层消费（SameListBefore 复用）
            {
                var roots = Harness.OuterBeforeInner(producerBefore: true,
                    out var container, out var consumer, out var producer);
                Assert("外层排前 → 内层消费（SameListBefore）",
                    roots, producer.StepID, consumer.StepID,
                    LinkLegality.SameListBefore, false, "");
            }

            // 3. 同层后→前（倒序）：致命错
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                Assert("同层倒序 → 致命错",
                    roots, p.StepID, c1.StepID,
                    LinkLegality.SameListReversed, true, "依赖倒序");
            }

            // 4. 外层排后 → 内层消费（ProducerAfterEnclosingContainer）：致命错
            {
                var roots = Harness.OuterBeforeInner(producerBefore: false,
                    out var container, out var consumer, out var producer);
                Assert("外层排后 → ProducerAfterEnclosingContainer → 致命错",
                    roots, producer.StepID, consumer.StepID,
                    LinkLegality.ProducerAfterEnclosingContainer, true, "依赖倒序");
            }

            // 5. 跨分支：致命错
            {
                var cond = new ConditionStep("", "If", StubPluginProvider.IfPlugin, "判断");
                var inIf = new ActionStep("", "If内", StubPluginProvider.LeafPlugin, "If内");
                inIf.SetInputValue("In", 0d);
                var inElse = new ActionStep("", "Else内", StubPluginProvider.LeafPlugin, "Else内");
                inElse.SetInputValue("In", 0d);
                cond.Children[0].Steps.Add(inIf);
                cond.Children[1].Steps.Add(inElse);
                Harness.RawLink(inElse, "In", inIf, "Out");
                var roots = new List<StepModel> { cond };
                Assert("跨分支 → 致命错",
                    roots, inIf.StepID, inElse.StepID,
                    LinkLegality.CrossBranch, true, "跨分支取数");
            }

            // 6. 自连（SameListReversed，文案含"引用了自身的输出"）
            {
                var s = new ActionStep("", "自连", StubPluginProvider.LeafPlugin, "S");
                s.SetInputValue("In", 0d);
                Harness.RawLink(s, "In", s, "Out");
                var roots = new List<StepModel> { s };
                Assert("自连 → 致命错且文案含『引用了自身的输出』",
                    roots, s.StepID, s.StepID,
                    LinkLegality.SameListReversed, true, "引用了自身的输出");
            }

            // 7. For.Index → 循环体内消费（ProducerIsAncestor，合法）
            {
                var outer = new ForStep("", "For", StubPluginProvider.ForPlugin, "循环");
                var body = outer.Children[0];
                var inner = new ActionStep("", "体内", StubPluginProvider.LeafPlugin, "体内");
                inner.SetInputValue("In", 0d);
                body.Steps.Add(inner);
                var roots = new List<StepModel> { outer };
                Assert("For.Index → 循环体（ProducerIsAncestor）合法",
                    roots, outer.StepID, inner.StepID,
                    LinkLegality.ProducerIsAncestor, false, "");
            }

            // 8. 野 TargetStepId（Unknown）：不报错（LinkPorts 已报致命断连）
            {
                var ghost = Guid.NewGuid();
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.SetLink("In", new LinkReference(LinkKind.StepPort, ghost, "Out", "幽灵.Out"));
                var roots = new List<StepModel> { c1 };
                var topo = FlowTopology.Build(roots);
                var actual = topo.Classify(ghost, c1.StepID);
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("野 Id → Classify=Unknown 且不报倒序错",
                    actual == LinkLegality.Unknown && errors.Count == 0,
                    $"{actual} / 错误 {errors.Count} 条");
            }

            // 9. 跳过：禁用消费步骤
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.IsDisEnable = true;
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("禁用消费步骤 → 跳过倒序检查",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }

            // 10. 跳过：非 StepPort 来源
            {
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                c1.SetLink("In", new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "GV", "全局.gv"));
                var roots = new List<StepModel> { c1 };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("非 StepPort 来源 → 跳过",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }

            // 11. 跳过：禁用生产方（且倒序时仍不报）
            {
                var p = new ActionStep("", "生产", StubPluginProvider.LeafPlugin, "P");
                p.SetInputValue("In", 0d);
                p.IsDisEnable = true;
                var c1 = new ActionStep("", "消费", StubPluginProvider.LeafPlugin, "C1");
                c1.SetInputValue("In", 0d);
                Harness.RawLink(c1, "In", p, "Out");
                var roots = new List<StepModel> { c1, p };
                var errors = new List<CompilationError>();
                method!.Invoke(null, new object[] { roots, errors });
                Check("禁用生产方 → 跳过倒序检查",
                    errors.Count == 0, $"错误 {errors.Count} 条");
            }
        }

        // ==================================================================
        //  [I] 分支改名后泳道标题跟着走
        // ==================================================================
        private static void BranchDisplayNameNotification()
        {
            Section("[I] 分支改名与显示名通知");

            var branch = new StepCollection { BranchType = BranchType.If, StepName = "If", Expression = "" };
            var raised = new List<string?>();
            ((INotifyPropertyChanged)branch).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            branch.StepName = "命中";
            Check("改 StepName 补发 DisplayName 通知",
                raised.Contains(nameof(StepCollection.StepName)) && raised.Contains(nameof(StepCollection.DisplayName)),
                string.Join(",", raised.Where(x => x != null)));
            Check("未绑定条件时显示名带占位提示", branch.DisplayName.Contains("未绑定条件"), $"'{branch.DisplayName}'");

            raised.Clear();
            branch.Expression = "Score > 80";
            Check("补上条件后显示名带表达式",
                raised.Contains(nameof(StepCollection.DisplayName)) && branch.DisplayName == "命中 [ Score > 80 ]",
                $"'{branch.DisplayName}'");

            raised.Clear();
            branch.BranchType = BranchType.Else;
            Check("改成 Else 后不再要求条件",
                !branch.RequiresExpression && branch.DisplayName == "命中" && raised.Contains(nameof(StepCollection.DisplayName)),
                $"'{branch.DisplayName}'");

            // 画布泳道标题回读 Branch.DisplayName → 改名后即时生效
            var h = new Harness();
            var cond = h.If("判断");
            h.Add(cond);
            var lane = h.Lane(cond.Children[0])!;
            var live = lane.Header;
            cond.Children[0].StepName = "成功分支";
            Check("画布泳道标题随分支改名刷新", lane.Header != live && lane.Header.Contains("成功分支"), $"'{lane.Header}'");
        }

        // ==================================================================
        //  [J] 节点池：重画不丢选中；删步骤摘节点
        // ==================================================================
        private static void NodePoolKeepsSelection()
        {
            Section("[J] 增量重建与选中保持");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            h.Add(a); h.Add(b);
            var c = h.Canvas;

            var nodeA = h.Node(a)!;
            nodeA.IsSelected = true;
            Check("置选中生效", h.Node(a)!.IsSelected, "");

            h.Add(h.Leaf("C"));
            Check("集合变更后仍复用同一 VM 实例", ReferenceEquals(h.Node(a), nodeA), "");
            Check("复用后选中态未蒸发", nodeA.IsSelected, "");
            Check("重画后步骤节点数递增",
                c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step) == 3,
                $"{c.Nodes.Count(n => n.Kind == CanvasNodeKind.Step)} 个步骤节点");

            h.Flow.Steps.Remove(b);
            Check("删除步骤后画布摘掉节点",
                h.Node(b) == null && h.MainSteps().Count == 2, $"{h.MainSteps().Count}");
            Check("被删步骤的坐标同步回收", h.Flow.Layout.Find(b.StepID) == null, "");
            Check("存活节点仍保持选中", h.Node(a)!.IsSelected, "");

            // 上游被删：聚合连线随之消失并计入降级
            var h2 = new Harness();
            var p = h2.Leaf("P");
            var q = h2.Leaf("Q");
            Harness.WithInput(q, "In");
            h2.Add(p); h2.Add(q);
            Harness.RawLink(q, "In", p, "Out");
            var c2 = h2.Canvas;
            Check("默认态：数据线一条不画（开关默认关）", !c2.ShowDataLinks && c2.DataLinkCount == 0, $"{c2.DataLinkCount}");
            c2.ShowDataLinks = true;   // 本组测上游删除后的降级计数，不测显隐开关
            Check("连线画出到上游", c2.DataLinkCount == 1, $"{c2.Links.Count}");
            h2.Flow.Steps.Remove(p);
            Check("上游删除后连线随重画消失并计入降级",
                c2.DataLinkCount == 0 && c2.DeferredLinkCount == 1, $"deferred={c2.DeferredLinkCount}");
            Check("连接器连接态在重画时统一复位",
                c2.Nodes.SelectMany(n => n.Inputs.Concat(n.Outputs))
                    .All(port => !port.IsConnected), "");
        }

        // ==================================================================
        //  [K] 坐标写回纪律：只有步骤节点入库
        // ==================================================================
        private static void LayoutWriteBackDiscipline()
        {
            Section("[K] 坐标写回");

            var h = new Harness();
            var cond = h.If("判断");
            var innerFor = h.For("内层循环");
            innerFor.Children[0].Steps.Add(h.Leaf("B1"));
            cond.Children[0].Steps.Add(innerFor);
            var a = h.Leaf("A");
            h.Add(cond); h.Add(a);
            var c = h.Canvas;

            Check("首帧自动布局补齐缺失坐标（含嵌套）", h.Flow.Layout.Count >= 4, $"Layout.Count={h.Flow.Layout.Count}");
            var nodeA = h.Node(a)!;
            Check("自动布局结果已回填节点", h.Flow.Layout.TryGet(a.StepID, out var stored) && nodeA.Location.X == stored!.X, "");

            nodeA.Location = new Point(1234, 567);
            Check("拖动步骤节点写回坐标库",
                h.Flow.Layout.Find(a.StepID)!.X == 1234 && h.Flow.Layout.Find(a.StepID)!.Y == 567,
                $"({h.Flow.Layout.Find(a.StepID)!.X:0}, {h.Flow.Layout.Find(a.StepID)!.Y:0})");

            var versionBefore = h.Flow.Version;
            nodeA.Location = new Point(2000, 200);
            Check("挪坐标不递增图纸版本（不触发重编译）", h.Flow.Version == versionBefore, $"{versionBefore}");

            // 容器框的位置是算出来的：程序性改动不得入库
            var frame = h.Node(cond)!;
            var frameStoredX = h.Flow.Layout.Find(cond.StepID)!.X;
            frame.Location = new Point(424242, 424242);
            Check("挪动容器框不污染坐标库",
                h.Flow.Layout.Find(cond.StepID)!.X == frameStoredX, $"{frameStoredX:0}");
            c.RebuildInPlace();
            Check("重画后框体位置回到内容外接框",
                frame.Location.X != 424242, $"frame.X={frame.Location.X:0}");
        }

        // ==================================================================
        //  [L] 外部选中 → 画布选中并滚入视野（扁平画布无层级可切）
        // ==================================================================
        private static void OutsideSelectionDrivesCanvas()
        {
            Section("[L] 外部选中联动");

            var h = new Harness();
            var a = h.Leaf("A");
            var cond = h.If("判断");
            var deep = h.Leaf("深层");
            cond.Children[0].Steps.Add(deep);
            h.Add(a); h.Add(cond);
            var c = h.Canvas;

            h.Workspace.SwitchStep(deep);
            Check("外部选中深层步骤 → 画布对应节点被选中", h.Node(deep)!.IsSelected, "");

            h.Workspace.SwitchStep(a);
            Check("外部选中顶层步骤 → 选中跟着走", h.Node(a)!.IsSelected, "");

            // 画布点选 → 工作区当前步骤（流程栏/属性栏跟随）
            c.Nodes.First(n => n.Model == deep).IsSelected = true;
            Check("画布点选回写工作区当前步骤", ReferenceEquals(h.Workspace.CurrentStep, deep),
                $"'{h.Workspace.CurrentStep?.StepName}'");

            var ghost = new ActionStep("", "幽灵", StubPluginProvider.LeafPlugin, "幽灵") as StepModel;
            var thrown = false;
            try { h.Workspace.SwitchStep(ghost!); }
            catch (InvalidOperationException) { thrown = true; }
            Check("选中不在图纸里的步骤由 WorkspaceContext 抛错（画布不参与兜底）", thrown, "");
            Check("抛错后画布选中未被破坏", h.Node(a)!.IsSelected || h.Node(deep)!.IsSelected, "");

            // 步骤改名不重画（只订阅集合增删）
            var nodeCount = c.Nodes.Count;
            deep.StepName = "改名后的深层";
            Check("改名不触发重建", c.Nodes.Count == nodeCount && h.Node(deep)!.Header.Contains("改名后"),
                $"'{h.Node(deep)!.Header}'");
        }

        // ==================================================================
        //  [M] 拖拽改序提交 + Version 递增次数 + 撤销逆操作
        // ==================================================================
        private static void DragReorderAndVersion()
        {
            Section("[M] 拖拽改序与 Version");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            var c = h.Leaf("C");
            h.Add(a); h.Add(b); h.Add(c);
            var cv = h.Canvas;

            var version0 = h.Flow.Version;
            Check("初始画布已建：节点 3 步骤", h.MainSteps().Count == 3, "");

            // 模拟 OnItemsDragCompleted 提交改序：a 从 index 0 移到 index 2
            var reorder = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 2);
            FlowCanvasExtensions.InvokeRedo(reorder);

            Check("Redo 执行后 Steps[0] = B", h.Flow.Steps[0] == b, "");
            Check("Redo 执行后 Steps[2] = A", h.Flow.Steps[2] == a, "");
            Check("Redo 触发 Version 递增（1 次）", h.Flow.Version == version0 + 1, $"{version0} → {h.Flow.Version}");
            Check("Redo 后画布主列按新顺序展示",
                h.MainSteps().Select(n => n.StepId).SequenceEqual(new[] { b.StepID, c.StepID, a.StepID }),
                string.Join(",", h.MainSteps().Select(n => n.Header)));

            cv.PushUndoReflection(reorder);
            Check("压栈后撤销栈深度 = 1", cv.UndoStackSize == 1, $"size={cv.UndoStackSize}");
            Check("压栈后重做栈为空", cv.RedoStackSize == 0, "");

            var another = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another);
            cv.PushUndoReflection(another);
            Check("第二次压栈后撤销栈 = 2", cv.UndoStackSize == 2, "");

            var versionBeforeUndo = h.Flow.Version;
            cv.UndoCommand.Execute();
            Check("Undo 把第二次改序回滚：Steps[0] = B", h.Flow.Steps[0] == b, $"'{h.Flow.Steps[0].StepName}'");
            Check("Undo 触发 Version 递增", h.Flow.Version == versionBeforeUndo + 1, "");
            Check("Undo 后重做栈 = 1", cv.RedoStackSize == 1, "");

            var versionBeforeRedo = h.Flow.Version;
            cv.RedoCommand.Execute();
            Check("Redo 重放第二次改序：Steps[0] = C", h.Flow.Steps[0] == c, $"'{h.Flow.Steps[0].StepName}'");
            Check("Redo 触发 Version 递增", h.Flow.Version == versionBeforeRedo + 1, "");
            Check("Redo 后撤销栈 = 2", cv.UndoStackSize == 2, "");

            cv.UndoCommand.Execute();
            cv.UndoCommand.Execute();
            Check("两次 Undo 后回到原始顺序 [A,B,C]",
                h.Flow.Steps[0] == a && h.Flow.Steps[1] == b && h.Flow.Steps[2] == c,
                string.Join(",", h.Flow.Steps.Select(s => s.StepName)));
            Check("全部 Undo 后撤销栈空", cv.UndoStackSize == 0, "");
            Check("全部 Undo 后重做栈 = 2", cv.RedoStackSize == 2, "");

            Check("栈空时 UndoCommand.CanExecute = false", !cv.UndoCommand.CanExecute(), "");
            Check("栈非空时 RedoCommand.CanExecute = true", cv.RedoCommand.CanExecute(), "");

            var another2 = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(another2);
            cv.PushUndoReflection(another2);
            Check("新操作后 redo 栈被清空", cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [O] 跨分支移分支（仅同层）
        // ==================================================================
        private static void CrossBranchMoveAndUndo()
        {
            Section("[O] 跨分支移分支");

            var h = new Harness();
            var cond = h.If("判断");
            var ifBranch = cond.Children[0];
            var elseBranch = cond.Children[1];
            var b1 = h.Leaf("B1");
            var b2 = h.Leaf("B2");
            ifBranch.Steps.Add(b1);
            ifBranch.Steps.Add(b2);
            h.Add(cond);
            var cv = h.Canvas;

            Check("建图：If 分支 2 个、Else 空",
                ifBranch.Steps.Count == 2 && elseBranch.Steps.Count == 0, "");

            var move = FlowCanvasExtensions.CreateMoveBranchCommand(
                b1, ifBranch.Steps, 0, elseBranch.Steps, 0);
            FlowCanvasExtensions.InvokeRedo(move);
            cv.PushUndoReflection(move);

            Check("Redo 后 If 分支剩 [B2]", ifBranch.Steps.Count == 1 && ifBranch.Steps[0] == b2, "");
            Check("Redo 后 Else 分支为 [B1]", elseBranch.Steps.Count == 1 && elseBranch.Steps[0] == b1, "");
            Check("画布重新渲染：两条泳道都在、两个节点都可见",
                h.Lane(ifBranch) != null && h.Lane(elseBranch) != null
                && h.Node(b1) != null && h.Node(b2) != null,
                "");

            cv.UndoCommand.Execute();
            Check("Undo 后 If 分支恢复 [B1,B2]",
                ifBranch.Steps.Count == 2 && ifBranch.Steps[0] == b1 && ifBranch.Steps[1] == b2, "");
            Check("Undo 后 Else 分支空", elseBranch.Steps.Count == 0, "");

            cv.RedoCommand.Execute();
            Check("Redo 后再次 If=[B2], Else=[B1]",
                ifBranch.Steps[0] == b2 && elseBranch.Steps[0] == b1, "");

            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程后撤销栈被清空", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
            h.Workspace.SwitchFlow(h.Flow);
            Check("切回原流程后栈仍空（不复活）", cv.UndoStackSize == 0 && cv.RedoStackSize == 0, "");
        }

        // ==================================================================
        //  [P] 解绑与撤销：右键连线逐条解绑（画布唯一的连线写回路径）
        // ==================================================================
        private static void UnbindUndoRedo()
        {
            Section("[P] 解绑 / 撤销");

            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            Harness.WithInput(b, "In");
            h.Add(a); h.Add(b);
            Harness.RawLink(b, "In", a, "Out");
            var c = h.Canvas;
            Check("默认态：数据线一条不画（开关默认关）", !c.ShowDataLinks && c.DataLinkCount == 0, $"{c.DataLinkCount}");
            c.ShowDataLinks = true;   // 本组测解绑/撤销的连线重画，不测显隐开关

            var link = h.Link(a, b)!;
            var binding = link.Bindings.Single();
            Check("绑定快照带解绑前的图纸引用（撤销的依据）", binding.OriginalLink != null, "");

            link.UnbindCommand.Execute(binding);
            Check("解绑后图纸引用删除", !b.LinkedSources.ContainsKey("In"), "");
            Check("解绑后连线消失", c.DataLinkCount == 0, $"{c.Links.Count}");
            Check("解绑留痕状态栏（可撤销）", (c.StatusHint ?? "").Contains("解绑"), $"'{c.StatusHint}'");

            c.UndoCommand.Execute();
            Check("撤销恢复图纸引用", b.LinkedSources.ContainsKey("In")
                && b.LinkedSources["In"].TargetStepId == a.StepID, "");
            Check("撤销后连线重画回来", c.DataLinkCount == 1 && h.Link(a, b) != null, "");

            c.RedoCommand.Execute();
            Check("重做再次解绑", !b.LinkedSources.ContainsKey("In") && c.DataLinkCount == 0, "");
        }

        // ==================================================================
        //  [Q] 栈深 50 上限与切流程清栈
        // ==================================================================
        private static void UndoStackBoundary()
        {
            Section("[Q] 栈深上限与切流程清栈");

            var h = new Harness();
            var p = h.Leaf("P");
            var q = h.Leaf("Q");
            Harness.WithInput(q, "In");
            h.Add(p); h.Add(q);
            Harness.RawLink(q, "In", p, "Out");
            var cv = h.Canvas;

            // 断线命令是 internal：与既有 CheckLinkOrder 同款反射构造
            var disconnectType = typeof(FlowCanvasViewModel).Assembly.GetType(
                "VisionMaster.ViewModels.DisconnectCommand")
                ?? throw new InvalidOperationException("DisconnectCommand 类型未找到");
            var cmd = Activator.CreateInstance(disconnectType,
                q, "In", q.LinkedSources["In"])!;

            for (int i = 0; i < 51; i++)
                cv.PushUndoReflection(cmd);
            Check("压栈 51 条后栈深封顶 50", cv.UndoStackSize == 50, $"size={cv.UndoStackSize}");

            var other = new FlowModel { FlowName = "其他流程" };
            h.Solution.Flows.Add(other);
            h.Workspace.SwitchFlow(other);
            Check("切流程清空撤销栈（旧图纸引用不阻塞 GC）",
                cv.UndoStackSize == 0 && !cv.UndoCommand.CanExecute(), "");
        }

        // ==================================================================
        //  [R] 运行时变量：定义节点 → 按变量名回找的模块连线 + 落盘往返
        // ==================================================================
        private static void RuntimeVariableModuleLinks()
        {
            Section("[R] 运行时变量模块连线");

            // ---- R1 定义节点 → For.LoopCount：按变量名聚合出一条线 ----
            var h = new Harness();
            var def = h.Variable("loopN", "int", "定义loopN");
            var forStep = h.For("循环");
            h.Add(def);
            h.Add(forStep);
            forStep.SetLink("LoopCount", new LinkReference(
                LinkKind.RuntimeVariable,
                LinkProtocol.RuntimeVariableMarkerGuid,
                "loopN",
                "Runtime.loopN"));
            var cv = h.Canvas;
            Check("默认态：数据线一条不画（开关默认关）", !cv.ShowDataLinks && cv.DataLinkCount == 0, $"{cv.DataLinkCount}");
            cv.ShowDataLinks = true;   // 本组测变量回找的连线，不测显隐开关

            Check("定义节点被识别并渲染（身份来自 FlowQueryHelper 的同一口径）",
                h.Node(def) != null, "");
            Check("按变量名回找出模块连线（不依赖端口）",
                h.Link(def, forStep) != null, $"links={cv.Links.Count}");
            Check("变量线合法且不降级",
                cv.DeferredLinkCount == 0 && cv.IllegalLinkCount == 0 && !h.Link(def, forStep)!.IsIllegal, "");
            Check("绑定显示串带变量名与输入别名",
                h.Link(def, forStep)!.Bindings.Single().Display.Contains("loopN")
                && h.Link(def, forStep)!.Bindings.Single().Display.Contains("循环次数"),
                $"'{h.Link(def, forStep)!.Bindings.Single().Display}'");
            Check("图纸侧三元组不变（画布只读投影）",
                forStep.LinkedSources["LoopCount"].TargetStepId == LinkProtocol.RuntimeVariableMarkerGuid
                && forStep.LinkedSources["LoopCount"].TargetPortName == "loopN", "");

            // ---- R2 定义节点被删 → 降级隐形，不标红不删图纸 ----
            h.Flow.Steps.Remove(def);
            Check("定义节点被删 → 降级隐形（不画线、不标红、不告警）",
                cv.DeferredLinkCount == 1 && cv.DataLinkCount == 0 && cv.WarningVisibility == Visibility.Collapsed,
                $"deferred={cv.DeferredLinkCount}");
            Check("降级不动图纸（画布不替用户删线）",
                forStep.LinkedSources.ContainsKey("LoopCount"), "");

            // ---- R3 落盘往返：RuntimeVariable 显式落盘，定义身份不丢 ----
            var hb = new Harness();
            var var2 = hb.Variable("thresh", "double", "定义thresh");
            var leaf = hb.Leaf("消费");
            Harness.WithInput(leaf, "In");
            hb.Add(var2);
            hb.Add(leaf);
            leaf.SetLink("In", new LinkReference(
                LinkKind.RuntimeVariable,
                LinkProtocol.RuntimeVariableMarkerGuid,
                "thresh",
                "Runtime.thresh"));
            hb.Canvas.ShowDataLinks = true;   // 同上：测的是"回找成线"，不是显隐开关
            hb.Canvas.RebuildInPlace();
            Check("变量喂普通算子输入口同样回找成线",
                hb.Link(var2, leaf) != null && !hb.Link(var2, leaf)!.IsIllegal, "");

            var json = SolutionService.Serialize(hb.Solution);
            Check("落盘文本里 Kind 是数字 3（不再靠 DisplayAddress 猜）",
                json.Contains("\"Kind\": 3") && json.Contains("\"Runtime.thresh\""), "");

            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            var reloadedFlow = reloaded.Flows.Single(f => f.FlowName == "画布断言流程");
            var def2 = reloadedFlow.Steps.Single(s => s.PluginTypeName!.Contains("VariableDefinitionPlugin"));
            var leaf2 = reloadedFlow.Steps.Single(s => !ReferenceEquals(s, def2));
            Check("往返后连线身份不变",
                leaf2.LinkedSources["In"].NormalizeKind() == LinkKind.RuntimeVariable
                && leaf2.LinkedSources["In"].TargetPortName == "thresh", "");
            Check("往返后变量定义步骤仍能被识别（Name/Type 存成裸字符串）",
                FlowQueryHelper.TryGetDefinedVariable(def2, out var rn2, out var rt2)
                && rn2 == "thresh" && rt2 == typeof(double),
                $"{rn2}/{rt2.Name}");

            // ---- R4 地基回归：绑定弹窗候选树递归 For 子层 ----
            var hc = new Harness();
            var pre = hc.Leaf("前置");
            var outer = hc.For("外层For");
            var innerDef = hc.Variable("inLoop", "int", "循环内定义");
            var innerConsumer = hc.Leaf("循环内消费");
            Harness.WithInput(innerConsumer, "In");
            hc.Add(pre);
            hc.Add(outer);
            outer.Children[0].Steps.Add(innerDef);
            outer.Children[0].Steps.Add(innerConsumer);

            var upstream = FlowQueryHelper.GetUpstreamNodes(hc.Flow.Steps, innerConsumer);
            Check("候选树能递归进 For 子层拿到循环内定义的变量（旧实现只认 If）",
                upstream.Contains(pre) && upstream.Contains(outer) && upstream.Contains(innerDef),
                $"找到 {upstream.Count} 个：[{string.Join(",", upstream.Select(s => s.StepName))}]");
        }

        // ==================================================================
        //  [S] Nodify 7.3 真实传参形态（IL 取证）——拖拽改序与建线在真机事件链上必须可用
        //
        //  背景（2026-10-07 三方审查 + reviewer 字节级复核）：
        //  · NodifyEditor::OnItemsDragStarted/Completed 的 IL 是 Execute(编辑器.DataContext)——
        //    实参 = 画布 VM 自身，不是被拖容器列表；旧实现只认 IEnumerable → 快照永远为空 →
        //    改序/跨分支提交在真机不可达（旧断言直调伪造 List，测不出真实事件链断在哪）。
        //  · PendingConnection::OnPendingConnectionCompleted 的 IL 是 Execute(get_Target)——
        //    实参 = 单个目标连接器；旧实现的 Tuple 分支是死代码，且先清 IsVisible 后读端点，
        //    读到的必是 null → 建线整条闭环真机断链。
        //  本节一律用「库实际会传的参数」驱动命令，钉死真实事件链。
        // ==================================================================
        private static void NodifyRealParameterShapes()
        {
            Section("[S] Nodify 7.3 真实传参形态（拖拽/建线）");

            // ---- S1 拖拽：实参 = 编辑器 DataContext（画布 VM 自身）→ 改序必须提交 ----
            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            var c = h.Leaf("C");
            h.Add(a); h.Add(b); h.Add(c);
            var cv = h.Canvas;
            Check("前置：主列 3 步已上画布", h.MainSteps().Count == 3, "");

            h.Node(a)!.IsSelected = true;
            cv.ItemsDragStartedCommand.Execute(cv);      // Nodify 7.3 实参 = 编辑器 DataContext
            Check("DataContext 形态下拖拽快照按选中集建立", h.MainSteps().Count == 3, "");
            Check("拖拽开始只记快照、不入撤销栈", cv.UndoStackSize == 0, $"size={cv.UndoStackSize}");

            h.Node(a)!.Location = new Point(h.Node(a)!.Location.X, 10000);   // 拖到最下方
            cv.ItemsDragCompletedCommand.Execute(cv);
            Check("拖拽提交改序：Steps 顺序变为 [B,C,A]",
                ReferenceEquals(h.Flow.Steps[0], b) && ReferenceEquals(h.Flow.Steps[2], a),
                string.Join(",", h.Flow.Steps.Select(s => s.StepName)));
            Check("改序已可撤销", cv.UndoCommand.CanExecute(), "");
            cv.UndoCommand.Execute();
            Check("撤销回到 [A,B,C]",
                ReferenceEquals(h.Flow.Steps[0], a) && ReferenceEquals(h.Flow.Steps[2], c),
                string.Join(",", h.Flow.Steps.Select(s => s.StepName)));

            // S1b 参数为 null（不应发生）也不炸、不提交。先清选中——选中集是拖拽快照的
            // 主路径，带着选中测 null 会按选中集提交，那是另一个（正确的）行为
            h.Node(a)!.IsSelected = false;
            cv.ItemsDragStartedCommand.Execute(null);
            cv.ItemsDragCompletedCommand.Execute(null);
            Check("null 实参不炸不提交", cv.UndoStackSize == 0, $"size={cv.UndoStackSize}");

            // ---- S2 建线：实参 = 目标连接器；先取两端后清手势状态 ----
            var h2 = new Harness();
            var p = h2.Add(h2.Leaf("生产"));
            var q = h2.Add(Harness.WithInput(h2.Leaf("消费"), "In"));
            var cv2 = h2.Canvas;

            var requested = cv2.RequestLink(h2.Node(p)!, h2.Node(q)!);
            Check("建线请求以消费方发出（目标连接器实参形态）", ReferenceEquals(requested, q),
                requested == null ? "未发出" : requested.StepName);
            Check("拖线状态已收场", !cv2.PendingConnection.IsVisible && cv2.PendingConnection.Source == null, "");

            var reverse = cv2.RequestLinkReverse(h2.Node(p)!, h2.Node(q)!);
            Check("反向起拖（从输入脚拖向输出脚）同样发出", ReferenceEquals(reverse, q), "");

            // ---- S3 非法连线被拒且提示落状态栏 ----
            // 图式：「早(0) → 晚(1)」；从"晚"的输出脚拖向"早"= 生产方排在消费方之后 = 倒序非法
            var h3 = new Harness();
            var early = h3.Add(h3.Leaf("早"));
            var late = h3.Add(h3.Leaf("晚"));
            var cv3 = h3.Canvas;
            var rejected = cv3.RequestLink(h3.Node(late)!, h3.Node(early)!);
            Check("倒序连线被拒（不发起绑定弹窗）", rejected == null, "");
            Check("拒绝原因落在状态栏", (cv3.StatusHint ?? string.Empty).Contains("连线被拒绝"), $"'{cv3.StatusHint}'");

            // ---- S4 拖线端口反馈：可达亮出、不可达压灰、结束复位 ----
            // 图式：「更早(0) → 源头(1) → 下游(2)」；从"源头"的输出脚起拖，
            // 下游可达、"更早"是倒序端不可连、自身防自环不可连
            var h4 = new Harness();
            h4.Add(h4.Leaf("更早"));
            var s4 = h4.Add(h4.Leaf("源头"));
            var t4 = h4.Add(Harness.WithInput(h4.Leaf("下游"), "In"));
            var cv4 = h4.Canvas;
            var early4 = h4.Flow.Steps[0];
            Check("常态：端口默认可连（默认压灰=首次打开满屏灰点的视觉缺陷）",
                h4.Node(t4)!.Inputs[0].IsConnectable && h4.Node(early4)!.Inputs[0].IsConnectable, "");
            cv4.StartConnectionCommand.Execute(h4.Node(s4)!.Outputs[0]);
            Check("拖线开始：正序下游端口可连", h4.Node(t4)!.Inputs[0].IsConnectable, "");
            Check("拖线开始：倒序端不可连", !h4.Node(early4)!.Inputs[0].IsConnectable, "");
            Check("拖线开始：自身端口不可连（防自环）", !h4.Node(s4)!.Inputs[0].IsConnectable, "");
            cv4.PendingConnection.IsVisible = false;
            Check("拖线结束：端口反馈全部复位", h4.Node(early4)!.Inputs[0].IsConnectable, "");
        }

        // ==================================================================
        //  [T] 画布删除 / 禁用 / 撤销栈与外部结构变化同步
        // ==================================================================
        private static void DeleteDisableAndUndoSync()
        {
            Section("[T] 删除/禁用/撤销栈同步");

            // ---- T1 外部删除步骤 → 撤销栈清空（改序命令的下标快照已失效，防错位回滚）----
            var h = new Harness();
            h.Add(h.Leaf("A"));
            var b = h.Add(h.Leaf("B"));
            var cv = h.Canvas;
            var reorder = FlowCanvasExtensions.CreateReorderCommand(h.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(reorder);
            cv.PushUndoReflection(reorder);
            Check("前置：撤销栈 = 1", cv.UndoStackSize == 1, $"size={cv.UndoStackSize}");
            h.Flow.Steps.Remove(b);     // 流程栏视角的外部删除
            Check("外部删除后撤销栈清空", cv.UndoStackSize == 0, $"size={cv.UndoStackSize}");
            Check("画布节点同步摘除", h.Node(b) == null, "");

            // ---- T1b 画布自身的拖拽改序写回不算外部变化：命令保留在栈里可撤销 ----
            var h1b = new Harness();
            var a1b = h1b.Add(h1b.Leaf("A"));
            h1b.Add(h1b.Leaf("B"));
            var cv1b = h1b.Canvas;
            h1b.Node(a1b)!.IsSelected = true;
            cv1b.ItemsDragStartedCommand.Execute(null);
            h1b.Node(a1b)!.Location = new Point(h1b.Node(a1b)!.Location.X, 10000);
            cv1b.ItemsDragCompletedCommand.Execute(null);
            Check("画布自身拖拽改序：栈里保留命令（不被当外部变化清掉）",
                cv1b.UndoStackSize == 1, $"size={cv1b.UndoStackSize}");
            cv1b.UndoCommand.Execute();
            Check("该命令仍可撤销回原序", ReferenceEquals(h1b.Flow.Steps[0], a1b), "");

            // ---- T2 Delete：删选中节点，容器连子树一起走 ----
            var h2 = new Harness();
            var container = h2.If("容器");
            var inner = h2.Leaf("内层");
            container.Children[0].Steps.Add(inner);
            h2.Add(container);
            var cv2 = h2.Canvas;
            h2.Node(container)!.IsSelected = true;
            cv2.DeleteSelectionCommand.Execute();
            Check("容器被移出图纸", !h2.Flow.Steps.Contains(container), "");
            Check("画布节点全部摘除（含子树）", h2.Node(container) == null && h2.Node(inner) == null, "");
            // 2026-10-11 撤销补口：删除作为引用型命令压栈，不再清空撤销栈（见 [G2]）
            Check("删除压栈可撤销（不再清空撤销栈）", cv2.UndoStackSize == 1, $"size={cv2.UndoStackSize}");
            Check("状态栏有删除回执", (cv2.StatusHint ?? string.Empty).Contains("已删除"), $"'{cv2.StatusHint}'");
            cv2.UndoCommand.Execute();
            Check("撤销删除：容器与子树回原位", h2.Flow.Steps.Contains(container)
                && container.Children[0].Steps.Contains(inner) && h2.Node(inner) != null, "");
            cv2.RedoCommand.Execute();
            Check("重做删除：容器再次移出", !h2.Flow.Steps.Contains(container) && h2.Node(container) == null, "");

            // ---- T3 禁用切换：翻转 + 标题后缀与菜单文案即时刷新 ----
            var h3 = new Harness();
            var s = h3.Add(h3.Leaf("步骤"));
            h3.Canvas.RebuildInPlace();
            var node = h3.Node(s)!;
            node.ToggleDisableCommand!.Execute();
            Check("禁用翻转", s.IsDisEnable, "");
            Check("标题带（已禁用）后缀", node.Header.Contains("已禁用"), $"'{node.Header}'");
            Check("菜单文案翻转为启用", node.DisableMenuHeader == "启用", "");
            node.ToggleDisableCommand!.Execute();
            Check("再切回启用", !s.IsDisEnable && node.DisableMenuHeader == "禁用", "");

            // ---- T3b 注释投影：配置界面写 Description，画布节点卡片/注释行即时跟随 ----
            // StepModel 构造把 Description 默认置为插件名，"用户真写了注释"=非空且 ≠ 插件名
            var h3b = new Harness();
            var s3b = h3b.Add(h3b.Leaf("步骤"));
            h3b.Canvas.RebuildInPlace();
            var node3b = h3b.Node(s3b)!;
            Check("默认 Description=插件名时不显示注释行（不与副标题重复）",
                node3b.CommentVisibility == Visibility.Collapsed, $"Comment='{node3b.Comment}'");
            s3b.Description = "车间工位 3 面阵相机";
            Check("注释写入后 Comment 即时跟随（INPC 不等重建）",
                node3b.Comment == "车间工位 3 面阵相机" && node3b.CommentVisibility == Visibility.Visible,
                $"'{node3b.Comment}'");
            Check("卡片悬停提示含名称与注释",
                node3b.CardTooltip.Contains("步骤") && node3b.CardTooltip.Contains("工位 3"),
                $"'{node3b.CardTooltip}'");

            // ---- T3c 多选批量禁用/断点（评审清单 B 包交互补齐）：选中集 >1 时对每个选中步骤节点执行 ----
            var h3c = new Harness();
            var b1c = h3c.Add(h3c.Leaf("批量1"));
            var b2c = h3c.Add(h3c.Leaf("批量2"));
            var b3c = h3c.Add(h3c.Leaf("批量3"));
            var outsider = h3c.Add(h3c.Leaf("未选中"));
            h3c.Canvas.RebuildInPlace();
            h3c.Node(b1c)!.IsSelected = true;
            h3c.Node(b2c)!.IsSelected = true;
            h3c.Node(b3c)!.IsSelected = true;
            Check("T3c 前置：3 个节点处于多选", h3c.Canvas.Nodes.Count(n => n.IsSelected) == 3, "");
            h3c.Node(b1c)!.ToggleDisableCommand!.Execute();
            Check("T3c 多选批量禁用：一次全禁用",
                b1c.IsDisEnable && b2c.IsDisEnable && b3c.IsDisEnable, "");
            Check("T3c 批量禁用：未选中不受影响", !outsider.IsDisEnable, "");
            int versionBeforeBreakpoint = h3c.Flow.Version;
            h3c.Node(b1c)!.ToggleBreakpointCommand!.Execute();
            Check("T3c 多选批量断点：一次全设断点",
                b1c.IsBreakpoint && b2c.IsBreakpoint && b3c.IsBreakpoint, "");
            Check("T3c 批量断点不推版本（[RuntimeState] 口径不变）",
                h3c.Flow.Version == versionBeforeBreakpoint, $"version={h3c.Flow.Version}");
            h3c.Node(b1c)!.ToggleBreakpointCommand!.Execute();
            Check("T3c 批量断点可整体反向", !b1c.IsBreakpoint && !b2c.IsBreakpoint && !b3c.IsBreakpoint, "");

            // 单节点行为保持：清掉多选后，右键靶不依赖选中态（T3 既有口径不被批量改造误伤）
            h3c.Node(b1c)!.IsSelected = false;
            h3c.Node(b2c)!.IsSelected = false;
            h3c.Node(b3c)!.IsSelected = false;
            h3c.Node(outsider)!.ToggleDisableCommand!.Execute();
            Check("T3c 无多选：维持单节点行为（只打右键靶）",
                outsider.IsDisEnable, "");

            // ---- T4 连线整批解绑：一对模块的多条绑定打包成一个撤销单元 ----
            var h4 = new Harness();
            var p4 = h4.Add(h4.Leaf("生产"));
            var q4 = h4.Add(h4.Leaf("消费"));
            Harness.RawLink(q4, "In", p4, "Out");
            // 同一对模块的第二条绑定（端口无需真实存在，BuildLinks 按 LinkedSources 全量聚合）
            q4.SetLink("Extra", new LinkReference(LinkKind.StepPort, p4.StepID, "Out", "生产.Out"));
            var cv4 = h4.Canvas;
            cv4.ShowDataLinks = true;   // 本组测整批解绑的聚合线，不测显隐开关
            var link = h4.Link(p4, q4);
            Check("前置：一对模块聚合成一条线、两条绑定",
                link != null && link.Bindings.Count == 2, $"bindings={link?.Bindings.Count}");
            link!.UnbindAllCommand!.Execute();
            Check("整批解绑后图纸无绑定", q4.LinkedSources.Count == 0, $"remain={q4.LinkedSources.Count}");
            Check("整批解绑压一个撤销单元", cv4.UndoStackSize == 1, $"size={cv4.UndoStackSize}");
            cv4.UndoCommand.Execute();
            Check("一次撤销恢复两条绑定", q4.LinkedSources.Count == 2, $"restored={q4.LinkedSources.Count}");

            // ---- T5 改名即时刷新（画布不订阅属性变更，节点自己跟随模型）----
            var h5 = new Harness();
            var s5 = h5.Add(h5.Leaf("原名"));
            h5.Canvas.RebuildInPlace();
            s5.StepName = "改名后";
            Check("流程栏改名后画布标题即时跟随", h5.Node(s5)!.Header == "改名后", $"'{h5.Node(s5)!.Header}'");

            // ---- T6 顺序链开关：关掉后只画数据线（数据线只画真实绑定的口径不变）----
            var h6 = new Harness();
            h6.Add(h6.Leaf("A"));
            var b6 = h6.Add(Harness.WithInput(h6.Leaf("B"), "In"));
            Harness.RawLink(b6, "In", h6.Flow.Steps[0], "Out");
            var cv6 = h6.Canvas;
            Check("默认态：数据线一条不画（开关默认关）", !cv6.ShowDataLinks && cv6.DataLinkCount == 0, $"{cv6.DataLinkCount}");
            cv6.ShowDataLinks = true;   // 本组测顺序链开关，数据线显式打开作对照
            Check("默认显示顺序链", cv6.Links.Any(l => l.IsOrderLink), $"links={cv6.Links.Count}");
            cv6.ShowOrderLinks = false;
            Check("关闭顺序链后 Links 只剩数据线",
                cv6.Links.Count == 1 && !cv6.Links[0].IsOrderLink, $"links={cv6.Links.Count}");
            cv6.ShowOrderLinks = true;
            Check("重新打开顺序链恢复", cv6.Links.Any(l => l.IsOrderLink), "");

            // ---- T7 连线进出边按两端几何选向（消费方被拖到左侧 → 左出右入，绕行就近）----
            var h7 = new Harness();
            var a7 = h7.Add(h7.Leaf("A"));
            var b7 = h7.Add(Harness.WithInput(h7.Leaf("B"), "In"));
            Harness.RawLink(b7, "In", a7, "Out");
            var cv7 = h7.Canvas;
            cv7.ShowDataLinks = true;   // 本组测连线进出边选向，线必须可见
            var link7 = h7.Link(a7, b7)!;
            // AutoLayout 竖排同列（dx=0 走默认左出右入）；真实拖动路径：Location 改动 → UpdateLinkAnchors 同步方向
            h7.Node(b7)!.Location = new Point(h7.Node(a7)!.Location.X + 600, h7.Node(b7)!.Location.Y);
            Check("消费方拖到右侧：右出左入",
                link7.SourcePosition == Nodify.ConnectorPosition.Right
                && link7.TargetPosition == Nodify.ConnectorPosition.Left,
                $"src={link7.SourcePosition} tgt={link7.TargetPosition}");
            h7.Node(b7)!.Location = new Point(h7.Node(a7)!.Location.X - 600, h7.Node(b7)!.Location.Y);
            Check("消费方拖到左侧：左出右入（绕行就近，不横穿画布）",
                link7.SourcePosition == Nodify.ConnectorPosition.Left
                && link7.TargetPosition == Nodify.ConnectorPosition.Right,
                $"src={link7.SourcePosition} tgt={link7.TargetPosition}");
            // 真实拖动路径：改 Location → OnNodePropertyChanged → UpdateLinkAnchors 同步方向

            // ---- T8 拖动分支内步骤后容器框贴合同步（拖动中不重算、抬手一次重建）----
            // 拖动距离刻意控制在泳道内（不触发改序/跨分支提交——那是另一条已验证的路径）
            var h8 = new Harness();
            var if8 = h8.If("容器");
            var inner8 = h8.Leaf("内层");
            if8.Children[0].Steps.Add(inner8);
            h8.Add(if8);
            var cv8 = h8.Canvas;
            double frame0 = h8.Node(if8)!.LaneHeight;
            double frameTop0 = h8.Node(if8)!.Location.Y;
            h8.Node(inner8)!.IsSelected = true;
            cv8.ItemsDragStartedCommand.Execute(null);
            h8.Node(inner8)!.Location = new Point(h8.Node(inner8)!.Location.X, h8.Node(inner8)!.Location.Y + 40);
            cv8.ItemsDragCompletedCommand.Execute(null);
            double frame1 = h8.Node(if8)!.LaneHeight;
            // 贴合判据：框体跟着被拖内容走、且仍完整包住它。本图式里空泳道的头带与有内容泳道对齐，
            // 单分支内容整体下移 → 框体整体平移（高不变），所以不能只看高度增量
            Check("拖动分支内步骤后容器框贴合同步（框体跟随内容、仍包住被拖步骤）",
                Inside(NodeBox(h8.Node(if8)!), NodeBox(h8.Node(inner8)!))
                && h8.Node(if8)!.Location.Y > frameTop0 + 30,
                $"frame.top {frameTop0:0} → {h8.Node(if8)!.Location.Y:0}，h {frame0:0} → {frame1:0}");
            Check("拖拽收尾重建后画布完整（步骤仍在分支内）",
                h8.Node(inner8) != null
                && h8.Topology().TryGet(inner8.StepID, out var pos8)
                && ReferenceEquals(pos8!.Owner, if8.Children[0].Steps), "");
        }

        // ==================================================================
        //  [U] 十层嵌套压力图式（结构正确性 + 重建性能）
        //  与渲染探针场景 D 同构：10 层 If 嵌套、33 个算子、21 条数据线。
        //  结构断言钉死：层数/算子数/连线数/几何包含关系；性能数字只打印不断言（机器抖动）。
        // ==================================================================
        private static void TenLevelNestingStress()
        {
            Section("[U] 十层嵌套压力图式（10 层 / 33 算子）");

            var h = new Harness();
            var capture = h.Leaf("图像采集_0");
            var preprocess = h.Leaf("图像预处理_0");
            var summary = h.Leaf("结果汇总_0");

            ConditionStep? inner = null;
            var leaves = new List<StepModel>();
            var ifSteps = new List<ConditionStep>();
            for (int level = 10; level >= 1; level--)
            {
                var ifStep = h.If($"分档_{level}");
                var leafA = h.Leaf($"测量_{level}A");
                var leafB = h.Leaf($"测量_{level}B");
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
            foreach (var leaf in leaves)
                Harness.RawLink(leaf, "In", capture, "Out");
            Harness.RawLink(inner!, "In", preprocess, "Out");

            var cv = h.Canvas;   // 首次构建（AutoLayout + 超深自动折叠）

            // ifSteps 按 level 10→1 加入：ifSteps[10-lv] = 分档_lv（分档_1 深度 1）
            ConditionStep IfOf(int lv) => ifSteps[10 - lv];

            // ---- 超深自动折叠（AutoCollapseDepth=3）：1~3 层展开、4~10 层默认折叠 ----
            // 折叠标记查布局库：画布节点只含可见节点——分档_4 折叠后 5~10 层根本没有节点，
            // 查画布会把"被祖先藏起来"误读成"没折叠"
            bool CollapsedFlagOf(int lv) => h.Flow.Layout.Find(IfOf(lv).StepID)?.Collapsed == true;
            Check("1~3 层容器展开（阈值内）",
                Enumerable.Range(1, 3).All(lv => !CollapsedFlagOf(lv)), "");
            Check("4~10 层容器默认折叠（超深自动折叠）",
                Enumerable.Range(4, 7).All(CollapsedFlagOf),
                string.Join(",", Enumerable.Range(1, 10).Select(lv => $"{lv}:{(CollapsedFlagOf(lv) ? "折" : "展")}")));
            Check("折叠态：深层子孙不渲染（渲染步骤数远小于全量 23）",
                h.Steps().Count < 23 && h.Steps().Count > 0, $"steps={h.Steps().Count}");
            Check("折叠态无告警（被折叠藏的绑定不算非法/降级）",
                cv.IllegalLinkCount == 0 && cv.DeferredLinkCount == 0,
                $"illegal={cv.IllegalLinkCount} deferred={cv.DeferredLinkCount} hidden={cv.HiddenLinkCount}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            cv.RebuildInPlace();
            sw.Stop();
            Console.WriteLine($"    [perf] RebuildInPlace（10 层 / 33 算子 / 折叠态）: {sw.ElapsedMilliseconds} ms");

            // ---- 手动展开全部折叠容器：全量渲染断言（自动折叠前的旧口径）----
            foreach (var lv in Enumerable.Range(4, 7))
                h.Flow.Layout.ToggleCollapsed(IfOf(lv).StepID);
            cv.RebuildInPlace();

            Check("全部展开后 33 个算子全上画布（23 步骤 + 10 容器）",
                h.Steps().Count == 23 && h.Containers().Count == 10,
                $"steps={h.Steps().Count} containers={h.Containers().Count}");
            // 数据线默认隐藏：先钉默认态（一条不画），再显式打开走"连线构建"的既有口径
            Check("默认态：数据线一条不画（ShowDataLinks 默认 false）",
                !cv.ShowDataLinks && cv.DataLinkCount == 0, $"{cv.DataLinkCount}");
            cv.ShowDataLinks = true;
            Check("全部展开后数据线 21 条（20 叶子 + 1 容器）", cv.DataLinkCount == 21, $"{cv.DataLinkCount}");
            Check("全部展开后顺序链 12 条（顶层 3 + 每层 If 分支 9）", cv.OrderLinkCount == 12, $"{cv.OrderLinkCount}");
            Check("无非法 / 无降级", cv.IllegalLinkCount == 0 && cv.DeferredLinkCount == 0,
                $"illegal={cv.IllegalLinkCount} deferred={cv.DeferredLinkCount}");

            // 几何包含：最外层容器框必须包住最内层容器框，且内层在图纸里确实嵌套 10 层
            // ifSteps 按 level 10→1 加入：ifSteps[0]=分档_10（最内），ifSteps[^1]=分档_1（最外）
            var outerIf = ifSteps[^1];       // 分档_1
            var deepestIf = ifSteps[0];      // 分档_10
            var outerNode = h.Node(outerIf)!;
            var deepNode = h.Node(deepestIf)!;
            Check("外层容器框包住内层容器框",
                outerNode.LaneWidth > deepNode.LaneWidth && outerNode.LaneHeight > deepNode.LaneHeight,
                $"outer={outerNode.LaneWidth:0}×{outerNode.LaneHeight:0} deep={deepNode.LaneWidth:0}×{deepNode.LaneHeight:0}");

            Check("最深层容器在图纸里的嵌套深度 = 10 层",
                NestingDepth(deepestIf, h.Flow.Steps) == 10, $"{NestingDepth(deepestIf, h.Flow.Steps)}");

            // 嵌套链上每一步（含最深层叶子）都被画布渲染
            // leaves 顺序：level 10→1，leaves[0]=测量_10A（最深层的 A 叶）
            Check("全部展开后最深层叶子（测量_10A）有节点", h.Node(leaves[0]) != null, "");
            Check("顺序链开关压力场景下同样可关",
                RunOrderLinkToggle(cv), $"links={cv.Links.Count}");

            // ---- 深容器（第 4 层）折叠交互：点一下真的翻、整理逐容器保持现场 ----
            // 病根（2026-10-09 修）：TidyLayoutCore 恢复折叠现场用的是 ToggleCollapsed（翻转），
            // 而重排会把深于 AutoCollapseDepth 的容器自动折叠 → 再翻一次等于翻回展开：
            // 表现为"第 4 层及更深的容器，展开/折叠点击与整理都反着来"。
            // 起点：上面"手动展开全部折叠容器"刚把 4~10 层都置成展开，本组从"折叠→展开"两个方向各测一次。
            // 放在本段最后：上面那批断言依赖全量渲染，本组会把 4 层重新折叠回去。
            string FlagDump() => string.Join(",", Enumerable.Range(1, 10).Select(lv => $"{lv}:{(CollapsedFlagOf(lv) ? "折" : "展")}"));

            cv.ToggleCollapseCommand.Execute(h.Node(IfOf(4)));
            Check("第 4 层容器点一下能真的折叠（深容器折叠点击生效）",
                CollapsedFlagOf(4), FlagDump());

            cv.ToggleCollapseCommand.Execute(h.Node(IfOf(4)));
            Check("第 4 层容器再点一下能真的展开（此前被 Toggle 翻转吞掉）",
                !CollapsedFlagOf(4), FlagDump());

            cv.TidyLayoutCommand.Execute();
            Check("整理后折叠现场逐容器保持（4~10 层仍是展开，整理不擅自折叠）",
                Enumerable.Range(4, 7).All(lv => !CollapsedFlagOf(lv)), FlagDump());

            cv.ToggleCollapseCommand.Execute(h.Node(IfOf(4)));
            cv.TidyLayoutCommand.Execute();
            Check("第 4 层折叠后再整理，仍是折叠（整理不把它翻回去）",
                CollapsedFlagOf(4), FlagDump());
        }

        /// <summary>统计某步骤在图纸树里的嵌套深度（顶层=1）</summary>
        private static int NestingDepth(StepModel target, System.Collections.ObjectModel.ObservableCollection<StepModel> roots)
        {
            return Walk(roots, 1);

            int Walk(System.Collections.ObjectModel.ObservableCollection<StepModel> list, int depth)
            {
                foreach (var step in list)
                {
                    if (ReferenceEquals(step, target)) return depth;
                    if (step is IContainerStep container && container.Children != null)
                    {
                        foreach (var branch in container.Children)
                        {
                            if (branch?.Steps == null) continue;
                            int found = Walk(branch.Steps, depth + 1);
                            if (found > 0) return found;
                        }
                    }
                }
                return -1;
            }
        }

        private static bool RunOrderLinkToggle(FlowCanvasViewModel cv)
        {
            cv.ShowDataLinks = true;   // 本组测顺序链开关，数据线要保持 21 条作对照
            int before = cv.OrderLinkCount;
            cv.ShowOrderLinks = false;
            bool off = cv.OrderLinkCount == 0 && cv.DataLinkCount == 21;
            cv.ShowOrderLinks = true;
            return off && before > 0 && cv.OrderLinkCount > 0;
        }

        // ==================================================================
        //  [X] 数据线显隐（默认隐藏 + 非法线例外）/ 画布编辑入口 / 工具箱落点与工厂
        //  守的是四项落地：A 数据线默认隐藏、B/C 双击与右键的模块参数入口、
        //  D 工具箱拖入（工厂抽取 + 落点命中 + 顶层插入）。
        // ==================================================================
        private static void DataLinkVisibilityAndCanvasEditing()
        {
            Section("[X] 数据线显隐与画布编辑入口");

            // ---- X1 默认态与开关幂等：只显示执行顺序；数据线可开可关 ----
            var h = new Harness();
            var a = h.Leaf("A");
            var b = h.Leaf("B");
            var d = h.Leaf("C");
            Harness.WithInput(b, "In");
            Harness.WithInput(d, "In");
            h.Add(a); h.Add(b); h.Add(d);
            Harness.RawLink(b, "In", a, "Out");
            Harness.RawLink(d, "In", b, "Out");
            var c = h.Canvas;

            Check("默认态：ShowOrderLinks=true / ShowDataLinks=false",
                c.ShowOrderLinks && !c.ShowDataLinks, $"{c.ShowOrderLinks}/{c.ShowDataLinks}");
            Check("默认态：数据线 0 条、顺序链照常 >0",
                c.DataLinkCount == 0 && c.OrderLinkCount > 0, $"数据线={c.DataLinkCount} 顺序链={c.OrderLinkCount}");
            c.ShowDataLinks = true;
            Check("打开数据线：两层数据线各一根（2 条）", c.DataLinkCount == 2, $"{c.DataLinkCount}");
            c.ShowDataLinks = false;
            Check("再关：又回到 0（幂等）", c.DataLinkCount == 0 && c.OrderLinkCount > 0, $"数据线={c.DataLinkCount}");

            // ---- X2 非法线例外：数据线关闭时红虚线照常渲染（错误提示不是噪声） ----
            var h2 = new Harness();
            var p2 = h2.Leaf("A");
            var q2 = h2.Leaf("B");
            var r2 = h2.Leaf("C");
            Harness.WithInput(q2, "In");
            h2.Add(p2); h2.Add(q2); h2.Add(r2);
            Harness.RawLink(q2, "In", p2, "Out");   // 合法：A → B
            Harness.RawLink(p2, "In", r2, "Out");   // 非法：C 排在 A 之后（执行顺序倒序）
            var c2 = h2.Canvas;

            Check("混合图式：数据线关闭时普通线隐藏、非法链仍在（红虚线例外）",
                !c2.ShowDataLinks && c2.IllegalLinkCount == 1
                && c2.DataLinkCount == 1 && c2.Links.Single(l => !l.IsOrderLink).IsIllegal
                && c2.WarningVisibility == Visibility.Visible,
                $"数据线={c2.DataLinkCount} 非法={c2.IllegalLinkCount}");
            c2.ShowDataLinks = true;
            Check("打开数据线：普通线回来（非法线一直在）",
                c2.DataLinkCount == 2 && c2.Links.Count(l => !l.IsOrderLink && l.IsIllegal) == 1,
                $"数据线={c2.DataLinkCount}");

            // ---- X3 画布编辑入口：节点 VM 的「模块参数」命令（双击/右键共用）存在、泳道空跑 ----
            var h3 = new Harness();
            var cont3 = h3.If("容器");
            h3.Add(cont3);
            var cv3 = h3.Canvas;
            var node3 = h3.Node(cont3)!;
            Check("节点挂上「模块参数」入口命令", node3.OpenModuleParametersCommand != null, "");

            bool thrown3 = false;
            try { node3.OpenModuleParametersCommand!.Execute(); }
            catch { thrown3 = true; }
            Check("（headless 无容器）执行打开命令不抛：静默空跑", !thrown3, "");

            var lane3 = h3.Lanes().FirstOrDefault();
            bool laneThrown = false;
            try { cv3.OpenModuleParameters(lane3); }
            catch { laneThrown = true; }
            Check("泳道节点调打开入口空跑不抛（Model 为 null 的守卫）",
                !laneThrown && lane3 != null && lane3.Model == null, "");

            // ---- X4 StepFactory：五种 ModuleTypeName 的类型分派（画布 Drop 与流程栏 Drop 共用一份） ----
            Check("BuiltIn_While → WhileStep",
                StepFactory.CreateFromTool(NewTool("BuiltIn_While", container: true), "步骤_0") is WhileStep, "");
            Check("BuiltIn_For → ForStep",
                StepFactory.CreateFromTool(NewTool("BuiltIn_For", container: true), "步骤_0") is ForStep, "");
            var par4 = StepFactory.CreateFromTool(NewTool("BuiltIn_Parallel", container: true), "步骤_0") as ParallelStep;
            Check("BuiltIn_Parallel → ParallelStep（显式 ExecutionMode=Parallel，口径同流程栏）",
                par4 != null && par4.ExecutionMode == VisionMaster.Models.ParallelExecutionMode.Parallel,
                $"{par4?.ExecutionMode}");
            Check("BuiltIn_If → ConditionStep（容器兜底）",
                StepFactory.CreateFromTool(NewTool("BuiltIn_If", container: true), "步骤_0") is ConditionStep, "");
            // 等待节点是**非容器但不是算子**：工厂必须先认 ModuleTypeName，再走 !IsContainer 兜底，
            // 写反了会静默变成 ActionStep（编译期才报"找不到插件 BuiltIn_Wait"）。
            Check("BuiltIn_Wait（非容器）→ WaitStep",
                StepFactory.CreateFromTool(NewTool("BuiltIn_Wait", container: false), "步骤_0") is WaitStep, "");
            var leaf4 = StepFactory.CreateFromTool(NewTool("VM.CanvasStub.Leaf", container: false), "步骤_4");
            Check("非容器 → ActionStep，且步骤名原样落上",
                leaf4 is ActionStep && leaf4.StepName == "步骤_4", $"{leaf4.GetType().Name}/{leaf4.StepName}");

            // ---- X5 落点命中：FindDeepestContainerAt 取最深容器 / 框外为 null ----
            var h5 = new Harness();
            var par5 = h5.Parallel("并行组");
            var innerIf5 = h5.If("内层If");
            par5.Children[0].Steps.Add(innerIf5);
            h5.Add(par5);
            var cv5 = h5.Canvas;
            var parNode5 = h5.Node(par5)!;
            var ifNode5 = h5.Node(innerIf5)!;

            Check("点在 If 框内 → 返回 If（最深）",
                ReferenceEquals(cv5.FindDeepestContainerAt(new Point(
                    ifNode5.Location.X + ifNode5.LaneWidth / 2,
                    ifNode5.Location.Y + ifNode5.LaneHeight / 2)), ifNode5), "");

            var parOnlyPoint = new Point(parNode5.Location.X + 3, parNode5.Location.Y + 3);
            Check("点在并行组框内但 If 框外 → 返回并行组",
                ReferenceEquals(cv5.FindDeepestContainerAt(parOnlyPoint), parNode5),
                $"命中={cv5.FindDeepestContainerAt(parOnlyPoint)?.Model?.StepName ?? "null"}");

            Check("点在空白处 → null", cv5.FindDeepestContainerAt(new Point(-5000, -5000)) == null, "");

            // ---- X6 画布拖放 DropToolAt：容器内进第一条分支末尾 / 顶层按 Y 插入 / Version 推进 ----
            var h6 = new Harness();
            var cont6 = h6.If("容器");
            h6.Add(cont6);
            var cv6 = h6.Canvas;
            var contNode6 = h6.Node(cont6)!;
            int ver6 = h6.Flow.Version;

            bool dropped6 = cv6.DropToolAt(
                NewTool("BuiltIn_While", container: true),
                new Point(contNode6.Location.X + contNode6.LaneWidth / 2, contNode6.Location.Y + 30));
            Check("拖到容器框内 → 落进第一条分支末尾（WhileStep）",
                dropped6 && cont6.Children[0].Steps.Count == 1 && cont6.Children[0].Steps[0] is WhileStep,
                $"分支步数={cont6.Children[0].Steps.Count}");
            Check("容器内落点不进主流程 / Version 推进（结构变更）",
                h6.Flow.Steps.Count == 1 && h6.Flow.Version > ver6, $"version {ver6}→{h6.Flow.Version}");
            Check("状态栏给拖放回执", (cv6.StatusHint ?? "").Contains("已添加"), $"'{cv6.StatusHint}'");

            cv6.DropToolAt(
                NewTool("VM.CanvasStub.Leaf", container: false),
                new Point(contNode6.Location.X, contNode6.Location.Y + contNode6.LaneHeight + 40));
            Check("顶层落点：按中心 Y 插到容器之后",
                h6.Flow.Steps.Count == 2 && h6.Flow.Steps[1] is ActionStep,
                string.Join(",", h6.Flow.Steps.Select(s => s.StepName)));
        }

        /// <summary>造一个工具箱模板桩（[X] 段工厂/落点断言用）</summary>
        private static ToolItemModel NewTool(string typeName, bool container) => new()
        {
            Id = Guid.NewGuid(),
            Name = "模板",
            Icon = "\uE700",
            ModuleTypeName = typeName,
            IsContainer = container,
        };

        // ==================================================================
        //  [G1] 画布编辑门禁（EditGate，2026-10-11 三视角评审 P0-1）：
        //  豁免表单一出口（断点全放行 / 参数仅暂停放行 / 其余运行中全拦）+ 画布拦截点
        //  驱动手法照既有运行锁断言：GlobalEventBus.Publish(MainRunState.X) + finally 复位 NotStarted
        // ==================================================================
        private static void EditGateChecks()
        {
            Section("[G1] 画布编辑门禁（EditGate）");

            // ---- 门禁级：豁免表 ----
            var gate = new EditGate();
            gate.Activate();
            try
            {
                Check("G1 未启动：全部编辑动作放行且不锁",
                    gate.IsEditable(ModuleEditAction.StructuralEdit)
                    && gate.IsEditable(ModuleEditAction.ModuleParameters)
                    && gate.IsEditable(ModuleEditAction.ToggleBreakpoint)
                    && !gate.IsRunLocked, "");

                GlobalEventBus.Publish(MainRunState.RunningContinuous);
                Check("G1 运行中：结构编辑拦 / 断点放行 / 参数拦 / IsRunLocked=true",
                    !gate.IsEditable(ModuleEditAction.StructuralEdit)
                    && gate.IsEditable(ModuleEditAction.ToggleBreakpoint)
                    && !gate.IsEditable(ModuleEditAction.ModuleParameters)
                    && gate.IsRunLocked, "");

                GlobalEventBus.Publish(MainRunState.Paused);
                Check("G1 已暂停：模块参数放行 / 结构编辑仍拦",
                    gate.IsEditable(ModuleEditAction.ModuleParameters)
                    && !gate.IsEditable(ModuleEditAction.StructuralEdit), "");

                GlobalEventBus.Publish(MainRunState.NotStarted);
                Check("G1 回到未启动：全放行（订阅链路复位）",
                    gate.IsEditable(ModuleEditAction.StructuralEdit)
                    && gate.IsEditable(ModuleEditAction.ModuleParameters)
                    && !gate.IsRunLocked, "");
            }
            finally
            {
                GlobalEventBus.Publish(MainRunState.NotStarted);   // 复位全局总线，别把锁漏给后续断言
                gate.Deactivate();
            }

            // ---- 画布级：拦截点（拖入 / 删除）与豁免（断点）----
            var h = new Harness();
            var a = h.Add(h.Leaf("A"));
            var cv = h.Canvas;

            GlobalEventBus.Publish(MainRunState.RunningOnce);
            try
            {
                bool dropped = cv.DropToolAt(NewTool("VM.CanvasStub.Leaf", container: false), new Point(-5000, -5000));
                Check("G1 运行中拖入：不插入（返回 false、图纸不变）",
                    !dropped && h.Flow.Steps.Count == 1, $"steps={h.Flow.Steps.Count}");
                Check("G1 运行中拖入给了拦截提示", (cv.StatusHint ?? "").Contains("运行中"), $"'{cv.StatusHint}'");

                h.Node(a)!.IsSelected = true;
                cv.DeleteSelectionCommand.Execute();
                Check("G1 运行中删除：图纸不动、命令不压栈",
                    h.Flow.Steps.Contains(a) && cv.UndoStackSize == 0, $"size={cv.UndoStackSize}");

                // 断点豁免：任何运行态都可切（先清再切，断言翻成 true）
                a.IsBreakpoint = false;
                cv.ToggleBreakpointCommand.Execute(h.Node(a));
                Check("G1 运行中断点仍可切换（豁免表：断点全放行）", a.IsBreakpoint, "");
                a.IsBreakpoint = false;

                // 参数面板：headless 无弹窗服务本来就不开窗——断"运行中调用了也无副作用、不抛"
                bool threw = false;
                try { cv.OpenModuleParameters(h.Node(a)); }
                catch { threw = true; }
                Check("G1 运行中开参数：调用无副作用不抛", !threw, "");
            }
            finally
            {
                GlobalEventBus.Publish(MainRunState.NotStarted);
            }

            Check("G1 停止后恢复可编辑：拖入成功且压撤销栈",
                cv.DropToolAt(NewTool("VM.CanvasStub.Leaf", container: false), new Point(-5000, -5000))
                && h.Flow.Steps.Count == 2 && cv.UndoStackSize == 1,
                $"steps={h.Flow.Steps.Count} size={cv.UndoStackSize}");
        }

        // ==================================================================
        //  [G2] 撤销补口（2026-10-11）：引用型 Add/Delete 命令——
        //  与既有 Reorder/MoveBranch 的「下标型」差别：本组记录**元素引用本体**，
        //  owner 内容被外部动过时按引用判存在性静默降级，不错拿别的步骤
        // ==================================================================
        private static void AddDeleteUndoChecks()
        {
            Section("[G2] 撤销补口（引用型 Add / Delete 命令）");

            // ---- 拖入 → Undo → 消失 → Redo → 回到原位 ----
            var h = new Harness();
            h.Add(h.Leaf("A"));
            var cv = h.Canvas;
            bool dropped = cv.DropToolAt(NewTool("VM.CanvasStub.Leaf", container: false), new Point(-5000, -5000));
            var added = h.Flow.Steps[0];   // 落点 Y=-5000 → 插在最上
            Check("G2 前置：拖入已上图纸且压撤销命令",
                dropped && ReferenceEquals(h.Flow.Steps[0], added) && cv.UndoStackSize == 1,
                $"steps={h.Flow.Steps.Count} size={cv.UndoStackSize}");
            cv.UndoCommand.Execute();
            Check("G2 撤销拖入：步骤消失", !h.Flow.Steps.Contains(added) && h.Flow.Steps.Count == 1, "");
            Check("G2 撤销拖入：重做栈 = 1", cv.RedoStackSize == 1, "");
            cv.RedoCommand.Execute();
            Check("G2 重做拖入：步骤回到原位（下标 0）", ReferenceEquals(h.Flow.Steps[0], added), "");

            // ---- 删除（容器子树）→ Undo → 原位恢复 + 指向被删步骤的连线复活 → Redo → 再删 ----
            var h2 = new Harness();
            var consumer = Harness.WithInput(h2.Leaf("消费"), "In");
            var container = h2.If("容器");
            var inner = h2.Leaf("内层");
            container.Children[0].Steps.Add(inner);
            h2.Add(consumer);
            h2.Add(container);
            Harness.RawLink(consumer, "In", inner, "Out");   // 跨分支取数（非法红线）——正好当"指向被删步骤的连线"
            var cv2 = h2.Canvas;
            Check("G2 前置：容器内步骤 → 顶层 的连线在（跨分支非法线照常画）",
                h2.Link(inner, consumer) != null, "");

            h2.Node(container)!.IsSelected = true;
            cv2.DeleteSelectionCommand.Execute();
            Check("G2 容器删除：子树一并移出、连线降级计数",
                !h2.Flow.Steps.Contains(container) && cv2.DeferredLinkCount == 1,
                $"deferred={cv2.DeferredLinkCount}");
            Check("G2 删除压栈（不清空撤销栈）", cv2.UndoStackSize == 1, $"size={cv2.UndoStackSize}");

            cv2.UndoCommand.Execute();
            Check("G2 撤销删除：容器回原位（下标 1）、内层步骤回来",
                h2.Flow.Steps.Count == 2 && ReferenceEquals(h2.Flow.Steps[1], container)
                && container.Children[0].Steps.Contains(inner), "");
            Check("G2 撤销删除：指向被删步骤的连线随之复活（Deferred 归零）",
                cv2.DeferredLinkCount == 0 && h2.Link(inner, consumer) != null, "");

            cv2.RedoCommand.Execute();
            Check("G2 重做删除：容器再次移出、连线再次降级",
                !h2.Flow.Steps.Contains(container) && cv2.DeferredLinkCount == 1, "");

            // ---- 删除不清空撤销栈：之前的改序仍可 Undo（LIFO：先撤删除、再撤改序）----
            var h3 = new Harness();
            var a3 = h3.Add(h3.Leaf("A"));
            var b3 = h3.Add(h3.Leaf("B"));
            var cv3 = h3.Canvas;
            var reorder = FlowCanvasExtensions.CreateReorderCommand(h3.Flow.Steps, 0, 1);
            FlowCanvasExtensions.InvokeRedo(reorder);
            cv3.PushUndoReflection(reorder);
            Check("G2 前置：改序已压栈（[B,A]）",
                cv3.UndoStackSize == 1 && ReferenceEquals(h3.Flow.Steps[0], b3), "");

            h3.Node(b3)!.IsSelected = true;
            cv3.DeleteSelectionCommand.Execute();
            Check("G2 删除后撤销栈不清空（改序命令保留）", cv3.UndoStackSize == 2, $"size={cv3.UndoStackSize}");
            cv3.UndoCommand.Execute();
            Check("G2 先撤销删除：B 回来（在改序后的下标 0）", ReferenceEquals(h3.Flow.Steps[0], b3), "");
            cv3.UndoCommand.Execute();
            Check("G2 再撤销改序：回到原序 [A,B]",
                ReferenceEquals(h3.Flow.Steps[0], a3) && ReferenceEquals(h3.Flow.Steps[1], b3), "");
        }

        // ==================================================================
        //  [G3] 流程栏搜索定位：深层命中 / 循环下一个 / 无命中不换选中
        // ==================================================================
        private static void SearchLocateChecks()
        {
            Section("[G3] 流程栏搜索定位");

            var h = new Harness();
            h.Add(h.Leaf("采集"));
            var cond = h.If("判断");
            var deep1 = h.Leaf("检测-左");
            var deep2 = h.Leaf("检测-右");
            cond.Children[0].Steps.Add(deep1);
            cond.Children[0].Steps.Add(deep2);
            h.Add(cond);

            var vm = new ProcessViewModel(h.Workspace, new SilentDialogService());
            try
            {
                vm.SearchText = "检测";
                vm.SearchNextCommand.Execute(null);
                Check("G3 命中深层步骤（第一个）：CurrentStep 切过去",
                    ReferenceEquals(h.Workspace.CurrentStep, deep1),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                vm.SearchNextCommand.Execute(null);
                Check("G3 再按一次：跳到第二个命中",
                    ReferenceEquals(h.Workspace.CurrentStep, deep2),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                vm.SearchNextCommand.Execute(null);
                Check("G3 到尾回绕：回到第一个命中", ReferenceEquals(h.Workspace.CurrentStep, deep1), "");

                vm.SearchText = "不存在的关键字XYZ";
                vm.SearchNextCommand.Execute(null);
                Check("G3 无命中：不换选中", ReferenceEquals(h.Workspace.CurrentStep, deep1), "");

                // 「上一个」（评审清单 B 包交互补齐）：同一次匹配列表反向回绕
                vm.SearchText = "检测";   // 命中列表 [检测-左, 检测-右]，当前停在 deep1（头）
                vm.SearchPreviousCommand.Execute(null);
                Check("G3 上一个：从头部反向回绕到最后一个命中",
                    ReferenceEquals(h.Workspace.CurrentStep, deep2),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                vm.SearchNextCommand.Execute(null);
                Check("G3 先下一个再上一个：回到原命中", ReferenceEquals(h.Workspace.CurrentStep, deep1), "");
            }
            finally
            {
                vm.Deactivate();
            }
        }

        // ==================================================================
        //  [G4] 失败自动跳转：新失败才跳、同一失败不重复跳、新一轮运行基线重置
        //  探测层 internal static 直调（与 TickRunningTimes 同款）；
        //  定时器一轮的实际动作经 internal RunRuntimeTick 直驱，不必等 Dispatcher 泵
        // ==================================================================
        private static void FailureJumpChecks()
        {
            Section("[G4] 失败自动跳转");

            var h = new Harness();
            var a = h.Add(h.Leaf("A"));
            var b = h.Add(h.Leaf("B"));

            var vm = new ProcessViewModel(h.Workspace, new SilentDialogService());
            try
            {
                a.State = StepState.Failed;
                var baseline = new HashSet<Guid>();
                var probe = ProcessViewModel.DetectNewFailures(h.Flow.Steps, baseline);
                Check("G4 探测：新失败被返回并记入基线",
                    ReferenceEquals(probe, a) && baseline.Contains(a.StepID), "");
                probe = ProcessViewModel.DetectNewFailures(h.Flow.Steps, baseline);
                Check("G4 探测：同一失败不重复报（基线拦截）", probe == null, "");

                vm.RunRuntimeTick();
                Check("G4 定时器一轮：当前步骤切到失败步骤",
                    ReferenceEquals(h.Workspace.CurrentStep, a),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                b.State = StepState.Failed;
                vm.RunRuntimeTick();
                Check("G4 新失败出现：切到新的失败",
                    ReferenceEquals(h.Workspace.CurrentStep, b),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                vm.RunRuntimeTick();
                Check("G4 再跑一轮：不重复跳（仍在 B）", ReferenceEquals(h.Workspace.CurrentStep, b), "");

                // 新一轮运行开始（NotStarted → Running*）→ 基线清零：旧失败重新算"新失败"。
                // a、b 都是上轮的旧失败且都仍是 Failed 态——基线重置后按扫描序跳第一个（a），
                // 先把当前步骤切到 b，跳转才可观测
                GlobalEventBus.Publish(MainRunState.RunningContinuous);
                h.Workspace.SwitchStep(b);
                vm.RunRuntimeTick();
                Check("G4 新一轮运行（基线重置）：旧失败重新算新失败、按扫描序跳第一个",
                    ReferenceEquals(h.Workspace.CurrentStep, a),
                    $"current={h.Workspace.CurrentStep?.StepName}");

                // 开关（评审清单 B 包交互补齐）：关闭时扫描照跑（基线照记）但不切选中；打开后恢复
                var vm2 = new ProcessViewModel(h.Workspace, new SilentDialogService());
                try
                {
                    var c = h.Add(h.Leaf("开关C"));
                    vm2.AutoJumpToFailure = false;
                    c.State = StepState.Failed;
                    vm2.RunRuntimeTick();
                    Check("G4 开关关闭：新失败不切选中（扫描照跑）",
                        ReferenceEquals(h.Workspace.CurrentStep, a),
                        $"current={h.Workspace.CurrentStep?.StepName}");

                    vm2.AutoJumpToFailure = true;
                    var d = h.Add(h.Leaf("开关D"));
                    d.State = StepState.Failed;
                    vm2.RunRuntimeTick();
                    Check("G4 开关打开：恢复自动跳转（切到新失败）",
                        ReferenceEquals(h.Workspace.CurrentStep, d),
                        $"current={h.Workspace.CurrentStep?.StepName}");
                }
                finally
                {
                    vm2.Deactivate();
                }
            }
            finally
            {
                GlobalEventBus.Publish(MainRunState.NotStarted);
                vm.Deactivate();
            }
        }

        // ==================================================================
        //  [G5] 展开 / 折叠全部：布局库所有容器折叠标记全翻（3 层嵌套图式）
        // ==================================================================
        private static void CollapseExpandAllChecks()
        {
            Section("[G5] 展开 / 折叠全部");

            var h = new Harness();
            h.Add(h.Leaf("A"));
            var outer = h.If("外层");
            var innerFor = h.For("内层循环");
            var innerIf = h.If("最内层");
            innerIf.Children[0].Steps.Add(h.Leaf("芯"));
            innerFor.Children[0].Steps.Add(innerIf);
            outer.Children[0].Steps.Add(innerFor);
            h.Add(outer);
            var cv = h.Canvas;

            bool AllFlags(bool collapsed) => new StepModel[] { outer, innerFor, innerIf }
                .All(s => h.Flow.Layout.Find(s.StepID)?.Collapsed == collapsed);

            // 起始态可能已被 AutoCollapseDepth 自动折叠过一部分，先全部展开归零对照
            cv.ExpandAllCommand.Execute();
            Check("G5 展开全部：三个容器全部展开", AllFlags(false),
                string.Join(",", new StepModel[] { outer, innerFor, innerIf }
                    .Select(s => $"{s.StepName}:{(h.Flow.Layout.Find(s.StepID)?.Collapsed == true ? "折" : "展")}")));

            cv.CollapseAllCommand.Execute();
            Check("G5 折叠全部：三个容器全部标记折叠", AllFlags(true),
                string.Join(",", new StepModel[] { outer, innerFor, innerIf }
                    .Select(s => $"{s.StepName}:{(h.Flow.Layout.Find(s.StepID)?.Collapsed == true ? "折" : "展")}")));

            cv.ExpandAllCommand.Execute();
            Check("G5 再展开全部：全部归位展开", AllFlags(false), "");
        }

        // ==================================================================
        //  [G6] 编译错误事件：发布 → 缓存条数；定位 → 切流程 + 切步骤
        // ==================================================================
        private static void CompileErrorEventChecks()
        {
            Section("[G6] 编译错误事件与错误列表定位");

            var h = new Harness();
            var a = h.Add(h.Leaf("出错算子"));
            var cv = h.Canvas;   // 先建画布（构造即订阅 CompilationErrorsEvent）
            var other = new FlowModel { FlowName = "第二流程" };
            h.Solution.Flows.Add(other);

            var err = new CompilationError
            {
                StepId = a.StepID,
                StepName = a.StepName,
                Message = "[连线断开] 上游步骤不存在",
            };
            GlobalEventBus.Publish(new Core.Events.CompilationErrorsEvent
            {
                FlowName = h.Flow.FlowName,
                Errors = new List<CompilationError> { err },
            });
            Check("G6 发布事件 → 缓存条数正确", cv.CompileErrorCount == 1, $"count={cv.CompileErrorCount}");

            // 切到别的流程后定位：应切回出错流程并选中出错步骤
            h.Workspace.SwitchFlow(other);
            cv.LocateCompileError(h.Flow.FlowName, a.StepID);
            Check("G6 定位：流程切回出错流程", ReferenceEquals(h.Workspace.CurrentFlow, h.Flow),
                $"current={h.Workspace.CurrentFlow?.FlowName}");
            Check("G6 定位：步骤切到出错步骤", ReferenceEquals(h.Workspace.CurrentStep, a),
                $"current={h.Workspace.CurrentStep?.StepName}");

            // 批量编译口径（评审清单 B 包交互升级）：不同 FlowName 各发一次 → 按流程分组累计
            GlobalEventBus.Publish(new Core.Events.CompilationErrorsEvent
            {
                FlowName = other.FlowName,
                Errors = new List<CompilationError>
                {
                    new() { StepId = null, StepName = null, Message = "流程级错误" },
                    new() { StepId = a.StepID, StepName = a.StepName, Message = "[连线类型不匹配]" },
                },
            });
            Check("G6 不同 FlowName 累计：两流程错误共存（1+2=3 条）",
                cv.CompileErrorCount == 3, $"count={cv.CompileErrorCount}");
            var groups = cv.BuildCompileErrorGroups();
            Check("G6 不同 FlowName 累计：缓存分 2 组（先后 = 首现顺序）",
                groups.Count == 2
                && groups[0].FlowName == h.Flow.FlowName && groups[0].Entries.Count == 1
                && groups[1].FlowName == other.FlowName && groups[1].Entries.Count == 2,
                "groups=" + string.Join(",", groups.Select(g => $"{g.FlowName}:{g.Entries.Count}")));

            // 同 FlowName 重新发布（重新编译该流程）：替换该组，另一流程的组不动
            GlobalEventBus.Publish(new Core.Events.CompilationErrorsEvent
            {
                FlowName = other.FlowName,
                Errors = new List<CompilationError>
                {
                    new() { StepId = null, StepName = null, Message = "流程级错误" },
                },
            });
            var regrouped = cv.BuildCompileErrorGroups();
            Check("G6 同 FlowName 重发：替换该组（另一流程组保留）",
                cv.CompileErrorCount == 2 && regrouped.Count == 2
                && regrouped[1].Entries.Count == 1,
                $"count={cv.CompileErrorCount} groups=" + string.Join(",", regrouped.Select(g => $"{g.FlowName}:{g.Entries.Count}")));

            // 「复制全部」的文本口径：组头行 + 条目行（弹窗本身 headless 不开，断 VM 侧数据）
            string copyText = cv.BuildCompileErrorsCopyText();
            Check("G6 复制全部文本：含两个组头与既有条目",
                copyText.Contains($"【{h.Flow.FlowName}】共 1 条")
                && copyText.Contains($"【{other.FlowName}】共 1 条")
                && copyText.Contains("[连线断开] 上游步骤不存在"),
                copyText.Replace(Environment.NewLine, " | "));

            // 流程级错误（StepId=null）与不存在的流程：定位静默无动作、不抛
            cv.LocateCompileError(other.FlowName, null);
            cv.LocateCompileError("不存在的流程", a.StepID);
            Check("G6 流程级错误 / 未知流程：定位静默无动作不抛", ReferenceEquals(h.Workspace.CurrentFlow, other), "");
        }

        // ==================================================================
        //  [G10] 流程栏执行统计（评审清单 B 包交互补齐）：规模 N/深度 d 与本轮耗时聚合
        //  ComputeRunStats internal static 直调（纯计算，与 DetectNewFailures 同一理由）；
        //  两行文案经 VM 属性读时现算，LastRunTimeMs 手工灌值构造已知口径
        // ==================================================================
        private static void RunStatsChecks()
        {
            Section("[G10] 流程栏执行统计");

            var h = new Harness();
            var a = h.Add(h.Leaf("A"));
            var cond = h.If("判断");
            var deep = h.Leaf("深层");
            var nested = h.For("内层循环");
            var core = h.Leaf("芯");
            nested.Children[0].Steps.Add(core);
            cond.Children[0].Steps.Add(nested);
            cond.Children[0].Steps.Add(deep);
            h.Add(cond);

            // 造已知图（深度口径 = 流程栏树的层级感：顶层算子/容器第 1 层，分支内容随嵌套 +1）：
            // 顶层 A、判断（第 1 层）→ 深层、内层循环（第 2 层）→ 芯（第 3 层）；N = 5
            var stats = ProcessViewModel.ComputeRunStats(h.Flow.Steps);
            Check("G10 规模统计：已知图 N=5 / 最深 3 层（容器与分支内都计入）",
                stats.TotalSteps == 5 && stats.MaxDepth == 3,
                $"n={stats.TotalSteps} depth={stats.MaxDepth}");
            Check("G10 无运行数据：总耗时 0、最慢为空", stats.TotalMs == 0 && stats.SlowestName == null, "");

            var vm = new ProcessViewModel(h.Workspace, new SilentDialogService());
            try
            {
                Check("G10 无运行数据文案：第二行「尚未运行」",
                    vm.RunTimeText == "尚未运行", $"'{vm.RunTimeText}'");

                // 设三个 LastRunTimeMs（整数避免文案受 "0.#" 舍入影响）：总耗时 = Sum；最慢 = Max 及其步骤名
                a.LastRunTimeMs = 120;
                deep.LastRunTimeMs = 300;
                core.LastRunTimeMs = 50;
                Check("G10 两行文案：规模行 + 耗时行（Sum/Max 口径）",
                    vm.StepCountText == "共 5 步 · 最深 3 层"
                    && vm.RunTimeText == "本轮总耗时 470 ms · 最慢：深层 300 ms",
                    $"'{vm.StepCountText}' / '{vm.RunTimeText}'");
            }
            finally
            {
                vm.Deactivate();
            }
        }

        // ==================================================================
        //  [G9] 工具箱（评审清单 B 包交互补齐 + 工具箱面板改版）：
        //  搜索过滤（名称/描述/分类命中、清空恢复全量）/ 列表·网格视图模式 / 收藏与收藏分组 /
        //  分类观感映射表 / Toolbox 配置节的序列化往返。
        //  ToolViewModel 是纯 VM（构造吃 IPluginProvider + 可选 AppSettingsService），
        //  headless 用断言桩直造，settings 传 null = 纯内存、不碰 AppConfig.json。
        // ==================================================================
        private static void ToolboxFilterChecks()
        {
            Section("[G9] 工具箱搜索过滤 / 视图模式 / 收藏");

            // 前置：桩插件 3 个算子、2 个分类（"断言" 1 个 / "逻辑" 2 个，见 StubPluginProvider）
            var provider = new StubPluginProvider();
            var vm = new ToolViewModel(provider);
            Check("G9 前置：全量 3 个算子 2 个分组",
                vm.TotalCount == 3 && vm.FilteredToolBarSource.Count == 2,
                $"total={vm.TotalCount} groups={vm.FilteredToolBarSource.Count}");

            vm.SearchText = "桩算子";   // 按名称命中：只有叶子算子
            Check("G9 按名称过滤：命中 1 条、只剩命中的分组",
                vm.TotalCount == 1 && vm.FilteredToolBarSource.Count == 1,
                $"total={vm.TotalCount} groups={vm.FilteredToolBarSource.Count}");

            vm.SearchText = "条件容器";   // 按描述命中：只有 If 的描述含它（For 是"循环容器"、叶子是"桩算子"）
            Check("G9 按描述过滤：命中 1 条", vm.TotalCount == 1, $"total={vm.TotalCount}");

            vm.SearchText = "逻辑";     // 按分类（= 分组名）命中：整组保留（此前实现漏了 Category）
            Check("G9 按分类过滤：整组命中（If+For 共 2 条）",
                vm.TotalCount == 2 && vm.FilteredToolBarSource.Count == 1
                && vm.FilteredToolBarSource[0].Children.Count == 2,
                $"total={vm.TotalCount} groups={vm.FilteredToolBarSource.Count}");

            vm.SearchText = "";         // 清空恢复全量
            Check("G9 清空恢复全量", vm.TotalCount == 3 && vm.FilteredToolBarSource.Count == 2,
                $"total={vm.TotalCount} groups={vm.FilteredToolBarSource.Count}");

            // ---- 视图模式（命令参数是编译期不检查的 object：一律 DelegateCommand<object?> + 判型，见 AGENTS.md R27） ----
            Check("G9 视图模式：默认列表视图", !vm.IsGridView && vm.IsListView, $"grid={vm.IsGridView}");

            vm.ToggleViewModeCommand.Execute(null);
            Check("G9 ToggleViewModeCommand：列表 → 网格（两个属性同时翻转）",
                vm.IsGridView && !vm.IsListView, $"grid={vm.IsGridView}");

            vm.SetViewModeCommand.Execute("List");
            Check("G9 SetViewModeCommand(\"List\")：回到列表", !vm.IsGridView, $"grid={vm.IsGridView}");

            vm.SetViewModeCommand.Execute("Grid");
            Check("G9 SetViewModeCommand(\"Grid\")：切到网格", vm.IsGridView, $"grid={vm.IsGridView}");

            vm.SetViewModeCommand.Execute("这不是模式名");
            Check("G9 SetViewModeCommand(未知参数)：按切换处理、不抛异常",
                !vm.IsGridView, $"grid={vm.IsGridView}");

            // ---- 收藏（settings = null：纯内存） ----
            var leaf = provider.GetModule(StubPluginProvider.LeafPlugin);
            var ifItem = provider.GetModule(StubPluginProvider.IfPlugin);
            var version0 = vm.FavoritesVersion;

            Check("G9 收藏前置：初始无收藏、没有收藏分组",
                !vm.HasFavorites && !vm.IsFavorite(leaf) && vm.FilteredToolBarSource.Count == 2,
                $"has={vm.HasFavorites} groups={vm.FilteredToolBarSource.Count}");

            vm.ToggleFavoriteCommand.Execute(leaf);
            Check("G9 收藏一个：IsFavorite 翻转、HasFavorites 置位、版本号自增",
                vm.IsFavorite(leaf) && vm.HasFavorites && vm.FavoritesVersion == version0 + 1,
                $"fav={vm.IsFavorite(leaf)} has={vm.HasFavorites} ver={vm.FavoritesVersion}");

            // 收藏分组置顶 + 条目是**原始 ToolItemModel 实例**（拖拽链路的硬约束：
            // 包一层 wrapper 会让放置端 FlowCanvasView.FindDraggedTool 拿到 null）
            Check("G9 收藏分组置顶、含该项、不计入 TotalCount",
                vm.FilteredToolBarSource.Count == 3
                && vm.FilteredToolBarSource[0].IsFavorites
                && vm.FilteredToolBarSource[0].Name == "收藏"
                && vm.FilteredToolBarSource[0].Children.Count == 1
                && ReferenceEquals(vm.FilteredToolBarSource[0].Children[0], leaf)
                && vm.TotalCount == 3,
                $"groups={vm.FilteredToolBarSource.Count} first={vm.FilteredToolBarSource[0].Name}"
                + $"/{vm.FilteredToolBarSource[0].Children.Count} total={vm.TotalCount}");

            vm.ToggleFavoriteCommand.Execute(ifItem);
            Check("G9 再收藏一个：收藏分组 2 条、版本号再增",
                vm.FilteredToolBarSource[0].Children.Count == 2 && vm.FavoritesVersion == version0 + 2,
                $"favGroup={vm.FilteredToolBarSource[0].Children.Count} ver={vm.FavoritesVersion}");

            vm.ToggleFavoriteCommand.Execute(ifItem);
            Check("G9 取消收藏（同一个命令）：该项移出收藏分组",
                vm.FilteredToolBarSource[0].Children.Count == 1 && !vm.IsFavorite(ifItem),
                $"favGroup={vm.FilteredToolBarSource[0].Children.Count}");

            vm.ToggleFavoriteCommand.Execute(leaf);
            Check("G9 取消最后一个收藏：收藏分组消失、恢复全量分组",
                !vm.HasFavorites && vm.FilteredToolBarSource.Count == 2
                && !vm.FilteredToolBarSource[0].IsFavorites,
                $"groups={vm.FilteredToolBarSource.Count} first={vm.FilteredToolBarSource[0].Name}");

            vm.ToggleFavoriteCommand.Execute("这不是条目");
            Check("G9 ToggleFavoriteCommand(非 ToolItemModel)：静默空跑、不抛异常",
                !vm.HasFavorites && vm.FilteredToolBarSource.Count == 2,
                $"groups={vm.FilteredToolBarSource.Count}");

            // ---- 分组默认折叠 + 展开态在过滤重建后不丢 + 搜索时强制展开 ----
            Check("G9 分组默认折叠（分类组 IsExpanded=false）",
                vm.FilteredToolBarSource.Count == 2
                && vm.FilteredToolBarSource.All(g => !g.IsFavorites && !g.IsExpanded),
                string.Join(",", vm.FilteredToolBarSource.Select(g => $"{g.Name}:{g.IsExpanded}")));

            // 用户手动展开"逻辑"组 → 状态记账；再触发一次过滤重建（搜索→清空）→ 仍展开
            var logicGroup = vm.FilteredToolBarSource.FirstOrDefault(g => g.Name == "逻辑");
            if (logicGroup != null) logicGroup.IsExpanded = true;

            vm.SearchText = "逻辑";
            Check("G9 搜索时命中分组强制展开（否则结果全压在折叠头下面）",
                vm.FilteredToolBarSource.Count > 0 && vm.FilteredToolBarSource.All(g => g.IsExpanded),
                string.Join(",", vm.FilteredToolBarSource.Select(g => $"{g.Name}:{g.IsExpanded}")));

            vm.SearchText = "";
            var logicAfter = vm.FilteredToolBarSource.FirstOrDefault(g => g.Name == "逻辑");
            Check("G9 用户展开态在过滤重建后不丢（逻辑组仍展开）",
                logicAfter != null && logicAfter.IsExpanded,
                $"logic={logicAfter?.IsExpanded}");

            // 收藏组默认展开（它是用户自己挑出来的少数几条）
            vm.ToggleFavoriteCommand.Execute(leaf);
            var favGroup = vm.FilteredToolBarSource.FirstOrDefault(g => g.IsFavorites);
            Check("G9 收藏组默认展开", favGroup != null && favGroup.IsExpanded,
                $"fav={favGroup?.IsExpanded}");
            vm.ToggleFavoriteCommand.Execute(leaf);   // 还原

            // ---- 分类观感映射表：13 个分类逐条核对，防"加了分类忘了配色" ----
            var categories = new[]
            {
                "逻辑控制", "缺陷检测", "标定", "测量", "图像处理", "常用工具", "数据处理",
                "几何工具", "定位", "运动控制", "流程控制", "逻辑判断", "变量操作",
            };
            var unconfigured = categories
                .Where(c => string.IsNullOrEmpty(ToolboxCategoryVisuals.IconFor(c))
                            || ToolboxCategoryVisuals.IconFor(c) == ToolboxCategoryVisuals.FallbackIcon
                            || ToolboxCategoryVisuals.ColorFor(c) == ToolboxCategoryVisuals.FallbackColor)
                .ToList();
            Check("G9 分类映射：13 个分类都有专属图标与颜色（防漏配）",
                ToolboxCategoryVisuals.Categories.Count == categories.Length && unconfigured.Count == 0,
                unconfigured.Count == 0
                    ? $"表内 {ToolboxCategoryVisuals.Categories.Count} 条"
                    : "落回兜底：" + string.Join(",", unconfigured));

            Check("G9 分类映射：未知分类回落通用图标 + 灰",
                ToolboxCategoryVisuals.IconFor("不存在的分类") == ToolboxCategoryVisuals.FallbackIcon
                && ToolboxCategoryVisuals.ColorFor("不存在的分类") == ToolboxCategoryVisuals.FallbackColor,
                "");

            // ---- 类型短名（悬浮预览里每个端口的类型标签用）：去程序集限定 / 泛型实参 / 命名空间 / 元数 ----
            Check("G9 类型短名：System.Double → Double",
                TypeNameShortenerConverter.Shorten("System.Double") == "Double",
                TypeNameShortenerConverter.Shorten("System.Double"));
            Check("G9 类型短名：程序集限定名只留短名",
                TypeNameShortenerConverter.Shorten("HalconDotNet.HImage, halcondotnet, Version=25.5.0.0, Culture=neutral, PublicKeyToken=null") == "HImage",
                TypeNameShortenerConverter.Shorten("HalconDotNet.HImage, halcondotnet, Version=25.5.0.0, Culture=neutral, PublicKeyToken=null"));
            Check("G9 类型短名：泛型去实参与元数（List`1[[…]] → List）",
                TypeNameShortenerConverter.Shorten("System.Collections.Generic.List`1[[System.Double, System.Private.CoreLib, Version=9.0.0.0]], System.Private.CoreLib") == "List",
                TypeNameShortenerConverter.Shorten("System.Collections.Generic.List`1[[System.Double, System.Private.CoreLib, Version=9.0.0.0]], System.Private.CoreLib"));
            Check("G9 类型短名：空 / 空白 → 空串（不抛）",
                TypeNameShortenerConverter.Shorten(null) == "" && TypeNameShortenerConverter.Shorten("   ") == "",
                "");

            // ---- Toolbox 配置节：JSON 往返（纯内存，不写盘——写了会污染断言宿主的 bin） ----
            var node = new ToolboxSettings();
            node.Favorites.Add("VM.Stub.Leaf");
            node.Favorites.Add("VM.Stub.If");
            var json = JsonConvert.SerializeObject(node);
            var restored = JsonConvert.DeserializeObject<ToolboxSettings>(json);
            Check("G9 ToolboxSettings JSON 往返：Favorites 一致",
                restored != null && restored.Favorites.Count == 2
                && restored.Favorites[0] == "VM.Stub.Leaf" && restored.Favorites[1] == "VM.Stub.If",
                $"json={json}");

            // 老配置里没有 Toolbox 节：Newtonsoft 遇到缺失的引用类型只留 null，
            // 靠 AppConfigModel 的属性初始化器兜底（AppConfigModel.cs 三次踩坑史的同一道闸）
            var legacy = JsonConvert.DeserializeObject<AppConfigModel>("{\"AuditRetentionDays\":30}");
            Check("G9 老配置缺 Toolbox 节：反序列化后仍非 null（初始化器兜底）",
                legacy?.Toolbox != null && legacy.Toolbox.Favorites != null,
                $"toolbox={(legacy?.Toolbox == null ? "null" : "ok")}");

            // ---- 落盘被占（无删除共享）时收藏仍要写入成功 ----
            // 2026-10-14 真机事故：运行期宿主以"允许读写、不允许删除"方式占着 AppConfig.json，
            // File.Replace 抛 ERROR_UNABLE_TO_REMOVE_REPLACED(1177)，点收藏即崩。
            // 这里复现该占用形态，钉住 Save() 的退路（Replace 失败 → 覆盖写）。
            // 落盘目标 = 本进程 BaseDirectory 的 AppConfig.json；密闭性靠"原本没有 → 用完删掉"还原（同 Z7）。
            string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppConfig.json");
            bool existedBefore = System.IO.File.Exists(configPath);
            try
            {
                var service = new AppSettingsService();
                // FileShare.ReadWrite = 允许读 + 允许写、不允许删除——正是触发 1177 的形态
                using (var hold = new System.IO.FileStream(configPath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                    service.SyncToolboxFavorites(new[] { "VM.Stub.Leaf" });   // 不得抛
                }
                var reloaded = new AppSettingsService();
                Check("G9 落盘被占（无删除共享）时收藏仍写入成功（Replace 失败退化为覆盖写）",
                    reloaded.Current?.Toolbox?.Favorites?.Contains("VM.Stub.Leaf") == true,
                    $"favs={string.Join(",", reloaded.Current?.Toolbox?.Favorites ?? new System.Collections.Generic.List<string>())}");
            }
            finally
            {
                if (!existedBefore && System.IO.File.Exists(configPath))
                    System.IO.File.Delete(configPath);
            }
        }

        // ==================================================================
        //  [G7] 画布复制/粘贴（FlowClipboard，2026-10-12 评审清单第三批；第一期单个节点）：
        //  复制快照走 SolutionService.SerializationSettings（与 .vms 落盘同一条管线）；
        //  粘贴物化五条重映射纪律——StepID 重生成 / 内部连线保留 / 外部连线过滤 /
        //  条件变量 Guid 键重映射 / 根节点同型计数命名；运行期字段复位；撤销与运行锁走既有闸。
        // ==================================================================
        private static void FlowClipboardChecks()
        {
            Section("[G7] 画布复制/粘贴（FlowClipboard）");
            FlowClipboard.Clear();

            // ---- 叶子：复制 → 粘贴 → 身份/名字/配置/运行期复位，撤销重做整件来回 ----
            var h = new Harness();
            var src = h.Add(Harness.WithInput(h.Leaf("叶子A"), "In"));
            src.SetInputValue("In", 3.14);
            src.Description = "配置样本";
            // 运行期脏值：State 会随序列化带过去（[RuntimeState] 不豁免落盘），粘贴必须复位；
            // IsBreakpoint / LastRunTimeMs 是 [JsonIgnore] / 纯内存——断言顺带钉住"复位不是靠不序列化"
            src.State = StepState.Success;
            src.LastRunTimeMs = 87.6;
            src.IsBreakpoint = true;
            var cv = h.Canvas;

            h.Node(src)!.IsSelected = true;
            cv.CopySelectedCommand.Execute(null);
            Check("G7 复制后剪贴板非空", FlowClipboard.HasPayload, "");

            cv.PasteCommand.Execute(new Point(0, double.MaxValue));
            var pasted = h.Flow.Steps.Count == 2 ? h.Flow.Steps[1] : null;
            Check("G7 叶子粘贴：新步骤追加到顶层末尾",
                pasted is ActionStep && !ReferenceEquals(pasted, src), $"steps={h.Flow.Steps.Count}");
            Check("G7 叶子粘贴：StepID 重生成（与源无共享身份）",
                pasted != null && pasted.StepID != src.StepID, "");
            Check("G7 叶子粘贴：名字不与图纸现有步骤重名",
                pasted != null && !NameExistsDeep(h.Flow.Steps, pasted.StepName, pasted),
                pasted?.StepName ?? "");
            Check("G7 叶子粘贴：配置与源一致（剥掉 ID/名字/运行态后序列化逐字相等）",
                pasted != null && ConfigJsonEquals(src, pasted), "");
            Check("G7 叶子粘贴：运行期字段复位（Idle / 无计时 / 无断点）",
                pasted != null && pasted.State == StepState.Idle && !pasted.IsRunningFocus
                && pasted.LastRunStartTimestamp == null && pasted.LastRunTimeMs == 0 && !pasted.IsBreakpoint, "");

            cv.UndoCommand.Execute();
            Check("G7 撤销粘贴：粘贴件整件消失（撤销补口覆盖粘贴）",
                h.Flow.Steps.Count == 1 && pasted != null && !h.Flow.Steps.Contains(pasted),
                $"steps={h.Flow.Steps.Count}");
            cv.RedoCommand.Execute();
            Check("G7 重做粘贴：粘贴件回到原位（下标 1）",
                h.Flow.Steps.Count == 2 && ReferenceEquals(h.Flow.Steps[1], pasted), "");

            // ---- 容器：If 双分支 + 分支内连线 + 指向子树外的连线 + 条件变量 → 重映射口径 ----
            var h2 = new Harness();
            var outside = h2.Add(Harness.WithInput(h2.Leaf("外部生产"), "In"));
            var cond = h2.If("容器");
            var innerA = Harness.WithInput(h2.Leaf("内部A"), "In");
            var innerB = Harness.WithInput(h2.Leaf("内部B"), "In");
            cond.Children[0].Steps.Add(innerA);
            cond.Children[0].Steps.Add(innerB);
            h2.Add(cond);
            Harness.RawLink(innerB, "In", innerA, "Out");     // 子树内部连线：粘贴后必须保留并重映射
            Harness.RawLink(innerA, "In", outside, "Out");    // 指向子树外部：粘贴件里必须整条过滤
            var localVar = new LocalVariableItem { Name = "阈值", DataTypeName = "System.Double" };
            cond.LocalVariables.Add(localVar);
            cond.SetLink(localVar.Id.ToString(), new LinkReference(
                LinkKind.RuntimeVariable, LinkProtocol.RuntimeVariableMarkerGuid, "阈值", "Runtime.阈值"));
            var cv2 = h2.Canvas;

            h2.Node(cond)!.IsSelected = true;
            cv2.CopySelectedCommand.Execute(null);
            cv2.PasteCommand.Execute(new Point(0, double.MaxValue));
            var pastedCond = h2.Flow.Steps.Count == 3
                ? h2.Flow.Steps.OfType<ConditionStep>().FirstOrDefault(s => !ReferenceEquals(s, cond))
                : null;
            Check("G7 容器粘贴：容器上图纸（顶层末尾）",
                pastedCond != null && h2.Flow.Steps.Count == 3, $"steps={h2.Flow.Steps.Count}");
            Check("G7 容器粘贴：分支结构完整（If 分支 2 步骤 / Else 分支空）",
                pastedCond != null && pastedCond.Children.Count == 2
                && pastedCond.Children[0].Steps.Count == 2 && pastedCond.Children[1].Steps.Count == 0, "");

            var pastedA = pastedCond?.Children[0].Steps.OfType<StepModel>().FirstOrDefault(s => s.StepName.Contains('A'));
            var pastedB = pastedCond?.Children[0].Steps.OfType<StepModel>().FirstOrDefault(s => s.StepName.Contains('B'));
            Check("G7 容器粘贴：子步骤 StepID 全部重生成",
                pastedA != null && pastedB != null
                && pastedA.StepID != innerA.StepID && pastedB.StepID != innerB.StepID
                && pastedA.StepID != pastedB.StepID, "");
            Check("G7 容器粘贴：内部连线保留且指向粘贴件内部节点",
                pastedA != null && pastedB != null
                && pastedB.GetLink("In") is LinkReference innerLink
                && innerLink.TargetStepId == pastedA.StepID
                && innerLink.DisplayAddress == $"{pastedA.StepName}.Out",
                pastedB?.GetLink("In")?.DisplayAddress ?? "");
            Check("G7 容器粘贴：指向子树外的连线被过滤（不随粘贴造悬空引用）",
                pastedA != null && !pastedA.LinkedSources.ContainsKey("In"), "");
            Check("G7 容器粘贴：条件变量 Id 重映射、Guid 键同步换",
                pastedCond != null && pastedCond.LocalVariables.Count == 1
                && pastedCond.LocalVariables[0].Id != localVar.Id
                && pastedCond.LinkedSources.ContainsKey(pastedCond.LocalVariables[0].Id.ToString()), "");
            Check("G7 容器粘贴：根名字不与图纸现有步骤重名",
                pastedCond != null && !NameExistsDeep(h2.Flow.Steps, pastedCond.StepName, pastedCond),
                pastedCond?.StepName ?? "");

            // ---- 并行分组：分支结构 + ExecutionMode 保留 ----
            var h3 = new Harness();
            var par = h3.Parallel("并行源");
            par.ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel;
            par.Children[0].Steps.Add(Harness.WithInput(h3.Leaf("分支1步骤"), "In"));
            par.Children[1].Steps.Add(Harness.WithInput(h3.Leaf("分支2步骤"), "In"));
            h3.Add(par);
            var cv3 = h3.Canvas;

            h3.Node(par)!.IsSelected = true;
            cv3.CopySelectedCommand.Execute(null);
            cv3.PasteCommand.Execute(new Point(0, double.MaxValue));
            var pastedPar = h3.Flow.Steps.Count == 2
                ? h3.Flow.Steps.OfType<ParallelStep>().FirstOrDefault(s => !ReferenceEquals(s, par))
                : null;
            Check("G7 并行粘贴：分组上图纸", pastedPar != null && h3.Flow.Steps.Count == 2, "");
            Check("G7 并行粘贴：两条分支完整保留",
                pastedPar != null && pastedPar.Children.Count == 2
                && pastedPar.Children[0].Steps.Count == 1 && pastedPar.Children[1].Steps.Count == 1, "");
            Check("G7 并行粘贴：ExecutionMode=Parallel 保留（不被反序列化默认值吞掉）",
                pastedPar != null && pastedPar.ExecutionMode == VisionMaster.Models.ParallelExecutionMode.Parallel, "");

            // ---- 空选/多选：第一期单个节点，多选不进剪贴板；空剪贴板粘贴空跑 ----
            var h5 = new Harness();
            h5.Add(h5.Leaf("甲"));
            h5.Add(h5.Leaf("乙"));
            var cv5 = h5.Canvas;
            h5.Node(h5.Flow.Steps[0])!.IsSelected = true;
            h5.Node(h5.Flow.Steps[1])!.IsSelected = true;
            FlowClipboard.Clear();
            cv5.CopySelectedCommand.Execute(null);
            Check("G7 多选复制：不进剪贴板（第一期口径，给提示）",
                !FlowClipboard.HasPayload && (cv5.StatusHint ?? "").Contains("单个"), $"'{cv5.StatusHint}'");
            cv5.PasteCommand.Execute(null);
            Check("G7 剪贴板为空：粘贴空跑（不插入不压栈）",
                h5.Flow.Steps.Count == 2 && cv5.UndoStackSize == 0, $"size={cv5.UndoStackSize}");

            // ---- 运行锁：Running 下粘贴空跑（门禁现成，驱动手法照 [G1]）----
            var h4 = new Harness();
            h4.Add(h4.Leaf("占位"));
            var cv4 = h4.Canvas;
            FlowClipboard.Copy(h4.Flow.Steps[0]);
            GlobalEventBus.Publish(MainRunState.RunningOnce);
            try
            {
                cv4.PasteCommand.Execute(new Point(0, double.MaxValue));
                Check("G7 运行中粘贴：不插入、不压栈",
                    h4.Flow.Steps.Count == 1 && cv4.UndoStackSize == 0,
                    $"steps={h4.Flow.Steps.Count} size={cv4.UndoStackSize}");
                Check("G7 运行中粘贴给了拦截提示", (cv4.StatusHint ?? "").Contains("运行中"), $"'{cv4.StatusHint}'");
            }
            finally
            {
                GlobalEventBus.Publish(MainRunState.NotStarted);   // 复位全局总线，别把锁漏给后续断言
            }
        }

        /// <summary>全树查名字是否已被占用（except 自身豁免；与 StepFactory.CountStepsDeep 同一条递归口径）</summary>
        private static bool NameExistsDeep(IEnumerable<StepModel> steps, string name, StepModel? except)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;

                if (!ReferenceEquals(step, except) && step.StepName == name) return true;

                if (step is IContainerStep container)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch != null && NameExistsDeep(branch.Steps, name, except)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// "配置一致"的比对口径：两边都按落盘管线设置序列化成 JSON，剥掉身份字段
        ///（StepID/StepName）与运行态（State——粘贴件被有意复位，见 G7 专属断言）后逐字相等。
        /// 不是引用相等——粘贴件本来就是深拷贝重映射的产物。
        /// </summary>
        private static bool ConfigJsonEquals(StepModel source, StepModel pasted)
        {
            var a = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(source, RoundTripSettings));
            var b = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(pasted, RoundTripSettings));
            foreach (var key in new[] { "StepID", "StepName", "State" })
            {
                a.Remove(key);
                b.Remove(key);
            }
            return Newtonsoft.Json.Linq.JToken.DeepEquals(a, b);
        }

        // ==================================================================
        //  [G8] 画布压力图式构造（2026-10-12 评审清单第三批；性能只量化不优化）：
        //  与 --render 的 [perf] 计时共用 RenderProbe.BuildPerfGraph 一份生成器。
        //  gate 只断"图式能全量建成、展开后算子数达标"，毫秒数看 --render 的 [perf] 行。
        // ==================================================================
        private static void PerfGraphChecks()
        {
            Section("[G8] 画布压力图式构造（300/600 节点）");

            var (h300, expected300) = RenderProbe.BuildPerfGraph(300);
            var cv300 = h300.Canvas;
            cv300.ExpandAllCommand.Execute();
            int built300 = h300.Steps().Count + h300.Containers().Count;
            Check("G8 300 节点图式：全量建成且展开后算子数达标",
                built300 == expected300 && built300 >= 300, $"built={built300} expected={expected300}");

            var (h600, expected600) = RenderProbe.BuildPerfGraph(600);
            var cv600 = h600.Canvas;
            cv600.ExpandAllCommand.Execute();
            int built600 = h600.Steps().Count + h600.Containers().Count;
            Check("G8 600 节点图式：全量建成且展开后算子数达标",
                built600 == expected600 && built600 >= 600, $"built={built600} expected={expected600}");
        }

        // ==================================================================
        //  离线工具：向方案文件的 MainTask 注入 If 容器（UI 实测素材）
        // ==================================================================
        private static void InjectIfIntoMainTask(string path)
        {
            var json = File.ReadAllText(path);
            var solution = JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
            var flow = solution.Flows.FirstOrDefault(f => f.FlowName == "MainTask")
                       ?? solution.Flows.First();

            var cond = new ConditionStep("", "If", "BuiltIn_If", "If_自动化测试");
            cond.Children[0].Expression = "1 > 0";
            flow.Steps.Add(cond);

            File.WriteAllText(path, SolutionService.Serialize(solution));
            Console.WriteLine($"OK: If 容器已注入 [{flow.FlowName}]，顶层步骤 {flow.Steps.Count} 个 → {path}");
        }

        // ==================================================================
        //  断言骨架
        // ==================================================================
        // 断言骨架对同程序的 ExecutionChecks（执行层断言）开放，共用一套计数与汇总口径
        internal static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"---- {title} ----");
        }

        internal static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  →  " + detail)}");
        }

        // ------------------------------------------------------------------
        //  几何断言辅助：画布节点盒 / 包含 / 相交
        //  （1px 容差 = 泳道与框体都有 1~1.5px 边框线；贴边不算压）
        // ------------------------------------------------------------------

        /// <summary>画布节点的渲染矩形：泳道与容器框用算出的 Lane*，步骤盒用标称尺寸</summary>
        internal static Rect NodeBox(CanvasNodeViewModel node) => node.Kind == CanvasNodeKind.Step
            ? new Rect(node.Location.X, node.Location.Y, FlowCanvasViewModel.NodeWidth, FlowCanvasViewModel.NodeHeight)
            : new Rect(node.Location.X, node.Location.Y, node.LaneWidth, node.LaneHeight);

        /// <summary>包含判定：inner 完整落在 outer 内（含 1px 容差）</summary>
        internal static bool Inside(Rect outer, Rect inner, double tol = 1)
            => inner.Left >= outer.Left - tol && inner.Top >= outer.Top - tol
               && inner.Right <= outer.Right + tol && inner.Bottom <= outer.Bottom + tol;

        /// <summary>相交判定：两块盒体彼此压住（贴边不算，容差 0.5px）</summary>
        internal static bool Overlaps(Rect a, Rect b, double tol = 0.5)
            => a.Left < b.Right - tol && b.Left < a.Right - tol
               && a.Top < b.Bottom - tol && b.Top < a.Bottom - tol;

        private static void Finish()
        {
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// 方案落盘用的反序列化设置：与 <c>SolutionService</c> 内部那份保持一致。
        /// 产品里那份是私有字段，这里只能用同一个 public binder 复刻，否则往返断言测的不是同一条链路。
        /// </summary>
        internal static readonly JsonSerializerSettings RoundTripSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new ConnectionConfigSerializationBinder()
        };
    }
}
