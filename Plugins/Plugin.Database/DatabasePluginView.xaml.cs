using System.Windows;
using System.Windows.Controls;

namespace Plugin.Database
{
    /// <summary>
    /// 数据库三步共用的配置视图（内容区）。
    ///
    /// 只有一件交互需要代码：测试按钮。走 <see cref="DatabasePluginBase.TestAsync"/>，
    /// 默认事务内执行后回滚，结果只上屏、不抛异常（测试失败不该把配置窗口带崩）。
    /// 其余全部是绑定：profile/语句下拉、参数行（LinkableValueEditor）、输出行、banner。
    /// </summary>
    public partial class DatabasePluginView : UserControl
    {
        public DatabasePluginView()
        {
            InitializeComponent();
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is DatabasePluginBase plugin)
                await plugin.TestAsync();
        }
    }
}
