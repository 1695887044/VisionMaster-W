using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.EventModel
{
    public class SessionStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 发生状态变更的具体运行会话
        /// </summary>
        public FlowSession Session { get; }

        /// <summary>
        /// 会话当前进入的新状态
        /// </summary>
        public SessionState State { get; }

        /// <summary>
        /// 附加信息（通常在 Faulted 状态下携带具体的异常报错信息）
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 构造函数 (强制初始化，保证属性只读)
        /// </summary>
        public SessionStateChangedEventArgs(FlowSession session, SessionState state, string message = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            State = state;
            Message = message;
        }
    }
}
