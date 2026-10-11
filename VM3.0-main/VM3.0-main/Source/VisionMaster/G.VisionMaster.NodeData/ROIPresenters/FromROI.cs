using G.Controls.Diagram.Presenter.Extensions;
using G.Extensions.TypeConverter;
using G.VisionMaster.NodeData.Base;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Windows;

namespace G.VisionMaster.NodeData.ROIPresenters;

[Display(Name = "继承")]
public class FromROI : ROIBase, IROI
{
    [JsonIgnore]
    public IROINodeData ROINodeData { get; set; }

    [JsonIgnore]
    [TypeConverter(typeof(IntRectConverter))]
    public Rect Rect
    {
        get
        {
            if (this.ROINodeData == null)
                return Rect.Empty;
            IROINodeData from = this.ROINodeData.GetFromNodeDatas().OfType<IROINodeData>().FirstOrDefault();
            return from?.ROI?.Rect ?? Rect.Empty;
        }
    }

}

