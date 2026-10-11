namespace G.Controls.Diagram.Bevaviors.NodeDataDropBehavior;

public class DiagramDropTypeNodeDataBehavior : DiagramDropNodeDataBehavior
{
    public Type NodeType
    {
        get { return (Type)GetValue(NodeTypeProperty); }
        set { SetValue(NodeTypeProperty, value); }
    }

    public static readonly DependencyProperty NodeTypeProperty =
        DependencyProperty.Register("NodeType", typeof(Type), typeof(DiagramDropTypeNodeDataBehavior), new FrameworkPropertyMetadata(typeof(Node), (d, e) =>
        {
            DiagramDropTypeNodeDataBehavior control = d as DiagramDropTypeNodeDataBehavior;

            if (control == null) return;

            if (e.OldValue is Type o)
            {

            }

            if (e.NewValue is Type n)
            {

            }

        }));

    protected override Node CreateNode()
    {
        return Activator.CreateInstance(this.NodeType) as Node; ;
    }
}
