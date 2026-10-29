using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Controls;
using Core.Interfaces;
using Plugin.Util.Models;
using VisionMaster.Plugins.Util;

namespace VariablePickerProbe
{
    /// <summary>
    /// 「变量名选择」Step 0 探针（2026-10-29）。
    ///
    /// 要回答四个问题，全部用**真视图/真控件**，不仿造：
    ///   S1 赋值插件的全局候选浮层：打开一次要实例化几个容器、耗时多少；过滤一次多少
    ///   S2 运行时名字列（可编辑 ComboBox）：下拉打开实例化几个容器 —— 顺带回答"Fluent 模板是否虚拟化"
    ///   S3 LinkableValueEditor 的封闭集合下拉（10k 选项）：填充与打开各多少、实例化几个
    ///   S4 目标形态原型（虚拟化列表 + ICollectionView 过滤 + 高度有界）：5000 条只实例化几行
    ///   S5 32 行全量重建（缺陷 C）：N=2414 时 SetConfigContext + 逐行添加的累计耗时
    ///
    /// 判据取"容器数"，不是耗时（耗时随机器浮动；"几千行全被实例化"才是可判定、可反证的表征）。
    /// S1 的断言是**反向**的：必须复现"全量实例化"。若不成立，说明诊断结论有误，探针判红。
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== 变量名选择探针（Step 0） ===");
            Console.WriteLine($">> 机器：{Environment.MachineName}  运行时：{Environment.Version}  WPF：{typeof(Window).Assembly.GetName().Version}");
            Console.WriteLine();

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeThemeChain(app);
            Console.WriteLine();

            try
            {
                Scenario1_AssignmentPopup(2414);
                Scenario1_AssignmentPopup(10000);
                Scenario2_RuntimeCombo(2414);
                Scenario3_LinkableEditorDropdown(10000);
                Scenario4_VirtualizedTarget(5000);
                Scenario5_RowRebuild(2414, 32);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("!! 探针异常终止：" + ex);
                _failures++;
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "RESULT: ALL_OK" : $"RESULT: FAILED ({_failures})");
            try { app.Shutdown(); } catch { }
            return _failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 主题链

        /// <summary>
        /// 按 App.xaml 的顺序合并同一套字典（Fluent 最后 = 与生产一致：
        /// 隐式 ComboBox 样式由 Fluent 那份胜出，UI 库 Selection.xaml 那份被顶掉）。
        /// 加载失败必须打出来 —— 主题没进来时"是否虚拟化"的测量结论无效。
        /// </summary>
        private static void MergeThemeChain(Application app)
        {
            var uris = new[]
            {
                "pack://application:,,,/UI;component/Themes/Generic.xaml",
                "pack://application:,,,/UI;component/Themes/Controls/Input.xaml",
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            };

            foreach (var uri in uris)
            {
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
                    Console.WriteLine(">> 主题合并 OK：" + uri);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("!! 主题合并失败：" + uri + " —— " + ex.GetType().Name + ": " + ex.Message);
                    _failures++;
                }
            }

