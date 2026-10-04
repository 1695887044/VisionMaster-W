using System.Windows;

namespace _MvvmDemo
{
    /// <summary>
    /// 纯 MVVM:代码后台没有一行业务逻辑 —— 数据、命令、绑定、清理
    /// 全部在 MainViewModel / XAML 与 App 生命周期里,这里只负责加载界面。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
