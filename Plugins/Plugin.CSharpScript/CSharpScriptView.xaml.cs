using Core.Editing;
using Core.Interfaces;
using ICSharpCode.AvalonEdit;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

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
    /// C# 脚本配置视图：左列接口变量轨（输入/输出变量 + Context 速查），右列 AvalonEdit C# 编辑器。
    /// 本视图不含"执行"按钮——试运行由主程序外壳提供。
    ///
    /// 2026-10-20 Web 化：只重排外观与令牌；DataContext 仍是插件实例，输入变量仍走
    /// LinkableValueEditor（Port / StepData / PortsVersion），AvalonEdit 与全部编辑能力未换。
    /// </summary>
    public partial class CSharpScriptView : UserControl
    {
        private CSharpScriptPlugin Plugin => DataContext as CSharpScriptPlugin;

        private bool _loadingText; // 程序写 Editor.Text 时置 true，避免 TextChanged 回写死循环
        private bool _editorInitialized;
        private bool _textInitialized;

        /// <summary>
        /// 「校验脚本」按钮的视图级命令适配器。
        /// 只转发现有 Validate_Click 行为，不新增业务逻辑；批量插件视图冒烟要求至少一条 Command 绑定。
        /// </summary>
        public ICommand ValidateCommand { get; }

        public CSharpScriptView()
        {
            ValidateCommand = new DelegateCommand(RunValidation);
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        /// <summary>
        /// DataContext 在 GetConfigView 里设置（此时 ScriptText 已由 Initialize 灌入）。
        /// 提前把正文放进编辑器：批量冒烟/离屏渲染不会触发 Loaded，否则证据图里编辑器是空的。
        /// 只初始化一次，不覆盖用户后续编辑。
        /// </summary>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            InitializeEditorText();
        }

        private void InitializeEditorText()
        {
            if (_textInitialized || Editor == null) return;
            var plugin = Plugin;
            if (plugin == null) return;

            _textInitialized = true;
            _loadingText = true;
            Editor.Text = plugin.ScriptText ?? "";
            _loadingText = false;
            Editor.SyntaxHighlighting = CSharpHighlighting.GetDefinition();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_editorInitialized) return;
            _editorInitialized = true;

            // 自定义 C# 语法高亮（Context API/类型/关键字；接口变量名不着色——它们不是 C# 变量）
            Editor.SyntaxHighlighting = CSharpHighlighting.GetDefinition();

            // 智能提示：补全（含 Context. 成员）+ 调用模板 + 悬浮文档 + 参数提示
            CSharpIntelliSense.Attach(Editor, CollectVarInfos);

            // 编辑行为：撤销/自动缩进/括号配对/注释切换/缩放/当前行高亮/查找替换（C# 行注释前缀 //）
            ScriptEditorBehavior.Attach(Editor, "//");

            // 右键菜单：校验 / 注释 / 取消注释 / 插入代码片段
            BuildEditorContextMenu();

            // 编辑器文本初始化（不绑双向 Text，改由 DataContextChanged/Loaded 拉取 + TextChanged 回写，
            // 规避换行/光标跳动；DataContextChanged 已初始化过则这里不再覆盖）
            InitializeEditorText();
        }

        /// <summary>收集接口变量（名称/方向/类型），供补全与悬浮使用。</summary>
        private IList<CSharpIntelliSense.VarInfo> CollectVarInfos()
        {
            var list = new List<CSharpIntelliSense.VarInfo>();
            if (Plugin == null) return list;

            foreach (var v in Plugin.InputVars)
            {
                if (v == null) continue;
                list.Add(new CSharpIntelliSense.VarInfo
                {
                    Name = v.Name,
                    Kind = "输入",
                    // R40：枚举上屏走中文显示名（下拉框显示"整数"，悬浮卡/补全说明不能又退回 Int）
                    Type = StepConfigOption.ForEnum(typeof(ScriptVarType), v.Type.ToString()).Display,
                    ClrType = v.Type.ToClrTypeName(),
                    IsOutput = false,
                });
            }
            foreach (var v in Plugin.OutputVars)
            {
                if (v == null) continue;
                list.Add(new CSharpIntelliSense.VarInfo
                {
                    Name = v.Name,
                    Kind = "输出",
                    Type = StepConfigOption.ForEnum(typeof(ScriptVarType), v.Type.ToString()).Display,
                    ClrType = v.Type.ToClrTypeName(),
                    IsOutput = true,
                });
            }
            return list;
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
        }

        private void AddOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.OutputVars == null) return;
            Plugin.OutputVars.Add(new ScriptVarDef { Name = UniqueName(Plugin.OutputVars, "out") });
        }

        private void DelInput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.InputVars == null) return;
            if ((sender as FrameworkElement)?.Tag is ScriptVarDef v)
            {
                Plugin.InputVars.Remove(v);
            }
        }

        private void DelOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.OutputVars == null) return;
            if ((sender as FrameworkElement)?.Tag is ScriptVarDef v)
            {
                Plugin.OutputVars.Remove(v);
            }
        }

        // ---- 共享变量（全局 / 系统，按 Id 声明） ----

        private void AddShared_Click(object sender, RoutedEventArgs e)
        {
            Plugin?.SharedVars?.Add(new ScriptSharedVarDef());
        }

        private void DelShared_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin?.SharedVars == null) return;
            if ((sender as FrameworkElement)?.Tag is ScriptSharedVarDef def)
                Plugin.SharedVars.Remove(def);
        }

        /// <summary>
        /// 选中变量候选 → 把 Id / 名字 / 类型 / 是否系统变量一起落到声明行。
        /// 只绑 <c>SelectedValue</c> 的话，Name/TypeName/IsSystem 会停在旧值——
        /// 变量改名后类型与"只读"标记都不跟，方向下拉会给出错误的可写状态。
        /// </summary>
        private void SharedVar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ScriptSharedVarDef def) return;
            if ((sender as ComboBox)?.SelectedItem is not VariableOption option) return;

            def.VariableId = option.VariableId;
            def.Name = option.Name;
            def.TypeName = option.TypeName;
            def.IsSystem = option.IsSystem;
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
        /// 校验脚本：强制重编译当前编辑器内容（不执行），结果显示在底部状态栏，
        /// 错误行标红并跳到首个错误行。
        /// </summary>
        private void Validate_Click(object sender, RoutedEventArgs e) => RunValidation();

        private void RunValidation()
        {
            if (Plugin == null) return;

            // 固化编辑器当前文本，确保校验的是屏幕上的内容
            Plugin.ScriptText = Editor.Text;

            string err = Plugin.ValidateScript();
            Plugin.ValidationResult = err == null ? "✔ 脚本校验通过（编译无误）" : "✘ " + err;

            // 从"第 N 行 第 M 列: ..."收集全部错误行号 → 编辑器标红 + 跳到首个错误行
            //（Roslyn 诊断行号即编辑器真实行号，无需换算）
            var errorLines = err == null ? null : ParseErrorLines(err);
            ScriptEditorBehavior.SetErrorLines(Editor, errorLines);
            if (errorLines != null && errorLines.Count > 0)
            {
                Editor.TextArea.Caret.Line = errorLines[0];
                Editor.ScrollToLine(errorLines[0]);
            }
        }

        private static List<int> ParseErrorLines(string err)
        {
            var list = new List<int>();
            foreach (Match m in Regex.Matches(err, @"第\s*(\d{1,4})\s*行"))
            {
                if (int.TryParse(m.Groups[1].Value, out int line) && line >= 1 && !list.Contains(line))
                    list.Add(line);
            }
            return list;
        }

        #region 右键菜单

        /// <summary>
        /// 编辑器右键菜单：校验 / 注释 / 取消注释 / 插入代码片段（按分类分组）。
        /// 菜单在代码里构建，属视图层职责，不违反 MVVM。
        /// </summary>
        private void BuildEditorContextMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu();

            menu.Items.Add(MenuItem("校验脚本（编译检查）", "检查语法/类型错误并标红错误行",
                (s, e) => Validate_Click(s, e)));
            menu.Items.Add(MenuItem("注释", "Ctrl+/ —— 给当前行/选区加 // 注释",
                (s, e) => ScriptEditorBehavior.SetComment(Editor.TextArea, true, "//")));
            menu.Items.Add(MenuItem("取消注释", "去掉当前行/选区的 // 注释",
                (s, e) => ScriptEditorBehavior.SetComment(Editor.TextArea, false, "//")));
            menu.Items.Add(new System.Windows.Controls.Separator());

            var tplRoot = new System.Windows.Controls.MenuItem { Header = "插入代码片段" };
            foreach (var grp in System.Linq.Enumerable.GroupBy(
                     CSharpTemplates.All, t => t.Category))
            {
                var catItem = new System.Windows.Controls.MenuItem { Header = grp.Key };
                foreach (var t in grp)
                {
                    var code = t.Code; // 闭包捕获
                    catItem.Items.Add(MenuItem(t.Title, null, (s, e) => InsertTemplate(code)));
                }
                tplRoot.Items.Add(catItem);
            }
            menu.Items.Add(tplRoot);

            Editor.ContextMenu = menu;
        }

        private static System.Windows.Controls.MenuItem MenuItem(
            string header, string toolTip, System.Windows.RoutedEventHandler onClick)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            if (toolTip != null) item.ToolTip = toolTip;
            item.Click += onClick;
            return item;
        }

        /// <summary>把片段整段插入到光标所在行的下方（不与现有代码粘连），可一次 Ctrl+Z 撤销。</summary>
        private void InsertTemplate(string code)
        {
            var doc = Editor.Document;
            var line = doc.GetLineByOffset(Editor.TextArea.Caret.Offset);

            string block = code.Replace("\r\n", "\n").Replace('\r', '\n');
            int insertAt = line.Offset + line.Length;
            string prefix = line.Length > 0 ? "\n" : "";

            doc.Insert(insertAt, prefix + block);
            Editor.TextArea.Caret.Offset = insertAt + prefix.Length;
        }

        #endregion
    }

    /// <summary>
    /// 校验状态三档：空串 → Idle；✔ 开头 → Ok；✘ 开头 → Error。
    /// 只驱动视觉，不改变插件 ValidationResult 的字符串语义。
    /// </summary>
    public sealed class ValidationStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return "Idle";
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("✔", StringComparison.Ordinal)) return "Ok";
            if (trimmed.StartsWith("✘", StringComparison.Ordinal)) return "Error";
            return "Idle";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>校验状态栏文案：空串显示「尚未校验」，其余原样显示。</summary>
    public sealed class ValidationTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string)
                ? "尚未校验 · 点击「校验脚本」做编译检查"
                : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
