using G.Controls.Diagram.Presenter.Extensions;

namespace G.Controls.Diagram.Presenter.NodeDatas.Base;

public interface ISelectableFromNodeData
{
    INodeData SelectedFromNodeData { get; set; }

    IEnumerable<INodeData> GetSelectedFromNodeDatas();

}

public abstract class SelectableFromNodeDataBase : ResultPresenterNodeDataBase, ISelectableFromNodeData
{
    public class SelectAllNodeData : NodeDataBase
    {
        public override string ToString()
        {
            return "全部";
        }
    }

    private INodeData _selectedFromNodeData;
    [MethodNameSourcePropertyItem(typeof(ComboBoxPropertyItem), nameof(GetSelectableFromNodeDatas))]
    [Display(Name = "输入控制", GroupName = "流程控制")]
    public INodeData SelectedFromNodeData
    {
        get { return _selectedFromNodeData; }
        set
        {
            _selectedFromNodeData = value;
            RaisePropertyChanged();
        }
    }

    private SelectAllNodeData _selectAll = new SelectAllNodeData();
    public IEnumerable<INodeData> GetSelectableFromNodeDatas()
    {
        yield return _selectAll;
        foreach (var item in this.FromNodeDatas)
        {
            yield return item;
        }
    }

    protected override bool CanInvoke(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (previors == null)
            return true;
        if (this.SelectedFromNodeData == null)
            return true;
        if (this.SelectedFromNodeData is SelectAllNodeData)
            return true;
        var from = previors.GetFromNodeData(diagram);
        if (from == this.SelectedFromNodeData)
            return true;
        return false;
    }

    public IEnumerable<INodeData> GetSelectedFromNodeDatas()
    {
        if (this.SelectedFromNodeData is SelectAllNodeData || this.SelectedFromNodeData == null)
        {
            foreach (var item in this.FromNodeDatas)
            {
                yield return item;
            }
            yield break;
        }
        yield return this.SelectedFromNodeData;
    }
}

