using G.Mvvm.ViewModels.Base;

namespace G.Presenters.Common;

/// <summary>
/// 显示并绑定CommandsBindableBase中的命令
/// </summary>
[Icon("\xEDE3")]
public class CommandsBindablePresenter : BindableBase
{
    public CommandsBindablePresenter(CommandsBindableBase presenter)
    {
        this.Presenter = presenter;
    }
    public CommandsBindableBase Presenter { get; set; }
}
