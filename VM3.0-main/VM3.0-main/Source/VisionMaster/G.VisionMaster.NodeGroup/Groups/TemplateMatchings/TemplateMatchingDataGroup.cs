using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.TemplateMatchings;

public interface ITemplateMatchingDataGroup : INodeDataGroup
{

}
[Icon(FontIcons.GotoToday)]
[Display(Name = "模板匹配模块", Description = "图像处理的基础检测", Order = 10600)]
public class TemplateMatchingDataGroup : NodeDataGroupBase, IImageDataGroup, ITemplateMatchingDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<ITemplateMatchingGroupableNodeData>().OrderBy(x => x.Order);
    }
}

public interface ITemplateMatchingGroupableNodeData : INodeData, IDisplayBindable
{

}
