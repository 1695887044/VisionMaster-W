using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Outputs;

public interface IOutputGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.Ethernet)]
[Display(Name = "结果输出模块", Description = "输出流程处理结果", Order = 10900)]
public class OutputDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IOutputGroupableNodeData>().OrderBy(x => x.Order);
    }
}

