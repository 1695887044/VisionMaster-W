using G.VisionMaster.NodeData.HelpPresenters;
using System.Text.Json.Serialization;

namespace G.VisionMaster.NodeData.Base;

public interface IHelpNodeData
{
    IHelpPresenter HelpPresenter { get; set; }
}

public abstract class HelpNodeDataBase : ShowPropertyNodeDataBase, IHelpNodeData
{
    protected HelpNodeDataBase()
    {
        this.HelpPresenter = this.CreateHelpPresenter();
    }
    private IHelpPresenter _helpPresenter;
    [JsonIgnore]
    [Browsable(false)]
    public IHelpPresenter HelpPresenter
    {
        get { return _helpPresenter; }
        set
        {
            _helpPresenter = value;
            RaisePropertyChanged();
        }
    }
    public virtual IHelpPresenter CreateHelpPresenter()
    {
        //https://G.github.io/WPF-Control-Docs/api/G.Controls.Diagram.Presenters.OpenCV.NodeDatas.Basic.AddSutract.html
        string fullName = this.GetType().FullName;
        return new HelpPresenter()
        {
            Url = "https://G.github.io/WPF-Control-Docs/api/" + fullName + ".html"
        };
    }
}

