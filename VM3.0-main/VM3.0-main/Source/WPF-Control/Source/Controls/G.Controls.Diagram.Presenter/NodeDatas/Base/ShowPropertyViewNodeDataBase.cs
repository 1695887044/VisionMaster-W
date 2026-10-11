namespace G.Controls.Diagram.Presenter.NodeDatas.Base;

public abstract class ShowPropertyViewNodeDataBase : DiagramableNodeDataBase, IDiagramShowPropertyView
{
    public virtual object GetPropertyPresenter()
    {
        return this;
    }
}
