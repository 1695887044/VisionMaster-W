using System;
using System.Globalization;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 枚举型下拉的一项：<b>落盘/绑定用的整数值</b> + <b>给人看的显示文本</b>。
    ///
    /// 为什么值与显示要分成两个属性
    /// ---------
    /// 与 <see cref="VisionMaster.Scada.ScadaChoiceOption"/> 同一条口径：PLC/Modbus 寄存器里存的是
    /// 数值，画面上的「手动/自动/检修」只是它的名字。若把显示文本当值用，改一次文案就会打断
    /// 已经配好的流程；反过来把数值直接上屏，操作员又看不懂。所以
    /// <c>SelectedValuePath=Value</c>、<c>DisplayMemberPath=Display</c> 各管一维。
    ///
    /// <see cref="ToString"/> 返回显示文本，是给"没写 DisplayMemberPath 的 ComboBox / 调试输出"
    /// 兜底的——WPF 的默认模板在缺少显示路径时会调它，返回类型全名是最难看的失败形态。
    /// </summary>
    public sealed class ChoiceOption
    {
        /// <param name="value">写进变量、参与比较的整数值（.vms 与绑定里的那一份）</param>
        /// <param name="display">下拉里显示的名字；null/空白时回落到数值文本，绝不显示空白项</param>
        public ChoiceOption(int value, string? display)
        {
            Value = value;
            Display = string.IsNullOrWhiteSpace(display)
                ? value.ToString(CultureInfo.InvariantCulture)
                : display!;
        }

        /// <summary>选项的值（绑定目标；显示文本改了它也不变）</summary>
        public int Value { get; }

        /// <summary>选项的显示文本</summary>
        public string Display { get; }

        /// <inheritdoc />
        public override string ToString() => Display;
    }
}
