using G.Controls.Diagram.Datas;
using G.Extensions.Common;
using G.NodeDatas.Onnx.OpenCV.Base;
using G.VisionMaster.NodeGroup.Groups.SrcImages;
using System.Collections.Generic;

namespace G.NodeDatas.Onnx.OpenCV.NodeDataGroups
{
    [Icon(FontIcons.CommandPrompt)]
    [Display(Name = "Onnx通用模型", Description = "CvDnn.ReadNetFromONNX", Order = 10500)]
    public class OnnxDataGroup : OnnxDataGroupBase, IImageDataGroup
    {
        protected override IEnumerable<INodeData> CreateNodeDatas()
        {
            return typeof(IOpenCVDnnNodeData).Assembly.GetInstances<IOpenCVDnnNodeData>().OrderBy(x => x.Order);
        }
    }
}
