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
            if (!LayoutHelper.Load())
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
