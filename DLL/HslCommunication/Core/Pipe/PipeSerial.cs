using System;
using System.IO.Ports;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// 串口的管道类对象，可以在不同的串口类中使用一个串口的通道信息<br />
	/// The pipe class object of the serial port can use the channel information of a serial port in different serial port classes
	/// </summary>
	[Obsolete("Use class PipeSerialPort instead")]
	public class PipeSerial : PipeBase, IDisposable
	{
		private SerialPort serialPort;

		/// <summary>
		/// 获取或设置一个值，该值指示在串行通信中是否启用请求发送 (RTS) 信号。<br />
		/// Gets or sets a value indicating whether the request sending (RTS) signal is enabled in serial communication.
		/// </summary>
		public bool RtsEnable
		{
			get
			{
				return serialPort.RtsEnable;
			}
			set
			{
				serialPort.RtsEnable = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public PipeSerial()
		{
			serialPort = new SerialPort();
		}

		/// <summary>
		/// 初始化串口信息，波特率，数据位，停止位，奇偶校验需要全部自己来指定<br />
		/// Start serial port information, baud rate, data bit, stop bit, parity all need to be specified
		/// </summary>
		/// <param name="portName">端口号信息，例如"COM3"</param>
		/// <param name="baudRate">波特率</param>
		/// <param name="dataBits">数据位</param>
		/// <param name="stopBits">停止位</param>
		/// <param name="parity">奇偶校验</param>
		public void SerialPortInni(string portName, int baudRate, int dataBits, StopBits stopBits, Parity parity)
		{
			if (!serialPort.IsOpen)
			{
				serialPort.PortName = portName;
				serialPort.BaudRate = baudRate;
				serialPort.DataBits = dataBits;
				serialPort.StopBits = stopBits;
				serialPort.Parity = parity;
			}
		}

		/// <summary>
		/// 根据自定义初始化方法进行初始化串口信息<br />
		/// Initialize the serial port information according to the custom initialization method
		/// </summary>
		/// <param name="initi">初始化的委托方法</param>
		public void SerialPortInni(Action<SerialPort> initi)
		{
			if (!serialPort.IsOpen)
			{
				serialPort.PortName = "COM1";
				initi(serialPort);
			}
		}

		/// <summary>
		/// 打开一个新的串行端口连接<br />
		/// Open a new serial port connection
		/// </summary>
		public OperateResult Open()
		{
			try
			{
				if (!serialPort.IsOpen)
				{
					serialPort.Open();
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
		}

		/// <summary>
		/// 获取一个值，指示串口是否处于打开状态<br />
		/// Gets a value indicating whether the serial port is open
		/// </summary>
		/// <returns>是或否</returns>
		public bool IsOpen()
		{
			return serialPort.IsOpen;
		}

		/// <summary>
		/// 关闭当前的串口连接<br />
		/// Close the current serial connection
		/// </summary>
		public OperateResult Close(Func<SerialPort, OperateResult> extraOnClose)
		{
			if (serialPort.IsOpen)
			{
				if (extraOnClose != null)
				{
					OperateResult operateResult = extraOnClose(serialPort);
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
				}
				try
				{
					serialPort.Close();
				}
				catch (Exception ex)
				{
					return new OperateResult(ex.Message);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public override void Dispose()
		{
			base.Dispose();
			serialPort?.Dispose();
		}

		/// <summary>
		/// 获取当前的串口对象信息<br />
		/// Get current serial port object information
		/// </summary>
		/// <returns>串口对象</returns>
		public SerialPort GetPipe()
		{
			return serialPort;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"PipeSerial[{serialPort.PortName},{serialPort.BaudRate},{serialPort.DataBits},{serialPort.StopBits},{serialPort.Parity}]";
		}
	}
}
