using System;
using System.Collections.Generic;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 组态「选择轴」入口：属性面板只说"给我一组候选轴"，弹窗怎么弹由这一层决定。
    ///
    /// 与 <see cref="IScadaVariablePicker"/> 同一手法：面板不依赖 Prism 弹窗、
    /// 也不认识运动模块；候选清单由弹窗的 ViewModel 从 <c>MotionAxisRegistry</c> 现取。
    /// </summary>
    public interface IScadaAxisPicker
    {
        /// <param name="current">当前已选的轴（用于预选；可为空）</param>
        /// <param name="onPicked">用户确认后的回调；取消不回调（取消 = 当我没点过）</param>
        void Pick(IReadOnlyList<ScadaAxisRef> current, Action<IReadOnlyList<ScadaAxisRef>> onPicked);

        /// <summary>
        /// 单选：给"一条动作只操作一根轴"的场景（组态动作表的运动命令族）用。
        ///
        /// 为什么单独开一个方法而不是让调用方 <see cref="Pick"/> 完只取第一项：
        /// 界面上必须<b>看得出只能选一个</b>——多选弹窗里勾了三个再回传第一个，
        /// 用户看到的是"我明明选了三根、结果只动了一根"，而那种错误没有任何报错。
        /// 所以单选是弹窗的一个模式（勾一行即取消其它行），不是调用方的取数约定。
        /// </summary>
        /// <param name="current">当前已选的轴（用于预选；可为空 = 没选）</param>
        /// <param name="onPicked">
        /// 用户确认后的回调（参数为选中的那根轴）；取消不回调。
        /// <b>不会</b>用 null 表示"取消"——取消就是"当我没点过"，不该被翻译成"清空原绑定"。
        /// </param>
        /// <remarks>
        /// 带默认实现（而不是纯抽象成员）：本接口在"运动命令族"之后才长出这个成员，
        /// 而既有实现者（断言工程的替身、将来别的宿主替身）不该为了一个它用不到的模式去补一个空方法——
        /// 补空的后果是"单选被当成取消"，静默丢配置。默认实现退化成多选取第一项，
        /// 语义上仍然正确（用户确实选了那一根），只是界面上看不出互斥。
        /// 真正的互斥在 <see cref="ScadaAxisPicker"/> + <c>ScadaAxisPickerViewModel</c> 里。
        /// </remarks>
        void PickSingle(IReadOnlyList<ScadaAxisRef> current, Action<ScadaAxisRef?> onPicked)
            => Pick(current, axes => onPicked(axes is { Count: > 0 } ? axes[0] : null));
    }
}
