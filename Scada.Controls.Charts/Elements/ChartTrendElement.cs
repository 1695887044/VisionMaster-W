using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VM.Charts;

namespace VisionMaster.Scada.Controls.Charts
{
    /// <summary>
    /// 趋势曲线图元（TypeKey = <c>Hmi.ChartTrend</c>）：把一根工程变量的历史值画成时间曲线。
    ///
    /// <b>数据从哪来</b>
    /// ---------
    /// 完全复用现有数据泵，<b>不加新通道</b>：宿主把变量绑到「数值」这一行（<see cref="ValueProperty"/>），
    /// 运行态变量一变，<c>ScadaRuntimeBinder</c> 就把新值写进这个依赖属性；
    /// 本控件在属性变更回调里把 <c>(到达时刻, 值)</c> 追加进缓冲。
    ///
    /// 这条路径白送两条语义，都不必自己实现：
    /// ① <b>"值没变不记点"天然成立</b>——数据泵写值前有等值守卫
    ///    （<c>ScadaElementBase.TryApplyRuntimeValue</c> 的 <c>if (!Equals(GetValue(target), converted))</c>），
    ///    同一个值连报十次只会到这里一次。现场 PLC 每 100ms 重发同一个数值是常态，
    ///    没有这层守卫就要在曲线侧再写一遍去重。
    /// ② <b>停止运行自动清干净</b>——运行值只活在控件上，宿主收工时会 <c>Refresh()</c> 一遍，
    ///    本控件在 <see cref="OnElementRefreshed"/> 里清空缓冲，于是"停止 = 回到空白设计态"。
    ///
    /// <b>为什么用"到达时刻"而不是定采样率</b>
    /// ---------
    /// SCADA 变量的变化是<b>不规则</b>的：压力在稳定段可能五分钟不变，在调节段可能一秒变十次。
    /// 定采样率（<c>SignalSeriesVm</c> 那条路）会把"值没变"也画成等间隔点，
    /// 看起来像"数据一直很平稳"——那是<b>编造出来的信息</b>。所以走 XY 散点语义
    /// （<c>ScatterSeriesVm</c>）：X 是真实的到达时刻，曲线的疏密本身就是信息
    /// （密 = 在动，疏 = 稳住了），这正是操作员判断工况最直接的依据。
    ///
    /// <b>缓冲为什么是全量左移而不是环形</b>
    /// ---------
    /// 环形缓冲写满后物理顺序会绕回起点，而 ScottPlot 的 Scatter 是<b>按数组顺序</b>连成折线的——
    /// 绕回的那一笔会从数组末尾横穿回开头，画出一条假的竖线。要环形就得每次渲染前重排一份连续数组，
    /// 那等于每帧多一次全量拷贝，比"每次追加时左移一格"还贵（且多一份要维护的临时数组）。
    /// 左移的代价是 <see cref="BufferCapacity"/> 个 double 的 <see cref="Array.Copy"/>，
    /// 4096 个 double = 32KB，实测在微秒量级，而它换来的是"数组永远是时间升序"这条不变量——
    /// 折线顺序、MaxIndex、跟随模式的取头都因此变成平凡问题。
    /// 库里的 <c>SignalSeriesVm</c> 用的也是这个做法（见其 Append），本图元与它保持一致。
    ///
    /// <b>为什么挂载后不抢鼠标</b>
    /// ---------
    /// <c>ChartView</c> 自带滚轮缩放、拖拽平移、十字光标、数据提示。这些在<b>独立图表窗口</b>里是优点，
    /// 但在组态画布里是冲突源：设计态要能拖动、框选这个图元，运行态点它要能触发图元事件。
    /// 所以本图元把 <c>IsHitTestVisible</c> 关掉，曲线退化成"一块会自己更新的图形"，
    /// 鼠标事件一律穿到外框上由画布处理。代价是运行态用不了数据提示——
    /// 等真需要"操作员在曲线上取值"时，再按"双击图元进入曲线放大视图"来做，
    /// 而不是让画布和图表抢同一串鼠标消息。
    /// </summary>
    public partial class ChartTrendElement : ScadaElementBase
    {
        /// <summary>
        /// 缓冲点数上限。
        ///
        /// 4096 这个取值的来由：8 秒时间窗下，变量以 500Hz 抖动也只是刚好填满；
        /// 而两个 4096 的 double 数组合计 64KB，一屏放十个曲线图元也不过 640KB。
        /// 真正起作用的是"时间窗"与这个上限<b>两个</b>条件的较小者——
        /// 窗口决定"画面上看多少"，上限决定"内存绝不失控"（现场见过传感器故障导致每毫秒一个值）。
        /// </summary>
        private const int BufferCapacity = 4096;

