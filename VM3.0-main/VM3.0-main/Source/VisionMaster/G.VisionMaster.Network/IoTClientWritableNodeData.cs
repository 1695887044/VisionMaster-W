using G.VisionMaster.NodeData;
using G.VisionMaster.NodeData.Base;
using G.VisionMaster.Network.Groups;
using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Form.Attributes;
using G.Common.Attributes;
using G.Services.Logger;
using G.Iocable;
using G.Extensions.FontIcon;
using IoTClient.Clients.Modbus;
using IoTClient.Clients.PLC;
using IoTClient.Common.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Windows.Media;

using SysSiemensVersion = IoTClient.Common.Enums.SiemensVersion;
using SysMitsubishiVersion = IoTClient.Enums.MitsubishiVersion;

namespace G.VisionMaster.Network
{
    [Icon(FontIcons.Upload)]
    [Display(Name = "写入PLC", GroupName = "网络通讯模块", Description = "基于IoTClient写入PLC数据(Modbus/Siemens/Mitsubishi等)", Order = 21)]
    public class IoTClientWritableNodeData : DemoNodeDataBase, INetwrokNodeData
    {
        #region Connection Parameters

        private IoTClientProtocol _protocol = IoTClientProtocol.ModBusTcp;
        [RefreshOnValueChanged]
        [Display(Name = "协议类型", GroupName = "运行参数", Description = "PLC通讯协议类型")]
        public IoTClientProtocol Protocol
        {
            get => _protocol;
            set
            {
                _protocol = value;
                RaisePropertyChanged();
            }
        }

        public bool IsModbus() => Protocol == IoTClientProtocol.ModBusTcp;
        public bool IsSiemens() => Protocol == IoTClientProtocol.Siemens;
        public bool IsMitsubishi() => Protocol == IoTClientProtocol.Mitsubishi;

        private string _ip = "127.0.0.1";
        [Display(Name = "IP地址", GroupName = "运行参数", Description = "PLC IP地址")]
        public string Ip
        {
            get => _ip;
            set
            {
                _ip = value;
                RaisePropertyChanged();
            }
        }

        private int _port = 502;
        [Display(Name = "端口号", GroupName = "运行参数", Description = "PLC 端口号")]
        public int Port
        {
            get => _port;
            set
            {
                _port = value;
                RaisePropertyChanged();
            }
        }

        // Modbus Specific
        private byte _stationNumber = 1;
        [BindingVisiblableMethodName(nameof(IsModbus))]
        [Display(Name = "站号(Modbus)", GroupName = "运行参数", Description = "Modbus站号")]
        public byte StationNumber
        {
            get => _stationNumber;
            set
            {
                _stationNumber = value;
                RaisePropertyChanged();
            }
        }

        private byte _functionCode = 16;
        [BindingVisiblableMethodName(nameof(IsModbus))]
        [Display(Name = "功能码(Modbus)", GroupName = "运行参数", Description = "Modbus功能码")]
        public byte FunctionCode
        {
            get => _functionCode;
            set
            {
                _functionCode = value;
                RaisePropertyChanged();
            }
        }

        // Siemens Specific
        private SysSiemensVersion _siemensVersion = SysSiemensVersion.S7_200Smart;
        [BindingVisiblableMethodName(nameof(IsSiemens))]
        [Display(Name = "西门子型号", GroupName = "运行参数", Description = "西门子PLC型号")]
        public SysSiemensVersion SiemensVersion
        {
            get => _siemensVersion;
            set
            {
                _siemensVersion = value;
                RaisePropertyChanged();
            }
        }

        // Mitsubishi Specific
        private SysMitsubishiVersion _mitsubishiVersion = SysMitsubishiVersion.Qna_3E;
        [BindingVisiblableMethodName(nameof(IsMitsubishi))]
        [Display(Name = "三菱型号", GroupName = "运行参数", Description = "三菱PLC型号")]
        public SysMitsubishiVersion MitsubishiVersion
        {
            get => _mitsubishiVersion;
            set
            {
                _mitsubishiVersion = value;
                RaisePropertyChanged();
            }
        }

        #endregion

        #region Write Parameters

        private string _address = "0";
        [Display(Name = "写入地址", GroupName = "运行参数", Description = "数据地址 (Modbus: 0, Siemens: V100, etc)")]
        public string Address
        {
            get => _address;
            set
            {
                _address = value;
                RaisePropertyChanged();
            }
        }

