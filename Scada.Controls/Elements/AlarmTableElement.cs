using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Prism.Commands;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报警汇总表图元（TypeKey = <c>Hmi.AlarmTable</c>）：把当前报警<b>逐条列出来</b>，
    /// 每条后面带一颗「确认」。
    ///
    /// 与相邻两个图元的分工
    /// ---------
    /// · <see cref="AlarmBannerElement"/> 是"报警在响"这件事的<b>提示</b>（一行/一屏滚动，只读）；
    /// · <see cref="AckButtonElement"/> 是"一把按掉"的<b>动作入口</b>（不显示清单）；
    /// · 本图元是<b>清单 + 逐条处置</b>：谁在报、什么时候报的、多要紧、确认了没有，一行一条。
    /// 三者的数据都来自同一个 <see cref="ScadaAlarmEngine"/>，所以"表里那一行"与"条子上那句话"
    /// 永远是同一条报警（排序、配色、状态词都取同一份事实）。
    ///
    /// 数据从哪来、什么时候重画
    /// ---------
    /// ① <b>每拍重读</b>：搭 <see cref="ScadaRuntimeContext.Beat"/>，每拍读一次
    ///    <see cref="ScadaAlarmEngine.ActiveAlarms"/>。报警状态不是"变量绑定"（它是引擎算出来的），
    ///    没有逐条变更通知可依附，只能拉。
    /// ② <b>指纹短路</b>：每行拼一个 <c>AlarmId|State|Severity|TimeTicks</c> 指纹，
    ///    与上一拍完全相同就<b>一个行对象都不重建</b>。这不是省 CPU 的优化，是硬纪律：
    ///    整表重建会把操作员<b>正按着的那颗「确认」按钮连行一起换走</b>，
    ///    手指抬起来时按到的已经是一个新按钮（与 <see cref="AxisPanelElement"/> 的点位表同一条纪律）。
    /// ③ 引擎的三个事件（<see cref="ScadaAlarmEngine.AlarmRaised"/> / <c>AlarmChanged</c> / <c>AlarmCleared</c>）
    ///    也挂上：它们可能在变量轮询线程上抛，所以统一切回本控件的 Dispatcher 再重读——
    ///    这样"确认完这一行立刻变灰"不必等下一拍。
    ///
    /// 排序
    /// ---------
    /// <b>不排</b>：<see cref="ScadaAlarmEngine.ActiveAlarms"/> 已经按"严重度降序、同级按激活时间降序"
    /// 排好（那是产品规则，不是某个面板的显示偏好）。本图元只做"取前 <see cref="MaxRows"/> 行"，
    /// 被截掉多少条写在页脚（"还有 N 条"）——截断的是画面，不是事实。
    ///
    /// 确认
    /// ---------
    /// 门禁三道：<see cref="AllowAck"/> 打开、运行态有上下文、当前角色过
    /// <see cref="IScadaAccessPolicy.CanElement"/>（与 <see cref="AckButtonElement"/> 同一口径，
    /// 权限键取自 <see cref="ScadaElement.RequiredPermission"/>，为 null 一律放行）。
    /// <see cref="ScadaAlarmEngine.Acknowledge"/> 返回 <b>0</b> 时有<b>两种完全不同的意思</b>：
    /// 这条报警已经不在了（恢复并被清除），或者早就确认过了。所以 0 一定要回查一次
    /// <see cref="ScadaAlarmEngine.ActiveAlarms"/> 再说——一律回"确认失败"会让操作员以为系统坏了。
    /// 结果写进 <see cref="StatusText"/>（工具条下方那行字 + 控件 ToolTip），<b>不弹窗</b>：
    /// 确认是高频动作，弹窗会把"按一下"变成"按三下"。
    ///
    /// 设计期（<see cref="ScadaElementBase.RuntimeContext"/> 为 null）
    /// ---------
    /// 表头照画，正文写"设计期不查询报警"——<b>不写 <see cref="EmptyText"/></b>。
    /// 编辑器里根本没有引擎，"当前无报警"是一句没人为它负责的话（与报警条的"未接入报警源"
    /// 同一条口径：宁可说"我不知道"，也不替系统报平安）。
    ///
    /// 模板
    /// ---------
    /// <b>不用 ListView + GridView</b>（R19：应用级 Fluent 的隐式 ListView 样式会顶掉框架那份，
    /// 列头消失、类型全名上屏）；用 <c>ItemsControl</c> + 行 <c>DataTemplate</c>，
    /// 列宽是固定像素（Auto 列 + 定宽单元格），表头与行同构所以天然对齐。
    /// <b>不写 ControlTemplate.Triggers 的 DataTrigger</b>（本环境静默失效）：
    /// 显隐、斑马纹、已确认淡化一律由控件算成只读 DP / 行字段，模板直绑。
    /// </summary>
    public partial class AlarmTableElement : ScadaElementBase
    {
        /// <summary>行数下限：0 行等于图元隐形，那是"看不见也没人知道"的组态事故</summary>
        public const int MinRows = 1;

        /// <summary>行数上限：再多也不该靠一张表看，那是报警历史面板的事</summary>
        public const int MaxRowsLimit = 200;

        /// <summary>
        /// 设计期正文提示。<b>刻意不是</b> <see cref="EmptyText"/>：
        /// 编辑器里没有报警引擎，"当前无报警"是假消息（见类注释）。
        /// </summary>
        public const string DesignTimeHint = "设计期不查询报警";

        /// <summary>没配 <see cref="EmptyText"/> 时的兜底文案</summary>
        public const string DefaultEmptyText = "当前无报警";

        /// <summary>权限不足时的状态行文案（与 <see cref="AckButtonElement"/> 逐字一致）</summary>
        public const string PermissionDeniedText = "当前角色没有操作权限";

        /// <summary>已确认未恢复的行淡化到这个不透明度（"这条我看过了"，但报警还在）</summary>
        public const double AcknowledgedRowOpacity = 0.55d;

        // 令牌色的兜底副本：断言宿主/纯净宿主没有 Application，TryFindResource 取不到时用它。
        // 取值与 ScadaTokens.xaml 的 HmiStateFault/Warning、HmiInfo、HmiZebra 逐项一致。
        private static readonly Brush FallbackCritical = ScadaBrushes.Frozen("#FFE03A2B");
        private static readonly Brush FallbackWarning = ScadaBrushes.Frozen("#FFFFB020");
        private static readonly Brush FallbackInfo = ScadaBrushes.Frozen("#FF3B82F6");
        private static readonly Brush FallbackZebra = ScadaBrushes.Frozen("#08FFFFFF");

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）。

        /// <summary>最多画几行（超出的截断；页脚报"还有 N 条"）</summary>
        [ScadaProperty(Key = "MaxRows", DisplayName = "最多行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "8", Min = 1, Max = 200,
                       Description = "最多画几行，超出的不画（页脚报「还有 N 条」）。行数多到超出控件高度时出现滚动条")]
        public partial int MaxRows { get; set; }

        /// <summary>是否显示工具条（标题 + 未确认计数 + 全部确认）</summary>
        [ScadaProperty(Key = "ShowToolbar", DisplayName = "显示工具条", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "True",
                       Description = "关掉后只剩表头 + 行：标题、未确认计数与「全部确认」按钮都不出现（只读监视用）")]
        public partial bool ShowToolbar { get; set; }

        /// <summary>
        /// 允许确认（行内「确认」+ 工具条「全部确认」）。
        ///
        /// 关掉就是"只读表"：行内不再出现按钮，工具条那颗也置灰。
        /// 权限仍照常判——这个开关只表达"这张表要不要提供处置入口"，不是权限的替代品。
        /// </summary>
        [ScadaProperty(Key = "AllowAck", DisplayName = "允许逐条确认", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "True",
                       Description = "关掉 = 只读表：行内「确认」与工具条「全部确认」都不可用（权限仍照常检查）")]
        public partial bool AllowAck { get; set; }

        /// <summary>是否显示时间列</summary>
        [ScadaProperty(Key = "ShowTime", DisplayName = "显示时间", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "显示「时间」列（激活时刻，本地时间到秒）。关掉后这一列整列收起")]
        public partial bool ShowTime { get; set; }

        /// <summary>是否显示级别列</summary>
        [ScadaProperty(Key = "ShowSeverity", DisplayName = "显示级别", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "显示「级别」列（严重 / 警告 / 提示，按级别上色）。关掉后这一列整列收起")]
        public partial bool ShowSeverity { get; set; }

        /// <summary>
        /// 一条报警都没有时显示的那句话（设计期不显示它，见类注释）。
        /// 默认值必须与 <see cref="DefaultEmptyText"/> 逐字一致：特性里的默认值会同时进依赖属性元数据
        /// 与描述符（旧 .vms 没写这个键时现取它），两处说法不同就会出现"新拖一个图元"和"老图元"文案不一样。
        /// </summary>
        [ScadaProperty(Key = "EmptyText", DisplayName = "无报警提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "当前无报警",
                       Description = "运行态一条报警都没有时正文显示的字；设计期正文固定显示「设计期不查询报警」")]
        public partial string? EmptyText { get; set; }

        /// <summary>隔行底色（斑马纹）</summary>
        [ScadaProperty(Key = "Zebra", DisplayName = "斑马纹", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
                       Description = "隔行加一层浅底（令牌 HmiZebraBrush，8% 白）。行多时更容易横着读，行少时关掉更干净")]
        public partial bool Zebra { get; set; }

        /// <summary>表头底色（null = 用令牌 <c>HmiSurfaceRaisedBrush</c>）</summary>
        [ScadaProperty(Key = "HeaderBackground", DisplayName = "表头底色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "",
                       Description = "表头那一行的底色。留空 = 令牌色；浅色主题给 #FFFFFFFF")]
        public partial Brush? HeaderBackground { get; set; }

        // ---- 只读运行态属性（模板与断言都读它们，代码里不直接改模板元素） ----

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<AlarmTableRow>), typeof(AlarmTableElement),
            new PropertyMetadata(Array.Empty<AlarmTableRow>()));

        /// <summary>要显示的行（已截断到 <see cref="MaxRows"/> 行，顺序即引擎给的次序）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasRowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasRows), typeof(bool), typeof(AlarmTableElement), new PropertyMetadata(false));

        /// <summary>此刻表里有没有行（正文与空态互斥的判据）</summary>
        public static readonly DependencyProperty HasRowsProperty = HasRowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ActiveCountPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ActiveCount), typeof(int), typeof(AlarmTableElement), new PropertyMetadata(0));

        /// <summary>引擎此刻挂着的报警<b>总条数</b>（未截断；截断只影响画面）</summary>
        public static readonly DependencyProperty ActiveCountProperty = ActiveCountPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey RowsVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(RowsVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>行列表的显隐（有行才显示）</summary>
        public static readonly DependencyProperty RowsVisibilityProperty =
            RowsVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey BodyVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BodyVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>正文提示（空态 / 设计期）的显隐，与行列表互斥</summary>
        public static readonly DependencyProperty BodyVisibilityProperty =
            BodyVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey BodyTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BodyText), typeof(string), typeof(AlarmTableElement), new PropertyMetadata(DesignTimeHint));

        /// <summary>
        /// 正文提示文案：设计期"设计期不查询报警" / 运行态空表 <see cref="EmptyText"/> / 有行时空串。
        /// 模板直绑它，<b>不绑 <see cref="EmptyText"/></b>——后者在运行态有行时也不该出现在屏幕上。
        /// </summary>
        public static readonly DependencyProperty BodyTextProperty = BodyTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ToolbarVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ToolbarVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>工具条的显隐（由 <see cref="ShowToolbar"/> 算好）</summary>
        public static readonly DependencyProperty ToolbarVisibilityProperty =
            ToolbarVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey UnacknowledgedTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(UnacknowledgedText), typeof(string), typeof(AlarmTableElement), new PropertyMetadata(string.Empty));

        /// <summary>工具条上的未确认计数（如 <c>"未确认 2 条"</c> / <c>"全部已确认"</c>；设计期为"—"）</summary>
        public static readonly DependencyProperty UnacknowledgedTextProperty =
            UnacknowledgedTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey FooterTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FooterText), typeof(string), typeof(AlarmTableElement), new PropertyMetadata(string.Empty));

        /// <summary>页脚的截断提示（<c>"还有 N 条"</c>；没截断时为空串）</summary>
        public static readonly DependencyProperty FooterTextProperty = FooterTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey FooterVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FooterVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>页脚截断提示的显隐</summary>
        public static readonly DependencyProperty FooterVisibilityProperty =
            FooterVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(AlarmTableElement), new PropertyMetadata(string.Empty));

        /// <summary>
        /// 最近一次确认的结果 / 权限原因（只读）。成功与失败都写在这里，<b>不弹窗</b>。
        /// 不做自动消失：下一次操作会覆盖它（多养一个计时器只为淡出一行字，不值得）。
        /// </summary>
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>状态行的显隐（有话说才出现）</summary>
        public static readonly DependencyProperty StatusVisibilityProperty =
            StatusVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanAcknowledgeAllPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanAcknowledgeAll), typeof(bool), typeof(AlarmTableElement), new PropertyMetadata(false));

        /// <summary>工具条「全部确认」此刻能不能按（只读；有上下文 + 有权限 + <see cref="AllowAck"/>）</summary>
        public static readonly DependencyProperty CanAcknowledgeAllProperty =
            CanAcknowledgeAllPropertyKey.DependencyProperty;

        // 三列显隐：表头格绑它们，行里那份由控件算进 AlarmTableRow（模板不写 DataTrigger）。
        private static readonly DependencyPropertyKey TimeVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(TimeVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>「时间」列的显隐（表头用；行里那份在 <see cref="AlarmTableRow.TimeVisibility"/>）</summary>
        public static readonly DependencyProperty TimeVisibilityProperty =
            TimeVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SeverityVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SeverityVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>「级别」列的显隐（表头用）</summary>
        public static readonly DependencyProperty SeverityVisibilityProperty =
            SeverityVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey AckVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(AckVisibility), typeof(Visibility), typeof(AlarmTableElement),
            new PropertyMetadata(Visibility.Visible));

        /// <summary>「确认」列的显隐（表头用；<see cref="AllowAck"/> 关掉时整列收起）</summary>
        public static readonly DependencyProperty AckVisibilityProperty =
            AckVisibilityPropertyKey.DependencyProperty;

        /// <summary>上一拍的行指纹（见类注释的"指纹短路"）</summary>
        private string _fingerprint = string.Empty;

        /// <summary>上一拍算出来的"有没有操作权限"（权限变了要强制重建，按钮才会跟着变灰/变亮）</summary>
        private bool _lastAllowed;

        static AlarmTableElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AlarmTableElement),
                new FrameworkPropertyMetadata(typeof(AlarmTableElement)));
        }

        public AlarmTableElement()
        {
            // 命令先建：RefreshRows → RefreshPermissionState 会 RaiseCanExecuteChanged。
            AcknowledgeAllCommand = new DelegateCommand(AcknowledgeAll, () => CanAcknowledgeAll);

            RefreshRows(force: true);
        }

        /// <summary>要显示的行（只读，见 <see cref="RowsProperty"/>）</summary>
        public IReadOnlyList<AlarmTableRow> Rows => (IReadOnlyList<AlarmTableRow>)GetValue(RowsProperty);

        /// <summary>表里有没有行（只读）</summary>
        public bool HasRows => (bool)GetValue(HasRowsProperty);

        /// <summary>引擎挂着的报警总条数（只读，未截断）</summary>
        public int ActiveCount => (int)GetValue(ActiveCountProperty);

        /// <summary>行列表显隐（只读）</summary>
        public Visibility RowsVisibility => (Visibility)GetValue(RowsVisibilityProperty);

        /// <summary>正文提示显隐（只读）</summary>
        public Visibility BodyVisibility => (Visibility)GetValue(BodyVisibilityProperty);

        /// <summary>正文提示文案（只读）</summary>
        public string BodyText => (string)GetValue(BodyTextProperty);

        /// <summary>工具条显隐（只读）</summary>
        public Visibility ToolbarVisibility => (Visibility)GetValue(ToolbarVisibilityProperty);

        /// <summary>未确认计数文案（只读）</summary>
        public string UnacknowledgedText => (string)GetValue(UnacknowledgedTextProperty);

        /// <summary>页脚截断提示（只读）</summary>
        public string FooterText => (string)GetValue(FooterTextProperty);

        /// <summary>页脚截断提示显隐（只读）</summary>
        public Visibility FooterVisibility => (Visibility)GetValue(FooterVisibilityProperty);

        /// <summary>最近一次确认结果 / 权限原因（只读）</summary>
        public string StatusText => (string)GetValue(StatusTextProperty);

        /// <summary>状态行显隐（只读）</summary>
        public Visibility StatusVisibility => (Visibility)GetValue(StatusVisibilityProperty);

        /// <summary>「全部确认」此刻能不能按（只读）</summary>
        public bool CanAcknowledgeAll => (bool)GetValue(CanAcknowledgeAllProperty);

        /// <summary>「时间」列显隐（只读；表头绑它）</summary>
        public Visibility TimeVisibility => (Visibility)GetValue(TimeVisibilityProperty);

        /// <summary>「级别」列显隐（只读；表头绑它）</summary>
        public Visibility SeverityVisibility => (Visibility)GetValue(SeverityVisibilityProperty);

        /// <summary>「确认」列显隐（只读；表头绑它）</summary>
        public Visibility AckVisibility => (Visibility)GetValue(AckVisibilityProperty);

        /// <summary>工具条上的「全部确认」（与行内那颗走同一个门禁与同一种结果提示）</summary>
        public DelegateCommand AcknowledgeAllCommand { get; }

        /// <summary>
        /// 推一拍：重读引擎的当前报警并（必要时）重建行。
        ///
        /// <b>节拍回调、断言与"手动刷一下"共用这一个入口</b>——门禁、指纹、行重建都只写在这一处，
        /// 断言不必去伪造 DispatcherTimer 的 Tick。
        /// </summary>
        public void Sample() => RefreshRows();

        /// <summary>
        /// 确认某一条报警（行内按钮与断言共用这一个入口）。
        /// 返回是否真的发生了态迁移；失败原因写在 <see cref="StatusText"/> 上。
        /// </summary>
        public bool AcknowledgeOne(Guid alarmId)
        {
            if (alarmId == Guid.Empty)
                return false;

            if (!CheckCanAcknowledge(out var reason))
            {
                SetStatus(reason);
                return false;
            }

            var alarms = RuntimeContext!.Alarms;
            int changed = alarms.Acknowledge(alarmId);

            if (changed > 0)
            {
                SetStatus("已确认 1 条");
            }
            else
            {
                // ★ 0 有两种意思，必须分开说（一律回"确认失败"会让操作员以为系统坏了）：
                //   还在列表里 = 早就按过了；不在列表里 = 这条已经恢复并被清除。
                bool stillListed = alarms.ActiveAlarms.Any(a => a.AlarmId == alarmId);

                SetStatus(stillListed
                    ? "这条报警已经确认过了"
                    : "这条报警已经不在了（可能已恢复并被清除）");
            }

            // 引擎的 AlarmChanged 也会走一遍 RefreshRows；这里再强制一次，
            // 是为了覆盖"引擎没抛事件"的实现（契约要求抛，但结果提示不能依赖它）。
            RefreshRows(force: true);
            return changed > 0;
        }

        /// <summary>确认全部待确认报警（工具条按钮与断言共用这一个入口）</summary>
        public void AcknowledgeAll()
        {
            if (!CheckCanAcknowledge(out var reason))
            {
                SetStatus(reason);
                return;
            }

            var alarms = RuntimeContext!.Alarms;
            int changed = alarms.AcknowledgeAll();

            if (changed > 0)
            {
                SetStatus($"已确认 {changed} 条");
            }
            else
            {
                // 同样是 0 的两种意思：一条报警都没有 ≠ 有报警但都确认过了。
                SetStatus(alarms.ActiveAlarms.Count == 0
                    ? "当前没有报警"
                    : "没有待确认的报警（都确认过了）");
            }

            RefreshRows(force: true);
        }

        protected override void OnElementRefreshed()
        {
            ApplyStrokeInset(1);
            UpdateColumnVisibility();
            RefreshRows(); // 数据没变就不重建（指纹短路）
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
            {
                oldContext.Beat.Beat -= OnBeat;
                oldContext.Alarms.AlarmRaised -= OnAlarmEvent;
                oldContext.Alarms.AlarmChanged -= OnAlarmEvent;
                oldContext.Alarms.AlarmCleared -= OnAlarmEvent;
            }

            if (newContext != null)
            {
                newContext.Beat.Beat += OnBeat;
                newContext.Alarms.AlarmRaised += OnAlarmEvent;
                newContext.Alarms.AlarmChanged += OnAlarmEvent;
                newContext.Alarms.AlarmCleared += OnAlarmEvent;
            }

            // 换了一轮（装上 / 摘掉 / 换另一轮）：旧指纹作废，立刻整表重读一次。
            // 不这么做的话，"装上上下文之前就已经在报的报警"要等到下一次变量变化才出现。
            _fingerprint = string.Empty;
            RefreshRows(force: true);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 数据可能先于模板到达（上下文在造控件之后才注入），套模板时补摆一次。
            UpdateColumnVisibility();
            RefreshRows();
        }

        // ---- 配置变更 → 重建（这些是"输入变了但指纹不变"的情形，必须强制） ----

        partial void OnMaxRowsPropertyChanged() => RefreshRows(force: true);

        partial void OnAllowAckPropertyChanged()
        {
            UpdateColumnVisibility();
            RefreshRows(force: true);
        }

        partial void OnShowTimePropertyChanged()
        {
            UpdateColumnVisibility();
            RefreshRows(force: true);
        }

        partial void OnShowSeverityPropertyChanged()
        {
            UpdateColumnVisibility();
            RefreshRows(force: true);
        }

        partial void OnZebraPropertyChanged() => RefreshRows(force: true);

        partial void OnEmptyTextPropertyChanged() => RefreshRows(force: true);

        partial void OnShowToolbarPropertyChanged()
            => SetValue(ToolbarVisibilityPropertyKey, ShowToolbar ? Visibility.Visible : Visibility.Collapsed);

        /// <summary>节拍：每拍重读一次（指纹没变时什么都不会重建）</summary>
        private void OnBeat(TimeSpan elapsed) => RefreshRows();

        /// <summary>引擎有任何动静 → 切回 UI 线程重读（三个事件共用）</summary>
        private void OnAlarmEvent(ScadaAlarmRecord record) => RunOnUi(() => RefreshRows());

        /// <summary>
        /// 重读引擎并（必要时）重建行。
        ///
        /// <b>指纹短路是这里最重要的一行</b>：没变就直接返回，连行对象都不造新的——
        /// 否则每拍换掉整表，会把操作员正按着的「确认」按钮连行一起换走。
        /// </summary>
        /// <param name="force">true = 跳过指纹比对（配置变了 / 自己刚确认完）</param>
        private void RefreshRows(bool force = false)
        {
            var context = RuntimeContext;
            var alarms = context?.Alarms;
            var active = alarms?.ActiveAlarms ?? Array.Empty<ScadaAlarmRecord>();

            bool allowed = context != null && context.AccessPolicy.CanElement(Element);
            if (allowed != _lastAllowed)
            {
                _lastAllowed = allowed;
                force = true; // 权限变了：行内按钮要跟着变灰/变亮
            }

            int unacknowledged = alarms?.UnacknowledgedCount ?? 0;
            string fingerprint = BuildFingerprint(active, unacknowledged);

            if (!force && string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
            {
                // 数据没变：一个行对象都不重建，只把"与数据无关、与身份有关"的那两样对齐。
                RefreshPermissionState(allowed);
                return;
            }

            _fingerprint = fingerprint;

            int max = Math.Clamp(MaxRows, MinRows, MaxRowsLimit);
            int shown = Math.Min(max, active.Count);

            var rows = new List<AlarmTableRow>(shown);
            for (int i = 0; i < shown; i++)
                rows.Add(CreateRow(active[i], i, allowed));

            SetValue(RowsPropertyKey, (IReadOnlyList<AlarmTableRow>)rows);
            SetValue(ActiveCountPropertyKey, active.Count);
            SetValue(HasRowsPropertyKey, rows.Count > 0);
            SetValue(RowsVisibilityPropertyKey, rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed);
            SetValue(BodyVisibilityPropertyKey, rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible);
            SetValue(BodyTextPropertyKey, BuildBodyText(rows.Count));
            SetValue(FooterTextPropertyKey,
                active.Count > rows.Count ? $"还有 {active.Count - rows.Count} 条" : string.Empty);
            SetValue(FooterVisibilityPropertyKey,
                active.Count > rows.Count ? Visibility.Visible : Visibility.Collapsed);
            SetValue(UnacknowledgedTextPropertyKey, BuildUnacknowledgedText(unacknowledged));
            SetValue(ToolbarVisibilityPropertyKey, ShowToolbar ? Visibility.Visible : Visibility.Collapsed);

            RefreshPermissionState(allowed);
        }

        /// <summary>
        /// 指纹：每条报警的 <c>AlarmId|State|Severity|TimeTicks</c> + 未确认条数。
        ///
        /// 为什么这四项就够：表里每一格要么来自这四项（时间/级别/状态/行是否存在），
        /// 要么来自配置（名字、列显隐——那几条各自有强制重建的钩子）。多拼一个字段只会让
        /// 短路更容易失效，等于回到"每拍整表重建"。
        /// </summary>
        private static string BuildFingerprint(IReadOnlyList<ScadaAlarmRecord> active, int unacknowledged)
        {
            var builder = new StringBuilder(active.Count * 48 + 8);

            builder.Append(unacknowledged.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < active.Count; i++)
            {
                var record = active[i];

                builder.Append(';')
                       .Append(record.AlarmId.ToString("N")).Append('|')
                       .Append((int)record.State).Append('|')
                       .Append((int)record.Severity).Append('|')
                       .Append(record.ActivatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>造一行（行对象是快照：每次重建都换新实例，所以不存在"行忘了跟着记录刷新"）</summary>
        private AlarmTableRow CreateRow(ScadaAlarmRecord record, int index, bool allowed)
        {
            var alarmId = record.AlarmId;

            // 门禁在构造期定死：行一旦建出来，可点性就是确定的（权限/状态变了会整表重建）。
            bool canAcknowledge = AllowAck && allowed && record.State.NeedsAcknowledge();
            var command = new DelegateCommand(() => AcknowledgeOne(alarmId), () => canAcknowledge);

            bool acknowledged = record.State == ScadaAlarmState.Acknowledged;

            return new AlarmTableRow(
                alarmId,
                record.ActivatedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(record.Name) ? record.Message : record.Name,
                record.Severity.DisplayName(),
                record.State.DisplayName(),
                SeverityBrush(record.Severity),
                canAcknowledge,
                command,
                ShowTime ? Visibility.Visible : Visibility.Collapsed,
                ShowSeverity ? Visibility.Visible : Visibility.Collapsed,
                AllowAck ? Visibility.Visible : Visibility.Collapsed,
                Zebra && index % 2 == 1 ? ZebraBrush : null,
                acknowledged ? AcknowledgedRowOpacity : 1d,
                acknowledged);
        }

        /// <summary>
        /// 正文文案的三分支。<b>设计期那一支刻意不写 <see cref="EmptyText"/></b>：
        /// 编辑器里没有引擎，"当前无报警"是一句没人为它负责的话。
        /// </summary>
        private string BuildBodyText(int shownRows)
        {
            if (RuntimeContext == null)
                return DesignTimeHint;

            if (shownRows > 0)
                return string.Empty;

            return string.IsNullOrWhiteSpace(EmptyText) ? DefaultEmptyText : EmptyText!;
        }

        /// <summary>工具条的未确认计数（设计期给"—"，不编一个 0）</summary>
        private string BuildUnacknowledgedText(int unacknowledged)
        {
            if (RuntimeContext == null)
                return "未确认 —";

            return unacknowledged > 0
                ? $"未确认 {unacknowledged} 条"
                : "全部已确认";
        }

        /// <summary>刷新"与身份有关"的两样：全部确认按钮的可用性 + 权限原因（每拍都做，一次策略调用）</summary>
        private void RefreshPermissionState(bool allowed)
        {
            SetValue(CanAcknowledgeAllPropertyKey, allowed && AllowAck);
            AcknowledgeAllCommand.RaiseCanExecuteChanged();

            if (RuntimeContext != null && !allowed)
                SetStatus(PermissionDeniedText);
            else if (string.Equals(StatusText, PermissionDeniedText, StringComparison.Ordinal))
                SetStatus(string.Empty); // 权限恢复后把上一次的拒绝原因收掉
            else
                SetValue(StatusVisibilityPropertyKey,
                    string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible);
        }

        /// <summary>写状态行（同时挂到控件 ToolTip 上：一行字放不下时悬停能看全）</summary>
        private void SetStatus(string? text)
        {
            var message = text ?? string.Empty;

            SetValue(StatusTextPropertyKey, message);
            SetValue(StatusVisibilityPropertyKey,
                message.Length == 0 ? Visibility.Collapsed : Visibility.Visible);

            ToolTip = message.Length == 0 ? null : message;
        }

        /// <summary>
        /// 三道门禁：有运行上下文、<see cref="AllowAck"/> 打开、当前角色过
        /// <see cref="IScadaAccessPolicy.CanElement"/>（权限键取自 <see cref="ScadaElement.RequiredPermission"/>）。
        /// 与 <see cref="AckButtonElement"/> 同一口径——同一个画面上两颗确认入口不该有两套规则。
        /// </summary>
        private bool CheckCanAcknowledge(out string? reason)
        {
            if (RuntimeContext == null)
            {
                reason = "设计期不能确认报警（运行后可用）";
                return false;
            }

            if (!AllowAck)
            {
                reason = "确认入口已关闭（允许逐条确认 = False）";
                return false;
            }

            if (!RuntimeContext.AccessPolicy.CanElement(Element))
            {
                reason = PermissionDeniedText;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>把三列的显隐算好（表头格绑它们；行里那份在重建行时算进 <see cref="AlarmTableRow"/>）</summary>
        private void UpdateColumnVisibility()
        {
            SetValue(TimeVisibilityPropertyKey, ShowTime ? Visibility.Visible : Visibility.Collapsed);
            SetValue(SeverityVisibilityPropertyKey, ShowSeverity ? Visibility.Visible : Visibility.Collapsed);
            SetValue(AckVisibilityPropertyKey, AllowAck ? Visibility.Visible : Visibility.Collapsed);
        }

        /// <summary>按严重度挑色（与报警条、多态灯同一套语汇；令牌优先，纯净宿主回落同值副本）</summary>
        private static Brush SeverityBrush(ScadaAlarmSeverity severity) => severity switch
        {
            ScadaAlarmSeverity.Critical => TokenBrush("HmiStateFaultBrush", FallbackCritical),
            ScadaAlarmSeverity.Warning => TokenBrush("HmiStateWarningBrush", FallbackWarning),
            _ => TokenBrush("HmiInfoBrush", FallbackInfo),
        };

        /// <summary>斑马纹底色（令牌优先，纯净宿主回落同值副本）</summary>
        private static Brush ZebraBrush => TokenBrush("HmiZebraBrush", FallbackZebra);

        /// <summary>
        /// 取主题令牌画刷；没有 Application（断言宿主 / 纯净宿主）时回落到同值冻结副本。
        ///
        /// 为什么不直接 <c>FindResource</c>：那在没有 Application 的环境里会抛，
        /// 而"画一行报警"不该因为宿主没装主题就失败。
        /// </summary>
        private static Brush TokenBrush(string key, Brush fallback)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback;

        /// <summary>
        /// 把一段动作送回本控件的 UI 线程执行（与报警条同一范式）：
        /// 已在 UI 线程就直跑；窗口正在关闭就丢掉（往已停摆的 Dispatcher 排队会抛）。
        /// </summary>
        private void RunOnUi(Action action)
        {
            var dispatcher = Dispatcher;

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }

    /// <summary>
    /// 报警汇总表上的一行。
    ///
    /// <b>为什么要有这个类，而不是让模板直接绑 <see cref="ScadaAlarmRecord"/></b>
    /// ---------
    /// 与 <see cref="AlarmBannerRow"/> 逐字同一条理由：记录里全是"事实"（严重度是枚举、时间是 UTC、
    /// 态是枚举），而模板要的是"画法"（这一行该用哪个画刷、时间该显成什么样、这颗按钮能不能按）。
    /// 把换算放进模板就得用转换器 + 跨层 <c>RelativeSource</c>——那两样在无窗口的断言环境里都不可靠，
    /// 出错时是"静默显示成默认色/永远可点"，最难查的一类问题。放在这里，换算变成一次构造、
    /// 一句断言就能钉住。
    ///
    /// <b>本类是快照</b>：记录是长命对象（从激活一路走到清除），这一行只表示"某一刻该怎么画"。
    /// 指纹一变整表重建，所以不存在"行忘了跟着记录刷新"这种陈旧显示——
    /// 也正因为如此，行里能安全地攥一颗<b>按 alarmId 闭包</b>的确认命令。
    ///
    /// 显隐与配色是<b>字段</b>而不是模板触发器：本环境下 <c>ControlTemplate.Triggers</c> 里的
    /// DataTrigger 一律不触发（静默失效），状态切换只能由控件算好、模板直绑。
    /// </summary>
    public sealed class AlarmTableRow
    {
        public AlarmTableRow(
            Guid alarmId,
            string timeText,
            string nameText,
            string severityText,
            string stateText,
            Brush severityBrush,
            bool canAcknowledge,
            ICommand acknowledgeCommand,
            Visibility timeVisibility,
            Visibility severityVisibility,
            Visibility acknowledgeVisibility,
            Brush? rowBackground,
            double rowOpacity,
            bool isAcknowledged)
        {
            AlarmId = alarmId;
            TimeText = timeText ?? string.Empty;
            NameText = nameText ?? string.Empty;
            SeverityText = severityText ?? string.Empty;
            StateText = stateText ?? string.Empty;
            SeverityBrush = severityBrush ?? Brushes.Transparent;
            CanAcknowledge = canAcknowledge;
            AcknowledgeCommand = acknowledgeCommand ?? throw new ArgumentNullException(nameof(acknowledgeCommand));
            TimeVisibility = timeVisibility;
            SeverityVisibility = severityVisibility;
            AcknowledgeVisibility = acknowledgeVisibility;
            RowBackground = rowBackground;
            RowOpacity = rowOpacity;
            IsAcknowledged = isAcknowledged;
        }

        /// <summary>来源报警定义的身份（确认时按它找回去）</summary>
        public Guid AlarmId { get; }

        /// <summary>激活时刻（本地时间，只到秒）</summary>
        public string TimeText { get; }

        /// <summary>报警名（配置里没写名字时回落报警文本）</summary>
        public string NameText { get; }

        /// <summary>级别：严重 / 警告 / 提示</summary>
        public string SeverityText { get; }

        /// <summary>态机状态：激活 / 已确认 / 已恢复未确认 / 正常</summary>
        public string StateText { get; }

        /// <summary>级别文字的颜色（按严重度挑好，模板只管画）</summary>
        public Brush SeverityBrush { get; }

        /// <summary>这颗「确认」此刻能不能按（<see cref="AlarmTableElement.AllowAck"/> + 权限 + 这条还没确认）</summary>
        public bool CanAcknowledge { get; }

        /// <summary>行内「确认」命令（每行一个实例，闭包捕获本行的 <see cref="AlarmId"/>）</summary>
        public ICommand AcknowledgeCommand { get; }

        /// <summary>「时间」列的显隐（<see cref="AlarmTableElement.ShowTime"/> 关掉时收起，列宽随之归零）</summary>
        public Visibility TimeVisibility { get; }

        /// <summary>「级别」列的显隐</summary>
        public Visibility SeverityVisibility { get; }

        /// <summary>「确认」列的显隐（<see cref="AlarmTableElement.AllowAck"/> 关掉时整列收起）</summary>
        public Visibility AcknowledgeVisibility { get; }

        /// <summary>隔行底色（<see cref="AlarmTableElement.Zebra"/> 关掉或偶数行时为 null = 透明）</summary>
        public Brush? RowBackground { get; }

        /// <summary>整行不透明度（已确认未恢复的行淡化，但<b>不消失</b>——确认 ≠ 恢复）</summary>
        public double RowOpacity { get; }

        /// <summary>这条是否已被操作员确认（模板不需要它，断言与"灰显"口径用）</summary>
        public bool IsAcknowledged { get; }
    }
}
