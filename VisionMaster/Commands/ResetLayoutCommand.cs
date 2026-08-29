using System.Windows;
using System.Windows.Input;
using UI.Core;
using VisionMaster.Services;

namespace VisionMaster.Commands
{
    /// <summary>
    /// 加载默认布局命令：删除布局文件，重启软件后恢复 XAML 内置默认布局
    /// </summary>
    internal class ResetLayoutCommand : MarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            if (LayoutHelper.Reset())
            {
                MessageBox.Show(
                    "已恢复默认布局，请重启软件生效。",
                    "加载默认布局",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
