﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.EventModel
{
    /// <summary>
    /// 步骤重命名消息
    /// 当步骤名称变更时发布此消息，用于更新所有引用该步骤的连线和表达式
    /// </summary>
    public class StepRenamedMessage
    {
        /// <summary>
        /// 旧名称
        /// </summary>
        public string OldName { get; }

        /// <summary>
        /// 新名称
        /// </summary>
        public string NewName { get; }

        /// <summary>
        /// 创建步骤重命名消息
        /// </summary>
        public StepRenamedMessage(string oldName, string newName)
        {
            OldName = oldName;
            NewName = newName;
        }
    }
}
