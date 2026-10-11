using G.Common.Attributes;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Diagram.Presenter.Flowables;
using G.Extensions.FontIcon;
using G.Services.Logger;
using G.VisionMaster.NodeData;

namespace G.VisionMaster.Network;

[Icon(FontIcons.NarratorForwardMirrored)]
public abstract class ReadableModbusNodeData<T> : ModbusNodeDataBase
{

    private T _value;
    [ReadOnly(true)]
    [Display(Name = "读取结果", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "读取点位置的结果")]
    public T Value
    {
        get { return _value; }
        set
        {
            _value = value;
            RaisePropertyChanged();
        }
    }

    private ushort _startAddress = 0;
    [ReadOnly(true)]
    [DefaultValue(0)]
    [Display(Name = "读取位置", GroupName = VisionPropertyGroupNames.RunParameters, Description = "读取位置")]
    public ushort StartAddress
    {
        get { return _startAddress; }
        set
        {
            _startAddress = value;
            RaisePropertyChanged();
        }
    }

    private ushort _numberOfPoints = 1;
    [DefaultValue(1)]
    [Display(Name = "读取长度", GroupName = VisionPropertyGroupNames.RunParameters, Description = "读取位置")]
    public ushort NumberOfPoints
    {
        get { return _numberOfPoints; }
        set
        {
            _numberOfPoints = value;
            RaisePropertyChanged();
        }
    }

    protected virtual async Task<bool?> InvokeFrameAsync(IFlowablePartData previors, IFlowableDiagramData diagram)
    {
        IFlowableDiagramData invokeable = diagram;
        Action<IPartData> invoking = x =>
        {
            //OpenCVNodeDataBase data = x.GetContent<OpenCVNodeDataBase>();
            //data.UseInfoLogger = false;
            //data.UseReview = false;
            //data.UseAnimation = false;
            //diagram.Dispatcher.Invoke(() =>
            //{
            //    invokeable?.OnInvokingPart(x);
            //});
        };

        Action<IPartData> invoked = x =>
        {
            if (this.State == FlowableState.Canceling)
                return;
            invokeable?.OnInvokedPart(x);
            //Thread.Sleep(1000);
        };
        invoking.Invoke(this);
        invoked.Invoke(this);
        IEnumerable<IFlowableNodeData> tos = this.GetToNodeDatas(diagram).OfType<IFlowableNodeData>();
        IFlowableNodeData to = tos.FirstOrDefault();
        if (to == null)
            return true;
        tos.GotoState(diagram, x => FlowableState.Wait);
        IFlowableLinkData linkData = this.GetToLinkDatas(this.DiagramData).OfType<IFlowableLinkData>().Where(x => x.ToNodeID == to.ID)?.FirstOrDefault();
        bool? r = await to.Start(diagram, linkData);
        await Task.Delay(this.SleepMilliseconds);
        return r;
    }

    public override IFlowableResult Invoke(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (this.TcpClient == null || !this.TcpClient.Connected)
        {
            this.ModbusState = ModbusState.Connectting;
            if (!this.Connect())
            {
                this.ModbusState = ModbusState.Unconnet;
                return this.Error("连接失败");
            }
        }
        this.ModbusState = ModbusState.Connected;

        while (true)
        {
            if (this.State == FlowableState.Canceling)
                return this.Error("用户取消");
            diagram.Wait(x => x != this);
            diagram.Message = "发送采集读取数据...";
            this.Message = "正在读取采集数据...";
            try
            {
                this.Read(previors, diagram);
                this.InvokeFrameAsync(previors, diagram).Wait();
                this.UpdateTime = DateTime.Now;
                this.ModbusState = ModbusState.Success;
            }
            catch (Exception ex)
            {
                IocLog.Instance?.Error(ex);
                this.Message = ex.Message;
                this.ModbusState = ModbusState.Error;
            }
        }
    }

    protected abstract void Read(IFlowableLinkData previors, IFlowableDiagramData diagram);

}

