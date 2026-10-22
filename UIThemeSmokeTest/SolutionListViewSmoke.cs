using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 方案列表视图冒烟 / 渲染（--solution-list / --solution-list-render）。
    ///
    /// 用真实 SolutionListViewModel + 真实视图：命令的 CanExecute、DataGrid 的
    /// SelectedItem 双向、注释框回写模型都只有在真 VM 上才成立；桩 VM 会让
    /// 「未选中时动作禁用」「默认 chip 跟随 StartupSolutionPath」这类断言假绿。
    /// 不调用 OnDialogOpened / ConfirmCommand：前者会读本机 AppConfig，后者会写盘。
    /// </summary>
    internal static class SolutionListViewSmoke
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
                RunConverterChecks(Check);

                var vm = new SolutionListViewModel(
                    new SolutionService(), new WorkspaceContext(), new AppSettingsService());
                var view = new SolutionListView { DataContext = vm };
                var host = CreateHost(view, 960, 640);
                try
                {
                    Populate(vm, "normal");
                    Pump(host);

                    var grid = view.FindName("SolutionGrid") as DataGrid;
                    var empty = view.FindName("EmptyState") as Grid;
                    var chipOn = view.FindName("DefaultChipOn") as Border;
                    var chipOff = view.FindName("DefaultChipOff") as Border;
                    var count = view.FindName("CountBadge") as TextBlock;
                    var startupText = view.FindName("StartupPathText") as TextBlock;
                    var startupEmpty = view.FindName("StartupPathEmpty") as TextBlock;
                    var hintNone = view.FindName("RailHintNone") as TextBlock;
                    var hintSelected = view.FindName("RailHintSelected") as TextBlock;
                    var open = view.FindName("OpenButton") as Button;
                    var setStartup = view.FindName("SetStartupButton") as Button;
                    var addCurrent = view.FindName("AddCurrentButton") as Button;
                    var add = view.FindName("AddButton") as Button;
                    var delete = view.FindName("DeleteButton") as Button;
                    var up = view.FindName("MoveUpButton") as Button;
                    var down = view.FindName("MoveDownButton") as Button;
                    var confirm = view.FindName("ConfirmButton") as Button;
                    var cancel = view.FindName("CancelButton") as Button;

                    Check("视图真实实例化 + DataContext 是真实 VM",
                        view.DataContext is SolutionListViewModel && Descendants<FrameworkElement>(view).Count() > 80,
                        $"elements={Descendants<FrameworkElement>(view).Count()}");

                    Check("DataGrid 五列（序号/名称/注释/路径/默认）",
                        grid != null && grid.Columns.Count == 5,
                        grid == null ? "grid=null" : $"cols={grid.Columns.Count}");

                    Check("列宽 40/1.2*/1.0*/1.6*220/72（名称/注释/路径按比例）",
                        grid != null
                        && grid.Columns[0].Width.Value == 40
                        && grid.Columns[1].Width.IsStar && Math.Abs(grid.Columns[1].Width.Value - 1.2) < 0.001 && grid.Columns[1].MinWidth == 160
                        && grid.Columns[2].Width.IsStar && Math.Abs(grid.Columns[2].Width.Value - 1.0) < 0.001 && grid.Columns[2].MinWidth == 100
                        && grid.Columns[3].Width.IsStar && Math.Abs(grid.Columns[3].Width.Value - 1.6) < 0.001 && grid.Columns[3].MinWidth == 220
                        && grid.Columns[4].Width.Value == 72,
                        grid == null ? "grid=null" : string.Join(" | ", grid.Columns.Select(c => $"{c.Width}/{c.MinWidth}")));

                    Check("960 下名称列 ≥ 180、路径列 ≥ 220（真实长方案名可读）",
                        grid != null && grid.Columns[1].ActualWidth >= 180 && grid.Columns[3].ActualWidth >= 220,
                        grid == null ? "grid=null" : $"name={grid.Columns[1].ActualWidth:0.#} path={grid.Columns[3].ActualWidth:0.#}");

                    Check("行高 52 / 表头仅列头 / 排序拖拽缩放全关 / 横向滚动 Disabled",
                        grid != null
                        && grid.RowHeight == 52
                        && grid.HeadersVisibility == DataGridHeadersVisibility.Column
                        && !grid.CanUserSortColumns
                        && !grid.CanUserReorderColumns
                        && !grid.CanUserResizeColumns
                        && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Disabled,
                        grid == null ? "grid=null" : $"row={grid.RowHeight} h={grid.HeadersVisibility} hbar={ScrollViewer.GetHorizontalScrollBarVisibility(grid)}");

                    Check("5 行数据进入 DataGrid", grid != null && grid.Items.Count == 5,
                        grid == null ? "grid=null" : $"items={grid.Items.Count}");

                    var row0 = RowOf(grid, vm.Solutions[0]);
                    var row1 = RowOf(grid, vm.Solutions[1]);
                    Check("行高实测约 52", row0 != null && Math.Abs(row0.ActualHeight - 52) <= 2,
                        row0 == null ? "row0=null" : $"h={row0.ActualHeight:0.#}");

                    bool chip0 = row0 != null && Descendants<TextBlock>(row0).Any(t => t.Text == "默认" && EffectivelyVisible(t));
                    bool chip1 = row1 != null && Descendants<TextBlock>(row1).Any(t => t.Text == "默认" && EffectivelyVisible(t));
                    Check("「默认」chip 只在 Path == StartupSolutionPath 的行显示",
                        chip0 && !chip1,
                        $"row0={chip0} row1={chip1}");

                    Check("卡片头默认摘要 + 计数徽标",
                        EffectivelyVisible(chipOn)
                        && !EffectivelyVisible(chipOff)
                        && Descendants<TextBlock>(chipOn).Any(t => t.Text.Contains("默认：主机型-双工位"))
                        && count?.Text == "5",
                        $"on={chipOn?.Visibility} off={chipOff?.Visibility} count={count?.Text}");

                    Check("默认 chip 用 soft 底 + 1px 边框 + 6px 圆角（设计稿，不是通用胶囊）",
                        chipOn != null
                        && ReferenceEquals(chipOn.Background, Application.Current.TryFindResource("DialogAccentSoftBrush"))
                        && ReferenceEquals(chipOn.BorderBrush, Application.Current.TryFindResource("DialogAccentBorderBrush"))
                        && chipOn.BorderThickness == new Thickness(1)
                        && chipOn.CornerRadius == new CornerRadius(6),
                        chipOn == null ? "chip=null" : $"bg={chipOn.Background} border={chipOn.BorderBrush}/{chipOn.BorderThickness} radius={chipOn.CornerRadius}");

                    var rowChip = row0 == null ? null : Descendants<Border>(row0)
                        .FirstOrDefault(b => Descendants<TextBlock>(b).Any(t => t.Text == "默认"));
                    Check("行内默认 chip 用 accent-light + 6px 圆角",
                        rowChip != null
                        && ReferenceEquals(rowChip.Background, Application.Current.TryFindResource("DialogAccentLightBrush"))
                        && rowChip.CornerRadius == new CornerRadius(6),
                        rowChip == null ? "rowChip=null" : $"bg={rowChip.Background} radius={rowChip.CornerRadius}");

                    Check("底栏默认启动路径显示完整值",
                        EffectivelyVisible(startupText)
                        && !EffectivelyVisible(startupEmpty)
                        && startupText?.Text == vm.StartupSolutionPath,
                        $"text={startupText?.Text} empty={startupEmpty?.Visibility}");

                    Check("9 个命令全部绑定到真实 VM",
                        ReferenceEquals(open?.Command, vm.OpenCommand)
                        && ReferenceEquals(setStartup?.Command, vm.SetStartupCommand)
                        && ReferenceEquals(addCurrent?.Command, vm.AddCurrentCommand)
                        && ReferenceEquals(add?.Command, vm.AddCommand)
                        && ReferenceEquals(delete?.Command, vm.DeleteCommand)
                        && ReferenceEquals(up?.Command, vm.MoveUpCommand)
                        && ReferenceEquals(down?.Command, vm.MoveDownCommand)
                        && ReferenceEquals(confirm?.Command, vm.ConfirmCommand)
                        && ReferenceEquals(cancel?.Command, vm.CancelCommand),
                        "command reference equality");

                    Check("选中行：打开/设默认/移除/上下移可用",
                        open != null && open.IsEnabled
                        && setStartup != null && setStartup.IsEnabled
                        && delete != null && delete.IsEnabled
                        && up != null && up.IsEnabled
                        && down != null && down.IsEnabled,
                        $"open={open?.IsEnabled} set={setStartup?.IsEnabled} del={delete?.IsEnabled} up={up?.IsEnabled} down={down?.IsEnabled}");

                    // 未选中：动作禁用 + 提示切换
                    vm.SelectedSolution = null;
                    Pump(host);
                    Check("未选中：动作禁用 + 「选中一行后可…」提示",
                        open != null && !open.IsEnabled
                        && setStartup != null && !setStartup.IsEnabled
                        && delete != null && !delete.IsEnabled
                        && up != null && !up.IsEnabled
                        && down != null && !down.IsEnabled
                        && EffectivelyVisible(hintNone)
                        && !EffectivelyVisible(hintSelected),
                        $"open={open?.IsEnabled} hintNone={hintNone?.Visibility} hintSel={hintSelected?.Visibility}");

                    // 排序边界：第一行上移禁用、最后一行下移禁用
                    vm.SelectedSolution = vm.Solutions[0];
                    Pump(host);
                    Check("排序边界：第一行上移禁用", up != null && !up.IsEnabled && down != null && down.IsEnabled,
                        $"up={up?.IsEnabled} down={down?.IsEnabled}");
                    vm.SelectedSolution = vm.Solutions[4];
                    Pump(host);
                    Check("排序边界：最后一行下移禁用", down != null && !down.IsEnabled && up != null && up.IsEnabled,
                        $"up={up?.IsEnabled} down={down?.IsEnabled}");

                    // 注释框：真实输入 → 工作副本
                    vm.SelectedSolution = vm.Solutions[0];
                    Pump(host);
                    var box = row0 == null ? null : Descendants<TextBox>(row0).FirstOrDefault(t => AutomationProperties.GetName(t) == "注释");
                    if (box != null) box.Text = "改过的注释";
                    Pump(host);
                    Check("注释编辑回写 Solutions[0].Comment",
                        box != null && vm.Solutions[0].Comment == "改过的注释",
                        box == null ? "box=null" : $"model={vm.Solutions[0].Comment}");

                    // 焦点与 ToolTip
                    var focusStyle = Application.Current.TryFindResource("DialogFocusVisual");
                    Check("焦点环走 DialogFocusVisual（行 / 注释框 / 按钮）",
                        row0 != null && ReferenceEquals(row0.FocusVisualStyle, focusStyle)
                        && box != null && ReferenceEquals(box.FocusVisualStyle, focusStyle)
                        && open != null && ReferenceEquals(open.FocusVisualStyle, focusStyle),
                        $"row={row0?.FocusVisualStyle != null} box={box?.FocusVisualStyle != null} open={open?.FocusVisualStyle != null}");

                    var toolTipOwners = Descendants<FrameworkElement>(view)
                        .Where(e => e.ToolTip is ToolTip)
                        .ToList();
                    var styledTips = toolTipOwners.Count(e =>
                        ReferenceEquals(((ToolTip)e.ToolTip).Style, Application.Current.TryFindResource("DialogToolTipStyle")));
                    Check("ToolTip 全部显式使用 DialogToolTipStyle",
                        toolTipOwners.Count > 0 && styledTips == toolTipOwners.Count,
                        $"{styledTips}/{toolTipOwners.Count}");

                    // 空清单
                    Populate(vm, "empty");
                    Pump(host);
                    Check("空清单：空态覆盖层可见、默认 chip 切到「未设置」、路径切「未设置」",
                        EffectivelyVisible(empty)
                        && count?.Text == "0"
                        && !EffectivelyVisible(chipOn)
                        && EffectivelyVisible(chipOff)
                        && !EffectivelyVisible(startupText)
                        && EffectivelyVisible(startupEmpty)
                        && open != null && !open.IsEnabled,
                        $"empty={empty?.Visibility} count={count?.Text} on={chipOn?.Visibility} off={chipOff?.Visibility} path={startupText?.Visibility}/{startupEmpty?.Visibility}");

                    Check("空态默认 chip 用 neutral 底 + 1px 边框 + 6px 圆角",
                        chipOff != null
                        && ReferenceEquals(chipOff.Background, Application.Current.TryFindResource("DialogRowHoverBrush"))
                        && ReferenceEquals(chipOff.BorderBrush, Application.Current.TryFindResource("DialogBorderBrush"))
                        && chipOff.BorderThickness == new Thickness(1)
                        && chipOff.CornerRadius == new CornerRadius(6),
                        chipOff == null ? "chipOff=null" : $"bg={chipOff.Background} border={chipOff.BorderBrush}/{chipOff.BorderThickness} radius={chipOff.CornerRadius}");

                    // 有数据但未选中：默认 chip 仍在，提示为「选中一行后可…」
                    Populate(vm, "noselect");
                    Pump(host);
                    Check("有数据未选中：默认 chip 仍在、动作禁用、提示正确",
                        EffectivelyVisible(chipOn)
                        && EffectivelyVisible(hintNone)
                        && !EffectivelyVisible(hintSelected)
                        && open != null && !open.IsEnabled,
                        $"chipOn={chipOn?.Visibility} hintNone={hintNone?.Visibility} open={open?.IsEnabled}");

                    // 900 窄档：路径列实际宽度 ≥ 220，横向滚动仍 Disabled
                    host.Width = 900;
                    host.Height = 560;
                    host.Measure(new Size(900, 560));
                    host.Arrange(new Rect(0, 0, 900, 560));
                    Populate(vm, "normal");
                    Pump(host);
                    Check("900 窄档：路径列实际宽度 ≥ 220、横向滚动 Disabled",
                        grid != null
                        && grid.Columns[3].ActualWidth >= 220
                        && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Disabled,
                        grid == null ? "grid=null" : $"path={grid.Columns[3].ActualWidth:0.#} hbar={ScrollViewer.GetHorizontalScrollBarVisibility(grid)}");

                    // 真实宿主窗口：Prism DialogWindow 的尺寸契约（960×640 / 900×560）。
                    // 用户截图里窗口被拉到整屏、名称列被挤成一条，根因就是这里没有默认尺寸。
                    var windowVm = new SolutionListViewModel(
                        new SolutionService(), new WorkspaceContext(), new AppSettingsService());
                    var windowView = new SolutionListView { DataContext = windowVm };
                    Populate(windowVm, "normal");
                    var dialogWindow = new Window
                    {
                        Left = -4000,
                        Top = -4000,
                        Width = 100,
                        Height = 100,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Content = windowView,
                    };
                    dialogWindow.Show();
                    Pump(dialogWindow);
                    Check("宿主窗口默认 960×640、最小 900×560",
                        Math.Abs(dialogWindow.Width - 960) < 1
                        && Math.Abs(dialogWindow.Height - 640) < 1
                        && Math.Abs(dialogWindow.MinWidth - 900) < 1
                        && Math.Abs(dialogWindow.MinHeight - 560) < 1
                        && dialogWindow.SizeToContent == SizeToContent.Manual,
                        $"size={dialogWindow.Width}x{dialogWindow.Height} min={dialogWindow.MinWidth}x{dialogWindow.MinHeight}");
                    var windowShot = Path.Combine(EvidencePaths.Dir(RepoRoot, "solution-list"), "after-render-v1-window.png");
                    RenderElement(windowView, windowShot);
                    Check("宿主窗口内容渲染 960×640", File.Exists(windowShot), windowShot);
                    dialogWindow.Close();
                }
                finally
                {
                    host.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 方案列表视图冒烟抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 方案列表视图冒烟：全部通过 ==="
                : $"### 方案列表视图冒烟：{failures} 项失败");
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
                var outDir = EvidencePaths.Dir(RepoRoot, "solution-list");
                Directory.CreateDirectory(outDir);

                var vm = new SolutionListViewModel(
                    new SolutionService(), new WorkspaceContext(), new AppSettingsService());
                var view = new SolutionListView { DataContext = vm };
                var host = CreateHost(view, 960, 640);
                try
                {
                    foreach (var mode in new[] { "normal", "empty", "noselect", "long-path" })
                    {
                        Populate(vm, mode);
                        Pump(host);
                        var path = Path.Combine(outDir, "after-render-v1-" + mode + ".png");
                        RenderElement(host, path);
                        Check("渲染 " + mode, File.Exists(path), path);
                    }

                    // 900 窄档：必须显式重新 Measure/Arrange，否则 Border 沿用 960 的布局
                    host.Width = 900;
                    host.Height = 560;
                    host.Measure(new Size(900, 560));
                    host.Arrange(new Rect(0, 0, 900, 560));
                    Populate(vm, "normal");
                    Pump(host);
                    var narrowPath = Path.Combine(outDir, "after-render-v1-narrow.png");
                    RenderElement(host, narrowPath);
                    Check("渲染 narrow(900×560)", File.Exists(narrowPath), narrowPath);
                }
                finally
                {
                    host.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 方案列表视图渲染抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 方案列表视图渲染：完成 ==="
                : $"### 方案列表视图渲染：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        // ==================================================================
        //  静态检查
        // ==================================================================

        private static void RunStaticChecks(Action<string, bool, string> check)
        {
            var xamlPath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\SolutionListView.xaml");
            var codePath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\SolutionListView.xaml.cs");
            var xamlRaw = File.Exists(xamlPath) ? File.ReadAllText(xamlPath) : string.Empty;
            var code = File.Exists(codePath) ? File.ReadAllText(codePath) : string.Empty;
            check("视图 XAML 可读", xamlRaw.Length > 0, xamlPath);
            check("code-behind 可读", code.Length > 0, codePath);
            if (xamlRaw.Length == 0 || code.Length == 0) return;

            var xaml = Regex.Replace(xamlRaw, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            check("XAML 无硬编码十六进制颜色", !Regex.IsMatch(xaml, @"#[0-9A-Fa-f]{3,8}"), "");
            check("XAML 无本地 keyed Style（CollectedDialogViews 纪律）", !Regex.IsMatch(xaml, @"<Style\b[^>]*x:Key="), "");

            string[] required =
            {
                "ItemsSource=\"{Binding Solutions}\"",
                "SelectedItem=\"{Binding SelectedSolution, Mode=TwoWay}\"",
                "Command=\"{Binding OpenCommand}\"",
                "Command=\"{Binding SetStartupCommand}\"",
                "Command=\"{Binding AddCurrentCommand}\"",
                "Command=\"{Binding AddCommand}\"",
                "Command=\"{Binding DeleteCommand}\"",
                "Command=\"{Binding MoveUpCommand}\"",
                "Command=\"{Binding MoveDownCommand}\"",
                "Command=\"{Binding ConfirmCommand}\"",
                "Command=\"{Binding CancelCommand}\"",
                "Text=\"{Binding Comment, UpdateSourceTrigger=PropertyChanged}\"",
                "x:Name=\"SolutionGrid\"",
                "x:Name=\"EmptyState\"",
                "x:Name=\"DefaultChipOn\"",
                "x:Name=\"DefaultChipOff\"",
                "x:Name=\"StartupPathText\"",
                "x:Name=\"StartupPathEmpty\"",
                "x:Name=\"RailHintNone\"",
                "x:Name=\"RailHintSelected\"",
                "x:Name=\"OpenButton\"",
                "x:Name=\"SetStartupButton\"",
                "x:Name=\"AddCurrentButton\"",
                "x:Name=\"AddButton\"",
                "x:Name=\"DeleteButton\"",
                "x:Name=\"MoveUpButton\"",
                "x:Name=\"MoveDownButton\"",
                "x:Name=\"ConfirmButton\"",
                "x:Name=\"CancelButton\"",
                "x:Name=\"EmptyAddCurrentButton\"",
                "x:Name=\"EmptyAddButton\"",
                "RowHeight=\"52\"",
                "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"",
                "Width=\"1.2*\"",
                "Width=\"1.0*\"",
                "Width=\"1.6*\"",
                "MinWidth=\"220\"",
                "Loaded=\"OnLoaded\"",
                "DialogCellInputQuiet",
                "DialogPrimaryButton",
                "DialogDangerButton",
                "DialogEmptyStateTitle",
                "DialogFocusVisual",
                "DialogToolTipStyle",
                "PathEqualsToVisibility",
                "StartupSummary",
            };
            var missing = required.Where(r => !xaml.Contains(r, StringComparison.Ordinal)).ToList();
            check("关键绑定/结构全部在 XAML 里", missing.Count == 0, string.Join(" | ", missing));

            check("code-behind 提供 3 个 UI 转换器 + Enter 处理器 + 宿主窗口尺寸",
                code.Contains("class PathEqualsToVisibilityConverter")
                && code.Contains("class StartupSummaryConverter")
                && code.Contains("class NullToVisibilityConverter")
                && code.Contains("CommentBox_PreviewKeyDown")
                && code.Contains("OnLoaded")
                && code.Contains("Window.GetWindow"),
                "");
        }

        private static void RunConverterChecks(Action<string, bool, string> check)
        {
            var pathEq = new PathEqualsToVisibilityConverter();
            object P(object a, object b) => pathEq.Convert(new[] { a, b }, typeof(Visibility), null, CultureInfo.InvariantCulture);
            check("PathEqualsToVisibilityConverter：同路径（忽略大小写）/不同/空",
                (Visibility)P(@"C:\a.vms", @"c:\A.VMS") == Visibility.Visible
                && (Visibility)P(@"C:\a.vms", @"C:\b.vms") == Visibility.Collapsed
                && (Visibility)P("", "") == Visibility.Collapsed
                && (Visibility)P(DependencyProperty.UnsetValue, @"C:\a.vms") == Visibility.Collapsed,
                "");

            var list = new ObservableCollection<AppSolutionEntry>
            {
                new AppSolutionEntry { Name = "主机型-双工位", Path = @"C:\line1.vms" },
                new AppSolutionEntry { Name = "副机型", Path = @"C:\line2.vms" },
            };
            var summary = new StartupSummaryConverter();
            object S(object solutions, object startup) => summary.Convert(new[] { solutions, startup }, typeof(string), null, CultureInfo.InvariantCulture);
            check("StartupSummaryConverter：命中 Name / 未命中退回文件名 / 空路径",
                (string)S(list, @"c:\LINE1.VMS") == "默认：主机型-双工位"
                && (string)S(list, @"C:\unknown.vms") == "默认：unknown"
                && (string)S(list, "") == "未设置默认启动",
                $"{S(list, @"C:\line1.vms")} | {S(list, @"C:\unknown.vms")} | {S(list, "")}");

            var nullVis = new NullToVisibilityConverter();
            object N(object value, string mode) => nullVis.Convert(value, typeof(Visibility), mode, CultureInfo.InvariantCulture);
            check("NullToVisibilityConverter：null/空串/非空 + Invert",
                (Visibility)N(null, null) == Visibility.Collapsed
                && (Visibility)N("", null) == Visibility.Collapsed
                && (Visibility)N(new object(), null) == Visibility.Visible
                && (Visibility)N(null, "Invert") == Visibility.Visible
                && (Visibility)N(new object(), "Invert") == Visibility.Collapsed,
                "");
        }

        // ==================================================================
        //  数据夹具
        // ==================================================================

        private static void Populate(SolutionListViewModel vm, string mode)
        {
            var rows = new List<AppSolutionEntry>
            {
                new AppSolutionEntry { Index = 1, Name = "主机型-双工位", Comment = "量产主力方案", Path = @"D:\Projects\现场方案\主机型-双工位-2026Q4.vms" },
                new AppSolutionEntry { Index = 2, Name = "副机型-单工位", Comment = "", Path = @"D:\Projects\现场方案\副机型-单工位-2026Q4.vms" },
                new AppSolutionEntry { Index = 3, Name = "调试用-离线仿真", Comment = "无相机时使用", Path = @"D:\Projects\现场方案\调试用-离线仿真.vms" },
                new AppSolutionEntry { Index = 4, Name = "旧版兼容-2025", Comment = "客户 A 历史项目", Path = @"D:\Projects\历史项目\客户A\旧版兼容-2025.vms" },
                new AppSolutionEntry { Index = 5, Name = "Demo", Comment = "演示用", Path = @"D:\Projects\Demo\Demo.vms" },
            };

            switch (mode)
            {
                case "empty":
                    vm.Solutions = new ObservableCollection<AppSolutionEntry>();
                    vm.SelectedSolution = null;
                    vm.StartupSolutionPath = "";
                    break;
                case "noselect":
                    vm.Solutions = new ObservableCollection<AppSolutionEntry>(rows);
                    vm.SelectedSolution = null;
                    vm.StartupSolutionPath = rows[0].Path;
                    break;
                case "long-path":
                    rows.Add(new AppSolutionEntry
                    {
                        Index = 6,
                        Name = "超长路径方案",
                        Comment = "用于验证省略与 ToolTip",
                        Path = @"D:\Projects\现场方案\超长目录\二级目录\三级目录\超长路径方案-2026Q4-最终版.vms",
                    });
                    vm.Solutions = new ObservableCollection<AppSolutionEntry>(rows);
                    vm.SelectedSolution = rows[5];
                    vm.StartupSolutionPath = rows[0].Path;
                    break;
                default:
                    vm.Solutions = new ObservableCollection<AppSolutionEntry>(rows);
                    vm.SelectedSolution = rows[1];
                    vm.StartupSolutionPath = rows[0].Path;
                    break;
            }
        }

        // ==================================================================
        //  渲染 / 视觉树工具
        // ==================================================================

        private static Border CreateHost(SolutionListView view, double width, double height)
        {
            var host = new Border
            {
                Width = width,
                Height = height,
                Background = Application.Current.TryFindResource("DialogBackgroundBrush") as Brush,
                Child = view,
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

        private static DataGridRow RowOf(DataGrid grid, object item)
        {
            if (grid == null || item == null) return null;
            grid.ScrollIntoView(item);
            grid.UpdateLayout();
            return grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        }

        /// <summary>
        /// 离屏 Border 宿主没有 PresentationSource，UIElement.IsVisible 恒 false；
        /// 沿视觉树检查每一层的 Visibility DP，判断"在当前模板里是否真的会显示"。
        /// </summary>
        private static bool EffectivelyVisible(DependencyObject element)
        {
            if (element == null) return false;

            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            }
            return true;
        }

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
