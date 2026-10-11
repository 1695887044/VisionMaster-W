namespace G.Services.Message.IODialog;

public interface IIOSaveFileDialogOption : IIOFileDialogOption
{
    string DefaultExt { get; set; }
    string DefaultFileName { get; set; }
}

