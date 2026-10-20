using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

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

        /// <summary>
        /// 枚举候选：<see cref="Value"/> = 成员名，<see cref="Display"/> = 成员上的
        /// <see cref="DisplayAttribute.Name"/>（没标就回退成员名）。
        ///
        /// 【为什么必须有它】插件的枚举成员注定是英文标识符（Circle / Regression），
        /// 而成员上本来就标了中文 <c>[Display(Name="圆")]</c> —— 宿主扫描插件名与参数名读的是
        /// 同一个特性。下拉以前只喂 <c>Enum.GetNames</c> 的成员名，等于把这份已经写好的中文丢掉，
        /// 现场操作者看到的就是"Circle / Regression"这种认不出含义的英文。
        ///
        /// 【为什么值仍然是成员名，不能拿显示名当值】流程与 .vms JSON 存的是成员名
        /// （枚举写回走 <c>Enum.Parse</c> 按名字解析）。把"圆"当值写进流程，
        /// 日后改一次中文名就打断一条已配好的流程 —— 与 <see cref="MotionDescriptor"/>
        /// 那句"引用一律用 Id，不要用显示名"是同一个坑。
        /// </summary>
        public static StepConfigOption ForEnum(Type enumType, string memberName)
            => new(memberName,
                   enumType?.GetField(memberName, BindingFlags.Public | BindingFlags.Static)
                           ?.GetCustomAttribute<DisplayAttribute>()?.GetName());
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
        // 多个来源：运动模块与通讯模块各注册一份，互不覆盖。
        // 用"整数组替换"而不是可变 List：读侧（UI 打开下拉）无锁拿快照，注册只发生在启动期。
        private static readonly object _gate = new();
        private static volatile Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>>[] _providers
            = Array.Empty<Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>>>();

        // 布尔二选一：契约层自带的封闭集合，不依赖宿主注册。
        // 值必须是 "True"/"False"（与 bool.ToString() 一致），显示文本给人看。
        private static readonly StepConfigOption[] BooleanChoices =
        {
            new("True", "是（True）"),
            new("False", "否（False）"),
        };

        /// <summary>
        /// 注册（**替换全部**）候选来源。保留这个语义给测试夹具用：
        /// 夹具里 <c>Register(kind =&gt; …)</c> 后不应再看到宿主其它来源的候选。
        /// 生产模块请用 <see cref="RegisterAdditional"/>，否则后注册的会顶掉先注册的
        /// （运动卡/轴名与通讯变量名分属两个模块，谁也不该覆盖谁）。
        /// </summary>
        public static void Register(Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>> provider)
        {
            lock (_gate)
                _providers = new[] { provider };
        }

        /// <summary>追加一个候选来源（生产模块用）。同一类别由多个来源提供时按注册顺序合并、按值去重。</summary>
        public static void RegisterAdditional(Func<StepConfigOptionKind, IReadOnlyList<StepConfigOption>> provider)
        {
            lock (_gate)
                _providers = _providers.Append(provider).ToArray();
        }

        /// <summary>取某类别的候选（含显示文本）。未注册/取不到时返回空列表，调用方应容忍空</summary>
        public static IReadOnlyList<StepConfigOption> GetOptionItems(StepConfigOptionKind kind)
        {
            // 布尔二选一是封闭集合，不需要任何宿主来源；优先返回，避免被测试夹具的
            // Register(kind => ...) 覆盖成空列表（否则 bool 端口又退回文本框 + 链接）。
            if (kind == StepConfigOptionKind.BooleanChoice)
                return BooleanChoices;

            var providers = _providers;
            if (providers.Length == 0)
                return Array.Empty<StepConfigOption>();

            var result = new List<StepConfigOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in providers)
            {
                try
                {
                    var options = provider(kind);
                    if (options == null) continue;

                    foreach (var option in options)
                    {
                        if (option == null) continue;
                        // 去重按 Value（写进流程的稳定键），先注册者优先
                        if (seen.Add(option.Value)) result.Add(option);
                    }
                }
                catch
                {
                    // 单个来源失败不该让绑定界面/端口声明崩：跳过它，其它来源照常
                }
            }

            return result;
        }

        /// <summary>
        /// 只要值（给 <see cref="DataPort.InputPort{T}.PresetOptions"/> 这类只有字符串列表的地方用）。
        /// 注意：这里**故意丢掉显示文本** —— 需要显示名的地方请用 <see cref="GetOptionItems"/>。
        /// </summary>
        public static IReadOnlyList<string> GetOptions(StepConfigOptionKind kind)
            => GetOptionItems(kind).Select(o => o.Value).ToList();
    }
}
