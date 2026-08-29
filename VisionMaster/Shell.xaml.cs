using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UI.Models;
using VisionMaster.Services;
using VisionMaster.Views;

namespace VisionMaster
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Shell : Window
    {
        public Shell()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 窗口渲染完成后恢复上次的布局（文件不存在或损坏时使用 XAML 默认布局），
        /// 随后按软件配置自动加载默认启动方案
        /// </summary>
        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            LayoutHelper.Load();

            if (DataContext is ShellViewModel vm)
            {
                await vm.AutoLoadStartupSolutionAsync();
            }
        }

        /// <summary>
        /// 关闭窗口时自动保存布局
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            LayoutHelper.Save();
        }
    }

}