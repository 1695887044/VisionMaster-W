using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// For 循环步骤模型
    /// 支持指定次数的循环执行
    /// </summary>
    public class ForStep : StepModel, IContainerStep
    {
        /// <summary>
        /// 默认循环次数（未被上游连线覆盖时使用）
        /// </summary>
        public int DefaultLoopCount { get; set; } = 10;

        /// <summary>
        /// 循环体步骤集合
        /// </summary>
        public ObservableCollection<StepCollection> Children { get; } = new();

        /// <summary>
        /// 创建 For 循环步骤
        /// </summary>
        public ForStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "循环体" });
        }
    }
}
