using DMSkin.Socket;
using VM.Start.Communacation;
using PCComm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Start.Common.Provide;
using VM.Start.Common.Enums;
using HslCommunication.ModBus;
using HslCommunication;
using System.IO.Ports;
using VM.Halcon.Helper;
using VM.Start.Common.Helper;
using VM.Start.Events;

namespace VM.Start.Communacation
{
    ////通讯模式
    //[Serializable]
    //public enum eCommunicationType
    //{
    //    TCP客户端 = 0,//客户端
    //    TCP服务器 = 1,//服务端
    //    UDP通讯 = 2,//udp
    //    串口通讯 = 3,//串口
    //}
    public delegate void ReceiveString(string str);

    [Serializable]
    public class ECommunacation : Common.Helper.NotifyPropertyBase
    {
        [NonSerialized]
        private AutoResetEvent m_RecStrSignal = new AutoResetEvent(false);

        [NonSerialized]
        private Queue<string> _m_RecStrQueue = new Queue<string>(); //接收信号总数 为0 的时候 表示没有接收到信号，队列，，
        public Queue<string> m_RecStrQueue
        {
            get
            {
                if (_m_RecStrQueue == null)
                {
                    _m_RecStrQueue = new Queue<string>();
                }
                return _m_RecStrQueue;
            }
            set { _m_RecStrQueue = value; }
        }
        private bool m_IsStartRec = false; //是否开始监听接收

        public event ReceiveString ReceiveString; //接受数据事件
        public string Key { get; set; } //
        public int Encode { get; set; } //编号
        public bool IsPLC { get; set; } = false; //是否为plc
        public eCommunicationType CommunicationType { get; set; } = eCommunicationType.TCP服务器; //通讯模式
        private bool _IsConnected = false;
        public bool IsConnected // 是否开始连接 或监听
        {
            get { return _IsConnected; }
            set { _IsConnected = value; }
        }
        public bool IsSendByHex { get; set; } // 使用十六进制发送
        public bool IsReceivedByHex { get; set; } // 使用十六进制接收

        private bool m_IsPlcDisconnect = false; //plc断开连接的时候 不显示下线提示 目前只有tcp客户端才会用到
        #region "网口参数"
        public string RemoteIP { get; set; } = "127.0.0.1"; //远程ip
        public int RemotePort { get; set; } = 9000; //远程端口
        public int LocalPort { get; set; } = 8000; //本地端口

        private List<string> m_SocketIpPortList = new List<string>(); //作为tcp服务端的时候 显示客户端的信息
        #endregion

        #region "串口参数"
        public string PortName { get; set; } = "COM1"; //串口号
        public List<string> PortNames { get; set; } = SerialPort.GetPortNames().ToList();
        public string BaudRate { get; set; } = "9600"; //波特率
        public List<string> BaudRates { get; set; } =
            new List<string>()
            {
                "1200",
                "2400",
                "4800",
                "9600",
                "14400",
                "19200",
                "38400",
                "56000",
                "57600",
                "115200",
                "230400",
                "460800",
                "460800"
            };
        public string Parity { get; set; } = "None"; //校验位
        public Array Paritys { get; set; } = Enum.GetValues(typeof(Parity));
        public string DataBits { get; set; } = "8"; //数据位
        public List<string> DataBitsList { get; set; } = new List<string>() { "5", "6", "7", "8" };
        public string StopBits { get; set; } = "One"; //停止位
        public List<string> StopBitsList { get; set; } = new List<string>() { "One", "Two" };
        #endregion

        #region "PLC参数"
        public string m_connectKey { get; set; } //PLC链接模块Key
        public PLCType m_PLCType { get; set; } = PLCType.ModbusTCP; //PLC类型
        public PLCDataType m_PLCDataType { get; set; } = PLCDataType.CDAB; //数据解析格式
        public int m_StationCode { get; set; } = 1; //站号
        public bool m_StartWithZero { get; set; } = true; //首地址为0
        public PLCIntDataLengthEnum m_IntNumber { get; set; } = PLCIntDataLengthEnum._32位; //Int占用D寄存器个数
        public PLCDoubleDataLengthEnum m_DoubleNumber { get; set; } = PLCDoubleDataLengthEnum._32位; //1为float(32) 2为Double(64)
        #endregion
        private string _Remarks;

