using G.Common.Attributes;
using G.Common.Interfaces;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Conditions;

public interface IConditionGroupableNodeData : INodeData, IOrderable
{

}

[Icon(FontIcons.Dial6)]
[Display(Name = "逻辑模块", Description = "对图像进行条件判断选择执行对应路径", Order = 10500)]
public class ConditionDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IConditionGroupableNodeData>().OrderBy(x => x.Order);
    }
}

