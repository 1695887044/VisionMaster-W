using Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 全局流程执行引擎接口 (定义了对运行时会话的全部操作契约)
    /// </summary>
    public interface IFlowEngine
    {
        /// <summary>
        /// 启动指定会话的连续循环执行 (进入死循环模式，由节拍控制)
        /// </summary>
        Task RunSessionAsync(FlowSession session);

        /// <summary>
        /// 启动指定会话的单次执行 (常用于标定、调试或手动触发)
        /// </summary>
        Task RunSessionOnceAsync(FlowSession session);

        /// <summary>
        /// 停止指定的活动会话
        /// </summary>
        void StopSession(FlowSession session);

        /// <summary>
        /// 紧急停止所有正在运行的会话 (急停开关)
        /// </summary>
        void StopAll();
        /// <summary>
        /// 暂停指定的活动会话 (进入暂停状态，等待恢复指令)
        /// </summary>
        void PauseSession(FlowSession session);
        /// <summary>
        /// 恢复指定的暂停会话 (从暂停状态恢复到运行状态)
        /// </summary>
        void ResumeSession(FlowSession session);
        /// <summary>
        /// 获取当前引擎中所有正在运行的 Session 数量
        /// </summary>
        int ActiveSessionCount { get; }
    }

}
