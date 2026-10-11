using G.Components.Modbus.Presenters;

namespace G.Components.Modbus.Base;

public interface IModbusTcpDataItem
{
    string Ip { get; set; }
    int Port { get; set; }
    byte SlaveAddress { get; set; }
    ushort StartAddress { get; set; }
    ushort NumberOfPoints { get; set; }
    string Message { get; set; }
    MasterState State { get; set; }
    DateTime? UpdateTime { get; set; }

}

