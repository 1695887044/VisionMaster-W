using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 文本列表的匹配模式（手册第 8 章：位 / 位号 / 范围）。
    ///
    /// 为什么不是枚举落盘：<c>.vms</c> 里存字符串（<c>"Exact"</c>/<c>"Range"</c>/<c>"Bit"</c>/<c>"BitIndex"</c>），
    /// 坏值不抛、按 Exact 兜底。与 <c>Hmi.Report.Mode</c> / <c>ScadaAction.SoundKind</c> 同一条口径：
    /// 枚举数值落盘一旦写错就是"读成另一种模式"，字符串至少人眼能看出不对。
    /// </summary>
    public enum ScadaStateMatchMode
    {
        /// <summary>精确值（默认）：条目的 <see cref="ScadaStateEntry.Value"/> 等于变量值</summary>
        Exact = 0,

        /// <summary>范围：条目的 <see cref="ScadaStateEntry.Min"/>~<see cref="ScadaStateEntry.Max"/> 闭区间包含变量值</summary>
        Range = 1,

        /// <summary>位（0/1）：变量非 0 匹配 <c>Value=1</c> 的条目，为 0 匹配 <c>Value=0</c> 的条目</summary>
        Bit = 2,

        /// <summary>位号（0~31）：条目的 <see cref="ScadaStateEntry.Value"/> 是位号，该位为 1 即命中</summary>
        BitIndex = 3,
    }

    /// <summary>
    /// 文本列表的一条：值 → 显示文字 / 图片 / 状态色。
    ///
    /// 值必须是整数，与 <c>Hmi.ComboBox.Value</c> / <c>Hmi.StateView.Value</c> 同一类型；
    /// 显示文字只影响上屏，改文案不会打断已经配好的值（与 ComboBox 现有 Options 的口径一致）。
    ///
    /// P1-2 追加的两项（<see cref="Image"/> / <see cref="Color"/>）只被 <c>Hmi.StateView</c> 消费，
    /// ComboBox 继续只读 <see cref="Text"/>。旧 <c>.vms</c> 没有这两个键时反序列化得到空串，
    /// StateView 的表现与 P1-1 的纯文本态一致（不显示图片、状态色回落到中性色），不需要迁移代码。
    ///
    /// v6 追加 <see cref="Min"/> / <see cref="Max"/>：只在 <see cref="ScadaStateMatchMode.Range"/>
    /// 模式下参与匹配；精确/位/位号三种模式完全不看它们（旧文件里这两个键不存在 → 0/0，不影响任何旧列表）。
    /// </summary>
    public sealed class ScadaStateEntry : BindableBase
    {
        private int _value;
        private int _min;
        private int _max;
        private string _text = string.Empty;
        private string _image = string.Empty;
        private string _color = string.Empty;

        /// <summary>写入值 / 匹配值（整数）</summary>
        [JsonProperty]
        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>范围下界（含）；仅 <see cref="ScadaStateMatchMode.Range"/> 模式使用</summary>
        [JsonProperty]
        public int Min
        {
            get => _min;
            set => SetProperty(ref _min, value);
        }

        /// <summary>范围上界（含）；仅 <see cref="ScadaStateMatchMode.Range"/> 模式使用</summary>
        [JsonProperty]
        public int Max
        {
            get => _max;
            set => SetProperty(ref _max, value);
        }

        /// <summary>显示文字；空串按值文本显示</summary>
        [JsonProperty]
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value ?? string.Empty);
        }

        /// <summary>
        /// 状态图片（方案内相对路径，如 <c>Image\pump-run.png</c>）；空串 = 这一档没有图。
        ///
        /// 与 <see cref="Text"/> 同一口径：只影响上屏，改图不会打断已经配好的值。
        /// 路径由宿主注入的图片解析器落到位图；控件库不认识方案目录，也不接受绝对路径。
        /// </summary>
        [JsonProperty]
        public string Image
        {
            get => _image;
            set => SetProperty(ref _image, value ?? string.Empty);
        }

        /// <summary>
        /// 状态色（<c>#AARRGGBB</c> 文本，如 <c>#FF22C55E</c>）；空串 = 不指定，用中性色。
        ///
        /// 存文本而不是 Brush：领域层不引用 WPF，且 <c>.vms</c> 要能跨机器交换；
        /// 文本 → 画刷的换算只发生在控件库（与属性袋里其它颜色键同一口径）。
        /// </summary>
        [JsonProperty]
        public string Color
        {
            get => _color;
            set => SetProperty(ref _color, value ?? string.Empty);
        }
    }

    /// <summary>
    /// 方案级共享文本列表：一份定义，多个 ComboBox 引用。
    ///
    /// 为什么是方案级而不是图元属性
    /// ---------
    /// 同一组“设备状态 / 工位模式”常出现在总览页、工程师页、弹窗里；
    /// 每图元一份 Options 会出现“三处各写一遍、改一处漏两处”。
    /// 列表存在 <see cref="ScadaDocument.StateLists"/>，图元只存稳定的 <see cref="StateListId"/>；
    /// 列表改名不打断引用，删除时由管理器提示引用数。
    ///
    /// 为什么只做“离散值 → 文本/图片/状态色”
    /// ---------
    /// P1-1 的唯一消费方是 ComboBox（值本来就是 int），只保留“值 → 文字”；
    /// P1-2 的 StateView 需要在同一份列表上再取“这一档长什么样”，于是条目追加
    /// <see cref="ScadaStateEntry.Image"/> / <see cref="ScadaStateEntry.Color"/>、
    /// 列表追加 <see cref="DefaultImage"/>。<b>只追加、不改既有语义</b>：
    /// <see cref="ScadaStateEntry.Value"/> / <see cref="ScadaStateEntry.Text"/> 与
    /// <see cref="DefaultText"/> 的口径一行不动，ComboBox 完全不受影响。
    ///
    /// v6 起支持手册第 8 章的三种匹配模式（<see cref="Mode"/>）：范围 / 位 / 位号。
    /// 原先这里写着"位/位号/范围不在本表里"，本轮按用户要求补齐——**默认仍是精确值**，
    /// 旧列表一条不用改，行为与 v5 完全一致。
    ///
    /// 变更通知
    /// ---------
    /// 列表自身与条目都实现 <see cref="INotifyPropertyChanged"/>；任何条目增删改都会把
    /// <see cref="Version"/> 加一。设计期的 ComboBox 订阅 Version 重解析，
    /// 管理器改完不用重建整张画布。
    /// </summary>
    public sealed class ScadaStateList : BindableBase
    {
        public const string DefaultFallbackText = "—";

        /// <summary>模式字符串（落盘值）：精确值</summary>
        public const string ModeExact = "Exact";

        /// <summary>模式字符串（落盘值）：范围</summary>
        public const string ModeRange = "Range";

        /// <summary>模式字符串（落盘值）：位（0/1）</summary>
        public const string ModeBit = "Bit";

        /// <summary>模式字符串（落盘值）：位号（0~31）</summary>
        public const string ModeBitIndex = "BitIndex";

        private Guid _stateListId = Guid.NewGuid();
        private string _name = "新建文本列表";
        private string _defaultText = DefaultFallbackText;
        private string _defaultImage = string.Empty;
        private string _mode = ModeExact;
        private ObservableCollection<ScadaStateEntry> _entries = new();
        private int _version;

        [JsonConstructor]
        public ScadaStateList()
        {
            AttachEntries();
        }

        /// <summary>稳定身份；ComboBox 存它，改名不打断引用</summary>
        [JsonProperty]
        public Guid StateListId
        {
            get => _stateListId;
            set => SetProperty(ref _stateListId, value);
        }

        /// <summary>显示名（管理器/选择器用；不参与运行匹配）</summary>
        [JsonProperty]
        public string Name
        {
            get => _name;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "未命名文本列表" : value.Trim();
                SetProperty(ref _name, normalized);
            }
        }

        /// <summary>没有匹配条目时显示的文字；空值回落到 <see cref="DefaultFallbackText"/></summary>
        [JsonProperty]
        public string DefaultText
        {
            get => _defaultText;
            set => SetProperty(ref _defaultText, string.IsNullOrEmpty(value) ? DefaultFallbackText : value);
        }

        /// <summary>
        /// 没有匹配条目时显示的图片（方案内相对路径）；空串 = 不显示图，由控件画虚线占位。
        /// 与 <see cref="DefaultText"/> 同一语义：只影响上屏，不参与值匹配。
        /// </summary>
        [JsonProperty]
        public string DefaultImage
        {
            get => _defaultImage;
            set => SetProperty(ref _defaultImage, value ?? string.Empty);
        }

        /// <summary>条目（值 → 文字）。增删/条目改动都会推高 <see cref="Version"/></summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaStateEntry> Entries
        {
            get => _entries;
            set
            {
                if (ReferenceEquals(_entries, value))
                    return;

                DetachEntries();
                _entries = value ?? new ObservableCollection<ScadaStateEntry>();
                AttachEntries();
                BumpVersion();
            }
        }

        /// <summary>变更计数；订阅方只看它是否变了，不关心具体改了哪条</summary>
        [JsonIgnore]
        public int Version
        {
            get => _version;
            private set => SetProperty(ref _version, value);
        }

        /// <summary>
        /// 匹配模式（落盘字符串，见 <see cref="ModeExact"/> 等四个常量）。
        /// 坏值/空值按 <see cref="ModeExact"/> 处理，不抛——一个坏字段不该让整张画面打不开。
        /// </summary>
        [JsonProperty]
        public string Mode
        {
            get => _mode;
            set
            {
                var normalized = NormalizeMode(value);
                if (SetProperty(ref _mode, normalized))
                {
                    RaisePropertyChanged(nameof(ModeKind));
                    BumpVersion();
                }
            }
        }

        /// <summary>模式的强类型投影（界面与运行态都读它，不各自解析字符串）</summary>
        [JsonIgnore]
        public ScadaStateMatchMode ModeKind => ParseMode(_mode);

        /// <summary>把落盘字符串归一成四个常量之一（大小写不敏感；不认识的一律 Exact）</summary>
        public static string NormalizeMode(string? mode)
        {
            switch ((mode ?? string.Empty).Trim())
            {
                case ModeRange:
                case "range":
                    return ModeRange;
                case ModeBit:
                case "bit":
                    return ModeBit;
                case ModeBitIndex:
                case "bitindex":
                    return ModeBitIndex;
                default:
                    return ModeExact;
            }
        }

        /// <summary>落盘字符串 → 强类型；不认识的一律 Exact</summary>
        public static ScadaStateMatchMode ParseMode(string? mode)
        {
            switch (NormalizeMode(mode))
            {
                case ModeRange: return ScadaStateMatchMode.Range;
                case ModeBit: return ScadaStateMatchMode.Bit;
                case ModeBitIndex: return ScadaStateMatchMode.BitIndex;
                default: return ScadaStateMatchMode.Exact;
            }
        }

        /// <summary>
        /// 按当前模式匹配一条；无匹配返回 null。
        ///
        /// 四种模式的判定（顺序即优先级，且只有 Range 会看 Min/Max）：
        /// · Exact —— <c>Value == 变量值</c>；
        /// · Range —— <c>Min &lt;= 变量值 &lt;= Max</c>（重复区间取第一条）；
        /// · Bit —— 变量值非 0 取 <c>Value == 1</c> 的条目，为 0 取 <c>Value == 0</c> 的条目；
        /// · BitIndex —— 条目 <c>Value</c> 是位号（0~31），该位为 1 即命中（多条命中取第一条）。
        ///
        /// 为什么 Range 用闭区间而不是左闭右开：现场配的是"0~50 正常、51~80 预警"这种整数区间，
        /// 用户写 50 就是要包含 50；左闭右开会让"51~80"与"0~50"之间凭空多出一个 50.5 的空洞。
        /// </summary>
        public ScadaStateEntry? Find(int value)
        {
            switch (ModeKind)
            {
                case ScadaStateMatchMode.Range:
                    return _entries.FirstOrDefault(entry => value >= entry.Min && value <= entry.Max);

                case ScadaStateMatchMode.Bit:
                    var flag = value != 0 ? 1 : 0;
                    return _entries.FirstOrDefault(entry => entry.Value == flag);

                case ScadaStateMatchMode.BitIndex:
                    return _entries.FirstOrDefault(entry =>
                        entry.Value >= 0 && entry.Value < 32 && ((value >> entry.Value) & 1) != 0);

                default:
                    return _entries.FirstOrDefault(entry => entry.Value == value);
            }
        }

        /// <summary>值的显示文字；无匹配用 <see cref="DefaultText"/>，条目文字为空用值文本</summary>
        public string DisplayText(int value)
        {
            var entry = Find(value);
            if (entry == null)
                return _defaultText;

            return string.IsNullOrWhiteSpace(entry.Text)
                ? value.ToString(CultureInfo.InvariantCulture)
                : entry.Text;
        }

        /// <summary>值的显示图片；无匹配用 <see cref="DefaultImage"/>，条目图片为空则不显示图</summary>
        public string DisplayImage(int value) => Find(value)?.Image ?? _defaultImage;

        /// <summary>值的状态色（#AARRGGBB 文本）；无匹配或条目未配时为空串</summary>
        public string DisplayColor(int value) => Find(value)?.Color ?? string.Empty;

        /// <summary>深拷贝（管理器用副本编辑，取消不影响原列表）</summary>
        public ScadaStateList Clone()
        {
            var clone = new ScadaStateList
            {
                StateListId = _stateListId,
                Name = _name,
                DefaultText = _defaultText,
                DefaultImage = _defaultImage,
                Mode = _mode,
            };

            foreach (var entry in _entries)
            {
                clone.Entries.Add(new ScadaStateEntry
                {
                    Value = entry.Value,
                    Min = entry.Min,
                    Max = entry.Max,
                    Text = entry.Text,
                    Image = entry.Image,
                    Color = entry.Color,
                });
            }

            return clone;
        }

        /// <summary>
        /// 把另一份列表的内容原地覆盖到本对象（保留本对象的引用）。
        /// 管理器确认时用：已存在的列表对象不换，ComboBox 的订阅才不会断。
        /// </summary>
        public void CopyFrom(ScadaStateList source)
        {
            if (source == null)
                return;

            Name = source.Name;
            DefaultText = source.DefaultText;
            DefaultImage = source.DefaultImage;
            Mode = source.Mode;

            _entries.Clear();
            foreach (var entry in source.Entries)
            {
                _entries.Add(new ScadaStateEntry
                {
                    Value = entry.Value,
                    Min = entry.Min,
                    Max = entry.Max,
                    Text = entry.Text,
                    Image = entry.Image,
                    Color = entry.Color,
                });
            }

            BumpVersion();
        }

        /// <summary>管理器删除列表时调用：让仍订阅旧对象的控件知道“这份列表已经没了”</summary>
        public void MarkDeleted()
        {
            _entries.Clear();
            _name = "已删除的文本列表";
            _defaultText = "未找到文本列表";
            _defaultImage = string.Empty;
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(DefaultText));
            RaisePropertyChanged(nameof(DefaultImage));
            BumpVersion();
        }

        private void AttachEntries()
        {
            _entries.CollectionChanged += OnEntriesChanged;
            foreach (var entry in _entries)
                entry.PropertyChanged += OnEntryPropertyChanged;
        }

        private void DetachEntries()
        {
            _entries.CollectionChanged -= OnEntriesChanged;
            foreach (var entry in _entries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ScadaStateEntry entry in e.OldItems)
                    entry.PropertyChanged -= OnEntryPropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (ScadaStateEntry entry in e.NewItems)
                    entry.PropertyChanged += OnEntryPropertyChanged;
            }

            BumpVersion();
        }

        private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) => BumpVersion();

        private void BumpVersion() => Version++;
    }
}
