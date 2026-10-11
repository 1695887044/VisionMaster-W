namespace G.Services.Common.MainWindow;

public interface IMainWindowSavableService : ISplashSave
{
    void Load(Window window);
}