            // 生产里隐式 ComboBox 样式实际由谁提供：打印一次，避免"以为测的是 A、其实用的是 B"
            var comboStyle = app.TryFindResource(typeof(ComboBox)) as Style;
            int chainSetters = 0;
            for (var s = comboStyle; s != null; s = s.BasedOn) chainSetters += s.Setters.Count;
            Console.WriteLine($">> 应用级隐式 ComboBox 样式：{(comboStyle == null ? "无" : "BasedOn 链 setters=" + chainSetters)}");
        }

        // ---------------------------------------------------------------- S1

        private static void Scenario1_AssignmentPopup(int n)
        {
            Console.WriteLine($"--- S1 赋值插件全局候选浮层（{n} 条） ---");

            var ctx = new PluginConfigContext { Variables = BuildOptions(n, VariableAssignmentPlugin.ScopeGlobal) };
            var plugin = new VariableAssignmentPlugin();
            plugin.SetConfigContext(ctx);
            plugin.Rows.Add(new AssignmentRow
            {
                Scope = VariableAssignmentPlugin.ScopeGlobal,
                VariableName = "Var_00000", // 命中已有候选：避免"已失效"项混进计数
            });

            var stepData = new ProbeStepData();
            var view = (FrameworkElement)plugin.GetConfigView(stepData);
            var win = ShowHost(view, 960, 640);

            var vm = plugin.RowVms.FirstOrDefault();
            if (vm == null)
            {
                Check("S1 行视图模型存在", false, "RowVms 为空");
                win.Close();
                return;
            }

            Check("S1 行作用域=全局（走 Popup 分支）", vm.IsGlobal, "IsGlobal=" + vm.IsGlobal);
            Console.WriteLine($"    行候选数 Candidates={vm.Candidates.Count}  FilteredCandidates={vm.FilteredCandidates.Count}");

            var sw = Stopwatch.StartNew();
            vm.IsGlobalNameOpen = true;
            Pump();
            sw.Stop();

            var popup = AllPopups(view).FirstOrDefault(p => p.IsOpen);
            int buttons = popup?.Child == null ? -1 : Visuals<Button>(popup.Child).Count();
            int items = popup?.Child == null ? -1 : Visuals<ItemsControl>(popup.Child).Select(c => c.Items.Count).DefaultIfEmpty(-1).Max();

            Console.WriteLine($"    打开浮层：{sw.ElapsedMilliseconds} ms   ItemsControl.Items={items}   实例化 Button={buttons}");
            Check("S1 浮层已打开（测量有效）", popup != null && popup.Child != null, popup == null ? "未找到打开的 Popup" : "ok");
            // 允许 ±2：Popup 里除条目外还有模板外壳按钮（样式派生），判据看"是否接近全量"而不是精确相等
            Check($"S1 现状复现：{n} 条全部实例化（ItemsControl 不虚拟化）",
                buttons > 25 && Math.Abs(buttons - items) <= 2,
                $"buttons={buttons} items={items} n={n}");

            var sw2 = Stopwatch.StartNew();
            vm.GlobalSearchText = "Var_0001";
            Pump();
            sw2.Stop();

            int buttons2 = popup?.Child == null ? -1 : Visuals<Button>(popup.Child).Count();
            int matched = vm.FilteredCandidates.Count;
            int items2 = popup?.Child == null ? -1 : Visuals<ItemsControl>(popup.Child).Select(c => c.Items.Count).DefaultIfEmpty(-1).Max();
            Console.WriteLine($"    过滤 \"Var_0001\"：{sw2.ElapsedMilliseconds} ms   命中={matched}   Items={items2}   实例化 Button={buttons2}");
            Check("S1 过滤后仍是全量命中项实例化", Math.Abs(buttons2 - items2) <= 2, $"buttons={buttons2} items={items2}");
            Check("S1 过滤确实收窄了候选", matched > 0 && matched < n, $"matched={matched} n={n}");

            vm.IsGlobalNameOpen = false;
            Pump();
            win.Close();
            Pump();
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- S2

        private static void Scenario2_RuntimeCombo(int n)
        {
            Console.WriteLine($"--- S2 运行时名字列（可编辑 ComboBox，{n} 条） ---");

            var ctx = new PluginConfigContext { Variables = BuildOptions(n, VariableAssignmentPlugin.ScopeRuntime) };
            var plugin = new VariableAssignmentPlugin();
            plugin.SetConfigContext(ctx);
            plugin.Rows.Add(new AssignmentRow
            {
                Scope = VariableAssignmentPlugin.ScopeRuntime,
                VariableName = string.Empty,
            });

            var view = (FrameworkElement)plugin.GetConfigView(new ProbeStepData());
            var win = ShowHost(view, 960, 640);

            var vm = plugin.RowVms.FirstOrDefault();
            var combo = Visuals<ComboBox>(view).FirstOrDefault(c => string.Equals(c.Tag as string, "NameEditor", StringComparison.Ordinal));
            if (combo == null)
            {
                Check("S2 找到运行时名字 ComboBox", false, "Tag=NameEditor 未命中");
                win.Close();
                return;
            }

            Check("S2 运行时 ComboBox 已渲染（测量有效）", combo.IsVisible && combo.Items.Count > 0,
                $"visible={combo.IsVisible} items={combo.Items.Count}");
            Console.WriteLine($"    ItemsSource.Count={combo.Items.Count}  IsEditable={combo.IsEditable}  IsTextSearchEnabled={combo.IsTextSearchEnabled}");

            var sw = Stopwatch.StartNew();
            combo.IsDropDownOpen = true;
            Pump();
            sw.Stop();

            int realized = 0;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) != null) realized++;
            }

            bool virtualized = realized > 0 && realized < Math.Max(2, combo.Items.Count / 2);
            Console.WriteLine($"    打开下拉：{sw.ElapsedMilliseconds} ms   总项={combo.Items.Count}   实例化 ComboBoxItem={realized}");
            Console.WriteLine($"    → Fluent 模板虚拟化：{(virtualized ? "是" : "否")}");
            Check("S2 下拉打开后有容器（测量有效）", realized > 0, $"realized={realized}");
            // 基线断言：记录 2026-10-29 的实测形态。修好虚拟化后这条会红，届时应改成本条的反面。
            Check("S2 基线：Fluent ComboBox 模板不虚拟化", !virtualized, $"realized={realized} items={combo.Items.Count}");
            Console.WriteLine($"    → 无过滤：IsTextSearchEnabled={combo.IsTextSearchEnabled}，键入不改变候选（代码事实，见 VariableAssignmentView.xaml:590）");

            combo.IsDropDownOpen = false;
            Pump();
            win.Close();
            Pump();
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- S3

        private static void Scenario3_LinkableEditorDropdown(int n)
        {
            Console.WriteLine($"--- S3 LinkableValueEditor 封闭集合下拉（{n} 个选项） ---");

            StepConfigOptionSource.RegisterAdditional(kind =>
                kind == StepConfigOptionKind.CommunicationVariableName ? BuildStepOptions(n) : null);

            var port = new InputPort<string>("ProbeCommVar", string.Empty, "探针用通讯变量名")
            {
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.CommunicationVariableName,
            };

            var editor = new LinkableValueEditor { StepData = new ProbeStepData() };

            var swFill = Stopwatch.StartNew();
            editor.Port = port;
            swFill.Stop();

            var win = ShowHost(editor, 420, 120);
            var box = editor.FindName("PART_EnumBox") as ComboBox;
            if (box == null)
            {
                Check("S3 找到 PART_EnumBox", false, "FindName 未命中");
                win.Close();
                return;
            }

            Console.WriteLine($"    填充 EnumOptions：{swFill.ElapsedMilliseconds} ms   EnumOptions.Count={editor.EnumOptions.Count}   渲染形态 IsOptionPort={editor.IsOptionPort}");
            Check("S3 封闭集合端口渲染成下拉（测量有效）", editor.IsOptionPort && box.Items.Count == n,
                $"IsOptionPort={editor.IsOptionPort} items={box.Items.Count}");

            var sw = Stopwatch.StartNew();
            box.IsDropDownOpen = true;
            Pump();
            sw.Stop();

            int realized = 0;
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.ItemContainerGenerator.ContainerFromIndex(i) != null) realized++;
            }

            bool virtualized3 = realized > 0 && realized < Math.Max(2, box.Items.Count / 2);
            Console.WriteLine($"    打开下拉：{sw.ElapsedMilliseconds} ms   总项={box.Items.Count}   实例化 ComboBoxItem={realized}");
            Console.WriteLine($"    → 该下拉虚拟化：{(virtualized3 ? "是" : "否")}");
            Check("S3 下拉打开后有容器（测量有效）", realized > 0, $"realized={realized}");
            // 基线断言：同上，记录 2026-10-29 实测形态。
            Check("S3 基线：LinkableValueEditor 封闭集合下拉不虚拟化", !virtualized3, $"realized={realized} items={box.Items.Count}");

            box.IsDropDownOpen = false;
            Pump();
            win.Close();
            Pump();
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- S4

        /// <summary>
        /// 目标形态原型：与设计方案同构（搜索 + ICollectionView.Filter + 虚拟化列表 + 高度有界）。
        /// 它证明"5000 条只实例化十来行"在设计形态下可达；不证明产品代码已具备。
        /// </summary>
        private static void Scenario4_VirtualizedTarget(int n)
        {
            Console.WriteLine($"--- S4 目标形态原型（虚拟化 + ICollectionView 过滤，{n} 条） ---");

            var data = new List<PickRow>();
            for (int i = 0; i < n; i++)
            {
                data.Add(new PickRow($"Var_{i:00000}", i % 3 == 0 ? "Int32" : i % 3 == 1 ? "Boolean" : "Double",
                    $"变量管理 · 第 {i} 号候选 · 说明文本用于占位"));
            }

            var view = new ListCollectionView(data);
            view.Filter = o => o is PickRow r && r.Matches(_lastQuery);

            var list = new ListBox
            {
                MaxHeight = 220,
                ItemsSource = view,
                BorderThickness = new Thickness(0),
                ItemTemplate = BuildPickRowTemplate(),
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            list.ItemContainerStyle = new Style(typeof(ListBoxItem));
            list.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 48d));
            list.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));

            var host = new Border { Width = 460, Child = list, Background = Brushes.White, Padding = new Thickness(8) };

            var sw = Stopwatch.StartNew();
            var win = ShowHost(host, 520, 340);
            sw.Stop();

            int realized = 0;
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.ItemContainerGenerator.ContainerFromIndex(i) != null) realized++;
            }
            Console.WriteLine($"    首次布局（{n} 条）：{sw.ElapsedMilliseconds} ms   总项={list.Items.Count}   实例化 ListBoxItem={realized}   MaxHeight={list.MaxHeight}");

            var sw2 = Stopwatch.StartNew();
            _lastQuery = "Var_01";
            view.Refresh();
            Pump();
            sw2.Stop();

            int realized2 = 0;
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.ItemContainerGenerator.ContainerFromIndex(i) != null) realized2++;
            }
            Console.WriteLine($"    过滤 \"Var_01\"：{sw2.ElapsedMilliseconds} ms   命中={list.Items.Count}   实例化 ListBoxItem={realized2}");

            Check("S4 目标形态：5000 条只实例化可见行（<=25）", realized > 0 && realized <= 25, $"realized={realized}");
            Check("S4 目标形态：过滤后仍只实例化可见行", realized2 > 0 && realized2 <= 25, $"realized={realized2}");
            Check("S4 目标形态：过滤耗时 < 200 ms", sw2.ElapsedMilliseconds < 200, $"{sw2.ElapsedMilliseconds} ms");

            _lastQuery = string.Empty;
            win.Close();
            Pump();
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- S5

        /// <summary>缺陷 C：每加一行都触发"全部行重算候选"，累计 O(rows^2 × N)。</summary>
        private static void Scenario5_RowRebuild(int n, int rows)
        {
            Console.WriteLine($"--- S5 多行重建（{rows} 行 × {n} 条候选） ---");

            var ctx = new PluginConfigContext { Variables = BuildOptions(n, VariableAssignmentPlugin.ScopeGlobal) };
            var plugin = new VariableAssignmentPlugin();

            var sw = Stopwatch.StartNew();
            plugin.SetConfigContext(ctx);
            for (int i = 0; i < rows; i++)
            {
                plugin.Rows.Add(new AssignmentRow
                {
                    Scope = VariableAssignmentPlugin.ScopeGlobal,
                    VariableName = "Var_" + i,
                });
            }
            sw.Stop();

            long candidateObjects = 0;
            for (int k = 1; k <= rows; k++) candidateObjects += (long)k * n;

            Console.WriteLine($"    SetConfigContext + {rows} 次 Rows.Add：{sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"    期间重算的候选对象累计 ≈ {candidateObjects:N0} 个（= N × rows(rows+1)/2，缺陷 C 的二次项）");
            Check("S5 行全部建出（测量有效）", plugin.RowVms.Count == rows, $"RowVms={plugin.RowVms.Count}");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- 数据构造

        private static List<VariableOption> BuildOptions(int n, string scope)
        {
            var list = new List<VariableOption>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new VariableOption
                {
                    Name = i == 0 ? "Ncc_Result" : $"Var_{i:00000}",
                    Scope = scope,
                    TypeName = i % 3 == 0 ? "Int32" : i % 3 == 1 ? "Boolean" : "Double",
                    Description = "NCC 匹配 · SMD 散焦 判定（探针造数）",
                    VariableId = Guid.NewGuid(),
                });
            }
            return list;
        }

        private static List<StepConfigOption> BuildStepOptions(int n)
        {
            var list = new List<StepConfigOption>(n);
            for (int i = 0; i < n; i++)
                list.Add(new StepConfigOption($"CommVar_{i:00000}", $"CommVar_{i:00000}（Int32）"));
            return list;
        }

        private static string _lastQuery = string.Empty;

        private sealed class PickRow
        {
            public PickRow(string name, string type, string note) { Name = name; TypeName = type; Note = note; }
            public string Name { get; }
            public string TypeName { get; }
            public string Note { get; }
            public bool Matches(string query)
                => string.IsNullOrWhiteSpace(query)
                   || Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || Note.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static DataTemplate BuildPickRowTemplate()
        {
            // 与赋值浮层的行模板同量级：名字(粗) + 类型 + 说明
            var template = new DataTemplate();
            var grid = new FrameworkElementFactory(typeof(Grid));
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition)); col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition)); col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(col0); grid.AppendChild(col1);

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(Grid.ColumnProperty, 0);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.TextProperty, new Binding(nameof(PickRow.Name)));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.FontSizeProperty, 12d);
            var note = new FrameworkElementFactory(typeof(TextBlock));
            note.SetValue(TextBlock.TextProperty, new Binding(nameof(PickRow.Note)));
            note.SetValue(TextBlock.FontSizeProperty, 10.5d);
            note.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            stack.AppendChild(name); stack.AppendChild(note);

            var type = new FrameworkElementFactory(typeof(TextBlock));
            type.SetValue(Grid.ColumnProperty, 1);
            type.SetValue(TextBlock.TextProperty, new Binding(nameof(PickRow.TypeName)));
            type.SetValue(TextBlock.FontSizeProperty, 10d);
            type.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
            type.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);

            grid.AppendChild(stack); grid.AppendChild(type);
            template.VisualTree = grid;
            return template;
        }

        // ---------------------------------------------------------------- 基础设施

        private static Window ShowHost(FrameworkElement content, double w, double h)
        {
            var win = new Window
            {
                Width = w,
                Height = h,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = new Border { Child = content, Background = Brushes.White },
            };
            win.Show();
            Pump();
            return win;
        }

        private static void Pump(int rounds = 3)
        {
            for (int i = 0; i < rounds; i++)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int n;
            try { n = VisualTreeHelper.GetChildrenCount(root); }
            catch { yield break; }

            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var inner in Visuals<T>(child)) yield return inner;
            }
        }

        /// <summary>Popup 不在主可视树里（自带 HwndSource），必须显式从逻辑树找并钻进 Child。</summary>
        private static IEnumerable<Popup> AllPopups(DependencyObject root)
        {
            var found = new List<Popup>();
            var stack = new Stack<DependencyObject>();
            var seen = new HashSet<DependencyObject>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null || !seen.Add(cur)) continue;

                if (cur is Popup popup)
                {
                    found.Add(popup);
                    if (popup.Child != null) stack.Push(popup.Child);
                }

                int n = 0;
                try { n = VisualTreeHelper.GetChildrenCount(cur); } catch { n = 0; }
                for (int i = 0; i < n; i++)
                {
                    var child = VisualTreeHelper.GetChild(cur, i);
                    if (child != null) stack.Push(child);
                }

                try
                {
                    foreach (var child in LogicalTreeHelper.GetChildren(cur))
                        if (child is DependencyObject d) stack.Push(d);
                }
                catch { /* 逻辑树不完整时忽略 */ }
            }

            return found;
        }

        private static void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine((ok ? "    [PASS] " : "    [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
            if (!ok) _failures++;
        }

        /// <summary>IStepConfigData 最小实现（同 UIThemeSmokeTest 的 SmokeStepData）。</summary>
        private sealed class ProbeStepData : IStepConfigData
        {
            private readonly Dictionary<string, object> _values = new();
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon => "";
            public string StepName => "变量名选择探针";
            public string Description => "";
            public Dictionary<string, object> InputValues => _values;
            public void SetInputValue(string key, object value) => _values[key] = value;
            public void RemoveInputValue(string key) => _values.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string GetLinkedAddress(string inputPortName) => null;
            public LinkReference GetLink(string inputPortName) => null;
            public void SetLink(string inputPortName, LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }
    }
}
