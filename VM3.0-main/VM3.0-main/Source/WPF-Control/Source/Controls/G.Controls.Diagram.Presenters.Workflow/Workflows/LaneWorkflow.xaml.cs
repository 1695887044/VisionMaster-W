namespace G.Controls.Diagram.Presenters.Workflow.Workflows;

//[Display()]
[Display(Name = "泳道图", GroupName = "流程图", Order = 1)]
public class LaneWorkflow : WorkflowBase
{
    public LaneWorkflow()
    {
        this.Layout = new LaneLayout();
    }
}
