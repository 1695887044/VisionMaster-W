using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 「调用流程」一次调用的结果：成败 + 可直接展示给操作员的中文原因 + 耗时。
    ///
    /// 为什么用只读结构体而不是 bool + out 参数：结果要一路带回插件的输出端口
    /// （Invoked / ElapsedMs），三件事实（成不成、为什么、多久）在任何失败分支上都必须齐全——
    /// "调用失败但没有原因"正是现场最难查的一类状态。
    /// </summary>
    public readonly struct FlowInvokeResult
    {
        /// <summary>目标流程是否成功跑完（含"跑完但内部有失败步骤"= 假，见实现口径）</summary>
        public bool Success { get; }

        /// <summary>可直接展示的中文原因（成功时是简述，失败时必须说清哪一步拦下）</summary>
        public string Message { get; }

        /// <summary>从开始调用到返回的毫秒数（超时也照实记录）</summary>
        public int ElapsedMs { get; }

        public FlowInvokeResult(bool success, string message, int elapsedMs)
        {
            Success = success;
            Message = message ?? string.Empty;
            ElapsedMs = elapsedMs;
        }

        public static FlowInvokeResult Ok(string message, int elapsedMs)
            => new(true, message, elapsedMs);

        public static FlowInvokeResult Fail(string message, int elapsedMs = 0)
            => new(false, message, elapsedMs);
    }

    /// <summary>
    /// 流程调用器：把一条流程当**子程序**跑起来并等它跑完（「调用流程」步骤的能力面）。
    ///
    /// 为什么挂在 <see cref="IExecutionContext"/> 上递送：插件只引用 Core.Interfaces，
    /// 物理上够不到宿主的流程引擎/运行管理器——上下文是运行期唯一能递送能力的通道
    /// （与 <see cref="IGlobalVariableWriter"/>、<see cref="ICameraProvider"/> 同一范式）。
    ///
    /// 实现方必须保证"非空"：没有引擎的宿主返回 <see cref="NullFlowInvoker"/>，
    /// 插件侧只判 <see cref="FlowInvokeResult.Success"/> 即可，不必为"这个能力可能不存在"再写判空。
    ///
    /// 门禁口径（宿主实现必须一致，插件不做二次判定）：
    ///  · 目标流程必须启用、未加密，且**调用方式勾选了「子程序调用」**（用户决策：
    ///    "只有选择了这个的才能被调用"在同一条纪律下扩展到子程序）；
    ///  · 目标正在运行 → 直接失败（不排队：排队会把"流程互等"变成挂死现场）；
    ///  · 自身/成环调用由"正在运行"这一条天然拦住（父流程持有自己的会话锁）。
    /// </summary>
    public interface IFlowInvoker
    {
        /// <summary>
        /// 调用指定流程并等待其跑完（单次执行）。
        /// </summary>
        /// <param name="flowName">目标流程名（与 HTTP 路由 / 运行状态镜像同口径：按名 Ordinal 查找）</param>
        /// <param name="timeoutMs">等待超时（毫秒）；超时**不打断**目标（取消归引擎令牌管），如实返回失败</param>
        /// <param name="cancellationToken">父流程的取消令牌（急停时连等待一起结束）</param>
        FlowInvokeResult Invoke(string flowName, int timeoutMs, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 空实现：宿主没有装配流程调用器时使用（容器注册用的简化上下文 / 单元测试夹具）。
    /// 调用一律失败并给出中文原因——比返回 null 让插件崩在空引用上友好得多。
    /// </summary>
    public sealed class NullFlowInvoker : IFlowInvoker
    {
        public static readonly NullFlowInvoker Instance = new();

        public FlowInvokeResult Invoke(string flowName, int timeoutMs, CancellationToken cancellationToken)
            => FlowInvokeResult.Fail("当前执行环境没有绑定流程引擎，无法调用流程（「调用流程」步骤不可用）");
    }
}
