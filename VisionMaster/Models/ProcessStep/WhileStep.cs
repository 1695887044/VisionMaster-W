﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// While 循环步骤模型
    /// 支持条件循环执行
    /// </summary>
    public class WhileStep : ConditionStep
    {
        /// <summary>
        /// 创建 While 循环步骤
        /// </summary>
        public WhileStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            // 覆盖基类的默认分支，While 只需要一个循环体容器
            Children.Clear();
            Children.Add(new StepCollection
            {
                BranchType = BranchType.Default,
                StepName = "循环体",
                Expression = "" // 用户填写循环条件
            });
        }
    }
}
