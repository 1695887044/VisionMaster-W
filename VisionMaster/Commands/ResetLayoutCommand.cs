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
            // 先确认：这是会丢弃用户面板位置/大小/显隐的破坏性操作（与"加载布局"不同，后者可取消）。
            var confirm = MessageBox.Show(
                "恢复默认布局会丢弃当前面板的位置、大小与显示状态。\n方案里保存的布局不受影响。\n\n确定继续？",
                "恢复默认布局",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (confirm != MessageBoxResult.OK)
                return;

            if (LayoutHelper.Reset())
            {
                MessageBox.Show(
                    "已恢复默认布局。",
                    "恢复默认布局",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                // Reset 在出厂快照缺失/过期时会删掉快照与用户布局，但当前会话无法立即重建布局
                // （XAML 默认布局只在启动时存在）。如实告知"重启后生效"，不要静默无反应。
                MessageBox.Show(
                    "已清除自定义布局。出厂默认快照不可用，重启后将按软件默认布局显示。",
                    "恢复默认布局",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
