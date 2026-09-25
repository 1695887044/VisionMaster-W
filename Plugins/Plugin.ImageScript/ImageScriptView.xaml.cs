using Core.Editing;
using Core.Interfaces;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 连线桥接：把"输入变量名 + 宿主插件"转换为该变量对应的动态输入端口（IInputPort）。
    /// 让 ItemsControl 行内的 LinkableValueEditor 能按名挂到插件动态端口上，实现在表格内连线。
    /// </summary>
    public class InputPortConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values?.Length > 0 ? values[0] as string : null;
            var plugin = values?.Length > 1 ? values[1] as ImageScriptPlugin : null;
            return plugin?.GetInputPort(name);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 图像脚本配置视图：左列过程管理 + 输入/输出变量声明连线，右列 AvalonEdit 脚本编辑器。
    /// 本视图不含"执行"按钮——试运行由主程序外壳提供。
    /// </summary>
    public partial class ImageScriptView : UserControl
    {
        private ImageScriptPlugin Plugin => DataContext as ImageScriptPlugin;

        // 当前编辑目标过程（独立于"运行过程"，允许编辑非运行过程）
        private EProcedure _editTarget;

        private bool _initialized;   // Loaded 完成前置 false，屏蔽初始化期事件
        private bool _loadingText;   // 程序写 Editor.Text 时置 true，避免回写 Body 死循环

        public ImageScriptView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 语法高亮（关键字来自 Keyword.cs；接口变量在切换过程时动态并入）
            Editor.SyntaxHighlighting = HalconHighlighting.GetDefinition();

            // 算子智能提示：补全 + 参数模板 + 悬浮文档（接口变量数据源=当前编辑过程）
            ScriptIntelliSense.Attach(Editor, CollectInterfaceVars);

            // 编辑行为：撤销/自动缩进/括号配对/注释切换/缩放/当前行高亮/查找替换（Halcon 行注释前缀 *）
            ScriptEditorBehavior.Attach(Editor, "*");

            // 右键菜单：编译 / 注释 / 取消注释 / 插入示例代码（28 个经典场景）
            BuildEditorContextMenu();

            // 运行过程下拉反映持久化的 SelectedProcedure
            BindingRunCombo();

            RefreshLists();
            _initialized = true;

            // 默认把编辑目标对准运行过程
            if (EditProcCombo.Items.Count > 0)
            {
                EditProcCombo.SelectedIndex = EditProcCombo.Items.IndexOf(Plugin.CurrentProcedure) >= 0
                    ? EditProcCombo.Items.IndexOf(Plugin.CurrentProcedure)
                    : 0;
            }
            else
            {
                LoadEditorText(null);
            }
        }

        #region 列表刷新

        private void RefreshLists()
        {
            if (Plugin == null) return;

            EditProcCombo.ItemsSource = Plugin.Procedures;
            EditProcCombo.DisplayMemberPath = "Name";
            RunProcCombo.ItemsSource = Plugin.RunProcedureNameList;
            RunProcCombo.SelectedItem = Plugin.SelectedProcedure;

            InputList.ItemsSource = Plugin.InputVars;
            OutputList.ItemsSource = Plugin.OutputVars;

            InputEmptyHint.Visibility = (Plugin.InputVars == null || Plugin.InputVars.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
            OutputEmptyHint.Visibility = (Plugin.OutputVars == null || Plugin.OutputVars.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BindingRunCombo()
        {
            // SelectedProcedure 变化（如导入后自动选中）时回显
            RunProcCombo.SelectedItem = Plugin?.SelectedProcedure;
        }

        #endregion

        #region 编辑器 ↔ 过程体

        private void EditProcCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            LoadEditorText(EditProcCombo.SelectedItem as EProcedure);
        }

        private void LoadEditorText(EProcedure proc)
        {
            _editTarget = proc;
            _loadingText = true;
            Editor.Text = proc?.Body ?? "";
            _loadingText = false;
            EditorTitle.Text = proc != null
                ? proc.GetProcedureMethod()
                : "（未选择过程）";
            UpdateEditorIntel();
        }

        /// <summary>把当前编辑过程的接口变量并入高亮规则（紫色 Variable 色）。</summary>
        private void UpdateEditorIntel()
        {
            Editor.SyntaxHighlighting = HalconHighlighting.GetDefinition(
                CollectInterfaceVars().Select(v => v.Name));
        }

        /// <summary>收集当前编辑过程的接口变量（名称/方向/类型），供补全与悬浮使用。</summary>
        private IList<ScriptIntelliSense.VarInfo> CollectInterfaceVars()
        {
            var list = new List<ScriptIntelliSense.VarInfo>();
            var proc = _editTarget;
            if (proc == null) return list;

            foreach (var n in proc.IconicInputList.Concat(proc.CtrlInputList))
                list.Add(new ScriptIntelliSense.VarInfo
                {
                    Name = n,
                    Kind = "输入",
                    Type = Plugin?.InputVars?.FirstOrDefault(v => v.Name == n)?.Type.ToString() ?? "接口参数",
                });
            foreach (var n in proc.IconicOutputList.Concat(proc.CtrlOutputList))
                list.Add(new ScriptIntelliSense.VarInfo
                {
                    Name = n,
                    Kind = "输出",
                    Type = Plugin?.OutputVars?.FirstOrDefault(v => v.Name == n)?.Type.ToString() ?? "接口参数",
                });
            return list;
        }

        private void Editor_TextChanged(object sender, EventArgs e)
        {
            if (_loadingText || _editTarget == null) return;
            // 直接写回内存模型；执行时按内容指纹决定是否重编译
            _editTarget.Body = Editor.Text;
        }

        #endregion

        #region 运行过程

        private void RunProcCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || Plugin == null) return;
            if (RunProcCombo.SelectedItem is string name)
                Plugin.SelectedProcedure = name;
        }

        #endregion

        // 变量类型变更由 ScriptVarDef.Type 的 PropertyChanged → 插件重建端口 → PortsVersion 锚点
        // 驱动行内 Port MultiBinding 自动重解析（无 SelectionChanged 事件回环）

        #region 过程操作

        private void NewProc_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            int n = 1;
            string baseName = "script";
            string name = baseName;
            while (Plugin.Procedures.Any(p => p.Name == name))
                name = $"{baseName}{++n}";

            var proc = new EProcedure
            {
                Name = name,
                Body = $"* {name}\r\n* 在此编写 Halcon 过程脚本\r\n\r\n"
            };
            Plugin.Procedures.Add(proc);
            Plugin.Procedures = Plugin.Procedures; // 触发通知/选中同步
            RefreshLists();
            EditProcCombo.SelectedItem = proc;
        }

        private void DeleteProc_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            var proc = EditProcCombo.SelectedItem as EProcedure;
            if (proc == null) return;

            if (MessageBox.Show($"确定删除过程 [{proc.Name}]？", "确认",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            Plugin.Procedures.Remove(proc);
            Plugin.Procedures = Plugin.Procedures;
            Plugin.RebuildVariablesFromSelectedProcedure();
            RefreshLists();
            if (EditProcCombo.Items.Count > 0)
                EditProcCombo.SelectedIndex = 0;
            else
                LoadEditorText(null);
        }

        private void SyncVars_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;
            Plugin.RebuildVariablesFromSelectedProcedure();
            RefreshLists();
            UpdateEditorIntel();
        }

        /// <summary>
        /// 恢复默认脚本：用插件自带的「1 个图像进 → 1 个图像出 + OK/NG 结论」整表替换现有过程。
        /// 会清掉现有脚本，所以先弹确认框；替换动作与"首次打开自动预置"共用插件层的同一份定义。
        /// </summary>
        private void RestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            int count = Plugin.Procedures?.Count ?? 0;
            if (count > 0 &&
                MessageBox.Show(
                    $"将清空当前 {count} 个脚本过程，替换为插件自带的默认脚本。\n\n" +
                    "方案未保存前关闭本窗口即可放弃这次修改。\n\n确定继续？",
                    "恢复默认脚本", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK)
                return;

            Plugin.ApplyDefaultScript();

            RefreshLists();
            // 不靠下拉的 SelectionChanged 刷编辑器：它只在"选中项真的变了"时才触发，
            // 而替换后第 0 项仍是过程（只是换了实例），编辑器会停在旧文本上
            EditProcCombo.SelectedItem = Plugin.CurrentProcedure;
            LoadEditorText(Plugin.CurrentProcedure);
        }

        /// <summary>校验脚本：强制重编译全部过程，结果显示在编辑器底部并标红错误行（不执行）。</summary>
        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            // 固化编辑器当前文本，确保校验的是屏幕上的内容
            if (_editTarget != null) _editTarget.Body = Editor.Text;

            string err = Plugin.ValidateScript();
            Plugin.ValidationResult = err == null ? "✔ 脚本校验通过（编译无误）" : "✘ " + err;
            // 校验时接口已按变量表反向同步，刷新顶部签名行显示
            if (_editTarget != null) EditorTitle.Text = _editTarget.GetProcedureMethod();
            ValidateResultText.Foreground = err == null
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32))
                : System.Windows.Media.Brushes.Red;

            // 解析错误行号 → 编辑器标红 + 跳到首个错误行
            var errorLines = err == null
                ? null
                : ParseErrorLines(err, Editor.Text);
            ScriptEditorBehavior.SetErrorLines(Editor, errorLines);
            if (errorLines != null && errorLines.Count > 0)
            {
                int line = errorLines[0];
                Editor.TextArea.Caret.Line = line;
                Editor.ScrollToLine(line);
            }
        }

        /// <summary>
        /// 从编译错误文本提取行号并换算成编辑器真实行号。
        /// 探针标定：HDevEngine 报的 N 是"第 N 个非空行"（数行时跳过空行），
        /// 因此在正文里找到第 N 个非空行的实际位置返回。
        /// </summary>
        private static System.Collections.Generic.List<int> ParseErrorLines(string err, string bodyText)
        {
            var list = new System.Collections.Generic.List<int>();
            var lines = (bodyText ?? "").Replace("\r", "").Split('\n');

            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(err, @"(?:program line|procedure call):\s*(\d{1,4})"))
            {
                if (!int.TryParse(m.Groups[1].Value, out int nonEmptyNo) || nonEmptyNo < 1) continue;
                int seen = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim().Length == 0) continue;   // 引擎跳过空行
                    seen++;
                    if (seen == nonEmptyNo && !list.Contains(i + 1)) { list.Add(i + 1); break; }
                }
            }
            return list;
        }

        /// <summary>格式化当前过程体（只调空白不改语义，可 Ctrl+Z 撤销）。</summary>
        private void Format_Click(object sender, RoutedEventArgs e)
        {
            if (_editTarget == null) return;

            string formatted = ScriptFormatter.Format(Editor.Text);
            if (formatted == Editor.Text) return;

            // 整篇替换走 Document.Replace：保留撤销栈，一次 Ctrl+Z 可回退
            Editor.Document.Replace(0, Editor.Document.TextLength, formatted);
            // TextChanged 已把新文本写回 _editTarget.Body
        }

        /// <summary>
        /// 编辑器右键菜单：编译 / 注释 / 取消注释 / 插入示例代码（按 8 大类分组的子菜单）。
        /// 菜单在代码里构建（28 项模板静态写 XAML 太冗长），属视图层职责，不违反 MVVM。
        /// </summary>
        private void BuildEditorContextMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu();

            menu.Items.Add(MenuItem("编译（校验脚本）", "检查语法/引用错误并标红错误行",
                (s, e) => Validate_Click(s, e)));
            menu.Items.Add(MenuItem("注释", "Ctrl+/ —— 给当前行/选区加 * 注释",
                (s, e) => ScriptEditorBehavior.SetComment(Editor.TextArea, true, "*")));
            menu.Items.Add(MenuItem("取消注释", "去掉当前行/选区的 * 注释",
                (s, e) => ScriptEditorBehavior.SetComment(Editor.TextArea, false, "*")));
            menu.Items.Add(new System.Windows.Controls.Separator());

            var tplRoot = new System.Windows.Controls.MenuItem { Header = "插入示例代码" };
            foreach (var grp in System.Linq.Enumerable.GroupBy(
                     ScriptTemplates.All, t => t.Category))
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

        /// <summary>把模板整段插入到光标所在行的下方（不与现有代码粘连），可一次 Ctrl+Z 撤销。</summary>
        private void InsertTemplate(string code)
        {
            // {A} 占位符 → 程序目录下 ScriptAssets 的绝对路径（Halcon 路径用正斜杠）
            string assets = System.IO.Path
                .Combine(AppDomain.CurrentDomain.BaseDirectory, "ScriptAssets")
                .Replace('\\', '/');
            try
            {
                // 模板会往这两个子目录写：模型存盘/NG留档，先确保存在
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "models"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "ng_records"));
            }
            catch { /* 目录已存在或无权限不影响插入 */ }

            code = code.Replace("{A}", assets);

            var doc = Editor.Document;
            var line = doc.GetLineByOffset(Editor.TextArea.Caret.Offset);

            string block = code.Replace("\r\n", "\n").Replace('\r', '\n');
            int insertAt = line.Offset + line.Length;
            string prefix = line.Length > 0 ? "\n" : "";

            doc.Insert(insertAt, prefix + block);
            Editor.TextArea.Caret.Offset = insertAt + prefix.Length;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "导入 Halcon 脚本",
                Filter = "HDevelop 文件 (*.hdev)|*.hdev|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var list = EProcedure.LoadXmlByFile(dlg.FileName);
                if (list == null || list.Count == 0)
                {
                    MessageBox.Show("导入失败：文件为空或不是有效的 .hdev。", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Plugin.Procedures = list;
                Plugin.RebuildVariablesFromSelectedProcedure();
                RefreshLists();

                // 优先把运行/编辑过程指向 main（若存在）
                var main = list.FirstOrDefault(p => p.Name == "main");
                if (main != null && Plugin.RunProcedureNameList.Contains("main") == false)
                    Plugin.SelectedProcedure = list.FirstOrDefault(p => p.Name != "main")?.Name
                                               ?? main.Name;
                else if (Plugin.RunProcedureNameList.Count > 0)
                    Plugin.SelectedProcedure = Plugin.RunProcedureNameList[0];

                RunProcCombo.SelectedItem = Plugin.SelectedProcedure;
                LoadEditorText(Plugin.CurrentProcedure ?? main ?? list[0]);
                if (EditProcCombo.Items.Count > 0)
                    EditProcCombo.SelectedItem = Plugin.CurrentProcedure ?? list[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show("导入出错：" + ex.Message, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (Plugin == null || Plugin.Procedures == null || Plugin.Procedures.Count == 0)
            {
                MessageBox.Show("当前没有可导出的脚本过程。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "导出 Halcon 脚本",
                Filter = "HDevelop 文件 (*.hdev)|*.hdev",
                FileName = "vm_script.hdev"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // 导出前把编辑器当前文本固化回内存模型，并同步接口列表
                if (_editTarget != null) _editTarget.Body = Editor.Text;
                Plugin.SyncInterfaceForExport();
                EProcedure.SaveToFile(dlg.FileName, Plugin.Procedures);
                MessageBox.Show("导出成功。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出出错：" + ex.Message, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
