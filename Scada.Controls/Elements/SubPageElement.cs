using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 画中画 / 嵌入页图元（TypeKey = <c>Hmi.SubPage</c>，P1a）：在一个矩形里只读显示另一张画面。
    ///
    /// P1a 的边界
    /// ---------
    /// · 固定目标页；不做变量切页（P1b）
    /// · 只读；子页里的按钮/输入不接收事件（P1b 再接事件宿主）
    /// · 数据绑定是活的：内部用一份自己的 <see cref="ScadaRuntimeBinder"/>，
    ///   不依赖主画布的数据泵（主画布只枚举自己的 <c>_containerIndex</c>）
    ///
    /// 递归防护
    /// ---------
    /// 内嵌画布把 <see cref="ScadaCanvas.AllowSubPages"/> 设为 false；
    /// 目标页里再出现 SubPage 时只显示“嵌套子页仅支持一层”，不会 A↔B 无限展开。
    /// </summary>
    public partial class SubPageElement : ScadaElementBase
    {
        public const string FitUniform = "Uniform";
        public const string FitUniformToFill = "UniformToFill";
        public const string FitFill = "Fill";
        public const string FitOriginal = "Original";

        public const string MissingPlaceholder = "Placeholder";
        public const string MissingKeepLast = "KeepLast";

        public const string SourceFixed = "Fixed";
        public const string SourceFollowVariable = "FollowVariable";

        [ScadaProperty(Key = "TargetPageId", DisplayName = "目标画面", Kind = ElementPropertyKind.Page,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "要嵌入的普通画面；弹窗页不能嵌入。Fixed 模式下按这个 Id 寻址")]
        public partial string? TargetPageId { get; set; }

        [ScadaProperty(Key = "TargetPageName", DisplayName = "目标画面名", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false, AffectsRender = false,
                       Description = "展示用名称；Id 是权威键，画面改名后这里可能过期，但寻址仍按 Id")]
        public partial string? TargetPageName { get; set; }

        [ScadaProperty(Key = "PageSource", DisplayName = "页面来源", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "Fixed",
                       Choices = new[] { "Fixed", "FollowVariable" }, AffectsRender = false,
                       Description = "Fixed = 固定目标页；FollowVariable = 读 PageName（绑定的字符串变量）按画面名切页")]
        public partial string? PageSource { get; set; }

        [ScadaProperty(Key = "PageName", DisplayName = "画面名变量", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "FollowVariable 模式读这里：把字符串变量绑到本属性，值 = 目标画面名；找不到就显示缺页占位")]
        public partial string? PageName { get; set; }

        [ScadaProperty(Key = "Interactive", DisplayName = "可交互", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.General, DefaultValue = "False", AffectsRender = false,
                       Description = "false = 只读显示（子页按钮不接收事件）；true = 子页按钮可操作，且子页图元事件纳入当前可见对象")]
        public partial bool Interactive { get; set; }

        [ScadaProperty(Key = "Fit", DisplayName = "缩放方式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "Uniform",
                       Choices = new[] { "Uniform", "UniformToFill", "Fill", "Original" }, AffectsRender = false,
                       Description = "嵌入页在矩形里的缩放规则；Original 不缩放，超出部分裁掉")]
        public partial string? Fit { get; set; }

        [ScadaProperty(Key = "ShowBackground", DisplayName = "显示背景", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "True", AffectsRender = false,
                       Description = "是否画目标页自己的背景色/背景图；关掉就透出本图元的 Fill")]
        public partial bool ShowBackground { get; set; }

        [ScadaProperty(Key = "MissingBehavior", DisplayName = "缺页时", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "Placeholder",
                       Choices = new[] { "Placeholder", "KeepLast" }, AffectsRender = false,
                       Description = "目标页被删/未选时：Placeholder = 显示占位；KeepLast = 保留上一次有效画面")]
        public partial string? MissingBehavior { get; set; }

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(SubPageElement), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PlaceholderTitlePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PlaceholderTitle), typeof(string), typeof(SubPageElement), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty PlaceholderTitleProperty = PlaceholderTitlePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey PlaceholderTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PlaceholderText), typeof(string), typeof(SubPageElement), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty PlaceholderTextProperty = PlaceholderTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ShowPlaceholderPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ShowPlaceholder), typeof(bool), typeof(SubPageElement), new PropertyMetadata(true));
        public static readonly DependencyProperty ShowPlaceholderProperty = ShowPlaceholderPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ShowCanvasPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ShowCanvas), typeof(bool), typeof(SubPageElement), new PropertyMetadata(false));
        public static readonly DependencyProperty ShowCanvasProperty = ShowCanvasPropertyKey.DependencyProperty;

        public static readonly DependencyProperty NestedTooDeepProperty = DependencyProperty.Register(
            nameof(NestedTooDeep), typeof(bool), typeof(SubPageElement),
            new PropertyMetadata(false, OnNestedTooDeepChanged));

        private static void OnNestedTooDeepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SubPageElement)d).UpdateStatus();

        static SubPageElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SubPageElement), new FrameworkPropertyMetadata(typeof(SubPageElement)));
        }

        public SubPageElement()
        {
            Loaded += (_, _) => UpdateStatus();
            Unloaded += (_, _) => DetachEmbedded();
            UpdateStatus();
        }

        public string? StatusText => (string?)GetValue(StatusTextProperty);
        public string? PlaceholderTitle => (string?)GetValue(PlaceholderTitleProperty);
        public string? PlaceholderText => (string?)GetValue(PlaceholderTextProperty);
        public bool ShowPlaceholder => (bool)GetValue(ShowPlaceholderProperty);
        public bool ShowCanvas => (bool)GetValue(ShowCanvasProperty);

        /// <summary>由父画布在“嵌套子页只允许一层”时置 true；本控件只显示占位，不再展开</summary>
        public bool NestedTooDeep
        {
            get => (bool)GetValue(NestedTooDeepProperty);
            set => SetValue(NestedTooDeepProperty, value);
        }

        private ScadaCanvas? _nestedCanvas;
        private Viewbox? _viewbox;
        private Border? _canvasHost;
        private ScadaRuntimeBinder? _binder;
        private ScadaPage? _currentPage;
        private ScadaPage? _registeredSubPage;

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (_nestedCanvas != null)
                _nestedCanvas.RuntimeContext = ToReadOnlyContext(newContext);

            UpdateStatus();
        }

        protected override void OnPageResolverChanged() => UpdateStatus();

        protected override void OnPageNameResolverChanged() => UpdateStatus();

        protected override void OnImageAssetResolverChanged() => UpdateStatus();

        protected override void OnElementRefreshed() => UpdateStatus();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _viewbox = GetTemplateChild("PART_Viewbox") as Viewbox;
            _canvasHost = GetTemplateChild("PART_CanvasHost") as Border;

            if (_canvasHost != null && _nestedCanvas == null)
            {
                _nestedCanvas = new ScadaCanvas
                {
                    IsReadOnly = true,
                    AllowSubPages = false,
                    IsHitTestVisible = false,
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };

                _canvasHost.Child = _nestedCanvas;
            }

            UpdateStatus();
        }

        partial void OnTargetPageIdPropertyChanged() => UpdateStatus();
        partial void OnTargetPageNamePropertyChanged() => UpdateStatus();
        partial void OnPageSourcePropertyChanged()
        {
            NormalizePageSource();
            UpdateStatus();
        }
        partial void OnPageNamePropertyChanged() => UpdateStatus();
        partial void OnInteractivePropertyChanged() => UpdateStatus();
        partial void OnFitPropertyChanged()
        {
            NormalizeFit();
            UpdateStatus();
        }
        partial void OnShowBackgroundPropertyChanged() => UpdateStatus();
        partial void OnMissingBehaviorPropertyChanged()
        {
            NormalizeMissingBehavior();
            UpdateStatus();
        }

        /// <summary>供断言/宿主在布局后强制建表；正常路径由 UpdateStatus 的 Dispatcher 排一次</summary>
        public void StartEmbeddedBindingNow()
        {
            StopBinder();

            if (_nestedCanvas == null || _currentPage == null || NestedTooDeep)
                return;

            if (RuntimeContext?.Values is not { } values)
                return;

            _binder = new ScadaRuntimeBinder(
                _nestedCanvas,
                values,
                _currentPage,
                (level, message) => System.Diagnostics.Debug.WriteLine($"[SubPage] {level}: {message}"));

            _binder.Start();
        }

        public int EmbeddedBoundCount => _binder?.BoundCount ?? 0;

        /// <summary>内嵌画布（断言/宿主检查只读状态用；正常配置不要直接改它的属性）</summary>
        public ScadaCanvas? EmbeddedCanvas => _nestedCanvas;

        private void UpdateStatus()
        {
            if (NestedTooDeep)
            {
                SetPlaceholder("嵌套子页仅支持一层", "目标页里又出现了 SubPage；P1a 不做多层嵌套。");
                StopBinder();
                return;
            }

            var page = ResolveTargetPage();
            _currentPage = page;

            if (page == null)
            {
                if (IsKeepLast && _nestedCanvas?.Page != null)
                {
                    // KeepLast = 冻结上一次有效画面：停止 binder，避免已删除页面的变量继续刷进来。
                    StopBinder();
                    SetStatus("保留上一次画面");
                    return;
                }

                bool follow = IsFollowVariable;
                SetPlaceholder(
                    follow
                        ? (string.IsNullOrWhiteSpace(PageName) ? "未选择画面变量" : "未找到画面")
                        : (string.IsNullOrWhiteSpace(TargetPageId) ? "未选择嵌入画面" : "未找到画面"),
                    follow
                        ? (string.IsNullOrWhiteSpace(PageName)
                            ? "FollowVariable 模式需要把字符串变量绑到「画面名变量」。"
                            : $"画面名「{PageName}」没有对应页；按缺页占位。")
                        : (string.IsNullOrWhiteSpace(TargetPageId)
                            ? "在属性面板选择目标画面，或把页面来源切到 FollowVariable。"
                            : "目标页已删除，或 Id 不属于当前方案。"));
                StopBinder();
                return;
            }

            if (page.IsPopup)
            {
                SetPlaceholder("弹窗页不能嵌入", "目标画面标了 IsPopup；画中画只接受普通页。");
                StopBinder();
                return;
            }

            if (RuntimeContext is { } context && !context.AccessPolicy.CanPage(page))
            {
                var key = page.RequiredPermissionOf();
                SetPlaceholder("无权查看此画面", $"目标页要求「{ScadaPermissionKeys.DisplayName(key)}」权限；不渲染半页内容。");
                StopBinder();
                return;
            }

            ShowCanvasNow(page);
            SetStatus(page.Name + (Interactive ? " · 可交互" : " · 只读"));
        }

        private void ShowCanvasNow(ScadaPage page)
        {
            if (_nestedCanvas == null || _viewbox == null)
                return;

            StopBinder();

            if (_registeredSubPage != null)
            {
                RuntimeContext?.SubPages?.Exit(_registeredSubPage);
                _registeredSubPage = null;
            }

            _nestedCanvas.Page = page;
            _nestedCanvas.ItemsSource = page.Elements;
            _nestedCanvas.PageWidth = page.Width;
            _nestedCanvas.PageHeight = page.Height;
            _nestedCanvas.Width = Math.Max(1, page.Width);
            _nestedCanvas.Height = Math.Max(1, page.Height);
            _nestedCanvas.PageBackground = ShowBackground ? ParseBrush(page.Background) : Brushes.Transparent;
            _nestedCanvas.PageBackgroundImage = ShowBackground ? ResolveImageAsset(page.BackgroundImage) : null;
            _nestedCanvas.PageBackgroundStretch = ParseStretch(page.BackgroundStretch);
            _nestedCanvas.PageBackgroundOpacity = Math.Clamp(page.BackgroundOpacity / 100d, 0, 1);
            _nestedCanvas.PageBackgroundTile = string.Equals(page.BackgroundStretch, "Tile", StringComparison.OrdinalIgnoreCase);
            _nestedCanvas.StateListResolver = StateListResolver;
            _nestedCanvas.ImageAssetResolver = ImageAssetResolver;
            _nestedCanvas.ImageBytesResolver = ImageBytesResolver;
            _nestedCanvas.PageResolver = PageResolver;
            _nestedCanvas.PageNameResolver = PageNameResolver;
            _nestedCanvas.IsReadOnly = !Interactive;
            _nestedCanvas.IsHitTestVisible = Interactive;
            _nestedCanvas.Focusable = Interactive;
            _nestedCanvas.RuntimeContext = Interactive ? RuntimeContext : ToReadOnlyContext(RuntimeContext);
            _viewbox.Stretch = ParseViewboxStretch(Fit);

            if (Interactive)
            {
                RuntimeContext?.SubPages?.Enter(page);
                _registeredSubPage = page;
            }

            // 嵌套画布不在本控件的模板树里预先实例化：先把它的模板套上，
            // 否则第一次 StartEmbeddedBindingNow 枚举到的是空集合。
            _nestedCanvas.ApplyTemplate();
            _nestedCanvas.UpdateLayout();

            SetValue(ShowCanvasPropertyKey, true);
            SetValue(ShowPlaceholderPropertyKey, false);

            // 嵌套画布的控件要等模板套用/布局后才有；Loaded 之前直接 Start 会建出空表。
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartEmbeddedBindingNow));
        }

        private void SetPlaceholder(string title, string text)
        {
            SetValue(PlaceholderTitlePropertyKey, title);
            SetValue(PlaceholderTextPropertyKey, text);
            SetValue(ShowPlaceholderPropertyKey, true);
            SetValue(ShowCanvasPropertyKey, false);
            SetValue(StatusTextPropertyKey, title);
        }

        private void SetStatus(string text)
        {
            SetValue(StatusTextPropertyKey, text);
            SetValue(ShowPlaceholderPropertyKey, false);
            SetValue(ShowCanvasPropertyKey, true);
        }

        private void StopBinder()
        {
            _binder?.Stop();
            _binder = null;
        }

        /// <summary>卸载：停掉内嵌 binder，并让嵌套画布退掉目标页的 Layers/Elements 订阅</summary>
        private void DetachEmbedded()
        {
            StopBinder();

            if (_registeredSubPage != null)
            {
                RuntimeContext?.SubPages?.Exit(_registeredSubPage);
                _registeredSubPage = null;
            }

            if (_nestedCanvas == null)
                return;

            _nestedCanvas.ItemsSource = null;
            _nestedCanvas.Page = null;
            _nestedCanvas.RuntimeContext = null;
        }

        /// <summary>
        /// 嵌套画布只读上下文：保留报警/节拍/权限/值源/历史/文本列表，去掉写口、
        /// 轴命令与确认出口。只给 IsReadOnly=true 不够——图元自己的点击仍会写变量。
        /// </summary>
        private static ScadaRuntimeContext? ToReadOnlyContext(ScadaRuntimeContext? source)
        {
            if (source == null)
                return null;

            return new ScadaRuntimeContext(
                source.Alarms,
                source.Beat,
                writer: null,
                accessPolicy: source.AccessPolicy,
                axisSource: source.AxisSource,
                axisCommands: null,
                confirmations: null,
                values: source.Values,
                priorAlarms: source.PriorAlarms,
                history: source.History,
                stateLists: source.StateLists);
        }

        private bool IsKeepLast
            => string.Equals(MissingBehavior, MissingKeepLast, StringComparison.OrdinalIgnoreCase);

        private bool IsFollowVariable
            => string.Equals(PageSource, SourceFollowVariable, StringComparison.OrdinalIgnoreCase);

        private ScadaPage? ResolveTargetPage()
            => IsFollowVariable
                ? ResolvePageByName(PageName)
                : ResolvePage(TargetPageId);

        private void NormalizePageSource()
        {
            string canonical = string.Equals(PageSource, SourceFollowVariable, StringComparison.OrdinalIgnoreCase)
                ? SourceFollowVariable
                : SourceFixed;

            if (!string.Equals(PageSource, canonical, StringComparison.Ordinal))
                PageSource = canonical;
        }

        private void NormalizeFit()
        {
            string canonical = string.Equals(Fit, FitUniformToFill, StringComparison.OrdinalIgnoreCase)
                ? FitUniformToFill
                : string.Equals(Fit, FitFill, StringComparison.OrdinalIgnoreCase)
                    ? FitFill
                    : string.Equals(Fit, FitOriginal, StringComparison.OrdinalIgnoreCase)
                        ? FitOriginal
                        : FitUniform;

            if (!string.Equals(Fit, canonical, StringComparison.Ordinal))
                Fit = canonical;
        }

        private void NormalizeMissingBehavior()
        {
            string canonical = string.Equals(MissingBehavior, MissingKeepLast, StringComparison.OrdinalIgnoreCase)
                ? MissingKeepLast
                : MissingPlaceholder;

            if (!string.Equals(MissingBehavior, canonical, StringComparison.Ordinal))
                MissingBehavior = canonical;
        }

        private static Stretch ParseViewboxStretch(string? fit)
            => string.Equals(fit, FitFill, StringComparison.OrdinalIgnoreCase)
                ? Stretch.Fill
                : string.Equals(fit, FitUniformToFill, StringComparison.OrdinalIgnoreCase)
                    ? Stretch.UniformToFill
                    : string.Equals(fit, FitOriginal, StringComparison.OrdinalIgnoreCase)
                        ? Stretch.None
                        : Stretch.Uniform;

        private static Stretch ParseStretch(string? stretch)
            => string.Equals(stretch, "UniformToFill", StringComparison.OrdinalIgnoreCase)
                ? Stretch.UniformToFill
                : string.Equals(stretch, "Fill", StringComparison.OrdinalIgnoreCase)
                    ? Stretch.Fill
                    : string.Equals(stretch, "None", StringComparison.OrdinalIgnoreCase)
                        ? Stretch.None
                        : Stretch.Uniform;

        private static Brush? ParseBrush(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                if (new BrushConverter().ConvertFromString(text) is Brush brush)
                {
                    if (brush.CanFreeze)
                        brush.Freeze();
                    return brush;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