        public string Remarks
        {
            get { return _Remarks; }
            set
            {
                _Remarks = value;
                RaisePropertyChanged();
            }
        }

        [NonSerialized]
        private string _SendText;

        /// <summary>
        /// 发送的文本
        /// </summary>
        public string SendText
        {
            get { return _SendText; }
            set
            {
                _SendText = value;
                RaisePropertyChanged();
            }
        }

        [NonSerialized]
        private string _ReceiveText;

        /// <summary>
        /// 接收的文本
        /// </summary>
        public string ReceiveText
        {
            get { return _ReceiveText; }
            set
            {
                _ReceiveText = value;
                RaisePropertyChanged();
            }
        }

        #region Command
        [NonSerialized]
        private CommandBase _ConnectCommand;
        public CommandBase ConnectCommand
        {
            get
            {
                if (_ConnectCommand == null)
                {
                    _ConnectCommand = new CommandBase(
                        (obj) =>
                        {
                            if (IsConnected)
                            {
                                DisConnect();
                            }
                            else
                            {
                                Connect();
                            }
                            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                        }
                    );
                }
                return _ConnectCommand;
            }
        }

        [NonSerialized]
        private CommandBase _SendCommand;
        public CommandBase SendCommand
        {
            get
            {
                if (_SendCommand == null)
                {
                    _SendCommand = new CommandBase(
                        (obj) =>
                        {
                            if (IsConnected)
                            {
                                SendStr(SendText);
                            }
                        }
                    );
                }
                return _SendCommand;
            }
        }

        [NonSerialized]
        private CommandBase _ClearSendTextCommand;
        public CommandBase ClearSendTextCommand
        {
            get
            {
                if (_ClearSendTextCommand == null)
                {
                    _ClearSendTextCommand = new CommandBase(
                        (obj) =>
                        {
                            SendText = "";
                        }
                    );
                }
                return _ClearSendTextCommand;
            }
        }

        [NonSerialized]
        private CommandBase _ClearReceiveTextCommand;
        public CommandBase ClearReceiveTextCommand
        {
            get
            {
                if (_ClearReceiveTextCommand == null)
                {
                    _ClearReceiveTextCommand = new CommandBase(
                        (obj) =>
                        {
                            ReceiveText = "";
                        }
                    );
                }
                return _ClearReceiveTextCommand;
            }
        }

        #endregion

        [NonSerialized]
        private DMTcpServer m_DMTcpServer; //tcp服务端

        [NonSerialized]
        private DMTcpClient m_DMTcpClient; //tcp客户端

        [NonSerialized]
        private DMUdpClient m_DMUdpClient; //udp

        [NonSerialized]
        private MySerialPort m_MySerialPort; //串口

        //以下为PLC通讯对象
        [NonSerialized]
        private ModbusRtu m_modbusRtuClient;

        [NonSerialized]
        private ModbusTcpNet m_modbusTCPClient;

        public DMTcpClient DMTcpClient() //返回tcp客户端
        {
            return m_DMTcpClient;
        }

        // public bool IsHasObjectConnected { get; set; }// 是否已经连接上对象
        private int m_ObjectConnectedCount = 0; //已经连接目标数量 tcp服务端会有多个的可能
        public bool IsHasObjectConnected
        {
            get { return m_ObjectConnectedCount > 0 ? true : false; }
            set
            {
                if (value == true)
                {
                    if (m_ObjectConnectedCount < 0)
                    {
                        m_ObjectConnectedCount = 1;
                    }
                    else
                    {
                        m_ObjectConnectedCount++;
                    }
                }
                else
                {
                    m_ObjectConnectedCount--;
                }
            }
        }

        /// <summary>
        /// 手动 设置连接状态 针对eplc添加的功能
        /// </summary>
        /// <param name="flag"></param>
        public void SetObjectConnected(bool flag)
        {
            m_ObjectConnectedCount = flag == true ? 1 : 0;
        }

        public ECommunacation()
        {
            ReceiveString += ECommunacation_ReceiveString;
        }

        /// <summary>
        /// 添加接收的数据到队列中
        /// </summary>
        public void AddRecString(string recStr)
        {
            if (m_IsStartRec == true)
            {
                m_RecStrQueue.Enqueue(recStr);

                if (m_RecStrQueue.Count == 1)
                {
                    if (m_RecStrSignal == null)
                        m_RecStrSignal = new AutoResetEvent(false);
                    m_RecStrSignal.Set();
                }
            }
        }

