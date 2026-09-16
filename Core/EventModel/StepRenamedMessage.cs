﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.EventModel
{
    /// <summary>
    /// 步骤重命名消息
    /// 当步骤名称变更时发布此消息，用于更新所有引用该步骤的连线显示地址
    /// </summary>
    public class StepRenamedMessage
    {
        /// <summary>
        /// 被改名步骤的唯一标识。
        /// 级联更新一律以它为准，不用名字匹配——名字匹配会误伤同名步骤，
        /// 且步骤名可以合法地重复出现在不同流程里
        /// </summary>
        public Guid StepId { get; }

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
        public StepRenamedMessage(Guid stepId, string oldName, string newName)
        {
            StepId = stepId;
            OldName = oldName;
            NewName = newName;
        }
    }
}
