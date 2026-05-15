﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 条件步骤模型
    /// 支持 If-Else 和 Switch 等分支结构
    /// </summary>
    public class ConditionStep : StepModel, IContainerStep
    {
        /// <summary>
        /// 本地变量列表
        /// 用于描述条件局部变量和上游端口变量的关系
        /// </summary>
        public ObservableCollection<LocalVariableItem> LocalVariables { get; set; } = new();

        /// <summary>
        /// 子分支集合
        /// </summary>
        public ObservableCollection<StepCollection> Children { get; } = new();

        /// <summary>
        /// 创建条件步骤
        /// </summary>
        public ConditionStep(
            string icon,
            string pluginName,
            string pluginTypeName,
            string stepName = null
        )
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            if (pluginTypeName.Contains("If"))
            {
                // If 算子默认创建 If 和 Else 两个分支
                Children.Add(
                    new StepCollection
                    {
                        BranchType = BranchType.If,
                        StepName = "If",
                        Expression = "", // 用户填写条件，如 "Score > 80"
                    }
                );

                Children.Add(
                    new StepCollection { BranchType = BranchType.Else, StepName = "Else" }
                );
            }
            else if (pluginTypeName.Contains("Switch"))
            {
                // Switch 算子默认创建一个 Case 分支
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
