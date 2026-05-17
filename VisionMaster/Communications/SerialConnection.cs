using HslCommunication;
using HslCommunication.ModBus;
using System;
using System.IO.Ports;

namespace VisionMaster.Communications
{
    public class SerialConnection : BaseConnection<ModbusRtu>
    {
        public override string ConnectionName { get; set; } = string.Empty;
        public override CommunicationType Type => CommunicationType.ModbusRtu;
        public override ConnectionConfigBase? Config { get; protected set; }

        public SerialConnection(CommunicationConfig config)
        {
            if (config.Config is not SerialConfig serialConfig)
                throw new InvalidOperationException("需要串口配置");
            Config = serialConfig;
            ConnectionName = config.ConnectionName;
        }

        public SerialConnection(SerialConfig config)
        {
            Config = config;
            ConnectionName = config.PortName + "@" + config.BaudRate;
        }

        protected override void InitializeDevice()
        {
            var serialConfig = Config as SerialConfig;
            _device = new ModbusRtu();
            _device.SerialPortInni(serialConfig!.PortName, serialConfig.BaudRate, serialConfig.DataBits,
                serialConfig.StopBits switch { StopBitsMode.Two => StopBits.Two, _ => StopBits.One },
                serialConfig.Parity switch { ParityMode.Odd => Parity.Odd, ParityMode.Even => Parity.Even, _ => Parity.None });
        }

        protected override OperateResult ConnectServer() => _device!.Open();
        protected override void CloseConnection() => _device?.Close();
        protected override OperateResult<bool> ReadBool(string address) => _device!.ReadBool(address);
        protected override OperateResult<short> ReadInt16(string address) => _device!.ReadInt16(address);
        protected override OperateResult<ushort> ReadUInt16(string address) => _device!.ReadUInt16(address);
        protected override OperateResult<int> ReadInt32(string address) => _device!.ReadInt32(address);
        protected override OperateResult<uint> ReadUInt32(string address) => _device!.ReadUInt32(address);
        protected override OperateResult<float> ReadFloat(string address) => _device!.ReadFloat(address);
        protected override OperateResult<double> ReadDouble(string address) => _device!.ReadDouble(address);
        protected override OperateResult<byte[]> ReadBytesCore(string address, ushort length) => _device!.Read(address, length);
        protected override OperateResult WriteBool(string address, bool value) => _device!.Write(address, value);
        protected override OperateResult WriteInt16(string address, short value) => _device!.Write(address, value);
        protected override OperateResult WriteUInt16(string address, ushort value) => _device!.Write(address, value);
        protected override OperateResult WriteInt32(string address, int value) => _device!.Write(address, value);
        protected override OperateResult WriteUInt32(string address, uint value) => _device!.Write(address, value);
        protected override OperateResult WriteFloat(string address, float value) => _device!.Write(address, value);
        protected override OperateResult WriteDouble(string address, double value) => _device!.Write(address, value);
        protected override OperateResult WriteBytesCore(string address, byte[] data) => _device!.Write(address, data);

        public override string ToString() => $"Serial[{((Config as SerialConfig)?.PortName)}@{((Config as SerialConfig)?.BaudRate)}]";
    }
}
