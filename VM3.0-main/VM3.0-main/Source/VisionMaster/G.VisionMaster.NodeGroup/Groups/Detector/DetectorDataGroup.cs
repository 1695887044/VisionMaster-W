using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Detector;

public interface IDetectorGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.LargeErase)]
[Display(Name = "对象识别模块", Description = "识别图像中的对象", Order = 10700)]
public class DetectorDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IDetectorGroupableNodeData>().OrderBy(x => x.Order);
    }
}

