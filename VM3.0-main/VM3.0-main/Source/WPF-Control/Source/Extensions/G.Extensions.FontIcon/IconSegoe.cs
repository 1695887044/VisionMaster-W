using G.Mvvm.Commands;
using G.Mvvm.ViewModels.Base;
using System.Windows;

namespace G.Extensions.FontIcon;

public class IconSegoe : BindableBase
{
    public int CodePoint { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
    public string CodeKey { get; set; }
    public RelayCommand CopyCommand => new RelayCommand(x =>
    {
        Clipboard.SetText(this.Key);
    });

    public RelayCommand CopyCodeKeyCommand => new RelayCommand(x =>
    {
        Clipboard.SetText(this.CodeKey);
    });
}
