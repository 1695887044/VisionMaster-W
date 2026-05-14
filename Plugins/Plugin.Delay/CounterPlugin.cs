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
          Name = "Counter",
          GroupName = "常用工具",
          Description = "计数器，每次执行时累加步长。支持复位清零。",
          ShortName = "\uf0a2" 
      )]
    public class CounterPlugin : VisionPluginBase
    {
        public InputPort<bool> ResetPort { get; } = new InputPort<bool>("Reset", false, "复位信号(True时清零)");
        public InputPort<int> StepPort { get; } = new InputPort<int>("Step", 1, "每次执行的递增步长");

        public OutputPort<int> CurrentCount { get; } = new OutputPort<int>("Count", "当前计数值");

        private int _count = 0; // 内部状态缓存

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 1. 检查复位信号
            if (ResetPort.GetTypedValue())
            {
                _count = 0;
                context.Logger.Info($"{InstanceName} 收到复位信号，计数已清零。");
            }
            else
            {
                // 2. 正常累加
                int step = StepPort.GetTypedValue();
                _count += step;
                context.Logger.Info($"{InstanceName} 计数递增 {step}，当前值: {_count}");
            }

            // 3. 输出结果
            CurrentCount.Value = _count;
        }

        public override void Initialize() { _count = 0; }
        public override void Dispose() { }
    }
}
