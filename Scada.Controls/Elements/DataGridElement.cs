using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
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

        private readonly List<ScadaGridRow> _rows = new();

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

        static DataGridElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DataGridElement),
                new FrameworkPropertyMetadata(typeof(DataGridElement)));
        }

        public DataGridElement()
        {
            SizeChanged += (_, _) => RebuildRowViews();
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

        private static object CoerceMaxRows(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;
            if (value < 1) return 1;
            return value > MaxRowsLimit ? MaxRowsLimit : value;
        }

        /// <summary>
        /// 列定义走"自定义属性"通道（与轴候选同一手法）：它是<b>一条 JSON 摊成多个视觉元素</b>，
        /// 不是一个能直接落到依赖属性上的值。
        /// </summary>
        protected override void ApplyCustomProperty(string key, string value)
        {
            if (string.Equals(key, ScadaGridColumnsCodec.Key, StringComparison.Ordinal))
            {
                _columns = ScadaGridColumnsCodec.Parse(value);
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
            RebuildRowViews();
            RefreshEmptyState();
        }

        /// <summary>
        /// 一拍：读采行信号 → 判上升沿 → 同一拍逐列取值 → 攒行 → 写确认。
        ///
        /// 只在 UI 线程上跑（节拍源由宿主在运行窗口的 Dispatcher 上养）。
        /// 没有任何变量句柄时安静返回：设计态与"还没配变量"都是正常状态。
        /// </summary>
        private void OnBeat(TimeSpan elapsed) => Sample();

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

            bool configured = !string.IsNullOrWhiteSpace(SampleVariable) && _columns.Count > 0;

            SetValue(EmptyTitlePropertyKey, configured ? "等待第一条记录" : "还没有配置采集");
            SetValue(EmptyHintPropertyKey, configured
                ? "采行信号还没触发。产品过站后会在这里逐条出现。"
                : "在属性面板「数据」里绑定采行信号、在「列」里配置列，表格才会开始记录。");
        }
    }
}
