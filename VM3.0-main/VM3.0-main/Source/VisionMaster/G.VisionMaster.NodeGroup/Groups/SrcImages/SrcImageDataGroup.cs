using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;

namespace G.VisionMaster.NodeGroup.Groups.SrcImages;

[Icon(FontIcons.Camera)]
[Display(Name = "图像数据源", Description = "设置输入图像", Order = 10000)]
public class SrcImageDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<ISrcImageGroupableNodeData>().OrderBy(x => x.Order); ;
    }
}

public interface ISrcImageGroupableNodeData : INodeData, IDisplayBindable
{

}
