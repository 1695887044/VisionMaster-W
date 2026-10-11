using G.Controls.Diagram.Presenter.NodeDatas.Base;
using System.Text.Json.Serialization;

namespace G.VisionMaster.NodeData.Base;

public interface IResultImageSourceNodeData
{
    ImageSource ResultImageSource { get; set; }
}

public abstract class ResultImageSourceNodeDataBase : SelectableFromNodeDataBase, IResultImageSourceNodeData
{
    private bool _useResultImageSource = true;
    [JsonIgnore]
    [Browsable(false)]
    public bool UseResultImageSource
    {
        get { return _useResultImageSource; }
        set
        {
            _useResultImageSource = value;
            RaisePropertyChanged();
        }
    }

    private ImageSource _resultImageSource;
    [JsonIgnore]
    [Browsable(false)]
    [XmlIgnore]
    public ImageSource ResultImageSource
    {
        get { return _resultImageSource; }
        set
        {
            _resultImageSource = value;
            RaisePropertyChanged();
        }
    }
}
