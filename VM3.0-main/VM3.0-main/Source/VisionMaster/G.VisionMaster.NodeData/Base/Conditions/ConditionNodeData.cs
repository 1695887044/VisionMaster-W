using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.Extensions.Mvvm.ViewModels;
using System.Text.Json.Serialization;

namespace G.VisionMaster.NodeData.Base.Conditions;

public interface IConditionNodeData : IDiagramableNodeData, IDisplayBindable
{
    IEnumerable<INodeData> AllFromAndThisNodeDatas { get; }
}

public abstract class ConditionNodeData<T> : VisionNodeData<T>, IOnDiagramDeserialized, IConditionNodeData where T : IDisposable
{
    protected ConditionNodeData()
    {
        this.UseInvokedPart = false;
    }
    private VisionPropertyConditionsPrensenter _conditionsPrensenter;
    public VisionPropertyConditionsPrensenter ConditionsPrensenter
    {
        get { return _conditionsPrensenter; }
        set
        {
            _conditionsPrensenter = value;
            RaisePropertyChanged();
        }
    }

    [JsonIgnore]
    [Browsable(false)]
    public IEnumerable<INodeData> AllFromAndThisNodeDatas => this.GetAllFromAndThisNodeDatas();

    public IEnumerable<INodeData> GetAllFromAndThisNodeDatas()
    {
        yield return this;
        foreach (INodeData item in this.GetAllFromNodeDatas())
        {
            yield return item;
        }
    }

    public override object GetPropertyPresenter()
    {
        if (this._conditionsPrensenter == null)
        {
            this._conditionsPrensenter = new VisionPropertyConditionsPrensenter();
            this._conditionsPrensenter.LoadData(this);
        }
        return _conditionsPrensenter;
    }

    public void OnDiagramDeserialized()
    {
        if (this._conditionsPrensenter == null)
        {
            this._conditionsPrensenter = new VisionPropertyConditionsPrensenter();
            this._conditionsPrensenter.LoadData(this);
        }
        _conditionsPrensenter.LoadData(this);
    }

    protected override IEnumerable<Tuple<IFlowablePortData, Predicate<IFlowableLinkData>>> GetFlowablePortDatas(IFlowableDiagramData diagramData)
    {
        if (this._conditionsPrensenter == null)
            yield break;
        List<VisionPropertyConditionPrensenter> matches = this._conditionsPrensenter.PropertyConfidtions.Where(x => x.IsMatchInputNode()).ToList();
        IEnumerable<INodeData> toNodeDatas = matches.Select(x => x.SelectedOutputNodeData).Where(x => x != null);
        foreach (INodeData toNodeData in toNodeDatas)
        {
            foreach (IFlowablePortData item in this.GetToNodePortData(toNodeData, diagramData).OfType<IFlowablePortData>())
            {
                System.Diagnostics.Debug.WriteLine(item.NodeID);
                Predicate<IFlowableLinkData> predicate = new Predicate<IFlowableLinkData>(x => x.ToNodeID == toNodeData.ID);
                yield return new Tuple<IFlowablePortData, Predicate<IFlowableLinkData>>(item, predicate);
            }
        }
    }
}