        private void ECommunacation_ReceiveString(string str)
        {
            Logger.AddLog($"[{Key}]接收数据:{str}");
            ReceiveText += str + "\n";
        }

        public bool Connect()
        {
            if (IsConnected == true)
                return true; //已经连接 则不执行

            if (IsPLC)
            {
                switch (m_PLCType)
                {
                    case PLCType.ModbusRtu:
                        if (m_modbusRtuClient == null)
                        {
                            m_modbusRtuClient = new ModbusRtu();
                        }
                        m_modbusRtuClient.AddressStartWithZero = m_StartWithZero;
                        m_modbusRtuClient.Station = (byte)m_StationCode;
                        if (m_MySerialPort != null)
                            m_MySerialPort.ClosePort();
                        StopBits stopbits = System.IO.Ports.StopBits.One;
                        if (StopBits == "One")
                            stopbits = System.IO.Ports.StopBits.One;
                        Parity parity = System.IO.Ports.Parity.None;
                        if (Parity == "One")
                            parity = System.IO.Ports.Parity.None;
                        m_modbusRtuClient.SerialPortInni(
                            PortName,
                            Convert.ToInt32(BaudRate),
                            Convert.ToInt32(DataBits),
                            stopbits,
                            parity
                        );
                        IsConnected = m_modbusRtuClient.Open().IsSuccess;
                        break;
                    case PLCType.ModbusTCP:
                        if (m_modbusTCPClient == null)
                        {
                            m_modbusTCPClient = new ModbusTcpNet(
                                RemoteIP,
                                RemotePort,
                                (byte)m_StationCode
                            );
                        }
                        m_modbusTCPClient.IpAddress = RemoteIP;
                        m_modbusTCPClient.Port = RemotePort;
                        m_modbusTCPClient.AddressStartWithZero = m_StartWithZero;
                        m_modbusTCPClient.Station = (byte)m_StationCode;

                        if (m_DMTcpClient != null)
                            m_DMTcpClient.StopConnection(); //关闭客户端链接
                        IsConnected = m_modbusTCPClient.ConnectServer().IsSuccess; //打开ModbusTcp链接
                        break;
                }
            }
            else
            {
                switch (CommunicationType)
                {
                    case eCommunicationType.TCP客户端:
                        if (m_DMTcpClient == null)
                        {
                            m_DMTcpClient = new DMTcpClient();
                            m_DMTcpClient.OnReceviceByte += M_DMTcpClient_OnReceviceByte;
                            m_DMTcpClient.OnStateInfo += M_DMTcpClient_OnStateInfo;
                            m_DMTcpClient.OnErrorMsg += M_DMTcpClient_OnErrorMsg;
                        }
                        m_DMTcpClient.ServerIp = RemoteIP;
                        m_DMTcpClient.ServerPort = RemotePort;
                        m_DMTcpClient.StartConnection();
                        break;
                    case eCommunicationType.TCP服务器:
                        if (m_DMTcpServer == null)
                        {
                            m_DMTcpServer = new DMTcpServer();
                            m_DMTcpServer.OnReceviceByte += M_DMTcpServer_OnReceviceByte;
                            m_DMTcpServer.OnOnlineClient += M_DMTcpServer_OnOnlineClient;
                            m_DMTcpServer.OnOfflineClient += M_DMTcpServer_OnOfflineClient;
                        }
                        m_DMTcpServer.ServerIp = "0.0.0.0";
                        m_DMTcpServer.ServerPort = LocalPort;
                        IsConnected = m_DMTcpServer.Start();
                        if (IsConnected)
                        {
                            Logger.AddLog("服务器监听成功！");
                        }
                        break;
                    case eCommunicationType.UDP通讯:
                        if (m_DMUdpClient == null)
                        {
                            m_DMUdpClient = new DMUdpClient();

                            m_DMUdpClient.ReceiveByte += M_DMUdpClient_ReceiveByte;
                        }
                        m_DMUdpClient.RemoteIp = RemoteIP;
                        m_DMUdpClient.RemotePort = RemotePort;
                        m_DMUdpClient.LocalPort = LocalPort;
                        IsConnected = m_DMUdpClient.Start();
                        IsHasObjectConnected = IsConnected;

                        break;
                    case eCommunicationType.串口通讯:

                        if (m_MySerialPort == null)
                        {
                            m_MySerialPort = new MySerialPort();
                            m_MySerialPort.DataReceivedFunction += SerialPortGetBytes;
                            //m_MySerialPort.OnReceiveString += M_MySerialPort_OnReceiveString;
                        }
                        m_MySerialPort.PortName = PortName;
                        m_MySerialPort.BaudRate = BaudRate;
                        m_MySerialPort.DataBits = DataBits;
                        m_MySerialPort.StopBits = StopBits;
                        m_MySerialPort.Parity = Parity;

                        IsConnected = m_MySerialPort.OpenPort();

                        IsHasObjectConnected = IsConnected;
                        if (IsConnected)
                        {
                            Logger.AddLog($"串口({PortName})打开成功！");
                        }

                        break;
                    default:
                        break;
                }
            }

            return IsConnected;
        }

