using System;
using System.Windows;
using System.Windows.Input;
using UI.Core;
using VisionMaster.Services;

namespace VisionMaster.Commands
{
    /// <summary>
    /// 视图菜单命令：按 ContentId 激活对应面板（隐藏的恢复显示、被遮挡的切到前台）
    /// 用法：Command="{commands:ShowPanelCommand}" CommandParameter="Panel_ToolView"
    /// </summary>
    internal class ShowPanelCommand : MarkupCommandBase
    {
        public override void Execute(object parameter)
        {
            if (parameter is string contentId)
            {
                LayoutHelper.ShowPanel(contentId);
            }
        }
    }
}
