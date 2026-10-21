using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「编辑 / 写入数组」弹窗。
    /// <para>键盘：Esc = 取消（写入中不响应）；Ctrl+V = 多行/制表符文本按行铺到列表
    /// （单值粘贴仍交给 TextBox，保留光标位置）。</para>
    /// </summary>
    public partial class ArrayEditorView : UserControl
    {
        public ArrayEditorView()
        {
            InitializeComponent();

            // 无系统边框：Esc 是唯一的键盘取消入口。
            // 不用 InputBindings：焦点在 TextBox 里时命令绑定不可靠，Preview 隧道事件最稳。
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ArrayEditorViewModel vm) return;

            if (e.Key == Key.Escape)
            {
                if (!vm.CanCancel) return;
                vm.CancelCommand.Execute();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (!vm.CanCancel) return;

                string text = ReadClipboard();
                if (string.IsNullOrEmpty(text)) return;

                // 单值（含 Excel 复制单格常带的尾换行）交给 TextBox 自己处理；
                // 只有真正的多行/制表符文本才按行铺到列表。
                string trimmed = text.TrimEnd('\r', '\n');
                if (!trimmed.Contains('\n') && !trimmed.Contains('\r') && !trimmed.Contains('\t')) return;

                vm.Paste(text, ElementList.SelectedItem as ArrayEditorRow);
                e.Handled = true;
            }
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ArrayEditorViewModel vm) return;
            if (!vm.CanCancel) return;

            string text = ReadClipboard();
            if (string.IsNullOrEmpty(text)) return;

            vm.Paste(text, ElementList.SelectedItem as ArrayEditorRow);
        }

        /// <summary>剪贴板可能被其它进程占用（打开剪贴板历史/远程桌面时常见），读失败按空处理</summary>
        private static string ReadClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
