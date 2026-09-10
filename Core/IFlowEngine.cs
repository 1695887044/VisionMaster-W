using System;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程执行引擎接口
    /// 定义了对运行时会话的全部操作契约，支持多流程并发执行
    /// </summary>
    public interface IFlowEngine
    {
        /// <summary>
        /// 启动指定会话的连续循环执行
        /// 进入死循环模式，由节拍控制，直到收到停止指令
        /// </summary>
        /// <param name="session">要执行的流程会话</param>
        /// <returns>异步任务</returns>
        Task RunSessionAsync(FlowSession session);

        /// <summary>
        /// 启动指定会话的单次执行
        /// 常用于标定、调试或手动触发场景
        /// </summary>
        /// <param name="session">要执行的流程会话</param>
        /// <returns>异步任务</returns>
        Task RunSessionOnceAsync(FlowSession session);

        /// <summary>
        /// 停止指定的活动会话
        /// 通过取消令牌优雅地终止执行
        /// </summary>
        /// <param name="session">要停止的流程会话</param>
        void StopSession(FlowSession session);

        /// <summary>
        /// 紧急停止所有正在运行的会话（急停开关）
        /// 用于系统紧急状态下的快速停机
        /// </summary>
        void StopAll();

        /// <summary>
        /// 暂停指定的活动会话
        /// 进入暂停状态，等待恢复指令
        /// </summary>
        /// <param name="session">要暂停的流程会话</param>
        void PauseSession(FlowSession session);

        /// <summary>
        /// 恢复指定的暂停会话
        /// 从暂停状态恢复到运行状态
        /// </summary>
        /// <param name="session">要恢复的流程会话</param>
        void ResumeSession(FlowSession session);

        /// <summary>
        /// 获取当前引擎中所有正在运行的 Session 数量
        /// </summary>
        int ActiveSessionCount { get; }
    }
}