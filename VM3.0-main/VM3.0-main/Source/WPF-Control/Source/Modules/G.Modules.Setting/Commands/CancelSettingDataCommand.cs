global using G.Services.Message;
global using G.Services.Message.Dialog;
global using G.Services.Setting;

namespace G.Modules.Setting.Commands;

public class CancelSettingDataCommand : DialogCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        var r = await IocMessage.ShowDialogMessage("取消配置将不会保存，是否继续？");
        if (r == false)
            return;
        IocSetting.Instance.Load(null, out string message);
        this.Cancel(parameter);
    }
}
