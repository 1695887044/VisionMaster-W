using System;
using System.Collections.Generic;
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
            var evidenceDir = Path.Combine(RepoRoot, "docs", "UI设计基线", "_batch-evidence");
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

                        int elementCount = Descendants<FrameworkElement>(view).Count();
                        Check(label + " 视图实例化", elementCount > 10, $"elements={elementCount}");
                        bool dataContextOk = view.DataContext is IPluginConfigView || view is IPluginConfigView;
                        Check(label + " DataContext/IPluginConfigView", dataContextOk,
                            view.DataContext?.GetType().Name ?? "null");

                        var editors = Descendants<LinkableValueEditor>(view).ToList();
                        var ports = editors.Select(e => e.Port?.Name ?? "(null)").ToList();
                        Check(label + " LinkableValueEditor.Port", editors.All(e => e.Port != null),
                            $"count={editors.Count} ports={string.Join(",", ports)}");

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
