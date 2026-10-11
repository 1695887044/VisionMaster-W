namespace G.Services.Message.Snack;

public class ShowProgressSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Func<IPercentSnackItem, bool> action = x =>
            {
                for (int i = 0; i < 100; i++)
                {
                    x.Value = i;
                    x.Message = $"{x.Value}/100";
                    Thread.Sleep(20);
                }
                x.Value = 100;
                x.Message = $"{x.Value}/100";
                Thread.Sleep(1000);
                return true;
            };
        Ioc<ISnackMessageService>.Instance.ShowProgress(action);
    }
}
