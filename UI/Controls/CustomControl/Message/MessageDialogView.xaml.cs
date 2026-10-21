using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace UI.CustomControl
{
    /// <summary>
    /// 公共消息弹窗的卡片视图（设计基线 message-dialog v1）。
    ///
    /// 只负责视觉与结果上报；宿主窗口 / 遮罩 / 并发锁 / 跨线程由 <see cref="EasyDialog"/> 负责。
    /// 结果通过 <see cref="ResultRequested"/> 上报：Primary / Secondary / Close。
    /// </summary>
    public partial class MessageDialogView : UserControl
    {
        /// <summary>选项。设置后立即按语义、按钮语气与可见性重排卡片。</summary>
        public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
            nameof(Options),
            typeof(MessageDialogOptions),
            typeof(MessageDialogView),
            new PropertyMetadata(null, OnOptionsChanged));

        /// <summary>用户请求的结果（Primary / Secondary）。Close 由宿主窗口的 Closed 兜底。</summary>
        public event EventHandler<MessageResult>? ResultRequested;

        public MessageDialogView()
        {
            InitializeComponent();
        }

        /// <summary>弹窗选项。</summary>
        public MessageDialogOptions? Options
        {
            get => (MessageDialogOptions?)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        /// <summary>
        /// 真正生效的默认焦点（危险语气且未显式覆盖时 = Secondary）。
        /// 冒烟断言与宿主键盘转发都以它为准。
        /// </summary>
        public MessageResult DefaultFocusResult { get; private set; } = MessageResult.Primary;

        private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((MessageDialogView)d).ApplyOptions();

        /// <summary>按 Options 重排卡片：语义图标、主按钮语气、详情/次按钮/关闭的显隐、默认焦点。</summary>
        public void ApplyOptions()
        {
            var options = Options;
            if (options == null)
            {
                // Options 是 public DP，置 null 是合法路径；必须把上一次的语义/宽度/默认键
                // 一起复位，否则会残留红色主按钮、560 宽、旧图标与旧 IsDefault 标志。
                Card.Width = 480;
                TitleText.Text = string.Empty;
                LeadText.Text = string.Empty;
                LeadText.Visibility = Visibility.Collapsed;
                DetailsPanel.Visibility = Visibility.Collapsed;
                DetailsList.ItemsSource = null;
                HintText.Visibility = Visibility.Collapsed;
                QuestionText.Visibility = Visibility.Collapsed;
                SecondaryButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "好";
                PrimaryButton.IsDefault = false;
                SecondaryButton.IsDefault = false;
                SecondaryButton.IsCancel = false;
                ApplySeverity(MessageSeverity.Info);
                ApplyPrimaryTone(MessageTone.Primary);
                FootHint.Text = "Enter 关闭";
                DefaultFocusResult = MessageResult.Primary;
                return;
            }

            Card.Width = options.Width > 0 ? options.Width : 480;
            TitleText.Text = options.Title ?? string.Empty;

            var message = options.Message ?? string.Empty;
            LeadText.Text = message;
            LeadText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

            var hasDetails = options.Details != null && options.Details.Count > 0;
            DetailsPanel.Visibility = hasDetails ? Visibility.Visible : Visibility.Collapsed;
            DetailsList.ItemsSource = hasDetails ? options.Details : null;

            var hint = options.Hint ?? string.Empty;
            HintText.Text = hint;
            HintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;

            var question = options.Question ?? string.Empty;
            QuestionText.Text = question;
            QuestionText.Visibility = string.IsNullOrEmpty(question) ? Visibility.Collapsed : Visibility.Visible;

            CloseButton.Visibility = options.ShowClose ? Visibility.Visible : Visibility.Collapsed;

            var hasSecondary = !string.IsNullOrEmpty(options.SecondaryText);
            SecondaryButton.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;
            SecondaryButton.Content = options.SecondaryText ?? string.Empty;
            PrimaryButton.Content = string.IsNullOrEmpty(options.PrimaryText) ? "好" : options.PrimaryText;

            ApplySeverity(options.Severity);
            ApplyPrimaryTone(options.PrimaryTone);

            DefaultFocusResult = options.ResolveDefaultFocus();
            // 单按钮场景没有次按钮可落焦点，退回主按钮
            if (!hasSecondary && DefaultFocusResult == MessageResult.Secondary)
                DefaultFocusResult = MessageResult.Primary;

            // Enter：默认按钮；危险场景默认按钮是「取消」，主按钮红色但不是 IsDefault
            PrimaryButton.IsDefault = DefaultFocusResult == MessageResult.Primary;
            SecondaryButton.IsDefault = hasSecondary && DefaultFocusResult == MessageResult.Secondary;
            // Esc：只在存在次按钮时交给 IsCancel；单按钮时由宿主收口成 Secondary（不能把主按钮设成 IsCancel）
            SecondaryButton.IsCancel = hasSecondary;

            FootHint.Text = BuildFootHint(options, hasSecondary, DefaultFocusResult);
        }

        /// <summary>按默认焦点把键盘焦点放到对应按钮（危险场景 = 次按钮）。</summary>
        public bool FocusDefault()
        {
            var target = DefaultFocusResult == MessageResult.Secondary
                         && SecondaryButton.Visibility == Visibility.Visible
                ? SecondaryButton
                : PrimaryButton;
            return target.Focus();
        }

        /// <summary>主操作（Enter 默认 / Y / 主按钮）。</summary>
        public void RequestPrimary() => RaiseResult(MessageResult.Primary);

        /// <summary>次操作（Esc / N / × / 次按钮）。</summary>
        public void RequestSecondary() => RaiseResult(MessageResult.Secondary);

        /// <summary>触发默认按钮：普通场景 = 主操作；危险场景 = 取消。</summary>
        public void RequestDefault()
        {
            var result = DefaultFocusResult;
            if (result == MessageResult.Secondary && SecondaryButton.Visibility != Visibility.Visible)
                result = MessageResult.Primary;
            RaiseResult(result);
        }

        private void RaiseResult(MessageResult result) => ResultRequested?.Invoke(this, result);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 焦点必须等窗口真正可交互后再给：Loaded 同帧 Focus() 会被窗口初始化抢回去。
            Dispatcher.BeginInvoke(new Action(() => FocusDefault()), DispatcherPriority.Input);
        }

        private void ApplySeverity(MessageSeverity severity)
        {
            string styleKey;
            string glyph;
            string brushKey;
            switch (severity)
            {
                case MessageSeverity.Success:
                    styleKey = "DialogMessageIconSuccess";
                    glyph = "\uF058";
                    brushKey = "DialogSuccessTextBrush";
                    break;
                case MessageSeverity.Warning:
                    styleKey = "DialogMessageIconWarning";
                    glyph = "\uF071";
                    brushKey = "DialogWarningTextBrush";
                    break;
                case MessageSeverity.Danger:
                    styleKey = "DialogMessageIconDanger";
                    glyph = "\uF057";
                    brushKey = "DialogDangerTextBrush";
                    break;
                default:
                    styleKey = "DialogMessageIconInfo";
                    glyph = "\uF05A";
                    brushKey = "DialogAccentDarkBrush";
                    break;
            }

            if (TryFindResource(styleKey) is Style style)
                IconBlock.Style = style;
            IconGlyph.Text = glyph;
            if (TryFindResource(brushKey) is Brush brush)
                IconGlyph.Foreground = brush;
        }

        private void ApplyPrimaryTone(MessageTone tone)
        {
            var key = tone == MessageTone.Danger ? "DialogMessageDangerButton" : "DialogMessagePrimaryButton";
            if (TryFindResource(key) is Style style)
                PrimaryButton.Style = style;

            // 危险主按钮用红色焦点环（设计基线），普通主按钮沿用共享 DialogFocusVisual。
            var focusKey = tone == MessageTone.Danger ? "DialogMessageDangerFocusVisual" : "DialogFocusVisual";
            if (TryFindResource(focusKey) is Style focus)
                PrimaryButton.FocusVisualStyle = focus;
        }

        private static string BuildFootHint(MessageDialogOptions options, bool hasSecondary, MessageResult defaultFocus)
        {
            var primary = string.IsNullOrEmpty(options.PrimaryText) ? "好" : options.PrimaryText;
            if (!hasSecondary)
                return "Enter 关闭";

            var secondary = options.SecondaryText ?? string.Empty;
            if (options.PrimaryTone == MessageTone.Danger)
                return $"Enter {secondary} · Esc {secondary} · 危险操作需点击或 Tab 到「{primary}」";
            return defaultFocus == MessageResult.Secondary
                ? $"Enter {secondary} · Esc {secondary}"
                : $"Enter {primary} · Esc {secondary}";
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e) => RequestPrimary();

        private void SecondaryButton_Click(object sender, RoutedEventArgs e) => RequestSecondary();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => RequestSecondary();

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not MessageDetail detail)
                return;

            var path = detail.Path ?? string.Empty;
            if (path.Length == 0)
                return;

            try
            {
                Clipboard.SetText(path);
            }
            catch (Exception)
            {
                // 剪贴板被其它进程占用：保留 copy 字形，悬停提示改成「复制失败」
                ShowCopyFailure(button);
                return;
            }

            ShowCopySuccess(button);
        }

        private void ShowCopySuccess(Button button)
        {
            StopCopyTimer(button);
            button.Content = "\uF00C";
            if (TryFindResource("DialogSuccessTextBrush") is Brush success)
                button.Foreground = success;
            AutomationProperties.SetHelpText(button, "已复制");

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1400),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                button.Content = "\uF0C5";
                button.ClearValue(ForegroundProperty);
                button.ToolTip = CreateCopyToolTip();
                button.Tag = null;
                AutomationProperties.SetHelpText(button, "复制路径");
            };
            button.Tag = timer;
            timer.Start();
        }

        private void ShowCopyFailure(Button button)
        {
            StopCopyTimer(button);
            button.ToolTip = CreateToolTip("复制失败，请重试");
            AutomationProperties.SetHelpText(button, "复制失败，请重试");

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1400),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                button.ToolTip = CreateCopyToolTip();
                button.Tag = null;
                AutomationProperties.SetHelpText(button, "复制路径");
            };
            button.Tag = timer;
            timer.Start();
        }

        private static void StopCopyTimer(Button button)
        {
            if (button.Tag is DispatcherTimer running)
            {
                running.Stop();
                button.Tag = null;
            }
        }

        private ToolTip CreateCopyToolTip() => CreateToolTip("复制路径");

        private ToolTip CreateToolTip(string content)
        {
            var tip = new ToolTip { Content = content };
            if (TryFindResource("DialogToolTipStyle") is Style style)
                tip.Style = style;
            return tip;
        }
    }

    /// <summary>
    /// 详情行 chip 的底色样式：按 <see cref="MessageChipTone"/> 取 DialogMessageChip*。
    /// 为什么用组件专用键而不是 DialogChip*：FluentAliases.xaml 最后合并，会把同名
    /// DialogChip* 转发到 FluentChip*Style（成功绿 #0E7A0D 等），与设计基线的 Dialog
    /// 令牌不一致；DialogMessageChip* 是唯一键，颜色只取 Colors.xaml 的 Dialog 令牌。
    /// </summary>
    public sealed class MessageChipStyleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var tone = value is MessageChipTone typed ? typed : MessageChipTone.Neutral;
            var key = tone switch
            {
                MessageChipTone.Accent => "DialogMessageChipAccent",
                MessageChipTone.Success => "DialogMessageChipSuccess",
                MessageChipTone.Danger => "DialogMessageChipDanger",
                _ => "DialogMessageChipNeutral",
            };
            return Application.Current?.TryFindResource(key) as Style;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 详情行 chip 的文字样式：四档都用组件专用的 <c>DialogMessageChipText*</c>，
    /// 危险档取 DialogDangerTextBrush（深红，4.9:1）；同样在视图层解析（R30）。
    /// </summary>
    public sealed class MessageChipTextStyleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var tone = value is MessageChipTone typed ? typed : MessageChipTone.Neutral;
            var key = tone switch
            {
                MessageChipTone.Accent => "DialogMessageChipTextAccent",
                MessageChipTone.Success => "DialogMessageChipTextSuccess",
                MessageChipTone.Danger => "DialogMessageChipTextDanger",
                _ => "DialogMessageChipTextNeutral",
            };
            return Application.Current?.TryFindResource(key) as Style;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
