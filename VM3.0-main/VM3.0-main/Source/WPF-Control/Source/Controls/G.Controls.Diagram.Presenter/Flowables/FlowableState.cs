namespace G.Controls.Diagram.Presenter.Flowables;

public enum FlowableState
{
    Ready = 0,
    Wait,
    Running,
    Success,
    Error,
    Canceling,
    Canceled,
    Stopped,
    Pause,
    Break
}
