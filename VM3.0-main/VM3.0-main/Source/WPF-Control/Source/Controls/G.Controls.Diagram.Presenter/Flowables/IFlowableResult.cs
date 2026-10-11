// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Controls.Diagram.Presenter.Flowables;

public interface IFlowableResult
{
    string Message { get; }
    FlowableResultState State { get; set; }
}

