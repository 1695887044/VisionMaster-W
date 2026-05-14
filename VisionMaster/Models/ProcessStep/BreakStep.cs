﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// Break 步骤模型
    /// 用于跳出循环结构
    /// </summary>
    public class BreakStep : StepModel
    {
        public BreakStep(string icon, string pluginName, string typeName, string stepName = null) 
            : base(icon, pluginName, typeName, stepName) { }
    }

    /// <summary>
    /// Continue 步骤模型
    /// 用于跳过当前循环迭代，继续下一次循环
    /// </summary>
    public class ContinueStep : StepModel
    {
        public ContinueStep(string icon, string pluginName, string typeName, string stepName = null) 
            : base(icon, pluginName, typeName, stepName) { }
    }

    /// <summary>
    /// Return 步骤模型
    /// 用于终止整个流程执行
    /// </summary>
    public class ReturnStep : StepModel
    {
        public ReturnStep(string icon, string pluginName, string typeName, string stepName = null) 
            : base(icon, pluginName, typeName, stepName) { }
    }
}
