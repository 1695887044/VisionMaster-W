using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class ConditionStep : StepModel,IContainerStep
    {
        /// <summary>
        /// 用来描述 条件局部变量和 上游端口变量的关系  Var_1  在子容器  会有一个 <Var_1,LinkSource>的字典
        /// </summary>
        public ObservableCollection<LocalVariableItem> LocalVariables { get; set; } = new();
        public ObservableCollection<StepCollection> Children { get; } = new();

        public ConditionStep(
            string icon,
            string pluginName,
            string pluginTypeName,
            string stepName = null
        )
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            if (pluginTypeName.Contains("If")) // 假设你的 If 算子标识名包含 "If"
            {
                // 对于 If 算子，默认给它分配 "If" 和 "Else" 两个分支
                Children.Add(
                    new StepCollection
                    {
                        BranchType = BranchType.If,
                        StepName = "If",
                        Expression = "", // 预留给用户填写的条件，比如 "Score > 80"
                    }
                );

                Children.Add(
                    new StepCollection { BranchType = BranchType.Else, StepName = "Else" }
                );
            }
            else if (pluginTypeName.Contains("Switch")) // 预留给 Switch 算子
            {
                // 对于 Switch 算子，默认给一个 Case 分支
                Children.Add(
                    new StepCollection
                    {
                        BranchType = BranchType.Case,
                        StepName = "Case 1",
                        Expression = "",
                    }
                );
            }
            else
            {
                // 兜底逻辑
                Children.Add(
                    new StepCollection { BranchType = BranchType.If, StepName = "默认分支" }
                );
            }
        }
    }
}
