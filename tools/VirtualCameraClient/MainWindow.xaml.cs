using System.Windows;

namespace VirtualCameraClient
{
    /// <summary>
    /// 主窗口（View 层）。只做三件事：绑定 ViewModel、注入"文件夹选择"委托、管窗口生命周期。
    /// 所有业务逻辑都在 MainViewModel 里，这里保持"薄"。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();

            // 弹系统文件夹对话框是纯 UI 行为，放在 View 里实现；
            // ViewModel 只拿一个"返回路径"的委托，从而不依赖任何对话框类型（保持可测试）。
            _vm.FolderPicker = ShowFolderDialog;
            DataContext = _vm;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 窗口一显示就启动心跳（心跳独立于推图开关持续发送）
            _vm.Start();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            // 关窗时停推图、取消心跳、释放 HttpClient
            _vm.Dispose();
        }

        private string ShowFolderDialog()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择要推送的图片文件夹",
                Multiselect = false
            };

            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
    }
}
