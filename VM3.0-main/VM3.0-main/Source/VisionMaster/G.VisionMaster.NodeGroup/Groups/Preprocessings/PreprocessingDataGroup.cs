using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels;
using G.VisionMaster.NodeGroup.Groups.SrcImages;

namespace G.VisionMaster.NodeGroup.Groups.Preprocessings;

public interface IPreprocessingGroupableNodeData : INodeData, IDisplayBindable
{

}

[Icon(FontIcons.Color)]
[Display(Name = "图像预处理模块", Description = "对图像进行预处理操作", Order = 10100)]
public class PreprocessingDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IPreprocessingGroupableNodeData>().OrderBy(x => x.Order);
    }
}