        private static readonly Color DefaultCurveColor = Color.FromRgb(0x2D, 0x7D, 0xD2);

        private readonly double[] _xs = new double[BufferCapacity];
        private readonly double[] _ys = new double[BufferCapacity];

        /// <summary>曲线系列。数组由本控件持有并原址填充，ScottPlot 直接引用同一份（零拷贝）。</summary>
        private readonly ScatterSeriesVm _series;

        private readonly ChartView _chart;

        /// <summary>数据起点（第一次收到值的那一刻），X 轴一律用"相对它的秒数"。</summary>
        private DateTime _origin;

        /// <summary>已写入的点数（写满后恒等于容量）</summary>
        private int _count;

        private readonly Dispatcher _dispatcher;

        public ChartTrendElement()
        {
            // 捕获构造时的调度器：本控件必然在 UI 线程上被造出来（画布建控件），
            // 之后数据泵可能在别的线程上推值，那时要靠它把"追加一个点"送回来。
            _dispatcher = Dispatcher.CurrentDispatcher;

            _series = new ScatterSeriesVm("trend", "趋势", DefaultCurveColor, _xs, _ys)
            {
                LineWidth = 1.6,
                MarkerSize = 0, // 点太密时标记点会糊成一片，趋势看的是线形
            };

            _chart = new ChartView
            {
                AutoScroll = true,
                AutoScrollSeconds = 60,
                ShowLegend = false,
                Measurable = true,
                ShowDataTips = true,
                // 关掉命中：见类注释末段——画布要能拖它、选它，鼠标消息不能在这里被吃掉。
                IsHitTestVisible = false,
            };

            // 关键：ScatterSeriesVm 实现的是 IScatterSeriesVm（不是 ISeriesVm），
            // 而 SeriesSource 的归一化逻辑只认 IEnumerable，所以必须包成集合再给它。
            _chart.SeriesSource = new ObservableCollection<IScatterSeriesVm> { _series };
        }

