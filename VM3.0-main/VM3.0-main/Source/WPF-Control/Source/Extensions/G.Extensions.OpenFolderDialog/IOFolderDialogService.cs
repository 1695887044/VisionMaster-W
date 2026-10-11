using G.Services.Message.IODialog;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace G.Extensions.OpenFolderDialog;

public class IOFolderDialogService : IIOFolderDialogService
{
    public string ShowOpenFolder(string title = "打开文件夹")
    {
        CommonOpenFileDialog folderDialog = new CommonOpenFileDialog();
        folderDialog.Title = title;
        folderDialog.IsFolderPicker = true;
        if (folderDialog.ShowDialog() != CommonFileDialogResult.Ok)
            return null;
        return folderDialog.FileName;
    }
}
