using System;
using System.IO.Ports;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Serial
{
	/// <summary>
	/// 所有串行通信类的基类，提供了一些基础的服务，核心的通信实现<br />
	/// The base class of all serial communication classes provides some basic services for the core communication implementation
	/// </summary>
	public class SerialBase : BinaryCommunication, IDisposable
	{
		private bool disposedValue = false;

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
		/// 实例化一个无参的构造方法<br />
		/// Instantiate a parameterless constructor
		/// </summary>
		public SerialBase()
		{
			CommunicationPipe = new PipeSerialPort();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.String)" />
		public virtual void SerialPortInni(string portName)
		{
			pipe.SerialPortInni(portName);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceSerialPort.SerialPortInni(System.String,System.Int32)" />
		public virtual void SerialPortInni(string portName, int baudRate)
		{
			SerialPortInni(portName, baudRate, 8, StopBits.One, Parity.None);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.String,System.Int32,System.Int32,System.IO.Ports.StopBits,System.IO.Ports.Parity)" />
		public virtual void SerialPortInni(string portName, int baudRate, int dataBits, StopBits stopBits, Parity parity)
		{
			pipe.SerialPortInni(portName, baudRate, dataBits, stopBits, parity);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.SerialPortInni(System.Action{System.IO.Ports.SerialPort})" />
		public void SerialPortInni(Action<SerialPort> initi)
		{
			pipe.SerialPortInni(initi);
			PortName = pipe.GetPipe().PortName;
			BaudRate = pipe.GetPipe().BaudRate;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSerialPort.OpenCommunication" />
		public virtual OperateResult Open()
		{
			OperateResult<bool> operateResult = pipe.OpenCommunication();
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

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceSerialPort.IsOpen" />
		public bool IsOpen()
		{
			return pipe.GetPipe().IsOpen;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceSerialPort.Close" />
		public void Close()
		{
			if (pipe.GetPipe().IsOpen)
			{
				ExtraOnDisconnect();
				pipe.CloseCommunication();
			}
		}

		/// <summary>
		/// 释放当前的对象
		/// </summary>
		/// <param name="disposing">是否在</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					pipe?.CloseCommunication();
				}
				disposedValue = true;
			}
		}

		/// <summary>
		/// 释放当前的对象
		/// </summary>
		public void Dispose()
		{
			Dispose(disposing: true);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SerialBase{CommunicationPipe}";
		}
	}
}
