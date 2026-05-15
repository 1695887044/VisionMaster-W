using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// 通信的管道信息
	/// </summary>
	public abstract class CommunicationPipe : IDisposable
	{
		private bool disposedValue;

		private int receiveTimeOut = 5000;

		private int sleepTime = 0;

		private bool useServerActivePush = false;

		private int connectErrorCount = 0;

		private ICommunicationLock communicationLock;

		/// <summary>
		/// 当启用设备方主动发送数据时，用于同步访问方法的信号同步功能
		/// </summary>
		protected AutoResetEvent autoResetEvent;

		/// <summary>
		/// 当启用设备方主动发送数据时，用于应答服务机制的数据缓存
		/// </summary>
		protected byte[] bufferQA = null;

		/// <summary>
		/// 是否是长连接的状态<br />
		/// Whether it is a long connection state
		/// </summary>
		protected bool isPersistentConn = false;

		/// <summary>
		/// 获取或设置接收服务器反馈的时间，如果为负数，则不接收反馈 <br />
		/// Gets or sets the time to receive server feedback, and if it is a negative number, does not receive feedback
		/// </summary>
		/// <example>
		/// 设置1秒的接收超时的示例
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="ReceiveTimeOutExample" title="ReceiveTimeOut示例" />
		/// </example>
		/// <remarks>
		/// 超时的通常原因是服务器端没有配置好，导致访问失败，为了不卡死软件，所以有了这个超时的属性。
		/// </remarks>
		public int ReceiveTimeOut
		{
			get
			{
				return receiveTimeOut;
			}
			set
			{
				receiveTimeOut = value;
			}
		}

		/// <summary>
		/// 获取或设置在正式接收对方返回数据前的时候，需要休息的时间，当设置为0的时候，不需要休息。<br />
		/// Get or set the time required to rest before officially receiving the data from the other party. When it is set to 0, no rest is required.
		/// </summary>
		[HslMqttApi(HttpMethod = "GET", Description = "Get or set the time required to rest before officially receiving the data from the other party. When it is set to 0, no rest is required.")]
		public int SleepTime
		{
			get
			{
				return sleepTime;
			}
			set
			{
				sleepTime = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的管道是否激活从设备主动推送的功能，设置为 true 时支持主动从设备方接收数据信息<br />
		/// Gets or sets whether the current pipeline activates the function of actively pushing data from the device. If this is set to true, it supports actively receiving data information from the device
		/// </summary>
		public bool UseServerActivePush
		{
			get
			{
				return useServerActivePush;
			}
			set
			{
				if (value)
				{
					if (autoResetEvent == null)
					{
						autoResetEvent = new AutoResetEvent(initialState: false);
					}
					isPersistentConn = true;
				}
				useServerActivePush = value;
			}
		}

		/// <summary>
		/// 获取或设置当前管道的线程锁对象，默认是简单的一个互斥锁<br />
		/// Gets or sets the thread lock object of the current pipeline, which defaults to a simple mutex
		/// </summary>
		public ICommunicationLock CommunicationLock
		{
			get
			{
				return communicationLock;
			}
			set
			{
				communicationLock = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的管道是否是长连接，仅对于串口及TCP是有效的，默认都是长连接
		/// </summary>
		public bool IsPersistentConnection { get; set; } = true;


		/// <summary>
		/// 用来决定当前接收的消息是否是问答服务的消息
		/// </summary>
		public Func<CommunicationPipe, OperateResult<byte[]>, bool> DecideWhetherQAMessageFunction { get; set; }

		/// <summary>
		/// 实例化一个默认的构造对象
		/// </summary>
		public CommunicationPipe()
		{
			communicationLock = new CommunicationLockSimple();
		}

		/// <summary>
		/// 根据给定的消息，发送的数据，接收到数据来判断是否接收完成报文
		/// </summary>
		/// <param name="netMessage">消息类对象</param>
		/// <param name="sendValue">发送的数据内容</param>
		/// <param name="ms">接收数据的流</param>
		/// <returns>是否接收完成数据</returns>
		protected bool CheckMessageComplete(INetMessage netMessage, byte[] sendValue, ref MemoryStream ms)
		{
			if (netMessage == null)
			{
				return true;
			}
			SpecifiedCharacterMessage specifiedCharacterMessage = netMessage as SpecifiedCharacterMessage;
			if (specifiedCharacterMessage != null)
			{
				byte[] array = ms.ToArray();
				byte[] bytes = BitConverter.GetBytes(specifiedCharacterMessage.ProtocolHeadBytesLength);
				switch (bytes[3] & 0xF)
				{
				case 1:
					if (array.Length > specifiedCharacterMessage.EndLength && array[array.Length - 1 - specifiedCharacterMessage.EndLength] == bytes[1])
					{
						return true;
					}
					break;
				case 2:
					if (array.Length > specifiedCharacterMessage.EndLength + 1 && array[array.Length - 2 - specifiedCharacterMessage.EndLength] == bytes[1] && array[array.Length - 1 - specifiedCharacterMessage.EndLength] == bytes[0])
					{
						return true;
					}
					break;
				}
			}
			else if (netMessage.ProtocolHeadBytesLength > 0)
			{
				byte[] array2 = ms.ToArray();
				if (array2.Length >= netMessage.ProtocolHeadBytesLength)
				{
					int num = netMessage.PependedUselesByteLength(array2);
					if (num > 0)
					{
						array2 = array2.RemoveBegin(num);
						ms = new MemoryStream();
						ms.Write(array2);
						if (array2.Length < netMessage.ProtocolHeadBytesLength)
						{
							return false;
						}
					}
					netMessage.HeadBytes = array2.SelectBegin(netMessage.ProtocolHeadBytesLength);
					netMessage.SendBytes = sendValue;
					int contentLengthByHeadBytes = netMessage.GetContentLengthByHeadBytes();
					if (array2.Length >= netMessage.ProtocolHeadBytesLength + contentLengthByHeadBytes)
					{
						if (netMessage.ProtocolHeadBytesLength > netMessage.HeadBytes.Length)
						{
							ms = new MemoryStream();
							ms.Write(array2.RemoveBegin(netMessage.ProtocolHeadBytesLength - netMessage.HeadBytes.Length));
						}
						return true;
					}
				}
			}
			else if (netMessage.CheckReceiveDataComplete(sendValue, ms))
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// 重置当前的连续错误计数为0，并且返回重置前时候的值
		/// </summary>
		/// <returns>重置前的值</returns>
		public int ResetConnectErrorCount()
		{
			return Interlocked.Exchange(ref connectErrorCount, 0);
		}

		/// <summary>
		/// 自增当前的连续错误计数，并且获取自增后的值信息，最大到10亿为止，无法继续增加了。
		/// </summary>
		/// <returns>自增后的值信息</returns>
		protected int IncrConnectErrorCount()
		{
			int num = Interlocked.Increment(ref connectErrorCount);
			if (num > 1000000000)
			{
				Interlocked.Exchange(ref connectErrorCount, 1000000000);
			}
			return num;
		}

		/// <summary>
		/// 主动引发一个管道错误，从而让管道可以重新打开
		/// </summary>
		public void RaisePipeError()
		{
			Interlocked.CompareExchange(ref connectErrorCount, 1, 0);
		}

		/// <summary>
		/// 当前的管道连接对象是否发生了错误
		/// </summary>
		/// <returns>是否发生了通道的异常</returns>
		public virtual bool IsConnectError()
		{
			return connectErrorCount > 0;
		}

		/// <summary>
		/// 发送数据到当前的管道中去<br />
		/// Send data to the current pipe
		/// </summary>
		/// <param name="data">等待发送的数据</param>
		/// <returns>是否发送成功</returns>
		public OperateResult Send(byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return Send(data, 0, data.Length);
		}

		/// <summary>
		/// 将一个数据缓存中的指定的部分字段，发送到当前的管道中去<br />
		/// Sends the specified partial field from a data cache to the current pipeline
		/// </summary>
		/// <param name="data">等待发送的缓存数据</param>
		/// <param name="offset">起始偏移的地址</param>
		/// <param name="size">发送的字节长度信息</param>
		/// <returns>是否发送成功</returns>
		public virtual OperateResult Send(byte[] data, int offset, int size)
		{
			return new OperateResult<int>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 从管道里，接收指定长度的报文数据信息，如果长度指定为-1，表示接收不超过2048字节的动态长度。另外可以指定超时时间，进度报告等<br />
		/// Receives the packet data of a specified length from the pipe. If the length is set to -1, 
		/// it indicates that the dynamic length of the packet is not more than 2048 bytes. You can also specify timeouts, progress reports, etc
		/// </summary>
		/// <param name="length">接收的长度信息</param>
		/// <param name="timeOut">指定的超时时间</param>
		/// <param name="reportProgress">进行进度报告的委托</param>
		/// <returns>是否接收成功的结果对象</returns>
		public virtual OperateResult<byte[]> Receive(int length, int timeOut, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			
			OperateResult<byte[]> operateResult = NetSupport.CreateReceiveBuffer(length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<int> operateResult2 = Receive(operateResult.Content, 0, length, timeOut, reportProgress);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? operateResult.Content : operateResult.Content.SelectBegin(operateResult2.Content));
		}

		/// <summary>
		/// 接收固定长度的字节数组，允许指定超时时间，默认为60秒，当length大于0时，接收固定长度的数据内容，当length小于0时，buffer长度的缓存数据<br />
		/// Receiving a fixed-length byte array, allowing a specified timeout time. The default is 60 seconds. When length is greater than 0, 
		/// fixed-length data content is received. When length is less than 0, random data information of a length not greater than 2048 is received.
		/// </summary>
		/// <param name="buffer">等待接收的数据缓存信息</param>
		/// <param name="offset">开始接收数据的偏移地址</param>
		/// <param name="length">准备接收的数据长度，当length大于0时，接收固定长度的数据内容，当length小于0时，接收不大于2048长度的随机数据信息</param>
		/// <param name="timeOut">单位：毫秒，超时时间，默认为60秒，如果设置小于0，则不检查超时时间</param>
		/// <param name="reportProgress">进行进度报告的委托</param>
		/// <returns>包含了字节数据的结果类</returns>
		public virtual OperateResult<int> Receive(byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			return new OperateResult<int>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 开始后台接收相关的报文数据，当<see cref="P:HslCommunication.Core.Pipe.CommunicationPipe.UseServerActivePush" />为True时，则使用本方法
		/// </summary>
		public virtual OperateResult StartReceiveBackground(INetMessage netMessage)
		{
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 打开当前的管道信息，返回是否成功打开的结果对象，并通过属性 Content 指示当前是否为新创建的连接对象，如果是，则该值为 true<br />
		/// </summary>
		/// <remarks>
		/// 并切换长连接操作
		/// </remarks>
		/// <returns>是否打开成功的结果对象</returns>
		public virtual OperateResult<bool> OpenCommunication()
		{
			return new OperateResult<bool>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 关闭当前的管道信息，返回是否关闭成功的结果对象
		/// </summary>
		/// <returns>是否关闭成功</returns>
		public virtual OperateResult CloseCommunication()
		{
			return new OperateResult<int>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 设置当前的问答状态下的缓存数据
		/// </summary>
		/// <param name="buffer">设置的缓存</param>
		protected void SetBufferQA(byte[] buffer)
		{
			bufferQA = buffer;
			autoResetEvent.Set();
		}

		/// <summary>
		/// 包含了一个复杂的逻辑，从管道里根据当前的消息格式定义，接收报文信息，这个报文可能是来自服务器主动推送的。具体可以通过参数 <paramref name="useActivePush" /> 来特殊控制。<br />
		/// Contains a complex logic from the pipeline, according to the current message format definition, to receive message information, 
		/// this message may be actively pushed from the server. The parameter <paramref name="useActivePush" /> can be used for special control.
		/// </summary>
		/// <param name="netMessage">消息对象</param>
		/// <param name="sendValue">发送的数据，大多数的情况，都可以为空</param>
		/// <param name="useActivePush">是否使用服务方主动推送的数据，默认为 true</param>
		/// <param name="reportProgress">进行进度报告的委托</param>
		/// <param name="logMessage">用于消息记录的日志信息</param>
		/// <returns>是否</returns>
		public virtual OperateResult<byte[]> ReceiveMessage(INetMessage netMessage, byte[] sendValue, bool useActivePush = true, Action<long, long> reportProgress = null, Action<byte[]> logMessage = null)
		{
			if (useServerActivePush && useActivePush)
			{
				if (autoResetEvent.WaitOne(ReceiveTimeOut))
				{
					if (netMessage != null)
					{
						netMessage.HeadBytes = bufferQA;
					}
					logMessage?.Invoke(bufferQA);
					return OperateResult.CreateSuccessResult(bufferQA);
				}
				CloseCommunication();
				return new OperateResult<byte[]>(-IncrConnectErrorCount(), StringResources.Language.ReceiveDataTimeout + ReceiveTimeOut);
			}
			if (netMessage == null || netMessage.ProtocolHeadBytesLength == -1)
			{
				if (netMessage != null && netMessage.SendBytes == null)
				{
					netMessage.SendBytes = sendValue;
				}
				DateTime now = DateTime.Now;
				MemoryStream memoryStream = new MemoryStream();
				do
				{
					OperateResult<byte[]> operateResult = ReceiveByMessage(ReceiveTimeOut, null, reportProgress);
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
					if (operateResult.Content != null && operateResult.Content.Length != 0)
					{
						memoryStream.Write(operateResult.Content);
						logMessage?.Invoke(operateResult.Content);
					}
					if (netMessage == null)
					{
						return OperateResult.CreateSuccessResult(memoryStream.ToArray());
					}
					if (netMessage.CheckReceiveDataComplete(sendValue, memoryStream))
					{
						return OperateResult.CreateSuccessResult(memoryStream.ToArray());
					}
				}
				while (ReceiveTimeOut < 0 || !((DateTime.Now - now).TotalMilliseconds > (double)ReceiveTimeOut));
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + ReceiveTimeOut + " Received: " + memoryStream.ToArray().ToHexString(' '));
			}
			OperateResult<byte[]> operateResult2 = ReceiveByMessage(ReceiveTimeOut, netMessage, reportProgress);
			if (operateResult2.IsSuccess)
			{
				logMessage?.Invoke(operateResult2.Content);
			}
			return operateResult2;
		}

		/// <summary>
		/// 将数据发送到当前的管道里，并从管道接收相关的数据信息，可以指定消息类型，发送数据，是否有数据响应，休眠时间<br />
		/// To send data to the current pipeline and receive relevant data information from the pipeline, you can specify the message type, 
		/// the data sent, whether there is a data response, and the sleep time
		/// </summary>
		/// <param name="netMessage">当前接收的消息体信息</param>
		/// <param name="sendValue">等待发送的数据</param>
		/// <param name="hasResponseData">是否有数据返回</param>
		/// <param name="sleep">休眠时间</param>
		/// <param name="logMessage">用于消息记录的日志信息</param>
		/// <returns>读取的结果对象</returns>
		protected OperateResult<byte[]> ReadFromCoreServerHelper(INetMessage netMessage, byte[] sendValue, bool hasResponseData, int sleep, Action<byte[]> logMessage = null)
		{
			if (netMessage != null)
			{
				netMessage.SendBytes = sendValue;
			}
			OperateResult operateResult = Send(sendValue);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (!hasResponseData)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (sleep > 0)
			{
				HslHelper.ThreadSleep(sleep);
			}
			DateTime now = DateTime.Now;
			int num = 0;
			OperateResult<byte[]> operateResult2;
			while (true)
			{
				operateResult2 = ReceiveMessage(netMessage, sendValue, useActivePush: true, null, logMessage);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				bool num2;
				if (netMessage != null)
				{
					switch (netMessage.CheckMessageMatch(sendValue, operateResult2.Content))
					{
					case 0:
						return new OperateResult<byte[]>("INetMessage.CheckMessageMatch failed" + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(sendValue, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(operateResult2.Content, ' '));
					default:
						num++;
						num2 = ReceiveTimeOut >= 0 && (DateTime.Now - now).TotalMilliseconds > (double)ReceiveTimeOut;
						goto IL_0194;
					case 1:
						break;
					}
				}
				break;
				IL_0194:
				if (num2)
				{
					return new OperateResult<byte[]>("Receive Message timeout: " + ReceiveTimeOut + " CheckMessageMatch times:" + num);
				}
			}
			if (netMessage != null && !netMessage.CheckHeadBytesLegal(null))
			{
				return new OperateResult<byte[]>(StringResources.Language.CommandHeadCodeCheckFailed + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(sendValue, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(operateResult2.Content, ' '));
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReadFromCoreServerHelper(HslCommunication.Core.IMessage.INetMessage,System.Byte[],System.Boolean,System.Int32,System.Action{System.Byte[]})" />
		public virtual OperateResult<byte[]> ReadFromCoreServer(INetMessage netMessage, byte[] sendValue, bool hasResponseData, Action<byte[]> logMessage = null)
		{
			OperateResult<byte[]> operateResult = ReadFromCoreServerHelper(netMessage, sendValue, hasResponseData, SleepTime, logMessage);
			if (!operateResult.IsSuccess)
			{
				if (operateResult.ErrorCode < 0 && operateResult.ErrorCode != int.MinValue)
				{
				}
				return operateResult;
			}
			ResetConnectErrorCount();
			return operateResult;
		}

		/// <summary>
		/// 接收一条完整的 <seealso cref="T:HslCommunication.Core.IMessage.INetMessage" /> 数据内容，需要指定超时时间，单位为毫秒。 <br />
		/// Receive a complete <seealso cref="T:HslCommunication.Core.IMessage.INetMessage" /> data content, Need to specify a timeout period in milliseconds
		/// </summary>
		/// <param name="timeOut">超时时间，单位：毫秒</param>
		/// <param name="netMessage">消息的格式定义</param>
		/// <param name="reportProgress">接收消息的时候的进度报告</param>
		/// <returns>带有是否成功的byte数组对象</returns>
		private OperateResult<byte[]> ReceiveByMessage(int timeOut, INetMessage netMessage, Action<long, long> reportProgress = null)
		{
			if (netMessage == null)
			{
				return Receive(-1, timeOut);
			}
			if (netMessage.ProtocolHeadBytesLength < 0)
			{
				byte[] bytes = BitConverter.GetBytes(netMessage.ProtocolHeadBytesLength);
				int num = bytes[3] & 0xF;
				OperateResult<byte[]> operateResult = null;
				switch (num)
				{
				case 1:
					operateResult = ReceiveCommandLineFromPipe(bytes[1], timeOut);
					break;
				case 2:
					operateResult = ReceiveCommandLineFromPipe(bytes[1], bytes[0], timeOut);
					break;
				}
				if (operateResult == null)
				{
					return new OperateResult<byte[]>("Receive by specified code failed, length check failed");
				}
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				netMessage.HeadBytes = operateResult.Content;
				SpecifiedCharacterMessage specifiedCharacterMessage = netMessage as SpecifiedCharacterMessage;
				if (specifiedCharacterMessage != null)
				{
					if (specifiedCharacterMessage.EndLength == 0)
					{
						return operateResult;
					}
					OperateResult<byte[]> operateResult2 = Receive(specifiedCharacterMessage.EndLength, timeOut);
					if (!operateResult2.IsSuccess)
					{
						return operateResult2;
					}
					return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(operateResult.Content, operateResult2.Content));
				}
				return operateResult;
			}
			OperateResult<byte[]> operateResult3 = Receive(netMessage.ProtocolHeadBytesLength, timeOut);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			int num2 = netMessage.PependedUselesByteLength(operateResult3.Content);
			int num3 = 0;
			while (num2 >= netMessage.ProtocolHeadBytesLength)
			{
				operateResult3 = Receive(netMessage.ProtocolHeadBytesLength, timeOut);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				num2 = netMessage.PependedUselesByteLength(operateResult3.Content);
				num3++;
				if (num3 > 10)
				{
					break;
				}
			}
			if (num2 > 0)
			{
				OperateResult<byte[]> operateResult4 = Receive(num2, timeOut);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				operateResult3.Content = SoftBasic.SpliceArray<byte>(operateResult3.Content.RemoveBegin(num2), operateResult4.Content);
			}
			netMessage.HeadBytes = operateResult3.Content;
			int contentLengthByHeadBytes = netMessage.GetContentLengthByHeadBytes();
			if (contentLengthByHeadBytes <= 0)
			{
				return OperateResult.CreateSuccessResult(operateResult3.Content);
			}
			byte[] array = new byte[netMessage.HeadBytes.Length + contentLengthByHeadBytes];
			netMessage.HeadBytes.CopyTo(array, 0);
			OperateResult operateResult5 = Receive(array, netMessage.HeadBytes.Length, contentLengthByHeadBytes, timeOut, reportProgress);
			if (!operateResult5.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult5);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 接收一行命令数据，需要自己指定这个结束符，默认超时时间为60秒，也即是60000，单位是毫秒<br />
		/// To receive a line of command data, you need to specify the terminator yourself. The default timeout is 60 seconds, which is 60,000, in milliseconds.
		/// </summary>
		/// <param name="endCode">结束符信息</param>
		/// <param name="timeout">超时时间，默认为60000，单位为毫秒，也就是60秒</param>
		/// <returns>带有结果对象的数据信息</returns>
		private OperateResult<byte[]> ReceiveCommandLineFromPipe(byte endCode, int timeout = 60000)
		{
			try
			{
				List<byte> list = new List<byte>(128);
				DateTime now = DateTime.Now;
				bool flag = false;
				while ((DateTime.Now - now).TotalMilliseconds < (double)timeout)
				{
					OperateResult<byte[]> operateResult = Receive(1, timeout);
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
					list.AddRange(operateResult.Content);
					if (operateResult.Content[0] == endCode)
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + " " + timeout);
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 接收一行命令数据，需要自己指定这个结束符，默认超时时间为60秒，也即是60000，单位是毫秒<br />
		/// To receive a line of command data, you need to specify the terminator yourself. The default timeout is 60 seconds, which is 60,000, in milliseconds.
		/// </summary>
		/// <param name="endCode1">结束符1信息</param>
		/// <param name="endCode2">结束符2信息</param>
		/// /// <param name="timeout">超时时间，默认无穷大，单位毫秒</param>
		/// <returns>带有结果对象的数据信息</returns>
		private OperateResult<byte[]> ReceiveCommandLineFromPipe(byte endCode1, byte endCode2, int timeout = 60000)
		{
			try
			{
				List<byte> list = new List<byte>(128);
				DateTime now = DateTime.Now;
				bool flag = false;
				while ((DateTime.Now - now).TotalMilliseconds < (double)timeout)
				{
					OperateResult<byte[]> operateResult = Receive(1, timeout);
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
					list.AddRange(operateResult.Content);
					if (operateResult.Content[0] == endCode2 && list.Count > 1 && list[list.Count - 2] == endCode1)
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + " " + timeout);
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.Send(System.Byte[])" />
		public async Task<OperateResult> SendAsync(byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return await SendAsync(data, 0, data.Length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.Send(System.Byte[],System.Int32,System.Int32)" />
		public virtual async Task<OperateResult> SendAsync(byte[] data, int offset, int size)
		{
			return await Task.Run(() => Send(data, offset, size)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.Receive(System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public virtual async Task<OperateResult<byte[]>> ReceiveAsync(int length, int timeOut, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			
			OperateResult<byte[]> buffer = NetSupport.CreateReceiveBuffer(length);
			if (!buffer.IsSuccess)
			{
				return buffer;
			}
			OperateResult<int> receive = await ReceiveAsync(buffer.Content, 0, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(receive);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? buffer.Content : buffer.Content.SelectBegin(receive.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.Receive(System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public virtual async Task<OperateResult<int>> ReceiveAsync(byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			return await Task.Run(() => Receive(buffer, offset, length, timeOut, reportProgress)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.OpenCommunication" />
		public virtual async Task<OperateResult<bool>> OpenCommunicationAsync()
		{
			return await Task.Run(() => OpenCommunication()).ConfigureAwait(continueOnCapturedContext: false);
		}

		public virtual async Task<OperateResult> CloseCommunicationAsync()
		{
			return await Task.Run(() => CloseCommunication()).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReceiveCommandLineFromPipe(System.Byte,System.Int32)" />
		private async Task<OperateResult<byte[]>> ReceiveCommandLineFromPipeAsync(byte endCode, int timeout = 60000)
		{
			try
			{
				List<byte> bufferArray = new List<byte>(128);
				DateTime st = DateTime.Now;
				bool bOK = false;
				while ((DateTime.Now - st).TotalMilliseconds < (double)timeout)
				{
					OperateResult<byte[]> headResult = await ReceiveAsync(1, timeout).ConfigureAwait(continueOnCapturedContext: false);
					if (!headResult.IsSuccess)
					{
						return headResult;
					}
					bufferArray.AddRange(headResult.Content);
					if (headResult.Content[0] == endCode)
					{
						bOK = true;
						break;
					}
				}
				if (!bOK)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + " " + timeout);
				}
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReceiveCommandLineFromPipe(System.Byte,System.Byte,System.Int32)" />
		private async Task<OperateResult<byte[]>> ReceiveCommandLineFromPipeAsync(byte endCode1, byte endCode2, int timeout = 60000)
		{
			try
			{
				List<byte> bufferArray = new List<byte>(128);
				DateTime st = DateTime.Now;
				bool bOK = false;
				while ((DateTime.Now - st).TotalMilliseconds < (double)timeout)
				{
					OperateResult<byte[]> headResult = await ReceiveAsync(1, timeout).ConfigureAwait(continueOnCapturedContext: false);
					if (!headResult.IsSuccess)
					{
						return headResult;
					}
					bufferArray.AddRange(headResult.Content);
					if (headResult.Content[0] == endCode2 && bufferArray.Count > 1 && bufferArray[bufferArray.Count - 2] == endCode1)
					{
						bOK = true;
						break;
					}
				}
				if (!bOK)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + " " + timeout);
				}
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReceiveByMessage(System.Int32,HslCommunication.Core.IMessage.INetMessage,System.Action{System.Int64,System.Int64})" />
		private async Task<OperateResult<byte[]>> ReceiveByMessageAsync(int timeOut, INetMessage netMessage, Action<long, long> reportProgress = null)
		{
			if (netMessage == null)
			{
				return await ReceiveAsync(-1, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (netMessage.ProtocolHeadBytesLength < 0)
			{
				byte[] headCode = BitConverter.GetBytes(netMessage.ProtocolHeadBytesLength);
				int codeLength = headCode[3] & 0xF;
				OperateResult<byte[]> receive = null;
				switch (codeLength)
				{
				case 1:
					receive = await ReceiveCommandLineFromPipeAsync(headCode[1], timeOut).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case 2:
					receive = await ReceiveCommandLineFromPipeAsync(headCode[1], headCode[0], timeOut).ConfigureAwait(continueOnCapturedContext: false);
					break;
				}
				if (receive == null)
				{
					return new OperateResult<byte[]>("Receive by specified code failed, length check failed");
				}
				if (!receive.IsSuccess)
				{
					return receive;
				}
				netMessage.HeadBytes = receive.Content;
				SpecifiedCharacterMessage message = netMessage as SpecifiedCharacterMessage;
				if (message != null)
				{
					if (message.EndLength == 0)
					{
						return receive;
					}
					OperateResult<byte[]> endResult = await ReceiveAsync(message.EndLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
					if (!endResult.IsSuccess)
					{
						return endResult;
					}
					return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(receive.Content, endResult.Content));
				}
				return receive;
			}
			OperateResult<byte[]> headResult = await ReceiveAsync(netMessage.ProtocolHeadBytesLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			if (!headResult.IsSuccess)
			{
				return headResult;
			}
			int start = netMessage.PependedUselesByteLength(headResult.Content);
			int cycleCount = 0;
			while (start >= netMessage.ProtocolHeadBytesLength)
			{
				headResult = await ReceiveAsync(netMessage.ProtocolHeadBytesLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!headResult.IsSuccess)
				{
					return headResult;
				}
				start = netMessage.PependedUselesByteLength(headResult.Content);
				cycleCount++;
				if (cycleCount > 10)
				{
					break;
				}
			}
			if (start > 0)
			{
				OperateResult<byte[]> head2Result = await ReceiveAsync(start, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!head2Result.IsSuccess)
				{
					return head2Result;
				}
				headResult.Content = SoftBasic.SpliceArray<byte>(headResult.Content.RemoveBegin(start), head2Result.Content);
			}
			netMessage.HeadBytes = headResult.Content;
			int contentLength = netMessage.GetContentLengthByHeadBytes();
			if (contentLength <= 0)
			{
				return OperateResult.CreateSuccessResult(headResult.Content);
			}
			byte[] result = new byte[netMessage.HeadBytes.Length + contentLength];
			netMessage.HeadBytes.CopyTo(result, 0);
			OperateResult contentResult = await ReceiveAsync(result, netMessage.HeadBytes.Length, contentLength, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!contentResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(contentResult);
			}
			return OperateResult.CreateSuccessResult(result);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReceiveMessage(HslCommunication.Core.IMessage.INetMessage,System.Byte[],System.Boolean,System.Action{System.Int64,System.Int64},System.Action{System.Byte[]})" />
		public virtual async Task<OperateResult<byte[]>> ReceiveMessageAsync(INetMessage netMessage, byte[] sendValue, bool useActivePush = true, Action<long, long> reportProgress = null, Action<byte[]> logMessage = null)
		{
			if (useServerActivePush && useActivePush)
			{
				if (autoResetEvent.WaitOne(ReceiveTimeOut))
				{
					if (netMessage != null)
					{
						netMessage.HeadBytes = bufferQA;
					}
					logMessage?.Invoke(bufferQA);
					return OperateResult.CreateSuccessResult(bufferQA);
				}
				CloseCommunication();
				return new OperateResult<byte[]>(-IncrConnectErrorCount(), StringResources.Language.ReceiveDataTimeout + ReceiveTimeOut);
			}
			if (netMessage == null || netMessage.ProtocolHeadBytesLength == -1)
			{
				if (netMessage != null && netMessage.SendBytes == null)
				{
					netMessage.SendBytes = sendValue;
				}
				DateTime startTime = DateTime.Now;
				MemoryStream ms = new MemoryStream();
				do
				{
					OperateResult<byte[]> read2 = await ReceiveByMessageAsync(ReceiveTimeOut, null, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
					if (!read2.IsSuccess)
					{
						return read2;
					}
					if (read2.Content != null && read2.Content.Length != 0)
					{
						ms.Write(read2.Content);
						logMessage?.Invoke(read2.Content);
					}
					if (netMessage == null)
					{
						return OperateResult.CreateSuccessResult(ms.ToArray());
					}
					if (netMessage.CheckReceiveDataComplete(sendValue, ms))
					{
						return OperateResult.CreateSuccessResult(ms.ToArray());
					}
				}
				while (ReceiveTimeOut < 0 || !((DateTime.Now - startTime).TotalMilliseconds > (double)ReceiveTimeOut));
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + ReceiveTimeOut + " Received: " + ms.ToArray().ToHexString(' '));
			}
			OperateResult<byte[]> read = await ReceiveByMessageAsync(ReceiveTimeOut, netMessage, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (read.IsSuccess)
			{
				logMessage?.Invoke(read.Content);
			}
			return read;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReadFromCoreServerHelper(HslCommunication.Core.IMessage.INetMessage,System.Byte[],System.Boolean,System.Int32,System.Action{System.Byte[]})" />
		protected async Task<OperateResult<byte[]>> ReadFromCoreServerHelperAsync(INetMessage netMessage, byte[] sendValue, bool hasResponseData, int sleep, Action<byte[]> logMessage = null)
		{
			if (netMessage != null)
			{
				netMessage.SendBytes = sendValue;
			}
			OperateResult sendResult = await SendAsync(sendValue).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(sendResult);
			}
			if (ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (!hasResponseData)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (sleep > 0)
			{
				HslHelper.ThreadSleep(sleep);
			}
			DateTime start = DateTime.Now;
			int times = 0;
			OperateResult<byte[]> resultReceive;
			while (true)
			{
				resultReceive = await ReceiveMessageAsync(netMessage, sendValue, useActivePush: true, null, logMessage).ConfigureAwait(continueOnCapturedContext: false);
				if (!resultReceive.IsSuccess)
				{
					return resultReceive;
				}
				bool num;
				if (netMessage != null)
				{
					switch (netMessage.CheckMessageMatch(sendValue, resultReceive.Content))
					{
					case 0:
						return new OperateResult<byte[]>("INetMessage.CheckMessageMatch failed" + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(sendValue, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(resultReceive.Content, ' '));
					default:
						times++;
						num = ReceiveTimeOut >= 0 && (DateTime.Now - start).TotalMilliseconds > (double)ReceiveTimeOut;
						goto IL_0344;
					case 1:
						break;
					}
				}
				break;
				IL_0344:
				if (num)
				{
					return new OperateResult<byte[]>("Receive Message timeout: " + ReceiveTimeOut + " CheckMessageMatch times:" + times);
				}
			}
			if (netMessage != null && !netMessage.CheckHeadBytesLegal(null))
			{
				return new OperateResult<byte[]>(StringResources.Language.CommandHeadCodeCheckFailed + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(sendValue, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(resultReceive.Content, ' '));
			}
			return OperateResult.CreateSuccessResult(resultReceive.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.CommunicationPipe.ReadFromCoreServer(HslCommunication.Core.IMessage.INetMessage,System.Byte[],System.Boolean,System.Action{System.Byte[]})" />
		public virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(INetMessage netMessage, byte[] sendValue, bool hasResponseData, Action<byte[]> logMessage = null)
		{
			OperateResult<byte[]> read = await ReadFromCoreServerHelperAsync(netMessage, sendValue, hasResponseData, SleepTime, logMessage).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				if (read.ErrorCode < 0 && read.ErrorCode != int.MinValue)
				{
				}
				return read;
			}
			ResetConnectErrorCount();
			return read;
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				autoResetEvent?.Dispose();
				communicationLock?.Dispose();
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			if (!disposedValue)
			{
				Dispose(disposing: true);
				GC.SuppressFinalize(this);
				disposedValue = true;
			}
		}
	}
}
