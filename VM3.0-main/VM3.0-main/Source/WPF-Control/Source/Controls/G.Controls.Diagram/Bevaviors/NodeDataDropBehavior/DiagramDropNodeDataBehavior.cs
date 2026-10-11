namespace G.Controls.Diagram.Bevaviors.NodeDataDropBehavior;

public class DiagramDropNodeDataBehavior : DropNodeDataBehaviorBase<Diagram>
{
    public bool UseAutoAddLinkOnEnd
    {
        get { return (bool)GetValue(UseAutoAddLinkOnEndProperty); }
        set { SetValue(UseAutoAddLinkOnEndProperty, value); }
    }

    public static readonly DependencyProperty UseAutoAddLinkOnEndProperty =
        DependencyProperty.Register("UseAutoAddLinkOnEnd", typeof(bool), typeof(DiagramDropNodeDataBehavior), new FrameworkPropertyMetadata(true));


    protected override void OnDropNodeData(INodeData nodeData, Point offset, Point location)
    {
        Node node = this.CreateNodeByData(nodeData);
        node.Content = nodeData;
        node.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        node.Location = new Point(location.X - offset.X + node.DesiredSize.Width / 2, location.Y - offset.Y + node.DesiredSize.Height / 2);
        //collection.Add(node);
        //this.AssociatedObject.RefreshData();
        this.AssociatedObject.AddNode(node);
        if (this.UseAutoAddLinkOnEnd)
            this.AssociatedObject.LinkOnEnd(node);
    }
}
