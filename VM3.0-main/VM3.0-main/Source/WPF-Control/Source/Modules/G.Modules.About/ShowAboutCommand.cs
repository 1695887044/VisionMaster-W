using G.Common.Attributes;
using G.Services.Common.About;
using G.Services.Message.Dialog.Commands;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.About;

[Icon("\xE946")]
[Display(Name = "关于", Description = "显示关于页面")]
public class ShowAboutCommand : ShowIocPresenterCommandBase<IAboutViewPresenter>
{

}