using G.Common.Commands;
namespace G.Controls.Diagram;

public class RemoveNodeCommand : MarkupCommandBase
{
    public override void Execute(object parameter)
    {
        if (parameter is Node node)
            node.Delete();

        if (parameter is ContextMenu menu)
        {
            menu.PlacementTarget.GetParent<Node>()?.Delete();
        }
    }
}
