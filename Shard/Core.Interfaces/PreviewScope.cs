using System;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// "当前正在执行哪条流程"的发布线程作用域。
    ///
    /// 为什么需要它
    /// ---------
    /// 插件发布预览时只有 <c>IVisionPlugin</c> 实例，拿不到会话/流程名
    /// （<c>PublishPreview</c> 是扩展方法，签名里没有 <c>IExecutionContext</c>）。
    /// 而图像集必须知道"这张图是谁的"——否则多流程并发时只能猜，猜错就会把别的流程的
    /// 帧当成自己的清掉（2026-10-27 现场："A 投窗口1、B 投窗口2 → 窗口2 被 A 反复清空"）。
    ///
    /// 用法（引擎侧，每轮执行期间）
    /// ---------
    /// <code>
    /// using (PreviewScope.Begin(session.FlowName))
    ///     session.ExecutionEngine.Run(context);
    /// </code>
    /// 作用域必须在**执行线程上**打开（连续/单次运行都在 <c>Task.Run</c> 的 lambda 里），
    /// 并行分支的 <c>Task.Run</c> 会自动继承（AsyncLocal 随 ExecutionContext 流动）；
    /// 子程序调用会嵌套成子流程自己的名字，退出后自动恢复父流程名。
    ///
    /// 取不到时怎么办
    /// ---------
    /// <see cref="CurrentFlowName"/> 为 null 表示"不在任何流程的一轮里"（例如插件自建线程/定时器
    /// 发布、或非流程宿主）。消费方**不许**回退成"最后开轮的那条流程"——那正是旧实现的缺陷。
    /// 正确做法是落到一个明确的伪流程桶，让"没有归属"可被看见。
    /// </summary>
    public static class PreviewScope
    {
        private static readonly AsyncLocal<string?> _flowName = new();

        /// <summary>当前执行线程所属的流程名；不在流程一轮内时为 null</summary>
        public static string? CurrentFlowName => _flowName.Value;

        /// <summary>
        /// 进入一条流程的执行作用域。返回的作用域释放时恢复进入前的值（支持子程序嵌套）。
        /// </summary>
        public static IDisposable Begin(string? flowName)
        {
            var previous = _flowName.Value;
            _flowName.Value = string.IsNullOrWhiteSpace(flowName) ? null : flowName!.Trim();
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            public Scope(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _flowName.Value = _previous;
            }
        }
    }
}
