using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core.Controls;
using Core.Interfaces;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 批量插件自定义视图冒烟（--plugin-views [ProjectFilter]）：
    /// 从 Modules\Plugin*.dll 反射找 IPluginCustomViewProvider，真实实例化视图并检查：
    /// · 视图可实例化、DataContext/IPluginConfigView 契约；
    /// · LinkableValueEditor 的 Port 绑定；
    /// · Button.Command / ComboBox / TextBox 绑定；
    /// · OnConfirm 可执行；
    /// · 项目内 XAML 无硬编码十六进制颜色；
    /// · 渲染一张初始态 PNG 到 docs/UI设计基线/_batch-evidence/。
    ///
    /// 这是批量改造的公共基线；每个插件的具体端口/命令/画布断言由该插件的
    /// code-changes 记录与 reviewer 复核补足。单插件模式（--image-acquisition）仍是参考实现。
    /// </summary>
    internal static class PluginViewSmoke
    {
        private const string RepoRoot = @"d:\C#\VM";

        public static int Run(string[] args)
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            var modules = Path.Combine(RepoRoot, "Modules");
            var filter = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : null;
            var dlls = Directory.Exists(modules)
                ? Directory.GetFiles(modules, "Plugin*.dll")
                    .Where(p => filter == null || Path.GetFileName(p).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            Check("找到待检插件 DLL", dlls.Count > 0, $"count={dlls.Count} filter={filter ?? "(all)"}");
            var evidenceDir = EvidencePaths.Dir(RepoRoot, "_batch-evidence");
            Directory.CreateDirectory(evidenceDir);

            foreach (var dll in dlls)
            {
                var asmName = Path.GetFileNameWithoutExtension(dll);
                Assembly asm;
                try
                {
                    asm = Assembly.LoadFrom(dll);
                }
                catch (Exception ex)
                {
                    Check(asmName + " 可装载", false, ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                var providers = types
                    .Where(t => t != null && t.IsClass && !t.IsAbstract
                        && typeof(IPluginCustomViewProvider).IsAssignableFrom(t))
                    .ToList();

                if (providers.Count == 0)
                {
                    Console.WriteLine($"  [SKIP] {asmName}：无自定义视图");
                    continue;
                }

                foreach (var providerType in providers)
                {
                    var label = $"{asmName}.{providerType.Name}";
                    try
                    {
                        var plugin = Activator.CreateInstance(providerType);
                        if (plugin == null)
                        {
                            Check(label + " 可实例化", false, "null");
                            continue;
                        }

                        var stepData = new SmokeStepData();
                        var provider = (IPluginCustomViewProvider)plugin;
                        var view = provider.GetConfigView(stepData) as FrameworkElement;
                        Check(label + " GetConfigView 返回 FrameworkElement", view != null, view?.GetType().Name ?? "null");
                        if (view == null)
                            continue;

                        // HandEye 的进度条在空表时整块 Collapsed；冒烟里先补 3 行有效数据，
                        // 让模板与绑定真的参与布局（否则 PART_Indicator.ActualWidth 恒 0，查不出问题）。
                        EnsureHandEyeSamples(plugin);
                        EnsureVariableAssignmentRows(plugin);

                        var host = new Border
                        {
                            Width = 920,
                            Height = 520,
                            Child = view,
                            Background = Application.Current.TryFindResource("DialogSurfaceBrush") as Brush
                        };
                        host.Measure(new Size(920, 520));
                        host.Arrange(new Rect(0, 0, 920, 520));
                        host.UpdateLayout();
                        host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                        // ProgressBar 的模板契约：PART_Track 量宽度、PART_Indicator 被设宽度。
                        // 缺 PART_Track 时 WPF 不设 Indicator.Width —— 条形值对但永远不显示
                        // （HandEye 采集进度条就是这么坏的）。这里直接看渲染后的 ActualWidth。
                        var progressBars = Descendants<ProgressBar>(view).ToList();
                        foreach (var bar in progressBars)
                        {
                            bar.ApplyTemplate();
                            if (bar.Visibility != Visibility.Visible || bar.ActualWidth <= 0 || bar.ActualHeight <= 0)
                            {
                                Check(label + " ProgressBar 参与布局", false,
                                    $"visibility={bar.Visibility} actual={bar.ActualWidth:0.#}x{bar.ActualHeight:0.#}");
                                continue;
                            }

                            double mid = bar.Minimum + (bar.Maximum - bar.Minimum) * 0.5;
                            bar.SetCurrentValue(ProgressBar.ValueProperty, mid);
                            bar.UpdateLayout();

                            var track = bar.Template?.FindName("PART_Track", bar) as FrameworkElement;
                            var indicator = bar.Template?.FindName("PART_Indicator", bar) as FrameworkElement;
                            Check(label + " ProgressBar 指示条可见（PART_Track + PART_Indicator）",
                                track != null && indicator != null && indicator.ActualWidth > 0,
                                $"value={mid:0.##} PART_Track={(track == null ? "null" : track.ActualWidth.ToString("0.#"))} "
                                + $"PART_Indicator={(indicator == null ? "null" : indicator.ActualWidth.ToString("0.#"))}");
                        }

                        int elementCount = Descendants<FrameworkElement>(view).Count();
                        Check(label + " 视图实例化", elementCount > 10, $"elements={elementCount}");
                        bool dataContextOk = view.DataContext is IPluginConfigView || view is IPluginConfigView;
                        Check(label + " DataContext/IPluginConfigView", dataContextOk,
                            view.DataContext?.GetType().Name ?? "null");

                        var editors = Descendants<LinkableValueEditor>(view).ToList();
                        var ports = editors.Select(e => e.Port?.Name ?? "(null)").ToList();
                        Check(label + " LinkableValueEditor.Port", editors.All(e => e.Port != null),
                            $"count={editors.Count} ports={string.Join(",", ports)}");

                        // 变量赋值 v3：全局行的候选与选中项必须真的到位（否则 ComboBox 会显示空白）。
                        if (plugin.GetType().FullName == "VisionMaster.Plugins.Util.VariableAssignmentPlugin")
                        {
                            var rowVms = plugin.GetType().GetProperty("RowVms")?.GetValue(plugin) as System.Collections.IEnumerable;
                            var globalVm = rowVms?.Cast<object>().FirstOrDefault(vm =>
                            {
                                var row = vm.GetType().GetProperty("Row")?.GetValue(vm);
                                return string.Equals(
                                    row?.GetType().GetProperty("Scope")?.GetValue(row) as string,
                                    "Global", StringComparison.OrdinalIgnoreCase);
                            });
                            var candidates = globalVm?.GetType().GetProperty("Candidates")?.GetValue(globalVm)
                                as System.Collections.ICollection;
                            var selected = globalVm?.GetType().GetProperty("SelectedGlobalCandidate")?.GetValue(globalVm);
                            Check(label + " 全局行候选与选中项", candidates != null && candidates.Count > 0 && selected != null,
                                $"candidates={candidates?.Count ?? -1} selected={(selected == null ? "null" : "ok")}");
                        }

                        // LinkableValueEditor 内部的 🔗/✕ 按钮是共享控件的 Click 按钮，不是视图动作按钮；
                        // 只检查视图自己的按钮是否至少有一个 Command 绑定。
                        var ownButtons = Descendants<Button>(view)
                            .Where(b => !IsInsideLinkableEditor(b)).ToList();
                        var buttonCommands = ownButtons
                            .Select(b => BindingOperations.GetBinding(b, Button.CommandProperty)?.Path?.Path)
                            .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                        Check(label + " Button.Command 绑定", ownButtons.Count == 0 || buttonCommands.Count > 0,
                            $"buttons={ownButtons.Count} commands={string.Join(",", buttonCommands)}");

                        var combos = Descendants<ComboBox>(view).ToList();
                        var comboBindings = combos
                            .Select(c => (
                                Items: BindingOperations.GetBinding(c, ItemsControl.ItemsSourceProperty)?.Path?.Path,
                                Selected: BindingOperations.GetBinding(c, System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.Path?.Path))
                            .Where(x => !string.IsNullOrEmpty(x.Items) || !string.IsNullOrEmpty(x.Selected))
                            .ToList();
                        Check(label + " ComboBox 绑定", combos.Count == 0 || comboBindings.Count > 0,
                            string.Join(";", comboBindings.Select(x => $"{x.Items}/{x.Selected}")));

                        var textBoxes = Descendants<TextBox>(view).ToList();
                        var textBindings = textBoxes
                            .Select(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path)
                            .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                        Check(label + " TextBox.Text 绑定", textBoxes.Count == 0 || textBindings.Count > 0,
                            string.Join(",", textBindings));

                        // OnConfirm：插件或视图任一实现 IPluginConfigView 即可
                        var configView = plugin as IPluginConfigView ?? view as IPluginConfigView;
                        if (configView == null)
                        {
                            Check(label + " OnConfirm 可执行", false, "插件/视图均未实现 IPluginConfigView");
                        }
                        else
                        {
                            try
                            {
                                configView.OnConfirm(stepData);
                                Check(label + " OnConfirm 可执行", true, $"InputValues={stepData.InputValues.Count}");
                            }
                            catch (Exception ex)
                            {
                                Check(label + " OnConfirm 可执行", false, ex.GetType().Name + ": " + ex.Message);
                            }
                        }

                        // 静态：项目内所有 XAML 不得有硬编码十六进制颜色
                        // 程序集名不一定等于工程目录名（如 Plugin.Util → Plugin.Utility），按前缀解析
                        var projectDir = ResolveProjectDir(asmName);
                        var xamls = projectDir != null && Directory.Exists(projectDir)
                            ? Directory.GetFiles(projectDir, "*.xaml", SearchOption.AllDirectories)
                                .Where(p => !p.Contains(@"\bin\") && !p.Contains(@"\obj\")).ToList()
                            : new List<string>();
                        int hex = xamls.Sum(p => Regex.Matches(File.ReadAllText(p), @"#[0-9A-Fa-f]{6,8}").Count);
                        Check(label + " XAML hex=0", xamls.Count > 0 && hex == 0,
                            $"xaml={xamls.Count} hex={hex}");

                        var png = Path.Combine(evidenceDir, $"{asmName}-{providerType.Name}.png");
                        RenderElement(host, png);
                        Check(label + " 渲染 PNG", File.Exists(png), png);

                        if (plugin is IDisposable disposable)
                            disposable.Dispose();
                        host.Child = null;
                    }
                    catch (Exception ex)
                    {
                        Check(label + " 冒烟异常", false, ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            Console.WriteLine(failures == 0
                ? "=== 插件自定义视图批量冒烟：全部通过 ==="
                : $"### 插件自定义视图批量冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        private static string? ResolveProjectDir(string asmName)
        {
            var exact = Path.Combine(RepoRoot, "Plugins", asmName);
            if (Directory.Exists(exact))
                return exact;

            var pluginsRoot = Path.Combine(RepoRoot, "Plugins");
            if (!Directory.Exists(pluginsRoot))
                return null;

            return Directory.GetDirectories(pluginsRoot)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.StartsWith(asmName, StringComparison.OrdinalIgnoreCase)
                        || asmName.StartsWith(name, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(d => Path.GetFileName(d).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// 冒烟专用：HandEye 的进度条在空表时整块 Collapsed，先补 3 行有效数据让它参与布局。
        /// 用反射而不是引用 Plugin.HandEye（本宿主按 DLL 名加载插件，不编译期依赖插件工程）。
        /// </summary>
        private static void EnsureHandEyeSamples(object plugin)
        {
            var type = plugin.GetType();
            if (type.FullName != "Plugin.HandEye.HandEyePlugin") return;

            var samples = type.GetProperty("Samples")?.GetValue(plugin) as System.Collections.IList;
            if (samples == null || samples.Count > 0) return;

            var sampleType = type.Assembly.GetType("Plugin.HandEye.Models.HandEyeSample");
            var setRow = sampleType?.GetMethod("SetRow");
            if (sampleType == null || setRow == null) return;

            for (int i = 0; i < 3; i++)
            {
                var row = Activator.CreateInstance(sampleType);
                var values = new double[12];
                for (int k = 0; k < 12; k++) values[k] = 1.0 + i + k * 0.1;
                setRow.Invoke(row, new object[] { values });
                samples.Add(row);
            }
        }

        /// <summary>
        /// 冒烟专用：变量赋值 v3 的行模板在空表时不实例化；补两行（运行时 + 全局）让它参与布局，
        /// 才能检查每行 LinkableValueEditor.Port、ComboBox 绑定与行内按钮命令。
        /// 用反射而不是引用 Plugin.Util（本宿主按 DLL 名加载插件，不编译期依赖插件工程）。
        /// </summary>
        private static void EnsureVariableAssignmentRows(object plugin)
        {
            var type = plugin.GetType();
            if (type.FullName != "VisionMaster.Plugins.Util.VariableAssignmentPlugin") return;

            var rowsProperty = type.GetProperty("Rows");
            var rowType = type.Assembly.GetType("Plugin.Util.Models.AssignmentRow");
            if (rowsProperty == null || rowType == null) return;

            var collectionType = typeof(ObservableCollection<>).MakeGenericType(rowType);
            if (Activator.CreateInstance(collectionType) is not System.Collections.IList rows) return;

            object MakeRow(string id, string name, string scope, string port)
            {
                var row = Activator.CreateInstance(rowType)!;
                rowType.GetProperty("RowId")!.SetValue(row, id);
                rowType.GetProperty("VariableName")!.SetValue(row, name);
                rowType.GetProperty("Scope")!.SetValue(row, scope);
                rowType.GetProperty("CreateIfNotExists")!.SetValue(row, false);
                rowType.GetProperty("ValuePortName")!.SetValue(row, port);
                rowType.GetProperty("Enabled")!.SetValue(row, true);
                return row;
            }

            rows.Add(MakeRow("smoke-r1", "OKCount", "Runtime", "Value_smoke-r1"));
            rows.Add(MakeRow("smoke-r2", "LineName", "Global", "Value_smoke-r2"));
            rowsProperty.SetValue(plugin, rows);

            if (plugin is IPluginConfigContextProvider provider)
            {
                provider.SetConfigContext(new PluginConfigContext
                {
                    Variables = new List<VariableOption>
                    {
                        new VariableOption { Name = "OKCount", Scope = "Runtime", TypeName = "Int32", Description = "本轮累计良品数" },
                        new VariableOption { Name = "LineName", Scope = "Global", TypeName = "String", Description = "产线名称" },
                    }
                });
            }

            // 补两个常量，让值行的 LinkableValueEditor 真实渲染文本态（不是空框）。
            var inputs = type.GetProperty("Inputs")?.GetValue(plugin) as IReadOnlyDictionary<string, IInputPort>;
            if (inputs != null)
            {
                if (inputs.TryGetValue("Value_smoke-r1", out var p1)) p1.Value = "21";
                if (inputs.TryGetValue("Value_smoke-r2", out var p2)) p2.Value = "LineA";
            }
        }

        private static bool IsInsideLinkableEditor(DependencyObject node)
        {
            var cur = node;
            while (cur != null)
            {
                if (cur is LinkableValueEditor)
                    return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var sub in Descendants<T>(child))
                    yield return sub;
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

        private sealed class SmokeStepData : IStepConfigData
        {
            private readonly Dictionary<string, object> _values = new();
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon => "";
            public string StepName => "插件视图冒烟";
            public string Description => "";
            public Dictionary<string, object> InputValues => _values;
            public void SetInputValue(string key, object value) => _values[key] = value;
            public void RemoveInputValue(string key) => _values.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string GetLinkedAddress(string inputPortName) => null!;
            public LinkReference GetLink(string inputPortName) => null!;
            public void SetLink(string inputPortName, LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }
    }
}
