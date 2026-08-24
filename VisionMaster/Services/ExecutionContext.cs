using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
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
        /// 端口绑定服务
        /// </summary>
        public IPortBindingService PortBindingService { get; init; }

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
        /// 工作空间管理器
        /// </summary>
        public IWorkspaceManager Workspace { get; init; }

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