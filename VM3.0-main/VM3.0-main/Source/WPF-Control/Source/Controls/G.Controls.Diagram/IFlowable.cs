namespace G.Controls.Diagram;

public interface IFlowable : IDisposable, IMessageable, IStopwatchable
{
    bool UseInfoLogger { get; set; }
    void Clear();
}