        private IoTDataType _dataType = IoTDataType.Int16;
        [Display(Name = "数据类型", GroupName = "运行参数", Description = "写入的数据类型")]
        public IoTDataType DataType
        {
            get => _dataType;
            set
            {
                _dataType = value;
                RaisePropertyChanged();
            }
        }

        private string _writeText = "0";
        [Display(Name = "写入值", GroupName = "运行参数", Description = "需要写入的值")]
        public string WriteText
        {
            get => _writeText;
            set
            {
                _writeText = value;
                RaisePropertyChanged();
            }
        }

        private string _statusMessage;
        [Display(Name = "写入状态", GroupName = "结果显示")]
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                RaisePropertyChanged();
            }
        }

        private Brush _statusColor = Brushes.Black;
        [JsonIgnore]
        [Browsable(false)]
        public Brush StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                RaisePropertyChanged();
            }
        }

        private bool _executeWrite;
        [Display(Name = "执行写入", GroupName = "运行参数", Description = "执行一次写入操作")]
        public bool ExecuteWrite
        {
            get => _executeWrite;
            set
            {
                if (_executeWrite != value)
                {
                    _executeWrite = value;
                    RaisePropertyChanged();
                    if (_executeWrite)
                    {
                        WriteOnce();
                        _executeWrite = false;
                        RaisePropertyChanged(nameof(ExecuteWrite));
                    }
                }
            }
        }

        #endregion

        public override IFlowableResult Invoke(IFlowableLinkData previors, IFlowableDiagramData diagram)
        {
            try
            {
                WriteOnce();
                return this.OK("写入成功");
            }
            catch (Exception ex)
            {
                return this.Error(ex.Message);
            }
        }

        private void WriteOnce()
        {
            try
            {
                switch (this.Protocol)
                {
                    case IoTClientProtocol.ModBusTcp:
                        WriteModbusTcp();
                        break;
                    case IoTClientProtocol.Siemens:
                        WriteSiemens();
                        break;
                    case IoTClientProtocol.Mitsubishi:
                        WriteMitsubishi();
                        break;
                    case IoTClientProtocol.OmronFins:
                        WriteOmronFins();
                        break;
                    case IoTClientProtocol.AllenBradley:
                        WriteAllenBradley();
                        break;
                    default:
                        throw new NotImplementedException($"Protocol {this.Protocol} not implemented");
                }
                this.StatusMessage = "Success";
                this.StatusColor = Brushes.Green;
            }
            catch (Exception ex)
            {
                this.StatusMessage = $"Error: {ex.Message}";
                this.StatusColor = Brushes.Red;
                Ioc<ILogService>.Instance?.Error($"Write failed: {ex.Message}");
                throw new Exception($"Write failed: {ex.Message}", ex);
            }
        }

        private void WriteModbusTcp()
        {
            var client = new ModbusTcpClient(this.Ip, this.Port);
            try
            {
                if (!client.Open().IsSucceed) client.Open();

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.Write(this.Address, short.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt16: result = client.Write(this.Address, ushort.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Int32: result = client.Write(this.Address, int.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt32: result = client.Write(this.Address, uint.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Int64: result = client.Write(this.Address, long.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt64: result = client.Write(this.Address, ulong.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Float: result = client.Write(this.Address, float.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Double: result = client.Write(this.Address, double.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Bool: result = client.Write(this.Address, bool.Parse(this.WriteText), this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.String: result = client.Write(this.Address, this.WriteText, this.StationNumber, this.FunctionCode); break;
                }
                if (result != null && !result.IsSucceed) throw new Exception(result.Err);
            }
            finally
            {
                client.Close();
            }
        }

        private void WriteSiemens()
        {
            var client = new SiemensClient(this.SiemensVersion, this.Ip, this.Port);
            try
            {
                if (!client.Open().IsSucceed) client.Open();

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.Write(this.Address, short.Parse(this.WriteText)); break;
                    case IoTDataType.UInt16: result = client.Write(this.Address, ushort.Parse(this.WriteText)); break;
                    case IoTDataType.Int32: result = client.Write(this.Address, int.Parse(this.WriteText)); break;
                    case IoTDataType.UInt32: result = client.Write(this.Address, uint.Parse(this.WriteText)); break;
                    case IoTDataType.Int64: result = client.Write(this.Address, long.Parse(this.WriteText)); break;
                    case IoTDataType.UInt64: result = client.Write(this.Address, ulong.Parse(this.WriteText)); break;
                    case IoTDataType.Float: result = client.Write(this.Address, float.Parse(this.WriteText)); break;
                    case IoTDataType.Double: result = client.Write(this.Address, double.Parse(this.WriteText)); break;
                    case IoTDataType.Bool: result = client.Write(this.Address, bool.Parse(this.WriteText)); break;
                    case IoTDataType.String: result = client.Write(this.Address, this.WriteText); break;
                }
                if (result != null && !result.IsSucceed) throw new Exception(result.Err);
            }
            finally
            {
                client.Close();
            }
        }

        private void WriteMitsubishi()
        {
            var client = new MitsubishiClient(this.MitsubishiVersion, this.Ip, this.Port);
            try
            {
                if (!client.Open().IsSucceed) client.Open();

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.Write(this.Address, short.Parse(this.WriteText)); break;
                    case IoTDataType.UInt16: result = client.Write(this.Address, ushort.Parse(this.WriteText)); break;
                    case IoTDataType.Int32: result = client.Write(this.Address, int.Parse(this.WriteText)); break;
                    case IoTDataType.UInt32: result = client.Write(this.Address, uint.Parse(this.WriteText)); break;
                    case IoTDataType.Int64: result = client.Write(this.Address, long.Parse(this.WriteText)); break;
                    case IoTDataType.UInt64: result = client.Write(this.Address, ulong.Parse(this.WriteText)); break;
                    case IoTDataType.Float: result = client.Write(this.Address, float.Parse(this.WriteText)); break;
                    case IoTDataType.Double: result = client.Write(this.Address, double.Parse(this.WriteText)); break;
                    case IoTDataType.Bool: result = client.Write(this.Address, bool.Parse(this.WriteText)); break;
                    case IoTDataType.String: result = client.Write(this.Address, this.WriteText); break;
                }
                if (result != null && !result.IsSucceed) throw new Exception(result.Err);
            }
            finally
            {
                client.Close();
            }
        }

        private void WriteOmronFins()
        {
            var client = new OmronFinsClient(this.Ip, this.Port);
            try
            {
                if (!client.Open().IsSucceed) client.Open();

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.Write(this.Address, short.Parse(this.WriteText)); break;
                    case IoTDataType.UInt16: result = client.Write(this.Address, ushort.Parse(this.WriteText)); break;
                    case IoTDataType.Int32: result = client.Write(this.Address, int.Parse(this.WriteText)); break;
                    case IoTDataType.UInt32: result = client.Write(this.Address, uint.Parse(this.WriteText)); break;
                    case IoTDataType.Int64: result = client.Write(this.Address, long.Parse(this.WriteText)); break;
                    case IoTDataType.UInt64: result = client.Write(this.Address, ulong.Parse(this.WriteText)); break;
                    case IoTDataType.Float: result = client.Write(this.Address, float.Parse(this.WriteText)); break;
                    case IoTDataType.Double: result = client.Write(this.Address, double.Parse(this.WriteText)); break;
                    case IoTDataType.Bool: result = client.Write(this.Address, bool.Parse(this.WriteText)); break;
                    case IoTDataType.String: result = client.Write(this.Address, this.WriteText); break;
                }
                if (result != null && !result.IsSucceed) throw new Exception(result.Err);
            }
            finally
            {
                client.Close();
            }
        }

        private void WriteAllenBradley()
        {
            var client = new AllenBradleyClient(this.Ip, this.Port);
            try
            {
                if (!client.Open().IsSucceed) client.Open();

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.Write(this.Address, short.Parse(this.WriteText)); break;
                    case IoTDataType.UInt16: result = client.Write(this.Address, ushort.Parse(this.WriteText)); break;
                    case IoTDataType.Int32: result = client.Write(this.Address, int.Parse(this.WriteText)); break;
                    case IoTDataType.UInt32: result = client.Write(this.Address, uint.Parse(this.WriteText)); break;
                    case IoTDataType.Int64: result = client.Write(this.Address, long.Parse(this.WriteText)); break;
                    case IoTDataType.UInt64: result = client.Write(this.Address, ulong.Parse(this.WriteText)); break;
                    case IoTDataType.Float: result = client.Write(this.Address, float.Parse(this.WriteText)); break;
                    case IoTDataType.Double: result = client.Write(this.Address, double.Parse(this.WriteText)); break;
                    case IoTDataType.Bool: result = client.Write(this.Address, bool.Parse(this.WriteText)); break;
                    case IoTDataType.String: result = client.Write(this.Address, this.WriteText); break;
                }
                if (result != null && !result.IsSucceed) throw new Exception(result.Err);
            }
            finally
            {
                client.Close();
            }
        }
    }
}
