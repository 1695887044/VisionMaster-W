using ICSharpCode.AvalonEdit.Highlighting;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 连线桥接：把"输入变量名 + 宿主插件"转换为该变量对应的动态输入端口（IInputPort）。
    /// 让 ItemsControl 行内的 LinkableValueEditor 能按名挂到插件动态端口上，实现在表格内连线。
    /// （与图像脚本插件的 InputPortConverter 同一范式，仅宿主类型不同）
    /// </summary>
    public class InputPortConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as CSharpScriptPlugin : null;
            return plugin?.GetInputPort(name);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// C# 脚本配置视图：右列 AvalonEdit C# 编辑器（内置 C# 高亮），左列输入/输出变量表。
    /// 本视图不含"执行"按钮——试运行由主程序外壳提供。
    /// </summary>
    public partial class CSharpScriptView : UserControl
    {
        private CSharpScriptPlugin Plugin => DataContext as CSharpScriptPlugin;

        private bool _loadingText; // 程序写 Editor.Text 时置 true，避免 TextChanged 回写死循环

        public CSharpScriptView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 内置 C# 语法高亮（AvalonEdit 自带，无需自备 xshd）
            Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

            // 编辑器文本初始化（不绑双向 Text，改由 Loaded 拉取 + TextChanged 回写，规避换行/光标跳动）
            _loadingText = true;
            Editor.Text = Plugin?.ScriptText ?? "";
            _loadingText = false;

            RefreshLists();
        }

        private void RefreshLists()
        {
            if (Plugin == null) return;

            InputList.ItemsSource = Plugin.InputVars;
            OutputList.ItemsSource = Plugin.OutputVars;

            InputEmptyHint.Visibility = (Plugin.InputVars == null || Plugin.InputVars.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
            OutputEmptyHint.Visibility = (Plugin.OutputVars == null || Plugin.OutputVars.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // 编辑器文本 → 插件配置属性（执行时按内容指纹决定是否重编译）
        private void Editor_TextChanged(object sender, EventArgs e)
        {
            if (_loadingText || Plugin == null) return;
            Plugin.ScriptText = Editor.Text;
        }

        #region 变量增删

        private void AddInput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.InputVars == null) return;
            Plugin.InputVars.Add(new ScriptVarDef { Name = UniqueName(Plugin.InputVars, "in") });
            RefreshLists();
        }

        private void AddOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.OutputVars == null) return;
            Plugin.OutputVars.Add(new ScriptVarDef { Name = UniqueName(Plugin.OutputVars, "out") });
            RefreshLists();
        }

        private void DelInput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.InputVars == null) return;
            if ((sender as FrameworkElement)?.Tag is ScriptVarDef v)
            {
                Plugin.InputVars.Remove(v);
                RefreshLists();
            }
        }

        private void DelOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.OutputVars == null) return;
            if ((sender as FrameworkElement)?.Tag is ScriptVarDef v)
            {
                Plugin.OutputVars.Remove(v);
                RefreshLists();
            }
        }

        // 生成不与现有条目重复的默认名：in / in2 / in3 …
        private static string UniqueName(System.Collections.ObjectModel.ObservableCollection<ScriptVarDef> list, string baseName)
        {
            string name = baseName;
            int n = 1;
            while (list.Any(v => v.Name == name))
                name = $"{baseName}{++n}";
            return name;
        }

        #endregion

        /// <summary>
        /// 校验脚本：强制重编译当前编辑器内容（不执行），结果显示在底部并跳到首个错误行。
        /// </summary>
        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            // 固化编辑器当前文本，确保校验的是屏幕上的内容
            Plugin.ScriptText = Editor.Text;

            string err = Plugin.ValidateScript();
            Plugin.ValidationResult = err == null ? "✔ 脚本校验通过（编译无误）" : "✘ " + err;
            ValidateResultText.Foreground = err == null
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : Brushes.Red;

            // 从"第 N 行 第 M 列: ..."解析首个行号 → 光标/滚动跳过去（引擎诊断已带行列）
            if (err != null)
            {
                var m = Regex.Match(err, @"第\s*(\d{1,4})\s*行");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int line) && line >= 1)
                {
                    Editor.TextArea.Caret.Line = line;
                    Editor.ScrollToLine(line);
                }
            }
        }
    }
}
