using System;
using System.IO.Ports;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Device
{
	/// <summary>
	/// 串口的设备类对象信息
	/// </summary>
	public class DeviceSerialPort : DeviceCommunication
	{
		private PipeSerialPort pipe;

		/// <inheritdoc />
		public override CommunicationPipe CommunicationPipe
		{
			get
			{
				return base.CommunicationPipe;
			}
			set
			{
				base.CommunicationPipe = value;
				PipeSerialPort pipeSerialPort = value as PipeSerialPort;
				if (pipeSerialPort != null)
				{
					pipe = pipeSerialPort;
					PortName = pipe.GetPipe().PortName;
					BaudRate = pipe.GetPipe().BaudRate;
				}
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeSerialPort.RtsEnable" />
		[HslMqttApi(Description = "Gets or sets a value indicating whether the request sending (RTS) signal is enabled in serial communication.")]
		public bool RtsEnable
		{
			get
			{
				return pipe.RtsEnable;
			}
			set
			{
				pipe.RtsEnable = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeSerialPort.ReceiveEmptyDataCount" />
		[HslMqttApi(Description = "Get or set the number of consecutive empty data receptions, which is valid when data reception is completed, default is 1")]
		public int ReceiveEmptyDataCount
		{
			get
			{
				return pipe.ReceiveEmptyDataCount;
			}
			set
			{
				pipe.ReceiveEmptyDataCount = value;
			}
		}

		/// <summary>
		/// 是否在发送数据前清空缓冲数据，默认是false<br />
		/// Whether to empty the buffer before sending data, the default is false
		/// </summary>
		[HslMqttApi(Description = "Whether to empty the buffer before sending data, the default is false")]
		public bool IsClearCacheBeforeRead
		{
			get
			{
				return pipe.IsClearCacheBeforeRead;
			}
			set
			{
				pipe.IsClearCacheBeforeRead = value;
			}
		}

		/// <summary>
		/// 当前连接串口信息的端口号名称<br />
		/// The port name of the current connection serial port information
		/// </summary>
		[HslMqttApi(Description = "The port name of the current connection serial port information")]
		public string PortName { get; private set; }

		/// <summary>
		/// 当前连接串口信息的波特率<br />
		/// Baud rate of current connection serial port information
		/// </summary>
		[HslMqttApi(Description = "Baud rate of current connection serial port information")]
		public int BaudRate { get; private set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public DeviceSerialPort()
		{
			pipe = new PipeSerialPort();
			CommunicationPipe = pipe;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.String)" />
		public virtual void SerialPortInni(string portName)
		{
			pipe.SerialPortInni(portName);
		}

		/// <summary>
		/// 初始化串口信息，波特率，8位数据位，1位停止位，无奇偶校验<br />
		/// Initializes serial port information, baud rate, 8-bit data bit, 1-bit stop bit, no parity
		/// </summary>
		/// <param name="portName">端口号信息，例如"COM3"</param>
		/// <param name="baudRate">波特率</param>
		public virtual void SerialPortInni(string portName, int baudRate)
		{
			SerialPortInni(portName, baudRate, 8, StopBits.One, Parity.None);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.String,System.Int32,System.Int32,System.IO.Ports.StopBits,System.IO.Ports.Parity)" />
		public virtual void SerialPortInni(string portName, int baudRate, int dataBits, StopBits stopBits, Parity parity)
		{
			pipe.SerialPortInni(portName, baudRate, dataBits, stopBits, parity);
			PortName = portName;
			BaudRate = baudRate;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.Action{System.IO.Ports.SerialPort})" />
		public void SerialPortInni(Action<SerialPort> initi)
		{
			pipe.SerialPortInni(initi);
			PortName = pipe.GetPipe().PortName;
			BaudRate = pipe.GetPipe().BaudRate;
		}

		/// <summary>
		/// 打开一个新的串行端口连接<br />
		/// Open a new serial port connection
		/// </summary>
		public virtual OperateResult Open()
		{
			OperateResult<bool> operateResult = CommunicationPipe.OpenCommunication();
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content)
			{
				return InitializationOnConnect();
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 获取一个值，指示串口是否处于打开状态<br />
		/// Gets a value indicating whether the serial port is open
		/// </summary>
		/// <returns>是或否</returns>
		public bool IsOpen()
		{
            //return (CommunicationPipe as PipeSerialPort)?.GetPipe().IsOpen ?? (CommunicationPipe as PipeMoxa)?.IsOpen() ?? pipe.GetPipe().IsOpen;
            return (CommunicationPipe as PipeSerialPort)?.GetPipe().IsOpen  ?? pipe.GetPipe().IsOpen;
        }

		/// <summary>
		/// 关闭当前的串口连接<br />
		/// Close the current serial connection
		/// </summary>
		public void Close()
		{
			if (CommunicationPipe is PipeSerialPort)
			{
				if (pipe.GetPipe().IsOpen)
				{
					ExtraOnDisconnect();
					pipe.CloseCommunication();
				}
			}
			else
			{
				ExtraOnDisconnect();
				CommunicationPipe.CloseCommunication();
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DeviceSerialPort<{base.ByteTransform}>{{{CommunicationPipe}}}";
		}
	}
}
