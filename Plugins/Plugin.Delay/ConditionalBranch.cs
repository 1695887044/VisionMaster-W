using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Delay
{
    [Display(
     Name = "ConditionalBranch",
     GroupName = "流程控制",
     Description = "根据布尔条件选择输出不同的值",
     ShortName = "\uf0e8"
 )]
    public class ConditionalBranchPlugin : VisionPluginBase
    {
        public InputPort<bool> Condition { get; } = new InputPort<bool>("Condition", false, "判断条件");
        public InputPort<object> TrueValue { get; } = new InputPort<object>("TrueValue", null, "条件为真时输出的值");
        public InputPort<object> FalseValue { get; } = new InputPort<object>("FalseValue", null, "条件为假时输出的值");

        public OutputPort<object> Result { get; } = new OutputPort<object>("Result", "输出结果");

        public override void RunAlgorithm(IExecutionContext context)
        {
            bool condition = Condition.GetTypedValue();
            Result.Value = condition ? TrueValue.GetTypedValue() : FalseValue.GetTypedValue();
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
