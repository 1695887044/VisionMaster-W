using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 共享变量（全局变量 / 系统变量）的**按稳定 Id** 读写契约（运行期能力）。
    ///
    /// 为什么与 <see cref="IGlobalVariableWriter"/> 并存而不是改它：
    /// 写入口是既有契约（「变量赋值」、组态动作都在用），只按名字寻址；而脚本的声明表
    /// 必须按 <c>VariableId</c> 寻址（全局变量改名不断链），并且还需要"读"。
    /// 实现方（宿主侧 <c>GlobalVariableWriter</c>）同时实现两个接口：
    /// 类型守门与真正落值只有一份，不会出现"按名写"和"按 Id 写"两套口径。
    ///
    /// 系统变量（<c>VariableType.System</c>）由运动轴状态投影而来，不实现可写契约：
    /// <see cref="TryWrite"/> 对它返回 false 并给出中文原因（只读）。
    /// </summary>
    public interface ISharedVariableAccess
    {
        /// <summary>
        /// 按 Id 读共享变量的当前值（Id 为空或找不到时按 <paramref name="fallbackName"/> 兜底）。
        /// </summary>
        /// <param name="variableId">变量稳定身份（首选寻址）</param>
        /// <param name="fallbackName">兜底名字（旧方案/手输；Id 命中时不使用）</param>
        /// <param name="value">读到的值；失败为 null</param>
        /// <param name="error">失败原因（可直接展示给操作员）</param>
        bool TryRead(Guid variableId, string fallbackName, out object? value, out string? error);

        /// <summary>
        /// 按 Id 写共享变量（Id 为空或找不到时按 <paramref name="fallbackName"/> 兜底）。
        /// 类型守门与「变量赋值」同口径：类型化的值必须能赋给声明类型，文本按声明类型解析一次。
        /// </summary>
        bool TryWrite(Guid variableId, string fallbackName, object? value, out string? error);
    }

    /// <summary>
    /// 空实现：没有工作区的执行环境（容器注册用的简化上下文、断言夹具）用。
    /// 与 <c>NullCameraProvider</c> / <c>NullMotionProvider</c> 同一范式——
    /// 插件只判返回值，不必为"能力可能不存在"写一层判空。
    /// </summary>
    public sealed class NullSharedVariableAccess : ISharedVariableAccess
    {
        public static readonly NullSharedVariableAccess Instance = new();

        private NullSharedVariableAccess() { }

        public bool TryRead(Guid variableId, string fallbackName, out object? value, out string? error)
        {
            value = null;
            error = "当前执行环境没有绑定工作区，无法读取共享变量";
            return false;
        }

        public bool TryWrite(Guid variableId, string fallbackName, object? value, out string? error)
        {
            error = "当前执行环境没有绑定工作区，无法写入共享变量";
            return false;
        }
    }
}
