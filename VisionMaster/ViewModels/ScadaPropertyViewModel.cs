using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
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
    public sealed class ScadaPropertyGroup : BindableBase
    {
        private readonly bool _isSearching;
        private bool _isExpanded;
        private bool _hasError;
        private bool _hasWarning;
        private int _errorCount;
        private int _warningCount;

        public ScadaPropertyGroup(
            string name,
            IReadOnlyList<ScadaPropertyRowBase> rows,
            IReadOnlyList<ScadaPropertyRowBase> allRows,
            ScadaSeriesCard? seriesCard,
            int matchCount,
            int displayCount,
            bool isExpanded,
            bool isSearching)
        {
            Name = name;
            Rows = rows;
            AllRows = allRows;
            SeriesCard = seriesCard;
            MatchCount = matchCount;
            DisplayCount = displayCount;
            _isExpanded = isExpanded;
            _isSearching = isSearching;
            RefreshState();
        }

        /// <summary>分组标题</summary>
        public string Name { get; }

        /// <summary>该组下当前要显示的行（搜索时是过滤后的子集）</summary>
        public IReadOnlyList<ScadaPropertyRowBase> Rows { get; }

        /// <summary>该组下的全部行（搜索时也不过滤；组头错误点与曲线卡用它）</summary>
        public IReadOnlyList<ScadaPropertyRowBase> AllRows { get; }

        /// <summary>多曲线趋势「数据」组的曲线卡模型（其它组为 null）</summary>
        public ScadaSeriesCard? SeriesCard { get; }

        /// <summary>本组是否用曲线卡呈现（4 条曲线一行一条）</summary>
        public bool IsSeriesCard => SeriesCard != null;

        /// <summary>搜索命中行数（未搜索时等于 <see cref="DisplayCount"/>）</summary>
        public int MatchCount { get; }

        /// <summary>组头计数：普通组 = 全部行数；曲线卡组 = 4 条曲线 + 3 项设置 = 7</summary>
        public int DisplayCount { get; }

        /// <summary>组头计数文本（搜索时「命中/总数」）</summary>
        public string CountText => _isSearching ? $"{MatchCount}/{DisplayCount}" : DisplayCount.ToString(CultureInfo.InvariantCulture);

        /// <summary>组展开状态（Expander 双向绑定；搜索时命中组强制展开）</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>组内是否有校验错误（红点）</summary>
        public bool HasError => _hasError;

        /// <summary>组内是否有待完成项（琥珀点）</summary>
        public bool HasWarning => _hasWarning;

        /// <summary>琥珀点只在没有红点时显示（一组一个问题点，不叠两个）</summary>
        public bool ShowWarningDot => _hasWarning && !_hasError;

        /// <summary>错误/待完成项数（组头悬停说明用）</summary>
        public int ErrorCount => _errorCount;

        public int WarningCount => _warningCount;

        public string ErrorTooltip
            => _hasError ? $"{_errorCount} 项需要修正"
             : _hasWarning ? $"{_warningCount} 项待完成"
             : string.Empty;

        /// <summary>行状态变了之后重算组头聚合点（面板在逐行回读后调用）</summary>
        public void RefreshState()
        {
            int errors = 0;
            int warnings = 0;

            foreach (var row in AllRows)
            {
                if (row.HasError) errors++;
                else if (row.HasWarning) warnings++;
            }

            if (errors != _errorCount)
            {
                _errorCount = errors;
                RaisePropertyChanged(nameof(ErrorCount));
            }

            if (warnings != _warningCount)
            {
                _warningCount = warnings;
                RaisePropertyChanged(nameof(WarningCount));
            }

            bool hasError = errors > 0;
            bool hasWarning = warnings > 0;

            if (hasError != _hasError)
            {
                _hasError = hasError;
                RaisePropertyChanged(nameof(HasError));
            }

            if (hasWarning != _hasWarning)
            {
                _hasWarning = hasWarning;
                RaisePropertyChanged(nameof(HasWarning));
            }

            RaisePropertyChanged(nameof(ShowWarningDot));
            RaisePropertyChanged(nameof(ErrorTooltip));
        }
    }

    /// <summary>
    /// 多曲线趋势「数据」组的曲线卡模型（v2）：4 条曲线一行一条，
    /// 每条 = 颜色点 + 名称 + 值 + ƒx。行对象仍是普通的 <see cref="ScadaPropertyRow"/>，
    /// 这里只是把它们按曲线组织起来——落盘键、可绑语义、校验全部沿用原行。
    /// </summary>
    public sealed class ScadaSeriesCard
    {
        public ScadaSeriesCard(
            IReadOnlyList<ScadaSeriesItem> items,
            ScadaPropertyRowBase timeWindowRow,
            ScadaPropertyRowBase rangeModeRow,
            ScadaPropertyRowBase showLegendRow)
        {
            Items = items;
            TimeWindowRow = timeWindowRow;
            RangeModeRow = rangeModeRow;
            ShowLegendRow = showLegendRow;
        }

        public IReadOnlyList<ScadaSeriesItem> Items { get; }

        public ScadaPropertyRowBase TimeWindowRow { get; }

        public ScadaPropertyRowBase RangeModeRow { get; }

        public ScadaPropertyRowBase ShowLegendRow { get; }
    }

    /// <summary>曲线卡里的一条曲线：颜色 / 名称 / 值三行 + 序号标题</summary>
    public sealed class ScadaSeriesItem
    {
        public ScadaSeriesItem(string title, ScadaPropertyRowBase colorRow, ScadaPropertyRowBase nameRow, ScadaPropertyRowBase valueRow)
        {
            Title = title;
            ColorRow = colorRow;
            NameRow = nameRow;
            ValueRow = valueRow;
        }

        public string Title { get; }

        public ScadaPropertyRowBase ColorRow { get; }

        public ScadaPropertyRowBase NameRow { get; }

        public ScadaPropertyRowBase ValueRow { get; }
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
        /// <summary>
        /// 这一行只读（由描述符的 <c>ReadOnlyKeys</c> 声明）：显示但置灰，悬停提示"由系统计算"。
        /// 典型：管线的 X/Y/宽高/旋转——几何由路由器现算，改了会被下一拍写回。
        /// </summary>
        public bool IsReadOnly { get; set; }

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

        /// <summary>
        /// 所属分组。默认取声明；<b>可被面板覆盖</b>——多曲线趋势的 4 个曲线颜色在 v2 里
        /// 移入「数据」组卡（呈现层分组，落盘键与描述符声明都不动）。
        /// </summary>
        public string? GroupOverride { get; set; }

        public string Group => GroupOverride ?? Spec.Group;

        /// <summary>编辑器类型（面板据此挑模板）</summary>
        public ElementPropertyKind Kind => Spec.Kind;

        /// <summary>
        /// 数值行的单位后缀（<c>px</c> / <c>°</c>），空串 = 不显示（NumericBox 的 Suffix 见空即收）。
        ///
        /// 为什么按 Key 映射、而不是给 <see cref="IPropertySpec"/> 加字段：单位是"给人看的标注"，
        /// 不参与落盘语义，几十处声明各填一遍只会让契约变胖；而键名是稳定的——图元与画面共用
        /// 同一套名字（<c>$X/$Y/$Width/$Height/$Rotation</c> 与 <c>Width/Height/GridSize</c>），
        /// 一处白名单就覆盖两侧。将来真要做成声明字段，把这里改成读 Spec 即可，XAML 一行不用动。
        /// </summary>
        public string Unit => Key switch
        {
            "$X" or "$Y" or "$Width" or "$Height" => "px",
            "Width" or "Height" or "GridSize" => "px",
            "FontSize" or "StrokeThickness" or "CornerRadius" => "px",
            "BackgroundOpacity" => "%",
            "TimeWindow" => "s",
            "PopupOffsetX" or "PopupOffsetY" => "px",
            "$Rotation" => "°",
            _ => string.Empty,
        };

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
        /// 通知 <c>Choices</c> 没人听（见 <see cref="ScadaPermissionPropertyRow.RefreshValue"/>）。
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

        /// <summary>
        /// 文本类字段用上标签布局（标签在上、控件全宽）。可绑行仍走左标签：
        /// 它右侧要留 ƒx 槽与绑定 chip，上标签会把那一列挤没。
        /// </summary>
        public bool UseStackedLayout
            => (Kind == ElementPropertyKind.Text || Kind == ElementPropertyKind.MultilineText) && !IsBindable;

        /// <summary>本行是图片文件且已经选了图（决定空值占位与「清除」是否出现）</summary>
        public bool HasImage => Kind == ElementPropertyKind.ImageFile && !string.IsNullOrWhiteSpace(_value);

        /// <summary>本行有校验错误（组头红点聚合用）</summary>
        public virtual bool HasError => !IsValid;

        /// <summary>本行处于「待完成」状态（组头琥珀点聚合用；事件/动画行覆写）</summary>
        public virtual bool HasWarning => false;

        /// <summary>
        /// 成对网格：左行的右伙伴（<b>view-only</b>，不改变行账本——<c>Rows.Count</c> 与断言都不动）。
        /// 右行仍留在 <c>_rows</c> 里，只是由左行的模板渲染、自己的行模板收起。
        /// </summary>
        public ScadaPropertyRowBase? PairWith { get; set; }

        /// <summary>成对网格：右行指向左行（搜索时任一命中要把伙伴一起带出来）</summary>
        public ScadaPropertyRowBase? PairOwner { get; set; }

        /// <summary>成对网格：本行是右伙伴，自己的行模板收起</summary>
        public bool IsPairedFollow { get; set; }

        /// <summary>本行是否带右伙伴</summary>
        public bool HasPair => PairWith != null;

        /// <summary>
        /// 搜索匹配：属性名 / 键名 / 分组名，大小写不敏感。
        /// 复合行可覆写以匹配子行（曲线卡、成对行）。
        /// </summary>
        public virtual bool MatchesSearch(string query)
            => DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Key.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Group.Contains(query, StringComparison.OrdinalIgnoreCase);

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
                    // 只读行没有"让用户就地改"这回事：它连输入框都没有，缓冲区里多出一个
                    // 模型里并不存在的值，只会让面板显示一个假事实。直接按模型回填。
                    if (Kind == ElementPropertyKind.ReadOnly)
                    {
                        RefreshValue();
                        return;
                    }

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
            RaisePropertyChanged(nameof(HasImage));
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
    public class ScadaPropertyRow : ScadaPropertyRowBase
    {
        private readonly Func<string?>? _solutionPath;

        public ScadaPropertyRow(
            ScadaElement element,
            ElementPropertyDescriptor descriptor,
            Func<string?>? solutionPath = null)
            : base(descriptor ?? throw new ArgumentNullException(nameof(descriptor)),
                   ElementValueAccess.Read(element ?? throw new ArgumentNullException(nameof(element)), descriptor))
        {
            Element = element;
            Descriptor = descriptor;
            _solutionPath = solutionPath;

            PickImageCommand = new DelegateCommand(OnPickImage, () => IsImageFile);
            ClearImageCommand = new DelegateCommand(OnClearImage, () => IsImageFile && !string.IsNullOrWhiteSpace(Value));
        }

        /// <summary>本行写入的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>本行对应的属性声明（面板之外的人按它判断，如命令的 CanExecute）</summary>
        public ElementPropertyDescriptor Descriptor { get; }

        /// <summary>这一行是不是“方案内图片文件”（Hmi.Picture.SourcePath）</summary>
        public bool IsImageFile => Kind == ElementPropertyKind.ImageFile;

        /// <summary>选择图片：复制进方案 Image\，把相对路径写回属性（走基类提交，进撤销栈）</summary>
        public DelegateCommand PickImageCommand { get; }

        /// <summary>清除图片（只清引用，不删文件）</summary>
        public DelegateCommand ClearImageCommand { get; }

        /// <summary>图片文件名摘要（属性面板那一行显示用）</summary>
        public string ImageSummary => IsImageFile ? ScadaImageFileRowSupport.Summary(Value) : string.Empty;

        /// <summary>图片行上屏文本：空值给「未选择」占位（旧版空值只剩两个点，看着像坏行）</summary>
        public string ImageSummaryText => IsImageFile ? (HasImage ? ImageSummary : "未选择") : string.Empty;

        /// <summary>缩略图（缺失/解码失败时为 null）</summary>
        public ImageSource? ImagePreview => IsImageFile ? ScadaImageFileRowSupport.Preview(_solutionPath, Value) : null;

        /// <summary>是不是几何键（不落属性袋，直接改模型字段）</summary>
        public bool IsGeometry => Descriptor.IsGeometry;

        /// <summary>
        /// 本行属性当前有没有绑工程变量——<b>直接问图元，不另存一份状态</b>
        /// （与类注释的口径 2 一致：面板永远是模型的一个视图）。
        /// </summary>
        public override bool HasBinding => Element.FindBinding(Key) != null;

        /// <summary>已绑变量的名字（只作展示；寻址一律用 Id，名字随时可能过期）</summary>
        public override string? BindingVariableName => Element.FindBinding(Key)?.ToReference().DisplayText;

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

            if (!IsImageFile)
                return;

            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImageSummaryText));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }

        private void OnPickImage()
        {
            if (!ScadaImageFileRowSupport.TryPick(_solutionPath, out var relativePath, out var error))
            {
                // 取消：error 为空，什么都不做；失败：把原因写进行内红字（不弹窗）
                if (!string.IsNullOrEmpty(error))
                    SetError(error);
                return;
            }

            SetError(null);
            Value = relativePath; // 基类提交：写模型 + 撤销栈 + 回填规范值
            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }

        private void OnClearImage()
        {
            SetError(null);
            Value = string.Empty;
            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 轴面板「候选轴」行（Kind = <see cref="ElementPropertyKind.AxisList"/>）。
    ///
    /// 值与普通属性一样落在属性袋里（<see cref="ScadaAxisOptionsCodec"/> 的规范 JSON），
    /// 但编辑入口不是文本框，而是"摘要 + 选择轴…"：
    /// 轴名是工程里的一组身份，手打既容易打错、也没法带 Id。
    ///
    /// 它<b>不是数据绑定</b>（IsBindable=false）：轴候选是设计期配置，
    /// 运行期由面板自己的下拉框切换——所以这一行不挂 ƒx，也不进 ScadaRuntimeBinder。
    /// </summary>
    public sealed class ScadaAxisListRow : ScadaPropertyRow
    {
        private readonly Action<ScadaAxisListRow> _pick;

        /// <summary>按 Id 取最新轴名；null = 轴已删除（回退到落盘名并标注「已移除」）</summary>
        private readonly Func<Guid, string?> _resolveName;

        public ScadaAxisListRow(ScadaElement element, ElementPropertyDescriptor descriptor,
                                Action<ScadaAxisListRow> pick, Func<Guid, string?> resolveName)
            : base(element, descriptor)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            _resolveName = resolveName ?? throw new ArgumentNullException(nameof(resolveName));
            PickAxesCommand = new DelegateCommand(() => _pick(this));
        }

        /// <summary>「选择轴…」：打开多选弹窗，确认后由面板写回模型</summary>
        public DelegateCommand PickAxesCommand { get; }

        /// <summary>
        /// 属性行上显示的摘要（未选 / X、Y、Z / X 等 N 根）。
        ///
        /// 名字<b>不读</b>属性袋里那份 JSON：它是"选轴那一刻"的快照，轴改名后必然过期。
        /// 先按 Id 问注册表要最新名；查不到（轴被删）才回退到落盘名并标注「已移除」——
        /// 这样用户一眼能区分"名字改了"与"轴没了"。
        /// </summary>
        public string Summary
        {
            get
            {
                var axes = ScadaAxisOptionsCodec.Parse(Value);
                if (axes.Count == 0)
                    return "未选择轴";

                string names = string.Join("、", axes.Select(DisplayNameOf));
                return axes.Count <= 3 ? names : $"{names} 等 {axes.Count} 根";
            }
        }

        private string DisplayNameOf(ScadaAxisRef axis)
        {
            var live = _resolveName(axis.AxisId);
            if (!string.IsNullOrWhiteSpace(live))
                return live!;

            return string.IsNullOrWhiteSpace(axis.Name) ? "（已移除）" : $"{axis.Name}（已移除）";
        }

        /// <summary>轴注册表变化后重算摘要（名字变了，但属性袋里那串 JSON 没变）</summary>
        internal void RefreshSummary() => RaisePropertyChanged(nameof(Summary));

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// 数据表格图元的「列定义」行（Kind = <see cref="ElementPropertyKind.ColumnList"/>）。
    ///
    /// 与 <see cref="ScadaAxisListRow"/> 逐字同构：行上只放摘要，点「选择列…」进弹窗。
    /// 为什么不做成多行文本框：一条 JSON 里每列 6 个字段，手写必然写错；
    /// 而错了的表现是"运行期整列显示 —"，离配置现场很远。
    /// </summary>
    public sealed class ScadaGridColumnsRow : ScadaPropertyRow
    {
        private readonly Action<ScadaGridColumnsRow> _pick;

        public ScadaGridColumnsRow(ScadaElement element, ElementPropertyDescriptor descriptor,
                                   Action<ScadaGridColumnsRow> pick)
            : base(element, descriptor)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            PickColumnsCommand = new DelegateCommand(() => _pick(this));
        }

        /// <summary>「选择列…」：打开列配置弹窗，确认后由面板写回</summary>
        public DelegateCommand PickColumnsCommand { get; }

        /// <summary>行上显示的摘要（未配置 / N 列 · 表头…）</summary>
        public string Summary
        {
            get
            {
                var columns = ScadaGridColumnsCodec.Parse(Value);
                if (columns.Count == 0)
                    return "未配置";

                var names = string.Join(" / ", columns.Select(c => c.Header));
                return columns.Count <= 3
                    ? $"{columns.Count} 列 · {names}"
                    : $"{columns.Count} 列 · {names}…";
            }
        }

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// 报表列定义的一行（Kind = <see cref="ElementPropertyKind.ReportColumnList"/>）。
    ///
    /// 与 <see cref="ScadaGridColumnsRow"/> 同形（摘要 + 「选择列…」），所以视图侧
    /// 复用同一个 DataTemplate——但打开的是报表列编辑器：报表列绑的是<b>账本列名</b>，
    /// 还多一个聚合方式，跟"绑哪个变量"不是一回事。
    /// </summary>
    public sealed class ScadaReportColumnsRow : ScadaPropertyRow
    {
        private readonly Action<ScadaReportColumnsRow> _pick;

        public ScadaReportColumnsRow(ScadaElement element, ElementPropertyDescriptor descriptor,
                                     Action<ScadaReportColumnsRow> pick)
            : base(element, descriptor)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            PickColumnsCommand = new DelegateCommand(() => _pick(this));
        }

        /// <summary>「选择列…」：打开报表列配置弹窗，确认后由面板写回</summary>
        public DelegateCommand PickColumnsCommand { get; }

        /// <summary>行上显示的摘要（未配置 / N 列 · 名称…）</summary>
        public string Summary
        {
            get
            {
                var columns = ScadaReportCodec.Parse(Value);
                if (columns.Count == 0)
                    return "未配置";

                var names = string.Join(" / ", columns.Select(c => c.Title));
                return columns.Count <= 3
                    ? $"{columns.Count} 列 · {names}"
                    : $"{columns.Count} 列 · {names}…";
            }
        }

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// 柱状图 / 饼图的「数据项」行：摘要 + 「配置数据项…」。
    ///
    /// 为什么按<b>属性键</b>认它、而不是加一个新的 <c>ElementPropertyKind</c>：
    /// 图表项与报表列<b>同形但不同义</b>（绑工程变量 vs 绑账本列名），加枚举值要同时改
    /// 生成器、属性面板与所有 switch；按键认只在面板这一处落地，
    /// 而 <c>BarSeries</c> / <c>PieSlices</c> 本来就是这两个图元的稳定标识。
    /// </summary>
    public sealed class ScadaChartSeriesRow : ScadaPropertyRow
    {
        private readonly Action<ScadaChartSeriesRow> _pick;

        public ScadaChartSeriesRow(ScadaElement element, ElementPropertyDescriptor descriptor,
                                   Action<ScadaChartSeriesRow> pick)
            : base(element, descriptor)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            PickSeriesCommand = new DelegateCommand(() => _pick(this));
        }

        /// <summary>「配置数据项…」：打开数据项编辑器，确认后由面板写回</summary>
        public DelegateCommand PickSeriesCommand { get; }

        /// <summary>行上显示的摘要（未配置 / N 项 · 名称…）</summary>
        public string Summary
        {
            get
            {
                var items = ScadaChartCodec.Parse(Value);
                if (items.Count == 0)
                    return "未配置";

                var names = string.Join(" / ", items.Select(i => i.Label));
                return items.Count <= 3
                    ? $"{items.Count} 项 · {names}"
                    : $"{items.Count} 项 · {names}…";
            }
        }

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// 方案级文本列表的一行（Kind = <see cref="ElementPropertyKind.StateList"/>）。
    ///
    /// 行上是一个列表选择器 + 「管理…」：候选来自方案文档，不是手打；
    /// 值为空 = 不使用列表（ComboBox 继续走旧的 Options）。
    /// </summary>
    public sealed class ScadaStateListRow : ScadaPropertyRow
    {
        private readonly Func<IReadOnlyList<ScadaStateList>> _lists;
        private readonly Action<ScadaStateListRow> _manage;

        public ScadaStateListRow(
            ScadaElement element,
            ElementPropertyDescriptor descriptor,
            Func<IReadOnlyList<ScadaStateList>> lists,
            Action<ScadaStateListRow> manage)
            : base(element, descriptor)
        {
            _lists = lists ?? throw new ArgumentNullException(nameof(lists));
            _manage = manage ?? throw new ArgumentNullException(nameof(manage));
            ManageCommand = new DelegateCommand(() => _manage(this));
        }

        /// <summary>「管理…」：打开文本列表管理器</summary>
        public DelegateCommand ManageCommand { get; }

        /// <summary>选择器候选；第一项是“不使用文本列表”</summary>
        public IReadOnlyList<ScadaStateListOption> Options
        {
            get
            {
                var result = new List<ScadaStateListOption>
                {
                    new(string.Empty, "（不使用文本列表）"),
                };

                var current = Value;
                var matched = false;

                foreach (var list in _lists())
                {
                    var id = list.StateListId.ToString("D");
                    result.Add(new ScadaStateListOption(id, list.Name));
                    if (string.Equals(id, current, StringComparison.OrdinalIgnoreCase))
                        matched = true;
                }

                // 当前 Id 已不在文档里（列表被删）：给一个占位项，别让下拉框显示空白。
                if (!string.IsNullOrWhiteSpace(current) && !matched)
                    result.Add(new ScadaStateListOption(current, "（已删除的文本列表）"));

                return result;
            }
        }

        /// <summary>行摘要（测试/无障碍用）</summary>
        public string Summary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Value))
                    return "未选择";

                foreach (var option in Options)
                {
                    if (string.Equals(option.Value, Value, StringComparison.OrdinalIgnoreCase))
                        return option.Name;
                }

                return "（已删除的文本列表）";
            }
        }

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Options));
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>文本列表选择器的一项：Value 是 Guid 字符串，Name 是给人看的名</summary>
    public sealed class ScadaStateListOption
    {
        public ScadaStateListOption(string value, string name)
        {
            Value = value ?? string.Empty;
            Name = name ?? string.Empty;
        }

        public string Value { get; }

        public string Name { get; }
    }

    /// <summary>
    /// 方案内画面的一行（Kind = <see cref="ElementPropertyKind.Page"/>）。
    /// 候选只列可导航页（排除弹窗页与基座），当前值已删除时给占位项，不静默变成“未选择”。
    /// </summary>
    public sealed class ScadaPageRow : ScadaPropertyRow
    {
        private readonly Func<ScadaDocument> _document;

        public ScadaPageRow(ScadaElement element, ElementPropertyDescriptor descriptor, Func<ScadaDocument> document)
            : base(element, descriptor)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public IReadOnlyList<ScadaPageOption> Options
        {
            get
            {
                var result = new List<ScadaPageOption>
                {
                    new(string.Empty, "（未选择画面）"),
                };

                var document = _document();
                var current = Value;
                var matched = false;

                foreach (var page in document.Pages)
                {
                    if (page.IsPopup || document.IsBasePage(page))
                        continue;

                    var id = page.PageId.ToString("D");
                    result.Add(new ScadaPageOption(id, page.Name));
                    if (string.Equals(id, current, StringComparison.OrdinalIgnoreCase))
                        matched = true;
                }

                if (!string.IsNullOrWhiteSpace(current) && !matched)
                    result.Add(new ScadaPageOption(current, "（已删除的画面）"));

                return result;
            }
        }

        public string Summary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Value))
                    return "未选择";

                foreach (var option in Options)
                {
                    if (string.Equals(option.Value, Value, StringComparison.OrdinalIgnoreCase))
                        return option.Name;
                }

                return "（已删除的画面）";
            }
        }

        public override void RefreshValue()
        {
            base.RefreshValue();
            RaisePropertyChanged(nameof(Options));
            RaisePropertyChanged(nameof(Summary));
        }
    }

    /// <summary>画面选择器的一项：Value 是 PageId 字符串，Name 是画面名</summary>
    public sealed class ScadaPageOption
    {
        public ScadaPageOption(string value, string name)
        {
            Value = value ?? string.Empty;
            Name = name ?? string.Empty;
        }

        public string Value { get; }

        public string Name { get; }
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
        private readonly Func<string?>? _solutionPath;
        private readonly Func<IReadOnlyList<ScadaPermissionInfo>>? _permissionCatalog;

        private string _describedPermission;
        private IReadOnlyList<string> _permissionChoices = Array.Empty<string>();
        private IReadOnlyList<ScadaChoiceOption> _permissionOptions = Array.Empty<ScadaChoiceOption>();

        public ScadaPagePropertyRow(
            ScadaPage page,
            ScadaDocument document,
            ScadaPagePropertySpec spec,
            Func<string?>? solutionPath = null,
            Func<IReadOnlyList<ScadaPermissionInfo>>? permissionCatalog = null)
            : base(spec ?? throw new ArgumentNullException(nameof(spec)),
                   spec.Read(page ?? throw new ArgumentNullException(nameof(page)),
                             document ?? throw new ArgumentNullException(nameof(document))))
        {
            Page = page;
            Document = document;
            Spec = spec;
            _solutionPath = solutionPath;
            _permissionCatalog = permissionCatalog;

            if (IsPermission)
            {
                _describedPermission = ScadaPermissionChoices.Describe(Value);
                (_permissionChoices, _permissionOptions) =
                    ScadaPermissionChoices.Build(_describedPermission, _permissionCatalog);
            }

            PickImageCommand = new DelegateCommand(OnPickImage, () => IsImageFile);
            ClearImageCommand = new DelegateCommand(OnClearImage, () => IsImageFile && !string.IsNullOrWhiteSpace(Value));
        }

        /// <summary>本行写入的目标画面</summary>
        public ScadaPage Page { get; }

        /// <summary>目标画面所属的方案（声明里的委托拿它写方案级的值）</summary>
        public ScadaDocument Document { get; }

        /// <summary>本行对应的画面属性声明（把基类的 <c>IPropertySpec</c> 收窄成画面声明，省掉每次读写的强转）</summary>
        public new ScadaPagePropertySpec Spec { get; }

        /// <summary>这一行是不是“方案内图片文件”（背景图）</summary>
        public bool IsImageFile => Spec.Kind == ElementPropertyKind.ImageFile;

        /// <summary>这一行是不是页面级「所需权限」行（候选来自权限目录，不是静态声明）</summary>
        public bool IsPermission => string.Equals(Spec.Key, "RequiredPermission", StringComparison.Ordinal);

        /// <summary>候选的落盘值：权限行是 <c>__none__</c> + 权限 Key；其余行转手声明</summary>
        public override IReadOnlyList<string> Choices => IsPermission ? _permissionChoices : base.Choices;

        /// <summary>下拉实际绑定的一份（值 + 目录里的显示名）</summary>
        public override IReadOnlyList<ScadaChoiceOption> ChoiceOptions => IsPermission ? _permissionOptions : base.ChoiceOptions;

        /// <summary>选择图片：复制进方案 Image\，把相对路径写回属性（走基类提交，进撤销栈）</summary>
        public DelegateCommand PickImageCommand { get; }

        /// <summary>清除图片（只清引用，不删文件——文件可能被别的页面共用）</summary>
        public DelegateCommand ClearImageCommand { get; }

        /// <summary>图片文件名摘要（属性面板那一行显示用；不暴露完整路径）</summary>
        public string ImageSummary => IsImageFile ? ScadaImageFileRowSupport.Summary(Value) : string.Empty;

        /// <summary>图片行上屏文本：空值给「未选择」占位</summary>
        public string ImageSummaryText => IsImageFile ? (HasImage ? ImageSummary : "未选择") : string.Empty;

        /// <summary>缩略图（缺失/解码失败时为 null，模板回退占位）</summary>
        public ImageSource? ImagePreview => IsImageFile ? ScadaImageFileRowSupport.Preview(_solutionPath, Value) : null;

        public override void RefreshValue()
        {
            base.RefreshValue();

            if (IsPermission)
            {
                string current = ScadaPermissionChoices.Describe(Value);
                if (!string.Equals(current, _describedPermission, StringComparison.Ordinal))
                {
                    _describedPermission = current;
                    (_permissionChoices, _permissionOptions) =
                        ScadaPermissionChoices.Build(current, _permissionCatalog);
                    RaisePropertyChanged(nameof(Choices));
                    RaisePropertyChanged(nameof(ChoiceOptions));
                }
            }

            if (!IsImageFile)
                return;

            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImageSummaryText));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 权限目录变了：只重算候选，不动当前值。当前值若是刚被删掉的 Key，
        /// 候选里会把它作为"未知权限"临时补在末尾（与图元行同一口径）。
        /// </summary>
        public void RefreshChoices()
        {
            if (!IsPermission) return;

            _describedPermission = ScadaPermissionChoices.Describe(Value);
            (_permissionChoices, _permissionOptions) =
                ScadaPermissionChoices.Build(_describedPermission, _permissionCatalog);
            RaisePropertyChanged(nameof(Choices));
            RaisePropertyChanged(nameof(ChoiceOptions));
        }

        private void OnPickImage()
        {
            if (!ScadaImageFileRowSupport.TryPick(_solutionPath, out var relativePath, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                    SetError(error);
                return;
            }

            SetError(null);
            Value = relativePath; // 基类提交：写模型 + 撤销栈 + 回填规范值
            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }

        private void OnClearImage()
        {
            SetError(null);
            Value = string.Empty;
            RaisePropertyChanged(nameof(ImageSummary));
            RaisePropertyChanged(nameof(ImagePreview));
            ClearImageCommand.RaiseCanExecuteChanged();
        }

        protected override string Read() => Spec.Read(Page, Document);

        protected override bool Commit(string text)
        {
            // 页面级权限：候选与写回都走权限目录 / TrySetRequiredPermission，
            // 不能落到下面的 Spec.Write——那里只做裸赋值，没有"旧角色清空 + 撤销操作名"。
            if (IsPermission)
            {
                if (!TryParsePermission(text, out var permissionKey))
                {
                    SetError(string.IsNullOrWhiteSpace(text)
                        ? "请从下拉里选一个权限（清空不等于「不限制」）"
                        : $"不认识的权限「{text}」");
                    return false;
                }

                if (!Page.TrySetRequiredPermission(permissionKey, out var permissionError))
                {
                    SetError(permissionError);
                    return false;
                }

                return true;
            }

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

        /// <summary>
        /// 下拉值 → 权限 Key（页面级）。与图元行同一口径：<c>__none__</c> → <c>null</c>，
        /// 只有当前候选里认得的值才落得下；空文案不算「不限制」（候选失配时不能静默放权）。
        /// </summary>
        private bool TryParsePermission(string? text, out string? permissionKey)
        {
            permissionKey = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (string.Equals(text, ScadaPermissionKeys.NotLimitedValue, StringComparison.Ordinal))
                return true;

            if (!_permissionChoices.Contains(text, StringComparer.Ordinal))
                return false;

            permissionKey = text;
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
        public const string ElementGroup = ScadaPropertyGroups.Event;

        /// <param name="host">本行勾选的目标宿主：图元或画面，见 <see cref="IScadaEventHost"/></param>
        /// <param name="group">本行落在哪一组（图元事件用 <see cref="ElementGroup"/>，画面事件用 <see cref="ScadaPageEvents.Group"/>）</param>
        public ScadaEventRow(IScadaEventHost host, ScadaEventType eventType, string group, IScadaVariablePicker picker, IScadaPagePicker pagePicker, IScadaFlowPicker flowPicker, IScadaAxisPicker? axisPicker = null, IScadaRecipePicker? recipePicker = null)
            : base(new ScadaEventSpec(eventType, group),
                   (host ?? throw new ArgumentNullException(nameof(host))).FindEventHook(eventType) != null
                       ? "True" : "False")
        {
            // 显示名与悬停说明沿用声明里的那一份：面板上勾的那一行和运行日志里出现的那一行
            // 必须对得上号，否则用户对不上"我配的"和"响的"。
            Editor = new ScadaEventEditorViewModel(host, eventType, DisplayName, Description, picker, pagePicker, flowPicker, axisPicker, recipePicker);

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

        /// <summary>组头聚合：配了事件却没有动作 = 待完成</summary>
        public override bool HasWarning => Editor.HasWarning;

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
        public const string GroupName = ScadaPropertyGroups.Animation;

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

        private bool _isExpanded = true;

        /// <summary>展开区开合（与「配没配」解耦：勾选管存在，箭头管展开）</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                    RaisePropertyChanged(nameof(IsBodyVisible));
            }
        }

        /// <summary>参数区是否可见（配了 + 展开）</summary>
        public bool IsBodyVisible => HasAnimation && IsExpanded;

        /// <summary>折叠头摘要：有变量给 Detail，没变量给「未选变量」（琥珀）</summary>
        public string Summary
            => !HasAnimation ? string.Empty
             : !HasVariable ? "未选变量"
             : string.IsNullOrWhiteSpace(Detail) ? "已配置" : Detail;

        /// <summary>摘要是否用琥珀色（配了但没选驱动变量，运行态会跳过）</summary>
        public bool SummaryIsWarning => HasAnimation && !HasVariable;

        /// <summary>组头聚合：配了动画却没选驱动变量 = 待完成</summary>
        public override bool HasWarning => HasAnimation && !HasVariable;

        /// <summary>本行显示哪一块参数区（类型是行的固有属性，不会变，所以不必通知）</summary>
        public bool IsAppearance => Type == ScadaAnimationType.Appearance;

        public bool IsHorizontalMove => Type == ScadaAnimationType.HorizontalMove;

        public bool IsVerticalMove => Type == ScadaAnimationType.VerticalMove;

        /// <summary>水平或垂直移动（两者的参数区形状相同，只差"结束位置"那个轴的标签）</summary>
        public bool IsMove => IsHorizontalMove || IsVerticalMove;

        public bool IsVisibility => Type == ScadaAnimationType.Visibility;

        /// <summary>启用对象动画的参数区</summary>
        public bool IsEnable => Type == ScadaAnimationType.Enable;

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

        /// <summary>启用对象：反转语义（true = 1 禁用、0 启用）</summary>
        public bool EnableInvert
        {
            get => Animation?.Invert ?? false;
            set
            {
                if (Animation is not { } animation || animation.Invert == value) return;

                using (animation.BeginEdit("动画反转"))
                    animation.Invert = value;
            }
        }

        /// <summary>启用对象：禁用时给操作员看的原因</summary>
        public string DisableReason
        {
            get => Animation?.DisableReason ?? "当前不可用";
            set
            {
                if (Animation is not { } animation || animation.DisableReason == (value ?? string.Empty)) return;

                using (animation.BeginEdit("动画禁用原因"))
                    animation.DisableReason = value ?? string.Empty;
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
            RaisePropertyChanged(nameof(IsBodyVisible));
            RaisePropertyChanged(nameof(Summary));
            RaisePropertyChanged(nameof(SummaryIsWarning));
            RaisePropertyChanged(nameof(IsEnabled));
            RaisePropertyChanged(nameof(HasVariable));
            RaisePropertyChanged(nameof(VariableDisplayName));
            RaisePropertyChanged(nameof(RangeLow));
            RaisePropertyChanged(nameof(RangeHigh));
            RaisePropertyChanged(nameof(EndXText));
            RaisePropertyChanged(nameof(EndYText));
            RaisePropertyChanged(nameof(StartPositionText));
            RaisePropertyChanged(nameof(VisibleInRange));
            RaisePropertyChanged(nameof(EnableInvert));
            RaisePropertyChanged(nameof(DisableReason));

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

            _picker.Pick(animation.VariableId, animation.VariableName, animation.ArrayIndex,
                (id, name, arrayIndex) => animation.BindVariable(id, name, arrayIndex));
        }

        private void OnClearVariable()
        {
            if (Animation is not { } animation) return;

            using (animation.BeginEdit("清除动画驱动变量"))
            {
                animation.VariableId = Guid.Empty;
                animation.VariableName = null;
                animation.ArrayIndex = -1;
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
    /// 「类型」行的声明：图元类型键是<b>只读事实</b>——它写进 .vms、是注册表的键，
    /// 也是"本机未注册该图元"那条提示里出现的东西；用户要能看见它，但不该能改。
    ///
    /// 为什么不放进描述符：描述符是<b>按类型共享</b>的，而类型键是"这一行实例"的事实；
    /// 更要紧的是它不在属性袋里、也没有 <c>TargetProperty</c>，塞进描述符只会让
    /// <see cref="ElementValueAccess"/> 多一条"这个键没有落点"的特例。
    /// 与权限行同一安排：自带一份声明，顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道进面板。
    /// </summary>
    internal sealed class ScadaTypeSpec : IPropertySpec
    {
        /// <summary>无状态共享实例（读哪个图元由行实例钉住，与 <see cref="GeometryProperties.Standard"/> 同一手法）</summary>
        public static readonly ScadaTypeSpec Instance = new ScadaTypeSpec();

        private ScadaTypeSpec() { }

        /// <summary>不落属性袋，只是一个标识（与权限行的 <c>RequiredRole</c> 同源）</summary>
        public string Key => "ElementType";

        public string DisplayName => "类型";

        public ElementPropertyKind Kind => ElementPropertyKind.ReadOnly;

        public string Group => ScadaPropertyGroups.General;

        public string? Description =>
            "图元类型键（.vms 里存的就是它，注册表按它找控件）。类型在创建时定下，面板里不能改；要换类型请删掉后重新拖一个";

        /// <summary>类型键不是运行期数据，没有"绑一个变量进来"这回事</summary>
        public bool IsBindable => false;

        public double Min => double.NegativeInfinity;

        public double Max => double.PositiveInfinity;

        public IReadOnlyList<string> Choices => Array.Empty<string>();
    }

    /// <summary>
    /// 属性面板的一行<b>类型</b>：只读展示 <see cref="ScadaElement.TypeKey"/>。
    ///
    /// 与 <see cref="ScadaPropertyRow"/> 的差别只在"写"：那一行有落点，这一行没有——
    /// <see cref="Commit"/> 恒返回 false，而面板按 <see cref="ElementPropertyKind.ReadOnly"/>
    /// 给它的是只读块（<c>EditReadOnly</c> 模板），根本长不出输入框。
    /// 两道闸都留着：模板管"不给入口"，<see cref="Commit"/> 管"绕过模板也写不进去"。
    /// </summary>
    public sealed class ScadaTypeRow : ScadaPropertyRowBase
    {
        public ScadaTypeRow(ScadaElement element)
            : base(ScadaTypeSpec.Instance,
                   (element ?? throw new ArgumentNullException(nameof(element))).TypeKey)
        {
            Element = element;
        }

        /// <summary>本行展示的是哪个图元的类型</summary>
        public ScadaElement Element { get; }

        protected override string Read() => Element.TypeKey;

        /// <summary>类型不可改：这里是"绕过模板也写不进去"的第二道闸</summary>
        protected override bool Commit(string text) => false;
    }

    /// <summary>
    /// 权限行的声明（B3）：<see cref="ScadaElement.RequiredPermission"/> <b>不是</b>属性袋里的键，
    /// 描述符也声明不了它（那是强类型字段，理由见该属性的注释），所以它像事件行一样自带一份声明，
    /// 存在的唯一目的是顺着 <see cref="ScadaPropertyRowBase"/> 的既有通道
    /// （Kind/DisplayName/Description）进同一个面板模板，而不是为权限单开一套 UI。
    ///
    /// <see cref="Choices"/> 给空：候选<b>来自用户管理的权限目录</b>（含自定义，当前值还可能是
    /// 已删除权限的悬空 Key），由 <see cref="ScadaPermissionPropertyRow"/> 自己算——
    /// 与基类 <c>Choices</c> 注释里「所属图层」那条同源。声明是无状态的共享实例
    /// （与 <see cref="GeometryProperties.Standard"/> 同一手法）。
    /// </summary>
    internal sealed class ScadaPermissionSpec : IPropertySpec
    {
        /// <summary>无状态共享实例（读哪个图元由行实例钉住）</summary>
        public static readonly ScadaPermissionSpec Instance = new ScadaPermissionSpec();

        private ScadaPermissionSpec() { }

        /// <summary>不落属性袋，只是一个标识（与事件行的 <c>Event.N</c> 同源）</summary>
        public string Key => "RequiredPermission";

        public string DisplayName => "所需权限";

        public ElementPropertyKind Kind => ElementPropertyKind.Choice;

        public string Group => ScadaPropertyGroups.Interaction;

        public string? Description =>
            "运行态操作这个图元所需的权限（来自用户管理的权限目录，含自定义）。选「不限制」= 谁都能操作；按权限 Key 精确匹配，不做角色高低包含";

        /// <summary>权限是"谁能操作"，不该被变量驱动——绑一个变量进来等于把权限交给运行数据</summary>
        public bool IsBindable => false;

        public double Min => double.NegativeInfinity;

        public double Max => double.PositiveInfinity;

        public IReadOnlyList<string> Choices => Array.Empty<string>();
    }

    /// <summary>
    /// 权限候选的<b>唯一构造口径</b>：图元行与画面行共用。
    ///
    /// 候选 = 「不限制」 + 权限目录（内置 15 + 自定义） + 当前值（认不出来时临时补在末尾）。
    /// 与旧角色行的差别：显示名不再来自 <see cref="ScadaChoiceNames"/> 的固定词汇表，
    /// 而是来自权限目录本身——自定义权限的显示名只能由目录给。
    ///
    /// <b>当前值认不出来时也要画出来</b>：已删除权限的悬空 Key、旧版本写入的未知 Key，
    /// 若直接丢掉，下拉会显示成空白，用户看到的是"这一行莫名其妙是空的"，
    /// 而不是"这里有个坏值，重选一个"。这与旧角色行"未知角色(N) 临时补在末尾"同一取舍。
    /// </summary>
    internal static class ScadaPermissionChoices
    {
        public static (IReadOnlyList<string> Choices, IReadOnlyList<ScadaChoiceOption> Options) Build(
            string? current,
            Func<IReadOnlyList<ScadaPermissionInfo>>? catalog)
        {
            var options = new List<ScadaChoiceOption>
            {
                new(ScadaPermissionKeys.NotLimitedValue, "不限制"),
            };
            var seen = new HashSet<string>(StringComparer.Ordinal)
            {
                ScadaPermissionKeys.NotLimitedValue,
            };

            var source = catalog?.Invoke();
            if (source == null || source.Count == 0)
                source = ScadaPermissionKeys.BuiltIn;

            foreach (var permission in source)
            {
                if (permission == null || string.IsNullOrWhiteSpace(permission.Key))
                    continue;
                if (!seen.Add(permission.Key))
                    continue;

                options.Add(new ScadaChoiceOption(
                    permission.Key,
                    string.IsNullOrWhiteSpace(permission.Name) ? permission.Key : permission.Name));
            }

            if (!string.IsNullOrWhiteSpace(current)
                && !string.Equals(current, ScadaPermissionKeys.NotLimitedValue, StringComparison.Ordinal)
                && seen.Add(current))
            {
                options.Add(new ScadaChoiceOption(current, DescribeUnknown(current)));
            }

            return (options.Select(o => o.Value).ToArray(), options);
        }

        /// <summary>目录里没有的 Key：<c>Always</c> 是系统级恒放行，单独给一句人话；其余按"未知权限"显示。</summary>
        public static string DescribeUnknown(string key)
            => string.Equals(key, ScadaPermissionKeys.Always, StringComparison.Ordinal)
                ? "始终允许（Always）"
                : $"未知权限({key})";

        /// <summary>模型值 → 下拉里的落盘值；空 = 「不限制」哨兵。</summary>
        public static string Describe(string? permissionKey)
            => string.IsNullOrWhiteSpace(permissionKey)
                ? ScadaPermissionKeys.NotLimitedValue
                : permissionKey;
    }

    /// <summary>
    /// 属性面板的一行<b>所需权限</b>（B3）：一个下拉框 = "运行态操作这个图元要求哪个权限 Key"。
    ///
    /// 为什么不复用 <see cref="ScadaPropertyRow"/>：权限既不是几何键、也不在属性袋里，
    /// 走 <see cref="ElementValueAccess"/> 得先塞一个假键进去——那等于污染那条唯一通道。
    /// 与事件行、画面行同一取舍：读写逻辑各自一小段，值语义/标红/回填全部继承基类。
    ///
    /// 写回一律走 <see cref="ScadaPage.TrySetRequiredPermission"/>（D3），本行自己不碰
    /// <see cref="ScadaElement.RequiredPermission"/>：裸写虽然也会落一条记录，但那条记录没有操作名
    /// （撤销按钮上只显示"编辑"），而且 <c>ScadaWriteGuard.Strict</c> 下会被当场拒掉。
    ///
    /// 候选来自用户管理的权限目录（内置 + 自定义）。旧 .vms 只写了 <see cref="ScadaElement.RequiredRole"/>
    /// 的，读出来经 <see cref="ScadaPermissionKeys.LegacyRolePermission"/> 折成 Key 显示；
    /// 用户一旦在这里改选，写入口会把旧角色清空——否则选「不限制」时旧角色又会兜底回来。
    /// </summary>
    public sealed class ScadaPermissionPropertyRow : ScadaPropertyRowBase
    {
        private readonly ScadaPage _page;
        private readonly Func<IReadOnlyList<ScadaPermissionInfo>>? _catalog;

        private IReadOnlyList<string> _choices;
        private IReadOnlyList<ScadaChoiceOption> _options;

        /// <summary>上次算候选集时用的"当前值"（判断候选集要不要重算的唯一依据）</summary>
        private string _described;

        public ScadaPermissionPropertyRow(
            ScadaPage page,
            ScadaElement element,
            Func<IReadOnlyList<ScadaPermissionInfo>>? catalog = null)
            : base(ScadaPermissionSpec.Instance,
                   ScadaPermissionChoices.Describe((element ?? throw new ArgumentNullException(nameof(element))).RequiredPermissionOf()))
        {
            _page = page ?? throw new ArgumentNullException(nameof(page));
            Element = element;
            _catalog = catalog;

            _described = ScadaPermissionChoices.Describe(element.RequiredPermissionOf());
            (_choices, _options) = ScadaPermissionChoices.Build(_described, _catalog);
        }

        /// <summary>本行写入的目标图元</summary>
        public ScadaElement Element { get; }

        /// <summary>候选的落盘值（<c>__none__</c> + 权限 Key）</summary>
        public override IReadOnlyList<string> Choices => _choices;

        /// <summary>下拉实际绑定的一份（值 + 目录里的显示名）</summary>
        public override IReadOnlyList<ScadaChoiceOption> ChoiceOptions => _options;

        protected override string Read()
            => ScadaPermissionChoices.Describe(Element.RequiredPermissionOf());

        /// <summary>
        /// 回读时连候选集一起对一遍：坏值进出名单会让候选多一项/少一项，
        /// 而基类只通知 <c>Value</c>，不通知候选——不补这一手，
        /// 撤销掉一个坏值之后下拉框里那条"未知权限(...)"会一直挂着。
        /// 只在"当前值文案变了"时才重算并通知：本方法在图元属性变更时被逐行调用，
        /// 拖动图元期间会连着响很多次，权限跟几何无关，必须能一眼判定"没事发生"。
        /// </summary>
        public override void RefreshValue()
        {
            base.RefreshValue();

            string current = Read();
            if (string.Equals(current, _described, StringComparison.Ordinal)) return;

            _described = current;
            (_choices, _options) = ScadaPermissionChoices.Build(current, _catalog);
            RaisePropertyChanged(nameof(Choices));
            RaisePropertyChanged(nameof(ChoiceOptions));
        }

        /// <summary>
        /// 权限目录变了（新建/删除自定义权限）：只重算候选，不动当前值。
        /// 当前值若是刚被删掉的 Key，<see cref="ScadaPermissionChoices.Build"/> 会把它作为
        /// "未知权限"临时补在末尾——用户仍看得见它，并能改选一个有效权限。
        /// </summary>
        public void RefreshChoices()
        {
            _described = Read();
            (_choices, _options) = ScadaPermissionChoices.Build(_described, _catalog);
            RaisePropertyChanged(nameof(Choices));
            RaisePropertyChanged(nameof(ChoiceOptions));
        }

        protected override bool Commit(string text)
        {
            if (!TryParsePermission(text, out var permissionKey))
            {
                // 空文案单独措辞：下拉被清空（当前值不在候选里时 WPF 会这么做）不是"用户选了不限制"，
                // 而是"这一行现在对不上任何一个选项"，得让人看见并就地重选。
                SetError(string.IsNullOrWhiteSpace(text)
                    ? "请从下拉里选一个权限（清空不等于「不限制」）"
                    : $"不认识的权限「{text}」");
                return false;
            }

            // D3：写模型只走画面上的统一入口（它内部罩 BeginEdit，撤销栈里是一条"设置所需权限"）
            if (!_page.TrySetRequiredPermission(Element, permissionKey, out var error))
            {
                SetError(error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 下拉值 → 权限 Key。
        /// 只有<b>当前候选里认得的值</b>才落得下：<c>__none__</c> → <c>null</c>，
        /// 目录里的 Key → 原样；坏值项（临时补上的）也可以原样保留，但造不出新的坏值。
        ///
        /// 空文案<b>不算</b>「不限制」，这一点是刻意的。下拉不是可编辑控件，用户打不出空值；
        /// 它变空只有一种来路——当前值不在候选列表里，WPF 就把 SelectedItem 置成 null 并回写。
        /// 那时若把空当成"取消限制"，一次候选失配就会<b>静默把权限放大成谁都能操作</b>。
        /// </summary>
        private bool TryParsePermission(string? text, out string? permissionKey)
        {
            permissionKey = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (string.Equals(text, ScadaPermissionKeys.NotLimitedValue, StringComparison.Ordinal))
                return true;

            if (!_choices.Contains(text, StringComparer.Ordinal))
                return false;

            permissionKey = text;
            return true;
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
        private readonly IScadaFlowPicker _flowPicker;
        private readonly IScadaAxisPicker _axisPicker;

        /// <summary>「选配方」入口（「应用配方」动作行用）；可为 null = 没接（按钮灰掉，不静默空跑）</summary>
        private readonly IScadaRecipePicker? _recipePicker;

        /// <summary>
        /// 权限目录出口（「所需权限」下拉的候选来源）。可为 null = 断言宿主/无用户管理时
        /// 回落到 15 条内置权限；接了就在目录变化时刷新候选。
        /// </summary>
        private readonly IScadaPermissionCatalog? _permissionCatalogSource;

        /// <summary>当前权限目录快照（每次现取；用户管理里刚建的自定义权限立刻可选）</summary>
        private readonly Func<IReadOnlyList<ScadaPermissionInfo>> _permissionCatalog;

        /// <summary>轴身份 → 最新轴名的权威来源（属性行摘要按 Id 取显示名，不信任落盘时那份旧名）</summary>
        private readonly MotionAxisRegistry _axisRegistry;

        /// <summary>创建本 VM 的线程（轴改名事件可能在后台线程触发，通知必须回 UI 线程）</summary>
        private readonly Dispatcher _dispatcher;

        /// <summary>本拍轴名快照（轴注册表变化时整体换掉，见 <see cref="OnAxisRegistryChanged"/>）</summary>
        private IReadOnlyDictionary<Guid, string> _axisNames = new Dictionary<Guid, string>();

        /// <summary>摊平的行（模型有外部写入时逐行回读；分组视图从它派生）</summary>
        private readonly List<ScadaPropertyRowBase> _rows = new();

        /// <summary>分组折叠状态（按组名记住；搜索期间强制展开，清空后恢复）</summary>
        private readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.Ordinal);

        /// <summary>多曲线趋势的曲线卡模型（其它图元为 null）</summary>
        private ScadaSeriesCard? _seriesCard;

        private bool _subscribed;
        private ScadaElement? _element;
        private ScadaPage? _page;

        /// <summary>搜索词（空串 = 不过滤）。只影响"显示哪些行"，不影响行本身，见 <see cref="SearchText"/></summary>
        private string _searchText = string.Empty;

        public ScadaPropertyViewModel(ScadaEditorViewModel editor, IUserNotifier notifier, IScadaVariablePicker picker, IScadaPagePicker pagePicker, IScadaFlowPicker flowPicker, IScadaAxisPicker axisPicker, MotionAxisRegistry axisRegistry, IScadaRecipePicker? recipePicker = null, IScadaPermissionCatalog? permissionCatalog = null)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _pagePicker = pagePicker ?? throw new ArgumentNullException(nameof(pagePicker));
            _flowPicker = flowPicker ?? throw new ArgumentNullException(nameof(flowPicker));
            _axisPicker = axisPicker ?? throw new ArgumentNullException(nameof(axisPicker));
            _axisRegistry = axisRegistry ?? throw new ArgumentNullException(nameof(axisRegistry));
            _recipePicker = recipePicker;
            _permissionCatalogSource = permissionCatalog;
            _permissionCatalog = () =>
            {
                var catalog = permissionCatalog?.PermissionCatalog;
                return catalog is { Count: > 0 } ? catalog : ScadaPermissionKeys.BuiltIn;
            };
            _dispatcher = Dispatcher.CurrentDispatcher;
            RefreshAxisNames();

            // 参数化的 CanExecute 而不是 IsEnabled：行对象换了，可用性就得重算，
            // 挂在参数上比挂在面板状态上准（也便于断言里直接调 CanExecute）。
            // 命令参数用行基类而不是 ScadaPropertyRow：面板里两种行都有，命令认"行"就够了，
            // 将来画面属性要开绑定也只是把某条声明的 IsBindable 改成 true，签名不动。
            BindVariableCommand = new DelegateCommand<ScadaPropertyRowBase>(OnBindVariable, row => row != null && row.IsBindable);

            // 清除入口只对"确实绑了"的行可用：按钮的可见性也绑在 HasBinding 上，两处口径一致。
            // 谓词读的是行状态（不是面板状态），所以行状态一变就得由面板抛一次
            // RaiseCanExecuteChanged——见 OnElementPropertyChanged，那里是唯一的刷新点。
            ClearBindingCommand = new DelegateCommand<ScadaPropertyRowBase>(OnClearBinding, row => row != null && row.HasBinding);

            ClearSearchCommand = new DelegateCommand(() => SearchText = string.Empty, () => HasSearchText);
            ToggleFoldAllCommand = new DelegateCommand(OnToggleFoldAll);

            Activate();
        }

        /// <summary>当前编辑的图元（null 表示没选中图元，面板转入画面模式）</summary>
        public ScadaElement? Element => _element;

        /// <summary>当前编辑的画面（没选中图元时才有意义；无方案则为 null）</summary>
        public ScadaPage? Page => _page;

        /// <summary>有选中图元（驱动"锁定"勾选的可见性——锁定是图元才有的概念）</summary>
        public bool HasElement => _element != null;

        /// <summary>
        /// 当前编辑的是不是<b>基座页</b>（跨页公共层）——面板顶部据此常驻一句合成口径说明。
        ///
        /// 为什么读 <see cref="ScadaEditorViewModel.SelectedPage"/> 而不是本类的 <see cref="Page"/>：
        /// 后者只在"没选中图元"时才有值，而这条说明讲的是"这一页是什么"——选中基座上的图元时同样成立。
        ///
        /// 文案只讲<b>一条</b>规则（只有图元会合成），并给出唯一正解（插一块铺满的矩形）。
        /// 不写"为什么"：面板里每多一句解释，真正的属性就往下掉一行。
        /// </summary>
        public bool IsBasePageEditing
            => _editor.SelectedPage is { } page && _editor.Document.IsBasePage(page);

        /// <summary>
        /// 面板上现在有没有行可显示（决定空态水印）。<b>按当前搜索词过滤后</b>算——
        /// 搜索无结果时也要出水印，否则列表空着、一句提示都没有，用户会以为面板卡住了。
        /// </summary>
        public bool HasRows => Groups.Count > 0;

        /// <summary>搜索框里有没有内容（决定清空按钮显不显示）</summary>
        public bool HasSearchText => _searchText.Length > 0;

        /// <summary>
        /// 搜索词：按<b>属性名 / 键名 / 分组名</b>过滤，大小写不敏感。
        ///
        /// 为什么不重建行、只重算分组：重建会把用户正在编辑的那一行连输入框一起换掉
        /// （焦点丢、未提交的文本丢）。行是模型的视图，搜索只是"显示哪些行"的过滤器，
        /// 改它不该动行本身——所以走 <see cref="RebuildGroups"/>，而不是 <see cref="Rebuild"/>。
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, _searchText, StringComparison.Ordinal)) return;

                _searchText = text;
                RaisePropertyChanged(nameof(SearchText));
                RebuildGroups();

                // 谓词读 HasSearchText，值一变可用性就得重算（命令不会自己去问）
                ClearSearchCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>「清空搜索」：把搜索词抹掉（与用户手动删空走同一条路径）</summary>
        public DelegateCommand ClearSearchCommand { get; }

        /// <summary>「折叠全部 / 展开全部」：长面板一键收纳（v2）</summary>
        public DelegateCommand ToggleFoldAllCommand { get; }

        /// <summary>所有组都折叠了吗（决定按钮图标与提示）</summary>
        public bool AllCollapsed => Groups.Count > 0 && Groups.All(g => !g.IsExpanded);

        /// <summary>折叠按钮图标（⌃ = 可折叠，⌄ = 可展开）</summary>
        public string FoldGlyph => AllCollapsed ? "⌄" : "⌃";

        public string FoldTooltip => AllCollapsed ? "展开全部" : "折叠全部";

        /// <summary>对象头的尺寸 chip（画面模式显示 1920 × 1080；图元模式为空）</summary>
        public string HeaderMeta
            => _page is { } page
                ? $"{page.Width.ToString("0.##", CultureInfo.InvariantCulture)} × {page.Height.ToString("0.##", CultureInfo.InvariantCulture)}"
                : string.Empty;

        /// <summary>
        /// 空态标题（水印第一行）。与 <see cref="EmptyHint"/> 分工：标题说"现在是什么状态"，
        /// 正文说"该怎么办"——旧版只有一段长句，四行字挤在一起，扫一眼分不清是"没选"还是"不认识"。
        /// </summary>
        public string EmptyTitle
        {
            get
            {
                // 搜索无结果：与"这个对象本来就没属性"是两件事，混一句会让人去怀疑对象
                if (_rows.Count > 0 && Groups.Count == 0)
                    return "没有匹配的属性";

                if (_element != null)
                    return "这个图元本机不认识";

                if (_page != null)
                    return "没有可编辑的属性";

                return _editor.HasSolution ? "还没有组态画面" : "未选择对象";
            }
        }

        /// <summary>空态正文：四种成因要分开说，否则用户会以为是面板坏了</summary>
        public string EmptyHint
        {
            get
            {
                if (_rows.Count > 0 && Groups.Count == 0)
                    return "换个关键词试试。搜索匹配属性名、键名与分组名（如「宽」「Fill」「布局」）。";

                if (_element != null)
                    return "这个图元的类型在本机没有注册，面板拿不到它的属性声明，所以列不出任何东西。换台机器打开这个方案时，它仍会正常显示。";

                if (_page != null)
                    return "当前画面没有可编辑的属性。";

                if (_editor.HasSolution)
                    return "当前方案还没有组态画面。点组态画布上方的「新建画面」开始。";

                return "在画布上点选一个图元开始编辑；没选中时这里显示画面自己的属性。";
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
        /// 对象头的第一行：图元名 / 画面名 / 状态说明。
        ///
        /// 与 <see cref="HeaderSubtitle"/> 分工：这一行回答"编辑的是谁"，第二行回答"它是什么类型"。
        /// 旧版是一行「类型显示名 · 名字」，而新建图元时 Name 默认就等于 DisplayName，
        /// 于是面板顶上长期显示成「按钮 · 按钮」——两段一样的信息各占一半宽度。
        /// </summary>
        public string HeaderTitle
        {
            get
            {
                if (_element is { } element)
                    return element.Name;

                if (_page is { } page)
                    return page.Name;

                return _editor.HasSolution ? "当前方案没有组态画面" : "未打开方案";
            }
        }

        /// <summary>
        /// 对象头的第二行：图元的类型键（<c>Hmi.Button</c>）或"画面"。
        ///
        /// 用类型键而不是 CLR 类名：前者写进 .vms，也是"本机未注册"提示与注册表用的键；
        /// CLR 类名会随重构变，摆在面板上只会误导。类型没注册时在这里明说一句，
        /// 否则用户会以为属性丢了（与空态同一口径）。
        /// </summary>
        public string HeaderSubtitle
        {
            get
            {
                if (_element is { } element)
                    return ElementRegistry.Find(element.TypeKey) != null
                        ? element.TypeKey
                        : $"{element.TypeKey}（本机未注册该图元）";

                if (_page != null)
                    return "画面";

                return string.Empty;
            }
        }

        /// <summary>
        /// 对象头左侧那颗图标（Font Awesome 6 Solid 码点）：图元用「形状」，画面用「文件」。
        ///
        /// 为什么不分图元类型给不同图标：图元库没有图标资产（工具箱那边也只是名字 + 类型键），
        /// 在这里现造一张"类型 → 码点"的映射表，加一类图元就要多记一处，不值当。
        /// 图标在这里的作用是给对象头一个视觉锚点，不是"一眼认出是泵还是电机"。
        /// </summary>
        public string HeaderGlyph => _element != null ? "\uF5CB" : "\uF15B";

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

            // 轴改名不一定经过编辑器（用户在「运动卡设置」里改），所以单独订阅注册表：
            // 「候选轴」摘要按 Id 取最新名字，轴名变了要立刻回填，不能等重开选轴弹窗。
            _axisRegistry.Changed += OnAxisRegistryChanged;
            RefreshAxisNames();

            // 权限目录同理：用户刚在「用户管理」里新建/删除自定义权限，回到组态面板
            // 下拉里就该有/没有那一项，不必重选图元。
            if (_permissionCatalogSource != null)
                _permissionCatalogSource.CatalogChanged += OnPermissionCatalogChanged;

            Rebuild();
        }

        /// <summary>摘干净（幂等）；面板离树时调用，让旧图元不被本面板钉住</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _axisRegistry.Changed -= OnAxisRegistryChanged;
            if (_permissionCatalogSource != null)
                _permissionCatalogSource.CatalogChanged -= OnPermissionCatalogChanged;
            BindElement(null);
            BindPage(null);
        }

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 基座注册/摘除不一定伴随换页（摘掉基座后用户还停在同一页）：只刷顶部那条说明，
            // 不重建行——重建会把正在编辑的输入框与焦点一起换掉。
            if (e.PropertyName == nameof(ScadaEditorViewModel.CanSelectBasePage))
            {
                RaisePropertyChanged(nameof(IsBasePageEditing));
                return;
            }

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

        /// <summary>
        /// 轴注册表变了（改名/增删/换卡）：重取名字快照，并让所有「候选轴」行重算摘要。
        ///
        /// 事件可能在后台线程触发（发布器/别的会话调 Snapshot 时发现指纹变化），
        /// 所以先切回创建本 VM 的 Dispatcher 再碰属性通知。
        /// </summary>
        private void OnAxisRegistryChanged(object? sender, EventArgs e)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => OnAxisRegistryChanged(sender, e)));
                return;
            }

            RefreshAxisNames();

            foreach (var row in _rows.OfType<ScadaAxisListRow>())
                row.RefreshSummary();
        }

        /// <summary>
        /// 权限目录变了（用户管理里新建/删除自定义权限）：只让「所需权限」两行重算候选，
        /// 不重建整份面板——重建会把用户正在编辑的输入框与焦点一起换掉。
        /// 事件可能来自后台线程，先切回创建本 VM 的 Dispatcher。
        /// </summary>
        private void OnPermissionCatalogChanged()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(OnPermissionCatalogChanged));
                return;
            }

            foreach (var row in _rows)
            {
                switch (row)
                {
                    case ScadaPermissionPropertyRow permissionRow:
                        permissionRow.RefreshChoices();
                        break;
                    case ScadaPagePropertyRow pageRow when pageRow.IsPermission:
                        pageRow.RefreshChoices();
                        break;
                }
            }
        }

        /// <summary>重取「轴 Id → 最新轴名」快照（一次遍历，不在每个摘要 getter 里各查一遍）</summary>
        private void RefreshAxisNames()
        {
            var names = new Dictionary<Guid, string>();

            foreach (var binding in _axisRegistry.Snapshot())
            {
                if (binding.AxisId == Guid.Empty || string.IsNullOrWhiteSpace(binding.Name))
                    continue;

                names[binding.AxisId] = binding.Name;
            }

            _axisNames = names;
        }

        /// <summary>按 Id 取最新轴名；取不到（轴已删除）返回 null，由行回退到落盘名 + 标注</summary>
        private string? ResolveAxisName(Guid axisId)
            => _axisNames.TryGetValue(axisId, out var name) ? name : null;

        private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            foreach (var row in _rows)
                row.RefreshValue();

            // 行状态（非法 / 未完成）变了，组头聚合点要跟着重算——折叠时也能看见哪一组有问题。
            RefreshGroupStates();

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
                RaisePropertyChanged(nameof(HeaderTitle));
        }

        /// <summary>
        /// 画面自己变了（顶栏勾网格、别处改名、面板写回宽高）：逐行回读 + 刷新标题。
        ///
        /// 这里不区分变更属性名，理由和图元侧一样：画面统共六行，全量回读比维护
        /// "哪个属性变更要刷哪几行"的映射便宜，而且映射表必然漏。
        /// </summary>
        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 弹窗页开关会改变"模态/位置/偏移"四行是否可见：只 RefreshValue 不会增减行，
            // 必须整份重建（勾上后四行要立刻出现，取消后要立刻消失）。
            if (e.PropertyName == nameof(ScadaPage.IsPopup))
            {
                Rebuild();
                return;
            }

            foreach (var row in _rows)
                row.RefreshValue();

            RefreshGroupStates();

            if (e.PropertyName is null || e.PropertyName == nameof(ScadaPage.Name))
            {
                RaisePropertyChanged(nameof(HeaderTitle));
                RaisePropertyChanged(nameof(EmptyHint));
            }

            if (e.PropertyName is null
                || e.PropertyName == nameof(ScadaPage.Width)
                || e.PropertyName == nameof(ScadaPage.Height))
            {
                RaisePropertyChanged(nameof(HeaderMeta));
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
                {
                    // 条件可见：同一属性在不同呈现/模式下是否有效（如 ComboBox 的 ShowSeparator 只在 Text 呈现有效）。
                    // 不隐藏的话，属性面板上会留下"改了没反应"的死开关（设计基线原则 06）。
                    if (!property.IsVisibleFor(element))
                        continue;

                    // AxisList 行要一个"选择轴…"入口，普通属性行没有这个动作。
                    // 判据是行的 Kind 自述，而不是图元类型——与编辑器按 Kind 分流同一手法。
                    // 摘要显示的名字按 Id 从注册表现取（落盘 JSON 里那份是"选的时候"的名字，
                    // 轴改名后它必然过期；Id 才是权威）。
                    // AxisList / ColumnList 都要一个"进弹窗"的入口，普通行没有这个动作。
                    // 判据是行的 Kind 自述，而不是图元类型——与编辑器按 Kind 分流同一手法。
                    _rows.Add(property.Kind switch
                    {
                        ElementPropertyKind.AxisList => new ScadaAxisListRow(element, property, OnPickAxes, ResolveAxisName),
                        ElementPropertyKind.ColumnList => new ScadaGridColumnsRow(element, property, OnPickColumns),
                        ElementPropertyKind.ReportColumnList => new ScadaReportColumnsRow(element, property, OnPickReportColumns),
                        // 图表数据项：绑的是工程变量（不是账本列名），所以单独一个 Kind + 单独编辑器
                        ElementPropertyKind.ChartSeries => new ScadaChartSeriesRow(element, property, OnPickChartSeries),
                        ElementPropertyKind.StateList => new ScadaStateListRow(
                            element, property, () => _editor.Document.StateLists, OnManageStateLists),
                        ElementPropertyKind.Page => new ScadaPageRow(
                            element, property, () => _editor.Document),
                        _ => new ScadaPropertyRow(element, property, () => _editor.SolutionRoot),
                    });

                    // 描述符声明为只读的键（几何由系统算，如 PipeRun）：显示但置灰并说明原因。
                    // 判据放在"行建好之后"而不是在 switch 里逐类型判断——只读是描述符的属性，不是行类型的属性。
                    if (descriptor.ReadOnlyKeys.Contains(property.Key))
                        _rows[^1].IsReadOnly = true;
                }

                // v2：不再生成「类型」只读行——类型键已在对象头（名称下面那行等宽小字）显示，
                // 面板里再排一行只读框是重复信息。ScadaTypeRow 保留但不再进面板。

                // S5 事件行：描述符声明了哪些事件就出哪几行（落在 ScadaPropertyGroups.Event 组）。
                // 没声明任何事件的图元连这个组都不会有——"这个图元配不了事件"是描述符的表态，
                // 不是面板漏列；反过来，面板绝不自行罗列全部事件，那会把
                // "勾上了却永远不响的钩子"（Unloaded/ValueChanged 还没接触发源）漏给用户。
                foreach (var eventType in descriptor.Events)
                    _rows.Add(new ScadaEventRow(element, eventType, ScadaEventRow.ElementGroup, _picker, _pagePicker, _flowPicker, _axisPicker, _recipePicker));

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
                // TrySetRequiredPermission 会以"图元不属于本画面"拒掉并标红，不会写错画面。
                if (_editor.SelectedPage is { } owner)
                    _rows.Add(new ScadaPermissionPropertyRow(owner, element, _permissionCatalog));
            }
            else if (page != null)
            {
                // 传文档而不是让行自己去找：能走到这一支说明 SelectedPage 非空，而它只可能来自
                // 当前文档（换方案时 OnDocumentChanged 会把它重对齐成新文档的第一页或 null），
                // 所以这里递给行的永远是"那个画面真正所属的方案"，不是无方案时的兜底空文档。
                var document = _editor.Document;

                foreach (var spec in ScadaPageProperties.All)
                {
                    if (!spec.IsVisibleFor(page))
                        continue;

                    _rows.Add(new ScadaPagePropertyRow(page, document, spec, () => _editor.SolutionRoot, _permissionCatalog));
                }

                // 画面级事件行：与图元侧同一份实现（宿主收成 IScadaEventHost），差别只有清单来源与分组。
                // 只列 ScadaPageEvents 声明的那两条——清单里没有的事件不出行，因为"配了却永远不响"
                // 比"面板上没这个选项"糟得多。落在「运行」组，紧接「启动画面」之后：
                // 组的首现次序决定显示次序，而这一组的第一行是上面那条 StartupPage。
                foreach (var eventType in ScadaPageEvents.All)
                    _rows.Add(new ScadaEventRow(page, eventType, ScadaPageEvents.Group, _picker, _pagePicker, _flowPicker, _axisPicker, _recipePicker));
            }

            // v2 呈现层重组：曲线卡（只对 MultiTrend）+ 成对网格（不改变行账本）。
            BuildSeriesCard(element);
            PairRows();

            RaisePropertyChanged(nameof(Element));
            RaisePropertyChanged(nameof(Page));
            RaisePropertyChanged(nameof(HasElement));
            RaisePropertyChanged(nameof(IsBasePageEditing));
            RaisePropertyChanged(nameof(HeaderTitle));
            RaisePropertyChanged(nameof(HeaderSubtitle));
            RaisePropertyChanged(nameof(HeaderGlyph));
            RaisePropertyChanged(nameof(HeaderMeta));

            // 换选中之后按当前搜索词重算分组。搜索词<b>不清空</b>：用户正按「温度」找属性时切了个图元，
            // 期望的是"在新图元里继续找温度"，而不是搜索框自己空掉。
            RebuildGroups();
        }

        /// <summary>
        /// 按当前搜索词把 <see cref="_rows"/> 分组（组序取自 <see cref="ScadaPropertyGroups.Order"/>）。
        ///
        /// <b>只重算分组，不重建行</b>：行上挂着正在编辑的文本与输入框焦点，搜一次就重建
        /// 等于把用户手里的活抹掉。所以搜索走这里，换选中才走 <see cref="Rebuild"/>。
        ///
        /// 组序的来由见 <see cref="ScadaPropertyGroups.Order"/>：目录里没有的组排到最后
        /// （<c>OrderOf</c> 返回 <see cref="int.MaxValue"/>），<c>OrderBy</c> 是稳定排序，
        /// 它们之间因此保持各自的首次出现次序，不会整组消失；组内次序仍是源次序。
        /// </summary>
        private void RebuildGroups()
        {
            foreach (var old in Groups)
                old.PropertyChanged -= OnGroupPropertyChanged;

            bool searching = _searchText.Length > 0;
            List<ScadaPropertyRowBase> filtered = searching
                ? _rows.Where(row => row.MatchesSearch(_searchText)).ToList()
                : _rows.ToList();

            if (searching)
                IncludePairPartners(filtered);

            var allByGroup = _rows
                .GroupBy(row => row.Group, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ScadaPropertyRowBase>)g.ToArray(), StringComparer.Ordinal);

            var groups = new List<ScadaPropertyGroup>();

            foreach (var g in filtered
                         .GroupBy(row => row.Group, StringComparer.Ordinal)
                         .OrderBy(g => ScadaPropertyGroups.OrderOf(g.Key)))
            {
                var all = allByGroup.TryGetValue(g.Key, out var allRows)
                    ? allRows
                    : Array.Empty<ScadaPropertyRowBase>();

                bool series = _seriesCard != null
                              && string.Equals(g.Key, ScadaPropertyGroups.Data, StringComparison.Ordinal);

                int display = series ? _seriesCard!.Items.Count + 3 : all.Count;
                bool expanded = searching
                                || !_expandedGroups.TryGetValue(g.Key, out var state)
                                || state;

                var group = new ScadaPropertyGroup(
                    g.Key, g.ToArray(), all, series ? _seriesCard : null,
                    g.Count(), display, expanded, searching);

                group.PropertyChanged += OnGroupPropertyChanged;
                groups.Add(group);
            }

            Groups = groups;

            RaisePropertyChanged(nameof(Groups));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(EmptyTitle));
            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(AllCollapsed));
            RaisePropertyChanged(nameof(FoldGlyph));
            RaisePropertyChanged(nameof(FoldTooltip));
        }

        /// <summary>
        /// 搜索命中一个成对行时把伙伴一起带出来：成对行只由左行的模板渲染，
        /// 只留右行会得到一个"自己的模板被收起"的空行。
        /// </summary>
        private void IncludePairPartners(List<ScadaPropertyRowBase> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                if (row.PairWith is { } partner && !rows.Contains(partner))
                    rows.Add(partner);

                if (row.PairOwner is { } owner && !rows.Contains(owner))
                    rows.Add(owner);
            }
        }

        /// <summary>逐行回读后重算组头聚合点（红点 / 琥珀点）</summary>
        private void RefreshGroupStates()
        {
            foreach (var group in Groups)
                group.RefreshState();
        }

        private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScadaPropertyGroup.IsExpanded))
                return;

            if (sender is ScadaPropertyGroup group)
                _expandedGroups[group.Name] = group.IsExpanded;

            RaisePropertyChanged(nameof(AllCollapsed));
            RaisePropertyChanged(nameof(FoldGlyph));
            RaisePropertyChanged(nameof(FoldTooltip));
        }

        private void OnToggleFoldAll()
        {
            bool collapse = !AllCollapsed;

            foreach (var group in Groups)
                group.IsExpanded = !collapse;

            RaisePropertyChanged(nameof(AllCollapsed));
            RaisePropertyChanged(nameof(FoldGlyph));
            RaisePropertyChanged(nameof(FoldTooltip));
        }

        /// <summary>
        /// v2 成对网格：X/Y、宽/高、边框粗细/圆角、字号/字重、弹窗偏移 X/Y。
        /// <b>只动视图</b>——右行仍留在 <c>_rows</c> 里（行账本与断言都不变），
        /// 由左行的模板渲染、自己的行模板收起。
        /// </summary>
        private void PairRows()
        {
            Pair("$X", "$Y");
            Pair("$Width", "$Height");
            Pair("StrokeThickness", "CornerRadius");
            Pair("FontSize", "FontWeight");
            Pair("PopupOffsetX", "PopupOffsetY");
        }

        private void Pair(string leftKey, string rightKey)
        {
            var left = _rows.FirstOrDefault(r => string.Equals(r.Key, leftKey, StringComparison.Ordinal));
            var right = _rows.FirstOrDefault(r => string.Equals(r.Key, rightKey, StringComparison.Ordinal));

            if (left == null || right == null
                || left.IsBindable || right.IsBindable
                || left.PairWith != null || right.PairOwner != null)
                return;

            left.PairWith = right;
            right.PairOwner = left;
            right.IsPairedFollow = true;
        }

        /// <summary>
        /// 多曲线趋势的曲线卡（v2）：4 条曲线一行一条（颜色点 + 名称 + 值 + ƒx）。
        /// 只重组呈现：12 条系列行仍在 <c>_rows</c> 里（校验 / 绑定 / 搜索照旧），
        /// 4 个曲线颜色在呈现层移入「数据」组，组头计数显示 7（4 曲线 + 3 设置）。
        /// </summary>
        private void BuildSeriesCard(ScadaElement? element)
        {
            _seriesCard = null;

            if (element == null
                || !string.Equals(element.TypeKey, "Hmi.MultiTrend", StringComparison.Ordinal))
                return;

            var byKey = new Dictionary<string, ScadaPropertyRowBase>(StringComparer.Ordinal);
            foreach (var row in _rows)
            {
                if (!byKey.ContainsKey(row.Key))
                    byKey[row.Key] = row;
            }

            string[] valueKeys = { "Value1", "Value2", "Value3", "Value4" };
            string[] nameKeys = { "Series1Name", "Series2Name", "Series3Name", "Series4Name" };
            string[] colorKeys = { "Series1Color", "Series2Color", "Series3Color", "Series4Color" };
            string[] settingKeys = { "TimeWindow", "RangeMode", "ShowLegend" };

            foreach (var key in valueKeys.Concat(nameKeys).Concat(colorKeys).Concat(settingKeys))
            {
                if (!byKey.ContainsKey(key))
                    return;
            }

            var items = new List<ScadaSeriesItem>(4);
            for (int i = 0; i < 4; i++)
            {
                items.Add(new ScadaSeriesItem(
                    $"曲线 {i + 1}",
                    byKey[colorKeys[i]],
                    byKey[nameKeys[i]],
                    byKey[valueKeys[i]]));
            }

            // 呈现层分组：曲线颜色从「外观」移入「数据」组卡（落盘 Group 与描述符都不动）
            foreach (var key in colorKeys)
                byKey[key].GroupOverride = ScadaPropertyGroups.Data;

            _seriesCard = new ScadaSeriesCard(
                items,
                byKey["TimeWindow"],
                byKey["RangeMode"],
                byKey["ShowLegend"]);
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

            _picker.Pick(current?.VariableId ?? Guid.Empty, current?.VariableName,
                current?.ArrayIndex ?? -1, (id, name, arrayIndex) =>
            {
                if (!page.TrySetBinding(elementRow.Element, elementRow.Key, id, name, arrayIndex, out var error))
                    _notifier.ShowError(error);
            });
        }

        /// <summary>
        /// 「选择轴…」：给轴面板挑候选轴。
        ///
        /// 与「ƒx 绑变量」的区别：这不是数据绑定，而是设计期配置——写回属性袋里的
        /// <see cref="ScadaAxisOptionsCodec.Key"/>（规范 JSON），运行期由面板自己的下拉框切换。
        /// 写模型仍走 <see cref="ScadaPage.TrySetAxisOptions"/> 这个统一入口（撤销/归属校验只有一处）。
        /// </summary>
        /// <summary>
        /// 「选择列…」：开弹窗编辑列定义，确认后写回。
        ///
        /// 写回走 <see cref="ScadaPropertyRowBase.Value"/>（→ Commit → 一条撤销记录），
        /// 与其它行同一条路——绕过它就得自己管撤销作用域、标红与回填。
        /// </summary>
        private void OnPickColumns(ScadaGridColumnsRow row)
        {
            var editor = new VisionMaster.Views.Controls.ScadaGridColumnsEditor(
                ScadaGridColumnsCodec.Parse(row.Value));

            if (!UI.CustomControl.EasyDialog.ShowSync("配置列", editor))
                return;

            // 变量名为空的行由 codec 丢掉（配置容错），但必须<b>说出来</b>：
            // 静默丢配置等于"我填了却没生效"，比报错更难查。
            if (editor.DroppedCount > 0)
                _notifier.ShowWarn($"已忽略 {editor.DroppedCount} 行未选变量的列（列必须绑定一个变量）");

            row.Value = editor.ResultJson;
        }

        /// <summary>
        /// 「配置列…」（报表）：与数据表格同一套弹窗范式，但编辑的是账本列 + 聚合方式。
        /// </summary>
        /// <summary>
        /// 「配置数据项…」（柱状图 / 饼图）：每行 = 标签 + 变量名 + 颜色。
        /// 与报表列同一套弹窗范式，但编辑的是工程变量（不是账本列名）。
        /// </summary>
        private void OnPickChartSeries(ScadaChartSeriesRow row)
        {
            var editor = new VisionMaster.Views.Controls.ScadaChartSeriesEditor(
                ScadaChartCodec.Parse(row.Value),
                _picker);

            if (!UI.CustomControl.EasyDialog.ShowSync("配置数据项", editor))
                return;

            if (editor.DroppedCount > 0)
                _notifier.ShowWarn($"已忽略 {editor.DroppedCount} 行未填变量的数据项（每项必须绑一个工程变量）");

            row.Value = editor.ResultJson;
        }

        private void OnPickReportColumns(ScadaReportColumnsRow row)
        {
            var editor = new VisionMaster.Views.Controls.ScadaReportColumnsEditor(
                ScadaReportCodec.Parse(row.Value),
                _editor.CurrentSolutionFilePath);

            if (!UI.CustomControl.EasyDialog.ShowSync("配置报表列", editor))
                return;

            if (editor.DroppedCount > 0)
                _notifier.ShowWarn($"已忽略 {editor.DroppedCount} 行未填来源列的列（列必须对应账本里的一列）");

            row.Value = editor.ResultJson;
        }

        private void OnPickAxes(ScadaAxisListRow row)
        {
            var page = _editor.SelectedPage;
            if (page == null) return;

            // 预选当前已选的轴：让"改一处"而不是"重新找一遍"。
            var current = ScadaAxisOptionsCodec.Parse(row.Value);

            _axisPicker.Pick(current, picked =>
            {
                if (!page.TrySetAxisOptions(row.Element, picked, out var error))
                    _notifier.ShowError(error);
            });
        }

        /// <summary>
        /// 「管理…」：打开文本列表管理器。管理器编辑的是副本，确认后才写回文档；
        /// 写回是原地覆盖（保留列表对象引用），ComboBox 的 Version 订阅不会断。
        /// </summary>
        private void OnManageStateLists(ScadaStateListRow row)
        {
            var view = new VisionMaster.Views.DialogViews.StateListManagerView(_editor.Document);
            if (!UI.CustomControl.EasyDialog.ShowSync("文本列表", view))
                return;

            view.Commit();
            row.RefreshValue();
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

    /// <summary>
    /// 图片文件行（<see cref="ElementPropertyKind.ImageFile"/>）的共用逻辑：
    /// 摘要、缩略图、选择/清除。页面背景行与图元（Hmi.Picture）行共用这一份，
    /// 避免“页面能选、图元点了没反应”这种两条路径各写一半的分叉。
    /// </summary>
    internal static class ScadaImageFileRowSupport
    {
        /// <summary>文件名摘要（不暴露完整路径）</summary>
        public static string Summary(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "未设置（点「选择…」）";

            var normalized = value.Replace('/', '\\');
            var index = normalized.LastIndexOf('\\');
            return index >= 0 ? normalized[(index + 1)..] : normalized;
        }

        /// <summary>缩略图（缺失/解码失败时为 null，模板回退占位）</summary>
        public static ImageSource? Preview(Func<string?>? solutionPath, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return SolutionAssetService.TryLoad(solutionPath?.Invoke(), value, out var image, out _)
                ? image
                : null;
        }

        /// <summary>
        /// 打开文件对话框并导入。取消返回 false 且 <paramref name="error"/> 为空；
        /// 失败返回 false 且 <paramref name="error"/> 是可直接上屏的原因。
        /// </summary>
        public static bool TryPick(Func<string?>? solutionPath, out string relativePath, out string error)
        {
            relativePath = string.Empty;
            error = string.Empty;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择图片（复制进方案 Image 目录）",
                Filter = SolutionAssetService.FileDialogFilter,
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true)
                return false;

            return SolutionAssetService.TryImport(
                solutionPath?.Invoke(),
                dialog.FileName,
                out relativePath,
                out error);
        }
    }
}
