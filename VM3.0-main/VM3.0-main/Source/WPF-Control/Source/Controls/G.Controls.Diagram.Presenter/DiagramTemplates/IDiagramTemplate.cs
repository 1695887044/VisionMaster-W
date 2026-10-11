namespace G.Controls.Diagram.Presenter.DiagramTemplates;

public interface IDiagramTemplate : INameable, IGroupable
{
    public IDiagramData Diagram { get; set; }
}
