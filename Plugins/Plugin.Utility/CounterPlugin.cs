using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 计数器插件
    /// 每次执行时累加步长，支持复位清零
    /// </summary>
    [Display(
        Name = "计数器",
        GroupName = "常用工具",
        Description = "计数器，每次执行时累加步长，支持复位清零",
        ShortName = "\uf0a2"
    )]
    public class CounterPlugin : VisionPluginBase
    {
        /// <summary>
        /// 复位信号输入端口
        /// True时将计数器清零
        /// </summary>
        public InputPort<bool> ResetPort { get; } = new InputPort<bool>("Reset", false, "复位信号(True时清零)");

        /// <summary>
        /// 递增步长输入端口
        /// 每次执行时增加的值
        /// </summary>
        public InputPort<int> StepPort { get; } = new InputPort<int>("Step", 1, "每次执行的递增步长");

        /// <summary>
        /// 当前计数值输出端口
        /// </summary>
        public OutputPort<int> CurrentCount { get; } = new OutputPort<int>("Count", "当前计数值");

        /// <summary>
        /// 内部计数器状态
        /// </summary>
        private int _count = 0;

        /// <summary>
        /// 执行计数算法
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            if (ResetPort.GetTypedValue())
            {
                _count = 0;
                context.Logger.Info($"{InstanceName} 收到复位信号，计数已清零。");
            }
            else
            {
                int step = StepPort.GetTypedValue();
                _count += step;
                context.Logger.Info($"{InstanceName} 计数递增 {step}，当前值: {_count}");
            }

            CurrentCount.Value = _count;
        }

        /// <summary>
        /// 初始化插件，重置计数器
        /// </summary>
        public override void Initialize() => _count = 0;

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
