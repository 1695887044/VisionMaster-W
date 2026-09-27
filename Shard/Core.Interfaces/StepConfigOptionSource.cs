using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 候选项：**值**与**显示文本**分开。
    ///
    /// 【为什么必须分开 —— 运动卡的设备名就是这个道理】
    /// 候选值要写进流程（流程引用设备用的是"地址"这个稳定键），而显示文本是给人看的。
    /// 只显示 IP 时，有几张卡就极难辨认（截图里 127.0.0.1 / 192.168.0.11 一眼看不出谁是谁），
    /// 所以显示成「上料轴卡（192.168.0.11）」。
    /// 反过来，**若把显示文本当值用**，用户一改设备名，已经配好的流程就全断了 ——
    /// 这正是 <see cref="MotionDescriptor.DisplayName"/> 注释里那句
    /// "引用一律用 Id，不要用显示名 —— 显示名用户可以随便改"的同一个坑。
    /// </summary>
    public sealed class StepConfigOption
    {
        public StepConfigOption(string value, string? display = null)
        {
            Value = value;
            Display = string.IsNullOrWhiteSpace(display) ? value : display!;
        }

        /// <summary>写进流程的值（如卡地址）。**必须稳定**，不随界面改名而变</summary>
        public string Value { get; }

        /// <summary>界面上显示的文本（如「上料轴卡（192.168.0.11）」）；缺省时同 <see cref="Value"/></summary>
        public string Display { get; }

        /// <summary>
        /// 显示文本 —— 这是**兜底**，不是摆设。
        ///
        /// ComboBox 收起状态的渲染路径有好几条（DisplayMemberPath、SelectionBoxItem/
        /// SelectionBoxItemTemplate、ItemTemplate……），实际试下来：
        /// 弹层里的候选项显示对了，但**收起后的选中项**仍会走 ToString()
        ///（SelectionBoxItemTemplate 在 DisplayMemberPath 下并不按预想生成）。
        /// 与其追着每条渲染路径配模板，不如让对象自己会"说人话"——
        /// 任何路径落到 ToString() 时显示的都是 Display，而不是类型全名。
        /// </summary>
        public override string ToString() => Display;
    }

    /// <summary>
    /// 步骤参数候选项的来源注册表（宿主注入，消费方只读）。
    ///
    /// 【为什么放在契约层而不是 UI 库】
    /// 三类地方都要用它，而它们分属不同层：
    ///   · UI 库的属性面板（把标了 <see cref="StepConfigOptionsAttribute"/> 的常量渲染成下拉）；
    ///   · **插件的端口声明**（<see cref="DataPort.InputPort{T}.PresetOptions"/> 要在这里取候选 ——
    ///     "轴名 / 卡地址"这类候选项，插件的输入端口同样需要）；
    ///   · 宿主（注册来源）。
    /// 插件的端口声明发生在**契约层可见的代码**里，UI 库对插件不可见，
    /// 所以它只能住在契约层。它本身也不依赖任何 UI 类型。
    ///
    /// 【为什么是"注入"而不是自己取】候选项来自"当前方案的运动卡与轴映射"，
    /// 那是宿主的领域。契约层只保留一个函数指针，由宿主决定候选怎么算
    /// （例如只列"已启用"的轴、给卡地址配一个人能认出来的显示名）。
    /// 未注册时返回空列表：界面退化成可自由输入的文本框，而不是把人堵死 ——
    /// 还没配卡的时候本来就该允许手填。
    /// </summary>
    public static class StepConfigOptionSource
    {
        private static Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>>? _provider;

        /// <summary>由宿主在启动时注册一次</summary>
        public static void Register(Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>> provider)
            => _provider = provider;

        /// <summary>取某类别的候选（含显示文本）。未注册/取不到时返回空列表，调用方应容忍空</summary>
        public static IReadOnlyList<StepConfigOption> GetOptionItems(StepConfigOptionKind kind)
        {
            try
            {
                return _provider?.Invoke(kind) ?? Array.Empty<StepConfigOption>();
            }
            catch
            {
                // 候选取不到不该让绑定界面/端口声明崩：退化成可手填
                return Array.Empty<StepConfigOption>();
            }
        }

        /// <summary>
        /// 只要值（给 <see cref="DataPort.InputPort{T}.PresetOptions"/> 这类只有字符串列表的地方用）。
        /// 注意：这里**故意丢掉显示文本** —— 需要显示名的地方请用 <see cref="GetOptionItems"/>。
        /// </summary>
        public static IReadOnlyList<string> GetOptions(StepConfigOptionKind kind)
            => GetOptionItems(kind).Select(o => o.Value).ToList();
    }
}
