using G.VisionMaster.NodeGroup.Groups.Outputs;
using System.Windows;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.Info)]
[Display(Name = "提示运行消息", Description = "输出提示运行消息", Order = 10410)]
public class ShowInfoNotifyMessageOutputNodeData : OpenCVNodeDataBase, IOutputGroupableNodeData
{
    private string _value = "运行信息";
    [Display(Name = "消息信息", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于设置输出消息信息")]
    public string Value
    {
        get { return _value; }
        set
        {
            _value = value;
            RaisePropertyChanged();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IocMessage.Notify.ShowInfo(this.Value);
        });
        return this.OK(from.Mat, this.Value);
    }
}

