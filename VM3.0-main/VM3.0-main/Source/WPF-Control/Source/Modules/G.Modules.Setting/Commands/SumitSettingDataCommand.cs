namespace G.Modules.Setting.Commands;

public class SumitSettingDataCommand : DialogCommandBase
{
    public override void Execute(object parameter)
    {
        IocSetting.Instance.Save(out string message);
        this.Sumit(parameter);
    }
}
