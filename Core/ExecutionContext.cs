using Core.Interfaces;
using Core.Interfaces.Assets;
using Core.Interfaces.Database;
using Core.Interfaces.Io;
using System;
using System.Collections.Generic;
using System.Threading;
using VisionMaster.Binding;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 执行上下文类
    /// 为流程执行提供运行时上下文信息，包括日志、端口绑定、取消令牌等
    /// </summary>
    public class ExecutionContext : IExecutionContext
    {
        /// <summary>
        /// 日志服务
        /// </summary>
        public ILogService Logger { get; init; }

        /// <summary>
        /// 取消令牌，用于终止执行
        /// </summary>
        public CancellationToken CancellationToken { get; init; }

        /// <summary>
        /// 当前流程控制状态
        /// </summary>
        public FlowControlState CurrentFlowState { get; set; }

        /// <summary>
        /// 当前执行的会话
        /// </summary>
        public FlowSession CurrentSession { get; init; }

        /// <summary>
        /// 当前执行流程的名称（网络收图按流程名分槽取图用）。
        /// 直接由会话派生：会话是"这次执行"的唯一身份来源，再单独存一份必然出现两者不一致。
        /// 简化构造（容器注册的那份）没有会话，此处为 null，取图口会归入空串槽。
        /// </summary>
        public string CurrentFlowName => CurrentSession?.FlowName;

        /// <summary>
        /// 工作空间管理器
        /// </summary>
        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 相机仓库（方案级硬件资源）。
        ///
        /// 默认值是 <see cref="NullCameraProvider"/>，而不是 null —— 与 GlobalVariables 同一口径：
        /// 容器注册用的简化构造（没有工作区）与单元测试夹具都走这条默认路径，
        /// 插件侧因此只需判 <c>TryGetDevice</c> 的返回值，不必再为"相机能力可能不存在"写一层判空。
        /// </summary>
        public ICameraProvider Cameras { get; init; } = NullCameraProvider.Instance;

        /// <summary>
        /// 运动卡仓库（方案级硬件资源）。
        ///
        /// 默认值同样是空实现（<see cref="NullMotionProvider"/>），口径与 <see cref="Cameras"/> 一致：
        /// 容器注册的简化构造与单元测试夹具走这条默认路径，插件只需判 TryGet 的返回值。
        /// </summary>
        public IMotionProvider Motions { get; init; } = NullMotionProvider.Instance;

        /// <summary>
        /// 流程调用器（「调用流程」步骤用）。
        ///
        /// 默认值是 <see cref="NullFlowInvoker"/>，而不是 null —— 与 <see cref="Cameras"/> /
        /// <see cref="Motions"/> 同一口径：容器注册的简化构造与单元测试夹具走这条默认路径，
        /// 插件侧因此只判结果的 Success，不必再为"流程调用能力可能不存在"写一层判空。
        /// 真正可用的一份由流程引擎在构造上下文时注入（FlowEngineService.FlowInvoker）。
        /// </summary>
        public IFlowInvoker FlowInvoker { get; init; } = NullFlowInvoker.Instance;

        /// <summary>
        /// 通讯变量立刻读写入口（「立刻读 / 立刻写」步骤用）。
        ///
        /// 默认值是 <see cref="NullCommVariableAccess"/>，与 Cameras / Motions / FlowInvoker 同一口径：
        /// 容器注册用的简化构造与单元测试夹具走这条默认路径，
        /// 插件侧只需判 TryReadNow / TryWriteNow 的返回值，不必再判能力是否为空。
        /// 真正可用的一份由宿主在构造上下文时注入。
        /// </summary>
        public ICommVariableAccess Comms { get; init; } = NullCommVariableAccess.Instance;

        /// <summary>
        /// 数据库运行期能力面（「数据库查询 / 执行 / 写行」三步用）。
        ///
        /// 默认值是 <see cref="NullDatabaseAccess"/>，与 Cameras / Motions / Comms 同一口径：
        /// 容器注册用的简化构造与断言夹具走这条默认路径，插件侧只需判返回值，
        /// 不必再判"数据库能力是否存在"。真正可用的一份由宿主在构造上下文时注入。
        /// </summary>
        public IDatabaseAccess Database { get; init; } = NullDatabaseAccess.Instance;

        /// <summary>
        /// 当前执行节点的ID（用于调试和追踪）
        /// </summary>
        public Guid? CurrentNodeId { get; set; }

        /// <summary>
        /// 执行开始时间（用于超时控制）
        /// </summary>
        public DateTime ExecutionStartTime { get; init; } = DateTime.Now;

        /// <summary>
        /// 本地变量字典。
        ///
        /// 【二期真并行】修饰符从 { get; } 改为 { get; protected init; }（全文件唯一改动，
        /// IExecutionContext 契约零改动）：并行分支上下文（ParallelBranchExecutionContext）
        /// 在构造时把它替换成"记账字典"（BranchVariableRegistry，写先登记 key），
        /// join 后按真实写入集合做影子合并。除该子类外没有任何赋值方，既有行为不变。
        /// </summary>
        public IDictionary<string, object> LocalVariables { get; protected init; } = new Dictionary<string, object>();

        private GlobalVariableWriter? _variableStore;

        /// <summary>
        /// 全局变量写入口（懒建：绝大多数节点用不到，不必每次执行都构造）。
        ///
        /// 为什么不做成 init 属性：简化构造（容器注册用的那份，没有会话也没有工作区）下
        /// 也要能拿到一个非空对象——传入 null 工作区的写入器会"写入即失败并给出中文原因"，
        /// 这样插件侧只判断 TryWrite 的返回值即可，不必再判能力是否为空。
        /// </summary>
        public IGlobalVariableWriter GlobalVariables
            => _variableStore ??= new GlobalVariableWriter(Workspace);

        /// <summary>
        /// 共享变量按 Id 读写入口（与 <see cref="GlobalVariables"/> 共用同一个写入器实例：
        /// 类型守门与落值逻辑只有一份）。
        /// </summary>
        public ISharedVariableAccess SharedVariables
            => _variableStore ??= new GlobalVariableWriter(Workspace);

        private ICalibrationCatalog? _calibrationCatalog;

        /// <summary>
        /// 方案级标定资产目录：从当前工作区的方案懒建（与 GlobalVariables 同一模式）。
        /// 没有工作区时返回空目录；有工作区时始终是同一个包装实例，插件写入即落在方案上，
        /// 保存 .vms 时随 <see cref="SolutionModel.Calibrations"/> 一起落盘。
        /// </summary>
        public ICalibrationCatalog Calibrations
            => _calibrationCatalog ??= Workspace?.CurrentSolution is { } solution
                ? new SolutionCalibrationCatalog(solution)
                : NullCalibrationCatalog.Instance;

        private IProjectAssetResolver? _projectAssets;

        /// <summary>
        /// 方案内容资产目录：插件按 AssetId 解析模板/模型/图像文件路径。
        /// 与 Calibrations 同一懒建模式；没有工作区时回落实空实现。
        /// </summary>
        public IProjectAssetResolver Assets
            => _projectAssets ??= Workspace?.CurrentSolution is { } solution
                ? new SolutionProjectAssetResolver(solution)
                : NullProjectAssetResolver.Instance;

        /// <summary>
        /// 运行期能力面清单（唯一事实源）。新增任何"由宿主注入、缺省回落空实现"的能力属性时，
        /// 必须在这里登记，否则两处会同时判红：
        ///  · <c>EngineContextWiringChecks</c> 反射比对——<see cref="ExecutionContext"/> 上有、清单里没有 → 红；
        ///  · 并行分支上下文 <c>ParallelBranchExecutionContext</c> 的搬运——清单登记了没搬 → 红。
        ///
        /// 为什么用属性清单：能力面是 init 属性，反射拿到 <see cref="System.Reflection.PropertyInfo"/>
        /// 既能读父上下文的取值，也能直接比对"分支上下文与父上下文是不是同一批实例"。
        /// </summary>
        public static readonly System.Reflection.PropertyInfo[] RuntimeCapabilities =
        {
            typeof(ExecutionContext).GetProperty(nameof(Cameras))!,
            typeof(ExecutionContext).GetProperty(nameof(Motions))!,
            typeof(ExecutionContext).GetProperty(nameof(FlowInvoker))!,
            typeof(ExecutionContext).GetProperty(nameof(Comms))!,
            typeof(ExecutionContext).GetProperty(nameof(Database))!,
        };

        /// <summary>按 <see cref="RuntimeCapabilities"/> 的顺序取出全部能力面的当前值</summary>
        public object?[] SnapshotCapabilities()
        {
            var values = new object?[RuntimeCapabilities.Length];
            for (int i = 0; i < RuntimeCapabilities.Length; i++)
                values[i] = RuntimeCapabilities[i].GetValue(this);
            return values;
        }

        /// <summary>
        /// 【唯一的上下文装配入口】从 <see cref="ExecutionContextCapabilitySource"/> 造一份执行上下文。
        ///
        /// 为什么装配收在这里：连续执行、单次执行、并行分支、插件试运行以前各自写一份对象初始化器，
        /// 单次执行那份漏了 <c>Database</c>（2026-10-24 评审团 C1）——同一个流程
        /// "连续跑能查库、单次跑必报没有接入数据库服务"，且没有任何断言盯着"注入清单是否齐全"。
        /// 收成一个方法后，"漏注入"是运行期第一跳的异常，不是静默回落成空实现。
        ///
        /// 【反射装配】按 <see cref="RuntimeCapabilities"/> 逐项写回：新增能力属性只要登记进清单，
        /// 这里自动覆盖；<see cref="ExecutionContextCapabilitySource.ResolveCapabilities"/> 缺分支会抛，
        /// 不会静默漏搬。守门：FlowCanvasChecks 的 EngineContextWiringChecks。
        /// </summary>
        public static ExecutionContext Create(
            ILogService logService,
            FlowSession? session,
            IWorkspaceManager? workspace,
            CancellationToken token,
            ExecutionContextCapabilitySource source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            var context = new ExecutionContext(logService, session!, workspace!, token);
            var values = source.ResolveCapabilities();
            for (int i = 0; i < RuntimeCapabilities.Length; i++)
                RuntimeCapabilities[i].SetValue(context, values[i]);
            return context;
        }

        /// <summary>
        /// 构造函数（简化版本）
        /// </summary>
        /// <param name="logService">日志服务</param>
        public ExecutionContext(ILogService logService)
        {
            Logger = logService;
        }

        /// <summary>
        /// 构造函数（完整版本）
        /// </summary>
        /// <param name="logService">日志服务</param>
        /// <param name="session">当前会话</param>
        /// <param name="workspace">工作空间管理器</param>
        /// <param name="token">取消令牌</param>
        public ExecutionContext(ILogService logService, FlowSession session, IWorkspaceManager workspace, CancellationToken token)
        {
            Logger = logService;
            CurrentSession = session;
            Workspace = workspace;
            CancellationToken = token;
        }
    }
}