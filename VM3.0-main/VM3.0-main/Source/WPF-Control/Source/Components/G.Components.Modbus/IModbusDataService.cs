using G.Components.Modbus.Presenters;

namespace G.Components.Modbus;

public interface IModbusDataService
{
    ModbusTcpDatas Collection { get; set; }
    List<ITcpClientMaster> Masters { get; set; }
    ModbusDataState State { get; set; }
    void Add(IModbusTcpDataItem item);
    bool CanStart();
    bool CanStop();
    void Clear();
    List<ITcpClientMaster> CreateTcpClientMasters();
    void Delete(IModbusTcpDataItem item);
    List<ITcpClientMaster> GetMasters();
    void InvalidateMasters();
    Task Start();
    Task Stop();
}