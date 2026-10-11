using G.Common.Attributes;
using G.Common.Commands;
using G.Services.Message;
using G.Services.Message.Dialog;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.License
{
    [Icon("\xE72E")]
    [Display(Name = "许可证书", Description = "显示产品许可注册页面")]
    public class ShowLicenseCommand : DisplayMarkupCommandBase
    {
        public override bool CanExecute(object parameter)
        {
            return Ioc<ILicenseService>.Instance != null;
        }
        public override async void Execute(object parameter)
        {
            var option = LicenseProxy.Instance.IsVail(out string error);
            if (option == null)
            {
                var r = await IocMessage.Dialog.Show(error);
                if (r != true)
                    return;
            }
            await IocMessage.Dialog.Show(new LicenseViewPresenter(), x =>
            {
                x.Title = "许可";
                x.DialogButton = DialogButton.None;
            });
        }
    }
}