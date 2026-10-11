using G.Common.Attributes;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Flowables;
using G.Extensions.FontIcon;
using G.Services.Logger;
using G.VisionMaster.NodeData;

namespace G.VisionMaster.Network;

[Icon(FontIcons.NarratorForward)]
public abstract class WriteableModbusNodeData<T> : ModbusNodeDataBase
{
    private T _value;
    [Display(Name = "写入数据", GroupName = VisionPropertyGroupNames.RunParameters, Description = "写入寄存器的数据")]
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
    [DefaultValue(0)]
    [Display(Name = "寄存器位置", GroupName = VisionPropertyGroupNames.RunParameters, Description = "写入寄存器的位置")]
    public ushort StartAddress
    {
        get { return _startAddress; }
        set
        {
            _startAddress = value;
            RaisePropertyChanged();
        }
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
        try
        {
            this.Write(previors, diagram);
            this.UpdateTime = DateTime.Now;
            this.ModbusState = ModbusState.Success;
        }
        catch (Exception ex)
        {
            IocLog.Instance?.Error(ex);
            this.Message = ex.Message;
            this.ModbusState = ModbusState.Error;
        }
        return this.OK("发送数据成功");
    }

    protected abstract void Write(IFlowableLinkData previors, IFlowableDiagramData diagram);
}

