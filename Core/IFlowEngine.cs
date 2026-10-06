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
        /// <param name="debugSession">
        /// 本次运行是否为调试会话（DWV 第 1 期）：装调试门，支持断点 / 单步 / 暂停继续。
        /// 由引擎在<b>抢到会话锁之后</b>据此参数置位 <see cref="FlowSession.DebugEnabled"/>，收尾统一清回 false。
        /// 【为什么不许调用方自己先置位】界面若在调用前置 true、而这一刻 HTTP 触发抢先拿到会话锁，
        /// 界面这次调用会被拒（锁被占），true 却残留在会话上，于是"HTTP 触发不受断点影响"的
        /// 硬约束被静态破坏。置位权收归引擎，与"谁真正抢到锁"绑死。
        /// </param>
        /// <returns>异步任务</returns>
        Task RunSessionAsync(FlowSession session, bool debugSession = false);

        /// <summary>
        /// 启动指定会话的单次执行
        /// 常用于标定、调试或手动触发场景
        /// </summary>
        /// <param name="session">要执行的流程会话</param>
        /// <param name="debugSession">本次运行是否为调试会话（语义同 <see cref="RunSessionAsync"/>）</param>
        /// <returns>异步任务</returns>
        Task RunSessionOnceAsync(FlowSession session, bool debugSession = false);

        /// <summary>
        /// 单次执行，并**如实返回"这一单跑了没有"**（DWV 第 2 批：给「调用流程」用）。
        /// 抢不到会话锁时不抛不静默，返回 false —— 调用方（子程序调用 / 自动触发）必须据此如实报错，
        /// 否则会把"我没跑上"误读成"跑完了"（父流程带着未落地的数据继续走）。
        /// </summary>
        /// <param name="session">要执行的流程会话</param>
        /// <param name="debugSession">本次运行是否为调试会话（语义同 <see cref="RunSessionAsync"/>）</param>
        /// <returns>true = 抢到会话锁并跑完（含被停止打断）；false = 目标已被占用，本次未执行</returns>
        Task<bool> TryRunSessionOnceAsync(FlowSession session, bool debugSession = false);

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
        /// 单步执行：从调试停点放行，恰好执行一个节点后再次停住（DWV 第 1 期）。
        /// 仅当会话处于"运行中且已暂停"时有效；其余情况忽略（与 ResumeSession 的守卫同口径——
        /// 没有等待中的调试门时置"单步待停"只会遗留给下一轮，下一轮开始会清）。
        /// </summary>
        /// <param name="session">要单步的流程会话</param>
        void StepSession(FlowSession session);

        /// <summary>
        /// 获取当前引擎中所有正在运行的 Session 数量
        /// </summary>
        int ActiveSessionCount { get; }
    }
}