        static ChartTrendElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ChartTrendElement),
                new FrameworkPropertyMetadata(typeof(ChartTrendElement)));
        }

        /// <summary>模板部件名：承载 <see cref="ChartView"/> 的宿主</summary>
        public const string PartChartHost = "ChartHost";

        private static readonly DependencyPropertyKey HasSamplesPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasSamples), typeof(bool), typeof(ChartTrendElement),
            new PropertyMetadata(false));

        /// <summary>
        /// 缓冲里此刻有没有样本（只读）。
        ///
        /// 与 <see cref="SampleCount"/> 同源，但它是依赖属性：模板据此在“等待数据…”空态
        /// 与曲线之间切换（本环境 ControlTemplate.Triggers 里的 DataTrigger 不触发，
        /// 所以空态用一个独立的 Style.DataTrigger 绑这个属性，见 ChartTrendElement.xaml）。
        /// </summary>
        public static readonly DependencyProperty HasSamplesProperty = HasSamplesPropertyKey.DependencyProperty;

        /// <summary>缓冲里此刻有没有样本（只读；见 <see cref="HasSamplesProperty"/>）</summary>
        public bool HasSamples => (bool)GetValue(HasSamplesProperty);

        /// <summary>
        /// 缓冲里当前的样本点数（0 = 还没收到过值）。
        ///
        /// 对宿主是一个有用的状态量（"曲线还在等数据"与"数据来了但画不出来"是两种故障，
        /// 界面上要能分开说），对断言是"数据真的进了缓冲"的唯一外部可观察证据——
        /// 缓冲本身是私有的，而曲线画没画在无头断言宿主里验不了（见下）。
        ///
        /// <b>关于"曲线画出来了没有"这条验证的边界</b>：本图元把曲线交给
        /// <c>VM.Charts</c> 的 <see cref="ChartView"/>，而它底层是 ScottPlot → SkiaSharp
        /// 的 WPF 绘制面（<c>WpfPlot</c>）。SkiaSharp 的绘制面在<b>没有真实渲染目标</b>的
        /// 进程里（离屏 <c>RenderTargetBitmap</c>、无窗口）画不出内容——实测整幅图只有底色，
        /// 连坐标轴与网格都没有。所以"曲线真的显示成什么样"由两处保证：
        /// ① VM.Charts 自己的渲染证据（探针工程 <c>_ChartProbe5</c> 的 chart_render_*.png / demo_live.png）；
        /// ② 真机运行。断言宿主只钉到"数据进了缓冲 + 描述符与绑定通道齐备"这一层。
        /// </summary>
        public int SampleCount => _count;

        /// <summary>
        /// 数值：运行态把工程变量绑到这一行，曲线的每个点就是它的历史值。
        ///
        /// 它是本图元唯一的数据入口——<b>刻意不做"多曲线叠一张图"</b>。
        /// 多曲线要一个"曲线列表"编辑器（增删、配色、量程各自独立），那是另一个量级的功能；
        /// 而"两条曲线要对比"的现场诉求，先把两个曲线图元并排摆着也就够用了。
        /// </summary>
        [ScadaProperty(Key = "Value", DisplayName = "数值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true, AffectsRender = false,
                       Description = "运行态把工程变量绑到这里；曲线每个点的横坐标是「值到达的时刻」，"
                                   + "纵坐标是值本身。值没变时数据泵不会重复写入，所以不会画出虚假的等间隔点")]
        public partial double Value { get; set; }

        /// <summary>时间窗（秒）：横轴默认显示最近这么长的一段。</summary>
        [ScadaProperty(Key = "TimeWindow", DisplayName = "时间窗", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "60", Min = 2, Max = 3600, AffectsRender = false,
                       Description = "横轴默认显示最近多少秒（2~3600）。拖动或缩放查看历史后，"
                                   + "1.5 秒无操作会自动恢复跟随最新数据")]
        public partial double TimeWindow { get; set; }

        /// <summary>纵轴量程下限（仅在「固定量程」打开时生效）</summary>
        [ScadaProperty(Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", AffectsRender = false,
                       Description = "「固定量程」打开时纵轴的下边界；关闭时纵轴随数据自动缩放，这一行不生效")]
        public partial double Minimum { get; set; }

        /// <summary>纵轴量程上限（仅在「固定量程」打开时生效）</summary>
        [ScadaProperty(Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "100", AffectsRender = false)]
        public partial double Maximum { get; set; }

        /// <summary>
        /// 固定量程开关：打开＝纵轴夹在下限~上限之间，关闭＝纵轴随数据自动缩放。
        ///
        /// 为什么要一个开关，而不是"下限等于上限就自动"：现场量程刚好从 0 开始是极常见的情形
        /// （温度 0~150℃、压力 0~1.6MPa），拿任何"特定取值"当哨兵都会让这类量程配不出固定效果。
        /// </summary>
        [ScadaProperty(Key = "FixedRange", DisplayName = "固定量程", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "False", AffectsRender = false,
                       Description = "打开后纵轴固定在下限~上限之间（超出部分被裁掉，数据仍在缓冲里）；"
                                   + "关闭则纵轴随数据自动缩放")]
        public partial bool FixedRange { get; set; }

        /// <summary>曲线颜色</summary>
        [ScadaProperty(Key = "CurveBrush", DisplayName = "曲线颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2D7DD2", AffectsRender = false)]
        public partial Brush? CurveBrush { get; set; }

        /// <summary>网格与坐标轴的颜色</summary>
        [ScadaProperty(Key = "AxisBrush", DisplayName = "网格色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF3A3A3A", AffectsRender = false,
                       Description = "坐标轴与网格线的颜色；深色底上要比底色亮一档，否则看不出刻度")]
        public partial Brush? AxisBrush { get; set; }

        /// <summary>标题（显示在图表左上角；留空则不放标题）</summary>
        [ScadaProperty(Key = "Title", DisplayName = "标题", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "", IsBindable = true, AffectsRender = false,
                       Description = "图表左上角的一行字（通常写变量名与单位）；留空则不放标题")]
        public partial string? Title { get; set; }

        /// <summary>模板套上时把 <see cref="ChartView"/> 挂进宿主</summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild(PartChartHost) is ContentControl host)
                host.Content = _chart;

            ApplyChartAppearance();
        }

        /// <summary>
        /// 设计值刷新：清空运行态积攒的数据点，回到"空白设计态"。
        ///
        /// 宿主在停止运行时会对每个控件 <c>Refresh()</c>（见 <c>ScadaRuntimeBinder.Stop</c>），
        /// 于是"停止运行 = 曲线清空重来"与其他图元的"停止 = 回到设计值"完全一致——
        /// 否则下一轮运行会在上一轮的历史上接着画，看起来像"停机期间也在涨"。
        /// </summary>
        protected override void OnElementRefreshed()
        {
            ApplyStrokeInset(1);
            ApplyChartAppearance();
            ClearSamples();
        }

        /// <summary>
        /// 运行上下文装上/摘掉时清空缓冲。
        ///
        /// 与 <see cref="OnElementRefreshed"/> 是两条<b>互补</b>的路，缺一条就有残留：
        /// Refresh 覆盖"停止运行"；而同一轮运行里<b>切页再切回来</b>会复用同一批控件
        /// （切页是换 ItemsSource，控件按页重建，但"回到上一页"时如果页对象没换，控件就还在），
        /// 那时上下文重新装上，不清就会看到上一段的历史。两处都清，代价只是几行。
        /// </summary>
        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);
            ClearSamples();
        }

        /// <summary>
        /// 值变更 → 追加一个数据点。
        ///
        /// <b>为什么要回 UI 线程</b>：缓冲数组被 <c>ChartView</c> 直接引用着（零拷贝），
        /// 而渲染在 UI 线程上跑；在别的线程上改这两个数组就是与渲染并发读写同一片内存。
        /// 数据泵的调用线程不保证是 UI 线程（切页路径上就是 <c>Loaded</c> 优先级排进来的），
        /// 所以这里封送一次。顺带一个好处：缓冲只在 UI 线程上被碰，全程无需加锁。
        /// </summary>
        partial void OnValuePropertyChanged()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(AppendSample));
                return;
            }

            AppendSample();
        }

        partial void OnTimeWindowPropertyChanged() => ApplyChartAppearance();
        partial void OnMinimumPropertyChanged() => ApplyChartAppearance();
        partial void OnMaximumPropertyChanged() => ApplyChartAppearance();
        partial void OnFixedRangePropertyChanged() => ApplyChartAppearance();
        partial void OnCurveBrushPropertyChanged() => ApplyChartAppearance();
        partial void OnAxisBrushPropertyChanged() => ApplyChartAppearance();
        partial void OnTitlePropertyChanged() => ApplyChartAppearance();

        /// <summary>
        /// 把一个样本写进缓冲。
        ///
        /// 时间轴以"第一次收到值"为原点：拿绝对时刻（如 <c>DateTime.Ticks</c>）做 X，
        /// 数值会到 6e17 量级，浮点刻度在那个量级上已经不剩精度，画出来是空的。
        /// 所以内部一律用"相对原点的秒"，绝对时刻只在坐标轴标签上还原
        /// （通过 <see cref="ScatterSeriesVm.StartTime"/>，由 ChartView 换算成 HH:mm:ss）。
        /// </summary>
        private void AppendSample()
        {
            if (_count == 0)
            {
                _origin = DateTime.Now;
                _series.StartTime = _origin;
            }

            var seconds = (DateTime.Now - _origin).TotalSeconds;
            var value = Value;

            if (_count < BufferCapacity)
            {
                _xs[_count] = seconds;
                _ys[_count] = value;
                _count++;
            }
            else
            {
                // 满了就整体左移一格，把最旧的点挤出去。见类注释"为什么不是环形缓冲"。
                Array.Copy(_xs, 1, _xs, 0, BufferCapacity - 1);
                Array.Copy(_ys, 1, _ys, 0, BufferCapacity - 1);
                _xs[BufferCapacity - 1] = seconds;
                _ys[BufferCapacity - 1] = value;
            }

            // 空态显隐：第一笔样本进来就把“等待数据…”收掉。写在这里而不是散在各处，
            // 保证“缓冲有数”与“模板知道有数”永远同一步发生。
            SetValue(HasSamplesPropertyKey, true);

            // 通知控件"数据长到第几个了"（它据此同步 Scatter 的 MaxRenderIndex，并触发一次重绘）。
            // 数组是原址引用，所以这一步就是全部的通数据动作——没有拷贝、没有推送。
            _series.MaxIndex = _count - 1;

            // 缓冲写满之后 MaxIndex 恒为容量 - 1，setter 的同值守卫会提前返回、版本号不再递增，
            // 控件的渲染调度器就会判定"没变化"、停止重绘（表现为曲线卡住不动）。
            // 所以写满之后每一笔都要显式标一次"数据脏"（见 ScatterSeriesVm.MarkAppended）。
            if (_count == BufferCapacity)
                _series.MarkAppended();

            // 视口由本控件推：库里的"跟随模式"只认定采样率的信号系列
            // （它按 SampleRate 外推当前时刻），散点系列不参与，靠它画面会一直停在默认轴范围上
            // ——数据全在视野外，看起来就是"一条曲线都没画"（实测踩过）。
            UpdateViewport(seconds);
        }

        /// <summary>
        /// 按最新样本时刻滚动视口。
        ///
        /// 横轴：最近 <see cref="TimeWindow"/> 秒；纵轴：固定量程时钉在上下限，
        /// 否则在可见区间里现扫一遍（曲线要"看得见起伏"，自动量程是趋势图最常用的看法）。
        ///
        /// <b>节流</b>：<c>SetAxisLimits</c> 会同步重绘一次，而变量在调节段可能一秒变几十次。
        /// 位移不到窗口的 2% 就不重设——肉眼在这个尺度上看不出差别，但省下的是每次刷新的整帧重绘。
        /// </summary>
        private void UpdateViewport(double newestSeconds)
        {
            double window = Math.Clamp(TimeWindow, 2, 3600);

            if (newestSeconds - _lastViewportSeconds < window * 0.02)
                return;

            _lastViewportSeconds = newestSeconds;

            double xMin = Math.Max(0, newestSeconds - window);
            double xMax = newestSeconds + window * 0.02;

            double yMin, yMax;

            if (FixedRange && Maximum > Minimum)
            {
                yMin = Minimum;
                yMax = Maximum;
            }
            else if (!TryScanVisibleRange(xMin, out yMin, out yMax))
            {
                return; // 还没有落在窗口里的点：保持现状，别把轴设成空区间
            }

            _chart.SetAxisLimits(xMin, xMax, yMin, yMax);
        }

        /// <summary>
        /// 扫出"落在可见时间窗里的点的值域"，并留一成余量。
        ///
        /// 留余量是为了曲线不贴着上下边框（贴边时看不出"到顶了没有"，而趋势图的第一个问题
        /// 恰恰是"有没有到顶"）。上下限相同（一条平线）时给一个固定的最小跨度，
        /// 否则 ScottPlot 会因零高度区间画不出东西。
        /// </summary>
        private bool TryScanVisibleRange(double xMin, out double yMin, out double yMax)
        {
            yMin = double.MaxValue;
            yMax = double.MinValue;
            bool any = false;

            for (int i = 0; i < _count; i++)
            {
                if (_xs[i] < xMin)
                    continue;

                double v = _ys[i];

                if (double.IsNaN(v) || double.IsInfinity(v))
                    continue;

                if (v < yMin) yMin = v;
                if (v > yMax) yMax = v;
                any = true;
            }

            if (!any)
                return false;

            if (yMax - yMin < 1e-9)
            {
                yMin -= 1;
                yMax += 1;
            }
            else
            {
                double pad = (yMax - yMin) * 0.1;
                yMin -= pad;
                yMax += pad;
            }

            return true;
        }

        /// <summary>最近一次重设视口的时刻（秒），节流用（见 UpdateViewport）</summary>
        private double _lastViewportSeconds = double.NegativeInfinity;

        private void ClearSamples()
        {
            _count = 0;
            _origin = default;
            _lastViewportSeconds = double.NegativeInfinity;
            SetValue(HasSamplesPropertyKey, false);

            // MaxIndex = -1 告诉控件"一个点都没有"：ScottPlot 5 不允许渲染未写入区间，
            // 给 -1 它就不画这条线，正是我们要的空白态。
            _series.MaxIndex = -1;
            _series.StartTime = null;
        }

        /// <summary>
        /// 把设计态属性落到 <see cref="ChartView"/> 上。
        ///
        /// 逐个属性写在<b>一处</b>而不是散进各个变更回调里：这些设置互相牵连
        /// （改量程要同时看固定开关，改颜色要同时动底色与轴），
        /// 分开写就会出现"改了 A 但 B 没跟着更新"的组合缺口。
        /// </summary>
        private void ApplyChartAppearance()
        {
            // 跟随模式关掉、视口由本控件推（见 UpdateViewport）：
            // 库的 AutoScroll 只认定采样率的信号系列，而趋势图走的是散点语义
            // （X 是每个值真实到达的时刻），靠它画面会永远停在默认轴范围上。
            _chart.AutoScroll = false;

            if (CurveBrush is SolidColorBrush curve)
                _series.Color = curve.Color;

            if (AxisBrush is SolidColorBrush axis)
                _chart.SetAxisColors(axis.Color, axis.Color, axis.Color);

            // 深色运行画面：只覆盖本实例的画布/数据区/网格/刻度，不改全局 ChartTheme
            // （独立图表窗口与探针仍跟随 Fluent 主题）。取值与 ScadaTokens.xaml 对齐。
            _chart.SetSurfaceColors(
                Color.FromRgb(0x15, 0x1C, 0x23),
                Color.FromRgb(0x15, 0x1C, 0x23),
                Color.FromRgb(0x2A, 0x37, 0x45),
                Color.FromRgb(0xA9, 0xB7, 0xC6));

            _chart.Title = string.IsNullOrWhiteSpace(Title) ? null : Title;
            _chart.RefreshTheme();

            // 配置变了就立刻按新配置重设一次视口（时间窗/量程/固定开关都影响它）。
            // 没有数据时不动：那时轴范围无所谓，硬设一个空区间反而会画出一张怪图。
            _lastViewportSeconds = double.NegativeInfinity;

            if (_count > 0)
                UpdateViewport(_xs[_count - 1]);
        }
    }
}
