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
    /// 取候选的三种"空"状态。<see cref="Ok"/> 之外**都不是**"可以手填"的理由，
    /// 而是界面要说清的原因（见 <see cref="StepConfigOptionSource"/> 的 2026-10-24 口径）。
    /// </summary>
    public enum StepConfigOptionFailureKind
    {
        /// <summary>取到了候选，或有来源登记且清单不为空。</summary>
        Ok = 0,

        /// <summary>宿主没有为这个类别注册任何来源 —— **程序缺陷**（插件声明了 OptionKind 就该有人注册）。</summary>
        NoProvider = 1,

        /// <summary>来源登记了，但当前清单为空 —— 正常业务状态（还没配卡 / 没有已启用的轴）。</summary>
        EmptyList = 2,

        /// <summary>来源自己抛了异常 —— **程序缺陷**，异常文本在 <see cref="StepConfigOptionResult.ProviderErrors"/>。</summary>
        ProviderThrew = 3,
    }

    /// <summary>带原因的候选结果。<see cref="Items"/> 永远是具体列表（可能是空），从不返回 null。</summary>
    public sealed class StepConfigOptionResult
    {
        internal StepConfigOptionResult(
            StepConfigOptionKind kind,
            IReadOnlyList<StepConfigOption> items,
            bool hasProvider,
            IReadOnlyList<string>? providerErrors)
        {
            Kind = kind;
            Items = items ?? Array.Empty<StepConfigOption>();
            HasProvider = hasProvider;
            ProviderErrors = providerErrors ?? Array.Empty<string>();
        }

        public StepConfigOptionKind Kind { get; }

        /// <summary>去重合并后的候选（可能为空）。</summary>
        public IReadOnlyList<StepConfigOption> Items { get; }

        /// <summary>是否至少有一个来源为这个类别登记过（BooleanChoice 恒 true：契约层自带）。</summary>
        public bool HasProvider { get; }

        /// <summary>取候选时被吞掉的异常文本（"类型名: 消息"），空表示没有来源抛错。</summary>
        public IReadOnlyList<string> ProviderErrors { get; }

        /// <summary>三态判据。**判"能不能选"只看 <see cref="Items"/>.Count**，这个只判"为什么选不到"。</summary>
        public StepConfigOptionFailureKind Failure
            => ProviderErrors.Count > 0 ? StepConfigOptionFailureKind.ProviderThrew
             : !HasProvider ? StepConfigOptionFailureKind.NoProvider
             : Items.Count == 0 ? StepConfigOptionFailureKind.EmptyList
             : StepConfigOptionFailureKind.Ok;

        /// <summary>是否属于"程序缺陷"（要红着脸报错，而不是当正常空态）。</summary>
        public bool IsDefect
            => Failure is StepConfigOptionFailureKind.NoProvider or StepConfigOptionFailureKind.ProviderThrew;

        internal static StepConfigOptionResult BuiltIn(IReadOnlyList<StepConfigOption> items)
            => new(StepConfigOptionKind.BooleanChoice, items, hasProvider: true, providerErrors: null);

        internal static StepConfigOptionResult NoProvider(StepConfigOptionKind kind)
            => new(kind, Array.Empty<StepConfigOption>(), hasProvider: false, providerErrors: null);
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
    ///
    /// 【2026-10-24 口径变更（用户裁决）：名字类字段不再允许手填】
    /// 旧口径是"未注册时返回空列表：界面退化成可自由输入的文本框，而不是把人堵死 ——
    /// 还没配卡的时候本来就该允许手填"。实测这是一条**静默缺陷**：轴名/卡名这类字段
    /// 是框架权威清单，用户手填一个不存在的轴名时界面毫无提示，要到运行期才炸；
    /// 而"没注册来源"和"注册了但清单为空"两种完全不同的原因，在界面上又长得一模一样。
    /// 现口径：**候选取不到时不放开手填，而是把原因说清楚**（见 <see cref="GetOptionsResult"/>）：
    ///   · 该类别压根没登记来源 → 程序缺陷（宿主本应注册），红字报错；
    ///   · 已登记但清单为空 → 正常业务状态（还没配卡/没启用轴），给出"去哪里配"的入口；
    ///   · 来源自己抛异常 → 程序缺陷，异常被吞掉会让界面看起来"只是没数据"。
    /// 三种状态必须可区分，所以下面的读取入口返回的是**带原因的**结果，而不是裸空列表。
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

        /// <summary>取某类别的候选（含显示文本）。未注册/取不到时返回空列表，调用方应容忍空。
        /// <para>⚠ 只取列表会**丢掉失败原因**（"没登记来源"与"登记了但为空"都变成空列表）。
        /// 需要区分三种状态的地方请用 <see cref="GetOptionsResult"/>。</para></summary>
        public static IReadOnlyList<StepConfigOption> GetOptionItems(StepConfigOptionKind kind)
            => GetOptionsResult(kind).Items;

        /// <summary>
        /// 取某类别候选的**带原因**结果：三种"空"必须能分开。
        ///
        /// 【为什么不能只返回列表】"取不到候选"在界面上要渲染成三种完全不同的东西：
        ///   · <see cref="StepConfigOptionFailureKind.NoProvider"/> —— 宿主压根没为这个类别注册来源，
        ///     是**程序缺陷**（插件声明了 OptionKind，宿主本应注册），要红字报错；
        ///   · <see cref="StepConfigOptionFailureKind.EmptyList"/> —— 来源登记了但当前清单为空
        ///     （还没配运动卡 / 方案里没有已启用的轴），是**正常业务状态**，给"去哪里配"的入口；
        ///   · <see cref="StepConfigOptionFailureKind.ProviderThrew"/> —— 来源自己抛了异常。
        ///     以前这里是 <c>catch { }</c> 静默跳过，界面看起来只是"没数据"，
        ///     真正的缺陷被藏起来；现在把异常文本带出来。
        /// 三种状态下界面都**不再放开手填**（名字类字段是框架权威清单，见类注释的 2026-10-24 口径）。
        /// </summary>
        public static StepConfigOptionResult GetOptionsResult(StepConfigOptionKind kind)
        {
            // 布尔二选一是封闭集合，不需要任何宿主来源；优先返回，避免被测试夹具的
            // Register(kind => ...) 覆盖成空列表（否则 bool 端口又退回文本框 + 链接）。
            if (kind == StepConfigOptionKind.BooleanChoice)
                return StepConfigOptionResult.BuiltIn(BooleanChoices);

            var providers = _providers;
            if (providers.Length == 0)
                return StepConfigOptionResult.NoProvider(kind);

            var result = new List<StepConfigOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string>? failures = null;
            var produced = false;

            foreach (var provider in providers)
            {
                try
                {
                    var options = provider(kind);
                    if (options == null) continue;

                    produced = true;
                    foreach (var option in options)
                    {
                        if (option == null) continue;
                        // 去重按 Value（写进流程的稳定键），先注册者优先
                        if (seen.Add(option.Value)) result.Add(option);
                    }
                }
                catch (Exception ex)
                {
                    // 单个来源失败不该让绑定界面/端口声明崩：跳过它，其它来源照常。
                    // 但**不能静默**：把异常文本收集起来，交给界面按"程序缺陷"报出来。
                    (failures ??= new List<string>()).Add(ex.GetType().Name + ": " + ex.Message);
                }
            }

            return new StepConfigOptionResult(kind, result, produced, failures);
        }

        /// <summary>
        /// 只要值（给 <see cref="DataPort.InputPort{T}.PresetOptions"/> 这类只有字符串列表的地方用）。
        /// 注意：这里**故意丢掉显示文本** —— 需要显示名的地方请用 <see cref="GetOptionItems"/>。
        /// </summary>
        public static IReadOnlyList<string> GetOptions(StepConfigOptionKind kind)
            => GetOptionItems(kind).Select(o => o.Value).ToList();

        /// <summary>
        /// 某类别**当前是否取得到候选**。
        ///
        /// 【口径 2026-10-24 已改】旧注释把"取不到候选"一律当成正常业务状态，于是界面统一退化成
        /// "可手填的输入框 + 无候选，可手填"。现在名字类字段不再允许手填：**取不到候选时要么是
        /// 还没配（给"去哪里配"的入口），要么是宿主漏注册（程序缺陷，红字）**——这两种必须分开，
        /// 所以要判"为什么取不到"请用 <see cref="GetOptionsResult"/> 看
        /// <see cref="StepConfigOptionResult.Failure"/>，本方法只回答"现在有没有得选"。
        ///
        /// 【调用代价】候选由宿主现算（可能查方案），连着调两次会取两遍、还可能取到不一致的两份；
        /// 既要列表又要判空时请**只调一次 <see cref="GetOptionsResult"/>** 再判 Items.Count。
        /// </summary>
        public static bool HasOptions(StepConfigOptionKind kind) => GetOptionItems(kind).Count > 0;
    }
}
