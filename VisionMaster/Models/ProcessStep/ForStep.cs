using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class ForStep : StepModel, IContainerStep
    {
        // 如果没有被上游连线覆盖，默认循环次数为 10
        public int DefaultLoopCount { get; set; } = 10;

        public ObservableCollection<StepCollection> Children { get; } = new();

        public ForStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "循环体" });
        }
    }
}
