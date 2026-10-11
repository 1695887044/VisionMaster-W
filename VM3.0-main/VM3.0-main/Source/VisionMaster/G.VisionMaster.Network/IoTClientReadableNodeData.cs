using G.VisionMaster.NodeData;
using G.VisionMaster.NodeData.Base;
using G.VisionMaster.Network.Groups;
using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Form.Attributes;
using G.Common.Attributes;
using IoTClient.Clients.Modbus;
using IoTClient.Clients.PLC;
using IoTClient.Common.Enums;
using G.Services.Logger;
using G.Iocable;
using G.Extensions.FontIcon;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

using SysSiemensVersion = IoTClient.Common.Enums.SiemensVersion;
using SysMitsubishiVersion = IoTClient.Enums.MitsubishiVersion;

namespace G.VisionMaster.Network
{
    [Icon(FontIcons.Download)]
    [Display(Name = "读取PLC", GroupName = "网络通讯模块", Description = "基于IoTClient读取PLC数据(Modbus/Siemens/Mitsubishi等)", Order = 20)]
    public class IoTClientReadableNodeData : DemoNodeDataBase, INetwrokNodeData
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

        private byte _functionCode = 3;
        [BindingVisiblableMethodName(nameof(IsModbus))]
        [Display(Name = "功能码(Modbus)", GroupName = "运行参数", Description = "Modbus功能码 (通常3或4)")]
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

        #region Read Parameters

        private string _address = "0";
        [Display(Name = "读取地址", GroupName = "运行参数", Description = "数据地址 (Modbus: 0, Siemens: V100, etc)")]
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
        [Display(Name = "数据类型", GroupName = "运行参数", Description = "读取的数据类型")]
        public IoTDataType DataType
        {
            get => _dataType;
            set
            {
                _dataType = value;
                RaisePropertyChanged();
            }
        }

        private int _readInterval = 100;
        [Display(Name = "读取间隔(ms)", GroupName = "运行参数", Description = "自动读取时的间隔时间")]
        public int ReadInterval
        {
            get => _readInterval;
            set
            {
                _readInterval = value;
                RaisePropertyChanged();
            }
        }

