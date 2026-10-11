namespace G.Controls.Diagram.Presenter.Flowables;

public class FlowableResult : IFlowableResult
{
    public FlowableResult(string message)
    {
        this.Message = message;
    }
    public string Message { get; }

    public FlowableResultState State { get; set; }


    public static FlowableResult Break { get; } = new FlowableResult("阻止流程") { State = FlowableResultState.Break };
    public static FlowableResult OK { get; } = new FlowableResult("运行成功") { State = FlowableResultState.OK };
    public static FlowableResult Error { get; } = new FlowableResult("运行错误") { State = FlowableResultState.Error };

}

public class FlowableResult<T> : FlowableResult
{
    public FlowableResult(T t) : base(null)
    {
        this.State = FlowableResultState.OK;
        this.Value = t;
    }
    public FlowableResult(T t, string message) : base(message)
    {
        this.State = FlowableResultState.OK;
        this.Value = t;
    }
    public T Value { get; set; }
}
