﻿﻿﻿using System;
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
        /// 创建会话状态变更事件参数
        /// </summary>
        public SessionStateChangedEventArgs(string sessionId, string flowName, SessionState oldState, SessionState newState)
        {
            SessionId = sessionId;
            FlowName = flowName;
            OldState = oldState;
            NewState = newState;
        }
    }
}
