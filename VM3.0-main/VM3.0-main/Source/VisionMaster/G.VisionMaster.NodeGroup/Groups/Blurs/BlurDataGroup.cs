using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Blurs;

public interface IBlurGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.InPrivate)]
[Display(Name = "滤波模块", Description = "对图像进行滤波，降噪，模糊处理", Order = 10200)]
public class BlurDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IBlurGroupableNodeData>().OrderBy(x => x.Order);
    }
}

