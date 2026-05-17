using HslCommunication;
using HslCommunication.Profinet.Siemens;
using System;

namespace VisionMaster.Communications
{
    public class SiemensS7Connection : BaseConnection<SiemensS7Net>
    {
        public override string ConnectionName { get; set; } = string.Empty;
        public override CommunicationType Type => CommunicationType.SiemensS7;
        public override ConnectionConfigBase? Config { get; protected set; }

        public SiemensS7Connection(CommunicationConfig config)
        {
            if (config.Config is not SiemensS7Config s7Config)
                throw new InvalidOperationException("需要西门子S7配置");
            Config = s7Config;
            ConnectionName = config.ConnectionName;
        }

        public SiemensS7Connection(SiemensS7Config config)
        {
            Config = config;
            ConnectionName = config.IpAddress + ":" + config.Port + "(" + config.S7CpuType + ")";
        }

        private SiemensPLCS GetCpuType() => Config switch
        {
            SiemensS7Config s7 => s7.S7CpuType switch
            {
                "Smart200" => SiemensPLCS.S200Smart,
                "S1200" => SiemensPLCS.S1200,
                "S1500" => SiemensPLCS.S1500,
                "S300" => SiemensPLCS.S300,
                "S400" => SiemensPLCS.S400,
                _ => SiemensPLCS.S1200
            },
            _ => SiemensPLCS.S1200
        };

        protected override void InitializeDevice()
        {
            var s7Config = Config as SiemensS7Config;
            _device = new SiemensS7Net(GetCpuType(), s7Config!.IpAddress)
            {
                ConnectTimeOut = s7Config.TimeoutMs,
                Rack = s7Config.Rack,
                Slot = s7Config.Slot
            };
            _device.Port = s7Config.Port;
        }

        protected override OperateResult ConnectServer() => _device!.ConnectServer();
        protected override void CloseConnection() => _device?.ConnectClose();
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

        public override string ToString() => $"SiemensS7[{((Config as SiemensS7Config)?.IpAddress)}:{((Config as SiemensS7Config)?.Port)}({((Config as SiemensS7Config)?.S7CpuType)})]";
    }
}
