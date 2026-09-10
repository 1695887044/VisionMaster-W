using Core.Interfaces;
using UI.CustomControl;

namespace VisionMaster.Services
{
    /// <summary>
    /// IUserNotifier 默认实现：包装 UI 库的 Notifier 弹泡。
    /// 引擎等非 UI 层只依赖接口，未来可替换为状态栏提示/无提示等实现。
    /// </summary>
    public class UserNotifier : IUserNotifier
    {
        public void ShowInfo(string message) => Notifier.ShowInfo(message);
        public void ShowWarn(string message) => Notifier.ShowWarning(message);
        public void ShowError(string message) => Notifier.ShowError(message);
    }
}
