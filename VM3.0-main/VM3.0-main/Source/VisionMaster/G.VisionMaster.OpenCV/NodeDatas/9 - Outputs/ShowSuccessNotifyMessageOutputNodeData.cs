using G.VisionMaster.NodeGroup.Groups.Outputs;
using System.Windows;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.Completed)]
[Display(Name = "提示成功消息", Description = "输出提示消息", Order = 10410)]
public class ShowSuccessNotifyMessageOutputNodeData : OpenCVNodeDataBase, IOutputGroupableNodeData
{
    private string _value = "运行成功";
    [Display(Name = "消息信息", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于设置输出提示消息")]
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
            IocMessage.Notify.ShowSuccess(this.Value);
        });
        return this.OK(from.Mat, this.Value);
    }
}

