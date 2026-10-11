using G.Common.Attributes;
using G.Common.Commands;
using G.Services.Message;
using G.Services.Message.Dialog;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.License
{
    [Icon("\xE713")]
    [Display(Name = "会员", Description = "显示会员页面")]
    public class ShowVipCommand : DisplayMarkupCommandBase
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
            await IocMessage.Dialog.Show(new LicenseViewPresenter() { UseVip = true }, x =>
            {
                x.Title = "许可和会员";
                x.DialogButton = DialogButton.None;
            });
        }
    }
}