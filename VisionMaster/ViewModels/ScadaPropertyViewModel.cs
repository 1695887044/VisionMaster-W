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
    /// 一行的编辑区形态——面板据此决定给这一行套哪份模板。
    ///
    /// 为什么是枚举而不是原来那个 <c>IsCompositeEditor</c> 布尔：那种写法只够区分"普通行"与
    /// "唯一一种复合行"。事件行之后动画行也成了复合编辑区，两者的内部结构却毫不相干
    /// （动作表 vs 档位卡片），一个布尔说不出"该换哪一份模板"，于是 XAML 里只能靠
    /// 叠 <c>DataTrigger</c> 的先后次序去抢——那是把"行的身份"编码进了触发器的书写顺序，
    /// 加第三种就要靠"谁写在后面"来决定谁赢，读代码的人根本看不出规则。
    ///
    /// 取值一旦发布不许改数值，只许往后追加（与 <see cref="ScadaAnimationType"/> 同一纪律）：
    /// 断言里钉的是名字，但改数值会让"旧断言 + 新代码"在别人机器上静默错位。
    /// </summary>
    public enum ScadaRowEditorKind
    {
        /// <summary>标签 + 一个编辑器（绝大多数属性行）</summary>
        Inline = 0,

        /// <summary>事件行：开关 + 内联动作表</summary>
        Event = 1,

        /// <summary>动画行：开关 + 驱动变量 + 档位表 / 参数区</summary>
        Animation = 2,
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
        /// <summary>
        /// 颜色解析不出来时的色块（灰）——比留白诚实：留白看着像"这就是当前颜色"。
        /// <c>internal</c> 而不是 <c>private</c>：动画档位卡片那套取色器（<see cref="ScadaColorSlot"/>）
        /// 与属性行的取色器对同一串坏值必须给出同一块灰，否则一张面板上两处颜色控件两种观感。
        /// </summary>
        internal static readonly Brush FallbackSwatch = Frozen(0xCC, 0xCC, 0xCC);

        /// <summary>
        /// 取色板的预设色。<b>顺序即排版</b>（WrapPanel 每行 8 格），所以是一支排好序的数组，
        /// 而不是集合初始化器里随手堆。
        ///
        /// 第二排开头四个就是 <c>Hmi.AlarmBanner</c> 的默认状态色：报警横幅上是什么绿，
        /// 取色板里就该有那一格——否则"我想跟横幅配成一套"得去别处抄十六进制。
        /// 第四、五排是成对的深/浅面板底色，第五排末尾两格是半透明黑（遮罩、投影填充）。
        ///
        /// 全部冻结：面板里几十行共用这一份，任何一格被改都会牵动所有取色板。
        ///
        /// <c>internal</c> 而不是 <c>private</c>：动画档位卡片的取色器（<see cref="ScadaColorSlot"/>）
        /// 共用同一份色板——两套色板走偏，用户在"图元填充色"里挑得到的那一格，
        /// 到了"档位背景色"里就找不到了。
        /// </summary>
        internal static readonly IReadOnlyList<SolidColorBrush> PaletteCells = new uint[]
        {
            // 灰阶
            0xFF000000u, 0xFF262626u, 0xFF404040u, 0xFF595959u,
            0xFF8C8C8Cu, 0xFFBFBFBFu, 0xFFE0E0E0u, 0xFFFFFFFFu,
            // 状态：正常 / 警告 / 严重 / 提示，后四格是配套的深色变体
            0xFF34C759u, 0xFFFFB020u, 0xFFE03A2Bu, 0xFF3B82F6u,
            0xFF2D7DD2u, 0xFF1F5C9Eu, 0xFF198754u, 0xFFD63384u,
            // 工业：橙 / 黄 / 青 / 紫 / 蓝 / 灰蓝
            0xFFFD7E14u, 0xFFFFC107u, 0xFF17A2B8u, 0xFF6610F2u,
            0xFF0D6EFDu, 0xFF6C757Du, 0xFF495057u, 0xFF343A40u,
            // 深色面板
            0xFF1E1E1Eu, 0xFF202020u, 0xFF2B2B2Bu, 0xFF0F1115u,
            0xFF11212Eu, 0xFF1B2A1Bu, 0xFF2E1B1Bu, 0xFF3C3C3Cu,
            // 浅色面板 + 半透明黑（遮罩 / 投影）
            0xFFF8F9FAu, 0xFFF1F3F5u, 0xFFE9ECEFu, 0xFFDEE2E6u,
            0xFFCED4DAu, 0xFFADB5BDu, 0x80000000u, 0x40000000u,
        }.Select(Swatch).ToArray();

        /// <summary>按 0xAARRGGBB 造一格冻结色板（写成整数而不是"#AARRGGBB"再解析：省掉启动时的一轮解析）</summary>
        private static SolidColorBrush Swatch(uint argb)
        {
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb));
            brush.Freeze();
            return brush;
        }

        private static Brush Frozen(byte r, byte g, byte b) => Frozen(Color.FromRgb(r, g, b));

        /// <summary>造一支冻结画刷（<see cref="ScadaColorSlot"/> 也要用：草稿预览色块与它同一个口径）</summary>
        internal static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private string _value;
        private string? _error;
        private bool _isColorPickerOpen;
        private string _colorDraft = string.Empty;

        /// <param name="initialValue">当前值的文本形态，由派生类算好传入（见类注释的硬约束）</param>
        protected ScadaPropertyRowBase(IPropertySpec spec, string initialValue)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _value = initialValue ?? string.Empty;

            // 取色器的三个命令。放在基类而不是颜色行里：EditColor 模板是图元行与画面行共用的一份，
            // 命令挂在派生类上，画面行（如「画面背景色」）点开就是一块点不动的色板。
            // 传方法组只是建委托，不构成"构造函数调用虚成员"，硬约束不破。
            PickColorCommand = new DelegateCommand<object>(OnPickColor);
            ApplyColorCommand = new DelegateCommand(OnApplyColor);
            CancelColorCommand = new DelegateCommand(OnCancelColor);
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

        /// <summary>
        /// Choice 型的候选<b>落盘值</b>（<c>"Circle"</c>、<c>"Output"</c> 这一层，进 .vms 的就是它们）。
        /// 默认直接转手声明里的那一份（描述符声明的候选是静态的）。
        ///
        /// 下拉框绑的不是本属性而是 <see cref="ChoiceOptions"/>（那一份另配了中文名）——
        /// 本属性留着是因为"这个属性能取哪些值"是模型侧的事实，与界面显示成什么语言无关。
        ///
        /// 声明为 <c>virtual</c> 只为一种行：候选<b>来自被编辑对象</b>而不是来自声明。
        /// 「所属图层」就是这样——图层表挂在画面上，而声明是无状态的共享实例
        /// （与 <see cref="GeometryProperties.Standard"/> 跨图元共享同一个道理），它给不出候选。
        /// 覆写时记得在 <see cref="RefreshValue"/> 里补一次 <c>RaisePropertyChanged</c>，
        /// 否则画面上图层增删后下拉框还是旧名单（通知的是 <see cref="ChoiceOptions"/>，见那条注释）。
        /// </summary>
        public virtual IReadOnlyList<string> Choices => Spec.Choices;

        /// <summary>
        /// 下拉框<b>实际绑定</b>的候选：<see cref="Choices"/> 里的落盘值各配一个中文显示名
        /// （见 <see cref="ScadaChoiceNames"/>）。
        ///
        /// 为什么另立一个属性、而不是让模板直接绑 <see cref="Choices"/>：模板要的是"值 + 名字"两列，
        /// 而 <c>Choices</c> 只有值——把一串落盘值交给模板，它就只能显示 <c>Circle</c>、<c>Output</c>。
        /// 而 <see cref="Choices"/> 本身不动：它是描述符声明的落盘值，文件格式的口径不该被界面文案牵着走
        /// （翻译只发生在显示层，写回的一律是 <see cref="ScadaChoiceOption.Value"/>）。
        ///
        /// 覆写者注意：模板绑的是本属性，所以候选集变了要通知的也是<b>本属性</b>；
        /// 通知 <c>Choices</c> 没人听（见 <see cref="ScadaRolePropertyRow.RefreshValue"/>）。
        /// </summary>
        public virtual IReadOnlyList<ScadaChoiceOption> ChoiceOptions => ScadaChoiceNames.ToOptions(Choices);

        /// <summary>能否绑定变量（决定"ƒx"按钮显不显示）</summary>
        public bool IsBindable => Spec.IsBindable;

        /// <summary>
        /// 本行属性当前有没有绑工程变量。默认 <c>false</c>——画面属性（S3-e3）还没有绑定概念。
        ///
        /// 为什么放在基类：行模板是两种行共用的一份，模板里只该认"行"这个身份
        /// （与编辑器按 <see cref="Kind"/> 分流、复合编辑区按 <see cref="EditorKind"/>
        /// 分流是同一个手法）——否则 XAML 就得认识 <see cref="ScadaPropertyRow"/> 这个具体类型。
        /// </summary>
        public virtual bool HasBinding => false;

        /// <summary>
        /// 本行已绑的变量名（没绑时为 <c>null</c>）。
        /// 只作展示：寻址一律用变量 Id，名字随时可能过期（见 <c>ScadaBinding</c>）。
        /// </summary>
        public virtual string? BindingVariableName => null;

        /// <summary>
        /// 本行的编辑区形态（见 <see cref="ScadaRowEditorKind"/>）。事件行与动画行不止一个输入控件，
        /// 因此要独占整行宽度、不排标签列——勾选框 + 一张动作表（或档位卡片）塞进 82px 标签列旁边
        /// 那条窄格里根本没法用。
        ///
        /// 默认 <see cref="ScadaRowEditorKind.Inline"/>：绝大多数属性行就是"标签 + 一个编辑器"，
        /// 不该为少数派付代价。面板据此在 <c>PropertyRow</c> 里整份换模板——判据是<b>行的自述</b>
        /// 而不是行类型，与编辑器按 <see cref="Kind"/> 分流是同一个手法（那个文件刻意不认自定义类型）。
        /// </summary>
        public virtual ScadaRowEditorKind EditorKind => ScadaRowEditorKind.Inline;

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

                    // 数字框的失败原因是能替用户说清的（打的是字母）；其它类型的失败原因
                    // 只有行自己知道（图层名对不上、变量找不到…），Commit 已经写进 _error 了，
                    // 这里不能再抹一次——抹成 null 就成了"标红了却说不清为什么"。
                    if (Kind == ElementPropertyKind.Number)
                        SetError("请输入数字（小数点用 . ，不用千分位）");

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

        // ---- 取色板（S10）----
        // 只有 Kind == Color 的行会用到，但成员放在基类：EditColor 模板是图元行与画面行
        // 共用的一份，写在派生类里就得写两遍（与 HasBinding 放在基类是同一个理由）。
        // 面板里其余类型的行白白多挂三个命令对象，代价可以忽略。

        /// <summary>
        /// 取色板弹窗开着没有。色块（ToggleButton）与弹窗（Popup）绑的是同一个状态，
        /// 一个状态两个视图——不必再来一个"打开取色板"的命令。
        /// </summary>
        public bool IsColorPickerOpen
        {
            get => _isColorPickerOpen;
            set
            {
                if (_isColorPickerOpen == value) return;
                _isColorPickerOpen = value;

                // 每次打开都重新起草：面板是模型的一个视图，上一次关窗时留下的草稿
                // 可能已经被 Ctrl+Z 撤掉、或被画布上的操作改过，留着它就是第二份状态。
                if (value) ColorDraft = _value;

                RaisePropertyChanged(nameof(IsColorPickerOpen));
            }
        }

        /// <summary>取色板的预设色（冻结的，见 <see cref="PaletteCells"/>）</summary>
        public IReadOnlyList<SolidColorBrush> ColorPalette => PaletteCells;

        /// <summary>
        /// 取色板里的<b>草稿</b>（#AARRGGBB）。为什么要有草稿：一次提交 = 一条撤销记录，
        /// 而"拖一下透明度、连点几格看看"是取色时的常态，即时写回模型等于把撤销栈灌满、
        /// 把 Ctrl+Z 废掉。所以弹窗里改的全是它，点「确定」才经 <see cref="Value"/>
        /// 落到模型（那时才产生唯一的一条撤销记录）。
        /// </summary>
        public string ColorDraft
        {
            get => _colorDraft;
            set
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, _colorDraft, StringComparison.Ordinal)) return;

                _colorDraft = text;
                RaisePropertyChanged(nameof(ColorDraft));
                RaisePropertyChanged(nameof(ColorDraftBrush));
                // 透明度滑块跟着十六进制框走：手打 "#80…" 时滑块也该跳到 128，
                // 否则两个控件各说各话（草稿串是唯一真相，滑块只是它的一个视图）。
                RaisePropertyChanged(nameof(ColorDraftAlpha));
            }
        }

        /// <summary>
        /// 草稿的透明度（0..255）。取值是把 <see cref="ColorDraft"/> 的 AA 段读出来，
        /// 赋值是把它改写回同一个草稿串——<b>不另存一份 alpha 字段</b>，那才会真的劈叉。
        /// </summary>
        public double ColorDraftAlpha
        {
            get => TryColor(_colorDraft, out var color) ? color.A : 255d;
            set
            {
                if (!TryColor(_colorDraft, out var color))
                {
                    // 草稿现在不是个颜色（用户正打到 "#3B8" 这种半截串）。这里绝不把文本抹成
                    // 一个默认色——"把输入框抹回旧值等于当着他的面擦掉他打的东西"——只把滑块弹回去，
                    // 如实告诉他"这串现在没有透明度可言"。
                    RaisePropertyChanged(nameof(ColorDraftAlpha));
                    return;
                }

                byte alpha = (byte)Math.Max(0d, Math.Min(255d, Math.Round(value)));
                ColorDraft = Format(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        /// <summary>草稿的预览色块（解析不出来时同样用灰，与 <see cref="SwatchBrush"/> 一个口径）</summary>
        public Brush ColorDraftBrush
            => TryColor(_colorDraft, out var color) ? Frozen(color) : FallbackSwatch;

        /// <summary>点预设色格：<c>CommandParameter</c> 是 <see cref="Color"/></summary>
        public DelegateCommand<object> PickColorCommand { get; }

        /// <summary>「确定」：把草稿写回模型</summary>
        public DelegateCommand ApplyColorCommand { get; }

        /// <summary>「取消」：丢掉草稿（草稿从没进过模型，所以没什么要还原的）</summary>
        public DelegateCommand CancelColorCommand { get; }

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
        /// 声明为 <c>virtual</c> 只为一件事：复合编辑区（<see cref="ScadaRowEditorKind.Event"/> /
        /// <see cref="ScadaRowEditorKind.Animation"/>）除了回读那一个值，还得把整块子结构的状态
        /// 一起通知出去（事件行的动作表、动画行的档位卡片就是这么来的）。
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

        /// <summary>
        /// 记下/清掉本行的校验提示（<c>null</c> = 没问题）。
        /// <c>protected</c> 是给派生类用的：失败的<b>原因</b>只有行自己说得清
        /// （数字框打字母、图层名对不上…），基类替它写死一句只会说错话。
        /// </summary>
        protected void SetError(string? error)
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

        // ---- 取色板的私有部分 ----

        private void OnPickColor(object parameter)
        {
            if (parameter is not Color color) return;

            // 预设色都是不透明的，但保留用户已经调好的透明度：
            // "先调到 50% 再挨个色看看效果"是取色时最常见的用法，每换一格就弹回不透明
            // 等于每次都得重拖一遍滑块。
            byte alpha = TryColor(_colorDraft, out var current) ? current.A : (byte)255;
            ColorDraft = Format(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private void OnApplyColor()
        {
            // 走 Value 而不是自己调 ElementValueAccess.Write：撤销作用域、失败标红、
            // 回填模型规范值这三件事都在 Value 的 setter 里，绕过它就是各抄一遍。
            // 顺带一条：草稿与当前值相同时 Value 会提前返回，不产生空的撤销记录。
            Value = _colorDraft;
            IsColorPickerOpen = false;
        }

        private void OnCancelColor() => IsColorPickerOpen = false;

        /// <summary>
        /// 把模型里的颜色串解成四个通道。
        ///
        /// 这里用 <see cref="ColorConverter"/> 而不是 <see cref="BrushConverter"/>：要的是通道值，
        /// 拿一支画刷还得再判类型。<see cref="SwatchBrush"/> 那边坚持用 BrushConverter 是另一回事
        /// ——它要的正是"画布上会填成什么样"的那支画刷。
        ///
        /// <c>internal static</c> 给 <see cref="ScadaColorSlot"/> 复用：解析规则只许有一份，
        /// 否则"图元填充色"认得 <c>Red</c>、"档位背景色"不认得，同一串值两处两样。
        /// </summary>
        internal static bool TryColor(string? text, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                if (ColorConverter.ConvertFromString(text.Trim()) is Color parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch (NotSupportedException)
            {
                // 与 TryBrush 同口径：非法输入在属性面板里是常态而不是程序错误
            }
            catch (FormatException)
            {
                // "#GGG" 这种"看着像颜色、实际非法"的串会走到这里
            }

            return false;
        }

        /// <summary>把颜色写成模型认的文本形态（#AARRGGBB，即 <c>ElementPropertyKind.Color</c> 的约定）</summary>
        internal static string Format(Color color)
            => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
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

            // 一次提交 = 一条撤销记录（S9-d）。作用域必须罩在 Write 外面：
            // 几何键走的是 X/Y/Width/Height 的强类型 setter，属性袋键走 SetProperty(key, value)，
            // 两条路都只在"有活动作用域"时才被记进撤销栈。
            using (Element.BeginEdit($"修改 {Descriptor.DisplayName}"))
            {
                ElementValueAccess.Write(Element, Key, text);
            }

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

            // 同图元行：作用域要罩住 Write。画面行里少数几项的真值在方案上（如「启动画面」写的是
            // ScadaDocument.StartupPageId），所以作用域开在画面上——它是这两条路的共同祖先，
            // 而 ScadaChangeScope 本来就是线程级的，开在谁身上不影响记录内容。
            using (Page.BeginEdit($"修改 {Spec.DisplayName}"))
            {
                Spec.Write(Page, Document, text);
            }

            return true;
        }
    }

    /// <summary>
    /// 事件行的声明（S5）：事件不是属性袋里的键、也不落盘成属性——它的真值就在
    /// 宿主的 <see cref="IScadaEventHost.EventHooks"/> 里有没有对应的那条钩子。声明存在的唯一目的，
    /// 是让 <see cref="ScadaEventRow"/> 能顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （Kind/DisplayName/Description）进同一个面板模板，而不是为事件单开一套 UI。
    ///
    /// <b>分组为什么是构造参数而不是写死的常量</b>：同一个事件类型在不同宿主上的归属不同——
    /// 图元事件自成一「事件」组（图元面板的最后一组），画面事件落在「运行」组
    /// （与「启动画面」同处：都是"这一页跑起来是什么样"）。分组不是事件自身的属性，
    /// 而是"这类宿主的事件长在哪儿"的陈述，所以由调用方按自己那份清单给。
    /// </summary>
    internal sealed class ScadaEventSpec : IPropertySpec
    {
        /// <summary>本行对应的组态事件</summary>
        public ScadaEventType EventType { get; }

        public ScadaEventSpec(ScadaEventType eventType, string group)
        {
            EventType = eventType;
            Group = group;
            Key = $"Event.{(int)eventType}";
            // 显示名与运行日志里的「· 按下」「· 加载完成」同一个出处：
            // 面板上勾的那一行和日志里出现的那一行必须对得上号，否则用户对不上"我配的"和"响的"
            DisplayName = eventType.DisplayName();
        }

        public string Key { get; }

        public string DisplayName { get; }

        public ElementPropertyKind Kind => ElementPropertyKind.Bool;

        public string Group { get; }

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
    /// 勾上之后在原地内联展开一张<b>动作表</b>。
    ///
    /// <b>本行现在只是一层"适配器"</b>：真正的编辑逻辑（勾选语义、增删改序、选变量/选画面）
    /// 全部住在 <see cref="ScadaEventEditorViewModel"/> 里，由公共控件
    /// <c>Views.Controls.ScadaEventEditor</c> 呈现。本行保留同名同签名的转发成员，
    /// 是因为它还背着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （<see cref="ScadaRowEditorKind"/> / <c>Value</c> / <c>BoolValue</c>），
    /// 面板模板与既有断言都还认这些名字。
    ///
    /// 为什么要抽出去：同一套编辑逻辑现在有两个消费者——属性面板的事件行（图元事件、画面事件）
    /// 与独立的「变量事件」弹窗（变量级 5 类事件）。抽公共控件后两边共用一份，
    /// 不再各写一遍"勾选建钩子""增删动作""调次序"。
    ///
    /// <b>图元与画面共用这一行</b>（S5 起图元、S3-e4 起画面）：宿主收成 <see cref="IScadaEventHost"/>，
    /// 两者的差别只剩两处，且都不在这行里：① 事件清单从哪来（图元是 <see cref="ElementDescriptor.Events"/>、
    /// 画面是 <see cref="ScadaPageEvents.All"/>）；② 落在哪一组（构造时给，见 <see cref="ElementGroup"/>）。
    /// </summary>
    public sealed class ScadaEventRow : ScadaPropertyRowBase
    {
        /// <summary>
        /// 图元事件行的分组名。画面事件另有归属（<see cref="ScadaPageEvents.Group"/> =「运行」），
        /// 所以分组是构造参数而不是常量——理由见 <see cref="ScadaEventSpec"/> 的类注释。
        /// </summary>
        public const string ElementGroup = "事件";

        /// <param name="host">本行勾选的目标宿主：图元或画面，见 <see cref="IScadaEventHost"/></param>
        /// <param name="group">本行落在哪一组（图元事件用 <see cref="ElementGroup"/>，画面事件用 <see cref="ScadaPageEvents.Group"/>）</param>
        public ScadaEventRow(IScadaEventHost host, ScadaEventType eventType, string group, IScadaVariablePicker picker, IScadaPagePicker pagePicker)
            : base(new ScadaEventSpec(eventType, group),
                   (host ?? throw new ArgumentNullException(nameof(host))).FindEventHook(eventType) != null
                       ? "True" : "False")
        {
            // 显示名与悬停说明沿用声明里的那一份：面板上勾的那一行和运行日志里出现的那一行
            // 必须对得上号，否则用户对不上"我配的"和"响的"。
            Editor = new ScadaEventEditorViewModel(host, eventType, DisplayName, Description, picker, pagePicker);

            // 本行仍是"面板面向的那张脸"：编辑器对外状态（动作表整块、命令可用性）一变就转出去，
            // 既有的绑定与断言（认 ScadaEventRow.Actions / HasActions）不必知道底下换了实现。
            // 只转本行真有的名字——IsConfigured 是编辑器内部的勾选框真值，行上没有这一格。
            Editor.PropertyChanged += OnEditorPropertyChanged;
        }

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaEventEditorViewModel.Actions)
                || e.PropertyName == nameof(ScadaEventEditorViewModel.HasActions))
            {
                // 两个名字与行上的同名属性逐字相同，所以直接转字符串即可。
                RaisePropertyChanged(e.PropertyName);
            }
        }

        /// <summary>真正的编辑逻辑（公共控件绑的就是它）</summary>
        public ScadaEventEditorViewModel Editor { get; }

        /// <summary>本行勾选的目标宿主（转手 <see cref="Editor"/>，保留是为了既有读法）</summary>
        public IScadaEventHost Host => Editor.Host;

        /// <summary>本行对应的组态事件</summary>
        public ScadaEventType EventType => Editor.EventType;

        /// <summary>复合编辑区（开关 + 动作表），面板据此整份换模板</summary>
        public override ScadaRowEditorKind EditorKind => ScadaRowEditorKind.Event;

        /// <summary>本事件已配的动作表；没配钩子时为 <c>null</c>（口径见 <see cref="ScadaEventEditorViewModel.Actions"/>）</summary>
        public ObservableCollection<ScadaAction>? Actions => Editor.Actions;

        /// <summary>有动作表可编辑（= 已勾上），驱动动作区的可见性</summary>
        public bool HasActions => Editor.HasActions;

        /// <summary>动作类型下拉的候选</summary>
        public IReadOnlyList<ScadaActionTypeOption> ActionTypeChoices => Editor.ActionTypeChoices;

        public DelegateCommand AddActionCommand => Editor.AddActionCommand;

        public DelegateCommand<ScadaAction> RemoveActionCommand => Editor.RemoveActionCommand;

        public DelegateCommand<ScadaAction> MoveActionUpCommand => Editor.MoveActionUpCommand;

        public DelegateCommand<ScadaAction> MoveActionDownCommand => Editor.MoveActionDownCommand;

        /// <summary>「选变量」：参数是那条写变量动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickVariableCommand => Editor.PickVariableCommand;

        /// <summary>「选画面」：参数是那条切换画面动作（弹窗确认后回填它的 Id 与名字）</summary>
        public DelegateCommand<ScadaAction> PickPageCommand => Editor.PickPageCommand;

        protected override string Read() => Editor.ReadConfigured();

        protected override bool Commit(string text) => Editor.CommitConfigured(text);

        /// <summary>
        /// 基类只回读"勾没勾上"这一个值；本行还要把动作表整块的对外状态一起通知出去
        /// （动作集合与其中任意一条的属性变更，都会经钩子 → 宿主 → 面板汇到这条路上来）。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();
            Editor.RefreshFromHost();
        }
    }

    /// <summary>
    /// 动画行的声明（r4）：动画和事件一样，不是属性袋里的键、也不落盘成属性——它的真值就在
    /// <see cref="ScadaElement.Animations"/> 里有没有对应类型的那一条。声明存在的唯一目的，
    /// 是让 <see cref="ScadaAnimationRow"/> 顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （Kind/DisplayName/Description）进同一个面板模板，而不是为动画单开一套 UI。
    ///
    /// <see cref="Kind"/> 报 <see cref="ElementPropertyKind.Bool"/>：本行那个勾选框问的是
    /// "这条动画配没配"（勾上 = <see cref="ScadaElement.GetOrAddAnimation"/>，取消 =
    /// <see cref="ScadaElement.RemoveAnimation"/>），与事件行的"这个事件有没有配钩子"逐字同一语义。
    /// "跑不跑"是另一件事，由展开区里那颗「启用」复选框绑 <see cref="ScadaAnimation.IsEnabled"/> 管。
    ///
    /// <see cref="Group"/> 是常量而不是构造参数（对比 <see cref="ScadaEventSpec"/>）：动画只长在图元上
    /// ——画面自己没有"会动"这回事，动的是图元——所以这一组只可能出现在图元面板里。
    /// </summary>
    internal sealed class ScadaAnimationSpec : IPropertySpec
    {
        /// <summary>图元动画行的分组名（画面没有这一组，所以不必像事件那样由调用方给）</summary>
        public const string GroupName = "动画";

        public ScadaAnimationSpec(ScadaAnimationType animationType)
        {
            AnimationType = animationType;
            // 前缀刻意与属性键、事件键都不同：Key 是诊断与断言里认行的凭据，
            // 撞上 "Fill" 这类真属性键会让"我改的是哪一行"变得说不清。
            Key = $"Animation.{(int)animationType}";
            // 显示名与运行日志里的动画名同一个出处（ScadaAnimationTypeExtensions.DisplayName）
            DisplayName = animationType.DisplayName();
        }

        /// <summary>本行对应的动画类型</summary>
        public ScadaAnimationType AnimationType { get; }

        public string Key { get; }

        public string DisplayName { get; }

        public ElementPropertyKind Kind => ElementPropertyKind.Bool;

        public string Group => GroupName;

        public string? Description => AnimationType switch
        {
            ScadaAnimationType.Appearance =>
                "勾选后按「值 → 外观」的多档表改变图元的前景色/背景色/闪烁；自上而下先命中先用，一档都不命中则恢复设计外观",
            ScadaAnimationType.HorizontalMove =>
                "勾选后按驱动变量的值在范围内线性改变图元的 X；起始位置就是图元当前的 X，不可编辑",
            ScadaAnimationType.VerticalMove =>
                "勾选后按驱动变量的值在范围内线性改变图元的 Y；起始位置就是图元当前的 Y，不可编辑",
            ScadaAnimationType.Visibility =>
                "勾选后变量值命中范围时按「对象状态」显示或隐藏；不命中时不接管可见性，交回图层判定",
            _ => "勾选后运行态按驱动变量的值改变图元的显示",
        };

        public bool IsBindable => false;

        public double Min => double.NegativeInfinity;

        public double Max => double.PositiveInfinity;

        public IReadOnlyList<string> Choices => Array.Empty<string>();
    }

    /// <summary>
    /// 取色器的一格"槽"：把"一个颜色通道 + 它的弹窗状态"打包成一个可绑对象。
    ///
    /// 为什么要有它、而不是像属性行那样把取色状态摊在行上：动画档位卡片<b>一档就有两个颜色通道</b>
    /// （前景、背景），一条动画上又有若干档——摊在行上就成了 2N 份成员，而 N 是用户随时会变的数。
    /// 打包成一格之后，档位卡片只需两个属性（<see cref="ScadaAnimationStateViewModel.ForegroundSlot"/>
    /// 与 <see cref="ScadaAnimationStateViewModel.FillSlot"/>），增删档位不再牵动任何成员声明。
    ///
    /// 对外成员名与 <see cref="ScadaPropertyRowBase"/> 的取色器<b>逐字相同</b>
    /// （IsColorPickerOpen / ColorPalette / ColorDraft / ColorDraftAlpha / ColorDraftBrush /
    /// PickColorCommand / ApplyColorCommand / CancelColorCommand）：XAML 里那份取色弹窗体
    /// （<c>ColorPickerBody</c>）因此只有一份，属性行与档位卡片共用——同一块色板、同一套草稿语义、
    /// 同一个"点确定才产生一条撤销记录"。
    /// </summary>
    public sealed class ScadaColorSlot : BindableBase
    {
        private readonly Func<string?> _read;
        private readonly Action<string?> _write;
        private bool _isOpen;
        private string _draft = string.Empty;

        /// <param name="label">本槽在卡片上怎么称呼自己（"前景"/"背景"）</param>
        /// <param name="read">从模型读当前颜色串（<c>null</c>/空串 = 本档不改这个通道）</param>
        /// <param name="write">把颜色串写回模型；<b>实现方负责开作用域</b>（见 <see cref="ScadaAnimationState.BeginEdit"/>）</param>
        public ScadaColorSlot(string label, Func<string?> read, Action<string?> write)
        {
            Label = label ?? string.Empty;
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));

            PickColorCommand = new DelegateCommand<object>(OnPickColor);
            ApplyColorCommand = new DelegateCommand(OnApplyColor);
            CancelColorCommand = new DelegateCommand(OnCancelColor);
            ClearCommand = new DelegateCommand(OnClear, () => HasColor);
        }

        /// <summary>本槽的名字（"前景"/"背景"），卡片上那行小字用它</summary>
        public string Label { get; }

        /// <summary>本槽配了颜色没有（没配时色块画成灰，并在悬停提示里说明）</summary>
        public bool HasColor => !string.IsNullOrWhiteSpace(_read());

        /// <summary>
        /// 本槽当前颜色的色块。解析不出来（含"还没配"）时用 <see cref="ScadaPropertyRowBase.FallbackSwatch"/>，
        /// 与属性行的色块同一个口径——同一张面板上两处颜色控件对同一串坏值必须给出同一种观感。
        /// </summary>
        public Brush SwatchBrush
            => ScadaPropertyRowBase.TryColor(_read(), out var color)
                ? ScadaPropertyRowBase.Frozen(color)
                : ScadaPropertyRowBase.FallbackSwatch;

        /// <summary>色块与弹窗绑的同一个状态（一个状态两个视图，不必再来一个"打开取色板"的命令）</summary>
        public bool IsColorPickerOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value) return;
                _isOpen = value;

                // 每次打开都重新起草：上一次关窗留下的草稿可能已被 Ctrl+Z 撤掉、或被画布改过，
                // 留着它就是第二份状态（与属性行的取色器同一口径）。
                if (value) ColorDraft = _read() ?? string.Empty;

                RaisePropertyChanged(nameof(IsColorPickerOpen));
            }
        }

        /// <summary>取色板的预设色（与属性行共用同一份冻结色板）</summary>
        public IReadOnlyList<SolidColorBrush> ColorPalette => ScadaPropertyRowBase.PaletteCells;

        /// <summary>
        /// 取色板里的草稿（#AARRGGBB）。草稿<b>不进模型</b>：一次提交 = 一条撤销记录，
        /// 而"拖一下透明度、连点几格看看"是取色时的常态，即时写回等于把 Ctrl+Z 废掉。
        /// </summary>
        public string ColorDraft
        {
            get => _draft;
            set
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, _draft, StringComparison.Ordinal)) return;

                _draft = text;
                RaisePropertyChanged(nameof(ColorDraft));
                RaisePropertyChanged(nameof(ColorDraftBrush));
                // 透明度滑块跟着十六进制框走：手打 "#80…" 时滑块也该跳到 128，
                // 否则两个控件各说各话（草稿串是唯一真相，滑块只是它的一个视图）。
                RaisePropertyChanged(nameof(ColorDraftAlpha));
            }
        }

        /// <summary>草稿的透明度（0..255）。取值读草稿串的 AA 段，赋值把它改写回同一个草稿串——不另存字段</summary>
        public double ColorDraftAlpha
        {
            get => ScadaPropertyRowBase.TryColor(_draft, out var color) ? color.A : 255d;
            set
            {
                if (!ScadaPropertyRowBase.TryColor(_draft, out var color))
                {
                    // 草稿现在不是个颜色（用户正打到 "#3B8" 这种半截串）。绝不把文本抹成默认色——
                    // 那等于当着他的面擦掉他打的东西——只把滑块弹回去。
                    RaisePropertyChanged(nameof(ColorDraftAlpha));
                    return;
                }

                byte alpha = (byte)Math.Max(0d, Math.Min(255d, Math.Round(value)));
                ColorDraft = ScadaPropertyRowBase.Format(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        /// <summary>草稿的预览色块（解析不出来时同样用灰）</summary>
        public Brush ColorDraftBrush
            => ScadaPropertyRowBase.TryColor(_draft, out var color)
                ? ScadaPropertyRowBase.Frozen(color)
                : ScadaPropertyRowBase.FallbackSwatch;

        /// <summary>点预设色格：<c>CommandParameter</c> 是 <see cref="Color"/></summary>
        public DelegateCommand<object> PickColorCommand { get; }

        /// <summary>「确定」：把草稿写回模型（这一步才产生撤销记录）</summary>
        public DelegateCommand ApplyColorCommand { get; }

        /// <summary>「取消」：丢掉草稿（草稿从没进过模型，所以没什么要还原的）</summary>
        public DelegateCommand CancelColorCommand { get; }

        /// <summary>
        /// 「清除」：把本槽的颜色从模型里抹掉。
        ///
        /// 为什么必须有它、而不是"把色块调成透明就行"：一档完全可以只配背景色不配前景色，
        /// 而"没配"和"配成了全透明"在运行态是两回事（前者不动图元原来的前景色，后者把它涂成看不见）。
        /// 属性行的颜色<b>不</b>给这个按钮（那些属性在描述符里有默认值，清空等于写个坏值进 .vms）。
        /// </summary>
        public DelegateCommand ClearCommand { get; }

        /// <summary>模型那边这个通道变了（Ctrl+Z / 别处改）→ 重播色块与「清除」的可用性</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(HasColor));
            RaisePropertyChanged(nameof(SwatchBrush));
            ClearCommand.RaiseCanExecuteChanged();
        }

        private void OnPickColor(object parameter)
        {
            if (parameter is not Color color) return;

            // 预设色都是不透明的，但保留用户已经调好的透明度：
            // "先调到 50% 再挨个色看看效果"是取色时最常见的用法，每换一格就弹回不透明
            // 等于每次都得重拖一遍滑块。
            byte alpha = ScadaPropertyRowBase.TryColor(_draft, out var current) ? current.A : (byte)255;
            ColorDraft = ScadaPropertyRowBase.Format(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private void OnApplyColor()
        {
            _write(_draft);
            IsColorPickerOpen = false;
        }

        private void OnCancelColor() => IsColorPickerOpen = false;

        private void OnClear()
        {
            _write(null);
            IsColorPickerOpen = false;
        }
    }

    /// <summary>
    /// 动画档位卡片的一档：「值/值域 → 一套外观」。
    ///
    /// 为什么要有这层包装，而不像动作表那样直接双向绑到模型对象（<see cref="ScadaAction"/>）：
    /// 一档上要挂<b>两个取色器的开合与草稿状态</b>，那是纯粹的界面状态，不该落进 .vms
    /// （模型里只该有选好的颜色，不该有"弹窗开着没有"）。包装层同时钉住
    /// "<b>按模型实例复用</b>"这条纪律（见 <see cref="ScadaAnimationRow.SyncStates"/>）：
    /// 每次回填都新建包装对象的话，用户在"值下限"里按 Tab 提交会触发一次面板回填，
    /// 输入框被重建、焦点丢掉——而 Tab 到下一格正是这里最常用的操作。
    ///
    /// <b>不订阅模型</b>：档位内容的每一次变更都会经 <see cref="ScadaAnimation.OnStatesChanged"/>
    /// → <see cref="ScadaElement"/> 的 <c>Animations</c> 变更 → 面板 <c>RefreshValue</c> 汇过来，
    /// 由 <see cref="Refresh"/> 统一重播。自己再挂一份订阅只会多一条要摘的链
    /// （面板每次重建行都会换一批包装对象，摘漏一条就是一处泄漏）。
    /// </summary>
    public sealed class ScadaAnimationStateViewModel : BindableBase
    {
        public ScadaAnimationStateViewModel(ScadaAnimationState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));

            // 两个颜色通道各占一格。写回统一走 BeginEdit：拖滑块/点色格改的是草稿，
            // 点「确定」那一下才写模型，于是"配一个颜色"永远只产生一条撤销记录。
            ForegroundSlot = new ScadaColorSlot(
                "前景",
                () => State.Foreground,
                text =>
                {
                    using (State.BeginEdit("档位前景色"))
                        State.Foreground = text;
                });

            FillSlot = new ScadaColorSlot(
                "背景",
                () => State.Fill,
                text =>
                {
                    using (State.BeginEdit("档位背景色"))
                        State.Fill = text;
                });
        }

        /// <summary>本卡片对应的模型档位</summary>
        public ScadaAnimationState State { get; }

        /// <summary>前景色那一格取色器</summary>
        public ScadaColorSlot ForegroundSlot { get; }

        /// <summary>背景色那一格取色器</summary>
        public ScadaColorSlot FillSlot { get; }

        /// <summary>
        /// 值域下端点。两端存<b>字符串</b>（见 <see cref="ScadaAnimationState"/> 的类注释）：
        /// 用户打到一半的 "1." 与空串都该原样留着，解析发生在求值那一刻。
        /// 两端填一样 = 单点值，不一样 = 范围——没有"模式"字段要跟匹配逻辑对表。
        /// </summary>
        public string ValueLow
        {
            get => State.ValueLow;
            set
            {
                using (State.BeginEdit("档位值域"))
                    State.ValueLow = value;
            }
        }

        /// <summary>值域上端点（闭区间；与下端点相同 = 单点值）</summary>
        public string ValueHigh
        {
            get => State.ValueHigh;
            set
            {
                using (State.BeginEdit("档位值域"))
                    State.ValueHigh = value;
            }
        }

        /// <summary>本档是否闪烁（运行态按统一节拍在"正常/变淡"之间切换）</summary>
        public bool IsFlashing
        {
            get => State.IsFlashing;
            set
            {
                if (State.IsFlashing == value) return;

                using (State.BeginEdit("档位闪烁"))
                    State.IsFlashing = value;
            }
        }

        /// <summary>值域的展示文本（单点值显示成一个数，范围显示成 "低 ~ 高"）</summary>
        public string RangeText => State.RangeText;

        /// <summary>本档配了点什么的一句话摘要（卡片上不展开颜色弹窗也能看懂）</summary>
        public string Detail => State.Detail;

        /// <summary>模型变了就重播一遍——包括两个取色槽的色块</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(ValueLow));
            RaisePropertyChanged(nameof(ValueHigh));
            RaisePropertyChanged(nameof(IsFlashing));
            RaisePropertyChanged(nameof(RangeText));
            RaisePropertyChanged(nameof(Detail));

            ForegroundSlot.Refresh();
            FillSlot.Refresh();
        }
    }

    /// <summary>
    /// 属性面板的一行<b>动画</b>（r4）：一个勾选框 = "这条动画配没配"，勾上之后在原地内联展开
    /// 驱动变量选择 + 该类型自己的参数区（外观变化 = 档位卡片表；水平/垂直移动 = 范围 + 结束位置；
    /// 可见性 = 范围 + 对象状态）。
    ///
    /// <b>两个勾各管一件事</b>：
    /// <list type="bullet">
    /// <item><b>外层</b>（本行的 <see cref="ScadaPropertyRowBase.BoolValue"/>）= 有没有配这条动画。
    /// 勾上 = <see cref="ScadaElement.GetOrAddAnimation"/>，取消 = <see cref="ScadaElement.RemoveAnimation"/>
    /// （连同配好的档位/范围一起丢）；</item>
    /// <item><b>内层</b>（展开区里那颗「启用」）= 跑不跑，绑 <see cref="ScadaAnimation.IsEnabled"/>。
    /// 停用只暂停运行、配置原样留着，与 <see cref="ScadaBinding.IsEnabled"/> 同一口径。</item>
    /// </list>
    /// 合成一个勾会逼用户二选一：要么"停不掉但配置留着"，要么"删了配置才能停"，两种都不对。
    ///
    /// 参数区直接双向绑模型对象（而不是"读成文本、写回时解析"）：与事件行的动作表同一个理由
    /// ——<see cref="ScadaAnimation"/> 本身就是有变更通知的模型对象，范围/结束位置/可见状态
    /// 都没有"文本形态"这回事。唯一例外是"值域"两端：模型上那两格刻意存字符串，所以卡片直接绑字符串。
    /// </summary>
    public sealed class ScadaAnimationRow : ScadaPropertyRowBase
    {
        /// <param name="element">本行配置动画的目标图元（动画只长在图元上，所以是具体类型而不是接口）</param>
        /// <param name="type">本行对应的动画类型（一行一种，四种各一行）</param>
        /// <param name="picker">「选变量」入口（弹窗怎么弹由这一层决定，见 <see cref="IScadaVariablePicker"/>）</param>
        public ScadaAnimationRow(ScadaElement element, ScadaAnimationType type, IScadaVariablePicker picker)
            : base(new ScadaAnimationSpec(type),
                   (element ?? throw new ArgumentNullException(nameof(element))).FindAnimation(type) != null
                       ? "True" : "False")
        {
            Element = element;
            Type = type;
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));

            AddStateCommand = new DelegateCommand(OnAddState);
            RemoveStateCommand = new DelegateCommand<ScadaAnimationStateViewModel>(OnRemoveState, v => v != null);
            MoveStateUpCommand = new DelegateCommand<ScadaAnimationStateViewModel>(v => OnMoveState(v, -1), v => CanMove(v, -1));
            MoveStateDownCommand = new DelegateCommand<ScadaAnimationStateViewModel>(v => OnMoveState(v, +1), v => CanMove(v, +1));
            PickVariableCommand = new DelegateCommand(OnPickVariable, () => Animation != null);
            ClearVariableCommand = new DelegateCommand(OnClearVariable, () => Animation is { HasVariable: true });
        }

        /// <summary>「选变量」入口</summary>
        private readonly IScadaVariablePicker _picker;

        /// <summary>本行配置动画的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>本行对应的动画类型</summary>
        public ScadaAnimationType Type { get; }

        /// <summary>复合编辑区（开关 + 参数区），面板据此整份换模板</summary>
        public override ScadaRowEditorKind EditorKind => ScadaRowEditorKind.Animation;

        /// <summary>
        /// 本行对应的那条动画；<b>没配时为 <c>null</c></b>——"没配这条动画"与"配了但把档位删光了"
        /// 是两回事，模板据此整块收起参数区（与事件行的 <c>Actions</c> 同一手法）。
        /// </summary>
        public ScadaAnimation? Animation => Element.FindAnimation(Type);

        /// <summary>这条动画配了没有（外层复选框的当前态，也是参数区的可见性开关）</summary>
        public bool HasAnimation => Animation != null;

        /// <summary>摘要（不展开也能看出这条动画在干什么）</summary>
        public string Detail => Animation?.Detail ?? string.Empty;

        /// <summary>本行显示哪一块参数区（类型是行的固有属性，不会变，所以不必通知）</summary>
        public bool IsAppearance => Type == ScadaAnimationType.Appearance;

        public bool IsHorizontalMove => Type == ScadaAnimationType.HorizontalMove;

        public bool IsVerticalMove => Type == ScadaAnimationType.VerticalMove;

        /// <summary>水平或垂直移动（两者的参数区形状相同，只差"结束位置"那个轴的标签）</summary>
        public bool IsMove => IsHorizontalMove || IsVerticalMove;

        public bool IsVisibility => Type == ScadaAnimationType.Visibility;

        // ---- 外观变化：档位卡片表 ----

        /// <summary>
        /// 档位卡片表。<b>包装对象按模型实例复用</b>（见 <see cref="SyncStates"/>），
        /// 所以回填不会把用户正在编辑的那一格重建掉。
        /// </summary>
        public ObservableCollection<ScadaAnimationStateViewModel> States { get; } = new();

        public DelegateCommand AddStateCommand { get; }

        public DelegateCommand<ScadaAnimationStateViewModel> RemoveStateCommand { get; }

        public DelegateCommand<ScadaAnimationStateViewModel> MoveStateUpCommand { get; }

        public DelegateCommand<ScadaAnimationStateViewModel> MoveStateDownCommand { get; }

        // ---- 共用：驱动变量 + 启用 ----

        /// <summary>「选变量」：把当前驱动变量交给选择器，选中后回填</summary>
        public DelegateCommand PickVariableCommand { get; }

        /// <summary>「清除」：断开驱动变量（动画本身与档位都留着）</summary>
        public DelegateCommand ClearVariableCommand { get; }

        /// <summary>驱动变量选没选（决定提示说"请先选变量"还是正常显示）</summary>
        public bool HasVariable => Animation?.HasVariable ?? false;

        /// <summary>驱动变量的展示名（没选时为 <c>null</c>）</summary>
        public string? VariableDisplayName => Animation?.VariableDisplayName;

        /// <summary>
        /// 这条动画启不启用（展开区里那颗「启用」）。
        /// 停用<b>不</b>动配置：不建表、不订阅、不刷值，与 <see cref="ScadaBinding.IsEnabled"/> 同一口径。
        /// </summary>
        public bool IsEnabled
        {
            get => Animation?.IsEnabled ?? false;
            set
            {
                if (Animation is not { } animation || animation.IsEnabled == value) return;

                using (animation.BeginEdit(value ? "启用动画" : "停用动画"))
                    animation.IsEnabled = value;
            }
        }

        // ---- 移动 / 可见性：范围与端点 ----

        /// <summary>范围下端点（手册里的"范围值1"）：变量值 ≤ 它 → 停在起始位置 / 不命中</summary>
        public string RangeLow
        {
            get => Animation?.RangeLow ?? string.Empty;
            set
            {
                if (Animation is not { } animation) return;

                using (animation.BeginEdit("动画范围下限"))
                    animation.RangeLow = value;
            }
        }

        /// <summary>范围上端点（手册里的"范围值2"）：变量值 ≥ 它 → 停在结束位置</summary>
        public string RangeHigh
        {
            get => Animation?.RangeHigh ?? string.Empty;
            set
            {
                if (Animation is not { } animation) return;

                using (animation.BeginEdit("动画范围上限"))
                    animation.RangeHigh = value;
            }
        }

        /// <summary>水平移动的结束位置 X（绝对坐标）。文本形态便于"打到一半"，解析失败就退回模型里的值</summary>
        public string EndXText
        {
            get => FormatPosition(Animation?.EndX);
            set => CommitPosition(value, (animation, parsed) => animation.EndX = parsed, nameof(EndXText));
        }

        /// <summary>垂直移动的结束位置 Y（绝对坐标）</summary>
        public string EndYText
        {
            get => FormatPosition(Animation?.EndY);
            set => CommitPosition(value, (animation, parsed) => animation.EndY = parsed, nameof(EndYText));
        }

        /// <summary>
        /// 移动动画的起始位置（<b>只读展示</b>）。手册 7.5.1.4 明写"起始位置不可编辑"——
        /// 它就是图元当前的 X/Y。面板把它显示出来，是为了让"范围 → 起止位置"这条对应关系看得见；
        /// 存一份可编辑的副本只会带来"用户挪了图元、动画还按老起点算"的劈叉。
        /// </summary>
        public string StartPositionText => Type switch
        {
            ScadaAnimationType.HorizontalMove => FormatPosition(Element.X),
            ScadaAnimationType.VerticalMove => FormatPosition(Element.Y),
            _ => string.Empty,
        };

        /// <summary>可见性动画里"值命中范围时"的对象状态：<c>true</c> = 显示，<c>false</c> = 隐藏</summary>
        public bool VisibleInRange
        {
            get => Animation?.VisibleInRange ?? true;
            set
            {
                if (Animation is not { } animation || animation.VisibleInRange == value) return;

                using (animation.BeginEdit("动画对象状态"))
                    animation.VisibleInRange = value;
            }
        }

        protected override string Read() => Element.FindAnimation(Type) != null ? "True" : "False";

        protected override bool Commit(string text)
        {
            if (!bool.TryParse(text, out var flag))
                return false;

            if (flag)
            {
                // 只有"没这条动画"这一种情况动手：已配过的原样保留（含用户把档位删光的那条）——
                // 与事件行同一口径，面板不该趁一次重复勾选悄悄重置别人的配置。
                if (Element.FindAnimation(Type) == null)
                    Element.GetOrAddAnimation(Type);
            }
            else
            {
                // 返回值不用看：读出来是 false 才可能走到这（UI 勾选框语义），没得删也无妨
                Element.RemoveAnimation(Type);
            }

            return true;
        }

        /// <summary>
        /// 基类只回读"配没配"这一个值；本行还要把整块参数区的对外状态一起通知出去
        /// （档位表与其中任意一档的属性变更，都会经动画 → 图元 → 面板汇到这条路上来）。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();

            SyncStates();

            RaisePropertyChanged(nameof(Animation));
            RaisePropertyChanged(nameof(HasAnimation));
            RaisePropertyChanged(nameof(Detail));
            RaisePropertyChanged(nameof(IsEnabled));
            RaisePropertyChanged(nameof(HasVariable));
            RaisePropertyChanged(nameof(VariableDisplayName));
            RaisePropertyChanged(nameof(RangeLow));
            RaisePropertyChanged(nameof(RangeHigh));
            RaisePropertyChanged(nameof(EndXText));
            RaisePropertyChanged(nameof(EndYText));
            RaisePropertyChanged(nameof(StartPositionText));
            RaisePropertyChanged(nameof(VisibleInRange));

            // 上/下/删的可用性取决于"这一档在表里的位置"，位置一变就得重算——
            // 否则第一档的"↑"按钮还亮着，点下去没反应。
            AddStateCommand.RaiseCanExecuteChanged();
            RemoveStateCommand.RaiseCanExecuteChanged();
            MoveStateUpCommand.RaiseCanExecuteChanged();
            MoveStateDownCommand.RaiseCanExecuteChanged();
            PickVariableCommand.RaiseCanExecuteChanged();
            ClearVariableCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 把 <see cref="States"/> 与模型里的档位表对齐，<b>按模型实例复用包装对象</b>。
        ///
        /// 为什么不能整体重建：面板每次回填（<see cref="RefreshValue"/>）都跑这一趟，
        /// 而回填的触发源包括"用户刚在某个输入框里按 Tab 提交"——整体重建会让输入框连同焦点一起消失，
        /// 于是 Tab 到下一格变成"敲了字、Tab、字还在、光标没了"。
        ///
        /// 对齐分三步，次序不能换：先删模型里已经没有的（从后往前，避免下标漂移），
        /// 再按模型次序把留下的挪到位（<c>Move</c> 只发一次 CollectionChanged，容器不重建），
        /// 最后补上新增的（插在当前下标处，插完即成序）。
        /// </summary>
        private void SyncStates()
        {
            var models = Animation?.States;

            if (models == null)
            {
                if (States.Count > 0) States.Clear();
                return;
            }

            for (int i = States.Count - 1; i >= 0; i--)
            {
                if (!models.Contains(States[i].State))
                    States.RemoveAt(i);
            }

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                var existing = FindView(model);

                if (existing == null)
                {
                    // 走到这里 States.Count 必然 ≥ i（前 i 个位置已被模型的前 i 档占住），
                    // 所以 Insert(i, …) 不会越界。
                    States.Insert(i, new ScadaAnimationStateViewModel(model));
                    continue;
                }

                int current = States.IndexOf(existing);
                if (current != i)
                    States.Move(current, i);

                existing.Refresh();
            }
        }

        private ScadaAnimationStateViewModel? FindView(ScadaAnimationState model)
        {
            foreach (var view in States)
            {
                if (ReferenceEquals(view.State, model))
                    return view;
            }

            return null;
        }

        private void OnAddState()
        {
            if (Animation is not { } animation) return;

            using (animation.BeginEdit("新增动画档位"))
            {
                // Detached：新档位在加入集合之前不该被记进撤销栈——记录它等于
                // "撤销一次先删掉一个还没进过画面的对象"，凭空多一步。
                animation.States.Add(ScadaChangeScope.Detached(() => new ScadaAnimationState()));
            }
        }

        private void OnRemoveState(ScadaAnimationStateViewModel? view)
        {
            if (Animation is not { } animation || view == null) return;

            // 删掉最后一档<b>不</b>顺手取消勾选：那是"配置"与"行为"两件事，
            // 用户可能只是先把旧档位清掉、紧接着要加新的。空档位表在运行态本来就等同没配，
            // 面板上用一行提示把这件事说明白，比替他做决定更诚实（与事件行删光动作同一口径）。
            using (animation.BeginEdit("删除动画档位"))
                animation.States.Remove(view.State);
        }

        private void OnMoveState(ScadaAnimationStateViewModel? view, int offset)
        {
            if (Animation is not { } animation || view == null) return;

            int index = animation.States.IndexOf(view.State);
            int target = index + offset;

            if (index < 0 || target < 0 || target >= animation.States.Count) return;

            // Move 而不是"删了再插"：Move 只发一次 CollectionChanged，订阅链上少一轮摘挂，
            // 也不会让被移的那一档在中间态里短暂地"不存在"。
            using (animation.BeginEdit("调整动画档位次序"))
                animation.States.Move(index, target);
        }

        private bool CanMove(ScadaAnimationStateViewModel? view, int offset)
        {
            if (Animation is not { } animation || view == null) return false;

            int index = animation.States.IndexOf(view.State);
            int target = index + offset;

            return index >= 0 && target >= 0 && target < animation.States.Count;
        }

        /// <summary>
        /// 「选变量」：把这条动画当前的驱动变量交给选择器，选中后回填。
        ///
        /// 回填走 <see cref="ScadaAnimation.BindVariable"/>（先 Id 再名字，自带作用域）：
        /// Id 是权威身份，名字只作显示与"找不到 Id 时的兜底寻址"。取消时回调<b>一次都不触发</b>——
        /// 弹窗的取消不该被翻译成"清空原绑定"这种破坏性动作。
        /// </summary>
        private void OnPickVariable()
        {
            if (Animation is not { } animation) return;

            _picker.Pick(animation.VariableId, animation.VariableName, (id, name) => animation.BindVariable(id, name));
        }

        private void OnClearVariable()
        {
            if (Animation is not { } animation) return;

            using (animation.BeginEdit("清除动画驱动变量"))
            {
                animation.VariableId = Guid.Empty;
                animation.VariableName = null;
            }
        }

        private static string FormatPosition(double? value)
            => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

        /// <summary>
        /// 写回一个坐标。打不出来的串（"12a"、空）原样退回模型里的值：本行是复合编辑区，
        /// 没有属性行那套"标红 + 悬停说明"的通道，退回比默默吃掉诚实——
        /// 用户会看到数字弹回去，知道这次没生效。
        /// </summary>
        private void CommitPosition(string? text, Action<ScadaAnimation, double> write, string propertyName)
        {
            if (Animation is not { } animation) return;

            if (!TryNumber(text ?? string.Empty, out var parsed))
            {
                RaisePropertyChanged(propertyName);
                return;
            }

            using (animation.BeginEdit("动画结束位置"))
                write(animation, parsed);
        }
    }

    /// <summary>
    /// 权限行的声明（S12）：<see cref="ScadaElement.RequiredRole"/> <b>不是</b>属性袋里的键，
    /// 描述符也声明不了它（那是强类型字段，理由见该属性的注释），所以它像事件行一样自带一份声明，
    /// 存在的唯一目的是顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （Kind/DisplayName/Description）进同一个面板模板，而不是为权限单开一套 UI。
    ///
    /// <see cref="Choices"/> 给空：候选<b>来自被编辑对象</b>（当前值可能是文件里的坏值），
    /// 由 <see cref="ScadaRolePropertyRow"/> 自己算——与基类 <c>Choices</c> 注释里
    /// 「所属图层」那条同源。声明是无状态的共享实例（与 <see cref="GeometryProperties.Standard"/> 同一手法）。
    /// </summary>
    internal sealed class ScadaRoleSpec : IPropertySpec
    {
        /// <summary>每种角色一份共享实例（本类无状态，读哪个图元由行实例钉住）</summary>
        public static readonly ScadaRoleSpec Instance = new ScadaRoleSpec();

        private ScadaRoleSpec() { }

        /// <summary>不落属性袋，只是一个标识（与事件行的 <c>Event.N</c> 同源）</summary>
        public string Key => "RequiredRole";

        public string DisplayName => "操作权限";

        public ElementPropertyKind Kind => ElementPropertyKind.Choice;

        public string Group => "权限";

        public string? Description =>
            "运行态操作这个图元所需的最低角色。选「不限制」= 谁都能操作；高角色自动包含低角色（管理员可以按工程师的按钮）。未登录时按「操作员」计";

        /// <summary>权限是"谁能操作"，不该被变量驱动——绑一个变量进来等于把权限交给运行数据</summary>
        public bool IsBindable => false;

        public double Min => double.NegativeInfinity;

        public double Max => double.PositiveInfinity;

        public IReadOnlyList<string> Choices => Array.Empty<string>();
    }

    /// <summary>
    /// 属性面板的一行<b>操作权限</b>（S12）：一个下拉框 = "运行态操作这个图元所需的最低角色"。
    ///
    /// 为什么不复用 <see cref="ScadaPropertyRow"/>：权限既不是几何键、也不在属性袋里，
    /// 走 <see cref="ElementValueAccess"/> 得先塞一个假键进去——那等于污染那条唯一通道。
    /// 与事件行、画面行同一取舍：读写逻辑各自一小段，值语义/标红/回填全部继承基类。
    ///
    /// 写回一律走 <see cref="ScadaPage.TrySetRequiredRole"/>（D3），本行自己不碰
    /// <see cref="ScadaElement.RequiredRole"/>：裸写虽然也会落一条记录，但那条记录没有操作名
    /// （撤销按钮上只显示"编辑"），而且 <c>ScadaWriteGuard.Strict</c> 下会被当场拒掉。
    ///
    /// 候选第一项是"不限制"（对应模型里的 <c>null</c>）。文件里若存着不认识的数值
    /// （高版本存、低版本读），那一项会被临时补进候选——否则 ComboBox 匹配不上任何一项、
    /// 显示成空白，用户看到的是"这一行莫名其妙是空的"，而不是"这里有个坏值"。
    /// </summary>
    public sealed class ScadaRolePropertyRow : ScadaPropertyRowBase
    {
        /// <summary>"不限制"在下拉里的文案（= 模型里的 <c>null</c>）</summary>
        public const string NotLimitedText = "不限制";

        /// <summary>
        /// 标准候选：不限制 + 三个已定义角色。
        /// 按枚举算而不是手抄三个字符串：枚举扩展时自动带上，且显示名与状态栏、审计文件同一个出处
        /// （<see cref="ScadaRoleExtensions.DisplayName"/>），三处不会各叫各的。
        /// </summary>
        private static readonly IReadOnlyList<string> StandardChoices =
            new[] { NotLimitedText }
                .Concat(Enum.GetValues<ScadaRole>().Where(r => r.IsDefined()).Select(r => r.DisplayName()))
                .ToArray();

        private readonly ScadaPage _page;
        private IReadOnlyList<string> _choices;

        /// <summary>上次算候选集时用的"当前值文案"（判断候选集要不要重算的唯一依据）</summary>
        private string _described;

        public ScadaRolePropertyRow(ScadaPage page, ScadaElement element)
            : base(ScadaRoleSpec.Instance,
                   Describe((element ?? throw new ArgumentNullException(nameof(element))).RequiredRole))
        {
            _page = page ?? throw new ArgumentNullException(nameof(page));
            Element = element;

            _described = Describe(element.RequiredRole);
            _choices = BuildChoices(_described);
        }

        /// <summary>本行写入的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>
        /// 候选文案（不限制 + 三档角色，坏值临时补在末尾）。
        ///
        /// <b>本行不必覆写 <see cref="ScadaPropertyRowBase.ChoiceOptions"/></b>：这些候选本身就是中文文案，
        /// 而 <see cref="ScadaChoiceNames.DisplayName"/> 认不出的值原样返回——"不限制"、"管理员"、
        /// 坏值"未知角色(9)"过一遍词汇表出来还是它们自己。翻译只对描述符里那些英文落盘值有意义。
        /// </summary>
        public override IReadOnlyList<string> Choices => _choices;

        protected override string Read() => Describe(Element.RequiredRole);

        /// <summary>
        /// 回读时连候选集一起对一遍：坏值进出名单会让候选多一项/少一项，
        /// 而基类只通知 <c>Value</c>，不通知候选——不补这一手，
        /// 撤销掉一个坏值之后下拉框里那条"未知角色(N)"会一直挂着。
        ///
        /// 通知的是 <see cref="ScadaPropertyRowBase.ChoiceOptions"/> 而不是本类的 <see cref="Choices"/>：
        /// 模板绑的是前者（下拉要"值 + 名字"两列），通知后者等于对着空气喊话。
        /// 后者没有缓存，每次读都从 <c>_choices</c> 现算，所以这一次通知之后下拉拿到的一定是新名单。
        ///
        /// 只在"当前值文案变了"时才重算并通知：本方法在图元属性变更时被逐行调用
        /// （见 <c>OnElementPropertyChanged</c>），拖动图元期间会连着响很多次，
        /// 无条件通知等于让下拉框在拖动过程中反复重建列表。权限跟几何无关，
        /// 拖一百次也不会变，这里必须能一眼判定"没事发生"。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();

            string current = Describe(Element.RequiredRole);
            if (string.Equals(current, _described, StringComparison.Ordinal)) return;

            _described = current;
            _choices = BuildChoices(current);
            RaisePropertyChanged(nameof(ChoiceOptions));
        }

        /// <summary>候选集 = 标准四项，当前值认不出来时把那一项临时补在末尾</summary>
        private static IReadOnlyList<string> BuildChoices(string current)
            => StandardChoices.Contains(current)
                ? StandardChoices
                : StandardChoices.Concat(new[] { current }).ToArray();

        protected override bool Commit(string text)
        {
            if (!TryParseRole(text, out var role))
            {
                // 空文案单独措辞：下拉被清空（当前值不在候选里时 WPF 会这么做）不是"用户选了不限制"，
                // 而是"这一行现在对不上任何一个选项"，得让人看见并就地重选。
                SetError(string.IsNullOrWhiteSpace(text)
                    ? "请从下拉里选一个角色（清空不等于「不限制」）"
                    : $"不认识的角色「{text}」");
                return false;
            }

            // D3：写模型只走画面上的统一入口（它内部罩 BeginEdit，撤销栈里是一条"设置操作权限"）
            if (!_page.TrySetRequiredRole(Element, role, out var error))
            {
                SetError(error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 模型值 → 下拉文案。<c>null</c> = 不限制；坏值回落成"未知角色(N)"，
        /// 与属性面板、日志里看到的是同一个词（见 <see cref="ScadaRoleExtensions.DisplayName"/>）。
        /// </summary>
        private static string Describe(ScadaRole? role)
            => role is { } value ? value.DisplayName() : NotLimitedText;

        /// <summary>
        /// 下拉文案 → 模型值。
        /// 只有<b>认得的文案</b>才落得下：<c>"不限制"</c> → <c>null</c>，三个角色名 → 对应枚举。
        ///
        /// 空文案<b>不算</b>「不限制」，这一点是刻意的。下拉不是可编辑控件，用户打不出空值；
        /// 它变空只有一种来路——当前值不在候选列表里，WPF 就把 SelectedItem 置成 null 并回写。
        /// 那时若把空当成"取消限制"，一次候选失配就会<b>静默把权限放大成谁都能操作</b>，
        /// 而现场只会看到"这台机器本来要工程师才能开，怎么操作员也能开了"。
        /// 所以认不出来一律返回 <c>false</c>，由 <see cref="Commit"/> 标红让人重选——
        /// 宁可他看见一条红提示，也不能替他做一次放权。
        /// </summary>
        private static bool TryParseRole(string? text, out ScadaRole? role)
        {
            role = null;

            if (string.Equals(text, NotLimitedText, StringComparison.Ordinal))
                return true;

            foreach (var candidate in Enum.GetValues<ScadaRole>())
            {
                if (!candidate.IsDefined()) continue;

                if (string.Equals(candidate.DisplayName(), text, StringComparison.Ordinal))
                {
                    role = candidate;
                    return true;
                }
            }

            return false;
        }
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
        private readonly IScadaPagePicker _pagePicker;

        /// <summary>摊平的行（模型有外部写入时逐行回读；分组视图从它派生）</summary>
        private readonly List<ScadaPropertyRowBase> _rows = new();

        private bool _subscribed;
        private ScadaElement? _element;
        private ScadaPage? _page;

        public ScadaPropertyViewModel(ScadaEditorViewModel editor, IUserNotifier notifier, IScadaVariablePicker picker, IScadaPagePicker pagePicker)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _pagePicker = pagePicker ?? throw new ArgumentNullException(nameof(pagePicker));

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
        /// 顶栏那个「锁定」复选框（作用于当前编辑的那一个图元）。
        ///
        /// 为什么不照原样直绑 <c>Element.IsLocked</c>：那是一条绕开
        /// <see cref="ScadaPage.TrySetElementLocked"/> 的裸 setter 写路径。S9 之后编辑器路径
        /// 一律要罩在 <c>BeginEdit</c> 里，裸写虽然也会落一条记录，但那条记录<b>没有操作名</b>，
        /// 撤销按钮上只会显示"编辑"；更要紧的是 <c>ScadaWriteGuard.Strict</c> 下它会被当场拒掉——
        /// 面板其余每一行（属性袋、事件、动作、绑定）都规规矩矩走 Try 家族，只有这一处漏着。
        ///
        /// 写入口借编辑器的 <see cref="ScadaEditorViewModel.SetSelectedLocked"/>，而不是
        /// 自己拼一个 <c>new[] { _element }</c>：面板编辑的图元恒等于编辑器的主选中
        /// （<see cref="Rebuild"/> 就是从 <c>SelectedElement</c> 读来的），
        /// 借它走一遍等于复用同一条已被断言钉住的路径，也省掉面板自己找"这个图元属于哪张画面"。
        /// </summary>
        public bool IsElementLocked
        {
            get => _element?.IsLocked == true;
            set
            {
                _editor.SetSelectedLocked(value);

                // 写失败时（图元已经不在画面上）模型值没变、也不会有模型通知，
                // 而绑定是 TwoWay 的——不在这里回抛一次，复选框就会停在用户刚点出来的那个假状态上。
                // 写成功时这条与 OnElementPropertyChanged 那条会重一次，重一次是无害的（值没变）。
                RaisePropertyChanged(nameof(IsElementLocked));
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

            // 锁定不进属性行（它不是描述符声明的属性，而是图元的编辑保护位），
            // 所以刷新它的通知要单独补一条：撤销/重做把 IsLocked 改回来时，
            // 顶栏那个复选框得跟着动，否则面板显示的状态与画布上"拖得动/拖不动"会对不上。
            RaisePropertyChanged(nameof(IsElementLocked));

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
                    _rows.Add(new ScadaEventRow(element, eventType, ScadaEventRow.ElementGroup, _picker, _pagePicker));

                // r4 动画行：四种动画各一行，与描述符无关——"图元会不会动"不是类型的表态，
                // 而是这一台设备上这个图元要不要跟变量联动，任何图元都可能需要。
                // 所以这里用 Enum.GetValues 而不是描述符清单：动画类型是领域层闭集（见 ScadaAnimationType），
                // 加一种动画 = 在枚举尾部追加一项，面板这一行不用改。
                foreach (var animationType in Enum.GetValues<ScadaAnimationType>())
                    _rows.Add(new ScadaAnimationRow(element, animationType, _picker));

                // S12 权限行：与事件行平行，但语义独立——事件是"我能干什么"，
                // 权限是"谁能操作我"，所以单起一段而不是塞进上面那个循环。
                //
                // 用 _editor.SelectedPage 而不是本方法开头的局部 page：图元模式下那个 page 恒为 null
                // （两种模式互斥，见上面的三元表达式），口径与 OnBindVariable 逐字一致。
                // 万一 SelectedPage 与 SelectedElement 不同源（理论上不会），
                // TrySetRequiredRole 会以"图元不属于本画面"拒掉并标红，不会写错画面。
                if (_editor.SelectedPage is { } owner)
                    _rows.Add(new ScadaRolePropertyRow(owner, element));
            }
            else if (page != null)
            {
                // 传文档而不是让行自己去找：能走到这一支说明 SelectedPage 非空，而它只可能来自
                // 当前文档（换方案时 OnDocumentChanged 会把它重对齐成新文档的第一页或 null），
                // 所以这里递给行的永远是"那个画面真正所属的方案"，不是无方案时的兜底空文档。
                var document = _editor.Document;

                foreach (var spec in ScadaPageProperties.All)
                    _rows.Add(new ScadaPagePropertyRow(page, document, spec));

                // 画面级事件行：与图元侧同一份实现（宿主收成 IScadaEventHost），差别只有清单来源与分组。
                // 只列 ScadaPageEvents 声明的那两条——清单里没有的事件不出行，因为"配了却永远不响"
                // 比"面板上没这个选项"糟得多。落在「运行」组，紧接「启动画面」之后：
                // 组的首现次序决定显示次序，而这一组的第一行是上面那条 StartupPage。
                foreach (var eventType in ScadaPageEvents.All)
                    _rows.Add(new ScadaEventRow(page, eventType, ScadaPageEvents.Group, _picker, _pagePicker));
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
