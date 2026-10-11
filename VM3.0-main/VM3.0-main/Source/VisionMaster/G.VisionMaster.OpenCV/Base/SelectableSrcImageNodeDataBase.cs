using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.Controls.Form.PropertyItem.Attribute.SourcePropertyItem;
using G.Controls.Form.PropertyItem.ComboBoxPropertyItems;

namespace G.VisionMaster.OpenCV.Base;

public abstract class SelectableSrcImageNodeDataBase : ResultPresenterNodeDataBase
{
    private ISrcFilesNodeData _selectedSrcNodeData;
    [MethodNameSourcePropertyItem(typeof(ComboBoxPropertyItem), nameof(GetSelectableSrcNodeDatas))]
    [Display(Name = "选择图像源", GroupName = "流程控制")]
    public ISrcFilesNodeData SelectedFromNodeData
    {
        get { return _selectedSrcNodeData; }
        set
        {
            _selectedSrcNodeData = value;
            RaisePropertyChanged();
        }
    }

    public IEnumerable<INodeData> GetSelectableSrcNodeDatas()
    {
        return this.FromNodeDatas.OfType<ISrcFilesNodeData>();
    }

}