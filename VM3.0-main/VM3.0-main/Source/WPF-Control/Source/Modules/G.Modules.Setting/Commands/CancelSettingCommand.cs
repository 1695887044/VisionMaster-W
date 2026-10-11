namespace G.Modules.Setting.Commands;

[Icon("\xE77F")]
[Display(Name = "取消", Description = "取消保存并系统设置页面")]
public class CancelSettingCommand : DisplayMarkupCommandBase
{
    public override void Execute(object parameter)
    {
        IocSetting.Instance.Cancel();
    }
}
