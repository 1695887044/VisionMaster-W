using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// 串口管道信息
	/// </summary>
	public class PipeSerialPort : CommunicationPipe, IDisposable
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
		/// 获取或设置一个值，该值指示在串行通信中是否启用数据终端就绪 (Drt) 信号。<br />
		/// Gets or sets a value that indicates whether the Data Terminal Ready (DRT) signal is enabled in serial communication.
		/// </summary>
		public bool DtrEnable
		{
			get
			{
				return serialPort.DtrEnable;
			}
			set
			{
				serialPort.DtrEnable = value;
			}
		}

		/// <summary>
		/// 从串口中至少接收的字节长度信息，默认为1个字节
		/// </summary>
		public int AtLeastReceiveLength { get; set; } = 1;


		/// <summary>
		/// 获取或设置连续接收空的数据次数，在数据接收完成时有效，每个单位消耗的时间为<see cref="P:HslCommunication.Core.Pipe.CommunicationPipe.SleepTime" />。<br />
		/// Obtain or set the number of consecutive times to receive empty data, which is valid when the data is received, and the time consumed by each unit is <see cref="P:HslCommunication.Core.Pipe.CommunicationPipe.SleepTime" />
		/// </summary>
		[HslMqttApi(Description = "Get or set the number of consecutive empty data receptions, which is valid when data reception is completed, default is 1")]
		public int ReceiveEmptyDataCount { get; set; } = 1;


		/// <summary>
		/// 是否在发送数据前清空缓冲数据，默认是false<br />
		/// Whether to empty the buffer before sending data, the default is false
		/// </summary>
		[HslMqttApi(Description = "Whether to empty the buffer before sending data, the default is false")]
		public bool IsClearCacheBeforeRead { get; set; }

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public PipeSerialPort()
		{
			serialPort = new SerialPort();
			base.SleepTime = 20;
		}

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		/// <param name="portName">
		/// portName 支持格式化的方式，例如输入 COM3-9600-8-N-1，COM5-19200-7-E-2，其中奇偶校验的字母可选，N:无校验，O：奇校验，E:偶校验，停止位可选 0, 1, 2, 1.5 四种选项
		/// </param>
		public PipeSerialPort(string portName)
		{
			serialPort = new SerialPort();
			base.SleepTime = 20;
			SerialPortInni(portName);
		}

		/// <summary>
		/// 初始化串口信息，9600波特率，8位数据位，1位停止位，无奇偶校验<br />
		/// Initial serial port information, 9600 baud rate, 8 data bits, 1 stop bit, no parity
		/// </summary>
		/// <remarks>
		/// portName 支持格式化的方式，例如输入 COM3-9600-8-N-1，COM5-19200-7-E-2，其中奇偶校验的字母可选，N:无校验，O：奇校验，E:偶校验，停止位可选 0, 1, 2, 1.5 四种选项
		/// </remarks>
		/// <param name="portName">端口号信息，例如"COM3"</param>
		public void SerialPortInni(string portName)
		{
			if (portName.Contains("-") || portName.Contains(";"))
			{
				SerialPortInni(delegate(SerialPort sp)
				{
					sp.IniSerialByFormatString(portName);
				});
			}
			else
			{
				SerialPortInni(portName, 9600, 8, StopBits.One, Parity.None);
			}
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
		/// 获取一个值，指示串口是否处于打开状态<br />
		/// Gets a value indicating whether the serial port is open
		/// </summary>
		/// <returns>是或否</returns>
		public bool IsOpen()
		{
			return serialPort.IsOpen;
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

		/// <summary>
		/// 清除串口缓冲区的数据，并返回该数据，如果缓冲区没有数据，返回的字节数组长度为0<br />
		/// The number sent clears the data in the serial port buffer and returns that data, or if there is no data in the buffer, the length of the byte array returned is 0
		/// </summary>
		/// <returns>是否操作成功的方法</returns>
		public OperateResult<byte[]> ClearSerialCache()
		{
			return SPReceived(serialPort, null, null, awaitData: false);
		}

		/// <inheritdoc />
		public override OperateResult<bool> OpenCommunication()
		{
			try
			{
				if (!serialPort.IsOpen)
				{
					serialPort.Open();
					ResetConnectErrorCount();
					return OperateResult.CreateSuccessResult(value: true);
				}
				return OperateResult.CreateSuccessResult(value: false);
			}
			catch (Exception ex)
			{
				return new OperateResult<bool>("OpenCommunication failed: " + ex.Message);
			}
		}

		/// <inheritdoc />
		public override OperateResult CloseCommunication()
		{
			if (serialPort.IsOpen)
			{
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

		/// <inheritdoc />
		public override OperateResult Send(byte[] data, int offset, int size)
		{
			if (data != null && data.Length != 0)
			{
				
				try
				{
					serialPort.Write(data, offset, size);
					return OperateResult.CreateSuccessResult();
				}
				catch (Exception ex)
				{
					return new OperateResult(-IncrConnectErrorCount(), ex.Message);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult<int> Receive(byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			
			try
			{
				if (length > 0)
				{
					int value = serialPort.Read(buffer, offset, length);
					return OperateResult.CreateSuccessResult(value);
				}
				int value2 = serialPort.Read(buffer, offset, buffer.Length - offset);
				return OperateResult.CreateSuccessResult(value2);
			}
			catch (Exception ex)
			{
				return new OperateResult<int>(-IncrConnectErrorCount(), ex.Message);
			}
		}

		/// <summary>
		/// 从串口接收一串字节数据信息，直到没有数据为止，如果参数awaitData为false, 第一轮接收没有数据则返回<br />
		/// Receives a string of bytes of data information from the serial port until there is no data, and returns if the parameter awaitData is false
		/// </summary>
		/// <param name="serialPort">串口对象</param>
		/// <param name="netMessage">定义的消息体对象</param>
		/// <param name="sendValue">等待发送的数据对象</param>
		/// <param name="awaitData">是否必须要等待数据返回</param>
		/// <param name="logMessage">用于消息记录的日志信息</param>
		/// <returns>结果数据对象</returns>
		private OperateResult<byte[]> SPReceived(SerialPort serialPort, INetMessage netMessage, byte[] sendValue, bool awaitData, Action<byte[]> logMessage = null)
		{
			
			byte[] array = null;
			MemoryStream memoryStream = null;
			try
			{
				array = new byte[1024];
				memoryStream = new MemoryStream();
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
			DateTime now = DateTime.Now;
			int num = 0;
			int num2 = 0;
			while (true)
			{
				num2++;
				if (num2 > 1 && base.SleepTime >= 0)
				{
					HslHelper.ThreadSleep(base.SleepTime);
				}
				try
				{
					if (serialPort.BytesToRead < 1)
					{
						if (num2 == 1)
						{
							continue;
						}
						if ((DateTime.Now - now).TotalMilliseconds > (double)base.ReceiveTimeOut)
						{
							return new OperateResult<byte[]>(-IncrConnectErrorCount(), $"Time out: {base.ReceiveTimeOut}, received: {memoryStream.ToArray().ToHexString(' ')}");
						}
						if (memoryStream.Length >= AtLeastReceiveLength)
						{
							num++;
							if (netMessage == null && num >= ReceiveEmptyDataCount)
							{
								break;
							}
						}
						else if (!awaitData)
						{
							break;
						}
						continue;
					}
					num = 0;
					int num3 = serialPort.Read(array, 0, array.Length);
					if (num3 > 0)
					{
						memoryStream.Write(array, 0, num3);
						logMessage?.Invoke(array.SelectBegin(num3));
					}
					if (netMessage != null && CheckMessageComplete(netMessage, sendValue, ref memoryStream))
					{
						break;
					}
					if (base.ReceiveTimeOut > 0 && (DateTime.Now - now).TotalMilliseconds > (double)base.ReceiveTimeOut)
					{
						return new OperateResult<byte[]>(-IncrConnectErrorCount(), $"Time out: {base.ReceiveTimeOut}, received: {memoryStream.ToArray().ToHexString(' ')}");
					}
				}
				catch (Exception ex2)
				{
					return new OperateResult<byte[]>(-IncrConnectErrorCount(), ex2.Message);
				}
			}
			ResetConnectErrorCount();
			return OperateResult.CreateSuccessResult(memoryStream.ToArray());
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReceiveMessage(INetMessage netMessage, byte[] sendValue, bool useActivePush = true, Action<long, long> reportProgress = null, Action<byte[]> logMessage = null)
		{
			if (base.UseServerActivePush)
			{
				return base.ReceiveMessage(netMessage, sendValue, useActivePush, reportProgress);
			}
			return SPReceived(serialPort, netMessage, sendValue, awaitData: true, logMessage);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(INetMessage netMessage, byte[] sendValue, bool hasResponseData, Action<byte[]> logMessage = null)
		{
			if (IsClearCacheBeforeRead)
			{
				ClearSerialCache();
			}
			OperateResult<byte[]> operateResult = ReadFromCoreServerHelper(netMessage, sendValue, hasResponseData, 0, logMessage);
			if (operateResult.IsSuccess)
			{
				ResetConnectErrorCount();
			}
			return operateResult;
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReceiveMessageAsync(INetMessage netMessage, byte[] sendValue, bool useActivePush = true, Action<long, long> reportProgress = null, Action<byte[]> logMessage = null)
		{
			return await Task.Run(() => SPReceived(serialPort, netMessage, sendValue, awaitData: true, logMessage)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(INetMessage netMessage, byte[] sendValue, bool hasResponseData, Action<byte[]> logMessage = null)
		{
			return await Task.Run(() => ReadFromCoreServer(netMessage, sendValue, hasResponseData, logMessage)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			serialPort?.Dispose();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "PipeSerialPort[" + serialPort.ToFormatString() + "]";
		}
	}
}
