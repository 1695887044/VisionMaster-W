global using System.Windows.Media;
using System.Windows.Shell;

namespace G.Services.Message.TaskBar;

public interface ITaskBarMessage
{
    void Show(Action<TaskbarItemInfo> action);
    void ShowImage(ImageSource image);
    void ShowNormal(Action<TaskbarItemInfo> action);
    Task ShowPercent(Action<TaskbarItemInfo> action);
    Task<bool> ShowWaitting(Func<bool> action);
}
