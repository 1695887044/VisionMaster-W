namespace G.Components.Modbus.Base;

public interface IUnshortModbusTcpDataItem : IModbusTcpDataItem
{
    ushort Value { get; set; }
}

