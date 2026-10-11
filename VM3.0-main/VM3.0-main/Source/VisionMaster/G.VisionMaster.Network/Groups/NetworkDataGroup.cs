using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.Network.Groups;

public interface INetwrokDataGroup : INodeDataGroup
{

}
[Icon(FontIcons.NarratorForward)]
[Display(Name = "网络通讯模块", Description = "网络通讯模块", Order = 10700)]
public class NetworkDataGroup : NodeDataGroupBase, IImageDataGroup, INetwrokDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return typeof(INetwrokNodeData).Assembly.GetInstances<INetwrokNodeData>().OrderBy(x => x.Order);
    }
}

public interface INetwrokNodeData : INodeData, IDisplayBindable
{

}
