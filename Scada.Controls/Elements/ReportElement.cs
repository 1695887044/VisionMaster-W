using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Prism.Commands;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报表图元（TypeKey = <c>Hmi.Report</c>）：把 CSV 账本按时间范围取出来，
    /// 明细逐行看 / 汇总按班次分组算，并支持导出与打印。
    ///
    /// 与「数据表格」的分工
    /// ---------
    /// 数据表格是<b>实时显示缓冲</b>（最近 N 行、切页即失）；报表是<b>按范围取数 + 聚合 + 出纸</b>。
    /// 两者共用同一批 CSV 账本，但查询契约不同（见 <see cref="IScadaReportSource"/>）——
    /// 历史查询只给"当前页的字符串行"，报表要"整段范围聚合"。
    ///
    /// 三条纪律
    /// ---------
    /// ① <b>查询可取消</b>：切页/改范围/关窗时旧查询要停（`_querySeq` 让过期结果不覆盖新结果）。
    /// ② <b>导出/打印是"把数据带出画面"</b>：权限在<b>按下前</b>判（`CanExecute`），
    ///    成功后写审计（谁、什么范围、多少行、写到哪）。
    /// ③ <b>设计期不查数据</b>：只按配置列画空表 + 一句提示，避免在设计器里点出一个文件对话框。
    ///
    /// 分页
    /// ---------
    /// 明细模式在<b>客户端</b>分页（<see cref="PageSize"/> 行/页）：一次查询最多拿
    /// <see cref="MaxRows"/> 行，但界面只实例化一页的单元格——5000 行全铺出来会把 UI 卡住。
    /// 导出与打印用的是<b>整份结果</b>，不是当前页。
    /// </summary>
    public partial class ReportElement : ScadaElementBase
    {
        /// <summary>
        /// 默认样式键钉到本类型（R73 同族，2026-10-25 修）：不覆盖就继承基类样式键，
        /// 主题里按本类型键的那份模板永远匹配不上，画布上是一个空壳（拖上去看不见）。
        /// 断言里显式赋 Style 绕过了这条查找，所以绿过——见 PipeRunElement 同名注释。
        /// </summary>
        static ReportElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ReportElement),
                new FrameworkPropertyMetadata(typeof(ReportElement)));
        }

        /// <summary>
        /// 标题条 / 列头 / 工具条的底色。null = 用模板默认的深色令牌（保持既有观感）。
        /// 理由与 DataGridElement.HeaderBackground 相同：浅色主题（白卡）里那条深色带显得突兀，
        /// 而控件库不引用主题配置，故做成可选覆盖——不设就零回归。
        /// </summary>
        public static readonly DependencyProperty HeaderBackgroundProperty = DependencyProperty.Register(
            nameof(HeaderBackground), typeof(Brush), typeof(ReportElement),
            new PropertyMetadata(null, OnHeaderBrushChanged));

        /// <summary>标题条文字颜色。null = 用模板默认令牌色。</summary>
        public static readonly DependencyProperty HeaderForegroundProperty = DependencyProperty.Register(
            nameof(HeaderForeground), typeof(Brush), typeof(ReportElement),
            new PropertyMetadata(null, OnHeaderBrushChanged));

        /// <summary>
        /// 斑马纹底色。null = 模板默认 <c>HmiZebraBrush</c>（8% 白，深色底上才看得见）。
        /// 浅色主题要显式给一个淡灰，否则斑马纹在白底上等于没有。
        /// </summary>
        public static readonly DependencyProperty ZebraBackgroundProperty = DependencyProperty.Register(
            nameof(ZebraBackground), typeof(Brush), typeof(ReportElement),
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
            => ((ReportElement)d).ApplyHeaderBrushes();

        /// <summary>把可选配色写到命名模板部件上；属性为 null 时不碰（保留模板里的令牌色）。</summary>
        private void ApplyHeaderBrushes()
        {
            if (HeaderBackground is { } bg)
            {
                if (GetTemplateChild("PART_HeaderBar") is Border header) header.Background = bg;
                if (GetTemplateChild("PART_ColumnHeader") is Border columns) columns.Background = bg;
                if (GetTemplateChild("PART_Toolbar") is Border toolbar) toolbar.Background = bg;
            }

            if (HeaderForeground is { } fg && GetTemplateChild("PART_HeaderText") is TextBlock title)
                title.Foreground = fg;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyHeaderBrushes();
        }

        /// <summary>每页行数下限</summary>
        public const int MinPageSize = 20;

        /// <summary>每页行数上限</summary>
        public const int MaxPageSize = 1000;

        /// <summary>默认每页行数</summary>
        public const int DefaultPageSize = 200;

        /// <summary>默认明细行数上限（与数据源缺省一致）</summary>
        public const int DefaultMaxRows = 5000;

        // ---- 配置属性（声明次序 = 属性面板行序） ----

        /// <summary>明细 / 汇总</summary>
        [ScadaProperty(Key = "Mode", DisplayName = "报表模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Detail",
                       Choices = new[] { "Detail", "Summary" },
                       Description = "Detail = 逐行明细（可翻页）；Summary = 按分组聚合（班次/日/小时）")]
        public partial string? Mode { get; set; }

        /// <summary>范围起点</summary>
        [ScadaProperty(Key = "From", DisplayName = "起点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "-1d", IsBindable = false,
                       Description = "now / -1d / -8h / 08:00（今天）/ 2026-10-25 08:00")]
        public partial string? From { get; set; }

        /// <summary>范围终点（不含）</summary>
        [ScadaProperty(Key = "To", DisplayName = "终点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "now", IsBindable = false,
                       Description = "now / -0h / 20:00（今天）/ 2026-10-25 20:00；终点不含")]
        public partial string? To { get; set; }

        /// <summary>汇总分组</summary>
        [ScadaProperty(Key = "Grouping", DisplayName = "分组", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "None",
                       Choices = new[] { "None", "Shift", "Day", "Hour" },
                       Description = "仅汇总模式有效：班次 / 日 / 小时；明细模式忽略")]
        public partial string? Grouping { get; set; }

        /// <summary>班次起点</summary>
        [ScadaProperty(Key = "ShiftStarts", DisplayName = "班次起点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "08:00,20:00", IsBindable = false,
                       Description = "逗号分隔的班次开始时刻，如 08:00,20:00（两班）；跨零点的班自动回绕")]
        public partial string? ShiftStarts { get; set; }

        /// <summary>关键字过滤</summary>
        [ScadaProperty(Key = "Keyword", DisplayName = "关键字", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "匹配任意列（忽略大小写）；留空不过滤")]
        public partial string? Keyword { get; set; }

        /// <summary>列定义（JSON，由「配置列…」弹窗编辑）</summary>
        [ScadaProperty(Key = ScadaReportCodec.Key, DisplayName = "列", Kind = ElementPropertyKind.ReportColumnList,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "来源列（CSV 表头）/ 报表列名 / 数值格式 / 聚合方式 / 列宽")]
        public partial string? ColumnsJson { get; set; }

        /// <summary>进入运行态自动查一次</summary>
        [ScadaProperty(Key = "AutoQuery", DisplayName = "自动查询", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "打开画面/进入运行态时自动查一次；关掉后只能按「查询」")]
        public partial bool AutoQuery { get; set; }

        /// <summary>明细行数上限</summary>
        [ScadaProperty(Key = "MaxRows", DisplayName = "最多行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "5000", Min = 50, Max = 200000,
                       Description = "明细模式最多取多少行（超出截断并在页脚说明）")]
        public partial int MaxRows { get; set; }

        /// <summary>每页行数</summary>
        [ScadaProperty(Key = "PageSize", DisplayName = "每页行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "200", Min = 20, Max = 1000,
                       Description = "界面一页显示多少行（导出/打印用整份结果，不受它限制）")]
        public partial int PageSize { get; set; }

        /// <summary>允许导出</summary>
        [ScadaProperty(Key = "AllowExport", DisplayName = "允许导出", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Interaction, DefaultValue = "True",
                       Description = "关掉后导出按钮整体置灰；仍需满足「操作权限」")]
        public partial bool AllowExport { get; set; }

        /// <summary>允许打印</summary>
        [ScadaProperty(Key = "AllowPrint", DisplayName = "允许打印", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Interaction, DefaultValue = "True",
                       Description = "关掉后打印按钮整体置灰；仍需满足「操作权限」")]
        public partial bool AllowPrint { get; set; }

        /// <summary>显示工具条</summary>
        [ScadaProperty(Key = "ShowToolbar", DisplayName = "显示工具条", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Interaction, DefaultValue = "True",
                       Description = "关掉后只剩表格（查询/翻页/导出/打印全收起，适合只读大屏）")]
        public partial bool ShowToolbar { get; set; }

        /// <summary>纸张方向</summary>
        [ScadaProperty(Key = "PageOrientation", DisplayName = "纸张方向", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Interaction, DefaultValue = "Portrait",
                       Choices = new[] { "Portrait", "Landscape" },
                       Description = "打印用 A4；列多的报表选横向")]
        public partial string? PageOrientation { get; set; }

        /// <summary>斑马纹</summary>
        [ScadaProperty(Key = "Zebra", DisplayName = "斑马纹", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
                       Description = "隔行浅底；行高密时更容易横向对齐")]
        public partial bool Zebra { get; set; }

        // ---- 只读投影（模板直绑） ----

        private static readonly DependencyPropertyKey HeaderCellsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HeaderCells), typeof(IReadOnlyList<ReportCellView>), typeof(ReportElement),
            new PropertyMetadata(Array.Empty<ReportCellView>()));

        public static readonly DependencyProperty HeaderCellsProperty = HeaderCellsPropertyKey.DependencyProperty;

        /// <summary>表头格</summary>
        public IReadOnlyList<ReportCellView> HeaderCells => (IReadOnlyList<ReportCellView>)GetValue(HeaderCellsProperty);

        private static readonly DependencyPropertyKey RowViewsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(RowViews), typeof(IReadOnlyList<ReportRowView>), typeof(ReportElement),
            new PropertyMetadata(Array.Empty<ReportRowView>()));

        public static readonly DependencyProperty RowViewsProperty = RowViewsPropertyKey.DependencyProperty;

        /// <summary>当前页的行</summary>
        public IReadOnlyList<ReportRowView> RowViews => (IReadOnlyList<ReportRowView>)GetValue(RowViewsProperty);

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(ReportElement), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        /// <summary>状态行（五态文案：查询中/无记录/未接入/范围非法/空）</summary>
        public string StatusText => (string)GetValue(StatusTextProperty);

        private static readonly DependencyPropertyKey TitleMetaPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TitleMeta), typeof(string), typeof(ReportElement), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TitleMetaProperty = TitleMetaPropertyKey.DependencyProperty;

        /// <summary>标题右侧的范围/行数摘要</summary>
        public string TitleMeta => (string)GetValue(TitleMetaProperty);

        private static readonly DependencyPropertyKey FooterTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FooterText), typeof(string), typeof(ReportElement), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FooterTextProperty = FooterTextPropertyKey.DependencyProperty;

        /// <summary>页脚（来源/扫描行数/生成时间）</summary>
        public string FooterText => (string)GetValue(FooterTextProperty);

        private static readonly DependencyPropertyKey PageTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PageText), typeof(string), typeof(ReportElement), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PageTextProperty = PageTextPropertyKey.DependencyProperty;

        /// <summary>页码文本（1/3）</summary>
        public string PageText => (string)GetValue(PageTextProperty);

        private static readonly DependencyPropertyKey HasRowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasRows), typeof(bool), typeof(ReportElement), new PropertyMetadata(false));

        public static readonly DependencyProperty HasRowsProperty = HasRowsPropertyKey.DependencyProperty;

        /// <summary>当前页有没有行（模板据此在"表格"与"状态提示"之间切换）</summary>
        public bool HasRows => (bool)GetValue(HasRowsProperty);

        private static readonly DependencyPropertyKey IsBusyPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsBusy), typeof(bool), typeof(ReportElement), new PropertyMetadata(false));

        public static readonly DependencyProperty IsBusyProperty = IsBusyPropertyKey.DependencyProperty;

        /// <summary>正在查询（按钮置灰、状态行显示"查询中…"）</summary>
        public bool IsBusy => (bool)GetValue(IsBusyProperty);

        // ---- 可见性投影（模板直绑） ----
        //
        // 为什么用 Visibility 属性而不是模板里的 DataTrigger：本环境下 ControlTemplate.Triggers
        // 里的 DataTrigger 不触发（见 ImageViewElement / PictureElement 的同一条注释），
        // 写了等于没写；Visibility 是普通绑定，一定生效。

        private static readonly DependencyPropertyKey ToolbarVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ToolbarVisibility), typeof(Visibility), typeof(ReportElement),
            new PropertyMetadata(Visibility.Visible));

        public static readonly DependencyProperty ToolbarVisibilityProperty = ToolbarVisibilityPropertyKey.DependencyProperty;

        /// <summary>工具条显隐（配置「显示工具条」）</summary>
        public Visibility ToolbarVisibility => (Visibility)GetValue(ToolbarVisibilityProperty);

        private static readonly DependencyPropertyKey StatusVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusVisibility), typeof(Visibility), typeof(ReportElement),
            new PropertyMetadata(Visibility.Visible));

        public static readonly DependencyProperty StatusVisibilityProperty = StatusVisibilityPropertyKey.DependencyProperty;

        /// <summary>状态提示显隐（没有行时显示）</summary>
        public Visibility StatusVisibility => (Visibility)GetValue(StatusVisibilityProperty);

        private static readonly DependencyPropertyKey BusyVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BusyVisibility), typeof(Visibility), typeof(ReportElement),
            new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty BusyVisibilityProperty = BusyVisibilityPropertyKey.DependencyProperty;

        /// <summary>查询中骨架层显隐</summary>
        public Visibility BusyVisibility => (Visibility)GetValue(BusyVisibilityProperty);

        private static readonly DependencyPropertyKey HeaderVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HeaderVisibility), typeof(Visibility), typeof(ReportElement),
            new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty HeaderVisibilityProperty = HeaderVisibilityPropertyKey.DependencyProperty;

        /// <summary>列头显隐（没配列时整行收起）</summary>
        public Visibility HeaderVisibility => (Visibility)GetValue(HeaderVisibilityProperty);

        // ---- 命令 ----

        public DelegateCommand QueryCommand { get; }
        public DelegateCommand PreviousPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        public DelegateCommand ExportXlsxCommand { get; }
        public DelegateCommand ExportCsvCommand { get; }
        public DelegateCommand PrintCommand { get; }

        // ---- 运行状态 ----

        private ScadaReportResult? _result;
        private int _pageIndex;
        private CancellationTokenSource? _cts;
        private int _querySeq;
        private double _layoutWidth = -1;

        public ReportElement()
        {
            QueryCommand = new DelegateCommand(() => _ = QueryAsync(), () => CanQuery);
            PreviousPageCommand = new DelegateCommand(GoPreviousPage, () => _pageIndex > 0);
            NextPageCommand = new DelegateCommand(GoNextPage, () => _pageIndex + 1 < PageCount);
            ExportXlsxCommand = new DelegateCommand(() => Export("xlsx"), () => CanExport);
            ExportCsvCommand = new DelegateCommand(() => Export("csv"), () => CanExport);
            PrintCommand = new DelegateCommand(Print, () => CanPrint);

            Loaded += (_, _) =>
            {
                if (AutoQuery && RuntimeContext != null && _result == null)
                    _ = QueryAsync();
            };

            SetValue(ToolbarVisibilityPropertyKey, ShowToolbar ? Visibility.Visible : Visibility.Collapsed);
        }

        partial void OnShowToolbarPropertyChanged()
            => SetValue(ToolbarVisibilityPropertyKey, ShowToolbar ? Visibility.Visible : Visibility.Collapsed);

        /// <summary>最近一次查询结果（只读；导出/打印/断言用）</summary>
        public ScadaReportResult? Result => _result;

        /// <summary>当前页码（0 基）</summary>
        public int PageIndex => _pageIndex;

        /// <summary>总页数（按 <see cref="PageSize"/> 切；无结果时 0）</summary>
        public int PageCount
        {
            get
            {
                if (_result == null || _result.Rows.Count == 0)
                    return 0;

                var size = EffectivePageSize;
                return (_result.Rows.Count + size - 1) / size;
            }
        }

        private int EffectivePageSize => Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, MinPageSize, MaxPageSize);

        private bool IsRuntime => RuntimeContext?.Reports != null;

        private bool CanQuery => IsRuntime && !IsBusy;

        private bool HasResultRows => _result is { Rows.Count: > 0 };

        private bool CanExport
            => AllowExport && HasResultRows && RuntimeContext?.ReportExporter != null && HasOperateRight(out _);

        private bool CanPrint
            => AllowPrint && HasResultRows && HasOperateRight(out _);

        /// <summary>权限：按图元的「操作权限」判（导出/打印是把数据带出画面，不能只判登录）</summary>
        private bool HasOperateRight(out string? reason)
        {
            reason = null;
            var policy = RuntimeContext?.AccessPolicy;
            if (policy == null)
                return true; // 设计期/无上下文：命令本身也会因无数据而禁用

            var key = Element.RequiredPermissionOf();
            if (string.IsNullOrWhiteSpace(key))
            {
                reason = null;
                return true;
            }
            return policy.Can(key!, out reason);
        }

        // ---- 生命周期 ----

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);

            _cts?.Cancel();
            _result = null;
            _pageIndex = 0;

            if (newContext == null)
            {
                SetValue(StatusTextPropertyKey, "运行态显示报表（设计期不查询）");
                RebuildHeader();
                RebuildRows();
                RefreshCommands();
                return;
            }

            if (AutoQuery && IsLoaded)
                _ = QueryAsync();
            else
                SetValue(StatusTextPropertyKey, "点「查询」取数");
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();

            // 配置变了（列/模式/范围）：旧结果已经对不上，清掉并提示重查。
            // 不在 Refresh 里自动查：拖动图元也会走 Refresh，那会把查询变成拖一下查一次。
            _result = null;
            _pageIndex = 0;
            SetValue(StatusTextPropertyKey, RuntimeContext == null ? "运行态显示报表（设计期不查询）" : "配置已改，点「查询」重新取数");
            RebuildHeader();
            RebuildRows();
            RefreshCommands();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            // 列宽按可用宽度算：宽度变了才重建（差 1px 就重建会拖慢拖动）
            var width = ActualWidth;
            if (Math.Abs(width - _layoutWidth) < 1)
                return;

            _layoutWidth = width;
            RebuildHeader();
            RebuildRows();
        }

        // ---- 查询 ----

        private async System.Threading.Tasks.Task QueryAsync()
        {
            var context = RuntimeContext;
            if (context?.Reports == null)
            {
                SetValue(StatusTextPropertyKey, "未接入报表数据源：运行态才有历史账本。");
                return;
            }

            if (!TryBuildRequest(out var request, out var buildError))
            {
                SetValue(StatusTextPropertyKey, buildError);
                RefreshCommands();
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var seq = ++_querySeq;

            SetValue(IsBusyPropertyKey, true);
            SetValue(BusyVisibilityPropertyKey, Visibility.Visible);
            SetValue(StatusTextPropertyKey, "查询中…");
            RefreshCommands();

            ScadaReportResult result;
            try
            {
                result = await context.Reports.BuildAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                return; // 被新查询取代：不动界面
            }
            catch (Exception ex)
            {
                if (seq != _querySeq) return;
                SetValue(IsBusyPropertyKey, false);
                SetValue(BusyVisibilityPropertyKey, Visibility.Collapsed);
                SetValue(StatusTextPropertyKey, "查询失败：" + ex.Message);
                RefreshCommands();
                return;
            }

            if (seq != _querySeq)
                return; // 过期结果不覆盖新结果

            _result = result;
            _pageIndex = 0;
            SetValue(IsBusyPropertyKey, false);
            SetValue(BusyVisibilityPropertyKey, Visibility.Collapsed);

            RebuildHeader();
            RebuildRows();
            RefreshCommands();
        }

        /// <summary>把属性面板上的文本配置折成请求；范围/班次非法时给出中文原因。</summary>
        private bool TryBuildRequest(out ScadaReportRequest request, out string error)
        {
            request = null!;
            error = string.Empty;

            var now = DateTime.Now;

            if (!TryResolveMoment(From, now, out var from))
            {
                error = $"起点「{From}」看不懂：用 now / -1d / -8h / 08:00 / 2026-10-25 08:00。";
                return false;
            }

            if (!TryResolveMoment(To, now, out var to))
            {
                error = $"终点「{To}」看不懂：用 now / -1d / -8h / 20:00 / 2026-10-25 20:00。";
                return false;
            }

            var columns = ScadaReportCodec.Parse(ColumnsJson);
            if (columns.Count == 0)
            {
                error = "还没有配置列：在属性面板点「配置列…」加至少一列。";
                return false;
            }

            var grouping = (Grouping ?? "None").Trim().ToLowerInvariant() switch
            {
                "shift" => ScadaReportGrouping.Shift,
                "day" => ScadaReportGrouping.Day,
                "hour" => ScadaReportGrouping.Hour,
                _ => ScadaReportGrouping.None,
            };

            request = new ScadaReportRequest
            {
                Mode = string.Equals(Mode, "Summary", StringComparison.OrdinalIgnoreCase)
                    ? ScadaReportMode.Summary
                    : ScadaReportMode.Detail,
                FromLocal = from,
                ToLocal = to,
                Columns = columns,
                Keyword = Keyword,
                Grouping = grouping,
                ShiftStarts = (ShiftStarts ?? string.Empty)
                    .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries),
                MaxRows = MaxRows <= 0 ? DefaultMaxRows : MaxRows,
            };

            return true;
        }

        /// <summary>
        /// 解析一个时刻：<c>now</c> / <c>-1d</c> / <c>+2h</c> / <c>08:00</c>（今天）/
        /// <c>2026-10-25 08:00</c> / <c>2026-10-25</c>。
        /// 解析不出返回 false（调用方给中文提示，不猜）。
        /// </summary>
        public static bool TryResolveMoment(string? text, DateTime now, out DateTime value)
        {
            value = now;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var raw = text.Trim();

            if (raw.Equals("now", StringComparison.OrdinalIgnoreCase))
                return true;

            if (raw.Length > 1 && (raw[0] == '-' || raw[0] == '+')
                && double.TryParse(raw.Substring(1, raw.Length - 2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var amount))
            {
                var unit = char.ToLowerInvariant(raw[raw.Length - 1]);
                var span = unit switch
                {
                    's' => TimeSpan.FromSeconds(amount),
                    'm' => TimeSpan.FromMinutes(amount),
                    'h' => TimeSpan.FromHours(amount),
                    'd' => TimeSpan.FromDays(amount),
                    _ => TimeSpan.MinValue,
                };

                if (span == TimeSpan.MinValue)
                    return false;

                value = raw[0] == '-' ? now - span : now + span;
                return true;
            }

            // 纯时刻：落在今天（现场最常用：08:00 ~ 20:00）
            if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var clock)
                && clock >= TimeSpan.Zero && clock < TimeSpan.FromDays(1))
            {
                value = now.Date + clock;
                return true;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value))
                return true;

            value = now;
            return false;
        }

        // ---- 视图重建 ----

        private void RebuildHeader()
        {
            var columns = CurrentColumns();
            var widths = ComputeWidths(columns);

            var cells = new ReportCellView[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                cells[i] = new ReportCellView(columns[i].Title, widths[i], isHeader: true, alignRight: i > 0);

            SetValue(HeaderCellsPropertyKey, (IReadOnlyList<ReportCellView>)cells);
            SetValue(HeaderVisibilityPropertyKey, cells.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
        }

        private void RebuildRows()
        {
            var columns = CurrentColumns();
            var widths = ComputeWidths(columns);

            if (_result == null || _result.Rows.Count == 0)
            {
                SetValue(RowViewsPropertyKey, Array.Empty<ReportRowView>());
                SetValue(HasRowsPropertyKey, false);
                SetValue(StatusVisibilityPropertyKey, Visibility.Visible);
                SetValue(PageTextPropertyKey, string.Empty);
                UpdateFooter();
                return;
            }

            var size = EffectivePageSize;
            var start = _pageIndex * size;
            var rows = new List<ReportRowView>(Math.Min(size, _result.Rows.Count - start));

            for (var r = start; r < Math.Min(start + size, _result.Rows.Count); r++)
            {
                var source = _result.Rows[r];
                var cells = new ReportCellView[columns.Count];

                for (var c = 0; c < columns.Count; c++)
                {
                    var text = c < source.Count ? source[c] : string.Empty;
                    cells[c] = new ReportCellView(text, widths[c], isHeader: false, alignRight: c > 0);
                }

                rows.Add(new ReportRowView(cells));
            }

            SetValue(RowViewsPropertyKey, (IReadOnlyList<ReportRowView>)rows);
            SetValue(HasRowsPropertyKey, rows.Count > 0);
            SetValue(StatusVisibilityPropertyKey, rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible);
            SetValue(PageTextPropertyKey, PageCount <= 1 ? $"共 {_result.Rows.Count} 行" : $"{_pageIndex + 1} / {PageCount}");

            SetValue(StatusTextPropertyKey, string.Empty);
            UpdateFooter();
        }

        private void UpdateFooter()
        {
            if (_result == null)
            {
                SetValue(FooterTextPropertyKey, string.Empty);
                SetValue(TitleMetaPropertyKey, RangeText());
                return;
            }

            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(_result.SourceName))
                parts.Add("来源 " + _result.SourceName);
            parts.Add($"扫描 {_result.ScannedRows} 行");
            parts.Add(_result.GeneratedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(_result.Message))
                parts.Add(_result.Message!);

            SetValue(FooterTextPropertyKey, string.Join(" · ", parts));
            SetValue(TitleMetaPropertyKey, $"{RangeText()} · {_result.RowCount} 行");
        }

        private string RangeText()
            => $"{From ?? "now"} ~ {To ?? "now"}";

        private IReadOnlyList<ScadaReportColumn> CurrentColumns() => ScadaReportCodec.Parse(ColumnsJson);

        /// <summary>
        /// 列宽：定宽列按配置，余量列均分剩下的宽度（与数据表格同一套算法）。
        /// 可用宽度还没量出来时全部给一个保守值，避免算出 0 宽把文字全裁掉。
        /// </summary>
        private double[] ComputeWidths(IReadOnlyList<ScadaReportColumn> columns)
        {
            var widths = new double[columns.Count];
            if (columns.Count == 0)
                return widths;

            const double horizontalPadding = 20; // 左右各 10（与模板 Margin 对齐）
            var available = Math.Max(0, (ActualWidth > 0 ? ActualWidth : 400) - horizontalPadding - 2);

            var fixedSum = 0d;
            var flexCount = 0;

            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Width > 0)
                {
                    widths[i] = columns[i].Width;
                    fixedSum += columns[i].Width;
                }
                else
                {
                    flexCount++;
                }
            }

            if (flexCount == 0)
                return widths;

            var share = Math.Max(ScadaReportCodec.MinWidth, (available - fixedSum) / flexCount);
            for (var i = 0; i < columns.Count; i++)
            {
                if (widths[i] <= 0)
                    widths[i] = share;
            }

            return widths;
        }

        private void RefreshCommands()
        {
            QueryCommand.RaiseCanExecuteChanged();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
            ExportXlsxCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
        }

        private void GoPreviousPage()
        {
            if (_pageIndex <= 0)
                return;

            _pageIndex--;
            RebuildRows();
            RefreshCommands();
        }

        private void GoNextPage()
        {
            if (_pageIndex + 1 >= PageCount)
                return;

            _pageIndex++;
            RebuildRows();
            RefreshCommands();
        }

        // ---- 导出 / 打印 ----

        private void Export(string format)
        {
            if (_result == null || _result.Rows.Count == 0)
                return;

            var exporter = RuntimeContext?.ReportExporter;
            if (exporter == null)
            {
                SetValue(FooterTextPropertyKey, "未接入导出能力：宿主没有提供导出器。");
                return;
            }

            if (!HasOperateRight(out var denied))
            {
                SetValue(FooterTextPropertyKey, denied ?? "当前角色不能导出报表。");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出报表",
                Filter = format == "csv"
                    ? "CSV 文件 (*.csv)|*.csv"
                    : "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = BuildDefaultFileName(format),
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
                return;

            var ok = exporter.TryExport(_result, TitleText, format, dialog.FileName, out var error);
            var detail = $"范围 {RangeText()}，{_result.Rows.Count} 行 → {dialog.FileName}";

            SetValue(FooterTextPropertyKey, ok ? "已导出：" + dialog.FileName : "导出失败：" + error);
            WriteAudit("导出报表", ok ? "成功" : "失败", ok ? detail : detail + "；" + error);
        }

        private string BuildDefaultFileName(string format)
        {
            var title = string.IsNullOrWhiteSpace(TitleText) ? "报表" : TitleText;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            return $"{title}-{stamp}.{format}";
        }

        private string TitleText => Text ?? string.Empty;

        private void Print()
        {
            if (_result == null || _result.Rows.Count == 0)
                return;

            if (!HasOperateRight(out var denied))
            {
                SetValue(FooterTextPropertyKey, denied ?? "当前角色不能打印报表。");
                return;
            }

            var dialog = new System.Windows.Controls.PrintDialog();
            var landscape = string.Equals(PageOrientation, "Landscape", StringComparison.OrdinalIgnoreCase);

            dialog.PrintTicket.PageOrientation = landscape
                ? System.Printing.PageOrientation.Landscape
                : System.Printing.PageOrientation.Portrait;
            dialog.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
                System.Printing.PageMediaSizeName.ISOA4);

            if (dialog.ShowDialog() != true)
                return;

            var document = ReportPrintBuilder.Build(
                _result, TitleText, RangeText(), CurrentUserName(), landscape);
            var paginator = ReportPrintBuilder.WithFooter(
                ((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator,
                $"操作者 {CurrentUserName()}    范围 {RangeText()}");

            try
            {
                dialog.PrintDocument(paginator, string.IsNullOrWhiteSpace(TitleText) ? "报表" : TitleText);
                SetValue(FooterTextPropertyKey, "已发送到打印机。");
                WriteAudit("打印报表", "成功", $"范围 {RangeText()}，{_result.Rows.Count} 行");
            }
            catch (Exception ex)
            {
                SetValue(FooterTextPropertyKey, "打印失败：" + ex.Message);
                WriteAudit("打印报表", "失败", ex.Message);
            }
        }

        private string CurrentUserName()
            => RuntimeContext?.AccessPolicy.CurrentUserName ?? "未登录";

        /// <summary>写审计（没有审计出口就只留页脚提示，不阻断动作）。</summary>
        private void WriteAudit(string action, string result, string detail)
        {
            var audit = RuntimeContext?.Audit;
            if (audit == null)
                return;

            try
            {
                audit.Append(
                    CurrentUserName(),
                    string.Empty,
                    string.Empty,
                    string.IsNullOrWhiteSpace(TitleText) ? "报表" : TitleText,
                    "点击",
                    action,
                    result,
                    detail);
            }
            catch
            {
                // 审计失败不阻断：与 IScadaAuditSink 的约定一致（调用方记日志、动作照做）
            }
        }
    }
}
