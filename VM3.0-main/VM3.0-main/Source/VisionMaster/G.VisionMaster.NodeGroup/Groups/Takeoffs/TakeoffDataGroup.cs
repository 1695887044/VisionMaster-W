using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Takeoffs;

public interface ITakeoffGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.Annotation)]
[Display(Name = "图像分割提取模块", Description = "对图像进行预处理操作", Order = 10300)]
public class TakeoffDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<ITakeoffGroupableNodeData>().OrderBy(x => x.Order);
    }
}

