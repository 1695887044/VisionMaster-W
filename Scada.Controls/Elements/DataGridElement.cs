using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 表格里的一格（运行期视图模型）：文本 + 该格自己的宽度/对齐/等宽。
    ///
    /// 为什么把宽度挂在<b>格</b>上而不是让模板去查列定义：WPF 的 ItemsControl 不给项下标，
    /// 模板里拿不到"我是第几列"，也就查不到对应列的宽度。攒行时把列定义摊进每一格，
    /// 模板只绑 <c>Width</c> 就行，零转换器。
    /// </summary>
    public sealed class GridCellView
    {
        public GridCellView(string text, double width, TextAlignment align, bool monospace, bool isError)
        {
            Text = text ?? string.Empty;
            Width = width;
            Align = align;
            Monospace = monospace;
            IsError = isError;
        }

        /// <summary>已格式化的显示文本</summary>
        public string Text { get; }

        /// <summary>本格宽度（像素；余量列已经算成具体值）</summary>
        public double Width { get; }

        /// <summary>水平对齐</summary>
        public TextAlignment Align { get; }

        /// <summary>等宽字体（数字/时间/SN）</summary>
        public bool Monospace { get; }

        /// <summary>这一格没取到值（变量不存在/读取失败），模板标红</summary>
        public bool IsError { get; }
    }

    /// <summary>表格的一行（视图模型）：一串格。</summary>
    public sealed class GridRowView
    {
        public GridRowView(IReadOnlyList<GridCellView> cells) => Cells = cells;

        public IReadOnlyList<GridCellView> Cells { get; }
    }

    /// <summary>
    /// 历史范围下拉的一项：<see cref="Value"/> 是落盘值，<see cref="Label"/> 是界面文字。
    ///
    /// 为什么值不直接存中文：范围会写进 <c>.vms</c>，改显示文案（"最近 7 天" → "近一周"）
    /// 不该让已配好的画面变成未知值。与 <c>StepConfigOption.ForEnum</c> 同一条口径。
    /// </summary>
    public sealed class ScadaHistoryRangeOption
    {
        public ScadaHistoryRangeOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }
    }

    /// <summary>
    /// 数据表格图元（<c>Hmi.DataGrid</c>）：把"最近 N 条记录"摆出来。
    ///
    /// 三条边界
    /// ---------
    /// ① <b>它不认识变量注册表</b>：列定义里写的是变量<b>名字</b>，运行期经
    ///    <see cref="ScadaRuntimeContext.Values"/> 解析成句柄再取值——与轴面板认
    ///    <c>IScadaAxisSource</c> 同一条边界（图元库刻意不引用 VM.Core）。
    /// ② <b>它是显示缓冲，不是记录系统</b>：最近 N 行、切页重建、断电即失。
    ///    要留痕用「CSV 记录」插件（见 docs/UI设计基线/scada-datagrid/README.md）。
    /// ③ <b>采行 = 采行信号的上升沿</b>，图元对它是<b>只读</b>的（不复位）；需要"已收录"
    ///    就配「收录确认变量」，每采一行 +1。
    ///
    /// 采行在同一拍里完成：判到上升沿之后，<b>逐列从同一轮读取里取值</b>再攒行——
    /// 分两拍取会攒出"SN 是上一个产品、角度是下一个产品"的脏行。
    /// </summary>
    public class DataGridElement : ScadaElementBase
    {
        /// <summary>缓存行数的默认值与上下限（500 行 × 12 列已是"看不完"的量级）</summary>
        public const int DefaultMaxRows = 100;
        public const int MaxRowsLimit = 500;

        private IReadOnlyList<ScadaGridColumn> _columns = Array.Empty<ScadaGridColumn>();

        /// <summary>属性袋里配的实时列（历史模式下 <see cref="_columns"/> 会被换成历史列，切回实时要用回这一份）</summary>
        private IReadOnlyList<ScadaGridColumn> _configuredColumns = Array.Empty<ScadaGridColumn>();

        private readonly List<ScadaGridRow> _rows = new();

        /// <summary>历史模式的分页状态（0 基）</summary>
        private int _historyPageIndex;
        private int _historyTotal;
        private int _historyPageSize = 50;

        /// <summary>在飞查询的取消源（切页/改范围/关窗时取消旧请求）</summary>
        private CancellationTokenSource? _historyCts;

        private bool _historyBusy;

        private readonly ScadaRelayCommand _queryHistoryCommand;
        private readonly ScadaRelayCommand _previousHistoryPageCommand;
        private readonly ScadaRelayCommand _nextHistoryPageCommand;
        private readonly ScadaRelayCommand _refreshHistoryCommand;
        private readonly ScadaRelayCommand _cycleHistoryRangeCommand;

        /// <summary>上一拍的采行信号（边沿判定的唯一状态）</summary>
        private bool _signalHigh;

        /// <summary>本图元已收录的行数（确认变量写的就是它 +1 后的值）</summary>
        private int _recorded;

        /// <summary>列宽缓存（ActualWidth / 列定义变化时重算）</summary>
        private double[] _cellWidths = Array.Empty<double>();

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<GridRowView>), typeof(DataGridElement),
            new PropertyMetadata(Array.Empty<GridRowView>()));

        /// <summary>行（视图模型；模板直绑 ItemsSource）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HeaderCellsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HeaderCells), typeof(IReadOnlyList<GridCellView>), typeof(DataGridElement),
            new PropertyMetadata(Array.Empty<GridCellView>()));

        /// <summary>列头格（与行同宽同对齐，模板用同一个格子模板渲染）</summary>
        public static readonly DependencyProperty HeaderCellsProperty = HeaderCellsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(DataGridElement), new PropertyMetadata(string.Empty));

        /// <summary>脚注上的状态说明（坏列 / 确认写入失败 / 未配置），空串 = 正常</summary>
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyVisibility), typeof(Visibility), typeof(DataGridElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>空态显隐（由"有没有行 + 有没有配采行信号"算好，模板直绑）</summary>
        public static readonly DependencyProperty EmptyVisibilityProperty =
            EmptyVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyTitlePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyTitle), typeof(string), typeof(DataGridElement), new PropertyMetadata("等待第一条记录"));

        /// <summary>空态标题（两种成因分开说，见 <see cref="RefreshEmptyState"/>）</summary>
        public static readonly DependencyProperty EmptyTitleProperty = EmptyTitlePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyHintPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyHint), typeof(string), typeof(DataGridElement),
            new PropertyMetadata("采行信号还没触发。产品过站后会在这里逐条出现。"));

        /// <summary>空态正文</summary>
        public static readonly DependencyProperty EmptyHintProperty = EmptyHintPropertyKey.DependencyProperty;

        /// <summary>采行信号变量名（上升沿采一行）</summary>
        public static readonly DependencyProperty SampleVariableProperty = DependencyProperty.Register(
            nameof(SampleVariable), typeof(string), typeof(DataGridElement), new PropertyMetadata(string.Empty));

        /// <summary>收录确认变量名（可空：留空 = 不写）</summary>
        public static readonly DependencyProperty AckVariableProperty = DependencyProperty.Register(
            nameof(AckVariable), typeof(string), typeof(DataGridElement), new PropertyMetadata(string.Empty));

        /// <summary>缓存行数（超过就丢最旧的一行）</summary>
        public static readonly DependencyProperty MaxRowsProperty = DependencyProperty.Register(
            nameof(MaxRows), typeof(int), typeof(DataGridElement),
            new FrameworkPropertyMetadata(DefaultMaxRows, null, CoerceMaxRows));

        /// <summary>行高（像素）</summary>
        public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
            nameof(RowHeight), typeof(double), typeof(DataGridElement), new PropertyMetadata(28d));

        /// <summary>新行在上（默认；最新一条永远第一行，不抢滚动）</summary>
        public static readonly DependencyProperty NewestFirstProperty = DependencyProperty.Register(
            nameof(NewestFirst), typeof(bool), typeof(DataGridElement), new PropertyMetadata(true));

        /// <summary>斑马纹（隔行浅底）</summary>
        public static readonly DependencyProperty ZebraProperty = DependencyProperty.Register(
            nameof(Zebra), typeof(bool), typeof(DataGridElement), new PropertyMetadata(true));

        /// <summary>
        /// 数据源：<c>Live</c>（默认，采行信号攒行）或 <c>History</c>（查方案历史 CSV）。
        ///
        /// 为什么只换源不新建图元：列定义、行模板、斑马纹、空态、最大行数这些"怎么摆"
        /// 与"数据从哪来"无关；再造一个 HistoryGrid 等于把同一套模板复制一遍，
        /// 之后每个视觉修补都要改两处（设计基线 §历史数据第一条）。
        /// </summary>
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(string), typeof(DataGridElement),
            new PropertyMetadata("Live", OnSourceChanged));

        /// <summary>历史范围（Today / Yesterday / Last7Days / Last30Days）</summary>
        public static readonly DependencyProperty HistoryRangeProperty = DependencyProperty.Register(
            nameof(HistoryRange), typeof(string), typeof(DataGridElement),
            new PropertyMetadata("Last7Days", OnHistoryFilterChanged));

        /// <summary>每页条数（默认 50，1~500）</summary>
        public static readonly DependencyProperty HistoryPageSizeProperty = DependencyProperty.Register(
            nameof(HistoryPageSize), typeof(int), typeof(DataGridElement),
            new FrameworkPropertyMetadata(50, null, CoerceHistoryPageSize));

        /// <summary>时间列名（空 = 自动识别表头里含时间/日期的列）</summary>
        public static readonly DependencyProperty HistoryTimestampColumnProperty = DependencyProperty.Register(
            nameof(HistoryTimestampColumn), typeof(string), typeof(DataGridElement),
            new PropertyMetadata(string.Empty, OnHistoryFilterChanged));

        /// <summary>关键字过滤（空 = 不过滤；匹配任意列，忽略大小写）</summary>
        public static readonly DependencyProperty HistoryKeywordProperty = DependencyProperty.Register(
            nameof(HistoryKeyword), typeof(string), typeof(DataGridElement),
            new PropertyMetadata(string.Empty, OnHistoryFilterChanged));

        private static readonly DependencyPropertyKey IsHistoryModePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsHistoryMode), typeof(bool), typeof(DataGridElement), new PropertyMetadata(false));

        /// <summary>当前是否历史源模式（模板用它切工具栏）</summary>
        public static readonly DependencyProperty IsHistoryModeProperty = IsHistoryModePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryToolbarVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryToolbarVisibility), typeof(Visibility), typeof(DataGridElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>历史工具栏显隐</summary>
        public static readonly DependencyProperty HistoryToolbarVisibilityProperty =
            HistoryToolbarVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryBusyVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryBusyVisibility), typeof(Visibility), typeof(DataGridElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>查询中骨架显隐</summary>
        public static readonly DependencyProperty HistoryBusyVisibilityProperty =
            HistoryBusyVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryPageTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryPageText), typeof(string), typeof(DataGridElement), new PropertyMetadata("1 / 1"));

        /// <summary>分页读数（如 <c>2 / 7 · 共 312 条</c>）</summary>
        public static readonly DependencyProperty HistoryPageTextProperty = HistoryPageTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryRangeLabelPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryRangeLabel), typeof(string), typeof(DataGridElement), new PropertyMetadata("最近 7 天"));

        /// <summary>范围按钮上的文字（落盘值 → 中文名，读 <see cref="ScadaChoiceNames"/>）</summary>
        public static readonly DependencyProperty HistoryRangeLabelProperty =
            HistoryRangeLabelPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey TitleCountTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TitleCountText), typeof(string), typeof(DataGridElement), new PropertyMetadata("0 条"));

        /// <summary>
        /// 标题右侧的计数：实时模式是缓冲行数，历史模式是<b>过滤后的总数</b>。
        /// 历史模式若还显示本页行数，会和工具栏的"共 312 条"打架（5 vs 312）。
        /// </summary>
        public static readonly DependencyProperty TitleCountTextProperty = TitleCountTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryBannerVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryBannerVisibility), typeof(Visibility), typeof(DataGridElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>历史查询失败横幅显隐（不可用/超时/范围非法；有旧行时旧行仍在下面）</summary>
        public static readonly DependencyProperty HistoryBannerVisibilityProperty =
            HistoryBannerVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HistoryBannerTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HistoryBannerText), typeof(string), typeof(DataGridElement), new PropertyMetadata(string.Empty));

        /// <summary>横幅文字（失败原因 + 重试提示）</summary>
        public static readonly DependencyProperty HistoryBannerTextProperty =
            HistoryBannerTextPropertyKey.DependencyProperty;

        /// <summary>历史范围下拉的选项（XAML 用 <c>x:Static</c> 取）</summary>
        public static IReadOnlyList<ScadaHistoryRangeOption> HistoryRangeOptions { get; } = new[]
        {
            new ScadaHistoryRangeOption("Today", "今天"),
            new ScadaHistoryRangeOption("Yesterday", "昨天"),
            new ScadaHistoryRangeOption("Last7Days", "最近 7 天"),
            new ScadaHistoryRangeOption("Last30Days", "最近 30 天"),
        };

        static DataGridElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DataGridElement),
                new FrameworkPropertyMetadata(typeof(DataGridElement)));
        }

        /// <summary>
        /// 表头 / 工具栏 / 底栏的底色。null = 用模板默认的深色令牌（保持既有观感）。
        ///
        /// 【为什么需要它】模板里这三条用的是 <c>HmiSurfaceRaisedBrush</c> 深色令牌，
        /// 在浅色主题（白卡）里会变成一条突兀的黑带。控件库不引用主题配置，
        /// 所以把"要不要覆盖"做成可选属性：浅色方案显式给白/浅灰，深色方案不写即维持原样。
        /// 应用时机在 OnApplyTemplate（模板部件此时才存在）。
        /// </summary>
        public static readonly DependencyProperty HeaderBackgroundProperty = DependencyProperty.Register(
            nameof(HeaderBackground), typeof(Brush), typeof(DataGridElement),
            new PropertyMetadata(null, OnHeaderBrushChanged));

        /// <summary>表头 / 工具栏文字颜色。null = 用模板默认令牌色。</summary>
        public static readonly DependencyProperty HeaderForegroundProperty = DependencyProperty.Register(
            nameof(HeaderForeground), typeof(Brush), typeof(DataGridElement),
            new PropertyMetadata(null, OnHeaderBrushChanged));

        /// <summary>
        /// 斑马纹底色。null = 模板默认 <c>HmiZebraBrush</c>（8% 白，深色底上才看得见）。
        /// 浅色主题要显式给一个淡灰，否则斑马纹在白底上等于没有。
        /// </summary>
        public static readonly DependencyProperty ZebraBackgroundProperty = DependencyProperty.Register(
            nameof(ZebraBackground), typeof(Brush), typeof(DataGridElement),
            new PropertyMetadata(null));

        public Brush? HeaderBackground
        {
            get => (Brush?)GetValue(HeaderBackgroundProperty);
            set => SetValue(HeaderBackgroundProperty, value);
        }

        public Brush? HeaderForeground
        {
            get => (Brush?)GetValue(HeaderForegroundProperty);
            set => SetValue(HeaderForegroundProperty, value);
        }

        public Brush? ZebraBackground
        {
            get => (Brush?)GetValue(ZebraBackgroundProperty);
            set => SetValue(ZebraBackgroundProperty, value);
        }

        private static void OnHeaderBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DataGridElement)d).ApplyHeaderBrushes();

        /// <summary>把可选配色写到命名模板部件上；属性为 null 时不碰（保留模板里的令牌色）。</summary>
        private void ApplyHeaderBrushes()
        {
            if (HeaderBackground is { } bg)
            {
                if (GetTemplateChild("PART_HeaderBar") is Border header) header.Background = bg;
                if (GetTemplateChild("PART_ColumnHeader") is Border columns) columns.Background = bg;
                if (GetTemplateChild("PART_HistoryToolbar") is Border toolbar) toolbar.Background = bg;
                if (GetTemplateChild("PART_FooterBar") is Border footer) footer.Background = bg;
            }

            if (HeaderForeground is { } fg)
            {
                if (GetTemplateChild("PART_HeaderText") is TextBlock title) title.Foreground = fg;
                if (GetTemplateChild("PART_HeaderCount") is TextBlock count) count.Foreground = fg;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyHeaderBrushes();
        }

        public DataGridElement()
        {
            // SizeChanged 必须重算列宽：首次布局前 ActualWidth=0，余量列按 480 的保守宽度分，
            // 布局完成后不重算的话，列宽会一直停在"布局前"那一版（SN 列被截成 SN-2026-00…）。
            SizeChanged += (_, _) =>
            {
                RecomputeCellWidths();
                RebuildRowViews();
            };

            _queryHistoryCommand = new ScadaRelayCommand(() => _ = QueryHistoryAsync());
            _refreshHistoryCommand = new ScadaRelayCommand(() => _ = QueryHistoryAsync());
            _cycleHistoryRangeCommand = new ScadaRelayCommand(
                () =>
                {
                    var index = 0;
                    for (var i = 0; i < HistoryRangeOptions.Count; i++)
                    {
                        if (string.Equals(HistoryRangeOptions[i].Value, HistoryRange, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }

                    HistoryRange = HistoryRangeOptions[(index + 1) % HistoryRangeOptions.Count].Value;
                    _ = QueryHistoryAsync();
                });
            _previousHistoryPageCommand = new ScadaRelayCommand(
                () =>
                {
                    if (_historyPageIndex <= 0) return;
                    _historyPageIndex--;
                    _ = QueryHistoryAsync();
                },
                () => !_historyBusy && _historyPageIndex > 0);
            _nextHistoryPageCommand = new ScadaRelayCommand(
                () =>
                {
                    var pageCount = PageCount;
                    if (_historyPageIndex + 1 >= pageCount) return;
                    _historyPageIndex++;
                    _ = QueryHistoryAsync();
                },
                () => !_historyBusy && _historyPageIndex + 1 < PageCount);
        }

        /// <summary>行（视图模型）</summary>
        public IReadOnlyList<GridRowView> Rows => (IReadOnlyList<GridRowView>)GetValue(RowsProperty);

        /// <summary>列头格</summary>
        public IReadOnlyList<GridCellView> HeaderCells => (IReadOnlyList<GridCellView>)GetValue(HeaderCellsProperty);

        /// <summary>脚注状态（空串 = 正常）</summary>
        public string StatusText => (string)GetValue(StatusTextProperty);

        /// <summary>空态显隐</summary>
        public Visibility EmptyVisibility => (Visibility)GetValue(EmptyVisibilityProperty);

        /// <summary>空态标题</summary>
        public string EmptyTitle => (string)GetValue(EmptyTitleProperty);

        /// <summary>空态正文</summary>
        public string EmptyHint => (string)GetValue(EmptyHintProperty);

        /// <summary>列定义（解析自属性袋；只读）</summary>
        public IReadOnlyList<ScadaGridColumn> Columns => _columns;

        /// <summary>当前缓冲里的行（模型快照；断言与宿主用，模板用 <see cref="Rows"/>）</summary>
        public IReadOnlyList<ScadaGridRow> Buffer => _rows;

        /// <summary>已收录行数（单调递增，重启归零）</summary>
        public int RecordedCount => _recorded;

        /// <summary>采行信号变量名</summary>
        public string SampleVariable
        {
            get => (string)GetValue(SampleVariableProperty);
            set => SetValue(SampleVariableProperty, value);
        }

        /// <summary>收录确认变量名（留空 = 不写）</summary>
        public string AckVariable
        {
            get => (string)GetValue(AckVariableProperty);
            set => SetValue(AckVariableProperty, value);
        }

        /// <summary>缓存行数</summary>
        public int MaxRows
        {
            get => (int)GetValue(MaxRowsProperty);
            set => SetValue(MaxRowsProperty, value);
        }

        /// <summary>行高</summary>
        public double RowHeight
        {
            get => (double)GetValue(RowHeightProperty);
            set => SetValue(RowHeightProperty, value);
        }

        /// <summary>新行在上</summary>
        public bool NewestFirst
        {
            get => (bool)GetValue(NewestFirstProperty);
            set => SetValue(NewestFirstProperty, value);
        }

        /// <summary>斑马纹</summary>
        public bool Zebra
        {
            get => (bool)GetValue(ZebraProperty);
            set => SetValue(ZebraProperty, value);
        }

        /// <summary>数据源（Live / History）</summary>
        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>历史范围（Today / Yesterday / Last7Days / Last30Days）</summary>
        public string HistoryRange
        {
            get => (string)GetValue(HistoryRangeProperty);
            set => SetValue(HistoryRangeProperty, value);
        }

        /// <summary>每页条数</summary>
        public int HistoryPageSize
        {
            get => (int)GetValue(HistoryPageSizeProperty);
            set => SetValue(HistoryPageSizeProperty, value);
        }

        /// <summary>时间列名（空 = 自动识别）</summary>
        public string HistoryTimestampColumn
        {
            get => (string)GetValue(HistoryTimestampColumnProperty);
            set => SetValue(HistoryTimestampColumnProperty, value);
        }

        /// <summary>关键字过滤</summary>
        public string HistoryKeyword
        {
            get => (string)GetValue(HistoryKeywordProperty);
            set => SetValue(HistoryKeywordProperty, value);
        }

        /// <summary>当前是否历史源模式</summary>
        public bool IsHistoryMode => (bool)GetValue(IsHistoryModeProperty);

        /// <summary>历史工具栏显隐</summary>
        public Visibility HistoryToolbarVisibility => (Visibility)GetValue(HistoryToolbarVisibilityProperty);

        /// <summary>查询中骨架显隐</summary>
        public Visibility HistoryBusyVisibility => (Visibility)GetValue(HistoryBusyVisibilityProperty);

        /// <summary>分页读数</summary>
        public string HistoryPageText => (string)GetValue(HistoryPageTextProperty);

        /// <summary>范围按钮文字</summary>
        public string HistoryRangeLabel => (string)GetValue(HistoryRangeLabelProperty);

        /// <summary>标题右侧计数（历史模式 = 总数）</summary>
        public string TitleCountText => (string)GetValue(TitleCountTextProperty);

        /// <summary>历史失败横幅显隐</summary>
        public Visibility HistoryBannerVisibility => (Visibility)GetValue(HistoryBannerVisibilityProperty);

        /// <summary>历史失败横幅文字</summary>
        public string HistoryBannerText => (string)GetValue(HistoryBannerTextProperty);

        /// <summary>当前页码（0 基；断言用）</summary>
        public int HistoryPageIndex => _historyPageIndex;

        /// <summary>过滤后的总条数（断言用）</summary>
        public int HistoryTotalCount => _historyTotal;

        /// <summary>查询中（断言用）</summary>
        public bool IsHistoryBusy => _historyBusy;

        /// <summary>查询按钮</summary>
        public ICommand QueryHistoryCommand => _queryHistoryCommand;

        /// <summary>刷新按钮（语义与查询相同；分开是为了工具栏上两个入口各自可点）</summary>
        public ICommand RefreshHistoryCommand => _refreshHistoryCommand;

        /// <summary>上一页</summary>
        public ICommand PreviousHistoryPageCommand => _previousHistoryPageCommand;

        /// <summary>下一页</summary>
        public ICommand NextHistoryPageCommand => _nextHistoryPageCommand;

        /// <summary>范围按钮（点击循环：今天 → 昨天 → 最近 7 天 → 最近 30 天）</summary>
        public ICommand CycleHistoryRangeCommand => _cycleHistoryRangeCommand;

        private int PageCount
            => _historyTotal <= 0 ? 0 : (_historyTotal + _historyPageSize - 1) / _historyPageSize;

        private static object CoerceMaxRows(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;
            if (value < 1) return 1;
            return value > MaxRowsLimit ? MaxRowsLimit : value;
        }

        private static object CoerceHistoryPageSize(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;
            if (value < 1) return 1;
            return value > MaxRowsLimit ? MaxRowsLimit : value;
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGridElement grid)
                grid.ApplySourceMode();
        }

        /// <summary>范围/时间列/关键字变了：回到第一页（在第三页改筛选还停在第三页 = 空表）</summary>
        private static void OnHistoryFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGridElement grid)
            {
                grid._historyPageIndex = 0;
                grid.UpdateHistoryRangeLabel();
            }
        }

        /// <summary>
        /// 列定义走"自定义属性"通道（与轴候选同一手法）：它是<b>一条 JSON 摊成多个视觉元素</b>，
        /// 不是一个能直接落到依赖属性上的值。
        /// </summary>
        protected override void ApplyCustomProperty(string key, string value)
        {
            if (string.Equals(key, ScadaGridColumnsCodec.Key, StringComparison.Ordinal))
            {
                _configuredColumns = ScadaGridColumnsCodec.Parse(value);
                _columns = IsHistoryMode ? _columns : _configuredColumns;
                RecomputeCellWidths();
                RebuildRowViews();
                RefreshEmptyState();
                return;
            }

            base.ApplyCustomProperty(key, value);
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            // 换运行上下文 = 换一轮运行：缓冲与边沿状态都归零（上一次运行的记录不该续着）
            _signalHigh = false;
            _recorded = 0;
            _rows.Clear();

            // 在飞的历史查询也要停：新上下文的数据源可能已经换了一份。
            _historyCts?.Cancel();
            _historyCts = null;
            _historyPageIndex = 0;
            _historyTotal = 0;

            ApplySourceMode(queryIfHistory: false);
            RebuildRowViews();
            RefreshEmptyState();

            // 历史模式：进入运行就查一次（"先看到最近记录"是打开画面的默认预期）。
            if (IsHistoryMode && newContext?.History != null)
                _ = QueryHistoryAsync();
        }

        /// <summary>
        /// 一拍：读采行信号 → 判上升沿 → 同一拍逐列取值 → 攒行 → 写确认。
        ///
        /// 只在 UI 线程上跑（节拍源由宿主在运行窗口的 Dispatcher 上养）。
        /// 没有任何变量句柄时安静返回：设计态与"还没配变量"都是正常状态。
        /// 历史模式不采行：那张表的数据来自查询，采行信号在这里没有意义。
        /// </summary>
        private void OnBeat(TimeSpan elapsed)
        {
            if (IsHistoryMode)
                return;

            Sample();
        }

        /// <summary>
        /// 采一次（节拍每拍调一次）。
        ///
        /// <b>public 是给断言用的</b>：边沿判定与攒行是这张表最容易错的两处，
        /// 而它们全在"一拍"里；让断言直接推一拍，比在断言宿主里跑一个真定时器可靠得多
        /// （与 <c>FormatRunBarFlowText</c> 做成纯函数同一口径）。生产路径只有节拍调它。
        /// </summary>
        public void Sample()
        {
            if (RuntimeContext?.Values is not { } values)
                return;

            if (_columns.Count == 0)
            {
                RefreshEmptyState();
                return;
            }

            // 句柄每拍现解析：变量可能这一秒才被建出来、也可能刚被删（解析是字典查找，代价可忽略）。
            // 缓存反而要处理"何时失效"，而这里一拍最多十几次查找。
            if (!TryResolve(values, SampleVariable, out var signalHandle))
            {
                _signalHigh = false;
                SetStatus(string.IsNullOrWhiteSpace(SampleVariable)
                    ? string.Empty
                    : $"采行信号「{SampleVariable}」不存在");
                RefreshEmptyState();
                return;
            }

            bool high = ToBool(signalHandle!.Value);
            bool rising = high && !_signalHigh;
            _signalHigh = high;

            if (!rising)
            {
                RefreshEmptyState();
                return;
            }

            var (row, errorText) = CaptureRow(values);
            AppendRow(row);
            SetStatus(errorText);
            WriteAck(values);
            RefreshEmptyState();
        }

        /// <summary>
        /// 采一行：<b>同一拍里</b>把所有列读一遍再格式化。
        ///
        /// 这是"同一拍快照"的落点：分两拍读会攒出"SN 是上一个产品、角度是下一个产品"的脏行
        /// （设计基线 §记录与确认的边界）。
        /// </summary>
        private (ScadaGridRow Row, string? Error) CaptureRow(IScadaValueSource values)
        {
            var cells = new string[_columns.Count];
            int errorCell = -1;
            string? firstError = null;

            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];

                if (!TryResolve(values, column.Variable, out var handle))
                {
                    cells[i] = "—";
                    if (errorCell < 0)
                    {
                        errorCell = i;
                        firstError = $"{column.Header} → {column.Variable} 不存在";
                    }
                    continue;
                }

                cells[i] = FormatCell(handle!.Value, column);
            }

            var row = new ScadaGridRow(cells, errorCell, DateTime.Now);
            return (row, firstError is null ? null : $"未解析：{firstError}");
        }

        private static string FormatCell(object? value, ScadaGridColumn column)
        {
            if (value is null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(column.Format) && value is IFormattable formattable)
            {
                try
                {
                    return formattable.ToString(column.Format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    // 格式串写错不该让这一行消失：退回默认 ToString，并在脚注里留痕（见 SetStatus 的调用方）
                }
            }

            return value is IFormattable f
                ? f.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty;
        }

        /// <summary>攒行：按"新行在上/在下"插入，超过缓存上限丢最旧的一行。</summary>
        private void AppendRow(ScadaGridRow row)
        {
            if (NewestFirst)
                _rows.Insert(0, row);
            else
                _rows.Add(row);

            int max = MaxRows;
            if (_rows.Count > max)
                _rows.RemoveRange(NewestFirst ? max : 0, _rows.Count - max);

            _recorded++;
            RebuildRowViews();
        }

        /// <summary>
        /// 写收录确认变量（可空配置）。
        ///
        /// <b>写的是累加计数而不是脉冲</b>：脉冲总得有人复位，那就又把"读"变成"写"了；
        /// 计数是自洽的——PLC 判"变了"即可，人也能看出"一共记了多少条"。
        /// 写失败只记状态、<b>不回滚已入表的行</b>：行是真采到的，边沿判定保证不会重复采。
        /// </summary>
        private void WriteAck(IScadaValueSource values)
        {
            if (string.IsNullOrWhiteSpace(AckVariable))
                return;

            if (!TryResolve(values, AckVariable, out var handle))
            {
                SetStatus($"收录确认变量「{AckVariable}」不存在");
                return;
            }

            long current = ToLong(handle!.Value);
            long next = current + 1;

            if (!handle.TryWrite(next, out var error))
                SetStatus($"已收录 {_recorded} 行，但确认变量写入失败：{error}");
        }

        private static bool TryResolve(IScadaValueSource values, string? name, out IScadaValueHandle? handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return values.TryResolve(Guid.Empty, name!.Trim(), out handle) && handle != null;
        }

        private static bool ToBool(object? value) => value switch
        {
            null => false,
            bool b => b,
            string s => bool.TryParse(s.Trim(), out var parsed) && parsed,
            IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture) != 0d,
            _ => false,
        };

        private static long ToLong(object? value)
        {
            try
            {
                return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return 0;
            }
        }

        /// <summary>重算每列宽度：定宽照给，<c>0</c> 的列平分剩余宽度。</summary>
        private void RecomputeCellWidths()
        {
            int count = _columns.Count;
            var widths = new double[count];
            if (count == 0)
            {
                _cellWidths = widths;
                return;
            }

            const double padding = 20d;   // 左右各 10（与模板的 Margin 一致）

            double fixedSum = 0;
            int flexCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (_columns[i].Width > 0)
                {
                    widths[i] = _columns[i].Width;
                    fixedSum += widths[i];
                }
                else
                {
                    flexCount++;
                }
            }

            double available = ActualWidth > 0 ? ActualWidth - padding : 0;
            if (available <= 0)
            {
                // 还没布局：给个保守宽度，SizeChanged 之后会重算
                available = Math.Max(0, 480 - padding);
            }

            if (flexCount > 0)
            {
                double share = Math.Max(ScadaGridColumnsCodec.MinWidth, (available - fixedSum) / flexCount);
                for (int i = 0; i < count; i++)
                {
                    if (_columns[i].Width <= 0) widths[i] = share;
                }
            }

            _cellWidths = widths;
        }

        /// <summary>把模型行摊成视图行（列宽/对齐在这里落地，模板只绑格）。</summary>
        private void RebuildRowViews()
        {
            if (_columns.Count != _cellWidths.Length)
                RecomputeCellWidths();

            var views = new List<GridRowView>(_rows.Count);

            foreach (var row in _rows)
            {
                var cells = new GridCellView[_columns.Count];

                for (int i = 0; i < _columns.Count; i++)
                {
                    var column = _columns[i];
                    string text = i < row.Cells.Count ? row.Cells[i] : string.Empty;

                    cells[i] = new GridCellView(
                        text,
                        i < _cellWidths.Length ? _cellWidths[i] : column.Width,
                        ToTextAlignment(column.Align),
                        column.Monospace,
                        row.ErrorCell == i);
                }

                views.Add(new GridRowView(cells));
            }

            SetValue(RowsPropertyKey, (IReadOnlyList<GridRowView>)views);
            RebuildHeaderCells();
            UpdateTitleCount();
        }

        /// <summary>列头格：与行同宽同对齐（同一个格子模板渲染，列线才在一条竖线上）。</summary>
        private void RebuildHeaderCells()
        {
            var cells = new GridCellView[_columns.Count];

            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];

                cells[i] = new GridCellView(
                    column.Header,
                    i < _cellWidths.Length ? _cellWidths[i] : column.Width,
                    ToTextAlignment(column.Align),
                    false,
                    false);
            }

            SetValue(HeaderCellsPropertyKey, (IReadOnlyList<GridCellView>)cells);
        }

        private static TextAlignment ToTextAlignment(string? align) => align switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

        private void SetStatus(string text) => SetValue(StatusTextPropertyKey, text ?? string.Empty);

        // ------------------------------------------------------------------
        // 历史源模式
        // ------------------------------------------------------------------

        /// <summary>
        /// 按 <see cref="Source"/> 切模式：实时 → 历史（清缓冲、换列、提示先查询）；
        /// 历史 → 实时（把列换回属性袋里那一份）。
        /// </summary>
        private void ApplySourceMode() => ApplySourceMode(queryIfHistory: true);

        private void ApplySourceMode(bool queryIfHistory)
        {
            var history = string.Equals(Source, "History", StringComparison.OrdinalIgnoreCase);

            SetValue(IsHistoryModePropertyKey, history);
            SetValue(HistoryToolbarVisibilityPropertyKey, history ? Visibility.Visible : Visibility.Collapsed);
            HideHistoryBanner();
            UpdateHistoryRangeLabel();

            if (!history)
            {
                _historyCts?.Cancel();
                _historyCts = null;
                _historyBusy = false;
                SetValue(HistoryBusyVisibilityPropertyKey, Visibility.Collapsed);

                // 历史行不属于实时缓冲：切回来还留着，操作员会以为那是刚采到的产品。
                _rows.Clear();
                _columns = _configuredColumns;
                RecomputeCellWidths();
                RebuildRowViews();
                RefreshEmptyState();
                UpdateHistoryPagingUi();
                return;
            }

            // 进历史模式：实时缓冲不属于这里（否则会先显示上一轮的采行记录，
            // 用户以为那就是查询结果）。
            _rows.Clear();
            _signalHigh = false;
            _columns = Array.Empty<ScadaGridColumn>();
            RecomputeCellWidths();
            RebuildRowViews();
            SetValue(EmptyVisibilityPropertyKey, Visibility.Visible);
            SetValue(EmptyTitlePropertyKey, "选择范围后点「查询」");
            SetValue(EmptyHintPropertyKey, "历史记录从方案目录 History\\ 下的 CSV 账本读取。");
            SetStatus(string.Empty);
            UpdateHistoryPagingUi();

            if (queryIfHistory && RuntimeContext?.History != null)
                _ = QueryHistoryAsync();
        }

        /// <summary>
        /// 查一页历史。
        ///
        /// <b>public 是给断言用的</b>（与 <see cref="Sample"/> 同一口径）：真实查询是异步的，
        /// 断言用同步完成的假数据源 await 它，就能在无 Dispatcher 的宿主里验完整个状态机。
        /// 生产路径只有命令调它。
        /// </summary>
        public async Task QueryHistoryAsync()
        {
            if (!IsHistoryMode)
                return;

            var source = RuntimeContext?.History;
            if (source == null)
            {
                ShowHistoryProblem("未接入历史数据源", "宿主没有提供 IScadaHistorySource。");
                return;
            }

            if (!source.IsAvailable)
            {
                ShowHistoryProblem("历史数据源不可用", source.UnavailableReason ?? "目录不存在或不可访问。");
                return;
            }

            var (from, to) = ResolveHistoryRange(HistoryRange, DateTime.Now);
            if (from >= to)
            {
                ShowHistoryProblem("时间范围无效", "起点必须早于终点。");
                return;
            }

            // 新查询取代旧查询：旧请求取消，结果回来时也不再写界面（见下面的引用判断）。
            _historyCts?.Cancel();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _historyCts = cts;

            _historyBusy = true;
            HideHistoryBanner();
            SetValue(HistoryBusyVisibilityPropertyKey, Visibility.Visible);
            SetValue(EmptyVisibilityPropertyKey, Visibility.Collapsed);
            SetStatus("正在查询…");
            CommandManager.InvalidateRequerySuggested();

            try
            {
                var result = await source.QueryAsync(
                    new ScadaHistoryQuery
                    {
                        FromLocal = from,
                        ToLocal = to,
                        PageIndex = _historyPageIndex,
                        PageSize = HistoryPageSize,
                        TimestampColumn = HistoryTimestampColumn,
                        Keyword = HistoryKeyword,
                    },
                    cts.Token);

                if (!ReferenceEquals(_historyCts, cts) || cts.IsCancellationRequested)
                    return;

                ApplyHistoryResult(result);
            }
            catch (OperationCanceledException)
            {
                // 被新查询取代：新查询会自己更新界面。
            }
            catch (Exception ex)
            {
                ShowHistoryProblem("历史查询失败", ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_historyCts, cts))
                {
                    _historyCts = null;
                    _historyBusy = false;
                    SetValue(HistoryBusyVisibilityPropertyKey, Visibility.Collapsed);
                    RefreshEmptyState();
                    CommandManager.InvalidateRequerySuggested();
                }

                cts.Dispose();
            }
        }

        private void ApplyHistoryResult(ScadaHistoryResult result)
        {
            switch (result.Status)
            {
                case ScadaHistoryStatus.Ok:
                {
                    _historyTotal = result.TotalCount;
                    _historyPageIndex = result.PageIndex;
                    _historyPageSize = Math.Max(1, result.PageSize);

                    ApplyHistoryColumns(result.Columns);

                    _rows.Clear();
                    foreach (var cells in result.Rows)
                        _rows.Add(new ScadaGridRow(cells, -1, DateTime.Now));

                    RecomputeCellWidths();
                    RebuildRowViews();

                    HideHistoryBanner();
                    SetValue(EmptyVisibilityPropertyKey, _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed);
                    if (_rows.Count == 0)
                    {
                        SetValue(EmptyTitlePropertyKey, "该页没有记录");
                        SetValue(EmptyHintPropertyKey, "总数不为 0 但本页为空：换一页看看。");
                    }

                    SetStatus(BuildHistoryStatus(result));
                    break;
                }

                case ScadaHistoryStatus.Empty:
                {
                    _historyTotal = 0;
                    _historyPageIndex = result.PageIndex;
                    _historyPageSize = Math.Max(1, result.PageSize);

                    ApplyHistoryColumns(result.Columns);
                    _rows.Clear();
                    RecomputeCellWidths();
                    RebuildRowViews();

                    ShowHistoryProblem(
                        "该范围没有记录",
                        result.Message ?? "换个时间范围，或确认 History 目录里有 CSV 账本。",
                        banner: false);
                    break;
                }

                case ScadaHistoryStatus.Cancelled:
                    // 取消不是错误：界面保持原样（下一次查询会更新）。
                    return;

                default:
                    // Unavailable / InvalidRange / Timeout：不清空上一次结果，
                    // 只把原因写到脚注与空态（有旧行时脚注就是全部提示）。
                    ShowHistoryProblem(HistoryStatusTitle(result.Status), result.Message ?? "历史查询未完成。");
                    break;
            }

            UpdateHistoryPagingUi();
        }

        private static string HistoryStatusTitle(ScadaHistoryStatus status) => status switch
        {
            ScadaHistoryStatus.Unavailable => "历史数据源不可用",
            ScadaHistoryStatus.InvalidRange => "时间范围无效",
            ScadaHistoryStatus.Timeout => "历史查询超时",
            ScadaHistoryStatus.Cancelled => "历史查询已取消",
            _ => "历史查询未完成",
        };

        private static string BuildHistoryStatus(ScadaHistoryResult result)
        {
            var parts = new List<string>(3);
            parts.Add($"共 {result.TotalCount} 条");

            if (result.PageCount > 0)
                parts.Add($"第 {result.PageIndex + 1}/{result.PageCount} 页");

            if (!string.IsNullOrWhiteSpace(result.SourceName))
                parts.Add($"来源：{result.SourceName}");

            var text = string.Join(" · ", parts);
            return string.IsNullOrWhiteSpace(result.Message) ? text : $"{text} · {result.Message}";
        }

        /// <summary>历史列 → 表格列：时间列定宽等宽，其余平分剩余宽度。</summary>
        private void ApplyHistoryColumns(IReadOnlyList<string> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                _columns = Array.Empty<ScadaGridColumn>();
                return;
            }

            var list = new List<ScadaGridColumn>(columns.Count);

            foreach (var header in columns)
            {
                var text = header ?? string.Empty;
                var isTime = LooksLikeTimeColumn(text);

                list.Add(new ScadaGridColumn(
                    header: text,
                    variable: string.Empty,
                    width: isTime ? 148 : 0,
                    align: "left",
                    format: string.Empty,
                    monospace: isTime));
            }

            _columns = list;
        }

        private static bool LooksLikeTimeColumn(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return false;

            return header.Contains("时间", StringComparison.Ordinal)
                || header.Contains("日期", StringComparison.Ordinal)
                || header.Contains("time", StringComparison.OrdinalIgnoreCase)
                || header.Contains("date", StringComparison.OrdinalIgnoreCase);
        }

        private static (DateTime From, DateTime To) ResolveHistoryRange(string? range, DateTime now)
        {
            var today = now.Date;

            return (range ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "today" => (today, today.AddDays(1)),
                "yesterday" => (today.AddDays(-1), today),
                "last30days" => (today.AddDays(-29), today.AddDays(1)),
                _ => (today.AddDays(-6), today.AddDays(1)),
            };
        }

        /// <summary>
        /// 历史侧的失败/空态。有旧行时只写脚注、不遮住旧行（设计基线：不可用"不清空上一次结果"）。
        /// </summary>
        private void ShowHistoryProblem(string title, string hint, bool banner = true)
        {
            SetValue(EmptyTitlePropertyKey, title);
            SetValue(EmptyHintPropertyKey, hint);
            SetValue(EmptyVisibilityPropertyKey, _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed);
            SetStatus(hint);

            SetValue(HistoryBannerVisibilityPropertyKey, banner ? Visibility.Visible : Visibility.Collapsed);
            SetValue(HistoryBannerTextPropertyKey, banner ? $"{title}：{hint}" : string.Empty);
        }

        private void HideHistoryBanner()
        {
            SetValue(HistoryBannerVisibilityPropertyKey, Visibility.Collapsed);
            SetValue(HistoryBannerTextPropertyKey, string.Empty);
        }

        private void UpdateHistoryPagingUi()
        {
            var pageCount = PageCount;
            SetValue(
                HistoryPageTextPropertyKey,
                pageCount <= 0
                    ? "0 / 0"
                    : $"{_historyPageIndex + 1} / {pageCount} · 共 {_historyTotal} 条");

            UpdateTitleCount();
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateHistoryRangeLabel()
        {
            var label = HistoryRange;

            foreach (var option in HistoryRangeOptions)
            {
                if (string.Equals(option.Value, HistoryRange, StringComparison.OrdinalIgnoreCase))
                {
                    label = option.Label;
                    break;
                }
            }

            SetValue(HistoryRangeLabelPropertyKey, label);
        }

        private void UpdateTitleCount()
        {
            SetValue(
                TitleCountTextPropertyKey,
                IsHistoryMode ? $"{_historyTotal} 条" : $"{_rows.Count} 条");
        }

        /// <summary>
        /// 空态：两种成因分开说（没配采行信号 / 配了但还没来第一行）——
        /// 混成一句"暂无数据"会让用户去怀疑表格，而问题在配置。
        /// </summary>
        private void RefreshEmptyState()
        {
            bool empty = _rows.Count == 0;
            SetValue(EmptyVisibilityPropertyKey, empty ? Visibility.Visible : Visibility.Collapsed);

            if (!empty)
                return;

            // 历史模式的文案由查询结果写（空范围 / 不可用 / 未接入各说各的），
            // 这里只负责"有没有行"这一半；查询进行中则由骨架层接管。
            if (IsHistoryMode)
            {
                if (_historyBusy)
                    SetValue(EmptyVisibilityPropertyKey, Visibility.Collapsed);
                return;
            }

            bool configured = !string.IsNullOrWhiteSpace(SampleVariable) && _columns.Count > 0;

            SetValue(EmptyTitlePropertyKey, configured ? "等待第一条记录" : "还没有配置采集");
            SetValue(EmptyHintPropertyKey, configured
                ? "采行信号还没触发。产品过站后会在这里逐条出现。"
                : "在属性面板「数据」里绑定采行信号、在「列」里配置列，表格才会开始记录。");
        }
    }

    /// <summary>
    /// 图元内最小的命令实现（不引 Prism：控件库不该为三个按钮拉一套 MVVM 框架）。
    /// <see cref="CanExecuteChanged"/> 挂 <see cref="CommandManager.RequerySuggested"/>，
    /// 分页按钮的可用态就会跟着界面交互自动重算。
    /// </summary>
    internal sealed class ScadaRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public ScadaRelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
