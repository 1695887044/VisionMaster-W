using System.Windows;
using System.Windows.Input;
using UI.Core;
using VisionMaster.Services;

namespace VisionMaster.Commands
{
    /// <summary>
    /// 保存 AvalonDock 布局命令（工具菜单"保存布局"）
    /// </summary>
    internal class SaveLayoutCommand : MarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            LayoutHelper.Save();
        }
    }
}
