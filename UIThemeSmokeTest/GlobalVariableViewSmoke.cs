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
using MiniExcelLibs;
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

        /// <summary>冒烟夹具里的「监控分组」Id：必须与方案分组表一致，选中标签才不是 fallback。</summary>
        private static readonly Guid MonitorGroupId = Guid.NewGuid();

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
                    var manage = view.FindName("ManageVariableGroupsButton") as Button;
                    var add = view.FindName("AddButton") as Button;
                    var export = view.FindName("ExportButton") as Button;
                    var import = view.FindName("ImportButton") as Button;
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

                    Check("左栏选中项与 VM 双向一致", ReferenceEquals(sourceList?.SelectedItem, fixture.Vm.SelectedNode),
                        $"list={(sourceList?.SelectedItem as VariableSourceNode)?.DisplayName ?? "<null>"} vm={(fixture.Vm.SelectedNode as VariableSourceNode)?.DisplayName ?? "<null>"}");

                    Check("本地来源：分组/扫描组/地址三列收起",
                        ColumnVisibility(grid, 2) == Visibility.Collapsed
                        && ColumnVisibility(grid, 3) == Visibility.Collapsed
                        && ColumnVisibility(grid, 4) == Visibility.Collapsed,
                        $"cols={ColumnVisibility(grid, 2)},{ColumnVisibility(grid, 3)},{ColumnVisibility(grid, 4)}");

                    Check("本地来源：系统横幅收起、管理分组可见、添加变量可见、导出/导入收起",
                        !EffectivelyVisible(banner)
                        && EffectivelyVisible(manage)
                        && EffectivelyVisible(add)
                        && !EffectivelyVisible(export)
                        && !EffectivelyVisible(import),
                        $"banner={banner?.Visibility} manage={manage?.Visibility} add={add?.Visibility} " +
                        $"export={export?.Visibility} import={import?.Visibility}");

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
                    var arrayEdit = arrayRow == null ? null : Descendants<Button>(arrayRow)
                        .FirstOrDefault(b => Descendants<TextBlock>(b).Any(t => t.Text == "编辑数组…"));
                    var arraySummary = arrayRow == null ? null : Descendants<TextBlock>(arrayRow)
                        .FirstOrDefault(t => t.Text == "True · False …");
                    var arrayResets = arrayRow == null
                        ? new List<Button>()
                        : Descendants<Button>(arrayRow)
                            .Where(b => CommandPath(b) == "ResetCommand"
                                && Descendants<TextBlock>(b).Any(t => t.Text == "\uf0e2"))
                            .ToList();
                    bool arrayResetVisible = arrayResets.Any(EffectivelyVisible);
                    Check("本地数组根：摘要 + 「编辑数组…」+ 偏离时的 ↺（v2 数组编辑入口）",
                        arrayEdit != null && EffectivelyVisible(arrayEdit)
                        && arraySummary != null && EffectivelyVisible(arraySummary)
                        && arrayResetVisible,
                        $"edit={(arrayEdit == null ? "null" : EffectivelyVisible(arrayEdit).ToString())} " +
                        $"summary={(arraySummary == null ? "null" : EffectivelyVisible(arraySummary).ToString())} " +
                        $"resetVisible={arrayResetVisible} (candidates={arrayResets.Count})");
                    Check("本地数组根：旧「强制覆盖集合...」零残留",
                        arrayRow != null && Descendants<Button>(arrayRow)
                            .All(b => !((b.Content as string) ?? "").Contains("强制覆盖集合")),
                        "");

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
                    var childReset = childRow == null ? null : Descendants<Button>(childRow)
                        .FirstOrDefault(b => CommandPath(b) == "ResetElementCommand");
                    Check("数组元素行：偏离基准时出现逐元素 ↺（只恢复这一项）",
                        childReset != null && EffectivelyVisible(childReset),
                        childReset == null ? "button=null" : childReset.Visibility.ToString());
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

                    // 网络来源：三列展开、管理分组可见、系统横幅收起
                    Populate(fixture.Vm, "network");
                    Pump(host);
                    Check("网络来源：三列展开 + 管理分组可见 + 导出/导入可见且有命令",
                        ColumnVisibility(grid, 2) == Visibility.Visible
                        && ColumnVisibility(grid, 3) == Visibility.Visible
                        && ColumnVisibility(grid, 4) == Visibility.Visible
                        && EffectivelyVisible(manage)
                        && EffectivelyVisible(export)
                        && EffectivelyVisible(import)
                        && export?.Command != null
                        && import?.Command != null,
                        $"cols={ColumnVisibility(grid, 2)},{ColumnVisibility(grid, 3)},{ColumnVisibility(grid, 4)} " +
                        $"manage={manage?.Visibility} export={export?.Visibility}/{export?.Command != null} " +
                        $"import={import?.Visibility}/{import?.Command != null}");

                    var groupText = Descendants<TextBlock>(sourceList)
                        .FirstOrDefault(t => t.Text == "监控分组");
                    var templateSelector = view.Resources["GvSourceNodeSelector"] as VariableSourceNodeTemplateSelector;
                    var groupTemplate = view.Resources["GvGroupNodeTemplate"] as DataTemplate;
                    var monitorGroupNode = fixture.Vm.SourceNodes.FirstOrDefault(n => n.IsGroup && n.DisplayName == "监控分组");
                    var selectedTemplate = templateSelector?.SelectTemplate(monitorGroupNode, sourceList);
                    Check("左栏分组行按分组模板渲染（选择器返回 GroupTemplate，不是来源卡片）",
                        groupText != null && groupText.DataContext is VariableSourceNode { IsGroup: true }
                        && ReferenceEquals(selectedTemplate, groupTemplate),
                        groupText == null
                            ? "找不到「监控分组」文本"
                            : $"data={(groupText.DataContext as VariableSourceNode)?.DisplayName} templateIsGroup={ReferenceEquals(selectedTemplate, groupTemplate)}");

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

                    var recipeNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "RecipeBlock");
                    var recipeRow = RowOf(grid, recipeNode);
                    var recipeTexts = recipeRow == null
                        ? new List<string>()
                        : Descendants<TextBlock>(recipeRow)
                            .Where(t => EffectivelyVisible(t) && !string.IsNullOrEmpty(t.Text))
                            .Select(t => t.Text)
                            .ToList();
                    Check("网络数组根（可写区）：显示摘要 + 「写入数组…」、无编辑 TextBox",
                        recipeRow != null
                        && Descendants<TextBox>(recipeRow).All(t => !EffectivelyVisible(t))
                        && recipeTexts.Any(t => t.Contains("10", StringComparison.Ordinal) && t.Contains("20", StringComparison.Ordinal))
                        && recipeTexts.Any(t => t.Contains("写入数组", StringComparison.Ordinal))
                        && !recipeTexts.Any(t => string.Equals(t, "只读", StringComparison.Ordinal)),
                        recipeRow == null
                            ? "row=null"
                            : $"text={string.Join("/", recipeTexts)} visibleBoxes={Descendants<TextBox>(recipeRow).Count(t => EffectivelyVisible(t))}");

                    var netFlagsNode = fixture.Vm.FilteredDisplayNodes.First(n => n.Name == "NetFlags");
                    var netFlagsRow = RowOf(grid, netFlagsNode);
                    var netFlagsTexts = netFlagsRow == null
                        ? new List<string>()
                        : Descendants<TextBlock>(netFlagsRow)
                            .Where(t => EffectivelyVisible(t) && !string.IsNullOrEmpty(t.Text))
                            .Select(t => t.Text)
                            .ToList();
                    Check("网络数组根（只读区）：显示摘要 + 「只读」、无「写入数组…」",
                        netFlagsRow != null
                        && Descendants<TextBox>(netFlagsRow).All(t => !EffectivelyVisible(t))
                        && netFlagsTexts.Any(t => string.Equals(t, "只读", StringComparison.Ordinal))
                        && !netFlagsTexts.Any(t => t.Contains("写入数组", StringComparison.Ordinal)),
                        netFlagsRow == null
                            ? "row=null"
                            : $"text={string.Join("/", netFlagsTexts)} visibleBoxes={Descendants<TextBox>(netFlagsRow).Count(t => EffectivelyVisible(t))}");

                    var recipeChildNode = fixture.Vm.FilteredDisplayNodes
                        .First(n => n.Name == "[0]" && ReferenceEquals(n.OriginalModel, recipeNode.OriginalModel));
                    var recipeChildRow = RowOf(grid, recipeChildNode);
                    Check("网络数组元素行：只读文本、无编辑 TextBox",
                        recipeChildRow != null
                        && Descendants<TextBox>(recipeChildRow).All(t => !EffectivelyVisible(t))
                        && Descendants<TextBlock>(recipeChildRow).Any(t => string.Equals(t.Text, "10", StringComparison.Ordinal) && EffectivelyVisible(t)),
                        recipeChildRow == null
                            ? "row=null"
                            : $"text={string.Join("/", Descendants<TextBlock>(recipeChildRow).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)))} visibleBoxes={Descendants<TextBox>(recipeChildRow).Count(t => EffectivelyVisible(t))}");

                    // 系统来源：横幅、只读 chip、复制保留、重命名/删除隐藏
                    Populate(fixture.Vm, "system");
                    Pump(host);
                    Check("系统来源：只读横幅可见、管理分组可见、添加变量仍可见、导出/导入收起",
                        EffectivelyVisible(banner)
                        && EffectivelyVisible(manage)
                        && EffectivelyVisible(add)
                        && !EffectivelyVisible(export)
                        && !EffectivelyVisible(import),
                        $"banner={banner?.Visibility} manage={manage?.Visibility} add={add?.Visibility} " +
                        $"export={export?.Visibility} import={import?.Visibility}");

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
                        $"list={(sourceList.SelectedItem as VariableSourceNode)?.DisplayName ?? "<null>"} vm={netSource.DisplayName}");
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

                    // 分组行选中：作用域切到「连接 / 分组」，标签与搜索口径跟随
                    Populate(fixture.Vm, "network");
                    Pump(host);
                    var monitorNode = fixture.Vm.SourceNodes.FirstOrDefault(n => n.IsGroup && n.DisplayName == "监控分组");
                    if (monitorNode == null)
                    {
                        Check("左栏分组行存在（用于选中作用域断言）", false, "找不到「监控分组」行");
                    }
                    else
                    {
                        sourceList.SelectedItem = monitorNode;
                        Pump(host);
                        Check("左栏选中分组行 → VM 作用域切到该分组（SelectedGroupId / 分组标签）",
                            ReferenceEquals(fixture.Vm.SelectedNode, monitorNode)
                            && fixture.Vm.SelectedGroupId == monitorNode.GroupId
                            && fixture.Vm.IsGroupScope
                            && fixture.Vm.SelectedGroupName == "监控分组",
                            $"node={fixture.Vm.SelectedNode?.DisplayName} group={fixture.Vm.SelectedGroupId} label={fixture.Vm.SelectedGroupName}");
                    }

                    // 分组编辑器覆盖层：打开可见 + 分组行渲染；关闭收起
                    var groupOverlay = view.FindName("GroupEditorOverlay") as Grid;
                    fixture.Vm.OpenGroupManagerCommand.Execute();
                    Pump(host);
                    Check("分组编辑器覆盖层：打开可见 + 分组行渲染",
                        EffectivelyVisible(groupOverlay)
                        && Descendants<TextBlock>(groupOverlay).Any(t => t.Text == "管理分组")
                        && Descendants<TextBlock>(groupOverlay).Any(t => t.Text == "未分组"),
                        groupOverlay == null
                            ? "GroupEditorOverlay=null"
                            : $"visible={EffectivelyVisible(groupOverlay)} ungroupedRows={Descendants<TextBlock>(groupOverlay).Count(t => t.Text == "未分组")}");

                    fixture.Vm.CloseGroupManagerCommand.Execute();
                    Pump(host);
                    Check("分组编辑器覆盖层：关闭后收起", !EffectivelyVisible(groupOverlay),
                        $"visible={EffectivelyVisible(groupOverlay)}");

                    // ToolTip 显式样式：统计全部挂载点，不只看第一个按钮
                    var toolTipOwners = Descendants<FrameworkElement>(view)
                        .Where(e => e.ToolTip is ToolTip)
                        .ToList();
                    var styledTips = toolTipOwners.Count(e =>
                        ReferenceEquals(((ToolTip)e.ToolTip).Style, Application.Current.TryFindResource("DialogToolTipStyle")));
                    Check("ToolTip 全部显式使用 DialogToolTipStyle",
                        toolTipOwners.Count > 0 && styledTips == toolTipOwners.Count,
                        $"{styledTips}/{toolTipOwners.Count}");

                    // P3-1：网络数组的元素个数被导入改大后，树子行必须按 AddressConfig.Length 显示，
                    // 不能停在旧 Value/基准的长度（设备离线时永远少显示）。
                    var addressOk = GlobalVariableManagerViewModel.TryBuildAddress(
                        CommunicationType.ModbusTcp, ModbusArea.HoldingRegisters, "500", -1, typeof(int[]),
                        out var arrayAddress, out var addressError, elementCount: 5);
                    var arrayVar = new NetworkVariableModel
                    {
                        Name = "LengthChanged",
                        DataType = typeof(int[]),
                        ConnectionName = "PLC_Modbus",
                        AddressConfig = arrayAddress,
                        DefaultValue = new[] { 1, 2, 3 },
                        Value = new[] { 1, 2, 3 },
                    };
                    fixture.Workspace.GlobalVariables.Add(arrayVar);
                    fixture.Vm.OnDialogOpened(new DialogParameters());
                    // OnDialogOpened 会重建来源树；显式选回真实连接来源，确保明细过滤到该设备。
                    var realNet = fixture.Vm.SourceNodes.FirstOrDefault(s => s.IsNetwork && s.Key == "PLC_Modbus");
                    if (realNet != null) fixture.Vm.SelectedSource = realNet;
                    var arrayRoot = fixture.Vm.FilteredDisplayNodes
                        .FirstOrDefault(n => ReferenceEquals(n.OriginalModel, arrayVar));
                    Check("网络数组元素个数改大：子行按 AddressConfig.Length 显示 5 行",
                        addressOk && arrayRoot != null && arrayRoot.Children.Count == 5,
                        $"ok={addressOk} err={addressError} root={arrayRoot != null} children={arrayRoot?.Children.Count}");

                    // 反向：5→3 时旧 Value/基准仍长 5，树必须按配置长度缩到 3，摘要也不能还列 5 项。
                    var shrinkOk = GlobalVariableManagerViewModel.TryBuildAddress(
                        CommunicationType.ModbusTcp, ModbusArea.HoldingRegisters, "600", -1, typeof(int[]),
                        out var shrinkAddress, out var shrinkError, elementCount: 3);
                    var shrinkVar = new NetworkVariableModel
                    {
                        Name = "LengthShrink",
                        DataType = typeof(int[]),
                        ConnectionName = "PLC_Modbus",
                        AddressConfig = shrinkAddress,
                        DefaultValue = new[] { 1, 2, 3, 4, 5 },
                        Value = new[] { 1, 2, 3, 4, 5 },
                    };
                    fixture.Workspace.GlobalVariables.Add(shrinkVar);
                    fixture.Vm.OnDialogOpened(new DialogParameters());
                    var realNetShrink = fixture.Vm.SourceNodes.FirstOrDefault(s => s.IsNetwork && s.Key == "PLC_Modbus");
                    if (realNetShrink != null) fixture.Vm.SelectedSource = realNetShrink;
                    var shrinkRoot = fixture.Vm.FilteredDisplayNodes
                        .FirstOrDefault(n => ReferenceEquals(n.OriginalModel, shrinkVar));
                    Check("网络数组元素个数改小：子行 3 行 + 摘要/工具提示按 3 项",
                        shrinkOk && shrinkRoot != null
                        && shrinkRoot.Children.Count == 3
                        && shrinkRoot.ArraySummaryTooltip == "1 · 2 · 3",
                        $"ok={shrinkOk} err={shrinkError} root={shrinkRoot != null} " +
                        $"children={shrinkRoot?.Children.Count} tooltip={shrinkRoot?.ArraySummaryTooltip}");
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
                    foreach (var mode in new[] { "local", "network", "network-offline", "system", "empty", "search", "write-error", "group-editor" })
                    {
                        Populate(fixture.Vm, mode == "group-editor" ? "network" : mode);
                        if (mode == "group-editor")
                            fixture.Vm.OpenGroupManagerCommand.Execute();
                        Pump(host);
                        if (mode == "write-error")
                            MarkWriteError(view);
                        Pump(host);
                        var path = Path.Combine(outDir, "after-render-v1-" + mode + ".png");
                        RenderElement(host, path);
                        Check("渲染 " + mode, File.Exists(path), path);
                        if (mode == "group-editor")
                            fixture.Vm.CloseGroupManagerCommand.Execute();
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

                    // 批量导入/导出两个弹窗（独立窗口视图）：导出范围卡 + 导入差异表。
                    RenderIoDialogs(fixture, outDir, Check);

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
                "SelectedItem=\"{Binding SelectedNode, Mode=TwoWay}\"",
                "Text=\"{Binding SearchText, UpdateSourceTrigger=PropertyChanged}\"",
                "Command=\"{Binding AddCommand}\"",
                "Command=\"{Binding OpenGroupManagerCommand}\"",
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
                "ItemsSource=\"{Binding GroupOptions}\"",
                "SelectedItem=\"{Binding SelectedGroup",
                "{Binding ShowGroup",
                "GroupEditor.IsOpen",
                "GroupEditor.Rows",
                "CurrentValueText",
                "IsValueDeviated",
                "IsValueReadOnly",
                "IsSystemOwned",
                "IsEditableNode",
                "ArraySummary",
                "IsElementDeviated",
                "Command=\"{Binding DataContext.ResetElementCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}\"",
                "x:Name=\"AddButton\"",
                "x:Name=\"ManageVariableGroupsButton\"",
                "x:Name=\"ExportButton\"",
                "x:Name=\"ImportButton\"",
                "Command=\"{Binding ExportCommand}\"",
                "Command=\"{Binding ImportCommand}\"",
                "Visibility=\"{Binding IsNetworkSource, Converter={ui:BooleanToVisibilityConverter}}\"",
                "x:Name=\"SystemBanner\"",
                "x:Name=\"EmptySourceOverlay\"",
                "x:Name=\"SearchEmptyOverlay\"",
                "x:Name=\"GroupColumn\"",
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

            // ================================================================
            //  批量导入 / 导出两个独立弹窗的静态闸
            // ================================================================
            var exportPath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\DeviceVariableExportView.xaml");
            var importPath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\DeviceVariableImportView.xaml");
            var exportXaml = File.Exists(exportPath) ? File.ReadAllText(exportPath) : string.Empty;
            var importXaml = File.Exists(importPath) ? File.ReadAllText(importPath) : string.Empty;
            check("导入/导出弹窗 XAML 可读", exportXaml.Length > 0 && importXaml.Length > 0,
                $"{exportPath} | {importPath}");

            // 注释里的字符串不能算数：先剥注释再做 Contains / regex 判据。
            var exportXamlNoComments = Regex.Replace(exportXaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            var importXamlNoComments = Regex.Replace(importXaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            string[] exportRequired =
            {
                "Command=\"{Binding ExportCommand}\"",
                "Command=\"{Binding BrowseCommand}\"",
                "Command=\"{Binding CancelCommand}\"",
                "IsChecked=\"{Binding IsAllScope, Mode=TwoWay}\"",
                "IsChecked=\"{Binding IsCurrentGroupScope, Mode=TwoWay}\"",
                "Text=\"{Binding Subtitle}\"",
                "Text=\"{Binding ExportPath}\"",
                "Text=\"{Binding Summary}\"",
            };
            var exportMissing = exportRequired.Where(r => !exportXamlNoComments.Contains(r, StringComparison.Ordinal)).ToList();
            check("导出弹窗关键绑定齐全", exportMissing.Count == 0, string.Join(" | ", exportMissing));

            string[] importRequired =
            {
                "ItemsSource=\"{Binding Items}\"",
                "Command=\"{Binding ApplyCommand}\"",
                "Command=\"{Binding ReselectFileCommand}\"",
                "Command=\"{Binding DownloadTemplateCommand}\"",
                "IsChecked=\"{Binding SkipErrors, Mode=TwoWay}\"",
                "IsChecked=\"{Binding DeleteMissing, Mode=TwoWay}\"",
                "IsChecked=\"{Binding AutoCreateDisplayGroups, Mode=TwoWay}\"",
                "Text=\"{Binding ApplyHint}\"",
                "Text=\"{Binding ResultSummary}\"",
                "Text=\"{Binding GlobalError}\"",
                "services:DeviceVariableImportStatus.New",
                "services:DeviceVariableImportStatus.Update",
                "services:DeviceVariableImportStatus.Error",
            };
            var importMissing = importRequired.Where(r => !importXamlNoComments.Contains(r, StringComparison.Ordinal)).ToList();
            check("导入弹窗关键绑定齐全", importMissing.Count == 0, string.Join(" | ", importMissing));

            // DataGrid.RowHeight 是 double：XAML 里写 "Auto" 会在**视图构造期**抛
            // "Auto is not a valid value for Double"（编译期全绿）。自动行高只能写 NaN。
            check("导入弹窗 RowHeight 用 NaN（Auto 不是合法 double）",
                !importXamlNoComments.Contains("RowHeight=\"Auto\"", StringComparison.Ordinal)
                && importXamlNoComments.Contains("RowHeight=\"{x:Static sys:Double.NaN}\"", StringComparison.Ordinal),
                "");

            // Run.Text 的默认绑定模式是 TwoWay：绑到只读计数属性会在构造期抛
            // "无法对只读属性使用 TwoWay 绑定"。**所有**计数 Run 都必须显式 Mode=OneWay
            // （旧判据只抓"没写 Mode"的形态，写 Mode=TwoWay 会绕过）。
            var countRunTags = Regex.Matches(
                importXamlNoComments,
                @"<Run\b[^>]*Text=""\{Binding\s+(NewCount|UpdateCount|UnchangedCount|ErrorCount|MissingCount)[^}]*\}""");
            var badCountRun = countRunTags.Cast<Match>()
                .Any(m => !m.Value.Contains("Mode=OneWay", StringComparison.Ordinal));
            check("导入弹窗计数 Run 全部显式 OneWay",
                countRunTags.Count >= 5 && !badCountRun,
                $"runs={countRunTags.Count} bad={badCountRun}");

            var appPath = Path.Combine(RepoRoot, @"VisionMaster\App.xaml.cs");
            var app = File.Exists(appPath) ? File.ReadAllText(appPath) : string.Empty;
            var appNoComments = Regex.Replace(app, @"//.*?$|/\*.*?\*/", string.Empty,
                RegexOptions.Multiline | RegexOptions.Singleline);
            check("App 注册导入/导出弹窗（命令打开的名字必须已注册）",
                appNoComments.Contains("DeviceVariableExportViewModel.DialogName", StringComparison.Ordinal)
                && appNoComments.Contains("DeviceVariableImportViewModel.DialogName", StringComparison.Ordinal),
                appPath);
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
            var net = new VariableSourceNode { Key = "PLC_Modbus", DisplayName = "PLC_Modbus", Icon = "\uf1eb", IsNetwork = true, ProtocolText = "Modbus TCP", Count = 5, IsOnline = true, IsExpanded = true };
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
                    AddGroupRows(vm, net);
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
                    AddGroupRows(vm, net);
                    AddNetworkRows(vm, "PLC_Modbus", true);
                    break;
            }

            // 夹具直接改写 FilteredDisplayNodes（绕过 DisplayNodes 过滤），
            // 徽标要显式同步一次，否则渲染出"0 个变量"。
            vm.RefreshVisibleRootCount();
        }

        /// <summary>在连接行下面插入「全部 / 监控分组 / 未分组」三个分组行（只用于视图渲染/模板选择器）。</summary>
        private static void AddGroupRows(GlobalVariableManagerViewModel vm, VariableSourceNode parent)
        {
            int index = vm.SourceNodes.IndexOf(parent);
            if (index < 0) return;

            var rows = new[]
            {
                new VariableSourceNode
                {
                    Key = parent.Key + "|all", DisplayName = "全部", Icon = "\uf0ca", IsNetwork = true,
                    Level = 1, ParentKey = parent.Key, GroupId = null, Count = 5, IsOnline = parent.IsOnline
                },
                new VariableSourceNode
                {
                    Key = parent.Key + "|monitor", DisplayName = "监控分组", Icon = "\uf02b", IsNetwork = true,
                    Level = 1, ParentKey = parent.Key, GroupId = MonitorGroupId, Count = 3, IsOnline = parent.IsOnline
                },
                new VariableSourceNode
                {
                    Key = parent.Key + "|ungrouped", DisplayName = "未分组", Icon = "\uf01c", IsNetwork = true,
                    Level = 1, ParentKey = parent.Key, GroupId = Guid.Empty, Count = 2, IsOnline = parent.IsOnline
                },
            };

            for (int i = 0; i < rows.Length; i++)
                vm.SourceNodes.Insert(index + 1 + i, rows[i]);
        }

        private static void AddLocalRows(GlobalVariableManagerViewModel vm)
        {
            AddRoot(vm, NewVar("OKCount", typeof(int), 0, 128, "本班 OK 计数"), false, true, null, null);
            AddRoot(vm, NewVar("NGCount", typeof(int), 0, 3, "本班 NG 计数"), false, true, null, null);
            AddRoot(vm, NewVar("YieldRate", typeof(double), 0d, 0.976, "实时良率（OK / 总数）"), false, true, null, null);
            AddRoot(vm, NewVar("LastResult", typeof(string), "", "OK", "最近一次判定结果"), false, true, null, null);

            // 数组根必须用真实 LocalVariableModel：视图的数组根分支会检查
            // OriginalModel is LocalVariableModel（防止系统/网络数组出现编辑入口）。
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

            // 网络数组：第一期只读。用真实 NetworkVariableModel（视图的 NetworkArrayRoot/
            // ReadOnlyChild 分支按模型类型判定，FakeVariable 走不到这两支）。
            var recipe = new NetworkVariableModel
            {
                Name = "RecipeBlock",
                DataType = typeof(int[]),
                ConnectionName = connection,
                Description = "配方块（3 个 Int32，只读）",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 3
                },
                Value = new[] { 10, 20, 30 }
            };
            var recipeRoot = Root(recipe, true, online, "x=3;0", "默认组");
            recipeRoot.IsExpanded = true;
            vm.FilteredDisplayNodes.Add(recipeRoot);
            for (int i = 0; i < 3; i++)
                vm.FilteredDisplayNodes.Add(Child(recipeRoot, i, ((int[])recipe.Value!)[i]));

            // 只读区（离散输入）里的网络数组：根行必须是「只读」徽标，不能出现「写入数组…」。
            var netFlags = new NetworkVariableModel
            {
                Name = "NetFlags",
                DataType = typeof(bool[]),
                ConnectionName = connection,
                Description = "来料标记组（3 点，只读）",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.DiscreteInputs,
                    DataType = DataValueType.Boolean,
                    Offset = "0",
                    Length = 3
                },
                Value = new[] { true, false, true }
            };
            var netFlagsRoot = Root(netFlags, true, online, "x=2;0", "默认组");
            netFlagsRoot.IsExpanded = true;
            vm.FilteredDisplayNodes.Add(netFlagsRoot);
            for (int i = 0; i < 3; i++)
                vm.FilteredDisplayNodes.Add(Child(netFlagsRoot, i, ((bool[])netFlags.Value!)[i]));
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

                // 显示分组：冒烟夹具不建方案分组表，只给内置「未分组」，
                // 保证分组列渲染出可选项而不是空下拉。
                node.GroupOptions.Add(new VariableGroupOption { Id = Guid.Empty, Name = "未分组" });
                node.SelectedGroup = node.GroupOptions[0];
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

        /// <summary>
        /// 批量导入/导出两个独立弹窗的渲染与数据检查。
        /// <para>为什么放在变量管理渲染里：两者共用同一套 Dialog 皮肤与同一个设备来源，
        /// 但视图是独立 UserControl，不能靠 GlobalVariableView 的渲染档覆盖。</para>
        /// </summary>
        private static void RenderIoDialogs(
            Fixture fixture, string outDir, Action<string, bool, string> check)
        {
            var groups = fixture.Workspace.CurrentSolution.VariableGroups.ToList();
            var nv1 = CreateNetworkVariable("SpeedSet", "100", MonitorGroupId, "目标速度", typeof(int), 0);
            var nv2 = CreateNetworkVariable("RecipeBlock", "200", MonitorGroupId, "配方块", typeof(int), 10);
            var nv3 = CreateNetworkVariable("PartReady", "300", Guid.Empty, "工件就绪", typeof(bool), false);
            foreach (var nv in new[] { nv1, nv2, nv3 })
                fixture.Workspace.GlobalVariables.Add(nv);

            // 导出弹窗：全部 3 / 当前分组 2
            var exportVm = new DeviceVariableExportViewModel(fixture.Workspace, fixture.Manager);
            exportVm.OnDialogOpened(new DialogParameters
            {
                { DeviceVariableExportViewModel.ConnectionNameKey, "PLC_Modbus" },
                { DeviceVariableExportViewModel.CurrentGroupIdKey, MonitorGroupId },
                { DeviceVariableExportViewModel.HasCurrentGroupKey, true },
                { DeviceVariableExportViewModel.CurrentGroupNameKey, "监控分组" },
            });
            check("导出弹窗：全部 3 / 当前分组 2",
                exportVm.AllCount == 3 && exportVm.CurrentGroupCount == 2,
                $"all={exportVm.AllCount} group={exportVm.CurrentGroupCount}");

            var exportView = new DeviceVariableExportView { DataContext = exportVm };
            var exportHost = CreateHost(exportView, 620, 560);

            // R160：两张范围卡必须等宽（自定义 RadioButton 内容默认按内容宽收缩）。
            var scopeRadios = Descendants<RadioButton>(exportView)
                .Where(r => string.Equals(r.GroupName, "ExportScope", StringComparison.Ordinal))
                .ToList();
            check("导出弹窗：两张范围卡等宽（Stretch 生效）",
                scopeRadios.Count == 2
                && scopeRadios[0].ActualWidth > 200
                && Math.Abs(scopeRadios[0].ActualWidth - scopeRadios[1].ActualWidth) < 1,
                string.Join(" / ", scopeRadios.Select(r => r.ActualWidth.ToString("0.#"))));

            try
            {
                var exportPath = Path.Combine(outDir, "after-render-v1-export.png");
                RenderElement(exportHost, exportPath);
                check("渲染 export 弹窗", File.Exists(exportPath), exportPath);
            }
            finally
            {
                exportHost.Child = null;
            }

            // 导入弹窗：造一份 新增 / 更新 / 无变化 / 错误 都有的文件
            var rows = new List<Dictionary<string, object?>>
            {
                DeviceVariableExportService.BuildRow(nv1, groups),
                DeviceVariableExportService.BuildRow(nv2, groups),
                DeviceVariableExportService.BuildRow(nv3, groups),
            };
            rows[1][DeviceVariableColumns.Description] = "配方块（改）";

            var newRow = new Dictionary<string, object?>(rows[0], StringComparer.Ordinal);
            newRow[DeviceVariableColumns.Name] = "NewTemp";
            newRow[DeviceVariableColumns.DataType] = "小数 (double)";
            newRow[DeviceVariableColumns.Offset] = "400";
            newRow[DeviceVariableColumns.DisplayGroup] = "控制分组";
            newRow[DeviceVariableColumns.ScanGroup] = "不存在的组";
            rows.Add(newRow);

            var errorRow = new Dictionary<string, object?>(rows[0], StringComparer.Ordinal);
            errorRow[DeviceVariableColumns.Name] = "BadTemp";
            errorRow[DeviceVariableColumns.DataType] = "不存在的类型";
            rows.Add(errorRow);

            var file = Path.Combine(Path.GetTempPath(), "vm_io_render_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                MiniExcel.SaveAs(file, new Dictionary<string, object> { ["变量表"] = rows });

                var importVm = new DeviceVariableImportViewModel(fixture.Workspace, fixture.Manager, fixture.Bridge);
                importVm.OnDialogOpened(new DialogParameters
                {
                    { DeviceVariableImportViewModel.FilePathKey, file },
                    { DeviceVariableImportViewModel.ConnectionNameKey, "PLC_Modbus" },
                });
                check("导入弹窗：新增 1 / 更新 1 / 无变化 2 / 错误 1",
                    importVm.NewCount == 1 && importVm.UpdateCount == 1
                    && importVm.UnchangedCount == 2 && importVm.ErrorCount == 1,
                    $"{importVm.NewCount}/{importVm.UpdateCount}/{importVm.UnchangedCount}/{importVm.ErrorCount}");

                var importView = new DeviceVariableImportView { DataContext = importVm };
                var importHost = CreateHost(importView, 1060, 800);
                try
                {
                    var skipBox = Descendants<CheckBox>(importView)
                        .FirstOrDefault(c => (c.Content as string)?.StartsWith("跳过错误行", StringComparison.Ordinal) == true);
                    check("导入弹窗：错误行可勾选跳过、应用按钮在未跳过时禁用",
                        skipBox != null && skipBox.IsEnabled && !importVm.CanApply,
                        $"skipEnabled={skipBox?.IsEnabled} canApply={importVm.CanApply}");

                    var importPath = Path.Combine(outDir, "after-render-v1-import.png");
                    RenderElement(importHost, importPath);
                    check("渲染 import 弹窗", File.Exists(importPath), importPath);
                }
                finally
                {
                    importHost.Child = null;
                }
            }
            finally
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        private static NetworkVariableModel CreateNetworkVariable(
            string name, string offset, Guid groupId, string description, Type type, object defaultValue)
        {
            GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.ModbusTcp,
                type == typeof(bool) ? ModbusArea.Coils : ModbusArea.HoldingRegisters,
                offset, -1, type, out var address, out _);

            return new NetworkVariableModel
            {
                Name = name,
                DataType = type,
                Description = description,
                ConnectionName = "PLC_Modbus",
                AddressConfig = address,
                ScanGroup = string.Empty,
                GroupId = groupId,
                DefaultValue = defaultValue,
                Value = defaultValue,
            };
        }

        private static Border CreateHost(FrameworkElement view, double width, double height)
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
                // 变量管理/分组编辑器都读 CurrentSolution.VariableGroups；
                // 不挂方案时 CurrentSolution 为 null（真实启动早期也是这个状态），
                // 这里先挂一份空方案，让分组表有一个真实落点。
                Workspace.SwitchSolution(new SolutionModel());
                Workspace.CurrentSolution.VariableGroups.Add(
                    new VariableGroupDescriptor { Id = MonitorGroupId, Name = "监控分组" });
                Workspace.GlobalVariables.Clear();
                Manager = new AdvancedCommunicationManager();
                // 导入/导出入口按连接过滤变量：夹具没有连接时 SelectedConnection 恒 null，
                // 两个命令永远不可执行（按钮可见但点了没反应）。这里注册一条 Modbus 连接。
                Manager.AddConnection(new CommunicationConfig
                {
                    ConnectionName = "PLC_Modbus",
                    Protocol = CommunicationType.ModbusTcp,
                    ReadCycleMs = 1000,
                });
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
