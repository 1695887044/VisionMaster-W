namespace G.Services.Message.Notify;

public interface ISystemNotifyMessage
{
    void Show(string message, string title = null, NotifyBalloonIcon tipIcon = NotifyBalloonIcon.Info, int timeout = 1000);
}
