using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Interfaces
{
    public enum FlowControlState
    {
        Normal,
        Continue,
        Break,
        Return
    }

    public interface IExecutionContext
    {
        ILogService Logger { get; }

        CancellationToken CancellationToken { get; }

        FlowControlState CurrentFlowState { get; set; }
        
        Guid? CurrentNodeId { get; set; }

        /// <summary>
        /// 当前执行流程的名称（无会话/无名称时为 null）。
        /// 供"按流程名分槽"的取数口使用——目前是网络收图（<see cref="ImageHub"/>）：
        /// 采集步骤必须知道"我这张图该从哪个流程槽里取"，而这个信息只有执行上下文知道。
        /// </summary>
        string CurrentFlowName { get; }
        
        DateTime ExecutionStartTime { get; }

        /// <summary>
        /// 全局变量写入口：把运行期产出（如 HImage、检测结果）交给"变量管理"里的全局变量。
        ///
        /// 为什么挂在执行上下文上：插件只引用 Core.Interfaces，物理上够不到全局变量所在的程序集，
        /// 上下文是运行期唯一能递送能力的通道（与 Logger 同一范式）。
        /// 实现方必须保证"非空"：没有工作区时也要返回一个"写入即失败并说明原因"的实现，
        /// 让插件侧不必为"这个能力可能不存在"再写一层判空。
        /// </summary>
        IGlobalVariableWriter GlobalVariables { get; }

        /// <summary>
        /// 相机仓库：方案级硬件资源（连接 / 帧队列 / 状态机都在宿主侧，插件只拿契约）。
        ///
        /// 与 <see cref="GlobalVariables"/> 同一范式——插件物理上够不到宿主的 CameraProvider，
        /// 只能由上下文递送。实现方必须保证"非空"：没有相机服务时返回 <see cref="NullCameraProvider"/>，
        /// 让插件只判 <c>TryGetDevice</c> 的返回值即可。
        /// </summary>
        ICameraProvider Cameras { get; }

        IDictionary<string, object> LocalVariables { get; }
    }
}