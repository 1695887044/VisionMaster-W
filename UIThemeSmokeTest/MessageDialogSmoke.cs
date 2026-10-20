using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UI.CustomControl;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 公共消息弹窗冒烟 / 渲染（--message-dialog / --message-dialog-render）。
    ///
    /// 设计基线：docs/UI设计基线/message-dialog/design.html（v1）。
    /// 断言分三层：
    ///   ① 静态契约：令牌、分册归属、合并顺序、零硬编码颜色、R68 图标按钮格子；
    ///   ② 运行期几何：480 卡片 / 36 图标 / 16-600 标题 / 32 按钮 / 30 关闭 / 详情 2 行；
    ///   ③ 行为：危险默认焦点在次按钮、Enter 走 RequestDefault、Y/N/Esc 映射。
    /// 渲染证据：docs/UI设计基线/message-dialog/evidence/after-render-v1-{recovery,danger}.png。
    /// </summary>
    internal static class MessageDialogSmoke
    {
        private const string RepoRoot = @"d:\C#\VM";

        public static int RunSmoke()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            try
            {
                RunStaticChecks(Check);
                RunGlyphChecks(Check);
                RunApiChecks(Check);

                var recoveryView = new MessageDialogView { Options = CreateRecoveryOptions() };
                var recoveryHost = CreateHost(recoveryView, 640, 520);
                try
                {
                    CheckRecovery(Check, recoveryView);
                }
                finally
                {
                    recoveryHost.Child = null;
                }

                var dangerView = new MessageDialogView { Options = CreateDangerOptions() };
                var dangerHost = CreateHost(dangerView, 640, 520);
                try
                {
                    CheckDanger(Check, dangerView);
                }
                finally
                {
                    dangerHost.Child = null;
                }

                var minimalView = new MessageDialogView
                {
                    Options = new MessageDialogOptions
                    {
                        Severity = MessageSeverity.Success,
                        Title = "方案已保存",
                        Message = "方案已写入磁盘。",
                        PrimaryText = "好",
                        ShowClose = false,
                    },
                };
                var minimalHost = CreateHost(minimalView, 640, 420);
                try
                {
                    CheckMinimal(Check, minimalView);
                }
                finally
                {
                    minimalHost.Child = null;
                }

                CheckDangerChip(Check);
                CheckKeyboardMapping(Check);
                CheckDefaultFocusInWindow(Check);
                CheckEasyDialogIntegration(Check);
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 消息弹窗冒烟抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 消息弹窗冒烟：全部通过 ==="
                : $"### 消息弹窗冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        public static int RunRender()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            try
            {
                var outDir = Path.Combine(RepoRoot, "docs", "UI设计基线", "message-dialog", "evidence");
                Directory.CreateDirectory(outDir);

                RenderScene(CreateRecoveryOptions(), Path.Combine(outDir, "after-render-v1-recovery.png"), Check);
                RenderScene(CreateDangerOptions(), Path.Combine(outDir, "after-render-v1-danger.png"), Check);
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 消息弹窗渲染抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 消息弹窗渲染：完成 ==="
                : $"### 消息弹窗渲染：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        // ==================================================================
        //  数据夹具
        // ==================================================================

        private static MessageDialogOptions CreateRecoveryOptions() => new()
        {
            Severity = MessageSeverity.Warning,
            Title = "上次运行的现场",
            Message = "检测到 1 份未保存的方案草稿，已归档到下面的位置。",
            Details =
            {
                new MessageDetail("方案草稿", "1 份", @"Autosave\recovered", MessageChipTone.Accent),
                new MessageDetail("崩溃现场", "1 个", @"crash_reports\20261020-091233", MessageChipTone.Neutral),
            },
            Hint = "可用「打开方案」载入草稿继续编辑。",
            Question = "现在打开草稿所在目录吗？",
            PrimaryText = "打开目录",
            SecondaryText = "稍后再说",
        };

        private static MessageDialogOptions CreateDangerOptions() => new()
        {
            Severity = MessageSeverity.Danger,
            Title = "删除并行分组「分拣」",
            Message = "该分组包含 3 条分支、共 12 个算子。删除后可以用 Ctrl+Z 撤销。",
            Question = "确定要删除这个分组吗？",
            PrimaryText = "删除分组",
            SecondaryText = "取消",
            PrimaryTone = MessageTone.Danger,
        };

        // ==================================================================
        //  运行期断言
        // ==================================================================

        private static void CheckRecovery(Action<string, bool, string> check, MessageDialogView view)
        {
            var card = view.FindName("Card") as Border;
            var icon = view.FindName("IconBlock") as Border;
            var glyph = view.FindName("IconGlyph") as TextBlock;
            var title = view.FindName("TitleText") as TextBlock;
            var detailsPanel = view.FindName("DetailsPanel") as Border;
            var detailsList = view.FindName("DetailsList") as ItemsControl;
            var primary = view.FindName("PrimaryButton") as Button;
            var secondary = view.FindName("SecondaryButton") as Button;
            var close = view.FindName("CloseButton") as Button;
            var footHint = view.FindName("FootHint") as TextBlock;

            check("recovery 卡片宽 480 / 圆角 12 / 1px 卡片描边",
                card != null
                && Math.Abs(card.ActualWidth - 480) < 0.5
                && card.CornerRadius == new CornerRadius(12)
                && card.BorderThickness == new Thickness(1)
                && SameBrush(card.BorderBrush, Res("DialogCardBorderBrush")),
                card == null ? "card=null" : $"w={card.ActualWidth:0.#} r={card.CornerRadius} border={card.BorderThickness}");

            check("recovery 图标块 36×36 / 圆角 10 / Warning 语义样式",
                icon != null
                && Math.Abs(icon.ActualWidth - 36) < 0.5
                && Math.Abs(icon.ActualHeight - 36) < 0.5
                && icon.CornerRadius == new CornerRadius(10)
                && ReferenceEquals(icon.Style, Res("DialogMessageIconWarning")),
                icon == null ? "icon=null" : $"w={icon.ActualWidth:0.#} h={icon.ActualHeight:0.#} r={icon.CornerRadius}");

            check("recovery 标题 16 / SemiBold",
                title != null && Math.Abs(title.FontSize - 16) < 0.01 && title.FontWeight == FontWeights.SemiBold,
                title == null ? "title=null" : $"size={title.FontSize} weight={title.FontWeight}");

            check("recovery 详情面板可见 / 2 行 / 行模板已实例化",
                detailsPanel != null && detailsPanel.Visibility == Visibility.Visible
                && detailsList != null && detailsList.Items.Count == 2
                && Descendants<Border>(detailsList).Count(b => b.Name == "RowRoot") == 2,
                detailsList == null
                    ? "details=null"
                    : $"items={detailsList.Items.Count} rows={Descendants<Border>(detailsList).Count(b => b.Name == "RowRoot")}");

            check("recovery 主按钮高 32 / 背景 = DialogAccentBrush",
                primary != null
                && Math.Abs(primary.ActualHeight - 32) < 0.5
                && SameBrush(primary.Background, Res("DialogAccentBrush")),
                primary == null ? "primary=null" : $"h={primary.ActualHeight:0.#} bg={primary.Background}");

            check("recovery 次按钮可见 / 高 32 / 白底 + DialogBorderBrush",
                secondary != null
                && secondary.Visibility == Visibility.Visible
                && Math.Abs(secondary.ActualHeight - 32) < 0.5
                && SameBrush(secondary.Background, Res("DialogSurfaceBrush"))
                && SameBrush(secondary.BorderBrush, Res("DialogBorderBrush")),
                secondary == null ? "secondary=null" : $"vis={secondary.Visibility} h={secondary.ActualHeight:0.#}");

            check("recovery 关闭按钮 30×30 / Padding=0",
                close != null
                && Math.Abs(close.ActualWidth - 30) < 0.5
                && Math.Abs(close.ActualHeight - 30) < 0.5
                && close.Padding == new Thickness(0),
                close == null ? "close=null" : $"w={close.ActualWidth:0.#} h={close.ActualHeight:0.#} pad={close.Padding}");

            check("recovery 焦点环 / ToolTip 走 Dialog 令牌样式",
                primary != null && ReferenceEquals(primary.FocusVisualStyle, Res("DialogFocusVisual"))
                && secondary != null && ReferenceEquals(secondary.FocusVisualStyle, Res("DialogFocusVisual"))
                && close != null && ReferenceEquals(close.FocusVisualStyle, Res("DialogFocusVisual"))
                && close.ToolTip is ToolTip closeTip && ReferenceEquals(closeTip.Style, Res("DialogToolTipStyle")),
                "FocusVisualStyle / ToolTipStyle");

            check("recovery 图标字形 = FA6 warning（U+F071）",
                glyph != null && glyph.Text == "\uF071"
                && SameFontFamily(glyph.FontFamily, Res("DialogIconFont"))
                && SameBrush(glyph.Foreground, Res("DialogWarningTextBrush")),
                glyph == null ? "glyph=null" : $"text=U+{(int)(glyph.Text.Length > 0 ? glyph.Text[0] : '?'):X4}");

            check("recovery 默认焦点 = 主按钮（Enter 打开目录）",
                view.DefaultFocusResult == MessageResult.Primary
                && primary != null && primary.IsDefault
                && secondary != null && !secondary.IsDefault,
                $"focus={view.DefaultFocusResult} primaryDefault={primary?.IsDefault}");

            check("recovery 底栏提示 = Enter 打开目录 · Esc 稍后再说",
                footHint != null && footHint.Text == "Enter 打开目录 · Esc 稍后再说",
                footHint?.Text ?? "footHint=null");
        }

        private static void CheckDanger(Action<string, bool, string> check, MessageDialogView view)
        {
            var icon = view.FindName("IconBlock") as Border;
            var glyph = view.FindName("IconGlyph") as TextBlock;
            var primary = view.FindName("PrimaryButton") as Button;
            var secondary = view.FindName("SecondaryButton") as Button;
            var footHint = view.FindName("FootHint") as TextBlock;

            check("danger 图标块 = Danger 语义样式 / 字形 U+F057",
                icon != null && ReferenceEquals(icon.Style, Res("DialogMessageIconDanger"))
                && glyph != null && glyph.Text == "\uF057",
                icon == null ? "icon=null" : $"style={icon.Style == Res("DialogMessageIconDanger")}");

            check("danger 主按钮背景 = DialogDangerBrush",
                primary != null && SameBrush(primary.Background, Res("DialogDangerBrush")),
                primary == null ? "primary=null" : $"bg={primary.Background}");

            check("danger 主按钮焦点环 = DialogMessageDangerFocusVisual（红）",
                primary != null && ReferenceEquals(primary.FocusVisualStyle, Res("DialogMessageDangerFocusVisual")),
                primary == null ? "primary=null" : $"focus={primary.FocusVisualStyle}");

            check("danger 默认焦点 = 次按钮 / 次按钮 IsDefault / 主按钮不是 IsDefault",
                view.DefaultFocusResult == MessageResult.Secondary
                && secondary != null && secondary.IsDefault
                && primary != null && !primary.IsDefault,
                $"focus={view.DefaultFocusResult} secDefault={secondary?.IsDefault} priDefault={primary?.IsDefault}");

            check("danger 底栏提示说明 Enter = 取消",
                footHint != null && footHint.Text.Contains("Enter 取消") && footHint.Text.Contains("删除分组"),
                footHint?.Text ?? "footHint=null");
        }

        private static void CheckMinimal(Action<string, bool, string> check, MessageDialogView view)
        {
            var detailsPanel = view.FindName("DetailsPanel") as Border;
            var primary = view.FindName("PrimaryButton") as Button;
            var secondary = view.FindName("SecondaryButton") as Button;
            var close = view.FindName("CloseButton") as Button;
            var footHint = view.FindName("FootHint") as TextBlock;

            check("单按钮：次按钮隐藏 / 详情隐藏 / 关闭隐藏 / 提示 Enter 关闭",
                secondary != null && secondary.Visibility == Visibility.Collapsed
                && detailsPanel != null && detailsPanel.Visibility == Visibility.Collapsed
                && close != null && close.Visibility == Visibility.Collapsed
                && footHint != null && footHint.Text == "Enter 关闭",
                $"sec={secondary?.Visibility} details={detailsPanel?.Visibility} close={close?.Visibility} hint={footHint?.Text}");

            check("单按钮：默认焦点仍在主按钮",
                primary != null && primary.IsDefault && view.DefaultFocusResult == MessageResult.Primary,
                $"default={primary?.IsDefault} focus={view.DefaultFocusResult}");
        }

        private static void CheckDangerChip(Action<string, bool, string> check)
        {
            var options = new MessageDialogOptions
            {
                Severity = MessageSeverity.Warning,
                Title = "chip 语气",
                Details =
                {
                    new MessageDetail("相机", "离线", "CAM-01 · GigE", MessageChipTone.Danger),
                },
            };
            var view = new MessageDialogView { Options = options };
            var host = CreateHost(view, 640, 360);
            try
            {
                var list = view.FindName("DetailsList") as ItemsControl;
                var chipText = Descendants<TextBlock>(list).FirstOrDefault(t => t.Text == "离线");
                check("危险 chip 文字色 = DialogDangerTextBrush（不是 #EF4444）",
                    chipText != null && SameBrush(chipText.Foreground, Res("DialogDangerTextBrush")),
                    chipText == null ? "chip=null" : $"fg={chipText.Foreground}");

                var copy = Descendants<Button>(list).FirstOrDefault();
                check("复制按钮 24×24 / Padding=0 / 无障碍名",
                    copy != null
                    && Math.Abs(copy.ActualWidth - 24) < 0.5
                    && Math.Abs(copy.ActualHeight - 24) < 0.5
                    && copy.Padding == new Thickness(0)
                    && AutomationProperties.GetName(copy) == "复制路径",
                    copy == null ? "copy=null" : $"w={copy.ActualWidth:0.#} h={copy.ActualHeight:0.#} name={AutomationProperties.GetName(copy)}");
            }
            finally
            {
                host.Child = null;
            }
        }

        private static void CheckKeyboardMapping(Action<string, bool, string> check)
        {
            var view = new MessageDialogView { Options = CreateDangerOptions() };
            var results = new List<MessageResult>();
            view.ResultRequested += (_, result) => results.Add(result);

            view.RequestPrimary();
            view.RequestSecondary();
            view.RequestDefault();

            check("Y/N/Enter 映射：RequestPrimary → Primary / RequestSecondary → Secondary / RequestDefault → 危险时 Secondary",
                results.Count == 3
                && results[0] == MessageResult.Primary
                && results[1] == MessageResult.Secondary
                && results[2] == MessageResult.Secondary,
                string.Join(" | ", results));
        }

        private static void CheckDefaultFocusInWindow(Action<string, bool, string> check)
        {
            var view = new MessageDialogView { Options = CreateDangerOptions() };
            var window = new Window
            {
                Left = -4000,
                Top = -4000,
                Width = 640,
                Height = 520,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = view,
            };

            try
            {
                window.Show();
                Pump(window);

                var secondary = view.FindName("SecondaryButton") as Button;
                var primary = view.FindName("PrimaryButton") as Button;
                check("危险场景打开后键盘焦点落在次按钮（Enter 不会删除）",
                    secondary != null && secondary.IsKeyboardFocused && primary != null && !primary.IsKeyboardFocused,
                    $"secFocused={secondary?.IsKeyboardFocused} priFocused={primary?.IsKeyboardFocused}");

                check("FocusDefault() 幂等：再次调用仍落在次按钮",
                    view.FocusDefault() && secondary != null && secondary.IsKeyboardFocused,
                    $"focused={secondary?.IsKeyboardFocused}");
            }
            finally
            {
                window.Close();
            }
        }

        private static void CheckEasyDialogIntegration(Action<string, bool, string> check)
        {
            // 1) 新 API + 主按钮
            var options = CreateRecoveryOptions();
            var primary = RunEngineScenario(
                window => (window.Content as MessageDialogView)?.RequestPrimary(),
                () => EasyDialog.ShowMessageSync(options),
                out bool sawPrimaryView);
            check("EasyDialog 集成：ShowMessageSync + 主按钮 → Primary，宿主内容是新卡片",
                primary == MessageResult.Primary && sawPrimaryView,
                $"result={primary} view={sawPrimaryView}");

            // 2) 旧签名 + 次按钮：bool 映射与「旧调用点也走新卡片」
            var legacy = RunEngineScenario(
                window => (window.Content as MessageDialogView)?.RequestSecondary(),
                () => EasyDialog.ShowSync("旧签名", "正文"),
                out bool sawLegacyView);
            check("EasyDialog 集成：旧 ShowSync(title,message) + 次按钮 → false，且已换成新卡片",
                !legacy && sawLegacyView,
                $"result={legacy} view={sawLegacyView}");

            // 3) Esc → Secondary
            var escOptions = CreateRecoveryOptions();
            var esc = RunEngineScenario(
                window => RaiseKey(window, Key.Escape),
                () => EasyDialog.ShowMessageSync(escOptions),
                out _);
            check("EasyDialog 集成：Esc → Secondary", esc == MessageResult.Secondary, esc.ToString());

            // 4) Y → Primary
            var yOptions = CreateRecoveryOptions();
            var y = RunEngineScenario(
                window => RaiseKey(window, Key.Y),
                () => EasyDialog.ShowMessageSync(yOptions),
                out _);
            check("EasyDialog 集成：Y → Primary", y == MessageResult.Primary, y.ToString());

            // 5) N → Secondary
            var nOptions = CreateRecoveryOptions();
            var n = RunEngineScenario(
                window => RaiseKey(window, Key.N),
                () => EasyDialog.ShowMessageSync(nOptions),
                out _);
            check("EasyDialog 集成：N → Secondary", n == MessageResult.Secondary, n.ToString());

            // 6) Enter → 默认按钮（普通场景 = Primary）
            // 焦点先移出按钮：真实按键时焦点在按钮上会由 ButtonBase 自己 Click（Tab 到危险主按钮
            // 再 Enter 也能触发删除）；窗口级兜底只在焦点不在按钮上时生效，合成事件测的是这条兜底。
            var enterOptions = CreateRecoveryOptions();
            var enter = RunEngineScenario(
                window =>
                {
                    // 焦点移到 UserControl 本身（非 Button），模拟"焦点不在任何按钮上"的窗口级兜底；
                    // 真实按键时焦点在按钮上会由 ButtonBase 自己 Click。
                    if (window.Content is MessageDialogView view)
                    {
                        view.Focusable = true;
                        Keyboard.Focus(view);
                    }
                    RaiseKey(window, Key.Enter);
                },
                () => EasyDialog.ShowMessageSync(enterOptions),
                out _);
            check("EasyDialog 集成：Enter → 默认按钮（普通场景 Primary）", enter == MessageResult.Primary, enter.ToString());
        }

        /// <summary>
        /// 跑一次真实 EasyDialog：200ms 后对遮罩窗口里的 MessageDialogView 做一次动作，
        /// 再用 5s 兜底定时器收口（窗口没建出来时反射 SetResult(false)，避免 PushFrame 永久挂起）。
        /// </summary>
        private static T RunEngineScenario<T>(Action<Window> request, Func<T> show, out bool sawView)
        {
            bool localSawView = false;
            sawView = false;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var window = FindMessageWindow();
                if (window == null) return;
                localSawView = window.Content is MessageDialogView;
                request(window);
            };
            timer.Start();

            var guard = StartEngineGuard();
            try
            {
                var result = show();
                sawView = localSawView;
                return result;
            }
            finally
            {
                guard.Stop();
                timer.Stop();
            }
        }

        private static Window FindMessageWindow()
            => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Content is MessageDialogView);

        private static DispatcherTimer StartEngineGuard()
        {
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                FindMessageWindow()?.Close();
                // 兜底：窗口没建出来时直接收口当前 TCS（SetResult 是 internal，只能反射）
                typeof(EasyDialog)
                    .GetMethod("SetResult", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.Invoke(null, new object[] { false });
            };
            guard.Start();
            return guard;
        }

        private static void RaiseKey(Window window, Key key)
        {
            var source = PresentationSource.FromVisual(window);
            if (source == null)
            {
                window.Close();
                return;
            }

            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            window.RaiseEvent(args);
        }

        // ==================================================================
        //  静态检查
        // ==================================================================

        private static void RunStaticChecks(Action<string, bool, string> check)
        {
            var colorsPath = Path.Combine(RepoRoot, @"UI\Controls\Themes\Colors.xaml");
            var messagePath = Path.Combine(RepoRoot, @"UI\Controls\Themes\Dialog\Dialog.Message.xaml");
            var chipPath = Path.Combine(RepoRoot, @"UI\Controls\Themes\Dialog\Dialog.Chip.xaml");
            var aliasesPath = Path.Combine(RepoRoot, @"UI\Controls\Themes\Fluent\FluentAliases.xaml");
            var genericPath = Path.Combine(RepoRoot, @"UI\Controls\Themes\Generic.xaml");
            var viewXamlPath = Path.Combine(RepoRoot, @"UI\Controls\CustomControl\Message\MessageDialogView.xaml");
            var viewCodePath = Path.Combine(RepoRoot, @"UI\Controls\CustomControl\Message\MessageDialogView.xaml.cs");

            var colors = File.Exists(colorsPath) ? File.ReadAllText(colorsPath) : string.Empty;
            var messageXaml = File.Exists(messagePath) ? File.ReadAllText(messagePath) : string.Empty;
            var chip = File.Exists(chipPath) ? File.ReadAllText(chipPath) : string.Empty;
            var aliases = File.Exists(aliasesPath) ? File.ReadAllText(aliasesPath) : string.Empty;
            var generic = File.Exists(genericPath) ? File.ReadAllText(genericPath) : string.Empty;
            var viewXaml = File.Exists(viewXamlPath) ? File.ReadAllText(viewXamlPath) : string.Empty;
            var viewCode = File.Exists(viewCodePath) ? File.ReadAllText(viewCodePath) : string.Empty;

            check("Colors.xaml 新增 DialogTextTertiaryBrush = #64748B",
                Regex.IsMatch(colors, @"x:Key=""DialogTextTertiaryBrush""[^>]*Color=""#64748B"""),
                "");

            check("Dialog.Message.xaml 可读", messageXaml.Length > 0, messagePath);
            if (messageXaml.Length > 0)
            {
                string[] required =
                {
                    "DialogMessageCard",
                    "DialogMessageIcon",
                    "DialogMessageIconInfo",
                    "DialogMessageIconSuccess",
                    "DialogMessageIconWarning",
                    "DialogMessageIconDanger",
                    "DialogMessageTitle",
                    "DialogMessageLead",
                    "DialogMessageHint",
                    "DialogMessageQuestion",
                    "DialogMessageIconGlyph",
                    "DialogMessageDangerFocusVisual",
                    "DialogMessageDetails",
                    "DialogMessageDetailRow",
                    "DialogMessageDetailLabel",
                    "DialogMessagePath",
                    "DialogMessagePathText",
                    "DialogMessageFootHint",
                    "DialogMessageFooter",
                    "DialogMessageChip",
                    "DialogMessageChipAccent",
                    "DialogMessageChipNeutral",
                    "DialogMessageChipSuccess",
                    "DialogMessageChipDanger",
                    "DialogMessageChipText",
                    "DialogMessageChipTextAccent",
                    "DialogMessageChipTextNeutral",
                    "DialogMessageChipTextSuccess",
                    "DialogMessageChipTextDanger",
                };
                var missing = required.Where(k => !messageXaml.Contains($"x:Key=\"{k}\"", StringComparison.Ordinal)).ToList();
                check("Dialog.Message.xaml 29 个键齐全", missing.Count == 0, string.Join(" | ", missing));

                var sources = Regex.Matches(messageXaml, @"Source=""([^""]+)""")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
                check("Dialog.Message.xaml 只就地 merge Colors.xaml（R30）",
                    sources.Count == 1 && sources[0].EndsWith("Themes/Colors.xaml", StringComparison.Ordinal),
                    string.Join(" | ", sources));

                var stripped = StripComments(messageXaml);
                check("Dialog.Message.xaml 无硬编码 #RRGGBB（R140）",
                    !Regex.IsMatch(stripped, @"#[0-9A-Fa-f]{6}"),
                    "");

                check("Dialog.Message.xaml 不跨册引用 chip / 按钮 / 焦点环 / ToolTip / Fluent",
                    !Regex.IsMatch(stripped, @"StaticResource\s+(DialogChip|DialogIconButton|DialogFocusVisual|DialogToolTipStyle|Fluent)"),
                    "");
            }

            check("Message chip 族在 Dialog.Message.xaml（唯一键，避开 FluentAliases 对 DialogChip* 的覆盖）",
                messageXaml.Contains("x:Key=\"DialogMessageChipAccent\"", StringComparison.Ordinal)
                && messageXaml.Contains("x:Key=\"DialogMessageChipDanger\"", StringComparison.Ordinal)
                && messageXaml.Contains("x:Key=\"DialogMessageChipTextDanger\"", StringComparison.Ordinal)
                && messageXaml.Contains("DialogDangerTextBrush", StringComparison.Ordinal)
                && !chip.Contains("x:Key=\"DialogMessageChipTextDanger\"", StringComparison.Ordinal),
                "Dialog.Chip.xaml 不再保留同名键");

            check("FluentAliases.xaml 四个 Message 按钮样式齐全",
                new[] { "DialogMessagePrimaryButton", "DialogMessageSecondaryButton", "DialogMessageDangerButton", "DialogMessageCopyButton" }
                    .All(k => aliases.Contains($"x:Key=\"{k}\"", StringComparison.Ordinal)),
                "");

            check("Message 按钮 Padding 用 WPF 的 14,0（不是 CSS 的 0,14）",
                Regex.IsMatch(aliases, @"x:Key=""DialogMessagePrimaryButton""(?s:.*?)Padding"" Value=""14,0""")
                && Regex.IsMatch(aliases, @"x:Key=""DialogMessageSecondaryButton""(?s:.*?)Padding"" Value=""14,0""")
                && Regex.IsMatch(aliases, @"x:Key=""DialogMessageDangerButton""(?s:.*?)Padding"" Value=""14,0"""),
                "");

            check("复制按钮 24×24 + Padding=0（R68）",
                Regex.IsMatch(aliases, @"x:Key=""DialogMessageCopyButton""(?s:.*?)Width"" Value=""24""")
                && Regex.IsMatch(aliases, @"x:Key=""DialogMessageCopyButton""(?s:.*?)MinWidth"" Value=""24""")
                && Regex.IsMatch(aliases, @"x:Key=""DialogMessageCopyButton""(?s:.*?)Padding"" Value=""0"""),
                "");

            int messageIndex = generic.IndexOf("Dialog/Dialog.Message.xaml", StringComparison.Ordinal);
            int aliasesIndex = generic.IndexOf("Fluent/FluentAliases.xaml", StringComparison.Ordinal);
            check("Generic.xaml 在 FluentAliases 之前合并 Dialog.Message.xaml（R30 合并顺序）",
                messageIndex > 0 && aliasesIndex > messageIndex,
                $"message={messageIndex} aliases={aliasesIndex}");

            check("MessageDialogView.xaml 可读", viewXaml.Length > 0, viewXamlPath);
            if (viewXaml.Length > 0)
            {
                var stripped = StripComments(viewXaml);
                check("MessageDialogView.xaml 无硬编码 #RRGGBB（R140）",
                    !Regex.IsMatch(stripped, @"#[0-9A-Fa-f]{6}"),
                    "");

                check("视图使用 Dialog 焦点环 / ToolTip / 关闭按钮 / 复制按钮 / Tab 循环",
                    viewXaml.Contains("DialogFocusVisual", StringComparison.Ordinal)
                    && viewXaml.Contains("DialogToolTipStyle", StringComparison.Ordinal)
                    && viewXaml.Contains("DialogMessageCloseButton", StringComparison.Ordinal)
                    && viewXaml.Contains("DialogMessageCopyButton", StringComparison.Ordinal)
                    && viewXaml.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", StringComparison.Ordinal),
                    "");

                check("关闭按钮样式 30×30 + Padding=0（不写 28）",
                    Regex.IsMatch(aliases, @"x:Key=""DialogMessageCloseButton""(?s:.*?)Width"" Value=""30""")
                    && Regex.IsMatch(aliases, @"x:Key=""DialogMessageCloseButton""(?s:.*?)MinWidth"" Value=""30""")
                    && Regex.IsMatch(aliases, @"x:Key=""DialogMessageCloseButton""(?s:.*?)Padding"" Value=""0""")
                    && !Regex.IsMatch(viewXaml, @"x:Name=""CloseButton""(?s:.*?)Width=""28"""),
                    "");

                check("关闭 / 复制按钮挂无障碍名",
                    viewXaml.Contains("AutomationProperties.Name=\"关闭\"", StringComparison.Ordinal)
                    && viewXaml.Contains("AutomationProperties.Name=\"复制路径\"", StringComparison.Ordinal),
                    "");

                check("XAML 字形：关闭 U+F00D / 复制 U+F0C5",
                    viewXaml.Contains("&#xF00D;", StringComparison.Ordinal)
                    && viewXaml.Contains("&#xF0C5;", StringComparison.Ordinal),
                    "");
            }

            check("code-behind 四个语义字形 + 复制成功 check 字形",
                viewCode.Contains(@"\uF05A", StringComparison.Ordinal)
                && viewCode.Contains(@"\uF058", StringComparison.Ordinal)
                && viewCode.Contains(@"\uF071", StringComparison.Ordinal)
                && viewCode.Contains(@"\uF057", StringComparison.Ordinal)
                && viewCode.Contains(@"\uF00C", StringComparison.Ordinal),
                "");
        }

        private static void RunGlyphChecks(Action<string, bool, string> check)
        {
            var fontPath = Path.Combine(RepoRoot, @"UI\Controls\Asserts\FontFamilys\Font Awesome 6 Pro-Solid-900.otf");
            check("FA6 Pro Solid 字体文件存在", File.Exists(fontPath), fontPath);
            if (!File.Exists(fontPath)) return;

            var glyphs = new Dictionary<string, int>
            {
                ["info"] = 0xF05A,
                ["success"] = 0xF058,
                ["warning"] = 0xF071,
                ["danger"] = 0xF057,
                ["close"] = 0xF00D,
                ["copy"] = 0xF0C5,
                ["check"] = 0xF00C,
            };

            var typeface = new GlyphTypeface(new Uri(fontPath));
            var missing = glyphs
                .Where(pair => !typeface.CharacterToGlyphMap.ContainsKey(pair.Value))
                .Select(pair => $"{pair.Key}=U+{pair.Value:X4}")
                .ToList();

            check("七个字形码点在 FA6 Pro Solid 里都有真实字形",
                missing.Count == 0,
                missing.Count == 0
                    ? string.Join(" ", glyphs.Select(pair => $"{pair.Key}=U+{pair.Value:X4}"))
                    : "缺：" + string.Join(" | ", missing));
        }

        private static void RunApiChecks(Action<string, bool, string> check)
        {
            var legacyShow = typeof(EasyDialog).GetMethod("ShowAsync", new[] { typeof(string), typeof(string), typeof(bool) });
            check("EasyDialog.ShowAsync(title,message) 仍返回 Task<bool>（调用点零改动）",
                legacyShow != null && legacyShow.ReturnType == typeof(System.Threading.Tasks.Task<bool>),
                legacyShow == null ? "method=null" : legacyShow.ReturnType.Name);

            var legacySync = typeof(EasyDialog).GetMethod("ShowSync", new[] { typeof(string), typeof(string), typeof(bool) });
            check("EasyDialog.ShowSync(title,message) 仍返回 bool",
                legacySync != null && legacySync.ReturnType == typeof(bool),
                legacySync == null ? "method=null" : legacySync.ReturnType.Name);

            var customShow = typeof(EasyDialog).GetMethod("ShowCustomAsync", new[] { typeof(string), typeof(FrameworkElement), typeof(bool) });
            check("EasyDialog.ShowCustomAsync 仍返回 Task<bool>（属性表格 / 文本输入不受影响）",
                customShow != null && customShow.ReturnType == typeof(System.Threading.Tasks.Task<bool>),
                customShow == null ? "method=null" : customShow.ReturnType.Name);

            var messageShow = typeof(EasyDialog).GetMethod("ShowMessageAsync", new[] { typeof(MessageDialogOptions) });
            check("EasyDialog.ShowMessageAsync 返回 Task<MessageResult>",
                messageShow != null && messageShow.ReturnType == typeof(System.Threading.Tasks.Task<MessageResult>),
                messageShow == null ? "method=null" : messageShow.ReturnType.Name);

            var messageSync = typeof(EasyDialog).GetMethod("ShowMessageSync", new[] { typeof(MessageDialogOptions) });
            check("EasyDialog.ShowMessageSync 返回 MessageResult",
                messageSync != null && messageSync.ReturnType == typeof(MessageResult),
                messageSync == null ? "method=null" : messageSync.ReturnType.Name);

            var publicShow = typeof(MessageDialog).GetMethod("Show", new[] { typeof(MessageDialogOptions) });
            check("MessageDialog.Show 返回 MessageResult",
                publicShow != null && publicShow.ReturnType == typeof(MessageResult),
                publicShow == null ? "method=null" : publicShow.ReturnType.Name);

            var publicShowAsync = typeof(MessageDialog).GetMethod("ShowAsync", new[] { typeof(MessageDialogOptions) });
            check("MessageDialog.ShowAsync 返回 Task<MessageResult>",
                publicShowAsync != null && publicShowAsync.ReturnType == typeof(System.Threading.Tasks.Task<MessageResult>),
                publicShowAsync == null ? "method=null" : publicShowAsync.ReturnType.Name);

            var dangerDefault = new MessageDialogOptions { PrimaryTone = MessageTone.Danger };
            check("危险语气且未显式设 DefaultFocus → 解析为 Secondary",
                dangerDefault.ResolveDefaultFocus() == MessageResult.Secondary,
                dangerDefault.ResolveDefaultFocus().ToString());

            var overridden = new MessageDialogOptions { PrimaryTone = MessageTone.Danger, DefaultFocus = MessageResult.Primary };
            check("调用点显式覆盖 DefaultFocus 时以调用点为准",
                overridden.ResolveDefaultFocus() == MessageResult.Primary,
                overridden.ResolveDefaultFocus().ToString());
        }

        // ==================================================================
        //  渲染 / 视觉树工具
        // ==================================================================

        private static void RenderScene(MessageDialogOptions options, string path, Action<string, bool, string> check)
        {
            var view = new MessageDialogView { Options = options };
            var host = CreateHost(view, 640, 520);
            try
            {
                RenderElement(host, path);
                check("渲染 " + Path.GetFileName(path), File.Exists(path), path);
            }
            finally
            {
                host.Child = null;
            }
        }

        private static Border CreateHost(FrameworkElement child, double width, double height)
        {
            var host = new Border
            {
                Width = width,
                Height = height,
                Background = Res("DialogBackgroundBrush") as Brush,
                Child = child,
            };
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            Pump(host);
            return host;
        }

        private static void Pump(DispatcherObject element)
        {
            element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (element is FrameworkElement fe)
            {
                fe.UpdateLayout();
            }
        }

        private static void RenderElement(FrameworkElement element, string path)
        {
            var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static object Res(string key) => Application.Current?.TryFindResource(key);

        /// <summary>
        /// 画刷比较：同一个 Colors.xaml 可能被多个分册各 merge 一次，
        /// 令牌键解析到的是**不同实例**，所以不能只比引用；颜色相同即视为同一令牌。
        /// </summary>
        private static bool SameBrush(object actual, object expected)
        {
            if (ReferenceEquals(actual, expected)) return true;
            return actual is SolidColorBrush a && expected is SolidColorBrush e && a.Color == e.Color;
        }

        private static bool SameFontFamily(FontFamily actual, object expected)
        {
            if (ReferenceEquals(actual, expected)) return true;
            return actual != null && expected is FontFamily e
                && string.Equals(actual.Source, e.Source, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripComments(string xaml)
            => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
    }
}
