using G.Components.Modbus.Presenters;
namespace G.Components.Modbus.Base;

public interface ITcpClientMaster
{
    List<IModbusTcpDataItem> ModbusDatas { get; set; }
    MasterState State { get; set; }
    void Dispose();
    void Read();
}

