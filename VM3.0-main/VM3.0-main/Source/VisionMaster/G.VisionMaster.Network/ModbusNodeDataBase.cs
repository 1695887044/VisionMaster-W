using G.Extensions.Validation.Attributes;
using G.Services.Logger;
using G.VisionMaster.NodeData;
using G.VisionMaster.NodeData.Base;
using NModbus;
using System.Net.Sockets;

namespace G.VisionMaster.Network;

public abstract class ModbusNodeDataBase : DemoNodeDataBase, IVisionNodeData
{
    private IModbusMaster _master;
    private TcpClient _client;
    protected IModbusMaster Master => this._master;
    protected TcpClient TcpClient => this._client;
    private bool _useInvokedPart = true;
    [DefaultValue(true)]
    [Display(Name = "启用输出历史记录", GroupName = VisionPropertyGroupNames.DisplayParameters, Description = "用于控制是否输出到历史记录和预览图像")]
    public bool UseInvokedPart
    {
        get { return _useInvokedPart; }
        set
        {
            _useInvokedPart = value;
            RaisePropertyChanged();
        }
    }

    private string _ip = "127.0.0.1";
    [IPAddress]
    [DefaultValue("127.0.0.1")]
    [Display(Name = "Slave IP", GroupName = VisionPropertyGroupNames.RunParameters, Description = "Modbus地址")]
    public string Ip
    {
        get { return _ip; }
        set
        {
            _ip = value;
            RaisePropertyChanged();
        }
    }

    private int _port = 502;
    [DefaultValue(502)]
    [Display(Name = "Slave端口号", GroupName = VisionPropertyGroupNames.RunParameters, Description = "Modbus端口号")]
    public int Port
    {
        get { return _port; }
        set
        {
            _port = value;
            RaisePropertyChanged();
        }
    }

    private byte _slaveAddress = 1;
    [DefaultValue(1)]
    [Display(Name = "Slave地址", GroupName = VisionPropertyGroupNames.RunParameters, Description = "要从中读取值的设备的地址")]
    public byte SlaveAddress
    {
        get { return _slaveAddress; }
        set
        {
            _slaveAddress = value;
            RaisePropertyChanged();
        }
    }

    private DateTime? _updateTime;
    [Browsable(false)]
    [Display(Name = "更新时间", GroupName = VisionPropertyGroupNames.RunParameters, Description = "读取位置")]
    public DateTime? UpdateTime
    {
        get { return _updateTime; }
        set
        {
            _updateTime = value;
            RaisePropertyChanged();
        }
    }

    private int _SleepMilliseconds = 100;
    [DefaultValue(100)]
    [Display(Name = "时间间隔", GroupName = VisionPropertyGroupNames.RunParameters, Description = "读取间隔")]
    public int SleepMilliseconds
    {
        get { return _SleepMilliseconds; }
        set
        {
            _SleepMilliseconds = value;
            RaisePropertyChanged();
        }
    }

    private ModbusState _ModbusState = ModbusState.Stopped;
    [ReadOnly(true)]
    [DefaultValue(ModbusState.Stopped)]
    [Display(Name = "连接状态", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "当前状态")]
    public ModbusState ModbusState
    {
        get { return _ModbusState; }
        set
        {
            _ModbusState = value;
            RaisePropertyChanged();
        }
    }

    protected bool Connect()
    {

        this._client?.Dispose();
        try
        {
            this._client = new TcpClient(this.Ip, this.Port);
            ModbusFactory factory = new ModbusFactory();
            this._master = factory.CreateMaster(this._client);
            this._master.Transport.ReadTimeout = 100;
            this._master.Transport.Retries = 3;
            this._master.Transport.SlaveBusyUsesRetryCount = true;
            return true;
        }
        catch (Exception ex)
        {
            IocLog.Instance?.Error(ex);
        }
        return false;
    }

    public override void Dispose()
    {
        this._client?.Dispose();
        this._client = null;
        this._master?.Dispose();
        this._master = null;
        base.Dispose();
    }

}

