using System.Collections.Generic;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Pipe;
using HslCommunication.LogNet;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 二进制通信类，默认直接
	/// </summary>
	public class BinaryCommunication
	{
		/// <summary>
		/// 设置日志记录报文是否二进制，如果为False，那就使用ASCII码<br />
		/// Set whether the log message is binary, if it is False, then use ASCII code
		/// </summary>
		/// <remarks>
		/// 默认值为 true
		/// </remarks>
		protected bool LogMsgFormatBinary = true;

		private CommunicationPipe communicationPipe;

		private string connectionId;

		private byte[] sendbyteBefore = null;

		private string sendBefore = string.Empty;

		/// <inheritdoc cref="P:HslCommunication.Core.IReadWriteNet.ConnectionId" />
		[HslMqttApi(HttpMethod = "GET", Description = "The unique ID number of the current connection. The default is a 20-digit guid code plus a random number.")]
		public string ConnectionId
		{
			get
			{
				return connectionId;
			}
			set
			{
				connectionId = value;
			}
		}

		/// <summary>
		/// 获取或设置在发送通信报文前追加发送的字节信息，HEX格式，通常用于lora组网时，需要携带 00 00 00 02 四个字节的站地址功能。<br />
		/// Obtain or set the byte information sent before sending communication packets, HEX format, usually used for LORA networking, you need to carry 00 00 00 02 four-byte station address function.
		/// </summary>
		public string SendBeforeHex
		{
			get
			{
				return sendBefore;
			}
			set
			{
				sendBefore = value;
				if (string.IsNullOrEmpty(value))
				{
					sendbyteBefore = null;
				}
				else
				{
					sendbyteBefore = value.ToHexBytes();
				}
			}
		}

		/// <summary>
		/// 获取或设置当前的管道信息，管道类型为<see cref="P:HslCommunication.Core.Net.BinaryCommunication.CommunicationPipe" />的继承类，内置了<see cref="T:HslCommunication.Core.Pipe.PipeTcpNet" />管道，<see cref="T:HslCommunication.Core.Pipe.PipeUdpNet" />管道，<see cref="T:HslCommunication.Core.Pipe.PipeSerialPort" />管道等
		/// </summary>
		public virtual CommunicationPipe CommunicationPipe
		{
			get
			{
				return communicationPipe;
			}
			set
			{
				communicationPipe = value;
			}
		}

		/// <summary>
		/// 组件的日志工具，支持日志记录，只要实例化后，当前网络的基本信息，就以<see cref="F:HslCommunication.LogNet.HslMessageDegree.DEBUG" />等级进行输出<br />
		/// The component's logging tool supports logging. As long as the instantiation of the basic network information, the output will be output at <see cref="F:HslCommunication.LogNet.HslMessageDegree.DEBUG" />
		/// </summary>
		/// <remarks>
		/// 只要实例化即可以记录日志，实例化的对象需要实现接口 <see cref="T:HslCommunication.LogNet.ILogNet" /> ，本组件提供了三个日志记录类，你可以实现基于 <see cref="T:HslCommunication.LogNet.ILogNet" />  的对象。</remarks>
		/// <example>
		/// 如下的实例化适用于所有的Network及其派生类，以下举两个例子，三菱的设备类及服务器类
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="LogNetExample1" title="LogNet示例" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="LogNetExample2" title="LogNet示例" />
		/// </example>
		public ILogNet LogNet { get; set; }

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
		[HslMqttApi(HttpMethod = "GET", Description = "Gets or sets the time to receive server feedback, and if it is a negative number, does not receive feedback")]
		public int ReceiveTimeOut
		{
			get
			{
				return communicationPipe.ReceiveTimeOut;
			}
			set
			{
				communicationPipe.ReceiveTimeOut = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.CommunicationPipe.SleepTime" />
		[HslMqttApi(HttpMethod = "GET", Description = "Get or set the time required to rest before officially receiving the data from the other party. When it is set to 0, no rest is required.")]
		public int SleepTime
		{
			get
			{
				return CommunicationPipe.SleepTime;
			}
			set
			{
				CommunicationPipe.SleepTime = value;
			}
		}

		/// <summary>
		/// 默认的无参构造函数 <br />
		/// Default no-parameter constructor
		/// </summary>
		public BinaryCommunication()
		{
			connectionId = SoftBasic.GetUniqueStringByGuidAndRandom();
		}

		/// <summary>
		/// 获取一个新的消息对象的方法，需要在继承类里面进行重写<br />
		/// The method to get a new message object needs to be overridden in the inheritance class
		/// </summary>
		/// <returns>消息类对象</returns>
		protected virtual INetMessage GetNewNetMessage()
		{
			return null;
		}

		/// <summary>
		/// 决定当前的消息是否是用于问答机制返回的消息，默认直接返回 true, 实际的情况需要根据协议进行重写方法<br />
		/// To determine whether the current message is the message returned by the question answering mechanism, 
		/// the default is true. In actual cases, the rewriting method needs to be performed according to the protocol
		/// </summary>
		/// <param name="pipe">管道信息</param>
		/// <param name="receive">接收的数据信息</param>
		/// <returns>是否是问答的数据</returns>
		protected virtual bool DecideWhetherQAMessage(CommunicationPipe pipe, OperateResult<byte[]> receive)
		{
			return true;
		}

		/// <summary>
		/// 根据实际的协议选择是否重写本方法，有些协议在创建连接之后，需要进行一些初始化的信号握手，才能最终建立网络通道。<br />
		/// Whether to rewrite this method is based on the actual protocol. Some protocols require some initial signal handshake to establish a network channel after the connection is created.
		/// </summary>
		/// <returns>是否初始化成功，依据具体的协议进行重写</returns>
		/// <example>
		/// 有些协议不需要握手信号，比如三菱的MC协议，Modbus协议，西门子和欧姆龙就存在握手信息，此处的例子是继承本类后重写的西门子的协议示例
		/// <code lang="cs" source="HslCommunication_Net45\Profinet\Siemens\SiemensS7Net.cs" region="NetworkDoubleBase Override" title="西门子重连示例" />
		/// </example>
		protected virtual OperateResult InitializationOnConnect()
		{
			if (communicationPipe.UseServerActivePush)
			{
				communicationPipe.DecideWhetherQAMessageFunction = DecideWhetherQAMessage;
				communicationPipe.StartReceiveBackground(GetNewNetMessage());
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 根据实际的协议选择是否重写本方法，有些协议在断开连接之前，需要发送一些报文来关闭当前的网络通道<br />
		/// Select whether to rewrite this method according to the actual protocol. Some protocols need to send some packets to close the current network channel before disconnecting.
		/// </summary>
		/// <example>
		/// 目前暂无相关的示例，组件支持的协议都不用实现这个方法。
		/// </example>
		/// <returns>当断开连接时额外的操作结果</returns>
		protected virtual OperateResult ExtraOnDisconnect()
		{
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 和服务器交互完成的时候调用的方法，可以根据读写结果进行一些额外的操作，具体的操作需要根据实际的需求来重写实现<br />
		/// The method called when the interaction with the server is completed can perform some additional operations based on the read and write results. 
		/// The specific operations need to be rewritten according to actual needs.
		/// </summary>
		/// <param name="read">读取结果</param>
		protected virtual void ExtraAfterReadFromCoreServer(OperateResult read)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.InitializationOnConnect" />
		protected virtual async Task<OperateResult> InitializationOnConnectAsync()
		{
			if (communicationPipe.UseServerActivePush)
			{
				communicationPipe.DecideWhetherQAMessageFunction = DecideWhetherQAMessage;
				communicationPipe.StartReceiveBackground(GetNewNetMessage());
			}
			return await Task.FromResult(OperateResult.CreateSuccessResult()).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ExtraOnDisconnect" />
		protected virtual async Task<OperateResult> ExtraOnDisconnectAsync()
		{
			return await Task.FromResult(OperateResult.CreateSuccessResult()).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <summary>
		/// 对当前的命令进行打包处理，通常是携带命令头内容，标记当前的命令的长度信息，需要进行重写，否则默认不打包<br />
		/// The current command is packaged, usually carrying the content of the command header, marking the length of the current command, 
		/// and it needs to be rewritten, otherwise it is not packaged by default
		/// </summary>
		/// <remarks>
		/// 对发送的命令打包之后，直接发送给真实的对方设备了，例如在AB-PLC里面，就重写了打包方法，将当前的会话ID参数传递给PLC设备<br />
		/// After packaging the sent command, it is directly sent to the real counterpart device. For example, in AB-PLC, 
		/// the packaging method is rewritten and the current session ID parameter is passed to the PLC device.
		/// </remarks>
		/// <param name="command">发送的数据命令内容</param>
		/// <returns>打包之后的数据结果信息</returns>
		public virtual byte[] PackCommandWithHeader(byte[] command)
		{
			return command;
		}

		/// <summary>
		/// 根据对方返回的报文命令，对命令进行基本的拆包，例如各种Modbus协议拆包为统一的核心报文，还支持对报文的验证<br />
		/// According to the message command returned by the other party, the command is basically unpacked, for example, 
		/// various Modbus protocols are unpacked into a unified core message, and the verification of the message is also supported
		/// </summary>
		/// <remarks>
		/// 在实际解包的操作过程中，通常对状态码，错误码等消息进行判断，如果校验不通过，将携带错误消息返回<br />
		/// During the actual unpacking operation, the status code, error code and other messages are usually judged. If the verification fails, the error message will be returned.
		/// </remarks>
		/// <param name="send">发送的原始报文数据</param>
		/// <param name="response">设备方反馈的原始报文内容</param>
		/// <returns>返回拆包之后的报文信息，默认不进行任何的拆包操作</returns>
		public virtual OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return OperateResult.CreateSuccessResult(response);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ReadFromCoreServer(System.Byte[],System.Boolean,System.Boolean)" />
		public virtual OperateResult<byte[]> ReadFromCoreServer(byte[] send)
		{
			return ReadFromCoreServer(send, hasResponseData: true, usePackAndUnpack: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteDevice.ReadFromCoreServer(System.Collections.Generic.IEnumerable{System.Byte[]})" />
		public virtual OperateResult<byte[]> ReadFromCoreServer(IEnumerable<byte[]> send)
		{
			return NetSupport.ReadFromCoreServer(send, ReadFromCoreServer);
		}

		/// <summary>
		/// 将二进制的数据发送到管道中去，然后从管道里接收二进制的数据回来，并返回是否成功的结果对象。<br />
		/// Send binary data to the pipeline, and then receive binary data back from the pipeline, and return whether the success of the result object
		/// </summary>
		/// <param name="send">发送的完整的报文信息</param>
		/// <param name="hasResponseData">是否有等待的数据返回</param>
		/// <param name="usePackAndUnpack">是否需要对命令重新打包，在重写<see cref="M:HslCommunication.Core.Net.BinaryCommunication.PackCommandWithHeader(System.Byte[])" />方法后才会有影响</param>
		/// <returns>接收的完整的报文信息</returns>
		/// <remarks>
		/// 本方法用于实现本组件还未实现的一些报文功能，例如有些modbus服务器会有一些特殊的功能码支持，需要收发特殊的报文，详细请看示例
		/// </remarks>
		/// <example>
		/// 此处举例有个modbus服务器，有个特殊的功能码0x09，后面携带子数据0x01即可，发送字节为 0x00 0x00 0x00 0x00 0x00 0x03 0x01 0x09 0x01
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="ReadFromCoreServerExample2" title="ReadFromCoreServer示例" />
		/// </example>
		public virtual OperateResult<byte[]> ReadFromCoreServer(byte[] send, bool hasResponseData, bool usePackAndUnpack)
		{
			OperateResult<byte[]> operateResult = new OperateResult<byte[]>();
			OperateResult operateResult2 = communicationPipe.CommunicationLock.EnterLock(communicationPipe.ReceiveTimeOut);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			try
			{
				OperateResult<bool> operateResult3 = communicationPipe.OpenCommunication();
				if (!operateResult3.IsSuccess)
				{
					communicationPipe.CommunicationLock.LeaveLock();
					operateResult.CopyErrorFromOther(operateResult3);
					return operateResult;
				}
				if (operateResult3.Content)
				{
					OperateResult operateResult4 = InitializationOnConnect();
					if (!operateResult4.IsSuccess)
					{
						communicationPipe.CommunicationLock.LeaveLock();
						return OperateResult.CreateFailedResult<byte[]>(operateResult4);
					}
				}
				operateResult = ReadFromCoreServer(communicationPipe, send, hasResponseData, usePackAndUnpack);
				ExtraAfterReadFromCoreServer(operateResult);
				communicationPipe.CommunicationLock.LeaveLock();
			}
			catch
			{
				communicationPipe.CommunicationLock.LeaveLock();
				throw;
			}
			if (!communicationPipe.IsPersistentConnection)
			{
				ExtraOnDisconnect();
				communicationPipe.CloseCommunication();
			}
			return operateResult;
		}

		/// <summary>
		/// 使用指定的管道来进行数据通信，发送原始数据到管道，然后从管道接收相关的数据返回，本方法无锁
		/// </summary>
		/// <param name="pipe">管道信息</param>
		/// <param name="send">等待发送的数据</param>
		/// <param name="hasResponseData">是否需要返回的数据</param>
		/// <param name="usePackAndUnpack">是否进行封包，拆包操作</param>
		/// <returns>是否成功的结果对象</returns>
		public virtual OperateResult<byte[]> ReadFromCoreServer(CommunicationPipe pipe, byte[] send, bool hasResponseData, bool usePackAndUnpack)
		{
			if (usePackAndUnpack)
			{
				send = PackCommandWithHeader(send);
			}
			if (sendbyteBefore != null)
			{
				OperateResult operateResult = pipe.Send(sendbyteBefore);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				LogSendMessage(sendbyteBefore);
			}
			LogSendMessage(send);
			OperateResult<byte[]> operateResult2 = pipe.ReadFromCoreServer(GetNewNetMessage(), send, hasResponseData, LogRevcMessage);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (!usePackAndUnpack)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = UnpackResponseContent(send, operateResult2.Content);
			if (!operateResult3.IsSuccess && operateResult3.ErrorCode == int.MinValue)
			{
				operateResult3.ErrorCode = 10000;
			}
			return operateResult3;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ReadFromCoreServer(System.Byte[],System.Boolean,System.Boolean)" />
		public virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(byte[] send)
		{
			return await ReadFromCoreServerAsync(send, hasResponseData: true, usePackAndUnpack: true).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ReadFromCoreServer(System.Collections.Generic.IEnumerable{System.Byte[]})" />
		public virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(IEnumerable<byte[]> send)
		{
			return await NetSupport.ReadFromCoreServerAsync(send, ReadFromCoreServerAsync).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ReadFromCoreServer(System.Byte[],System.Boolean,System.Boolean)" />
		public virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(byte[] send, bool hasResponseData, bool usePackAndUnpack)
		{
			OperateResult<byte[]> read2 = new OperateResult<byte[]>();
			OperateResult enter = ((!HslHelper.UseAsyncLock) ? communicationPipe.CommunicationLock.EnterLock(communicationPipe.ReceiveTimeOut) : (await Task.Run(() => communicationPipe.CommunicationLock.EnterLock(communicationPipe.ReceiveTimeOut)).ConfigureAwait(continueOnCapturedContext: false)));
			if (!enter.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(enter);
			}
			try
			{
				OperateResult<bool> pipe = await communicationPipe.OpenCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
				if (!pipe.IsSuccess)
				{
					communicationPipe.CommunicationLock.LeaveLock();
					read2.CopyErrorFromOther(pipe);
					return read2;
				}
				if (pipe.Content)
				{
					OperateResult ini = await InitializationOnConnectAsync().ConfigureAwait(continueOnCapturedContext: false);
					if (!ini.IsSuccess)
					{
						communicationPipe.CommunicationLock.LeaveLock();
						return OperateResult.CreateFailedResult<byte[]>(ini);
					}
				}
				read2 = await ReadFromCoreServerAsync(communicationPipe, send, hasResponseData, usePackAndUnpack).ConfigureAwait(continueOnCapturedContext: false);
				ExtraAfterReadFromCoreServer(read2);
				communicationPipe.CommunicationLock.LeaveLock();
			}
			catch
			{
				communicationPipe.CommunicationLock.LeaveLock();
				throw;
			}
			if (!communicationPipe.IsPersistentConnection)
			{
				await ExtraOnDisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
				await communicationPipe.CloseCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			return read2;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.ReadFromCoreServer(HslCommunication.Core.Pipe.CommunicationPipe,System.Byte[],System.Boolean,System.Boolean)" />
		public virtual async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(CommunicationPipe pipe, byte[] send, bool hasResponseData, bool usePackAndUnpack)
		{
			if (usePackAndUnpack)
			{
				send = PackCommandWithHeader(send);
			}
			if (sendbyteBefore != null)
			{
				OperateResult sendBefore = await communicationPipe.SendAsync(sendbyteBefore).ConfigureAwait(continueOnCapturedContext: false);
				if (!sendBefore.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(sendBefore);
				}
				LogSendMessage(sendbyteBefore);
			}
			LogSendMessage(send);
			OperateResult<byte[]> read = await pipe.ReadFromCoreServerAsync(GetNewNetMessage(), send, hasResponseData, LogRevcMessage).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return read;
			}
			if (!usePackAndUnpack)
			{
				return read;
			}
			OperateResult<byte[]> unpack = UnpackResponseContent(send, read.Content);
			if (!unpack.IsSuccess && unpack.ErrorCode == int.MinValue)
			{
				unpack.ErrorCode = 10000;
			}
			return unpack;
		}

		/// <summary>
		/// 获取当前的报文进行日志记录的时候，是否使用二进制的格式记录，默认返回 <see cref="F:HslCommunication.Core.Net.BinaryCommunication.LogMsgFormatBinary" />，重写可以根据<paramref name="session" />对象分别返回不同记录模式<br />
		/// Whether to log the current packet in binary format, the default return is <see cref="F:HslCommunication.Core.Net.BinaryCommunication.LogMsgFormatBinary" />. If you want to override it, 
		/// different recording modes can be returned according to <paramref name="session" />
		/// </summary>
		/// <param name="session">会话对象</param>
		/// <param name="content">等待记录的字节消息内容</param>
		/// <returns>是否二进制记录报文格式</returns>
		protected virtual string GetLogTextFromBinary(PipeSession session, byte[] content)
		{
			return LogMsgFormatBinary ? content.ToHexString(' ') : SoftBasic.GetAsciiStringRender(content);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.LogSendMessage(System.Byte[],HslCommunication.Core.Net.PipeSession)" />
		protected void LogSendMessage(byte[] content)
		{
			LogSendMessage(content, null);
		}

		/// <summary>
		/// 使用日志记录一个发送的报文信息<br />
		/// Logs are used to record information about a send packet
		/// </summary>
		/// <param name="content">接收的报文信息</param>
		/// <param name="session">会话对象信息</param>
		protected void LogSendMessage(byte[] content, PipeSession session)
		{
			if (content != null)
			{
				string text = ((session == null) ? string.Empty : $"<{session.Communication}> ");
				LogNet?.WriteDebug(ToString(), text + StringResources.Language.Send + " : " + GetLogTextFromBinary(session, content));
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.LogRevcMessage(System.Byte[],HslCommunication.Core.Net.PipeSession)" />
		protected void LogRevcMessage(byte[] content)
		{
			LogRevcMessage(content, null);
		}

		/// <summary>
		/// 使用日志记录一个接收的报文信息<br /> `
		/// Logs are used to record information about a received packet
		/// </summary>
		/// <param name="content">接收的报文信息</param>
		/// <param name="session">会话对象信息</param>
		protected void LogRevcMessage(byte[] content, PipeSession session)
		{
			if (content != null)
			{
				string text = ((session == null) ? string.Empty : $"<{session.Communication}> ");
				LogNet?.WriteDebug(ToString(), text + StringResources.Language.Receive + " : " + GetLogTextFromBinary(session, content));
			}
		}

		/// <summary>
		/// 将当前的通信对象设置DTU模式，允许传入现成的管道，并返回初始化结果，如果该设备重写了握手报文，就是返回握手结果<br />
		/// Set the current communication object to DTU mode, allow the existing pipe to be passed in, and return the initialization result, 
		/// if the device rewrites the handshake packet, the handshake result is returned
		/// </summary>
		/// <param name="pipe">DTU的管道信息</param>
		/// <returns>是否设置管道并初始化成功</returns>
		public OperateResult SetDtuPipe(PipeDtuNet pipe)
		{
			if (pipe != null)
			{
				if (string.IsNullOrEmpty(ConnectionId))
				{
					ConnectionId = pipe.DTU;
				}
				CommunicationPipe = pipe;
				if (!pipe.IsConnectError())
				{
					return InitializationOnConnect();
				}
				return new OperateResult("Session dtu[" + pipe.DTU + "] net error");
			}
			return new OperateResult("pipe is NULL");
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.SetDtuPipe(HslCommunication.Core.Pipe.PipeDtuNet)" />
		public async Task<OperateResult> SetDtuPipeAsync(PipeDtuNet pipe)
		{
			if (pipe != null)
			{
				if (string.IsNullOrEmpty(ConnectionId))
				{
					ConnectionId = pipe.DTU;
				}
				CommunicationPipe = pipe;
				if (!pipe.IsConnectError())
				{
					return await InitializationOnConnectAsync();
				}
				return new OperateResult("Session dtu[" + pipe.DTU + "] net error");
			}
			return new OperateResult("pipe is NULL");
		}
	}
}
