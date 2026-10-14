using System.Collections.Generic;
using VisionMaster.Models;

namespace Core.Events
{
    /// <summary>
    /// 编译错误列表事件：编译失败时发布，供订阅方（流程画布等）缓存最近一次错误
    /// 并提供「错误列表 → 双击定位到出错步骤」的入口。
    /// 轻量载体（照 LinkPathEvent 的风格）：纯数据、无行为，GlobalEventBus 同步派发。
    /// </summary>
    public class CompilationErrorsEvent
    {
        /// <summary>出错流程名（FlowModel.FlowName 快照；定位时按名找流程）</summary>
        public string FlowName { get; set; }

        /// <summary>错误列表（含 StepId / StepName 定位信息；流程级错误 StepId 为 null）</summary>
        public IReadOnlyList<CompilationError> Errors { get; set; }
    }
}
