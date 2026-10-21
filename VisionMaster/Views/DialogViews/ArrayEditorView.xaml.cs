using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// ArrayEditorView.xaml 的交互逻辑。
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
            if (e.Key != Key.Escape) return;
            if (DataContext is not ArrayEditorViewModel vm) return;

            vm.CancelCommand.Execute();
            e.Handled = true;
        }
    }
}
