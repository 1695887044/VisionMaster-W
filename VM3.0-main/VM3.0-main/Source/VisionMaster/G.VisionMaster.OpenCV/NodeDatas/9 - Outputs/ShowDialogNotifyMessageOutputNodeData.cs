using G.Services.Message;
using G.VisionMaster.NodeGroup.Groups.Outputs;
using System.Windows;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.Unknown)]
[Display(Name = "提示对话框消息", Description = "输出提示消息", Order = 10410)]
public class ShowDialogNotifyMessageOutputNodeData : OpenCVNodeDataBase, IOutputGroupableNodeData
{
    private string _value = "是否继续运行流程";
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
        return this.OK(from.Mat);
    }

    public override async Task<IFlowableResult> InvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        var result = await base.InvokeAsync(previors, diagram);
        var r = await IocMessage.Notify.ShowDialog(this.Value);
        if (r != true && result is FlowableResult<Mat> matResult)
        {
            return this.Error(matResult.Value, "用户取消运行流程");
        }
        return result;
    }
}

