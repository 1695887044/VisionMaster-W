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
            // 语法高亮（关键字来自 Keyword.cs）
            Editor.SyntaxHighlighting = HalconHighlighting.GetDefinition();

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

        #region 变量类型变更 → 端口按新类型重建

        private void VarType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || Plugin == null) return;
            Plugin.NotifyVariablesChanged();
            // 端口对象已更换，刷新连线列让 LinkableValueEditor 重新解析 Port
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InputList.Items.Refresh();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        #endregion

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
                // 导出前把编辑器当前文本固化回内存模型
                if (_editTarget != null) _editTarget.Body = Editor.Text;
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
