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
            ApplyDockTheme();
        }

        /// <summary>
        /// 应用 AvalonDock 停靠主题：VS2013Light 控件模板 + Fluent Light 调色板画刷覆盖
        /// （切深色主题时把 Source 换成 DarkTheme.xaml 即可，模板复用同一套）
        /// </summary>
        private void ApplyDockTheme()
        {
            dockManager.Theme = new FluentLightTheme();
        }

        /// <summary>
        /// DictionaryTheme 是抽象类，需派生具体主题传入自定义字典
        /// </summary>
        private sealed class FluentLightTheme : AvalonDock.Themes.DictionaryTheme
        {
            public FluentLightTheme()
                : base(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/VisionMaster;component/Themes/DockThemes/FluentLight.xaml")
                })
            {
            }
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