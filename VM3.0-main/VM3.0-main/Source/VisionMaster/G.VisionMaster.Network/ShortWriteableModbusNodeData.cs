using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Flowables;
using G.VisionMaster.Network.Groups;

namespace G.VisionMaster.Network;

[Display(Name = "Modbus发送", GroupName = "网络通讯模块", Description = "配置数据采集并实时采集Modbus数据", Order = 10)]
public class ShortWriteableModbusNodeData : WriteableModbusNodeData<ushort>, INetwrokNodeData
{
    protected override void Write(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        this.Master.WriteSingleRegister(this.SlaveAddress, this.StartAddress, this.Value);
    }
}

