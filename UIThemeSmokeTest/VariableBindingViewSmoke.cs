using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using HalconDotNet;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 变量绑定弹窗冒烟 / 渲染（--variable-binding / --variable-binding-render）。
    ///
    /// 用**真实** VariableBindingViewModel + 真实视图：视图的 code-behind 只在真实 VM 下
    /// 才会挂上 CollectionView 过滤/分组；桩 VM 会让三栏为空、空态恒显示，断言会假绿。
    /// 目标步骤用插件输入端口表；来源只用「全局变量 (Global)」节点（不构造上游步骤，
    /// 避免 FlowQueryHelper 依赖 Prism 容器解析 IPluginProvider）。
    /// </summary>
    internal static class VariableBindingViewSmoke
    {
        private static readonly string RepoRoot = ResolveRepoRoot();
        private const string PluginType = "VM.VariableBindingSmoke.Plugin";

        private static string ResolveRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
            }
            return @"d:\C#\VM";
        }

        [Flags]
        private enum SmokeEnum
        {
            [Display(Name = "圆")]
            Circle = 1,
            [Display(Name = "直线")]
            Line = 2,
            [Display(Name = "椭圆")]
            Ellipse = 4,
        }

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
                RunConditionStepContract(Check);

                using var fixture = new Fixture();
                var view = new VariableBindingView { DataContext = fixture.Vm };
                var host = CreateHost(view, 1180, 760);
                try
                {
                    var targetList = view.FindName("TargetList") as ListBox;
                    var sourceList = view.FindName("SourceList") as ListBox;
                    var candidateList = view.FindName("CandidateList") as ListBox;
                    var targetFilter = view.FindName("TargetFilterList") as ListBox;
                    var sourceTabs = view.FindName("SourceTabList") as ListBox;
                    var targetEmpty = view.FindName("TargetEmptyOverlay") as Border;
                    var sourceEmpty = view.FindName("SourceEmptyOverlay") as Border;
                    var candidateEmpty = view.FindName("CandidateEmptyOverlay") as Border;

                    Check("视图真实实例化 + DataContext 是真实 VM",
                        view.DataContext is VariableBindingViewModel && Descendants<FrameworkElement>(view).Count() > 120,
                        $"elements={Descendants<FrameworkElement>(view).Count()}");

                    Check("三栏列表都拿到数据（输入端口 / 数据来源 / 候选端口）",
                        targetList?.Items.Count == 7
                        && sourceList?.Items.Count == 1
                        && candidateList?.Items.Count == 1,
                        $"target={targetList?.Items.Count} source={sourceList?.Items.Count} candidate={candidateList?.Items.Count}");

                    var sourceView = CollectionViewSource.GetDefaultView(fixture.Vm.TreeNodes);
                    Check("中栏按 SourceCategory 分组（1 个 GroupDescription、1 组）",
                        sourceView.GroupDescriptions.Count == 1
                        && sourceView.Groups != null
                        && sourceView.Groups.Count == 1,
                        $"groups={sourceView.Groups?.Count}");

                    // 目标筛选：bound 只剩已绑定（默认只有「图像」绑了 ImageVar）
                    targetFilter.SelectedIndex = 2; // 已绑定
                    Pump(host);
                    Check("目标筛选「已绑定」只剩 1 条",
                        targetList.Items.Count == 1,
                        $"items={targetList.Items.Count}");
                    targetFilter.SelectedIndex = 0;
                    Pump(host);

                    // 来源页签：上游算子应为空（本夹具没有上游步骤）
                    sourceTabs.SelectedIndex = 2;
                    Pump(host);
                    Check("来源页签「上游算子」为空且空态可见",
                        sourceList.Items.Count == 0 && sourceEmpty?.Visibility == Visibility.Visible,
                        $"items={sourceList.Items.Count} empty={sourceEmpty?.Visibility}");
                    sourceTabs.SelectedIndex = 0;
                    // 页签切到空的上游算子时 ListBox 会把 SelectedNode 清空，切回后要重新选来源。
                    fixture.Vm.SelectedNode = fixture.Vm.TreeNodes[0];
                    Pump(host);

                    // 默认目标「图像」→ 全局变量里只有 ImageVar 兼容
                    Check("默认 HImage 目标：候选只剩 ImageVar，被类型隐藏 6 个",
                        fixture.Vm.DisplayPorts.Count == 1
                        && fixture.Vm.DisplayPorts[0].Name == "ImageVar"
                        && fixture.Vm.HiddenByTypeCount == 6,
                        $"candidates={string.Join(",", fixture.Vm.DisplayPorts.Select(p => p.Name))} hidden={fixture.Vm.HiddenByTypeCount}");

                    var showAll = Descendants<CheckBox>(view)
                        .FirstOrDefault(c => (c.Content as string) == "显示全部");
                    showAll.IsChecked = true;
                    Pump(host);
                    Check("「显示全部」CheckBox → VM + 候选 7 条（6 条不可绑定）",
                        showAll.IsChecked == true
                        && fixture.Vm.ShowAllPorts
                        && candidateList.Items.Count == 7
                        && fixture.Vm.DisplayPorts.Count(p => !p.IsBindable) == 6,
                        $"checked={showAll.IsChecked} vm={fixture.Vm.ShowAllPorts} items={candidateList.Items.Count} disabled={fixture.Vm.DisplayPorts.Count(p => !p.IsBindable)}");
                    showAll.IsChecked = false;
                    Pump(host);

                    // 切 Double 目标：候选包含 ScoreVar 与 ScoresVar(Double[])
                    var scorePort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "最小分数");
                    fixture.Vm.SelectedInputPort = scorePort;
                    Pump(host);
                    Check("切 Double 目标：候选含 ScoreVar / ScoresVar",
                        fixture.Vm.DisplayPorts.Any(p => p.Name == "ScoreVar")
                        && fixture.Vm.DisplayPorts.Any(p => p.Name == "ScoresVar"),
                        string.Join(",", fixture.Vm.DisplayPorts.Select(p => p.Name)));

                    // 候选行「绑定」按钮：Command/CommandParameter 必须指向候选 PortDefinition
                    candidateList.UpdateLayout();
                    Pump(host);
                    var scoreButton = Descendants<Button>(candidateList)
                        .FirstOrDefault(b => b.DataContext is PortDefinition p
                            && p.Name == "ScoreVar"
                            && (b.Content as string) == "绑定");
                    if (scoreButton?.Command != null)
                        scoreButton.Command.Execute(scoreButton.CommandParameter);
                    Pump(host);
                    Check("候选行「绑定」按钮 → 写入 LinkedSources",
                        scoreButton?.Command != null
                        && fixture.Step.LinkedSources.ContainsKey("最小分数"),
                        scoreButton == null ? "button=null" : $"command={scoreButton.Command != null} hasLink={fixture.Step.LinkedSources.ContainsKey("最小分数")}");

                    // 「清除绑定」按钮：Command/CommandParameter 必须指向当前输入端口
                    var clearButton = Descendants<Button>(view)
                        .FirstOrDefault(b => (b.Content as string) == "清除绑定");
                    if (clearButton?.Command != null)
                        clearButton.Command.Execute(clearButton.CommandParameter);
                    Pump(host);
                    Check("「清除绑定」按钮 → 清空当前端口连线",
                        clearButton?.Command != null
                        && !fixture.Step.LinkedSources.ContainsKey("最小分数"),
                        clearButton == null ? "button=null" : $"command={clearButton.Command != null} hasLink={fixture.Step.LinkedSources.ContainsKey("最小分数")}");

                    // 数组 → 标量：行内索引面板 + 确认写 [index]
                    var scores = fixture.Vm.DisplayPorts.First(p => p.Name == "ScoresVar");
                    fixture.Vm.DoublockClickBindCommand.Execute(scores);
                    Pump(host);
                    Check("数组端口：展开行内索引面板（HasPendingArrayBind=true）",
                        fixture.Vm.HasPendingArrayBind && fixture.Vm.ArrayIndexText == "0",
                        $"pending={fixture.Vm.HasPendingArrayBind} index=[{fixture.Vm.ArrayIndexText}]");

                    fixture.Vm.SelectedOutputPort = fixture.Vm.DisplayPorts.First(p => p.Name == "IntVar");
                    Check("换候选自动收起数组索引面板（不会绑到旧端口）",
                        !fixture.Vm.HasPendingArrayBind,
                        $"pending={fixture.Vm.HasPendingArrayBind}");

                    // 重新打开同一个数组端口，继续验证确认路径
                    fixture.Vm.DoublockClickBindCommand.Execute(scores);
                    fixture.Vm.ArrayIndexText = "1";
                    fixture.Vm.ConfirmArrayBindCommand.Execute();
                    Pump(host);
                    Check("数组端口：确认后下标写进 TargetPortName",
                        fixture.Step.LinkedSources.TryGetValue("最小分数", out var arrayLink)
                        && arrayLink.TargetPortName == "ScoresVar[1]"
                        && !fixture.Vm.HasPendingArrayBind,
                        arrayLink == null ? "link=null" : $"port={arrayLink.TargetPortName}");

                    // 清除绑定：走 UnbindCommand，连线与旧手填值都必须消失
                    fixture.Step.SetInputValue("最小分数", "0.75");
                    fixture.Vm.UnbindCommand.Execute(scorePort);
                    Pump(host);
                    Check("清除绑定：LinkedSources / InputValues 键都被移除、LinkedAddress 清空",
                        !fixture.Step.LinkedSources.ContainsKey("最小分数")
                        && !fixture.Step.InputValues.ContainsKey("最小分数")
                        && string.IsNullOrEmpty(scorePort.LinkedAddress),
                        $"hasLink={fixture.Step.LinkedSources.ContainsKey("最小分数")} hasInput={fixture.Step.InputValues.ContainsKey("最小分数")} address=[{scorePort.LinkedAddress}]");

                    // 数组 → object：object 是整值语义，必须整数组绑定，不能强制取下标
                    var objectPort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "值");
                    fixture.Vm.SelectedInputPort = objectPort;
                    var scoresForObject = fixture.Vm.DisplayPorts.First(p => p.Name == "ScoresVar");
                    fixture.Vm.DoublockClickBindCommand.Execute(scoresForObject);
                    Pump(host);
                    fixture.Step.LinkedSources.TryGetValue("值", out var objectLink);
                    Check("数组→object：整数组绑定，不展开索引面板",
                        !fixture.Vm.HasPendingArrayBind
                        && objectLink != null
                        && objectLink.TargetPortName == "ScoresVar",
                        objectLink == null ? "link=null" : $"port={objectLink.TargetPortName} pending={fixture.Vm.HasPendingArrayBind}");

                    // 枚举目标：功能预设卡片 + 中文显示名
                    var enumPort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "查找类型");
                    fixture.Vm.SelectedInputPort = enumPort;
                    Pump(host);
                    var presetRadios = Descendants<RadioButton>(view)
                        .Where(r => r.DataContext is VariableBindingViewModel.PresetOptionItem)
                        .ToList();
                    Check("枚举目标：HasFunctionalEnumPort + 3 张预设卡片",
                        fixture.Vm.HasFunctionalEnumPort
                        && fixture.Vm.PresetOptions.Count == 3
                        && presetRadios.Count == 3,
                        $"hasEnum={fixture.Vm.HasFunctionalEnumPort} presets={fixture.Vm.PresetOptions.Count} radios={presetRadios.Count}");

                    var lineRadio = presetRadios.FirstOrDefault(r =>
                        r.DataContext is VariableBindingViewModel.PresetOptionItem p && p.Option == "Line");
                    if (lineRadio?.Command != null)
                        lineRadio.Command.Execute(lineRadio.CommandParameter);
                    Pump(host);
                    Check("预设卡片命令 → 写入成员名、显示中文名",
                        lineRadio?.Command != null
                        && fixture.Vm.ConstantValue == "Line"
                        && fixture.Vm.SelectedPresetDisplay == "直线",
                        lineRadio == null
                            ? "radio=null"
                            : $"command={lineRadio.Command != null} value={fixture.Vm.ConstantValue} display={fixture.Vm.SelectedPresetDisplay}");

                    // 单绑定模式：无 TargetStep 时也必须先建 mock 目标端口（插件视图发起路径）
                    using var noTargetFixture = new Fixture(singleBind: true, noTarget: true);
                    Check("单绑定无 TargetStep：仍建 mock 目标端口（不因 _targetStep=null 提前 return）",
                        noTargetFixture.Vm.DisplayDataPort.Count == 1
                        && noTargetFixture.Vm.SelectedInputPort != null,
                        $"ports={noTargetFixture.Vm.DisplayDataPort.Count} selected={(noTargetFixture.Vm.SelectedInputPort != null)}");

                    // 单绑定模式：搜索/筛选收起、主按钮文案「绑定并返回」
                    using var singleFixture = new Fixture(singleBind: true);
                    var singleView = new VariableBindingView { DataContext = singleFixture.Vm };
                    var singleHost = CreateHost(singleView, 1180, 760);
                    try
                    {
                        var search = singleView.FindName("TargetSearchBox") as TextBox;
                        var searchChrome = FindAncestor<Border>(search);
                        var confirm = Descendants<Button>(singleView)
                            .FirstOrDefault(b => b.Command == singleFixture.Vm.ConfirmCommand);
                        Check("单绑定：左栏搜索/筛选收起、主按钮「绑定并返回」",
                            searchChrome?.Visibility == Visibility.Collapsed
                            && confirm != null
                            && (confirm.Content as TextBlock)?.Text == "绑定并返回",
                            $"search={searchChrome?.Visibility} confirm={(confirm?.Content as TextBlock)?.Text}");

                        LinkReference bound = null;
                        singleFixture.Vm.RequestClose = MakeDialogCloseListener(result =>
                        {
                            if (result.Parameters.TryGetValue<LinkReference>("BoundLink", out var link))
                                bound = link;
                        });
                        var firstCandidate = singleFixture.Vm.DisplayPorts.FirstOrDefault();
                        if (firstCandidate != null)
                            singleFixture.Vm.DoublockClickBindCommand.Execute(firstCandidate);
                        Pump(singleHost);
                        Check("单绑定：选定候选后自动回传 BoundLink（不写活模型）",
                            bound != null,
                            bound == null ? "BoundLink=null" : $"kind={bound.Kind} port={bound.TargetPortName}");
                    }
                    finally
                    {
                        singleHost.Child = null;
                    }

                    // 空态：无输入端口 + 无全局变量
                    using var emptyFixture = new Fixture(empty: true);
                    var emptyView = new VariableBindingView { DataContext = emptyFixture.Vm };
                    var emptyHost = CreateHost(emptyView, 1180, 760);
                    try
                    {
                        var emptyTarget = emptyView.FindName("TargetEmptyOverlay") as Border;
                        var emptySource = emptyView.FindName("SourceEmptyOverlay") as Border;
                        Check("空态：输入端口/数据来源两个覆盖层都可见",
                            emptyTarget?.Visibility == Visibility.Visible
                            && emptySource?.Visibility == Visibility.Visible,
                            $"target={emptyTarget?.Visibility} source={emptySource?.Visibility}");
                    }
                    finally
                    {
                        emptyHost.Child = null;
                    }

                    // 视图静态纪律
                    Check("ToolTip 显式使用 DialogToolTipStyle",
                        Descendants<Button>(view).Any(b => b.ToolTip is ToolTip tip
                            && ReferenceEquals(tip.Style, Application.Current.TryFindResource("DialogToolTipStyle"))),
                        "");
                }
                finally
                {
                    host.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 变量绑定视图冒烟抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 变量绑定视图冒烟：全部通过 ==="
                : $"### 变量绑定视图冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// 条件节点（ConditionStep）的键口径：LocalVariables 铺端口、Description=Guid，
        /// 绑定/回读/解绑都必须用 Guid，不能落到 BuiltIn_If 的「虚拟变量/中间变量」插件端口上。
        /// </summary>
        private static void RunConditionStepContract(Action<string, bool, string> check)
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(Fake("FlagVar", typeof(bool), "布尔变量"));

            var solution = new SolutionModel { SolutionName = "条件绑定冒烟" };
            var flow = new FlowModel { FlowName = "条件绑定冒烟流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            var condition = new ConditionStep("", "条件", "BuiltIn_If", "条件_0");
            var flagId = Guid.NewGuid();
            condition.LocalVariables.Add(new LocalVariableItem
            {
                Id = flagId,
                Name = "Flag",
                DataTypeName = typeof(bool).AssemblyQualifiedName,
            });
            flow.Steps.Add(condition);

            var vm = new VariableBindingViewModel(workspace, new StubPluginProvider());
            vm.OnDialogOpened(new DialogParameters
            {
                { "IsSingleBindMode", false },
                { "TargetStep", condition },
            });

            check("条件节点：按 LocalVariables 列出端口（Description=变量 Guid）",
                vm.DisplayDataPort.Count == 1
                && vm.DisplayDataPort[0].Definition.Name == "Flag"
                && vm.DisplayDataPort[0].Definition.Description == flagId.ToString(),
                string.Join(" | ", vm.DisplayDataPort.Select(p => $"{p.Definition.Name}:{p.Definition.Description}")));

            vm.SelectedNode = vm.TreeNodes[0];
            vm.DoublockClickBindCommand.Execute(vm.SelectedNode.OutputDefinitions[0]);
            check("条件节点：双击绑定写 Guid 键（不是 Flag/虚拟变量）",
                condition.LinkedSources.ContainsKey(flagId.ToString())
                && !condition.LinkedSources.ContainsKey("Flag")
                && !condition.LinkedSources.ContainsKey("虚拟变量"),
                "LinkedSources 键：" + string.Join(",", condition.LinkedSources.Keys));

            vm.UnbindCommand.Execute(vm.DisplayDataPort[0]);
            check("条件节点：清除绑定删 Guid 键",
                !condition.LinkedSources.ContainsKey(flagId.ToString()),
                "LinkedSources 键：" + string.Join(",", condition.LinkedSources.Keys));
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
                var outDir = EvidencePaths.Dir(RepoRoot, "variable-binding");
                Directory.CreateDirectory(outDir);

                using var fixture = new Fixture();
                var view = new VariableBindingView { DataContext = fixture.Vm };
                var host = CreateHost(view, 1180, 760);
                try
                {
                    Render(host, outDir, "default", Check);

                    var scorePort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "最小分数");
                    fixture.Vm.SelectedInputPort = scorePort;
                    fixture.Vm.IsConstantMode = true;
                    fixture.Vm.ConstantValue = "0.75";
                    Pump(host);
                    Render(host, outDir, "constant", Check);

                    var enumPort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "查找类型");
                    fixture.Vm.SelectedInputPort = enumPort;
                    Pump(host);
                    Render(host, outDir, "enum", Check);

                    var imagePort = fixture.Vm.DisplayDataPort.First(p => p.Definition.Name == "图像");
                    fixture.Vm.SelectedInputPort = imagePort;
                    fixture.Vm.ShowAllPorts = true;
                    Pump(host);
                    Render(host, outDir, "incompatible", Check);
                    fixture.Vm.ShowAllPorts = false;

                    fixture.Vm.SelectedInputPort = scorePort;
                    var scores = fixture.Vm.DisplayPorts.First(p => p.Name == "ScoresVar");
                    var renderCandidateList = view.FindName("CandidateList") as ListBox;
                    renderCandidateList?.ScrollIntoView(scores);
                    fixture.Vm.SelectedOutputPort = scores;
                    Pump(host);
                    fixture.Vm.DoublockClickBindCommand.Execute(scores);
                    Pump(host);
                    Render(host, outDir, "array", Check);
                    fixture.Vm.CancelArrayBindCommand.Execute();

                    host.Width = 1000;
                    host.Height = 760;
                    host.Measure(new Size(1000, 760));
                    host.Arrange(new Rect(0, 0, 1000, 760));
                    fixture.Vm.SelectedInputPort = imagePort;
                    Pump(host);
                    Render(host, outDir, "narrow", Check);
                }
                finally
                {
                    host.Child = null;
                }

                using var emptyFixture = new Fixture(empty: true);
                var emptyView = new VariableBindingView { DataContext = emptyFixture.Vm };
                var emptyHost = CreateHost(emptyView, 1180, 760);
                try
                {
                    Render(emptyHost, outDir, "empty", Check);
                }
                finally
                {
                    emptyHost.Child = null;
                }

                using var singleFixture = new Fixture(singleBind: true);
                var singleView = new VariableBindingView { DataContext = singleFixture.Vm };
                var singleHost = CreateHost(singleView, 1180, 760);
                try
                {
                    Render(singleHost, outDir, "single", Check);
                }
                finally
                {
                    singleHost.Child = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 变量绑定视图渲染抛出异常:");
                Console.WriteLine(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 变量绑定视图渲染：完成 ==="
                : $"### 变量绑定视图渲染：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        private static void Render(FrameworkElement host, string outDir, string name, Action<string, bool, string> check)
        {
            var path = Path.Combine(outDir, "after-render-v1-" + name + ".png");
            RenderElement(host, path);
            var info = new FileInfo(path);
            // 不只判存在：旧 PNG 残留会让"本次渲染退化"假绿；PNG 非空至少说明本次写入了内容。
            check("渲染 " + name, info.Exists && info.Length > 10_000, $"{path} ({info.Length} bytes)");
        }

        // ==================================================================
        //  静态检查
        // ==================================================================

        private static void RunStaticChecks(Action<string, bool, string> check)
        {
            var xamlPath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\VariableBindingView.xaml");
            var codePath = Path.Combine(RepoRoot, @"VisionMaster\Views\DialogViews\VariableBindingView.xaml.cs");
            var xamlRaw = File.Exists(xamlPath) ? File.ReadAllText(xamlPath) : string.Empty;
            var code = File.Exists(codePath) ? File.ReadAllText(codePath) : string.Empty;
            check("视图 XAML 可读", xamlRaw.Length > 0, xamlPath);
            check("code-behind 可读", code.Length > 0, codePath);
            if (xamlRaw.Length == 0 || code.Length == 0) return;

            var xaml = System.Text.RegularExpressions.Regex.Replace(xamlRaw, "<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            check("XAML 无硬编码十六进制颜色", !System.Text.RegularExpressions.Regex.IsMatch(xaml, @"#[0-9A-Fa-f]{6,8}"), "");
            check("XAML 无本地 keyed Style", !System.Text.RegularExpressions.Regex.IsMatch(xaml, @"<Style\b[^>]*x:Key="), "");
            check("候选行类型绑定逐字使用 TypeNameToFriendlyNameConverter",
                xaml.Contains("DataTypeName, Converter={ui:TypeNameToFriendlyNameConverter}"), "");
            check("窗口可缩放 + SizeToContent=Manual",
                xaml.Contains("ResizeMode\" Value=\"CanResizeWithGrip\"")
                && xaml.Contains("SizeToContent\" Value=\"Manual\""), "");
            check("关键命令已接线（Unbind / Doublock / SelectPreset / ConfirmArray）",
                xaml.Contains("Command=\"{Binding UnbindCommand}\"")
                && xaml.Contains("Command=\"{Binding DataContext.DoublockClickBindCommand")
                && xaml.Contains("Command=\"{Binding DataContext.SelectPresetCommand")
                && xaml.Contains("Command=\"{Binding ConfirmArrayBindCommand}\""), "");
            check("三栏列表 ItemsSource 绑定齐全（DisplayDataPort / TreeNodes / DisplayPorts）",
                xaml.Contains("ItemsSource=\"{Binding DisplayDataPort}\"")
                && xaml.Contains("ItemsSource=\"{Binding TreeNodes}\"")
                && xaml.Contains("ItemsSource=\"{Binding DisplayPorts}\""), "");
            check("滚轮就近滚动接线（PreviewMouseWheel + FindScrollableAncestor）",
                xaml.Contains("PreviewMouseWheel=\"OnPreviewMouseWheel\"")
                && code.Contains("FindScrollableAncestor"), "");
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        private sealed class Fixture : IDisposable
        {
            public WorkspaceContext Workspace { get; }
            public ActionStep Step { get; }
            public VariableBindingViewModel Vm { get; }

            public Fixture(bool singleBind = false, bool empty = false, bool noTarget = false)
            {
                Workspace = new WorkspaceContext();
                Workspace.GlobalVariables.Clear();
                if (!empty)
                {
                    Workspace.GlobalVariables.Add(Fake("ImageVar", typeof(HImage), "图像变量"));
                    // 数组变量放前面：Double 目标的候选列表首行就能看到「数组」徽标与「选择索引」按钮。
                    Workspace.GlobalVariables.Add(Fake("ScoresVar", typeof(double[]), "分数数组"));
                    Workspace.GlobalVariables.Add(Fake("RegionVar", typeof(HRegion), "区域变量"));
                    Workspace.GlobalVariables.Add(Fake("ScoreVar", typeof(double), "分数变量"));
                    Workspace.GlobalVariables.Add(Fake("IntVar", typeof(int), "整数变量"));
                    Workspace.GlobalVariables.Add(Fake("FlagVar", typeof(bool), "布尔变量"));
                    Workspace.GlobalVariables.Add(Fake("WhenVar", typeof(DateTime), "时间变量"));
                }

                var solution = new SolutionModel { SolutionName = "变量绑定冒烟" };
                var flow = new FlowModel { FlowName = "变量绑定冒烟流程" };
                solution.Flows.Add(flow);
                Workspace.SwitchSolution(solution);
                Workspace.SwitchFlow(flow);

                Step = new ActionStep("", "模板匹配", PluginType, "模板匹配_0");
                // 让「图像」端口有一个已绑定来源，才能验证「已绑定」筛选与英雄区已绑定态。
                Step.LinkedSources["图像"] = new LinkReference(
                    LinkKind.StepPort,
                    Guid.NewGuid(),
                    "ImageVar",
                    "全局变量 (Global).ImageVar");
                flow.Steps.Add(Step);

                var provider = new StubPluginProvider();
                provider.RegisterModule(new ToolItemModel
                {
                    Name = "模板匹配",
                    ModuleTypeName = PluginType,
                    Category = "视觉工具",
                    Description = "变量绑定冒烟用插件",
                    InputDefinitions = empty
                        ? new List<PortDefinition>()
                        : new List<PortDefinition>
                        {
                            new() { Name = "图像", Description = "待匹配的输入图像", DataTypeName = typeof(HImage).AssemblyQualifiedName },
                            new() { Name = "模板图像", Description = "模板图像", DataTypeName = typeof(HImage).AssemblyQualifiedName },
                            new() { Name = "搜索区域", Description = "搜索区域", DataTypeName = typeof(HRegion).AssemblyQualifiedName },
                            new() { Name = "最小分数", Description = "最低匹配分数", DataTypeName = typeof(double).AssemblyQualifiedName },
                            new() { Name = "匹配数量", Description = "最多返回数量", DataTypeName = typeof(int).AssemblyQualifiedName },
                            new() { Name = "查找类型", Description = "匹配几何形状", DataTypeName = typeof(SmokeEnum).AssemblyQualifiedName, IsFunctionalEnum = true },
                            new() { Name = "值", Description = "任意值（object 端口）", DataTypeName = typeof(object).AssemblyQualifiedName },
                        },
                });

                Vm = new VariableBindingViewModel(Workspace, provider);
                var parameters = new DialogParameters
                {
                    { "IsSingleBindMode", singleBind },
                };
                if (!noTarget)
                    parameters.Add("TargetStep", Step);
                if (singleBind)
                {
                    parameters.Add("TargetPortName", "目标变量");
                    parameters.Add("TargetTypeName", typeof(object).AssemblyQualifiedName);
                }
                Vm.OnDialogOpened(parameters);

                // 默认选中第一个输入端口（图像），并选中全局变量来源，让候选列表有内容。
                if (!empty && Vm.TreeNodes.Count > 0)
                {
                    Vm.SelectedNode = Vm.TreeNodes[0];
                }
            }

            public void Dispose() { }
        }

        private static IVariable Fake(string name, Type type, string description) => new FakeVariable
        {
            Name = name,
            DataType = type,
            Description = description,
            VariableType = VariableType.Local,
            Value = type.IsArray
                ? Array.CreateInstance(type.GetElementType() ?? typeof(object), 0)
                : type == typeof(string) ? string.Empty : Activator.CreateInstance(type),
        };

        private sealed class FakeVariable : IVariable
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
        }

        private sealed class StubPluginProvider : IPluginProvider
        {
            private readonly Dictionary<string, ToolItemModel> _modules = new();
            private static readonly IReadOnlyDictionary<string, ToolItemModel> Empty =
                new Dictionary<string, ToolItemModel>();

            public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => _modules;
            public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins => Empty;
            public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins => Empty;
            public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins => Empty;

            public void RegisterModule(ToolItemModel plugin) => _modules[plugin.ModuleTypeName] = plugin;
            public void RegisterCamera(ToolItemModel plugin) { }
            public void RegisterLaser(ToolItemModel plugin) { }
            public void RegisterMotion(ToolItemModel plugin) { }
            public ToolItemModel GetModule(string name) => _modules.TryGetValue(name, out var p) ? p : null;
            public ToolItemModel GetCamera(string name) => null;
            public ToolItemModel GetLaser(string name) => null;
            public ToolItemModel GetMotion(string name) => null;
        }

        // ==================================================================
        //  渲染 / 视觉树工具
        // ==================================================================

        private static DialogCloseListener MakeDialogCloseListener(Action<IDialogResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic,
                null,
                new[] { typeof(Action<IDialogResult>) },
                null);
            if (ctor == null)
                throw new InvalidOperationException("Prism.Dialogs.DialogCloseListener 构造器形状变了");
            return (DialogCloseListener)ctor.Invoke(new object[] { onClosed });
        }

        private static Border CreateHost(VariableBindingView view, double width, double height)
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
                fe.UpdateLayout();
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

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            for (var current = element == null ? null : VisualTreeHelper.GetParent(element);
                 current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is T match) return match;
            }
            return null;
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
