using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Morphologys;

public interface IMorphologyGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.HomeGroup)]
[Display(Name = "形态学模块", Description = "对图像进行腐蚀、膨胀、开运算和闭运算", Order = 10400)]
public class MorphologyDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IMorphologyGroupableNodeData>().OrderBy(x => x.Order);
    }
}

