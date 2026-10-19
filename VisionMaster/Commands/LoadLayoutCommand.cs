using System.Windows;
using System.Windows.Input;
using UI.Core;
using VisionMaster.Services;

namespace VisionMaster.Commands
{
    /// <summary>
    /// 加载 AvalonDock 布局命令（工具菜单"加载布局"）
    /// </summary>
    internal class LoadLayoutCommand : MarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            // 传 false：这是"加载用户布局"，此刻内存里是用户当前布局，
            // 不能把它当成出厂默认写进 DefaultLayout.xml（出厂快照只由启动路径刷新）。
            if (!LayoutHelper.Load(refreshDefaultSnapshot: false))
            {
                MessageBox.Show(
                    "没有可加载的布局文件，或布局文件已损坏。",
                    "加载布局",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
