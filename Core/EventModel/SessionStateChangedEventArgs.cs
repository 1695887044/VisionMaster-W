﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.EventModel
{
    /// <summary>
    /// 会话状态变更事件参数
    /// </summary>
    public class SessionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 流程名称
        /// </summary>
        public string FlowName { get; }

        /// <summary>
        /// 旧状态
        /// </summary>
        public SessionState OldState { get; }

        /// <summary>
        /// 新状态
        /// </summary>
        public SessionState NewState { get; }

        /// <summary>
        /// 附加消息（可空）。
        ///
        /// 【为什么要有它】引擎在 Faulted 那条路径上一直把 ex.Message 传进 NotifyStateChanged，
        /// 但参数此前被丢弃（事件参数里根本没有这个字段）—— 调用方写得煞有介事、订阅者却永远拿不到，
        /// 典型的信息静默丢失。这里补上字段，让那条失败原因真正送达。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 创建会话状态变更事件参数
        /// </summary>
        public SessionStateChangedEventArgs(
            string sessionId,
            string flowName,
            SessionState oldState,
            SessionState newState,
            string message = null
        )
        {
            SessionId = sessionId;
            FlowName = flowName;
            OldState = oldState;
            NewState = newState;
            Message = message;
        }
    }
}
