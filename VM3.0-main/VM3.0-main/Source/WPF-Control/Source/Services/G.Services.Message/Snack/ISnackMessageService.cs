namespace G.Services.Message.Snack;

public interface ISnackMessageService
{
    Task<bool?> ShowDialog(string message);
    void ShowError(string message);
    void ShowFatal(string message);
    void ShowInfo(string message);
    void Show(ISnackItem message);
    Task<T> ShowProgress<T>(Func<IPercentSnackItem, T> action);
    Task<T> ShowString<T>(Func<ISnackItem, T> action);
    void ShowSuccess(string message);
    void ShowWarn(string message);
}
