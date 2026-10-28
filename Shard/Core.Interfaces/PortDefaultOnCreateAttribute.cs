using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 端口默认值随步骤创建一起落盘（工具箱拖入时写进 <c>StepModel.InputValues</c>）。
    ///
    /// 【为什么需要它】
    /// 端口构造时给的 <c>defaultValue</c> 只活在**插件实例**上，步骤模型
    /// （<c>StepModel.InputValues</c>）新建时是空字典；而编译期必填检查
    /// （FlowCompiler 的「[参数缺失] 必填参数 'X' 未配置」）只认 InputValues 里有没有这个键。
    /// 于是"拖进来就带着 500ms 默认值"的步骤会被判成没配置，必须先打开参数窗点一次「确定」
    /// （<see cref="VisionPluginBase.OnConfirm"/> 才写回）才能编译 —— 而运行期其实一直是好的。
    ///
    /// 标了本特性的端口，在步骤创建（<c>StepFactory.CreateFromTool</c>）时把端口当前值写进
    /// InputValues，等价于"用户点过一次确定"：拖入即可编译、画布上也不会亮"必填未配置"角标。
    ///
    /// 【什么时候**不要**标】
    /// 默认值本身带机械/电气后果的端口一律不标 —— 轴目标位置（0 会真走）、IO 输出值
    /// （true 会真输出）、子流程名（空串必然找不到流程）等，必须由人打开参数窗确认。
    /// 这类端口保持原样即可：不标特性 = 维持"必须人工确认"的旧行为。
    ///
    /// 只作用于**输入端口属性**（<c>InputPort&lt;T&gt;</c>）；<c>[StepConfig]</c> 配置属性不需要
    /// —— 它们不参与编译期必填检查，默认值本来就在编译时由插件实例生效。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class PortDefaultOnCreateAttribute : Attribute
    {
    }
}
