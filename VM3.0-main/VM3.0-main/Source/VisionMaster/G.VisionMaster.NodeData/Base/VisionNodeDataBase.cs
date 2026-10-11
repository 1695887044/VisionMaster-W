namespace G.VisionMaster.NodeData.Base;

public abstract class VisionNodeDataBase : StyleNodeDataBase
{
    protected VisionNodeDataBase()
    {
        this.PreviewMillisecondsDelay = 0;
        this.InvokeMillisecondsDelay = 0;
    }

    ~VisionNodeDataBase()
    {
        this.Dispose();
    }

    private int _previewMillisecondsDelay = 1500;
    [DefaultValue(1500)]
    [Display(Name = "预览延迟", GroupName = "流程控制", Description = "设置生成图像后预览等待时间")]
    public int PreviewMillisecondsDelay
    {
        get { return _previewMillisecondsDelay; }
        set
        {
            _previewMillisecondsDelay = value;
            RaisePropertyChanged();
        }
    }

    private int _invokeMillisecondsDelay = 500;
    [DefaultValue(500)]
    [Display(Name = "执行延迟", GroupName = "流程控制", Description = "执行完成后等待时间")]
    public int InvokeMillisecondsDelay
    {
        get { return _invokeMillisecondsDelay; }
        set
        {
            _invokeMillisecondsDelay = value;
            RaisePropertyChanged();
        }
    }
}