        private bool _isRunning;
        [Display(Name = "启动读取", GroupName = "运行参数", Description = "启动/停止读取")]
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    RaisePropertyChanged();
                    if (_isRunning)
                    {
                        StartReadLoop();
                    }
                }
            }
        }

        #endregion

        #region Result

        private string _valueText;
        [Display(Name = "读取结果", GroupName = "结果显示")]
        public string ValueText
        {
            get => _valueText;
            set
            {
                _valueText = value;
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

        #endregion

        public override IFlowableResult Invoke(IFlowableLinkData previors, IFlowableDiagramData diagram)
        {
            try
            {
                ReadOnce();
                return this.OK(this.ValueText);
            }
            catch (Exception ex)
            {
                return this.Error(ex.Message);
            }
        }

        private void StartReadLoop()
        {
            Task.Run(async () =>
            {
                while (this.IsRunning)
                {
                    try
                    {
                        ReadOnce();
                    }
                    catch (Exception ex)
                    {
                        this.ValueText = $"Error: {ex.Message}";
                        this.StatusColor = Brushes.Red;
                        Ioc<ILogService>.Instance?.Error($"PLC Read Loop Error: {ex.Message}");
                    }
                    await Task.Delay(this.ReadInterval);
                }
            });
        }

        private void ReadOnce()
        {
            try 
            {
                switch (this.Protocol)
                {
                    case IoTClientProtocol.ModBusTcp:
                        ReadModbusTcp();
                        break;
                    case IoTClientProtocol.Siemens:
                        ReadSiemens();
                        break;
                    case IoTClientProtocol.Mitsubishi:
                        ReadMitsubishi();
                        break;
                    case IoTClientProtocol.OmronFins:
                        ReadOmronFins();
                        break;
                    case IoTClientProtocol.AllenBradley:
                        ReadAllenBradley();
                        break;
                    default:
                        throw new NotImplementedException($"Protocol {this.Protocol} not implemented");
                }
                this.StatusColor = Brushes.Green;
                RaisePropertyChanged(nameof(ValueText));
            }
            catch
            {
                this.StatusColor = Brushes.Red;
                throw;
            }
        }

        private void ReadModbusTcp()
        {
            var client = new ModbusTcpClient(this.Ip, this.Port);
            try
            {
                var openResult = client.Open();
                if (!openResult.IsSucceed) throw new Exception($"Connect failed: {openResult.Err}");

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.ReadInt16(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt16: result = client.ReadUInt16(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Int32: result = client.ReadInt32(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt32: result = client.ReadUInt32(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Int64: result = client.ReadInt64(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.UInt64: result = client.ReadUInt64(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Float: result = client.ReadFloat(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Double: result = client.ReadDouble(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.Bool: result = client.ReadCoil(this.Address, this.StationNumber, this.FunctionCode); break;
                    case IoTDataType.String: result = client.ReadString(this.Address, this.StationNumber, this.FunctionCode); break;
                }

                if (result != null)
                {
                    if (!result.IsSucceed) throw new Exception(result.Err);
                    this.ValueText = result.Value?.ToString();
                }
            }
            finally
            {
                client.Close();
            }
        }

        private void ReadSiemens()
        {
            var client = new SiemensClient(this.SiemensVersion, this.Ip, this.Port);
            try
            {
                var openResult = client.Open();
                if (!openResult.IsSucceed) throw new Exception($"Connect failed: {openResult.Err}");

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.ReadInt16(this.Address); break;
                    case IoTDataType.UInt16: result = client.ReadUInt16(this.Address); break;
                    case IoTDataType.Int32: result = client.ReadInt32(this.Address); break;
                    case IoTDataType.UInt32: result = client.ReadUInt32(this.Address); break;
                    case IoTDataType.Int64: result = client.ReadInt64(this.Address); break;
                    case IoTDataType.UInt64: result = client.ReadUInt64(this.Address); break;
                    case IoTDataType.Float: result = client.ReadFloat(this.Address); break;
                    case IoTDataType.Double: result = client.ReadDouble(this.Address); break;
                    case IoTDataType.Bool: result = client.ReadBoolean(this.Address); break;
                    case IoTDataType.String: result = client.ReadString(this.Address); break;
                }

                if (result != null)
                {
                    if (!result.IsSucceed) throw new Exception(result.Err);
                    this.ValueText = result.Value?.ToString();
                }
            }
            finally
            {
                client.Close();
            }
        }

        private void ReadMitsubishi()
        {
            var client = new MitsubishiClient(this.MitsubishiVersion, this.Ip, this.Port);
            try
            {
                var openResult = client.Open();
                if (!openResult.IsSucceed) throw new Exception($"Connect failed: {openResult.Err}");

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.ReadInt16(this.Address); break;
                    case IoTDataType.UInt16: result = client.ReadUInt16(this.Address); break;
                    case IoTDataType.Int32: result = client.ReadInt32(this.Address); break;
                    case IoTDataType.UInt32: result = client.ReadUInt32(this.Address); break;
                    case IoTDataType.Int64: result = client.ReadInt64(this.Address); break;
                    case IoTDataType.UInt64: result = client.ReadUInt64(this.Address); break;
                    case IoTDataType.Float: result = client.ReadFloat(this.Address); break;
                    case IoTDataType.Double: result = client.ReadDouble(this.Address); break;
                    case IoTDataType.Bool: result = client.ReadBoolean(this.Address); break;
                    case IoTDataType.String: result = client.ReadString(this.Address); break;
                }

                if (result != null)
                {
                    if (!result.IsSucceed) throw new Exception(result.Err);
                    this.ValueText = result.Value?.ToString();
                }
            }
            finally
            {
                client.Close();
            }
        }

        private void ReadOmronFins()
        {
            var client = new OmronFinsClient(this.Ip, this.Port);
            try
            {
                var openResult = client.Open();
                if (!openResult.IsSucceed) throw new Exception($"Connect failed: {openResult.Err}");

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.ReadInt16(this.Address); break;
                    case IoTDataType.UInt16: result = client.ReadUInt16(this.Address); break;
                    case IoTDataType.Int32: result = client.ReadInt32(this.Address); break;
                    case IoTDataType.UInt32: result = client.ReadUInt32(this.Address); break;
                    case IoTDataType.Int64: result = client.ReadInt64(this.Address); break;
                    case IoTDataType.UInt64: result = client.ReadUInt64(this.Address); break;
                    case IoTDataType.Float: result = client.ReadFloat(this.Address); break;
                    case IoTDataType.Double: result = client.ReadDouble(this.Address); break;
                    case IoTDataType.Bool: result = client.ReadBoolean(this.Address); break;
                    case IoTDataType.String: result = client.ReadString(this.Address); break;
                }

                if (result != null)
                {
                    if (!result.IsSucceed) throw new Exception(result.Err);
                    this.ValueText = result.Value?.ToString();
                }
            }
            finally
            {
                client.Close();
            }
        }

        private void ReadAllenBradley()
        {
            var client = new AllenBradleyClient(this.Ip, this.Port);
            try
            {
                var openResult = client.Open();
                if (!openResult.IsSucceed) throw new Exception($"Connect failed: {openResult.Err}");

                dynamic result = null;
                switch (this.DataType)
                {
                    case IoTDataType.Int16: result = client.ReadInt16(this.Address); break;
                    case IoTDataType.UInt16: result = client.ReadUInt16(this.Address); break;
                    case IoTDataType.Int32: result = client.ReadInt32(this.Address); break;
                    case IoTDataType.UInt32: result = client.ReadUInt32(this.Address); break;
                    case IoTDataType.Int64: result = client.ReadInt64(this.Address); break;
                    case IoTDataType.UInt64: result = client.ReadUInt64(this.Address); break;
                    case IoTDataType.Float: result = client.ReadFloat(this.Address); break;
                    case IoTDataType.Double: result = client.ReadDouble(this.Address); break;
                    case IoTDataType.Bool: result = client.ReadBoolean(this.Address); break;
                    case IoTDataType.String: result = client.ReadString(this.Address); break;
                }

                if (result != null)
                {
                    if (!result.IsSucceed) throw new Exception(result.Err);
                    this.ValueText = result.Value?.ToString();
                }
            }
            finally
            {
                client.Close();
            }
        }
    }
}
