using System.Windows;
using System.Windows.Input;
using UI.Core;
using VisionMaster.Services;

namespace VisionMaster.Commands
{
    /// <summary>
    /// 恢复默认布局命令：立即恢复出厂默认布局（无需重启），同时清除用户布局文件
    /// </summary>
    internal class ResetLayoutCommand : MarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            if (LayoutHelper.Reset())
            {
                MessageBox.Show(
                    "已恢复默认布局。",
                    "恢复默认布局",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
