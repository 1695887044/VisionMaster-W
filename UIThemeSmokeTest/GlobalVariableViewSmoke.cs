using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using HalconDotNet;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 变量管理视图冒烟 / 渲染（--global-variables / --global-variables-render）。
    ///
    /// 用**真实** GlobalVariableManagerViewModel + 真实视图，而不是只有 AddCommand 的桩 VM：
    /// code-behind 的 HookViewModel / ApplyDetailColumnMode / 分组视图只有在 DataContext
    /// 是真实 VM 时才会执行；桩 VM 会让左栏为空、三列收放不执行，断言会假绿。
    /// </summary>
    internal static class GlobalVariableViewSmoke
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

                // 真路径：系统变量住在 Workspace.SystemVariables（不进 GlobalVariables），
                // 明细树必须把它们并进来，否则「系统变量」分区在真机恒为 0 行、只读横幅下面是空表。
                using (var sysFixture = new Fixture())
                {
                    sysFixture.Workspace.SystemVariables.Add(
                        new SystemVariableModel(Guid.NewGuid(), "Motion.X.Position", typeof(double), "X 轴位置（只读）"));
                    sysFixture.Workspace.SystemVariables.Add(
                        new SystemVariableModel(Guid.NewGuid(), "System.Io.Cam1.Online", typeof(bool), "相机在线（只读）"));

                    sysFixture.Vm.OnDialogOpened(new DialogParameters());
                    var systemSource = sysFixture.Vm.SourceNodes.First(s => s.Key == GlobalVariableManagerViewModel.SystemSourceKey);
                    sysFixture.Vm.SelectedSource = systemSource;
                    Check("真路径：SystemVariables 进入「系统变量」分区（计数 2 + 明细 2 行）",
                        systemSource.Count == 2
                        && sysFixture.Vm.FilteredDisplayNodes.Count == 2
                        && sysFixture.Vm.FilteredDisplayNodes.All(n => n.IsSystemOwned),
                        $"count={systemSource.Count} rows={sysFixture.Vm.FilteredDisplayNodes.Count}");
                }

                using var fixture = new Fixture();
                var view = new GlobalVariableView { DataContext = fixture.Vm };
                var host = CreateHost(view, 1300, 700);
                try
                {
                    Populate(fixture.Vm, "local");
                    Pump(host);

                    var sourceList = view.FindName("SourceList") as ListBox;
                    var grid = view.FindName("DetailGrid") as DataGrid;
                    var banner = view.FindName("SystemBanner") as Border;
                    var manage = view.FindName("ManageGroupsButton") as Button;
                    var add = view.FindName("AddButton") as Button;
                    var emptyOverlay = view.FindName("EmptySourceOverlay") as Grid;
                    var searchOverlay = view.FindName("SearchEmptyOverlay") as Grid;

                    Check("视图真实实例化 + DataContext 是真实 VM",
                        view.DataContext is GlobalVariableManagerViewModel && Descendants<FrameworkElement>(view).Count() > 80,
                        $"elements={Descendants<FrameworkElement>(view).Count()}");

                    Check("左栏来源列表拿到分组视图（GroupDescriptions=1、Groups=2）",
                        sourceList?.ItemsSource is ICollectionView view2
                        && view2.GroupDescriptions.Count == 1
                        && view2.Groups != null
                        && view2.Groups.Count == 2,
                        sourceList == null ? "SourceList=null" : $"items={sourceList.Items.Count} groups={(sourceList.ItemsSource as ICollectionView)?.Groups?.Count}");

                    Check("左栏选中项与 VM 双向一致", ReferenceEquals(sourceList?.SelectedItem, fixture.Vm.SelectedSource),
                        sourceList?.SelectedItem?.GetType().Name ?? "null");

                    Check("本地来源：来源/扫描组/地址三列收起",
                        ColumnVisibility(grid, 2) == Visibility.Collapsed
                        && ColumnVisibility(grid, 3) == Visibility.Collapsed
                        && ColumnVisibility(grid, 4) == Visibility.Collapsed,
                        $"cols={ColumnVisibility(grid, 2)},{ColumnVisibility(grid, 3)},{ColumnVisibility(grid, 4)}");

                    Check("本地来源：系统横幅收起、管理扫描组收起、添加变量可见",
                        !EffectivelyVisible(banner)
                        && !EffectivelyVisible(manage)
                        && EffectivelyVisible(add),
                        $"banner={banner?.Visibility} manage={manage?.Visibility} add={add?.Visibility}");

                    var okNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "OKCount");
                    var okRow = RowOf(grid, okNode);
                    var okBox = okRow == null ? null : Descendants<TextBox>(okRow).FirstOrDefault(t => CellValueEditBehavior.GetActive(t));
                    // 必须判「有效可见」：TextBox 自身 Visibility 恒为 Visible，被折叠的是它的父 StackPanel。
                    // 只看自身会让 EditableRoot 写坏时照样绿（R38 那类静默放行）。
                    Check("本地可编辑标量：当前值 TextBox + CellValueEditBehavior 生效",
                        okBox != null && EffectivelyVisible(okBox),
                        okBox == null ? "no active TextBox" : $"active={CellValueEditBehavior.GetActive(okBox)} visible={EffectivelyVisible(okBox)}");

                    var arrayNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "DefectFlags");
                    var arrayRow = RowOf(grid, arrayNode);
                    var arrayButton = arrayRow == null ? null : Descendants<Button>(arrayRow).FirstOrDefault(b => (b.Content as string) == "强制覆盖集合...");
                    Check("本地数组根：显示「强制覆盖集合...」", arrayButton != null && EffectivelyVisible(arrayButton),
                        arrayButton == null ? "button=null" : arrayButton.Visibility.ToString());

                    var imageNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "TemplateImage");
                    var imageRow = RowOf(grid, imageNode);
                    Check("图像变量：当前值显示「图像由流程写入」且无编辑 TextBox",
                        imageRow != null
                        && Descendants<TextBlock>(imageRow).Any(t => t.Text == "图像由流程写入" && EffectivelyVisible(t))
                        && Descendants<TextBox>(imageRow).All(t => !EffectivelyVisible(t)),
                        imageRow == null
                            ? "row=null"
                            : $"text={Descendants<TextBlock>(imageRow).Count(t => t.Text == "图像由流程写入")} visibleText={Descendants<TextBlock>(imageRow).Any(t => t.Text == "图像由流程写入" && EffectivelyVisible(t))} visibleBoxes={Descendants<TextBox>(imageRow).Count(EffectivelyVisible)}");

                    var okHeight = okRow?.ActualHeight ?? 0;
                    var childNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "[0]");
                    var childRow = RowOf(grid, childNode);
                    var childHeight = childRow?.ActualHeight ?? 0;
                    Check("行高：根行约 54、数组子行约 40", okHeight >= 52 && okHeight <= 58 && childHeight >= 38 && childHeight <= 44,
                        $"root={okHeight:0.#} child={childHeight:0.#}");

                    // 展开箭头：模拟真实点击（ToggleButtonAutomationPeer），而不是直接 Execute 命令。
                    // 直接 Execute 只能证明命令会翻转 VM，抓不到 IsChecked 被改成 TwoWay 后的"点击双翻转"。
                    var toggle = Descendants<ToggleButton>(view).FirstOrDefault(t => ReferenceEquals(t.CommandParameter, arrayNode));
                    bool before = arrayNode.IsExpanded;
                    bool clicked = false;
                    if (toggle != null)
                    {
                        // 走 ToggleButton 自己的 OnClick（与真实鼠标点击同一路径：先翻转 IsChecked，再执行 Command）。
                        // 离屏 Border 没有 PresentationSource，AutomationPeer.Toggle 会静默不生效，不能用来验点击。
                        var onClick = typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (onClick != null)
                        {
                            onClick.Invoke(toggle, null);
                            clicked = true;
                        }
                    }
                    Pump(host);
                    // ToggleNode 会 Clear/重建 DisplayNodes，行容器可能被回收；这里只判点击是否真的翻转 VM。
                    // OneWay 由静态检查（XAML 必须出现 IsChecked=...Mode=OneWay）守门：
                    // 若改成 TwoWay，真实点击会先写 IsExpanded=true、命令再翻回 false，本断言会准确变红。
                    Check("展开箭头：真实点击后 IsExpanded 翻转（OneWay 由静态断言守）",
                        toggle != null && toggle.Command != null && clicked
                        && arrayNode.IsExpanded != before,
                        $"toggle={toggle != null} command={toggle?.Command != null} clicked={clicked} before={before} expanded={arrayNode.IsExpanded}");

                    // 网络来源：三列展开、管理扫描组可见、系统横幅收起
                    Populate(fixture.Vm, "network");
                    Pump(host);
                    Check("网络来源：三列展开 + 管理扫描组可见",
                        ColumnVisibility(grid, 2) == Visibility.Visible
                        && ColumnVisibility(grid, 3) == Visibility.Visible
                        && ColumnVisibility(grid, 4) == Visibility.Visible
                        && EffectivelyVisible(manage),
                        $"cols={ColumnVisibility(grid, 2)},{ColumnVisibility(grid, 3)},{ColumnVisibility(grid, 4)} manage={manage?.Visibility}");

                    var speedNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "SpeedSet");
                    var speedRow = RowOf(grid, speedNode);
                    var speedBox = speedRow == null ? null : Descendants<TextBox>(speedRow).FirstOrDefault(t => CellValueEditBehavior.GetActive(t));
                    Check("网络可写标量：当前值 TextBox 可编辑（不误判只读）",
                        speedBox != null && EffectivelyVisible(speedBox),
                        speedBox == null ? "no active TextBox" : $"visible={EffectivelyVisible(speedBox)}");

                    var partNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "PartReady");
                    var partRow = RowOf(grid, partNode);
                    Check("网络只读存储区：无可见编辑 TextBox、显示锁 + 值",
                        partRow != null
                        && Descendants<TextBox>(partRow).All(t => !EffectivelyVisible(t))
                        && Descendants<TextBlock>(partRow).Any(t => string.Equals(t.Text, "False", StringComparison.OrdinalIgnoreCase) && EffectivelyVisible(t)),
                        partRow == null
                            ? "row=null"
                            : $"text={string.Join("/", Descendants<TextBlock>(partRow).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)))} visibleBoxes={Descendants<TextBox>(partRow).Count(t => EffectivelyVisible(t))}");

                    // 系统来源：横幅、只读 chip、复制保留、重命名/删除隐藏
                    Populate(fixture.Vm, "system");
                    Pump(host);
                    Check("系统来源：只读横幅可见、管理扫描组收起、添加变量仍可见",
                        EffectivelyVisible(banner)
                        && !EffectivelyVisible(manage)
                        && EffectivelyVisible(add),
                        $"banner={banner?.Visibility} manage={manage?.Visibility} add={add?.Visibility}");

                    var systemNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "Motion.X.Position");
                    var systemRow = RowOf(grid, systemNode);
                    var systemButtons = systemRow == null ? new List<Button>() : Descendants<Button>(systemRow).ToList();
                    // copy 用有效可见性：它同时证明整个操作列没有被折叠；否则下面两条 hidden 会被父级折叠"带绿"。
                    bool copyVisible = systemButtons.Any(b => CommandPath(b) == "CopyNameCommand" && EffectivelyVisible(b));
                    // 先 Any 再 All：按钮被整颗删掉时 Where 为空，单用 All 会真空通过。
                    bool renameHidden = systemButtons.Any(b => CommandPath(b) == "RenameCommand")
                        && systemButtons.Where(b => CommandPath(b) == "RenameCommand").All(b => !EffectivelyVisible(b));
                    bool deleteHidden = systemButtons.Any(b => CommandPath(b) == "DeleteCommand")
                        && systemButtons.Where(b => CommandPath(b) == "DeleteCommand").All(b => !EffectivelyVisible(b));
                    bool readonlyChip = systemRow != null && Descendants<TextBlock>(systemRow).Any(t => t.Text == "只读" && EffectivelyVisible(t));
                    Check("系统行：复制保留 / 重命名删除隐藏 / 只读 chip",
                        copyVisible && renameHidden && deleteHidden && readonlyChip,
                        $"copy={copyVisible} renameHidden={renameHidden} deleteHidden={deleteHidden} chip={readonlyChip}");
                    Check("系统行：当前值显示锁 + 值，无可见编辑 TextBox",
                        systemRow != null
                        && Descendants<TextBox>(systemRow).All(t => !EffectivelyVisible(t))
                        && Descendants<TextBlock>(systemRow).Any(t => t.Text == "123.456" && EffectivelyVisible(t)),
                        systemRow == null
                            ? "row=null"
                            : $"text={string.Join("/", Descendants<TextBlock>(systemRow).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)))} visibleBoxes={Descendants<TextBox>(systemRow).Count(t => EffectivelyVisible(t))}");

                    // 空态 / 搜索无结果
                    Populate(fixture.Vm, "empty");
                    Pump(host);
                    Check("空来源：空态覆盖层可见、搜索无结果层收起",
                        EffectivelyVisible(emptyOverlay) && !EffectivelyVisible(searchOverlay),
                        $"empty={emptyOverlay?.Visibility} search={searchOverlay?.Visibility}");

                    Populate(fixture.Vm, "search");
                    Pump(host);
                    Check("搜索无结果：搜索层可见、空态层收起",
                        !EffectivelyVisible(emptyOverlay) && EffectivelyVisible(searchOverlay),
                        $"empty={emptyOverlay?.Visibility} search={searchOverlay?.Visibility}");

                    Populate(fixture.Vm, "local");
                    Pump(host);
                    Check("有结果时两个覆盖层都收起",
                        !EffectivelyVisible(emptyOverlay) && !EffectivelyVisible(searchOverlay),
                        $"empty={emptyOverlay?.Visibility} search={searchOverlay?.Visibility}");

                    // 选中双向 + 网络→系统→网络
                    var localNode = fixture.Vm.SourceNodes.First(s => s.Key == "Local");
                    var systemSource = fixture.Vm.SourceNodes.First(s => s.Key == GlobalVariableManagerViewModel.SystemSourceKey);
                    var netSource = fixture.Vm.SourceNodes.First(s => s.IsNetwork && s.IsOnline);
                    sourceList.SelectedItem = systemSource;
                    Pump(host);
                    Check("ListBox 选中 → VM.SelectedSource（系统节点可达）",
                        ReferenceEquals(fixture.Vm.SelectedSource, systemSource),
                        fixture.Vm.SelectedSource?.Key ?? "null");
                    fixture.Vm.SelectedSource = netSource;
                    Pump(host);
                    Check("VM.SelectedSource → ListBox 选中（网络节点）",
                        ReferenceEquals(sourceList.SelectedItem, netSource),
                        sourceList.SelectedItem?.GetType().Name ?? "null");
                    fixture.Vm.SelectedSource = systemSource;
                    Pump(host);
                    Check("网络 → 系统：系统来源保持选中，不被 IsNetworkSource 改回本地",
                        ReferenceEquals(fixture.Vm.SelectedSource, systemSource) && !fixture.Vm.IsNetworkSource,
                        fixture.Vm.SelectedSource?.Key ?? "null");
                    fixture.Vm.SelectedSource = netSource;
                    Pump(host);
                    Check("系统 → 网络：网络来源可达且 IsNetworkSource 跟随",
                        ReferenceEquals(fixture.Vm.SelectedSource, netSource) && fixture.Vm.IsNetworkSource,
                        fixture.Vm.SelectedSource?.Key ?? "null");

                    // ToolTip 显式样式：统计全部挂载点，不只看第一个按钮
                    var toolTipOwners = Descendants<FrameworkElement>(view)
                        .Where(e => e.ToolTip is ToolTip)
                        .ToList();
                    var styledTips = toolTipOwners.Count(e =>
                        ReferenceEquals(((ToolTip)e.ToolTip).Style, Application.Current.TryFindResource("DialogToolTipStyle")));
                    Check("ToolTip 全部显式使用 DialogToolTipStyle",
                        toolTipOwners.Count > 0 && styledTips == toolTipOwners.Count,
                        $"{styledTips}/{toolTipOwners.Count}");
                }
                finally
                {
                    host.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 变量管理视图冒烟抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 变量管理视图冒烟：全部通过 ==="
                : $"### 变量管理视图冒烟：{failures} 项失败");
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
                var outDir = Path.Combine(RepoRoot, "docs", "UI设计基线", "global-variables", "evidence");
                Directory.CreateDirectory(outDir);

                using var fixture = new Fixture();
                var view = new GlobalVariableView { DataContext = fixture.Vm };
                var host = CreateHost(view, 1300, 700);
                try
                {
                    foreach (var mode in new[] { "local", "network", "network-offline", "system", "empty", "search", "write-error" })
                    {
                        Populate(fixture.Vm, mode);
                        Pump(host);
                        if (mode == "write-error")
                            MarkWriteError(view);
                        Pump(host);
                        var path = Path.Combine(outDir, "after-render-v1-" + mode + ".png");
                        RenderElement(host, path);
                        Check("渲染 " + mode, File.Exists(path), path);
                    }

                    // 1200 窄档（网络来源）：必须显式重新 Measure/Arrange，否则 Border 会沿用上一档 1300 的布局
                    host.Width = 1200;
                    host.Height = 700;
                    host.Measure(new Size(1200, 700));
                    host.Arrange(new Rect(0, 0, 1200, 700));
                    Populate(fixture.Vm, "network");
                    Pump(host);
                    var narrowPath = Path.Combine(outDir, "after-render-v1-narrow.png");
                    RenderElement(host, narrowPath);
                    Check("渲染 narrow", File.Exists(narrowPath), narrowPath);

                    // 在线/离线副行必须不同（真实 VM 没有连接时 IsOnline 恒 false，所以 Populate 里显式设 true）
                    var online = fixture.Vm.SourceNodes.First(s => s.Key == "PLC_Modbus");
                    var offline = fixture.Vm.SourceNodes.First(s => s.Key == "PLC_S7_1");
                    Check("网络在线/离线副行不同", online.Subtitle != offline.Subtitle,
                        $"online={online.Subtitle} offline={offline.Subtitle}");
                }
                finally
                {
                    host.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 变量管理视图渲染抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 变量管理视图渲染：完成 ==="
                : $"### 变量管理视图渲染：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        // ==================================================================
        //  静态检查
        // ==================================================================

        private static void RunStaticChecks(Action<string, bool, string> check)
        {
            var xamlPath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            var codePath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\GlobalVariableView.xaml.cs");
            var xamlRaw = File.Exists(xamlPath) ? File.ReadAllText(xamlPath) : string.Empty;
            var code = File.Exists(codePath) ? File.ReadAllText(codePath) : string.Empty;
            check("视图 XAML 可读", xamlRaw.Length > 0, xamlPath);
            check("code-behind 可读", code.Length > 0, codePath);
            if (xamlRaw.Length == 0 || code.Length == 0) return;

            var xaml = Regex.Replace(xamlRaw, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            check("XAML 无硬编码十六进制颜色", !Regex.IsMatch(xaml, @"#[0-9A-Fa-f]{6,8}"), "");
            check("XAML 无本地 keyed Style（CollectedDialogViews 纪律）", !Regex.IsMatch(xaml, @"<Style\b[^>]*x:Key="), "");

            string[] required =
            {
                "ItemsSource=\"{Binding FilteredDisplayNodes}\"",
                "SelectedItem=\"{Binding SelectedSource, Mode=TwoWay}\"",
                "Text=\"{Binding SearchText, UpdateSourceTrigger=PropertyChanged}\"",
                "Command=\"{Binding AddCommand}\"",
                "Command=\"{Binding ManageScanGroupsCommand}\"",
                "Command=\"{Binding DataContext.DeleteCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "Command=\"{Binding DataContext.RenameCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "Command=\"{Binding DataContext.CopyNameCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "Command=\"{Binding DataContext.ResetCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "Command=\"{Binding DataContext.EditArrayCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "Command=\"{Binding DataContext.ToggleNodeCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "IsChecked=\"{Binding IsExpanded, Mode=OneWay}\"",
                "ItemsSource=\"{Binding ScanGroupOptions}\"",
                "SelectedItem=\"{Binding ScanGroupText",
                "{Binding ShowScanGroup",
                "CurrentValueText",
                "IsValueDeviated",
                "IsValueReadOnly",
                "IsSystemOwned",
                "IsEditableNode",
                "x:Name=\"AddButton\"",
                "x:Name=\"ManageGroupsButton\"",
                "x:Name=\"SystemBanner\"",
                "x:Name=\"EmptySourceOverlay\"",
                "x:Name=\"SearchEmptyOverlay\"",
                "x:Name=\"SourceColumn\"",
                "x:Name=\"ScanGroupColumn\"",
                "x:Name=\"AddressColumn\"",
                "RowHeight=\"{x:Static sys:Double.NaN}\"",
                "MinRowHeight=\"40\"",
                "AlternationCount=\"1\"",
                "HorizontalScrollBarVisibility=\"Auto\"",
                "ToolTip Style=\"{StaticResource DialogToolTipStyle}\"",
            };
            var missing = required.Where(r => !xaml.Contains(r, StringComparison.Ordinal)).ToList();
            check("关键绑定/结构全部在 XAML 里", missing.Count == 0, string.Join(" | ", missing));

            check("当前值两处 CellValueEditBehavior + DialogCellInputQuiet",
                Regex.Matches(xaml, "CellValueEditBehavior\\.Active").Count >= 2
                && Regex.Matches(xaml, "DialogCellInputQuiet").Count >= 2,
                $"behavior={Regex.Matches(xaml, "CellValueEditBehavior\\.Active").Count} quiet={Regex.Matches(xaml, "DialogCellInputQuiet").Count}");

            check("code-behind 保留 HookViewModel 双路调用",
                Regex.Matches(code, "HookViewModel\\(\\);").Count >= 2,
                $"count={Regex.Matches(code, "HookViewModel\\(\\);").Count}");
            check("code-behind 使用 PropertyGroupDescription(IsNetwork) 分组",
                code.Contains("PropertyGroupDescription") && code.Contains("nameof(VariableSourceNode.IsNetwork)"),
                "");
            check("视图使用 TypeAbbreviationConverter", xaml.Contains("TypeAbbreviationConverter"), "");
        }

        private static void RunConverterChecks(Action<string, bool, string> check)
        {
            var abbrev = new TypeAbbreviationConverter();
            string A(string v) => (string)abbrev.Convert(v, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
            check("TypeAbbreviationConverter：Int32/Double/String/Boolean/HImage/数组/null",
                A("Int32") == "INT" && A("Double") == "DBL" && A("String") == "STR"
                && A("Boolean") == "BOOL" && A("HImage") == "IMG" && A("Boolean[]") == "ARR" && A(null) == "VAR",
                $"{A("Int32")}/{A("Double")}/{A("String")}/{A("Boolean")}/{A("HImage")}/{A("Boolean[]")}/{A(null)}");

            var brush = new DataTypeBrushConverter();
            var intBrush = brush.Convert("Int32", typeof(Brush), "Strong", System.Globalization.CultureInfo.InvariantCulture);
            var arrayBrush = brush.Convert("Boolean[]", typeof(Brush), "Strong", System.Globalization.CultureInfo.InvariantCulture);
            check("DataTypeBrushConverter：int→DialogAccentBrush、数组→DialogTypeArrayBrush",
                ReferenceEquals(intBrush, Application.Current.TryFindResource("DialogAccentBrush"))
                && ReferenceEquals(arrayBrush, Application.Current.TryFindResource("DialogTypeArrayBrush")),
                "");

            var empty = new EmptyStateVisibilityConverter();
            object E(int count, string search, string mode) => empty.Convert(new object[] { count, search }, typeof(Visibility), mode, System.Globalization.CultureInfo.InvariantCulture);
            check("EmptyStateVisibilityConverter：空态/搜索/有结果三态",
                (Visibility)E(0, "", "Empty") == Visibility.Visible
                && (Visibility)E(0, "XYZ", "Search") == Visibility.Visible
                && (Visibility)E(3, "", "Empty") == Visibility.Collapsed,
                "");

            // Subtitle 三态 + IsOnline 通知
            var local = new VariableSourceNode { Key = "Local", IsNetwork = false, IsOnline = true };
            var system = new VariableSourceNode { Key = GlobalVariableManagerViewModel.SystemSourceKey, IsNetwork = false, IsOnline = true };
            var net = new VariableSourceNode { Key = "PLC_Modbus", IsNetwork = true, IsOnline = true, ProtocolText = "Modbus TCP" };
            int subtitleNotifications = 0;
            net.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VariableSourceNode.Subtitle)) subtitleNotifications++; };
            net.IsOnline = false;
            check("Subtitle 三态 + IsOnline 变化通知",
                local.Subtitle == "内存变量 · 可编辑"
                && system.Subtitle == "宿主维护 · 只读"
                && net.Subtitle == "Modbus TCP · 离线"
                && subtitleNotifications == 1,
                $"{local.Subtitle} | {system.Subtitle} | {net.Subtitle} | notify={subtitleNotifications}");
        }

        // ==================================================================
        //  数据夹具
        // ==================================================================

        private static void Populate(GlobalVariableManagerViewModel vm, string mode)
        {
            vm.SearchText = string.Empty;
            vm.SourceNodes.Clear();
            vm.FilteredDisplayNodes.Clear();

            var local = new VariableSourceNode { Key = "Local", DisplayName = "本地变量", Icon = "\uf15b", Count = 6, IsOnline = true };
            var system = new VariableSourceNode { Key = GlobalVariableManagerViewModel.SystemSourceKey, DisplayName = "系统变量", Icon = "\uf013", Count = 8, IsOnline = true };
            var net = new VariableSourceNode { Key = "PLC_Modbus", DisplayName = "PLC_Modbus", Icon = "\uf1eb", IsNetwork = true, ProtocolText = "Modbus TCP", Count = 5, IsOnline = true };
            var netOff = new VariableSourceNode { Key = "PLC_S7_1", DisplayName = "PLC_S7_1", Icon = "\uf1eb", IsNetwork = true, ProtocolText = "Siemens S7", Count = 3, IsOnline = false };
            vm.SourceNodes.Add(local);
            vm.SourceNodes.Add(system);
            vm.SourceNodes.Add(net);
            vm.SourceNodes.Add(netOff);

            switch (mode)
            {
                case "local":
                    vm.SelectedSource = local;
                    AddLocalRows(vm);
                    break;
                case "network":
                    vm.SelectedSource = net;
                    AddNetworkRows(vm, "PLC_Modbus", true);
                    break;
                case "network-offline":
                    vm.SelectedSource = netOff;
                    AddNetworkRows(vm, "PLC_S7_1", false);
                    break;
                case "system":
                    vm.SelectedSource = system;
                    AddSystemRows(vm);
                    break;
                case "empty":
                    local.Count = 0;
                    vm.SelectedSource = local;
                    break;
                case "search":
                    vm.SelectedSource = local;
                    AddLocalRows(vm);
                    vm.SearchText = "XYZ";
                    break;
                case "write-error":
                    vm.SelectedSource = net;
                    AddNetworkRows(vm, "PLC_Modbus", true);
                    break;
            }
        }

        private static void AddLocalRows(GlobalVariableManagerViewModel vm)
        {
            AddRoot(vm, NewVar("OKCount", typeof(int), 0, 128, "本班 OK 计数"), false, true, null, null);
            AddRoot(vm, NewVar("NGCount", typeof(int), 0, 3, "本班 NG 计数"), false, true, null, null);
            AddRoot(vm, NewVar("YieldRate", typeof(double), 0d, 0.976, "实时良率（OK / 总数）"), false, true, null, null);
            AddRoot(vm, NewVar("LastResult", typeof(string), "", "OK", "最近一次判定结果"), false, true, null, null);

            // 数组根必须用真实 LocalVariableModel：视图的「强制覆盖集合...」分支会检查
            // OriginalModel is LocalVariableModel（防止系统/网络数组出现死按钮）。
            var flags = new LocalVariableModel
            {
                Name = "DefectFlags",
                DataType = typeof(bool[]),
                Description = "四工位缺陷标记",
                DefaultValue = new[] { false, false, false },
                Value = new[] { true, false, true },
            };
            var flagsRoot = Root(flags, false, true, null, null);
            flagsRoot.IsExpanded = true;
            vm.FilteredDisplayNodes.Add(flagsRoot);
            for (int i = 0; i < 3; i++)
            {
                var child = Child(flagsRoot, i, ((bool[])flags.Value)[i]);
                vm.FilteredDisplayNodes.Add(child);
            }

            AddRoot(vm, NewVar("TemplateImage", typeof(HImage), null, "", "模板图像（由流程写入）"), false, true, null, null);
        }

        private static void AddNetworkRows(GlobalVariableManagerViewModel vm, string connection, bool online)
        {
            var quality = online ? VariableQuality.Good : VariableQuality.Bad;
            if (!online)
            {
                AddRoot(vm, NewVar("AxisReady", typeof(bool), false, true, "轴就绪", VariableType.Communication, connection, quality), true, false, "M0.0", "默认组");
                AddRoot(vm, NewVar("CycleTime", typeof(double), 0d, 842.5, "节拍 (ms)", VariableType.Communication, connection, quality), true, false, "DB1.DBD0", "默认组");
                AddRoot(vm, NewVar("RecipeNo", typeof(int), 0, 7, "配方号", VariableType.Communication, connection, quality), true, false, "MW10", "默认组");
                return;
            }

            AddRoot(vm, NewVar("Trigger", typeof(bool), false, true, "启动触发（线圈）", VariableType.Communication, connection, quality), true, online, "x=1;0", "快组 (20ms)");
            AddRoot(vm, NewVar("SpeedSet", typeof(double), 0d, 1.25, "输送线速度设定", VariableType.Communication, connection, quality), true, online, "x=3;100", "默认组");
            AddRoot(vm, NewVar("Temperature", typeof(double), 0d, 86.4, "烘箱温度", VariableType.Communication, connection, online ? VariableQuality.Uncertain : VariableQuality.Bad), true, online, "x=3;102", "快组 (20ms)");
            AddRoot(vm, NewVar("AlarmCode", typeof(int), 0, 0, "报警代码", VariableType.Communication, connection, quality), true, online, "x=3;104", "默认组");
            AddRoot(vm, NewVar("PartReady", typeof(bool), false, false, "来料就绪（离散输入）", VariableType.Communication, connection, quality, readOnly: true), true, online, "x=2;10", "默认组");
        }

        private static void AddSystemRows(GlobalVariableManagerViewModel vm)
        {
            AddRoot(vm, NewVar("Motion.X.Position", typeof(double), 0d, 123.456, "X 轴当前位置 (mm)", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("Motion.X.Velocity", typeof(double), 0d, 0d, "X 轴当前速度 (mm/s)", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("Motion.Y.Position", typeof(double), 0d, 45.2, "Y 轴当前位置 (mm)", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("System.Io.Camera1.Online", typeof(bool), false, true, "相机 1 在线", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("System.Io.PLC_Modbus.Online", typeof(bool), false, true, "PLC 在线", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("System.Io.PLC_Modbus.State", typeof(int), 0, 1, "连接状态码", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("System.Io.StartButton", typeof(bool), false, false, "启动按钮", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
            AddRoot(vm, NewVar("System.Io.TowerRed", typeof(bool), false, false, "红灯输出", VariableType.System, null, VariableQuality.Good, readOnly: true), false, true, null, null);
        }

        private static void AddRoot(GlobalVariableManagerViewModel vm, FakeVariable model, bool isNetwork, bool isConnected, string address, string scanGroup)
            => vm.FilteredDisplayNodes.Add(Root(model, isNetwork, isConnected, address, scanGroup));

        private static VariableNode Root(IVariable model, bool isNetwork, bool isConnected, string address, string scanGroup)
        {
            var node = new VariableNode
            {
                OriginalModel = model,
                Name = model.Name,
                DataType = model.DataType,
                TypeName = model.DataType?.Name,
                Description = model.Description,
                ChildDefaultValue = model.DefaultValue,
                ChildValue = model.Value,
                Level = 0,
                IsNetwork = isNetwork,
                SourceLabel = isNetwork ? model.ConnectionName : "本地",
                Address = address,
                IsConnected = isConnected,
            };

            if (isNetwork)
            {
                node.ScanGroupOptions.Add("默认组");
                node.ScanGroupOptions.Add("快组 (20ms)");
                node.ScanGroupText = scanGroup ?? "默认组";
            }

            return node;
        }

        private static VariableNode Child(VariableNode parent, int index, object value)
        {
            var elementType = parent.DataType?.GetElementType() ?? typeof(object);
            var child = new VariableNode
            {
                OriginalModel = parent.OriginalModel,
                Name = "[" + index + "]",
                DataType = elementType,
                TypeName = elementType.Name,
                ChildValue = value,
                ElementIndex = index,
                Level = 1,
                IsNetwork = parent.IsNetwork,
                SourceLabel = parent.SourceLabel,
                IsConnected = parent.IsConnected,
            };
            parent.Children.Add(child);
            return child;
        }

        private static FakeVariable NewVar(
            string name,
            Type type,
            object defaultValue,
            object value,
            string description,
            VariableType variableType = VariableType.Local,
            string connectionName = null,
            VariableQuality quality = VariableQuality.Good,
            bool readOnly = false)
        {
            return new FakeVariable
            {
                Name = name,
                DataType = type,
                DefaultValue = defaultValue,
                Value = value ?? string.Empty,
                Description = description,
                VariableType = variableType,
                ConnectionName = connectionName,
                Quality = quality,
                ReadOnly = readOnly,
            };
        }

        // ==================================================================
        //  渲染 / 视觉树工具
        // ==================================================================

        private static Border CreateHost(GlobalVariableView view, double width, double height)
        {
            var host = new Border
            {
                Width = width,
                Height = height,
                Background = Application.Current.TryFindResource("DialogSurfaceBrush") as Brush,
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

        private static void MarkWriteError(GlobalVariableView view)
        {
            var box = Descendants<TextBox>(view)
                .FirstOrDefault(t => t.DataContext is VariableNode n && n.Name == "SpeedSet" && CellValueEditBehavior.GetActive(t));
            var binding = box?.GetBindingExpression(TextBox.TextProperty);
            if (binding == null) return;

            Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(), binding)
            {
                ErrorContent = "写入失败：连接已断开，设备未确认（显示上次值）",
            });
        }

        private static Visibility ColumnVisibility(DataGrid grid, int index)
            => grid != null && grid.Columns.Count > index ? grid.Columns[index].Visibility : Visibility.Collapsed;

        private static DataGridRow RowOf(DataGrid grid, object item)
        {
            if (grid == null || item == null) return null;
            grid.ScrollIntoView(item);
            grid.UpdateLayout();
            return grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        }

        /// <summary>
        /// 离屏 Border 宿主没有 PresentationSource，UIElement.IsVisible 恒 false；
        /// 这里沿视觉树检查每一层的 Visibility DP，判断"在当前模板里是否真的会显示"。
        /// </summary>
        private static bool EffectivelyVisible(DependencyObject element)
        {
            // null 必须判"不可见"：否则 FindName 取不到元素时会被当成可见/收起，
            // 让"按钮被整颗删掉"这类回归静默通过。
            if (element == null) return false;

            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            }
            return true;
        }

        private static string CommandPath(Button button)
        {
            var path = button == null ? null : BindingOperations.GetBinding(button, Button.CommandProperty)?.Path?.Path;
            return path != null && path.StartsWith("DataContext.", StringComparison.Ordinal)
                ? path.Substring("DataContext.".Length)
                : path;
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

        // ==================================================================
        //  夹具：真实 VM + 真实服务
        // ==================================================================

        private sealed class Fixture : IDisposable
        {
            public WorkspaceContext Workspace { get; }
            public AdvancedCommunicationManager Manager { get; }
            public NetworkVariableBridge Bridge { get; }
            public GlobalVariableManagerViewModel Vm { get; }

            public Fixture()
            {
                Workspace = new WorkspaceContext();
                Workspace.GlobalVariables.Clear();
                Manager = new AdvancedCommunicationManager();
                Bridge = new NetworkVariableBridge(Workspace, Manager, new StubLog());
                Vm = new GlobalVariableManagerViewModel(Workspace, Manager, Bridge, new SilentDialogService());
            }

            public void Dispose()
            {
                try { Vm.Dispose(); } catch { }
                try { Bridge.Dispose(); } catch { }
                try { Manager.Dispose(); } catch { }
            }
        }

        private sealed class FakeVariable : IVariable, IVariableDisplaySource
        {
            public Guid VariableId { get; } = Guid.NewGuid();
            public string Name { get; set; } = string.Empty;
            public Type DataType { get; set; } = typeof(int);
            public VariableType VariableType { get; set; } = VariableType.Local;
            public string ConnectionName { get; set; }
            public DeviceAddressBase AddressConfig { get; set; }
            public object DefaultValue { get; set; }
            public object Value { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public VariableQuality Quality { get; set; } = VariableQuality.Good;
            public bool ReadOnly { get; set; }

            public event EventHandler ValueChanged;
            public event PropertyChangedEventHandler PropertyChanged;

            public void ResetToDefault() => Value = DefaultValue;

            string IVariableDisplaySource.BindingKey => Name;
            string IVariableDisplaySource.DisplayLabel => Description;
            Type IVariableDisplaySource.DataType => DataType;
            object IVariableDisplaySource.Value => Value;
            string IVariableDisplaySource.SourceLabel => VariableType == VariableType.Communication ? ConnectionName ?? string.Empty : "本地";
            bool IVariableDisplaySource.IsReadOnly => ReadOnly || VariableType is VariableType.System or VariableType.Io;
            void IVariableDisplaySource.WriteValue(object newValue) => Value = newValue;
        }

        private sealed class StubLog : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }

        private sealed class SilentDialogService : IDialogService
        {
            public void ShowDialog(string name, IDialogParameters parameters) { }
            public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback) { }
            public void ShowDialog(string name, IDialogParameters parameters, string windowName) { }
            public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback, string windowName) { }
            public void Show(string name, IDialogParameters parameters) { }
            public void Show(string name, IDialogParameters parameters, DialogCallback callback) { }
            public void Show(string name, IDialogParameters parameters, string windowName) { }
            public void Show(string name, IDialogParameters parameters, DialogCallback callback, string windowName) { }
        }
    }
}
