using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Prism.Commands;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 配方面板图元（TypeKey = <c>Hmi.RecipePanel</c>）：把"当前生效配方"整块显示出来，
    /// 运行态接了 <see cref="IScadaRecipeCommands"/> 时再长出一条"选 / 下载 / 上传 / 保存"工具条。
    ///
    /// <b>它解决的是配置成本，不是新数据</b>
    /// ---------
    /// 配方数据本身已经由宿主投影成 6 条只读系统变量（<c>System.Recipe.*</c>）。
    /// 但现场要的是"一块牌子"：配方名、版本、状态、以及逐项参数。用现有图元拼一遍要摆
    /// 一个文本 + 一个数值域 + 一张数据表格，而数据表格是"按采行信号攒行"的记录控件，
    /// 用它显示配方参数属于误用（它等的是上升沿，不是一份静态清单）。
    /// 本图元一次把这几样画齐，且<b>不需要在设计期一条条绑变量</b>。
    ///
    /// <b>三条边界</b>
    /// ---------
    /// ① <b>只读展示</b>：面板本身不写 <c>System.Recipe.*</c>，也没有编辑态。改配方只能走主软件的
    ///    配方管理器，切换只能走按钮上的「应用配方」动作。运行态多出来的四个操作<b>全部委托</b>给
    ///    宿主（<see cref="IScadaRecipeCommands"/>），面板只负责入口、门禁与反馈。
    /// ② <b>变量名是硬约定</b>：直接读 <c>System.Recipe.*</c>，名字与格式化口径都收在
    ///    <see cref="ScadaRecipePanelData"/> 一处（Web 运行态也调它，两端不会各说各话）。
    /// ③ <b>设计态显示占位</b>：没有运行上下文时不显示 0/空配方，而是"配方数据未接入"，
    ///    与轴面板"未接入运动数据"同一口径（不把"不知道"伪装成一个看起来正常的值）。
    ///
    /// <b>升级：四个操作的语义与边界</b>
    /// ---------
    /// · <b>选择</b>：只改"面板上选中哪一份"（<see cref="SelectedRecipeId"/>，控件自己的 UI 状态，
    ///   <b>不落盘、不进模型、不动设备</b>）；
    /// · <b>下载</b>：选中配方 → 设备（宿主 Apply，含权限/运行闸/预检/回滚/终检/审计）；
    /// · <b>上传</b>：设备当前值 → 覆盖选中配方条目（现场调好了存回配方）；
    /// · <b>保存</b>：方案落盘。
    /// <b>没有 <see cref="ScadaRuntimeContext.RecipeCommands"/> 时工具条整条不出现</b>
    /// （<see cref="ToolbarVisibility"/> = Collapsed），面板退化成升级前的只读铭牌，行为一行不变。
    ///
    /// <b>为什么工具条显隐走只读依赖属性，而不是模板里的 DataTrigger</b>
    /// ---------
    /// 本环境下 <c>ControlTemplate.Triggers</c> 里的 DataTrigger 不触发（见 <c>ReportElement</c> /
    /// <c>PictureElement</c> 的同一条注释）。控件侧算好 <see cref="Visibility"/>，模板
    /// <c>TemplateBinding</c> 过去，一定生效。
    ///
    /// <b>状态机（四个操作共用一条）</b>
    /// ---------
    /// <list type="bullet">
    /// <item>进入命令：<see cref="IsBusy"/> = true → 三个按钮 + 下拉全部禁用、状态行「处理中…」；</item>
    /// <item>成功：状态行写成功文案（<c>ok</c> 绿），并起一个 2 秒的 <see cref="DispatcherTimer"/>
    ///   到点收起——<b>不用 Thread.Sleep</b>（那会把 UI 线程钉住）；</item>
    /// <item>失败：状态行写宿主给的原因（<c>danger</c> 红），<b>不</b>自动收起（要让操作员看清）；</item>
    /// <item>宿主抛异常：catch 住写「操作失败：…」并记一条审计，<b>绝不把异常抛到 UI 线程</b>；</item>
    /// <item><c>finally</c> 里恢复 <see cref="IsBusy"/> 与按钮可用性——无论成败。</item>
    /// </list>
    /// <b>连点</b>由两道闸挡住：<c>IsBusy</c> 期间三个命令的 <c>CanExecute</c> 为 false，
    /// 且命令体第一行就 <c>if (IsBusy) return;</c>（断言/脚本直接调方法时也走这一条）。
    ///
    /// <b>权限</b>：与 <see cref="AckButtonElement"/> 同口径——<see cref="ScadaRuntimeContext.AccessPolicy"/>
    /// 的 <c>CanElement</c>，每拍重算；无权限时三个按钮禁用、ToolTip 写「当前账号没有操作权限」。
    ///
    /// <b>数据刷新</b>：配方列表是配置型数据（没有变更通知），每拍（<see cref="ScadaRuntimeContext.Beat"/>）
    /// 重读一次，用指纹短路——只有 Id/Name/EntryCount/IsActive 真的变了才重建下拉项，
    /// 否则会把操作员正在展开的下拉打断（照 <c>AxisPanelElement.RefreshRows</c> 的写法）。
    ///
    /// 刷新走全画面统一节拍（与数据表格同一手法）：每拍现解析变量、现格式化，
    /// 只有文本真的变了才写依赖属性——配方本身很少变，但"偏离数"会随参数被改而变。
    /// </summary>
    public partial class RecipePanelElement : ScadaElementBase
    {
        // 声明次序 = 属性面板行序：标题 → 数据开关 → 行数。

        /// <summary>面板标题（如「当前配方」）</summary>
        [ScadaProperty(Key = "Title", DisplayName = "标题", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "当前配方",
                       Description = "面板左上角的一行字；留空则整行收起，只显示配方名与状态")]
        public partial string? Title { get; set; }

        /// <summary>是否显示逐项参数明细</summary>
        [ScadaProperty(Key = "ShowDetails", DisplayName = "显示参数明细", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "打开时在状态行下面列出配方里的每一项参数；" +
                                     "关掉只留「配方名 · 版本 + 状态」，适合只想要一块小铭牌的场合")]
        public partial bool ShowDetails { get; set; }

        /// <summary>参数明细最多显示多少行</summary>
        [ScadaProperty(Key = "MaxRows", DisplayName = "最多显示行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "12",
                       Min = ScadaRecipePanelData.MinRows, Max = ScadaRecipePanelData.MaxRowsLimit,
                       Description = "超出的部分折成一行「… 共 N 项」；配方参数通常十几项，12 行够看")]
        public partial int MaxRows { get; set; }

        // ==================================================================
        //  操作状态文案（断言与模板共用一份字面量，别在断言里手抄字符串）
        // ==================================================================

        /// <summary>命令执行期间的状态行文案</summary>
        public const string StatusTextProcessing = "处理中…";

        /// <summary>方案里一份配方都没有时的状态行文案</summary>
        public const string StatusTextNoRecipes = "方案里还没有配方";

        /// <summary>无操作权限时按钮的 ToolTip 与点击后的状态行文案</summary>
        public const string StatusTextNoPermission = "当前账号没有操作权限";

        /// <summary>宿主抛异常时的状态行前缀（后面接 <c>ex.Message</c>）</summary>
        public const string StatusTextFailurePrefix = "操作失败：";

        /// <summary>保存成功文案</summary>
        public const string StatusTextSaved = "已保存";

        /// <summary>成功文案停留时长（毫秒）——到点由 <see cref="DispatcherTimer"/> 收起</summary>
        public const int StatusFadeDelayMilliseconds = 2000;

        /// <summary>指纹哨兵：与"空列表"的指纹（空串）区分开，保证第一次刷新一定会走一遍</summary>
        private const string UninitializedFingerprint = "<none>";

        private static readonly DependencyPropertyKey HeaderTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HeaderText), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>主行文案：配方名 · 版本（无配方时是「未应用配方」/「配方已丢失」）</summary>
        public static readonly DependencyProperty HeaderTextProperty = HeaderTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>状态文案（已生效 / 偏离 N / 未应用 / 未启用 / 配方悬空）</summary>
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusKeyPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusKey), typeof(string), typeof(RecipePanelElement),
            new PropertyMetadata(ScadaRecipePanelData.StatusKeyMuted));

        /// <summary>状态键（模板据此挑色）：ok / warn / danger / muted</summary>
        public static readonly DependencyProperty StatusKeyProperty = StatusKeyPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<ScadaRecipePanelLine>), typeof(RecipePanelElement),
            new PropertyMetadata(Array.Empty<ScadaRecipePanelLine>()));

        /// <summary>参数明细行（已按 <see cref="MaxRows"/> 截断）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasRowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasRows), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>有没有明细行可显示（空态提示据此显隐）</summary>
        public static readonly DependencyProperty HasRowsProperty = HasRowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyHintPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyHint), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>空态提示（没有配方 / 没有明细 / 数据未接入）</summary>
        public static readonly DependencyProperty EmptyHintProperty = EmptyHintPropertyKey.DependencyProperty;

        // ==================================================================
        //  升级：工具条 / 下拉 / 状态行（全部只读，控件侧算好，模板只绑）
        // ==================================================================

        private static readonly DependencyPropertyKey ToolbarVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ToolbarVisibility), typeof(Visibility), typeof(RecipePanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>
        /// 工具条显隐：<c>RuntimeContext?.RecipeCommands != null</c> 才显示。
        /// 为 Collapsed 时面板与升级前完全一致（模板只绑它，不写 ControlTemplate DataTrigger）。
        /// </summary>
        public static readonly DependencyProperty ToolbarVisibilityProperty =
            ToolbarVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey RecipeOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(RecipeOptions), typeof(IReadOnlyList<ScadaRecipeOption>), typeof(RecipePanelElement),
            new PropertyMetadata(Array.Empty<ScadaRecipeOption>()));

        /// <summary>下拉里的候选配方（宿主给的那份投影；指纹没变时<b>同一个列表对象</b>不重建）</summary>
        public static readonly DependencyProperty RecipeOptionsProperty = RecipeOptionsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasRecipesPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasRecipes), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>方案里有没有配方（空表时下拉禁用、状态行写「方案里还没有配方」）</summary>
        public static readonly DependencyProperty HasRecipesProperty = HasRecipesPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SelectionTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectionText), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>标题下方那行"已选：名字（N 项）"；没选中时是空串（模板收起该行）</summary>
        public static readonly DependencyProperty SelectionTextProperty = SelectionTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SelectionVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectionVisibility), typeof(Visibility), typeof(RecipePanelElement),
            new PropertyMetadata(Visibility.Collapsed));

        /// <summary>已选摘要那一行的显隐（空串时收起，不留一条空白）</summary>
        public static readonly DependencyProperty SelectionVisibilityProperty =
            SelectionVisibilityPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey OperationStatusPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(OperationStatus), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>操作状态行（处理中… / 已下载：… / 宿主给的原因 / 操作失败：…）；空串时模板收起</summary>
        public static readonly DependencyProperty OperationStatusProperty =
            OperationStatusPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey OperationStatusKeyPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(OperationStatusKey), typeof(string), typeof(RecipePanelElement),
                new PropertyMetadata(ScadaRecipePanelData.StatusKeyMuted));

        /// <summary>操作状态行的颜色档：ok（成功）/ danger（失败）/ muted（进行中、提示）</summary>
        public static readonly DependencyProperty OperationStatusKeyProperty =
            OperationStatusKeyPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsBusyPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsBusy), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>有命令正在执行（四个入口全部禁用；也是"连点"的第二道闸）</summary>
        public static readonly DependencyProperty IsBusyProperty = IsBusyPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanOperatePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanOperate), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>此刻能不能操作（有命令出口 + 权限够 + 不忙）；三个按钮的 <c>CanExecute</c> 都以它为底</summary>
        public static readonly DependencyProperty CanOperateProperty = CanOperatePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanSelectPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanSelect), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>下拉可用（可操作 + 列表非空）</summary>
        public static readonly DependencyProperty CanSelectProperty = CanSelectPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanDownloadPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanDownload), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>「下载」可用（可操作 + 已选中配方）</summary>
        public static readonly DependencyProperty CanDownloadProperty = CanDownloadPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanUploadPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanUpload), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>「上传」可用（可操作 + 已选中配方）</summary>
        public static readonly DependencyProperty CanUploadProperty = CanUploadPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanSavePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanSave), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>「保存」可用（可操作即可，不依赖选中项）</summary>
        public static readonly DependencyProperty CanSaveProperty = CanSavePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PermissionToolTipPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PermissionToolTip), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(null));

        /// <summary>三个按钮的 ToolTip：无权限时是「当前账号没有操作权限」，有权限时 null（不弹空提示框）</summary>
        public static readonly DependencyProperty PermissionToolTipProperty =
            PermissionToolTipPropertyKey.DependencyProperty;

        // ==================================================================
        //  选中态（控件自己的 UI 状态：不落盘、不进模型）
        // ==================================================================

        /// <summary>运行期选中的配方 Id（下拉 TwoWay 同步；设计期不落盘）</summary>
        public static readonly DependencyProperty SelectedRecipeIdProperty = DependencyProperty.Register(
            nameof(SelectedRecipeId), typeof(Guid), typeof(RecipePanelElement),
            new PropertyMetadata(Guid.Empty, OnSelectedRecipeIdChanged));

        private static readonly DependencyProperty SelectedOptionProperty = DependencyProperty.Register(
            nameof(SelectedOption), typeof(ScadaRecipeOption), typeof(RecipePanelElement),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedOptionChanged));

        /// <summary>
        /// 下拉的选中项（与 <see cref="SelectedRecipeId"/> 双向同步）。
        ///
        /// 为什么模板绑 <c>SelectedItem</c> 而不是 <c>SelectedValue</c>：<c>SelectedValue</c> 在
        /// "ItemsSource 后到"时会丢掉选择（模板套上那一刻 Id 已经有值，但候选还没绑过去，
        /// ComboBox 找不到对应项就把选中置空，屏幕上是一个空的选择框——与
        /// <c>AxisPanelElement.SelectedAxisItem</c> 同一条结论）。<c>SelectedItem</c> 绑的是对象本身，
        /// Selector 会在 ItemsSource 到位后重新解析它。
        /// </summary>
        public ScadaRecipeOption? SelectedOption
        {
            get => (ScadaRecipeOption?)GetValue(SelectedOptionProperty);
            set => SetValue(SelectedOptionProperty, value);
        }

        // ---- 命令（模板按 ReportElement 的写法绑 Command + IsEnabled） ----

        /// <summary>下载：选中配方 → 设备</summary>
        public DelegateCommand DownloadCommand { get; }

        /// <summary>上传：设备当前值 → 覆盖选中配方条目</summary>
        public DelegateCommand UploadCommand { get; }

        /// <summary>保存：方案落盘</summary>
        public DelegateCommand SaveCommand { get; }

        // ---- 运行状态 ----

        /// <summary>上一次刷新时配方列表的指纹（Id|Name|EntryCount|IsActive）</summary>
        private string _optionsFingerprint = UninitializedFingerprint;

        /// <summary>选中态正在由控件自己写（挡住两个回调互相递归）</summary>
        private bool _applyingSelection;

        private DispatcherTimer? _statusTimer;

        static RecipePanelElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(RecipePanelElement),
                new FrameworkPropertyMetadata(typeof(RecipePanelElement)));
        }

        public RecipePanelElement()
        {
            DownloadCommand = new DelegateCommand(Download, () => CanDownload);
            UploadCommand = new DelegateCommand(Upload, () => CanUpload);
            SaveCommand = new DelegateCommand(Save, () => CanSave);

            // 窗口卸载时把 2 秒定时器停掉：DispatcherTimer 持有回调 → 回调持有控件，
            // 不停的话一个已关闭的画面会一直挂在调度器上。
            Unloaded += (_, _) => _statusTimer?.Stop();

            Refresh();
        }

        public string HeaderText => (string)GetValue(HeaderTextProperty);

        public string StatusText => (string)GetValue(StatusTextProperty);

        public string StatusKey => (string)GetValue(StatusKeyProperty);

        public IReadOnlyList<ScadaRecipePanelLine> Rows => (IReadOnlyList<ScadaRecipePanelLine>)GetValue(RowsProperty);

        public bool HasRows => (bool)GetValue(HasRowsProperty);

        public string EmptyHint => (string)GetValue(EmptyHintProperty);

        public Visibility ToolbarVisibility => (Visibility)GetValue(ToolbarVisibilityProperty);

        public IReadOnlyList<ScadaRecipeOption> RecipeOptions
            => (IReadOnlyList<ScadaRecipeOption>)GetValue(RecipeOptionsProperty);

        public bool HasRecipes => (bool)GetValue(HasRecipesProperty);

        public string SelectionText => (string)GetValue(SelectionTextProperty);

        public Visibility SelectionVisibility => (Visibility)GetValue(SelectionVisibilityProperty);

        public string OperationStatus => (string)GetValue(OperationStatusProperty);

        public string OperationStatusKey => (string)GetValue(OperationStatusKeyProperty);

        public bool IsBusy => (bool)GetValue(IsBusyProperty);

        public bool CanOperate => (bool)GetValue(CanOperateProperty);

        public bool CanSelect => (bool)GetValue(CanSelectProperty);

        public bool CanDownload => (bool)GetValue(CanDownloadProperty);

        public bool CanUpload => (bool)GetValue(CanUploadProperty);

        public bool CanSave => (bool)GetValue(CanSaveProperty);

        public string? PermissionToolTip => (string?)GetValue(PermissionToolTipProperty);

        public Guid SelectedRecipeId
        {
            get => (Guid)GetValue(SelectedRecipeIdProperty);
            set => SetValue(SelectedRecipeIdProperty, value);
        }

        /// <summary>
        /// 运行上下文换人：挂上新的节拍并立刻刷一次，退掉旧的。
        /// 与数据表格同一理由（订阅跟着上下文走，不跟着 Loaded 走——断言宿主没有 Loaded）。
        /// </summary>
        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            // 换一轮运行：上一轮的"处理中"与 2 秒定时器一并作废。
            // 不做这一步的话，上一轮死在命令里的 IsBusy 会把新一轮的四个入口永久钉成禁用。
            _statusTimer?.Stop();
            SetValue(IsBusyPropertyKey, false);
            SetValue(OperationStatusPropertyKey, string.Empty);
            SetValue(OperationStatusKeyPropertyKey, ScadaRecipePanelData.StatusKeyMuted);
            _optionsFingerprint = UninitializedFingerprint;

            Refresh();
        }

        protected override void OnElementRefreshed() => Refresh();

        partial void OnShowDetailsPropertyChanged() => Refresh();
        partial void OnMaxRowsPropertyChanged() => Refresh();
        partial void OnTitlePropertyChanged() => Refresh();

        private void OnBeat(TimeSpan elapsed) => Refresh();

        /// <summary>
        /// 从当前运行上下文读一次配方并重画（只读展示 + 工具条状态）。
        ///
        /// <b>public 是给断言用的</b>：读值 → 组文案 → 截断全在一次里；
        /// 让断言直接推一拍，比在断言宿主里养一个真定时器可靠得多（与数据表格的 <c>Sample</c> 同一口径）。
        /// </summary>
        public void Refresh()
        {
            var snapshot = ScadaRecipePanelData.Read(RuntimeContext?.Values, ShowDetails, MaxRows);

            Apply(snapshot.Header, snapshot.Status, snapshot.StatusKey, snapshot.Rows, snapshot.Hint);

            RefreshRecipeOptions(force: false);
            RecomputeCommandState();
        }

        private void Apply(string header, string status, string key,
                           IReadOnlyList<ScadaRecipePanelLine> rows, string hint)
        {
            if (!string.Equals(HeaderText, header, StringComparison.Ordinal))
                SetValue(HeaderTextPropertyKey, header);

            if (!string.Equals(StatusText, status, StringComparison.Ordinal))
                SetValue(StatusTextPropertyKey, status);

            if (!string.Equals(StatusKey, key, StringComparison.Ordinal))
                SetValue(StatusKeyPropertyKey, key);

            if (!RowsEqual(Rows, rows))
            {
                SetValue(RowsPropertyKey, rows);
                SetValue(HasRowsPropertyKey, rows.Count > 0);
            }

            if (!string.Equals(EmptyHint, hint, StringComparison.Ordinal))
                SetValue(EmptyHintPropertyKey, hint);
        }

        private static bool RowsEqual(IReadOnlyList<ScadaRecipePanelLine> left, IReadOnlyList<ScadaRecipePanelLine> right)
        {
            if (left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal)
                    || left[i].IsTruncated != right[i].IsTruncated)
                    return false;
            }

            return true;
        }

        // ==================================================================
        //  四个操作（鼠标点击与断言共用同一入口，同 AckButtonElement.Acknowledge）
        // ==================================================================

        /// <summary>下载选中配方到设备（点击与断言共用；<c>IsBusy</c> 期间再进来直接返回 = 防连点）</summary>
        public void Download()
        {
            if (IsBusy || RuntimeContext?.RecipeCommands is not { } commands || !EnsureOperable())
                return;

            var option = FindOption(SelectedRecipeId);
            if (option == null)
            {
                SetOperationStatus("还没有选中配方", ScadaRecipePanelData.StatusKeyMuted);
                return;
            }

            RunCommand(
                "下载配方",
                () =>
                {
                    bool ok = commands.TryDownload(option.RecipeId, out var error);
                    return (ok, error);
                },
                () => $"已下载：{option.Name}（{option.EntryCount} 项）",
                refreshAfter: false);
        }

        /// <summary>把设备当前值写回选中配方（点击与断言共用）</summary>
        public void Upload()
        {
            if (IsBusy || RuntimeContext?.RecipeCommands is not { } commands || !EnsureOperable())
                return;

            var option = FindOption(SelectedRecipeId);
            if (option == null)
            {
                SetOperationStatus("还没有选中配方", ScadaRecipePanelData.StatusKeyMuted);
                return;
            }

            RunCommand(
                "上传配方",
                () =>
                {
                    bool ok = commands.TryUpload(option.RecipeId, out var error);
                    return (ok, error);
                },
                () => $"已上传：{option.Name}（设备当前值已写回配方）",
                // 上传会改条目值（条目数一般不变，但宿主可能顺手重排/改备注）：
                // 强制重读一次快照，别让面板停在旧指纹上。
                refreshAfter: true);
        }

        /// <summary>方案落盘（点击与断言共用）</summary>
        public void Save()
        {
            if (IsBusy || RuntimeContext?.RecipeCommands is not { } commands || !EnsureOperable())
                return;

            RunCommand(
                "保存方案",
                () =>
                {
                    bool ok = commands.TrySave(out var error);
                    return (ok, error);
                },
                () => StatusTextSaved,
                refreshAfter: false);
        }

        /// <summary>
        /// 四个操作共用的执行壳：置忙 → 调宿主 → 写状态行 → 记审计 → 恢复。
        ///
        /// 三条纪律：
        /// ① 异常不许冒到 UI 线程（宿主的接口约定不抛，但控件不能假设）；
        /// ② 恢复一律在 <c>finally</c>（成败都要把 <see cref="IsBusy"/> 放开，
        ///    否则一次失败会把面板永久锁死）；
        /// ③ 成功文案 2 秒后由定时器收起，失败原因<b>不</b>自动收起。
        /// </summary>
        private void RunCommand(string action, Func<(bool Ok, string? Error)> body,
                                Func<string> successText, bool refreshAfter)
        {
            if (IsBusy)
                return;

            SetValue(IsBusyPropertyKey, true);
            SetOperationStatus(StatusTextProcessing, ScadaRecipePanelData.StatusKeyMuted);
            RecomputeCommandState(); // CanExecute → false（连点的第一道闸）

            bool ok = false;
            string detail = string.Empty;

            try
            {
                var (succeeded, error) = body();
                ok = succeeded;
                detail = ok ? successText() : error ?? "操作失败";

                SetOperationStatus(detail, ok ? ScadaRecipePanelData.StatusKeyOk : ScadaRecipePanelData.StatusKeyDanger);

                if (ok)
                    ScheduleStatusFade();
            }
            catch (Exception ex)
            {
                // 宿主实现抛了：收住、上屏、留痕，绝不 rethrow（那会带崩整个 UI 线程）。
                ok = false;
                detail = StatusTextFailurePrefix + ex.Message;
                SetOperationStatus(detail, ScadaRecipePanelData.StatusKeyDanger);
            }
            finally
            {
                SetValue(IsBusyPropertyKey, false);

                // 走到这里还停在「处理中…」说明 try 里没来得及写结论（理论上到不了），
                // 与其把"处理中"永远挂在屏上，不如清掉。
                if (string.Equals(OperationStatus, StatusTextProcessing, StringComparison.Ordinal))
                    SetOperationStatus(string.Empty, ScadaRecipePanelData.StatusKeyMuted);

                RecomputeCommandState();
            }

            WriteAudit(action, ok ? "成功" : "失败", detail);

            if (ok && refreshAfter)
                Refresh();
        }

        /// <summary>权限门禁（与 AckButtonElement 同口径：<c>AccessPolicy.CanElement(Element)</c>）</summary>
        private bool EnsureOperable()
        {
            var context = RuntimeContext;
            if (context == null)
                return false;

            if (context.AccessPolicy.CanElement(Element))
                return true;

            SetOperationStatus(StatusTextNoPermission, ScadaRecipePanelData.StatusKeyDanger);
            return false;
        }

        /// <summary>成功文案的 2 秒收起（DispatcherTimer，不用 Thread.Sleep——那会钉住 UI 线程）</summary>
        private void ScheduleStatusFade()
        {
            if (_statusTimer == null)
            {
                _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(StatusFadeDelayMilliseconds),
                };
                _statusTimer.Tick += OnStatusFadeTick;
            }

            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void OnStatusFadeTick(object? sender, EventArgs e)
        {
            _statusTimer?.Stop();

            // 只收成功文案：失败原因要留在屏上让操作员看清（不该 2 秒后自己消失）。
            if (string.Equals(OperationStatusKey, ScadaRecipePanelData.StatusKeyOk, StringComparison.Ordinal))
                SetOperationStatus(string.Empty, ScadaRecipePanelData.StatusKeyMuted);
        }

        private void SetOperationStatus(string text, string key)
        {
            text ??= string.Empty;

            if (!string.Equals(OperationStatus, text, StringComparison.Ordinal))
                SetValue(OperationStatusPropertyKey, text);

            if (!string.Equals(OperationStatusKey, key, StringComparison.Ordinal))
                SetValue(OperationStatusKeyPropertyKey, key);
        }

        /// <summary>写审计（没有审计出口就跳过；审计失败不阻断动作，与 IScadaAuditSink 的约定一致）</summary>
        private void WriteAudit(string action, string result, string detail)
        {
            var audit = RuntimeContext?.Audit;
            if (audit == null)
                return;

            try
            {
                audit.Append(
                    RuntimeContext!.AccessPolicy.CurrentUserName ?? "未登录",
                    string.Empty,
                    string.Empty,
                    string.IsNullOrWhiteSpace(Title) ? "配方" : Title!,
                    "点击",
                    action,
                    result,
                    detail);
            }
            catch
            {
                // 审计落盘失败不阻断：与 IScadaAuditSink 的约定一致
            }
        }

        // ==================================================================
        //  配方列表（配置型数据：每拍重读 + 指纹短路）
        // ==================================================================

        /// <summary>
        /// 重读宿主给的配方列表，变了才重建下拉项。
        ///
        /// 配方列表是**配置**（<c>IReadOnlyList&lt;ScadaRecipeOption&gt;</c>，没有变更通知），
        /// 所以每拍重读一次、靠指纹短路：没变时连列表对象都不换，否则操作员正在展开的下拉
        /// 会被整个换掉（与 <c>AxisPanelElement.RefreshRows</c> 同一条理由）。
        /// </summary>
        /// <param name="force">true = 跳过指纹比对（本控件自己写完配方后用）</param>
        private void RefreshRecipeOptions(bool force)
        {
            var commands = RuntimeContext?.RecipeCommands;

            if (commands == null)
            {
                _optionsFingerprint = UninitializedFingerprint;

                if (RecipeOptions.Count > 0)
                    SetOptions(Array.Empty<ScadaRecipeOption>());

                ApplySelection(Guid.Empty, null);
                return;
            }

            IReadOnlyList<ScadaRecipeOption> recipes;
            try
            {
                recipes = commands.Recipes ?? Array.Empty<ScadaRecipeOption>();
            }
            catch (Exception ex)
            {
                // 读列表也可能抛（宿主实现不当）：上屏 + 留痕，不让节拍回调把异常抛到调度循环。
                SetOperationStatus(StatusTextFailurePrefix + ex.Message, ScadaRecipePanelData.StatusKeyDanger);
                WriteAudit("读取配方列表", "失败", ex.Message);
                return;
            }

            string fingerprint = BuildOptionsFingerprint(recipes);
            if (!force && string.Equals(fingerprint, _optionsFingerprint, StringComparison.Ordinal))
                return;

            _optionsFingerprint = fingerprint;

            var merged = MergeOptions(recipes);
            SetOptions(merged);
            ResolveSelection(merged);

            // 空表的提示与列表同生命周期：列表回来了就收掉，空了就写上。
            if (merged.Count > 0)
            {
                if (string.Equals(OperationStatus, StatusTextNoRecipes, StringComparison.Ordinal))
                    SetOperationStatus(string.Empty, ScadaRecipePanelData.StatusKeyMuted);
            }
            else
            {
                SetOperationStatus(StatusTextNoRecipes, ScadaRecipePanelData.StatusKeyMuted);
            }
        }

        private static string BuildOptionsFingerprint(IReadOnlyList<ScadaRecipeOption> recipes)
        {
            if (recipes.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(recipes.Count * 48);

            for (int i = 0; i < recipes.Count; i++)
            {
                var option = recipes[i];

                if (i > 0)
                    builder.Append(';');

                builder.Append(option.RecipeId.ToString("N")).Append('|')
                       .Append(option.Name).Append('|')
                       .Append(option.EntryCount).Append('|')
                       .Append(option.IsActive);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 重建时对"内容没变"的项沿用旧实例。
        ///
        /// 为什么：<see cref="ScadaRecipeOption"/> 是 record（值相等），而依赖属性换值用的是
        /// <c>Equals</c>——若给选中项换了个"内容相同的新实例"，<c>SelectedOption</c> 的写入会被
        /// 判定为"没变化"，下拉里就留着一个已经不在 ItemsSource 里的对象（显示成空选择框）。
        /// 沿用旧实例还顺带保住了下拉的选中态。
        /// </summary>
        private IReadOnlyList<ScadaRecipeOption> MergeOptions(IReadOnlyList<ScadaRecipeOption> fresh)
        {
            if (RecipeOptions.Count == 0)
                return fresh;

            var merged = new List<ScadaRecipeOption>(fresh.Count);

            foreach (var option in fresh)
            {
                var previous = FindOption(option.RecipeId);
                merged.Add(previous != null && previous.Equals(option) ? previous : option);
            }

            return merged;
        }

        private void SetOptions(IReadOnlyList<ScadaRecipeOption> recipes)
        {
            SetValue(RecipeOptionsPropertyKey, recipes);
            SetValue(HasRecipesPropertyKey, recipes.Count > 0);
        }

        /// <summary>
        /// 列表刷新后的选中态：还在就保持，不在了回落到生效配方，再没有就选第一条。
        /// （静默跳到别的配方比显示空更危险——操作员会对着错的配方按「下载」。）
        /// </summary>
        private void ResolveSelection(IReadOnlyList<ScadaRecipeOption> recipes)
        {
            if (recipes.Count == 0)
            {
                ApplySelection(Guid.Empty, null);
                return;
            }

            var current = FindOption(recipes, SelectedRecipeId);

            if (current == null)
            {
                var activeId = Guid.Empty;

                try { activeId = RuntimeContext?.RecipeCommands?.ActiveRecipeId ?? Guid.Empty; }
                catch { /* 读 ActiveRecipeId 抛了就按"没有生效配方"处理，不影响下拉可用 */ }

                current = FindOption(recipes, activeId) ?? recipes[0];
            }

            ApplySelection(current.RecipeId, current);
        }

        private ScadaRecipeOption? FindOption(Guid id) => FindOption(RecipeOptions, id);

        private static ScadaRecipeOption? FindOption(IReadOnlyList<ScadaRecipeOption> options, Guid id)
        {
            if (id == Guid.Empty)
                return null;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].RecipeId == id)
                    return options[i];
            }

            return null;
        }

        /// <summary>同时写 Id 与选项，并刷新"已选：…"那一行（挡住两个回调互相递归）</summary>
        private void ApplySelection(Guid id, ScadaRecipeOption? option)
        {
            _applyingSelection = true;

            try
            {
                if (SelectedRecipeId != id)
                    SetValue(SelectedRecipeIdProperty, id);

                if (!ReferenceEquals(SelectedOption, option))
                    SetValue(SelectedOptionProperty, option);
            }
            finally
            {
                _applyingSelection = false;
            }

            UpdateSelectionText(option);
            RecomputeCommandState();
        }

        private void UpdateSelectionText(ScadaRecipeOption? option)
        {
            string text = option == null
                ? string.Empty
                : $"已选：{option.Name}（{option.EntryCount} 项）";

            if (!string.Equals(SelectionText, text, StringComparison.Ordinal))
                SetValue(SelectionTextPropertyKey, text);

            var visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (SelectionVisibility != visibility)
                SetValue(SelectionVisibilityPropertyKey, visibility);
        }

        private static void OnSelectedRecipeIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (RecipePanelElement)d;
            if (panel._applyingSelection)
                return;

            var id = e.NewValue is Guid value ? value : Guid.Empty;
            panel.ApplySelection(id, panel.FindOption(id));
        }

        private static void OnSelectedOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (RecipePanelElement)d;
            if (panel._applyingSelection)
                return;

            if (e.NewValue is ScadaRecipeOption option)
            {
                panel.ApplySelection(option.RecipeId, option);
                return;
            }

            // 下拉被 ItemsSource 重建清空时 Selector 会推一个 null 过来。那**不是**"操作员取消了选择"：
            // 按当前 Id 重新解析一份，解析得到就顶回去；真的找不到了才清空。
            var current = panel.FindOption(panel.SelectedRecipeId);
            panel.ApplySelection(current?.RecipeId ?? Guid.Empty, current);
        }

        // ==================================================================
        //  可用性（每拍重算：权限/登录状态会在运行中变）
        // ==================================================================

        private void RecomputeCommandState()
        {
            var context = RuntimeContext;
            var commands = context?.RecipeCommands;

            SetValue(ToolbarVisibilityPropertyKey,
                commands != null ? Visibility.Visible : Visibility.Collapsed);

            // 权限口径与 AckButtonElement 逐字一致（Element 为 null 时 CanElement 一律放行）。
            bool permitted = commands != null && context!.AccessPolicy.CanElement(Element);
            bool allowed = permitted && !IsBusy;
            bool hasSelection = SelectedRecipeId != Guid.Empty;

            SetValue(CanOperatePropertyKey, allowed);
            SetValue(CanSelectPropertyKey, allowed && HasRecipes);
            SetValue(CanDownloadPropertyKey, allowed && hasSelection);
            SetValue(CanUploadPropertyKey, allowed && hasSelection);
            SetValue(CanSavePropertyKey, allowed);

            // 有权限时置 null（不弹空提示框），无权限时给出原因。
            SetValue(PermissionToolTipPropertyKey,
                commands != null && !permitted ? StatusTextNoPermission : null);

            DownloadCommand.RaiseCanExecuteChanged();
            UploadCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
        }
    }
}
