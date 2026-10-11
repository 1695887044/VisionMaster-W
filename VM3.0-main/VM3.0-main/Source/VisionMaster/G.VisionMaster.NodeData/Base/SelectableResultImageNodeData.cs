using G.Controls.Form.PropertyItem.Attribute.SourcePropertyItem;
using G.Controls.Form.PropertyItem.ComboBoxPropertyItems;
using G.VisionMaster.NodeData.ResultImages;
using System.Text.Json.Serialization;

namespace G.VisionMaster.NodeData.Base;

public interface ISelectableResultImageNode<T> where T : IDisposable
{
    IVisionResultImage<T> SelectedResultImage { get; set; }
}

public abstract class SelectableResultImageNodeData<T> : ROINodeData<T>, ISelectableResultImageNode<T> where T : IDisposable
{
    private IVisionResultImage<T> _selectedResultImage;
    [JsonIgnore]
    [MethodNameSourcePropertyItem(typeof(ComboBoxPropertyItem), nameof(GetSelectableSrcNodeDatas))]
    [Display(Name = "输入图像源", GroupName = VisionPropertyGroupNames.BaseParameters, Order = -1)]
    public IVisionResultImage<T> SelectedResultImage
    {
        get { return _selectedResultImage; }
        set
        {
            _selectedResultImage = value;
            RaisePropertyChanged();
        }
    }

    public IEnumerable<IVisionResultImage<T>> GetSelectableSrcNodeDatas()
    {
        return this.AllFromNodeDatas.OfType<IVisionNodeData<T>>().SelectMany(x => x.ResultImages);
    }

}