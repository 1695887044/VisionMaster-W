namespace Core.Interfaces
{
    /// <summary>
    /// 用户通知抽象（UI 弹泡/状态栏等）：
    /// 引擎等非 UI 层通过此接口提醒用户，禁止直接依赖 UI 库。
    /// </summary>
    public interface IUserNotifier
    {
        void ShowInfo(string message);
        void ShowWarn(string message);
        void ShowError(string message);
    }
}
