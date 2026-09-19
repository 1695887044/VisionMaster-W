using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 属性面板的一个分组（<see cref="ScadaPropertyRowBase.Group"/> 相同的那些行）。
    ///
    /// 与工具箱的 <see cref="ScadaToolboxGroup"/> 一样是"一次构建、只读呈现"的快照：
    /// 选中变了就整体重建，不做增量 diff。十几行属性的重建成本比维护一套同步逻辑便宜得多，
    /// 也彻底杜绝"面板里留着上一个图元的值"这类劈叉。
    /// </summary>
    public sealed class ScadaPropertyGroup
    {
        public ScadaPropertyGroup(string name, IReadOnlyList<ScadaPropertyRowBase> rows)
        {
            Name = name;
            Rows = rows;
        }

        /// <summary>分组标题</summary>
        public string Name { get; }

        /// <summary>该组下的属性行（保持声明里的次序）</summary>
        public IReadOnlyList<ScadaPropertyRowBase> Rows { get; }
    }

    /// <summary>
    /// 属性面板一行的公共部分：一个 <see cref="IPropertySpec"/> + 一个正在被编辑的值。
    ///
    /// 为什么要抽这一层：面板的编辑器模板（文本框 / 数值框 / 色块 / 复选框 / 下拉）
    /// 认的其实只有 <c>DisplayName / Kind / Value / IsValid / SwatchBrush</c> 这几个名字，
    /// 跟"编辑的是图元还是画面"毫无关系。画面属性（S3-e3）要进同一个面板，
    /// 把这套逻辑留在 <see cref="ScadaPropertyRow"/> 里再抄一份，就是两份校验、两份标红、
    /// 两份"打完字回填规范值"——将来改一处漏一处。
    ///
    /// 一条硬约束：<b>基类构造函数绝不调用虚成员</b>。初值由派生类在 <c>base(…)</c> 的
    /// 实参里算好传进来（那时派生类自己的字段还没赋值，虚方法一调就是空引用）。
    /// </summary>
    public abstract class ScadaPropertyRowBase : BindableBase
    {
        /// <summary>颜色解析不出来时的色块（灰）——比留白诚实：留白看着像"这就是当前颜色"</summary>
        private static readonly Brush FallbackSwatch = Frozen(0xCC, 0xCC, 0xCC);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private string _value;
        private string? _error;

        /// <param name="initialValue">当前值的文本形态，由派生类算好传入（见类注释的硬约束）</param>
        protected ScadaPropertyRowBase(IPropertySpec spec, string initialValue)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _value = initialValue ?? string.Empty;
        }

        /// <summary>本行的属性声明</summary>
        public IPropertySpec Spec { get; }

        // ---- 以下全是声明的转手：XAML 只认行，不认声明，省掉模板里的两级路径 ----

        /// <summary>属性键</summary>
        public string Key => Spec.Key;

        /// <summary>标签文字</summary>
        public string DisplayName => Spec.DisplayName;

        /// <summary>所属分组</summary>
        public string Group => Spec.Group;

        /// <summary>编辑器类型（面板据此挑模板）</summary>
        public ElementPropertyKind Kind => Spec.Kind;

        /// <summary>Choice 型的下拉候选</summary>
        public IReadOnlyList<string> Choices => Spec.Choices;

        /// <summary>能否绑定变量（决定"ƒx"按钮显不显示）</summary>
        public bool IsBindable => Spec.IsBindable;

        /// <summary>
        /// 本行属性当前有没有绑工程变量。默认 <c>false</c>——画面属性（S3-e3）还没有绑定概念。
        ///
        /// 为什么放在基类：行模板是两种行共用的一份，模板里只该认"行"这个身份
        /// （与编辑器按 <see cref="Kind"/> 分流、复合编辑区按 <see cref="IsCompositeEditor"/>
        /// 分流是同一个手法）——否则 XAML 就得认识 <see cref="ScadaPropertyRow"/> 这个具体类型。
        /// </summary>
        public virtual bool HasBinding => false;

        /// <summary>
        /// 本行已绑的变量名（没绑时为 <c>null</c>）。
        /// 只作展示：寻址一律用变量 Id，名字随时可能过期（见 <c>ScadaBinding</c>）。
        /// </summary>
        public virtual string? BindingVariableName => null;

        /// <summary>
        /// 本行的编辑区是不是"<b>复合编辑区</b>"——不止一个输入控件，因此要独占整行宽度、
        /// 不排标签列。事件行（S5）是第一个：勾选框 + 一张动作表，塞进 82px 标签列旁边
        /// 那条窄格里根本没法用。
        ///
        /// 默认 <c>false</c>：绝大多数属性行就是"标签 + 一个编辑器"，不该为少数派付代价。
        /// 面板据此在 <c>PropertyRow</c> 里整份换模板——判据是<b>行的自述</b>而不是行类型，
        /// 与编辑器按 <see cref="Kind"/> 分流是同一个手法（那个文件刻意不认自定义类型）。
        /// </summary>
        public virtual bool IsCompositeEditor => false;

        /// <summary>悬停说明</summary>
        public string? Description => Spec.Description;

        /// <summary>
        /// 编辑框里的值。写回模型后一律用<b>模型的规范文本</b>回填输入框
        /// （见 <see cref="RefreshValue"/>），所以"宽"里打 -30 会被纠正成 30。
        ///
        /// 提交时机交给绑定侧：文本框用默认的 LostFocus，复选框/下拉用即时。
        /// 不在 PropertyChanged 上做，是因为"每敲一个字符就规范化一次"会把
        /// "1." 打成 "1"——用户永远输不进小数。
        /// </summary>
        public string Value
        {
            get => _value;
            set
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, _value, StringComparison.Ordinal)) return;

                if (!Commit(text))
                {
                    // 模型压根没接这个值（数字框里打了字母）。留着用户的文本并标红，
                    // 让他就地改完；把输入框抹回旧值等于当着他的面擦掉他打的东西。
                    _value = text;
                    RaisePropertyChanged(nameof(Value));
                    SetError(Kind == ElementPropertyKind.Number ? "请输入数字（小数点用 . ，不用千分位）" : null);
                    return;
                }

                RefreshValue();
            }
        }

        /// <summary>Bool 型行的复选框取值（换算放这儿，不为一处使用引进一个 IValueConverter 类）</summary>
        public bool? BoolValue
        {
            get => bool.TryParse(_value, out var flag) ? flag : null;
            set
            {
                if (value.HasValue)
                    Value = value.Value ? "True" : "False";
            }
        }

        /// <summary>值是否可用（false 时输入框标红，<see cref="ErrorText"/> 是悬停原因）</summary>
        public bool IsValid => _error == null;

        /// <summary>校验提示（null 表示没问题）</summary>
        public string? ErrorText => _error;

        /// <summary>
        /// Color 型的色块预览。
        ///
        /// 解析走 <see cref="BrushConverter"/>——和图元控件把 "#AARRGGBB" 变成 Fill 用的是同一个转换器。
        /// 不另写一份"井号加六个十六进制"的判断：那样色块显示的颜色和画布上真实填出的颜色
        /// 可能不一致（WPF 还认 "Red"、"#RGB"），用户会以为面板在骗他。
        /// </summary>
        public Brush SwatchBrush
            => Kind == ElementPropertyKind.Color && TryBrush(_value, out var brush)
                ? brush
                : FallbackSwatch;

        /// <summary>从模型读当前值（图元行走 <see cref="ElementValueAccess"/>，画面行走声明里的委托）</summary>
        protected abstract string Read();

        /// <summary>写回模型；返回 false 表示这个值模型没法接</summary>
        protected abstract bool Commit(string text);

        /// <summary>
        /// 用模型里的当前值回填本行。
        ///
        /// 外部写入（画布拖动改 X/Y、顶栏改网格、图层改名、S4 的绑定刷新）都汇到这一条路上来，
        /// 于是面板不会有"自己的第二份值"——它永远是模型的一个视图。
        ///
        /// 声明为 <c>virtual</c> 只为一件事：复合编辑区（<see cref="IsCompositeEditor"/>）除了
        /// 回读那一个值，还得把整块子结构的状态一起通知出去（事件行的动作表就是这么来的）。
        /// 派生类覆写时<b>必须先调 <c>base</c></b>，否则连值都不回读了。
        /// </summary>
        public virtual void RefreshValue()
        {
            string text = Read();

            if (!string.Equals(text, _value, StringComparison.Ordinal))
            {
                _value = text;
                RaisePropertyChanged(nameof(Value));
                RaisePropertyChanged(nameof(BoolValue));
            }

            // 越界照样写进模型（Min/Max 是编辑提示，不是模型约束：底图被拖出画面就得能存下负坐标），
            // 只把框标红——静默夹回边界值会让人以为鼠标坏了。
            SetError(RangeError(text));
            RaisePropertyChanged(nameof(SwatchBrush));
        }

        private string? RangeError(string text)
        {
            if (Kind != ElementPropertyKind.Number) return null;
            if (!TryNumber(text, out var value)) return null;

            // 边界是 ±∞ 时这两个比较恒为 false，所以提示里出现的数一定是有界的
            if (value < Spec.Min) return $"不能小于 {Spec.Min.ToString("0.##", CultureInfo.InvariantCulture)}";
            if (value > Spec.Max) return $"不能大于 {Spec.Max.ToString("0.##", CultureInfo.InvariantCulture)}";
            return null;
        }

        private void SetError(string? error)
        {
            if (string.Equals(_error, error, StringComparison.Ordinal)) return;

            _error = error;
            RaisePropertyChanged(nameof(IsValid));
            RaisePropertyChanged(nameof(ErrorText));
        }

        private static bool TryBrush(string? text, out Brush brush)
        {
            brush = FallbackSwatch;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                if (new BrushConverter().ConvertFromString(text.Trim()) is Brush parsed)
                {
                    parsed.Freeze();
                    brush = parsed;
                    return true;
                }
            }
            catch (NotSupportedException)
            {
                // 转换器的"不认识这串写法"就是异常，不是返回值——色块不该因为用户手滑打错一个字母掀掉面板
            }
            catch (FormatException)
            {
                // "#GGG" 这种看着像颜色、实际非法的串会走到颜色解析里抛这个。
                // 两类都得接：面板是给人改值的地方，非法输入是常态而不是程序错误。
            }

            return false;
        }

        /// <summary>
        /// 按不变文化解析数字 —— 和 <c>ElementValueAccess</c> 同一口径，否则 .vms 里的 "1.5"
        /// 会在这里被判成非法。给派生类复用，避免"图元一份、画面一份"的两套解析规则。
        /// </summary>
        protected static bool TryNumber(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 属性面板的一行图元属性：一个 <see cref="ElementPropertyDescriptor"/> + 一个正在被编辑的图元。
    ///
    /// 为什么不复用描述符自身当视图模型（描述符上有 DisplayName/Kind/默认值，看着够用）：
    /// 描述符实例是<b>跨图元共享</b>的（每个图元的六个几何属性用的是同一份
    /// <see cref="GeometryProperties.Standard"/>），把"当前值、红框状态"写回它就等于
    /// 让画面里所有矩形共用一个值。编辑态必须另立一份，且这份的生命周期只有一次选中。
    ///
    /// 读写只走 <see cref="ElementValueAccess"/> 一条通道，理由：几何键（"$X" 等）落在
    /// 强类型字段上、其它键落在属性袋上，这个分叉判断已经在 S2 写过一遍；在这里再写一遍
    /// 就是两处规则，将来加一个保留键必然漏一处。
    /// </summary>
    public sealed class ScadaPropertyRow : ScadaPropertyRowBase
    {
        public ScadaPropertyRow(ScadaElement element, ElementPropertyDescriptor descriptor)
            : base(descriptor ?? throw new ArgumentNullException(nameof(descriptor)),
                   ElementValueAccess.Read(element ?? throw new ArgumentNullException(nameof(element)), descriptor))
        {
            Element = element;
            Descriptor = descriptor;
        }

        /// <summary>本行写入的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>本行对应的属性声明（面板之外的人按它判断，如命令的 CanExecute）</summary>
        public ElementPropertyDescriptor Descriptor { get; }

        /// <summary>是不是几何键（不落属性袋，直接改模型字段）</summary>
        public bool IsGeometry => Descriptor.IsGeometry;

        /// <summary>
        /// 本行属性当前有没有绑工程变量——<b>直接问图元，不另存一份状态</b>
        /// （与类注释的口径 2 一致：面板永远是模型的一个视图）。
        /// </summary>
        public override bool HasBinding => Element.FindBinding(Key) != null;

        /// <summary>已绑变量的名字（只作展示；寻址一律用 Id，名字随时可能过期）</summary>
        public override string? BindingVariableName => Element.FindBinding(Key)?.VariableName;

        protected override string Read() => ElementValueAccess.Read(Element, Descriptor);

        protected override bool Commit(string text)
        {
            // 只有数字型需要预检：几何键的写入对非数字是"静默忽略"，
            // 让行先判一次，才知道该保留用户文本还是采纳模型的规范值。
            if (Kind == ElementPropertyKind.Number
                && !string.IsNullOrEmpty(text)
                && !TryNumber(text, out _))
            {
                return false;
            }

            ElementValueAccess.Write(Element, Key, text);
            return true;
        }

        /// <summary>
        /// 基类只回读"这个属性的值"；本行还要把<b>绑定状态</b>一起通知出去。
        ///
        /// 绑定的增删、换变量、改名字都会经 <see cref="ScadaElement.Bindings"/> 转到图元，
        /// 再由面板逐行打回这里（<c>OnElementPropertyChanged</c>），所以补通知只需要这一处；
        /// 命令侧不必自己去刷界面，也就不会出现"模型变了、按钮还亮着"的错位。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();

            RaisePropertyChanged(nameof(HasBinding));
            RaisePropertyChanged(nameof(BindingVariableName));
        }
    }

    /// <summary>
    /// 属性面板的一行<b>画面</b>属性（S3-e3）：没选中图元时，面板编辑的是画面自己。
    ///
    /// 与图元行唯一的差别就是读写落在哪儿——图元有属性袋（键值对，需要
    /// <see cref="ElementValueAccess"/> 那套分叉判断），画面全是强类型 CLR 属性，
    /// 读写由 <see cref="ScadaPagePropertySpec"/> 里的委托直接完成。
    /// 值语义、标红、色块、回填规范值这些行为全部继承自基类，一处都不重抄。
    ///
    /// 为什么要额外钉一份<b>方案</b>：画面上有少数几行的真值其实存在方案上（「启动画面」=
    /// <see cref="ScadaDocument.StartupPageId"/>，它是个跨画面的单选事实）。本类把它原样转手给声明，
    /// 自己既不做特判也不缓存——一旦在这里写"如果键是 StartupPage 就去改文档"，
    /// 声明驱动这条线就断了，以后每加一个方案级画面属性都要来改面板。
    /// </summary>
    public sealed class ScadaPagePropertyRow : ScadaPropertyRowBase
    {
        public ScadaPagePropertyRow(ScadaPage page, ScadaDocument document, ScadaPagePropertySpec spec)
            : base(spec ?? throw new ArgumentNullException(nameof(spec)),
                   spec.Read(page ?? throw new ArgumentNullException(nameof(page)),
                             document ?? throw new ArgumentNullException(nameof(document))))
        {
            Page = page;
            Document = document;
            Spec = spec;
        }

        /// <summary>本行写入的目标画面</summary>
        public ScadaPage Page { get; }

        /// <summary>目标画面所属的方案（声明里的委托拿它写方案级的值）</summary>
        public ScadaDocument Document { get; }

        /// <summary>本行对应的画面属性声明（把基类的 <c>IPropertySpec</c> 收窄成画面声明，省掉每次读写的强转）</summary>
        public new ScadaPagePropertySpec Spec { get; }

        protected override string Read() => Spec.Read(Page, Document);

        protected override bool Commit(string text)
        {
            // 与图元数字行同一预检：画面宽高/网格间距的 setter 对非数字是"原样忽略"
            // （TryNumber 不过就不赋值），不先判一次就没法区分"模型采纳了"和"模型没搭理"。
            if (Kind == ElementPropertyKind.Number
                && !string.IsNullOrEmpty(text)
                && !TryNumber(text, out _))
            {
                return false;
            }

            Spec.Write(Page, Document, text);
            return true;
        }
    }

    /// <summary>
    /// 事件行的声明（S5）：事件不是属性袋里的键、也不落盘成属性——它的真值就在
    /// <see cref="ScadaElement.EventHooks"/> 里有没有对应的那条钩子。声明存在的唯一目的，
    /// 是让 <see cref="ScadaEventRow"/> 能顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （Kind/DisplayName/Description）进同一个面板模板，而不是为事件单开一套 UI。
    ///
    /// 每种事件一份共享实例：声明本身无状态（读哪条钩子由行实例钉住），行对象才是一次
    /// 选中一份。与 <see cref="GeometryProperties.Standard"/> 跨图元共享同一个道理。
    /// </summary>
    internal sealed class ScadaEventSpec : IPropertySpec
    {
        /// <summary>本行对应的组态事件</summary>
        public ScadaEventType EventType { get; }

        public ScadaEventSpec(ScadaEventType eventType)
        {
            EventType = eventType;
            Key = $"Event.{(int)eventType}";
            // 显示名与运行日志里的「· 按下」「· 加载完成」同一个出处：
            // 面板上勾的那一行和日志里出现的那一行必须对得上号，否则用户对不上"我配的"和"响的"
            DisplayName = eventType.DisplayName();
        }

        public string Key { get; }

        public string DisplayName { get; }

        public ElementPropertyKind Kind => ElementPropertyKind.Bool;

        public string Group => "事件";

        public string? Description =>
            "勾选后，运行态命中该事件时按顺序执行这个事件下面配置的动作（新建时自动带一条\"记录日志\"便于验证）。设计态点击不触发";

        public bool IsBindable => false;

        public double Min => double.NegativeInfinity;

        public double Max => double.PositiveInfinity;

        public IReadOnlyList<string> Choices => Array.Empty<string>();
    }

    /// <summary>
    /// 动作类型下拉框的一项：把"给人看的名字"（<see cref="DisplayName"/>）和"落盘的枚举值"
    /// （<see cref="Type"/>）配成一对，让 ComboBox 用 <c>DisplayMemberPath</c> +
    /// <c>SelectedValuePath</c> 就能直接绑，不必为"枚举显示成中文"再引进一个值转换器。
    /// </summary>
    public sealed class ScadaActionTypeOption
    {
        public ScadaActionTypeOption(ScadaActionType type)
        {
            Type = type;
            DisplayName = type.DisplayName();
        }

        /// <summary>写回模型的值</summary>
        public ScadaActionType Type { get; }

        /// <summary>下拉里显示的名字（与运行日志里的措辞同一个出处）</summary>
        public string DisplayName { get; }
    }

    /// <summary>
    /// 属性面板的一行<b>事件</b>（S5）：一个勾选框 = "这个事件有没有配钩子"，
    /// 勾上之后在原地内联展开一张<b>动作表</b>（增 / 删 / 改类型 / 改日志文案 / 调顺序）。
    ///
    /// 为什么不复用 <see cref="ScadaPropertyRow"/>：事件不走 <see cref="ElementValueAccess"/>
    /// （属性袋/几何键那套分叉判断与事件无关，硬塞一个假键进去等于污染那条唯一通道）。
    /// 与画面行的取舍一致——读写逻辑各自一小段，值语义/标红/回填全部继承基类。
    ///
    /// 勾上 = 建钩子并自动补一条默认「记录日志」动作：空动作表在执行侧等同没配
    /// （<see cref="ScadaRuntime.RaiseElementEvent"/> 的第三道闸门），
    /// 不补的话用户勾完立刻去运行，看到的就是"点了没反应"，那比勾不上更伤信任。
    /// 取消勾选 = 整条钩子（连同它下面的动作）一起摘掉，与画面级「加载事件」同一口径。
    ///
    /// 设计期只有数据进出，<b>绝不执行动作</b>：这里是改模型，动作执行是运行态会话的事
    /// （第一道闸门 <c>IsRunning == false</c> 兜底，双保险）。
    ///
    /// <b>动作表为什么直接双向绑到模型对象</b>（而不是像别的行那样"读成文本、写回时解析"）：
    /// 动作本来就是有变更通知的模型对象（<see cref="ScadaAction"/>），它没有"文本形态"这回事，
    /// 也不存在"用户打的字模型接不接"的歧义——下拉框选中的是枚举、日志文案就是原样的字符串。
    /// 中间再套一层行视图模型，只会多出一份要同步的影子状态。
    ///
    /// <b>顺序有语义</b>：执行侧按 <see cref="ScadaEventHook.Actions"/> 的集合顺序依次执行，
    /// 所以"上移/下移"不是排版功能，是在改运行行为——第一版就把它做进来。
    /// </summary>
    public sealed class ScadaEventRow : ScadaPropertyRowBase
    {
        /// <summary>每种事件一份声明（含将来 S6/S8 追加的事件，枚举扩展时自动带上）</summary>
        private static readonly IReadOnlyDictionary<ScadaEventType, ScadaEventSpec> Specs
            = Enum.GetValues<ScadaEventType>().ToDictionary(t => t, t => new ScadaEventSpec(t));

        /// <summary>动作类型下拉的全部候选（枚举扩展时自动带上，与 <see cref="Specs"/> 同一手法）</summary>
        private static readonly IReadOnlyList<ScadaActionTypeOption> TypeChoices
            = Enum.GetValues<ScadaActionType>().Select(t => new ScadaActionTypeOption(t)).ToArray();

        public ScadaEventRow(ScadaElement element, ScadaEventType eventType, IScadaVariablePicker picker)
            : base(Specs[eventType],
                   (element ?? throw new ArgumentNullException(nameof(element))).FindEventHook(eventType) != null
                       ? "True" : "False")
        {
            Element = element;
            EventType = eventType;
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));

            AddActionCommand = new DelegateCommand(OnAddAction);
            RemoveActionCommand = new DelegateCommand<ScadaAction>(OnRemoveAction, a => IndexOf(a) >= 0);
            MoveActionUpCommand = new DelegateCommand<ScadaAction>(a => OnMoveAction(a, -1), a => CanMove(a, -1));
            MoveActionDownCommand = new DelegateCommand<ScadaAction>(a => OnMoveAction(a, +1), a => CanMove(a, +1));
            PickVariableCommand = new DelegateCommand<ScadaAction>(OnPickVariable, a => a != null);
        }

        /// <summary>「选变量」入口（弹窗怎么弹、弹哪个由这一层决定，见 IScadaVariablePicker）</summary>
        private readonly IScadaVariablePicker _picker;

        /// <summary>本行勾选的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>本行对应的组态事件</summary>
        public ScadaEventType EventType { get; }

        /// <summary>复合编辑区（开关 + 动作表），面板据此整份换模板</summary>
        public override bool IsCompositeEditor => true;

        /// <summary>
        /// 本事件已配的动作表；<b>没配钩子时为 <c>null</c></b>——"没有动作表"和"有一张空表"
        /// 是两回事，前者是压根没配，后者是配了但把动作删光了。模板据此整块隐藏动作区。
        /// </summary>
        public ObservableCollection<ScadaAction>? Actions => Element.FindEventHook(EventType)?.Actions;

        /// <summary>有动作表可编辑（= 已勾上），驱动动作区的可见性</summary>
        public bool HasActions => Actions != null;

        /// <summary>动作类型下拉的候选（所有事件行共用同一份静态列表）</summary>
        public IReadOnlyList<ScadaActionTypeOption> ActionTypeChoices => TypeChoices;

        public DelegateCommand AddActionCommand { get; }

        public DelegateCommand<ScadaAction> RemoveActionCommand { get; }

        public DelegateCommand<ScadaAction> MoveActionUpCommand { get; }

        public DelegateCommand<ScadaAction> MoveActionDownCommand { get; }

        /// <summary>「选变量」：参数是那条写变量动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickVariableCommand { get; }

        protected override string Read() => Element.FindEventHook(EventType) != null ? "True" : "False";

        protected override bool Commit(string text)
        {
            if (!bool.TryParse(text, out var flag))
                return false;

            if (flag)
            {
                // 只有"没钩子"这一种情况动手：已配过的钩子（含用户删光了动作的）原样保留——
                // 面板不该趁一次重复勾选悄悄往别人的动作表里塞东西。
                if (Element.FindEventHook(EventType) == null)
                {
                    var hook = Element.AddEventHook(EventType);
                    hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
                }
            }
            else
            {
                // 返回值不用看：读出来是 false 才可能走到这（UI 勾选框语义），没得删也无妨
                Element.RemoveEventHook(EventType);
            }

            return true;
        }

        /// <summary>
        /// 基类只回读"勾没勾上"这一个值；本行还要把动作表整块的对外状态一起通知出去
        /// （动作集合与其中任意一条的属性变更，都会经钩子 → 图元 → 面板汇到这条路上来）。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();

            RaisePropertyChanged(nameof(Actions));
            RaisePropertyChanged(nameof(HasActions));

            // 上/下/删的可用性取决于"这条动作在表里的位置"，位置一变就得重算——
            // 否则第一条动作的"↑"按钮还亮着，点下去没反应。
            RemoveActionCommand.RaiseCanExecuteChanged();
            MoveActionUpCommand.RaiseCanExecuteChanged();
            MoveActionDownCommand.RaiseCanExecuteChanged();
        }

        private void OnAddAction()
        {
            // GetOrAdd 而不是 Find：能点到这个按钮说明勾选框是勾上的，钩子必然已在；
            // 真遇到"表被别处摘了"的竞态，这里补一条空钩子也比抛异常强。
            Element.GetOrAddEventHook(EventType).Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
        }

        private void OnRemoveAction(ScadaAction? action)
        {
            if (action == null) return;

            // 删掉最后一条动作<b>不</b>顺手摘钩子：那是"配置"与"行为"两件事，
            // 用户可能只是先把旧动作清掉、紧接着要加新的。空动作表在执行侧本来就等同没配，
            // 面板上用一行提示把这件事说明白，比替他做决定更诚实。
            Actions?.Remove(action);
        }

        /// <summary>
        /// 「选变量」：把这条写变量动作当前的目标交给选择器，选中后回填。
        ///
        /// 回填走 <see cref="ScadaAction.BindVariable"/>（先 Id 再名字）：Id 是权威身份，
        /// 名字只作显示与"找不到 Id 时的兜底寻址"。取消时回调<b>一次都不触发</b>——
        /// 弹窗的取消不该被翻译成"清空原绑定"这种破坏性动作。
        /// </summary>
        private void OnPickVariable(ScadaAction? action)
        {
            if (action == null) return;

            _picker.Pick(action.VariableId, action.VariableName, (id, name) => action.BindVariable(id, name));
        }

        private void OnMoveAction(ScadaAction? action, int offset)
        {
            var list = Actions;
            int index = IndexOf(action);

            if (list == null || index < 0) return;

            int target = index + offset;
            if (target < 0 || target >= list.Count) return;

            // Move 而不是"删了再插"：Move 只发一次 CollectionChanged，订阅链上少一轮摘挂，
            // 也不会让被移的那条动作在中间态里短暂地"不存在"。
            list.Move(index, target);
        }

        private bool CanMove(ScadaAction? action, int offset)
        {
            var list = Actions;
            int index = IndexOf(action);

            if (list == null || index < 0) return false;

            int target = index + offset;
            return target >= 0 && target < list.Count;
        }

        private int IndexOf(ScadaAction? action)
            => action == null || Actions is not { } list ? -1 : list.IndexOf(action);
    }

    /// <summary>
    /// 组态属性面板：按声明自动生成编辑器行，写回模型。
    ///
    /// 与编辑器视图模型<b>共用同一个实例</b>（Prism 单例，见 <c>App.RegisterTypes</c>），
    /// 于是"画布点谁、面板就编辑谁"天然成立，不需要事件总线也不需要把选中态塞进模型。
    ///
    /// 面板有<b>两种模式</b>，由"选中了什么"唯一决定，且互斥：
    /// <code>
    /// 选中图元  →  图元属性（按 ElementDescriptor 的属性声明生成）
    /// 没选图元  →  画面属性（按 ScadaPageProperties.All 生成）——S3-e3 新增
    /// </code>
    /// 为什么用"没选中"当画面属性的入口，而不是再加一个"画面属性"按钮或再开一个面板：
    /// 一条判定（<c>SelectedElement == null</c>）就够，不引入"当前编辑对象"这个新概念，
    /// 也就不会出现"画面属性和图元属性同屏时谁在上"的扯皮；而且它正好顶替了原来那块
    /// "未选中图元"的空态提示，一寸 UI 都没多占。
    ///
    /// 三条口径：
    /// 1. 行源只有声明。面板不认识任何具体图元类型，也不写死"画面有哪几条属性"——
    ///    前者读 <see cref="ElementDescriptor"/>，后者读 <see cref="ScadaPageProperties"/>。
    ///    这条是整个 S3 最重要的性质，它把"属性面板"和"被编辑的对象"解耦开。
    /// 2. 只读回、只写回，不缓存值。行里的 <c>Value</c> 是模型当前值的视图，
    ///    任何模型变更都走 <see cref="ScadaPropertyRowBase.RefreshValue"/> 重新读一遍。
    /// 3. 画面级的<b>开关</b>（网格显示、吸附）不在本面板，仍由顶栏承载——它们在顶栏更顺手，
    ///    而且绑的是同一个模型属性，再来一处就成了"两个入口改同一个值"。
    ///    面板只补顶栏没给的那些：名称、说明、宽、高、背景色、网格间距。
    /// </summary>
    public class ScadaPropertyViewModel : BindableBase
    {
        private readonly ScadaEditorViewModel _editor;
        private readonly IUserNotifier _notifier;
        private readonly IScadaVariablePicker _picker;

        /// <summary>摊平的行（模型有外部写入时逐行回读；分组视图从它派生）</summary>
        private readonly List<ScadaPropertyRowBase> _rows = new();

        private bool _subscribed;
        private ScadaElement? _element;
        private ScadaPage? _page;

        public ScadaPropertyViewModel(ScadaEditorViewModel editor, IUserNotifier notifier, IScadaVariablePicker picker)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));

            // 参数化的 CanExecute 而不是 IsEnabled：行对象换了，可用性就得重算，
            // 挂在参数上比挂在面板状态上准（也便于断言里直接调 CanExecute）。
            // 命令参数用行基类而不是 ScadaPropertyRow：面板里两种行都有，命令认"行"就够了，
            // 将来画面属性要开绑定也只是把某条声明的 IsBindable 改成 true，签名不动。
            BindVariableCommand = new DelegateCommand<ScadaPropertyRowBase>(OnBindVariable, row => row != null && row.IsBindable);

            // 清除入口只对"确实绑了"的行可用：按钮的可见性也绑在 HasBinding 上，两处口径一致。
            // 谓词读的是行状态（不是面板状态），所以行状态一变就得由面板抛一次
            // RaiseCanExecuteChanged——见 OnElementPropertyChanged，那里是唯一的刷新点。
            ClearBindingCommand = new DelegateCommand<ScadaPropertyRowBase>(OnClearBinding, row => row != null && row.HasBinding);

            Activate();
        }

        /// <summary>当前编辑的图元（null 表示没选中图元，面板转入画面模式）</summary>
        public ScadaElement? Element => _element;

        /// <summary>当前编辑的画面（没选中图元时才有意义；无方案则为 null）</summary>
        public ScadaPage? Page => _page;

        /// <summary>有选中图元（驱动"锁定"勾选的可见性——锁定是图元才有的概念）</summary>
        public bool HasElement => _element != null;

        /// <summary>有没有行可显示（决定空态水印；两种模式都空才算空）</summary>
        public bool HasRows => _rows.Count > 0;

        /// <summary>空态文案：三种成因要分开说，否则用户会以为是面板坏了</summary>
        public string EmptyHint
        {
            get
            {
                if (_element != null)
                    return "这个图元的类型在本机没有注册，面板拿不到它的属性声明，所以列不出任何东西。\n\n换台机器打开这个方案时，它仍会正常显示。";

                if (_page != null)
                    return "当前画面没有可编辑的属性。";

                return "打开方案后，这里会显示画面自己的属性；在画布上点选一个图元，则切换到该图元的属性。";
            }
        }

        /// <summary>
        /// 标题："图元类型 · 图元名"或"画面 · 画面名"；没选中或类型没注册时给人话说明。
        ///
        /// 两种模式都要在标题里点名"编辑的是谁"——面板内容变了而标题没变，
        /// 用户会以为改图元名字改到了画面上（画面名和图元名都叫"名称"，光看标签分不出来）。
        /// </summary>
        public string HeaderText
        {
            get
            {
                if (_element is { } element)
                {
                    // 类型没注册是真实场景：.vms 带着插件图元跑到没装该插件的机器上。
                    // 这里必须说清楚"不是面板坏了，是这台机器不认识它"，
                    // 否则用户会以为属性丢了。
                    return ElementRegistry.Find(element.TypeKey) is { } descriptor
                        ? $"{descriptor.DisplayName} · {element.Name}"
                        : $"{element.TypeKey}（本机未注册该图元，无法编辑）";
                }

                if (_page is { } page)
                    return $"画面 · {page.Name}";

                return "未打开方案";
            }
        }

        /// <summary>分组后的属性行（声明次序权威：组按首现次序，组内按声明次序）</summary>
        public IReadOnlyList<ScadaPropertyGroup> Groups { get; private set; } = Array.Empty<ScadaPropertyGroup>();

        public DelegateCommand<ScadaPropertyRowBase> BindVariableCommand { get; }

        /// <summary>「清除绑定」：摘掉本行属性上的绑定（没绑的行不显示这个入口）</summary>
        public DelegateCommand<ScadaPropertyRowBase> ClearBindingCommand { get; }

        /// <summary>挂接编辑器与图元通知并对齐当前选中（幂等）</summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            _editor.PropertyChanged += OnEditorPropertyChanged;
            Rebuild();
        }

        /// <summary>摘干净（幂等）；面板离树时调用，让旧图元不被本面板钉住</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _editor.PropertyChanged -= OnEditorPropertyChanged;
            BindElement(null);
            BindPage(null);
        }

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认选中变更。画布自己拖动图元不改 SelectedElement，那条路走 OnElementPropertyChanged。
            // Pages/Document 一并接住：换方案时编辑器视图模型会先把 SelectedElement 置空，
            // 但那之后它可能直接赋新值而不经过 null，少接一条就会编辑到已废弃的图元。
            // SelectedPage 也要接：画面模式下，切页等于换编辑对象，不接就会把属性改到看不见的画面上。
            if (e.PropertyName is null
                or nameof(ScadaEditorViewModel.SelectedElement)
                or nameof(ScadaEditorViewModel.SelectedPage)
                or nameof(ScadaEditorViewModel.Pages)
                or nameof(ScadaEditorViewModel.Document))
            {
                Rebuild();
            }
        }

        private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            foreach (var row in _rows)
                row.RefreshValue();

            // 行里只通知了"这一行绑没绑"（HasBinding），可清除按钮的可用性挂在<b>面板的命令</b>上，
            // 命令的 CanExecute 谓词读的正是 HasBinding。WPF 的 ButtonBase 只在命令抛
            // CanExecuteChanged 时重算 IsEnabledCore——不抛，按钮就停在模板实例化那一刻的
            // 初值 false 上，表现为"绑上了，✕ 却点不动"（真机实锤）。
            // 行刷新与命令刷新必须成对出现，口径才不会分叉。
            ClearBindingCommand.RaiseCanExecuteChanged();

            if (e.PropertyName is null || e.PropertyName == nameof(ScadaElement.Name))
                RaisePropertyChanged(nameof(HeaderText));
        }

        /// <summary>
        /// 画面自己变了（顶栏勾网格、别处改名、面板写回宽高）：逐行回读 + 刷新标题。
        ///
        /// 这里不区分变更属性名，理由和图元侧一样：画面统共六行，全量回读比维护
        /// "哪个属性变更要刷哪几行"的映射便宜，而且映射表必然漏。
        /// </summary>
        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            foreach (var row in _rows)
                row.RefreshValue();

            if (e.PropertyName is null || e.PropertyName == nameof(ScadaPage.Name))
            {
                RaisePropertyChanged(nameof(HeaderText));
                RaisePropertyChanged(nameof(EmptyHint));
            }
        }

        /// <summary>
        /// 换编辑对象：整份重建。
        ///
        /// 不做"保留行、换个对象"的复用——同一个类型两次选中的行看似一样，但行里钉着
        /// 上一个实例，复用等于把泄漏和错值一起焊进面板。
        /// </summary>
        private void Rebuild()
        {
            var element = _editor.SelectedElement;

            // 图元优先：选中了图元就编辑图元，画面属性退到"没选东西"的时候才露脸。
            // 反过来（画面优先）会让"选中图元→面板没反应"，那是最刺眼的失效。
            var page = element == null ? _editor.SelectedPage : null;

            BindElement(element);
            BindPage(page);

            _rows.Clear();

            if (element != null && ElementRegistry.Find(element.TypeKey) is { } descriptor)
            {
                // 描述符在注册期已保证六个几何键齐全（ElementDescriptor.Validate），
                // 所以这里不需要为"没有名称/位置行"兜底——真缺了，那是注册表该报错的时候。
                foreach (var property in descriptor.Properties)
                    _rows.Add(new ScadaPropertyRow(element, property));

                // S5 事件行：描述符声明了哪些事件就出哪几行（Group="事件" 自动成组）。
                // 没声明任何事件的图元连这个组都不会有——"这个图元配不了事件"是描述符的表态，
                // 不是面板漏列；反过来，面板绝不自行罗列全部事件，那会把
                // "勾上了却永远不响的钩子"（Unloaded/ValueChanged 还没接触发源）漏给用户。
                foreach (var eventType in descriptor.Events)
                    _rows.Add(new ScadaEventRow(element, eventType, _picker));
            }
            else if (page != null)
            {
                // 传文档而不是让行自己去找：能走到这一支说明 SelectedPage 非空，而它只可能来自
                // 当前文档（换方案时 OnDocumentChanged 会把它重对齐成新文档的第一页或 null），
                // 所以这里递给行的永远是"那个画面真正所属的方案"，不是无方案时的兜底空文档。
                var document = _editor.Document;

                foreach (var spec in ScadaPageProperties.All)
                    _rows.Add(new ScadaPagePropertyRow(page, document, spec));
            }

            RaisePropertyChanged(nameof(Element));
            RaisePropertyChanged(nameof(Page));
            RaisePropertyChanged(nameof(HasElement));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(HeaderText));

            // GroupBy 的分组次序 = 键的首现次序，组内次序 = 源次序（LINQ to Objects 保证），
            // 正好就是声明里写的次序。与工具箱同一手法，不再二次排序。
            Groups = _rows
                .GroupBy(row => row.Group, StringComparer.Ordinal)
                .Select(g => new ScadaPropertyGroup(g.Key, g.ToArray()))
                .ToArray();

            RaisePropertyChanged(nameof(Groups));
        }

        /// <summary>换图元订阅（幂等：还是同一个就不动，避免反复摘挂漏一层）</summary>
        private void BindElement(ScadaElement? element)
        {
            if (ReferenceEquals(_element, element)) return;

            if (_element != null)
                _element.PropertyChanged -= OnElementPropertyChanged;

            _element = element;

            if (_element != null)
                _element.PropertyChanged += OnElementPropertyChanged;
        }

        /// <summary>换画面订阅（幂等，同上）</summary>
        private void BindPage(ScadaPage? page)
        {
            if (ReferenceEquals(_page, page)) return;

            if (_page != null)
                _page.PropertyChanged -= OnPagePropertyChanged;

            _page = page;

            if (_page != null)
                _page.PropertyChanged += OnPagePropertyChanged;
        }

        /// <summary>
        /// 「ƒx」：给本行属性挑一个工程变量，选中后落到 <see cref="ScadaElement.Bindings"/>。
        ///
        /// 三条口径：
        /// 1. 落模型一律走 <see cref="ScadaPage.TrySetBinding"/>（D3 统一写入口），
        ///    不在这里直接动集合——"谁能改模型"只能有一个答案，将来加撤销/校验才有一处可改。
        /// 2. 一个属性至多一条绑定：已绑过就是<b>换绑</b>（<c>TrySetBinding</c> 内部复用原条目），
        ///    用户已配的停用/格式串不会被一次换变量抹掉。
        /// 3. 取消<b>不触发回调</b>（见 <see cref="IScadaVariablePicker"/>），所以"清空绑定"另给一个
        ///    明确的清除入口（<see cref="ClearBindingCommand"/>），不藏在取消键里。
        /// </summary>
        private void OnBindVariable(ScadaPropertyRowBase? row)
        {
            // 画面属性行（S3-e3）没有绑定概念，其 IsBindable 恒为 false，命令的 CanExecute 已经挡住；
            // 这里再判一次类型是因为参数是"行"这个基类，而绑定的落点只存在于图元行上。
            if (row is not ScadaPropertyRow elementRow) return;

            // 用编辑器的当前画面，不用 _page：图元模式下 _page 是 null（两种模式互斥）。
            var page = _editor.SelectedPage;
            if (page == null) return;

            // 预选当前已绑的那个变量，让"换绑"是"改一处"而不是"重新找一遍"。
            var current = elementRow.Element.FindBinding(elementRow.Key);

            _picker.Pick(current?.VariableId ?? Guid.Empty, current?.VariableName, (id, name) =>
            {
                if (!page.TrySetBinding(elementRow.Element, elementRow.Key, id, name, out var error))
                    _notifier.ShowError(error);
            });
        }

        /// <summary>
        /// 「清除绑定」：把本行属性上的绑定摘掉。
        ///
        /// 只删绑定条目，<b>不动属性值本身</b>——绑定决定的是"运行态谁来写这个值"，
        /// 删掉绑定不等于把值还原成默认值，那件事得用户自己去改，替他做等于偷偷改数据。
        ///
        /// 成功不弹提示：绑定一摘，行上立刻回到"没绑"的样子，那就是最直接的反馈；
        /// 再弹一条气泡只是噪音。失败必须说（比如图元已被删），否则用户会以为点漏了。
        /// </summary>
        private void OnClearBinding(ScadaPropertyRowBase? row)
        {
            if (row is not ScadaPropertyRow elementRow) return;

            var page = _editor.SelectedPage;
            if (page == null) return;

            if (!page.TryRemoveBinding(elementRow.Element, elementRow.Key, out var error))
                _notifier.ShowError(error);
        }
    }
}
