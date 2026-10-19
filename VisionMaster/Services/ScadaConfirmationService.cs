using System.Windows;
using UI.CustomControl;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 轴控台「示教 / 定位」二次确认的宿主实现：走 <see cref="EasyDialog"/> 的模态提示框。
    ///
    /// 为什么放在宿主而不是控件库：控件库（<c>VM.Scada.Controls</c>）不认识 UI 主题与弹窗引擎，
    /// 它只认 <see cref="IScadaConfirmationService"/>；断言工程则换一个假实现，
    /// 在无窗口环境里把"确认 / 取消"两条路都跑一遍。
    /// </summary>
    public sealed class ScadaConfirmationService : IScadaConfirmationService
    {
        /// <inheritdoc />
        public bool Confirm(string title, string message)
        {
            // 无 WPF 应用上下文（断言宿主 / 设计器 / 已关机）：没有弹窗可弹。
            // 返回 false 让图元如实拒绝执行——静默放行等于把防误触开关悄悄关掉。
            if (Application.Current == null)
                return false;

            // 示教/定位来自按钮点击，本来就在 UI 线程；这里再兜一层，
            // 将来若有脚本/后台路径调用也不会跨线程碰控件。
            if (Application.Current.Dispatcher.CheckAccess())
                return EasyDialog.ShowSync(title, message);

            return Application.Current.Dispatcher.Invoke(
                () => EasyDialog.ShowSync(title, message));
        }
    }
}
