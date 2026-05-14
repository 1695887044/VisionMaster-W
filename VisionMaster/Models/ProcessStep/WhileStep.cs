using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class WhileStep : ConditionStep
    {
        public WhileStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            // 覆盖基类的默认分支，While 只需要一个循环体容器
            Children.Clear();
            Children.Add(new StepCollection
            {
                BranchType = BranchType.Default,
                StepName = "循环体",
                Expression = "" // 留给用户写条件
            });
        }
    }
}
