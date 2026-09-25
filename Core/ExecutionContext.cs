using Core.Interfaces;
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
        /// 当前执行节点的ID（用于调试和追踪）
        /// </summary>
        public Guid? CurrentNodeId { get; set; }

        /// <summary>
        /// 执行开始时间（用于超时控制）
        /// </summary>
        public DateTime ExecutionStartTime { get; init; } = DateTime.Now;

        /// <summary>
        /// 本地变量字典
        /// </summary>
        public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();

        private IGlobalVariableWriter? _globalVariables;

        /// <summary>
        /// 全局变量写入口（懒建：绝大多数节点用不到，不必每次执行都构造）。
        ///
        /// 为什么不做成 init 属性：简化构造（容器注册用的那份，没有会话也没有工作区）下
        /// 也要能拿到一个非空对象——传入 null 工作区的写入器会"写入即失败并给出中文原因"，
        /// 这样插件侧只判断 TryWrite 的返回值即可，不必再判能力是否为空。
        /// </summary>
        public IGlobalVariableWriter GlobalVariables
            => _globalVariables ??= new GlobalVariableWriter(Workspace);

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