using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Flowables;
using G.VisionMaster.Network.Groups;

namespace G.VisionMaster.Network;

[Display(Name = "Modbus采集", GroupName = "网络通讯模块", Description = "配置数据采集并实时采集Modbus数据", Order = 10)]
public class IntReadableModbusNodeData : ReadableModbusNodeData<int>, INetwrokNodeData
{
    protected override void Read(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        ushort[] registers = this.Master.ReadHoldingRegisters(this.SlaveAddress, this.StartAddress, this.NumberOfPoints);
        ushort value = registers[0];
        this.Value = (int)value;
    }
}