        #region PLC读写寄存器
        /// <summary>
        /// 写单个寄存器
        /// </summary>
        /// <returns></returns>
        public bool WriteRegister(int address, PLCDataWriteReadTypeEnum type, string data)
        {
            if (IsConnected == false)
                return false; //没有连接，返回false
            bool isSuccess = false;
            switch (m_PLCType)
            {
                case PLCType.ModbusRtu:
                    m_modbusRtuClient.DataFormat = (HslCommunication.Core.DataFormat)m_PLCDataType; //HslCommunication.Core.DataFormat.CDAB;
                    switch (type)
                    {
                        case PLCDataWriteReadTypeEnum.布尔:
                            isSuccess = m_modbusRtuClient
                                .Write(address.ToString(), data == "1" ? true : false)
                                .IsSuccess;
                            break;
                        case PLCDataWriteReadTypeEnum.整型:
                            if (m_IntNumber == PLCIntDataLengthEnum._16位)
                            {
                                isSuccess = m_modbusRtuClient
                                    .Write(address.ToString(), Convert.ToInt16(data))
                                    .IsSuccess;
                            }
                            else if (m_IntNumber == PLCIntDataLengthEnum._32位)
                            {
                                isSuccess = m_modbusRtuClient
                                    .Write(address.ToString(), Convert.ToInt32(data))
                                    .IsSuccess;
                            }
                            else
                            {
                                isSuccess = m_modbusRtuClient
                                    .Write(address.ToString(), Convert.ToInt64(data))
                                    .IsSuccess;
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.浮点:
                            if (m_DoubleNumber == PLCDoubleDataLengthEnum._32位)
                            {
                                isSuccess = m_modbusRtuClient
                                    .Write(address.ToString(), float.Parse(data))
                                    .IsSuccess;
                            }
                            else
                            {
                                isSuccess = m_modbusRtuClient
                                    .Write(address.ToString(), Convert.ToDouble(data))
                                    .IsSuccess;
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.字符串:
                            isSuccess = m_modbusRtuClient.Write(address.ToString(), data).IsSuccess;
                            break;
                    }
                    break;
                case PLCType.ModbusTCP:
                    m_modbusTCPClient.DataFormat = (HslCommunication.Core.DataFormat)m_PLCDataType; //HslCommunication.Core.DataFormat.CDAB;
                    switch (type)
                    {
                        case PLCDataWriteReadTypeEnum.布尔:
                            isSuccess = m_modbusTCPClient
                                .Write(address.ToString(), data == "1" ? true : false)
                                .IsSuccess;
                            break;
                        case PLCDataWriteReadTypeEnum.整型:
                            if (m_IntNumber == PLCIntDataLengthEnum._16位)
                            {
                                isSuccess = m_modbusTCPClient
                                    .Write(address.ToString(), Convert.ToInt16(data))
                                    .IsSuccess;
                            }
                            else if (m_IntNumber == PLCIntDataLengthEnum._32位)
                            {
                                isSuccess = m_modbusTCPClient
                                    .Write(address.ToString(), Convert.ToInt32(data))
                                    .IsSuccess;
                            }
                            else
                            {
                                isSuccess = m_modbusTCPClient
                                    .Write(address.ToString(), Convert.ToInt64(data))
                                    .IsSuccess;
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.浮点:
                            if (m_DoubleNumber == PLCDoubleDataLengthEnum._32位)
                            {
                                isSuccess = m_modbusTCPClient
                                    .Write(address.ToString(), float.Parse(data))
                                    .IsSuccess;
                            }
                            else
                            {
                                isSuccess = m_modbusTCPClient
                                    .Write(address.ToString(), Convert.ToDouble(data))
                                    .IsSuccess;
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.字符串:
                            isSuccess = m_modbusTCPClient.Write(address.ToString(), data).IsSuccess;
                            break;
                    }
                    break;
                default:
                    break;
            }
            return isSuccess;
        }

        /// <summary>
        /// 读单个寄存器
        /// </summary>
        /// <returns></returns>
        public bool ReadRegister(int address, PLCDataWriteReadTypeEnum type, out string data)
        {
            data = "";
            if (IsConnected == false)
                return false; //没有连接，返回false

            bool isSuccess = true;
            switch (m_PLCType)
            {
                case PLCType.ModbusRtu:
                    m_modbusRtuClient.DataFormat = (HslCommunication.Core.DataFormat)m_PLCDataType;
                    switch (type)
                    {
                        case PLCDataWriteReadTypeEnum.布尔:
                            data = m_modbusRtuClient
                                .ReadCoil(address.ToString())
                                .Content.ToString();
                            break;
                        case PLCDataWriteReadTypeEnum.整型:
                            if (m_IntNumber == PLCIntDataLengthEnum._16位)
                            {
                                data = m_modbusRtuClient
                                    .ReadInt16(address.ToString())
                                    .Content.ToString();
                            }
                            else if (m_IntNumber == PLCIntDataLengthEnum._32位)
                            {
                                data = m_modbusRtuClient
                                    .ReadInt32(address.ToString())
                                    .Content.ToString();
                            }
                            else
                            {
                                data = m_modbusRtuClient
                                    .ReadInt64(address.ToString())
                                    .Content.ToString();
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.浮点:
                            if (m_DoubleNumber == PLCDoubleDataLengthEnum._32位)
                            {
                                data = m_modbusRtuClient
                                    .ReadFloat(address.ToString())
                                    .Content.ToString();
                            }
                            else
                            {
                                data = m_modbusRtuClient
                                    .ReadDouble(address.ToString())
                                    .Content.ToString();
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.字符串:
                            break;
                    }
                    break;
                case PLCType.ModbusTCP:
                    m_modbusTCPClient.DataFormat = (HslCommunication.Core.DataFormat)m_PLCDataType; //HslCommunication.Core.DataFormat.CDAB;
                    switch (type)
                    {
                        case PLCDataWriteReadTypeEnum.布尔:
                            data = m_modbusTCPClient
                                .ReadCoil(address.ToString())
                                .Content.ToString();
                            break;
                        case PLCDataWriteReadTypeEnum.整型:
                            if (m_IntNumber == PLCIntDataLengthEnum._16位)
                            {
                                data = m_modbusTCPClient
                                    .ReadInt16(address.ToString())
                                    .Content.ToString();
                            }
                            else if (m_IntNumber == PLCIntDataLengthEnum._32位)
                            {
                                data = m_modbusTCPClient
                                    .ReadInt32(address.ToString())
                                    .Content.ToString();
                            }
                            else
                            {
                                data = m_modbusTCPClient
                                    .ReadInt64(address.ToString())
                                    .Content.ToString();
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.浮点:
                            if (m_DoubleNumber == PLCDoubleDataLengthEnum._32位)
                            {
                                data = m_modbusTCPClient
                                    .ReadFloat(address.ToString())
                                    .Content.ToString();
                            }
                            else
                            {
                                data = m_modbusTCPClient
                                    .ReadDouble(address.ToString())
                                    .Content.ToString();
                            }
                            break;
                        case PLCDataWriteReadTypeEnum.字符串:
                            break;
                    }
                    break;
                default:
                    break;
            }
            return isSuccess;
        }
        #endregion
        private void M_DMTcpClient_OnErrorMsg(string msg)
        {
            if (IsHasObjectConnected == true)
            {
                IsConnected = false;
                IsHasObjectConnected = false;
                Logger.AddLog(
                    $"与服务器的连接断开  {RemoteIP}:{RemotePort}",
                    Common.Enums.eMsgType.Error,
                    isDispGrowl: true
                );
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        private void M_DMTcpClient_OnStateInfo(string msg, SocketState state)
        {
            if (m_IsPlcDisconnect == true)
                return;

            switch (state)
            {
                case SocketState.Connecting:
                    break;
                case SocketState.Connected:
                    IsHasObjectConnected = true;
                    IsConnected = true;
                    Logger.AddLog(
                        $"已成功连接服务器  {RemoteIP}:{RemotePort}",
                        Common.Enums.eMsgType.Info,
                        isDispGrowl: true
                    );
                    EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                    break;
                case SocketState.Reconnection:
                    break;
                case SocketState.Disconnect:
                    if (IsHasObjectConnected == true)
                    {
                        IsConnected = false;
                        IsHasObjectConnected = false;
                        Logger.AddLog(
                            $"与服务器的连接断开  {RemoteIP}:{RemotePort}",
                            Common.Enums.eMsgType.Error,
                            isDispGrowl: true
                        );
                        EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
                    }
                    break;
                case SocketState.StartListening:
                    break;
                case SocketState.StopListening:
                    break;
                case SocketState.ClientOnline:
                    break;
                case SocketState.ClientOnOff:
                    break;
                default:
                    break;
            }
        }

        //作为服务器 ,客户端已经下线
        private void M_DMTcpServer_OnOfflineClient(Socket temp)
        {
            IsHasObjectConnected = false;
            m_SocketIpPortList.Remove(temp.RemoteEndPoint.ToString());
            Logger.AddLog(
                $"{temp.RemoteEndPoint.ToString()}客户端已断开连接",
                Common.Enums.eMsgType.Error,
                isDispGrowl: true
            );
            if (IsHasObjectConnected == true) //还有连接则提示剩余信息
            {
                Logger.AddLog(
                    $"当前连接的客户端数量为 {m_ObjectConnectedCount} , \r\n{string.Join("\r\n", m_SocketIpPortList)}",
                    Common.Enums.eMsgType.Info,
                    isDispGrowl: true
                );
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        //作为服务器 ,客户端已经上线
        private void M_DMTcpServer_OnOnlineClient(Socket temp)
        {
            IsHasObjectConnected = true;
            m_SocketIpPortList.Add(temp.RemoteEndPoint.ToString());
            Logger.AddLog(
                $"{temp.RemoteEndPoint.ToString()} 客户端已经连接",
                Common.Enums.eMsgType.Info,
                isDispGrowl: true
            );
            if (m_ObjectConnectedCount > 1) //还有连接则提示剩余信息
            {
                Logger.AddLog(
                    $"当前连接的客户端数量为 {m_ObjectConnectedCount} , \r\n{string.Join("\r\n", m_SocketIpPortList)}",
                    Common.Enums.eMsgType.Info,
                    isDispGrowl: true
                );
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        public void DisConnect()
        {
            m_IsPlcDisconnect = false;
            IsConnected = false;
            StopRecStrSignal();
            if (IsPLC)
            {
                switch (m_PLCType)
                {
                    case PLCType.ModbusRtu:

                        break;
                    case PLCType.ModbusTCP:
                        if (m_modbusTCPClient != null)
                            m_modbusTCPClient.ConnectClose();
                        if (m_DMTcpClient != null)
                            m_DMTcpClient.StopConnection();
                        break;
                }
            }
            else
            {
                switch (CommunicationType)
                {
                    case eCommunicationType.TCP客户端:
                        if (m_DMTcpClient != null)
                            m_DMTcpClient.StopConnection();
                        break;
                    case eCommunicationType.TCP服务器:
                        if (m_DMTcpServer != null)
                            m_DMTcpServer.Stop();
                        break;
                    case eCommunicationType.UDP通讯:
                        if (m_DMUdpClient != null)
                            m_DMUdpClient.Stop();
                        IsHasObjectConnected = false;
                        break;
                    case eCommunicationType.串口通讯:
                        if (m_MySerialPort != null)
                            m_MySerialPort.ClosePort();
                        IsHasObjectConnected = false;
                        break;
                    default:
                        break;
                }
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        /// <summary>
        /// 三菱plc连接的时候 只能有一个客户端连接,plc访问的时候 先断开 eplc 故单独使用这个
        /// </summary>
        public void PlcDisConnect()
        {
            m_IsPlcDisconnect = true;
            StopRecStrSignal();

            switch (CommunicationType)
            {
                case eCommunicationType.TCP客户端:
                    if (m_DMTcpClient != null)
                        m_DMTcpClient.StopConnection();
                    break;
                default:
                    break;
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        public bool SendStr(string str)
        {
            lock (this)
            {
                bool flag = false;
                if (IsConnected == false)
                {
                    return false;
                }
                if (IsSendByHex == true)
                {
                    Logger.AddLog($"16进制数据:{HexTool.ToBytesFromHexString(str)}");
                }
                switch (CommunicationType)
                {
                    case eCommunicationType.TCP客户端:
                        if (m_DMTcpClient != null)
                        {
                            flag = (bool)m_DMTcpClient?.SendCommand(str, IsSendByHex);
                        }

                        break;
                    case eCommunicationType.TCP服务器:
                        if (m_DMTcpServer != null)
                        {
                            try
                            {
                                //强制下线,而此时正在发送数据,会到导致ClientSocketList内容变了
                                foreach (Socket s in m_DMTcpServer.ClientSocketList)
                                {
                                    if (s.Connected)
                                    {
                                        m_DMTcpServer?.SendData(
                                            ((IPEndPoint)s.RemoteEndPoint).Address.ToString(),
                                            ((IPEndPoint)s.RemoteEndPoint).Port,
                                            str,
                                            IsSendByHex
                                        );
                                        flag = true;
                                    }
                                }
                            }
                            catch (Exception) { }
                        }
                        break;
                    case eCommunicationType.UDP通讯:
                        if (m_DMUdpClient != null)
                        {
                            m_DMUdpClient?.SendText(str, IsSendByHex);
                            flag = true;
                        }
                        break;
                    case eCommunicationType.串口通讯:
                        if (m_MySerialPort != null)
                        {
                            flag = (bool)m_MySerialPort?.WriteData(str, IsSendByHex);
                        }
                        break;
                    default:
                        break;
                }

                if (flag == true)
                {
                    Logger.AddLog($"[{Key}]发送数据:{str}");
                }
                else
                {
                    Logger.AddLog($"[{Key}]发送数据失败", Common.Enums.eMsgType.Error);
                }
                return flag;
            }
        }

        public void GetStr(out string pReturnStr)
        {
            string str = "";

            m_IsStartRec = true; //开始监听回调

            if (m_RecStrSignal == null)
                m_RecStrSignal = new AutoResetEvent(false);
            m_RecStrSignal.Reset(); //需要加这一句,因为断开连接的时候会执行 m_RecStrSignal.Set()

            if (m_RecStrQueue.Count > 0)
            {
                str = m_RecStrQueue.Dequeue();
            }
            else
            {
                if (m_RecStrSignal == null)
                    m_RecStrSignal = new AutoResetEvent(false);
                m_RecStrSignal.WaitOne();

                if (m_RecStrQueue.Count > 0)
                {
                    str = m_RecStrQueue.Dequeue();
                }
            }

            pReturnStr = str.Trim(); //最终赋值
        }

        public void StopRecStrSignal()
        {
            lock (this)
            {
                m_IsStartRec = false;
                m_RecStrQueue.Clear();
                if (m_RecStrSignal == null)
                    m_RecStrSignal = new AutoResetEvent(false);
                m_RecStrSignal.Set(); //停止阻塞 当项目停止的时候 停止阻塞
            }
        }

        // tcp服务端接收数据
        private void M_DMTcpServer_OnReceviceByte(System.Net.Sockets.Socket temp, byte[] dataBytes)
        {
            lock (this)
            {
                string str = "";
                if (IsReceivedByHex == true)
                {
                    str = HexTool.ToHexStringFromDataBytes(dataBytes);
                }
                else
                {
                    str = Encoding.Default.GetString(dataBytes).Trim().Trim('\0');
                }
                if (!string.IsNullOrWhiteSpace(str))
                {
                    ReceiveString?.Invoke(str);
                    AddRecString(str);
                }
            }
        }

        // tcp客户端接收数据
        private void M_DMTcpClient_OnReceviceByte(byte[] dataBytes)
        {
            lock (this)
            {
                string str = "";
                if (IsReceivedByHex == true)
                {
                    str = HexTool.ToHexStringFromDataBytes(dataBytes);
                }
                else
                {
                    str = Encoding.Default.GetString(dataBytes).Trim().Trim('\0');
                }
                if (!string.IsNullOrWhiteSpace(str))
                {
                    ReceiveString?.Invoke(str);
                    AddRecString(str);
                }
            }
        }

        //串口接收数据
        private void M_MySerialPort_OnReceiveString(string str)
        {
            lock (this)
            {
                str = str.Trim('\0');
                if (!string.IsNullOrWhiteSpace(str))
                {
                    //if (IsReceivedByHex == true) str = HexTool.ToBytesFromHexString(str).ToString();
                    ReceiveString?.Invoke(str);
                    AddRecString(str);
                }
            }
        }

        /// <summary>
        /// 串口从这里 把字节数据传出来
        /// </summary>
        /// <param name="serialPort"></param>
        /// <returns></returns>
        private string SerialPortGetBytes(SerialPort serialPort)
        {
            byte[] ReDatas = new byte[serialPort.BytesToRead];
            serialPort.Read(ReDatas, 0, ReDatas.Length);

            string str = "";
            if (IsReceivedByHex == true)
            {
                str = HexTool.ToHexStringFromDataBytes(ReDatas);
            }
            else
            {
                str = Encoding.Default.GetString(ReDatas).Trim().Trim('\0');
            }
            if (!string.IsNullOrWhiteSpace(str))
            {
                ReceiveString?.Invoke(str);
                AddRecString(str);
            }
            return str;
        }

        //udp接收数据
        private void M_DMUdpClient_ReceiveByte(ReceiveDataEventArgs e)
        {
            lock (this)
            {
                string str = "";
                if (IsReceivedByHex == true)
                {
                    str = HexTool.ToHexStringFromDataBytes(e.Buffer);
                }
                else
                {
                    str = Encoding.Default.GetString(e.Buffer).Trim().Trim('\0');
                }
                if (!string.IsNullOrWhiteSpace(str))
                {
                    if (IsReceivedByHex == true)
                        str = HexTool.StrToHexStr(str);
                    ReceiveString?.Invoke(str);
                    AddRecString(str);
                }
            }
        }

        /// <summary>
        /// 返回当前ecom的 Socket 只有网络通讯才有,目前只支持tcpclient
        /// </summary>
        /// <returns></returns>
        public Socket GetSocket()
        {
            switch (CommunicationType)
            {
                case eCommunicationType.TCP客户端:
                    if (m_DMTcpClient != null)
                    {
                        return m_DMTcpClient.Tcpclient?.Client;
                    }
                    break;
                case eCommunicationType.TCP服务器:
                    Logger.AddLog(" TcpServer暂不支持获取 socket");
                    break;
                case eCommunicationType.UDP通讯:
                    Logger.AddLog(" UDP暂不支持获取 socket");
                    break;
                case eCommunicationType.串口通讯:
                    Logger.AddLog(" COM不支持获取 socket");
                    break;
                default:
                    break;
            }

            return null;
        }

        public string GetInfoStr()
        {
            string str = "";
            switch (CommunicationType)
            {
                case eCommunicationType.TCP客户端:
                    str = $"远程主机: {RemoteIP}:{RemotePort}";
                    break;
                case eCommunicationType.TCP服务器:
                    str =
                        $"本地主机: 0.0.0.0:{LocalPort}\r\n客户端连接数量: {m_ObjectConnectedCount}\r\n客户端信息:\r\n{String.Join("\r\n", m_SocketIpPortList)}";

                    break;
                case eCommunicationType.UDP通讯:
                    str = $"本地主机: 0.0.0.0:{LocalPort}\r\n远程主机: {RemoteIP}:{RemotePort}";
                    break;
                case eCommunicationType.串口通讯:
                    str =
                        $"串口号: {PortName}\r\n波特率: {BaudRate}\r\n校验位: {Parity}\r\n数据位: {DataBits}\r\n停止位: {StopBits}";
                    break;
                default:
                    break;
            }

            return str;
        }

        /// <summary>
        /// 设置串口回调数据解析规则
        /// </summary>
        /// <param name="function"></param>
        public void SetSerialPortDataReceivedFunction(SerialPortDataReceivedFunction function)
        {
            //
            if (CommunicationType == eCommunicationType.串口通讯)
            {
                m_MySerialPort.DataReceivedFunction = function;
            }
        }
    }
}
