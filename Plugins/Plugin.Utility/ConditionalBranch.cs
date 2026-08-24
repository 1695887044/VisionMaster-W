using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 条件分支插件
    /// 根据布尔条件选择输出不同的值
    /// </summary>
    [Display(
        Name = "条件分支",
        GroupName = "逻辑判断",
        Description = "根据布尔条件选择输出不同的值",
        ShortName = "\uf0e8"
    )]
    public class ConditionalBranchPlugin : VisionPluginBase
    {
        /// <summary>
        /// 判断条件输入端口
        /// </summary>
        public InputPort<bool> Condition { get; } = new InputPort<bool>("Condition", false, "判断条件");

        /// <summary>
        /// 条件为真时输出的值
        /// </summary>
        public InputPort<object> TrueValue { get; } = new InputPort<object>("TrueValue", null, "条件为真时输出的值");

        /// <summary>
        /// 条件为假时输出的值
        /// </summary>
        public InputPort<object> FalseValue { get; } = new InputPort<object>("FalseValue", null, "条件为假时输出的值");

        /// <summary>
        /// 输出结果端口
        /// </summary>
        public OutputPort<object> Result { get; } = new OutputPort<object>("Result", "输出结果");

        /// <summary>
        /// 执行条件分支判断
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            bool condition = Condition.GetTypedValue();
            Result.Value = condition ? TrueValue.GetTypedValue() : FalseValue.GetTypedValue();
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public override void Initialize() { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
