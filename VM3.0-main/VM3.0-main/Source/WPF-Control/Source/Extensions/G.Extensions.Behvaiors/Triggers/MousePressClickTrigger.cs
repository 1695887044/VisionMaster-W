namespace G.Extensions.Behvaiors.Triggers;

/// <summary>
/// 按住一定时间间隔才会执行
/// </summary>
public class MousePressClickTrigger : MousePressTrigger
{
    public MousePressClickTrigger()
    {
        this.UseInvokeOnDown = false;
    }
    protected override void ElapsedInvokeActions()
    {
        this.InvokeActions(null);
        Stop();
    }
}
