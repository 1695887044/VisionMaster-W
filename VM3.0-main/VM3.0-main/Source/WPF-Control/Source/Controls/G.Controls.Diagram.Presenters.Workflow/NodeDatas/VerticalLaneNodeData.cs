namespace G.Controls.Diagram.Presenters.Workflow.NodeDatas;

[Display(Name = "垂直泳道", GroupName = "泳道图形状", Order = 0, Description = "垂直泳道")]

public class VerticalLaneNodeData : LaneNodeDataBase
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.Width = 200;
        this.Height = 800;
    }

    protected override Geometry GetGeometry()
    {
        return Geometry.Empty;
    }
}
