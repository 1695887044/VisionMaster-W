namespace G.Services.Message.IODialog;
public interface IIOFolderDialogService
{
    string ShowOpenFolder(string title = "打开文件夹");
}

public static class IOFolderDialogServiceExtension
{
    public static string ShowOpenFolderAction(this IIOFolderDialogService service, Action<string> sumitAction)
    {
        string s = service.ShowOpenFolder();
        if (s == null)
            return null;
        sumitAction?.Invoke(s);
        return s;
    }

}

