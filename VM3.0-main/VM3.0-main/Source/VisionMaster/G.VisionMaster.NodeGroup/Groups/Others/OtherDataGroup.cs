using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Others;

public interface IOtherGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.More)]
[Display(Name = "其他模块", Description = "图像处理的其他算法", Order = 10900)]
public class OtherDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IOtherGroupableNodeData>().OrderBy(x => x.Order);
    }
}

