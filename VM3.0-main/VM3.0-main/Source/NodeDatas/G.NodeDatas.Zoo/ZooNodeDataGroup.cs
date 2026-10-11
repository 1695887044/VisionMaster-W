using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.NodeDatas.Zoo.NodeDatas;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace G.NodeDatas.Zoo;

[Icon(FontIcons.Photo2)]
[Display(Name = "系统数据源", Description = "包含系统自带的一些图片示例数据源", Order = 10001)]
public class ZooNodeDataGroup : NodeDataGroupBase, IZooNodeDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IZooSrcImageFilesNodeData>().OrderBy(x => x.Order);
    }
}
