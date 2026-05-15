using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// 开放以太网协议，在拧紧枪中应用广泛，本通信支持基本的问答机制以及订阅机制，支持完全自定义的参数指定及数据读取。<br />
	/// Open Ethernet protocol, widely used in tightening guns, this communication supports basic question answering mechanism and subscription mechanism, supports fully customized parameter specification and data reading.
	/// </summary>
	/// <remarks>
	/// 自定义的读取使用<see cref="M:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.ReadCustomer(System.Int32,System.Int32,System.Int32,System.Int32,System.Collections.Generic.List{System.String})" />来实现，如果是订阅的数据，使用<see cref="E:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.OnReceivedOpenMessage" />绑定自己的方法触发。更详细的示例参考：http://api.hslcommunication.cn<br />
	/// Custom reads are implemented using <see cref="M:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.ReadCustomer(System.Int32,System.Int32,System.Int32,System.Int32,System.Collections.Generic.List{System.String})" />, and if it is subscribed data, use <see cref="E:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.OnReceivedOpenMessage" /> 
	/// bind your own method to trigger it. For a more detailed example, refer to: http://api.hslcommunication.cn
	/// </remarks>
	/// <example>
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\OpenProtocolNetSample.cs" region="Usage" title="连接及自定义读取使用" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\OpenProtocolNetSample.cs" region="Usage2" title="便捷的读取示例" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\OpenProtocolNetSample.cs" region="Usage3" title="订阅事件处理操作" />
	/// </example>
	public class OpenProtocolNet : TcpNetCommunication
	{
		private Timer timer;

		private ParameterSetMessages parameterSetMessages;

		private JobMessage jobMessage;

		private TighteningResultMessages tighteningResultMessages;

		private ToolMessages toolMessages;

		private TimeMessages timeMessages;

		private int revisonOnConnected;

		/// <summary>
		/// 获取或设置初始化连接时 MID0001 命令的版本号，默认为 1，如果设置小于 0，则表示不发送 MID0001 命令。<br />
		/// Get or set the version of the MID0001 command when initializing the connection, the default is 1, if the setting is less than 0, it means that the MID0001 command is not sent.
		/// </summary>
		/// <remarks>
		/// 当连接的设备是 MT Focus 6000的控制器时，本值需要设置为 6
		/// </remarks>
		public int RevisonOnConnected
		{
			get
			{
				return revisonOnConnected;
			}
			set
			{
				revisonOnConnected = value;
			}
		}

		/// <summary>
		/// 针对控制器的主动发送的消息，是否无视<c>Ack</c>标记，全部进行返回信号操作，默认为 <c>False</c>，将根据报文里的<c>Ack</c>信号来决定是否返回数据<br />
		/// If the default value is <c>False</c>, the controller will<c></c> decide whether to return data based on the <c>Ack</c> signal in the packet
		/// </summary>
		public bool AutoAckControllerMessage { get; set; } = false;


		/// <summary>
		/// 参数集合操作的相关属性，可以用来获取参数ID列表，设置数据等操作。<br />
		/// The properties related to parameter collection operations can be used to obtain parameter ID lists, set data, and other operations.
		/// </summary>
		public ParameterSetMessages ParameterSetMessages => parameterSetMessages;

		/// <summary>
		/// 任务消息的相关属性，可以用来获取任务的数据，订阅任务，取消订阅任务，选择任务，启动任务。<br />
		/// The relevant properties of task messages can be used to obtain task data, subscribe to tasks, unsubscribe tasks, select tasks, and start tasks.
		/// </summary>
		public JobMessage JobMessage => jobMessage;

		/// <summary>
		/// 拧紧结果消息的操作属性
		/// </summary>
		public TighteningResultMessages TighteningResultMessages => tighteningResultMessages;

		/// <summary>
		/// 工具消息的操作属性
		/// </summary>
		public ToolMessages ToolMessages => toolMessages;

		/// <summary>
		/// 时间消息的属性
		/// </summary>
		public TimeMessages TimeMessages => timeMessages;

		/// <summary>
		/// 当接收到OpenProtocol协议消息触发的事件
		/// </summary>
		public event EventHandler<OpenEventArgs> OnReceivedOpenMessage;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public OpenProtocolNet()
		{
			revisonOnConnected = 1;
			CommunicationPipe.UseServerActivePush = true;
			timer = new Timer(ThreadKeepAlive, null, 10000, 10000);
			parameterSetMessages = new ParameterSetMessages(this);
			jobMessage = new JobMessage(this);
			tighteningResultMessages = new TighteningResultMessages(this);
			toolMessages = new ToolMessages(this);
			timeMessages = new TimeMessages(this);
			LogMsgFormatBinary = false;
		}

		/// <summary>
		/// 使用指定的IP地址及端口来初始化对象<br />
		/// Use the specified IP address and port to initialize the object
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		public OpenProtocolNet(string ipAddress, int port = 4545)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new OpenProtocolMessage();
		}

		private void ThreadKeepAlive(object state)
		{
			if (!CommunicationPipe.IsConnectError())
			{
				OperateResult<byte[]> operateResult = BuildReadCommand(9999, 1, -1, -1, null);
				if (operateResult.IsSuccess)
				{
					CommunicationPipe.Send(operateResult.Content);
				}
			}
		}

		/// <inheritdoc />
		protected override OperateResult InitializationOnConnect()
		{
			if (revisonOnConnected >= 0)
			{
				OperateResult<byte[]> operateResult = BuildReadCommand(1, revisonOnConnected, -1, -1, null);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(operateResult);
				}
				OperateResult operateResult2 = CommunicationPipe.Send(operateResult.Content);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = CommunicationPipe.ReceiveMessage(GetNewNetMessage(), null, useActivePush: false);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>("InitializationOnConnect failed", operateResult3);
				}
				string @string = Encoding.ASCII.GetString(operateResult3.Content);
				if (@string.Substring(4, 4) == "0002")
				{
					return base.InitializationOnConnect();
				}
				return new OperateResult("Failed:" + @string.Substring(4, 4));
			}
			return base.InitializationOnConnect();
		}

		/// <inheritdoc />
		protected override OperateResult ExtraOnDisconnect()
		{
			OperateResult<byte[]> operateResult = BuildReadCommand(3, 1, -1, -1, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			return ReadFromCoreServer(CommunicationPipe, operateResult.Content, hasResponseData: true, usePackAndUnpack: true);
		}

		/// <inheritdoc />
		protected override async Task<OperateResult> InitializationOnConnectAsync()
		{
			if (revisonOnConnected >= 0)
			{
				OperateResult<byte[]> command = BuildReadCommand(1, revisonOnConnected, -1, -1, null);
				if (!command.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(command);
				}
				OperateResult send = await CommunicationPipe.SendAsync(command.Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!send.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(send);
				}
				OperateResult<byte[]> receive = await CommunicationPipe.ReceiveMessageAsync(GetNewNetMessage(), null, useActivePush: false).ConfigureAwait(continueOnCapturedContext: false);
				if (!receive.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>("InitializationOnConnect failed", receive);
				}
				string reply = Encoding.ASCII.GetString(receive.Content);
				if (reply.Substring(4, 4) == "0002")
				{
					return await base.InitializationOnConnectAsync();
				}
				return new OperateResult("Failed:" + reply.Substring(4, 4));
			}
			return await base.InitializationOnConnectAsync();
		}

		private int DecideSubscribeData(int mid)
		{
			if (mid == 15 || mid == 35 || mid == 52 || mid == 61 || mid == 71 || mid == 74 || mid == 76 || mid == 91 || mid == 101)
			{
				return mid + 1;
			}
			if (mid == 106 || mid == 107)
			{
				return 108;
			}
			if (mid == 121 || mid == 122 || mid == 123 || mid == 124)
			{
				return 125;
			}
			return mid switch
			{
				152 => 153, 
				211 => 212, 
				217 => 218, 
				221 => 222, 
				242 => 243, 
				251 => 252, 
				401 => 402, 
				421 => 422, 
				_ => -1, 
			};
		}

		/// <inheritdoc />
		protected override bool DecideWhetherQAMessage(CommunicationPipe pipe, OperateResult<byte[]> receive)
		{
			if (receive.Content.Length >= 20)
			{
				int num = Convert.ToInt32(Encoding.ASCII.GetString(receive.Content, 4, 4));
				bool flag = receive.Content[11] == 48;
				if (num == 9999)
				{
					return false;
				}
				int num2 = DecideSubscribeData(num);
				if (num2 > 0)
				{
					if (flag || AutoAckControllerMessage)
					{
						pipe.Send(BuildReadCommand(num2, 1, -1, -1, null).Content);
					}
					this.OnReceivedOpenMessage?.Invoke(this, new OpenEventArgs(Encoding.ASCII.GetString(receive.Content).TrimEnd(default(char))));
					return false;
				}
			}
			return base.DecideWhetherQAMessage(pipe, receive);
		}

		/// <summary>
		/// 使用自定义的命令读取数据，需要指定每个参数信息，然后返回字符串数据内容，根据实际的功能码，解析出实际的数据信息<br />
		/// To use a custom command to read data, you need to specify each parameter information, then return the string data content, and parse the actual data information according to the actual function code
		/// </summary>
		/// <param name="mid">The MID is four bytes long and is specified by four ASCII digits(‘0’…’9’). The MID describes how to interpret the message.</param>
		/// <param name="revison">The revision of the MID is specified by three ASCII digits(‘0’…’9’).The MID revision is unique per MID and is used in case several versions are available for the same MID. </param>
		/// <param name="stationId">The station the message is addressed to in the case of controller with multi-station configuration.The station ID is 1 byte long and is specified by one ASCII digit(‘0’…’9’). </param>
		/// <param name="spindleId">The spindle the message is addressed to in the case several spindles are connected to the same controller. The spindle ID is 2 bytes long and is specified by two ASCII digits (‘0’…’9’). </param>
		/// <param name="parameters">The Data Field is ASCII data representing the data. The data contains a list of parameters depending on the MID.Each parameter is represented with an ID and the parameter value. </param>
		/// <returns>结果数据信息</returns>
		[HslMqttApi(Description = "使用自定义的命令读取数据，需要指定每个参数信息，然后返回字符串数据内容，根据实际的功能码，解析出实际的数据信息")]
		public OperateResult<string> ReadCustomer(int mid, int revison, int stationId, int spindleId, List<string> parameters)
		{
			if (parameters == null)
			{
				parameters = new List<string>();
			}
			OperateResult<byte[]> operateResult = BuildReadCommand(mid, revison, stationId, spindleId, parameters);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			OperateResult operateResult3 = CheckRequestReplyMessages(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult3);
			}
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetString(operateResult2.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.ReadCustomer(System.Int32,System.Int32,System.Int32,System.Int32,System.Collections.Generic.List{System.String})" />
		public async Task<OperateResult<string>> ReadCustomerAsync(int mid, int revison, int stationId, int spindleId, List<string> parameters)
		{
			if (parameters == null)
			{
				parameters = new List<string>();
			}
			OperateResult<byte[]> command = BuildReadCommand(mid, revison, stationId, spindleId, parameters);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(command);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			OperateResult check = CheckRequestReplyMessages(read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(check);
			}
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetString(read.Content));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"OpenProtocolNet[{IpAddress}:{Port}]";
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.OpenProtocol.OpenProtocolNet.BuildOpenProtocolMessage(System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Boolean,System.String[])" />
		public static OperateResult<byte[]> BuildReadCommand(int mid, int revison, int stationId, int spindleId, List<string> parameters)
		{
			if (mid < 0 || mid > 9999)
			{
				return new OperateResult<byte[]>("Mid must be between 0 - 9999");
			}
			if (revison < 0 || revison > 999)
			{
				return new OperateResult<byte[]>("revison must be between 0 - 999");
			}
			if (stationId > 9)
			{
				return new OperateResult<byte[]>("stationId must be between 0 - 9");
			}
			if (spindleId > 99)
			{
				return new OperateResult<byte[]>("spindleId must be between 0 - 99");
			}
			int count = 0;
			parameters?.ForEach(delegate(string m)
			{
				count += m.Length;
			});
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append((20 + count).ToString("D4"));
			stringBuilder.Append(mid.ToString("D4"));
			stringBuilder.Append(revison.ToString("D3"));
			stringBuilder.Append('0');
			stringBuilder.Append((stationId < 0) ? "  " : stationId.ToString("D2"));
			stringBuilder.Append((spindleId < 0) ? "  " : spindleId.ToString("D2"));
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			if (parameters != null)
			{
				for (int i = 0; i < parameters.Count; i++)
				{
					stringBuilder.Append(parameters[i]);
				}
			}
			stringBuilder.Append('\0');
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		/// <summary>
		/// 构建一个读取的初始报文
		/// </summary>
		/// <param name="mid">The MID is four bytes long and is specified by four ASCII digits(‘0’…’9’). The MID describes how to interpret the message.</param>
		/// <param name="revison">The revision of the MID is specified by three ASCII digits(‘0’…’9’).The MID revision is unique per MID and is used in case several versions are available for the same MID. </param>
		/// <param name="ack">The No Ack Flag is used when setting a subscription. If the No Ack flag is not set in a subscription it means that the subscriber will acknowledge each “push” message sent by the controller (reliable mode).</param>
		/// <param name="stationId">The station the message is addressed to in the case of controller with multi-station configuration.The station ID is 1 byte long and is specified by one ASCII digit(‘0’…’9’). </param>
		/// <param name="spindleId">The spindle the message is addressed to in the case several spindles are connected to the same controller. The spindle ID is 2 bytes long and is specified by two ASCII digits (‘0’…’9’). </param>
		/// <param name="withIndex">每个参数的前面，是否携带索引信息</param>
		/// <param name="parameters">The Data Field is ASCII data representing the data. The data contains a list of parameters depending on the MID.Each parameter is represented with an ID and the parameter value. </param>
		/// <returns>原始字节的报文信息</returns>
		public static OperateResult<byte[]> BuildOpenProtocolMessage(int mid, int revison, int ack, int stationId, int spindleId, bool withIndex, params string[] parameters)
		{
			if (mid < 0 || mid > 9999)
			{
				return new OperateResult<byte[]>("Mid must be between 0 - 9999");
			}
			if (revison < 0 || revison > 999)
			{
				return new OperateResult<byte[]>("revison must be between 0 - 999");
			}
			if (stationId > 9)
			{
				return new OperateResult<byte[]>("stationId must be between 0 - 9");
			}
			if (spindleId > 99)
			{
				return new OperateResult<byte[]>("spindleId must be between 0 - 99");
			}
			int num = 0;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(mid.ToString("D4"));
			stringBuilder.Append(revison.ToString("D3"));
			stringBuilder.Append((ack < 0) ? " " : ack.ToString("D1"));
			stringBuilder.Append((stationId < 0) ? "  " : stationId.ToString("D2"));
			stringBuilder.Append((spindleId < 0) ? "  " : spindleId.ToString("D2"));
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			stringBuilder.Append(' ');
			if (parameters != null)
			{
				for (int i = 0; i < parameters.Length; i++)
				{
					if (withIndex)
					{
						stringBuilder.Append((i + 1).ToString("D2"));
						stringBuilder.Append(parameters[i]);
						num += 2 + parameters[i].Length;
					}
					else
					{
						stringBuilder.Append(parameters[i]);
						num += parameters[i].Length;
					}
				}
			}
			stringBuilder.Append('\0');
			stringBuilder.Insert(0, (20 + num).ToString("D4"));
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		/// <summary>
		/// 根据错误代码来获取到错误文本信息
		/// </summary>
		/// <param name="code">错误代号</param>
		/// <returns>错误文本</returns>
		public static string GetErrorText(int code)
		{
			return code switch
			{
				1 => "Invalid data", 
				2 => "Parameter set ID not present", 
				3 => "Parameter set can not be set.", 
				4 => "Parameter set not running", 
				6 => "VIN upload subscription already exists", 
				7 => "VIN upload subscription does not exists", 
				8 => "VIN input source not granted", 
				9 => "Last tightening result subscription already exists", 
				10 => "Last tightening result subscription does not exist", 
				11 => "Alarm subscription already exists", 
				12 => "Alarm subscription does not exist", 
				13 => "Parameter set selection subscription already exists", 
				14 => "Parameter set selection subscription does not exist", 
				15 => "Tightening ID requested not found", 
				16 => "Connection rejected protocol busy", 
				17 => "Job ID not present", 
				18 => "Job info subscription already exists", 
				19 => "Job info subscription does not exist", 
				20 => "Job can not be set", 
				21 => "Job not running", 
				22 => "Not possible to execute dynamic Job request", 
				23 => "Job batch decrement failed", 
				30 => "Controller is not a sync Master/station controller", 
				31 => "Multi-spindle status subscription already exists", 
				32 => "Multi-spindle status subscription does not exist", 
				33 => "Multi-spindle result subscription already exists", 
				34 => "Multi-spindle result subscription does not exist", 
				40 => "Job line control info subscription already exists", 
				41 => "Job line control info subscription does not exist", 
				42 => "Identifier input source not granted", 
				43 => "Multiple identifiers work order subscription already exists", 
				44 => "Multiple identifiers work order subscription does not exist", 
				50 => "Status external monitored inputs subscription already exists", 
				51 => "Status external monitored inputs subscription does not exist", 
				52 => "IO device not connected", 
				53 => "Faulty IO device ID", 
				58 => "No alarm present", 
				59 => "Tool currently in use", 
				60 => "No histogram available", 
				70 => "Calibration failed", 
				79 => "Command failed", 
				80 => "Audi emergency status subscription exists", 
				81 => "Audi emergency status subscription does not exist", 
				82 => "Automatic/Manual mode subscribe already exist", 
				83 => "Automatic/Manual mode subscribe does not exist", 
				84 => "The relay function subscription already exists", 
				85 => "The relay function subscription does not exist", 
				86 => "The selector socket info subscription already exist", 
				87 => "The selector socket info subscription does not exist", 
				88 => "The digin info subscription already exist", 
				89 => "The digin info subscription does not exist", 
				90 => "Lock at bach done subscription already exist", 
				91 => "Lock at bach done subscription does not exist", 
				92 => "Open protocol commands disabled", 
				93 => "Open protocol commands disabled subscription already exists", 
				94 => "Open protocol commands disabled subscription does not exist", 
				95 => "Reject request, PowerMACS is in manual mode", 
				96 => "Client already connected", 
				97 => "MID revision unsupported", 
				98 => "Controller internal request timeout", 
				99 => "Unknown MID", 
				_ => StringResources.Language.UnknownError, 
			};
		}

		/// <summary>
		/// 检查请求返回的消息是否合法的
		/// </summary>
		/// <param name="reply">返回的消息</param>
		/// <returns>是否合法的结果对象</returns>
		public static OperateResult CheckRequestReplyMessages(byte[] reply)
		{
			try
			{
				if (Encoding.ASCII.GetString(reply, 4, 4) == "0004")
				{
					string @string = Encoding.ASCII.GetString(reply, 20, 4);
					int num = Convert.ToInt32(Encoding.ASCII.GetString(reply, 24, 2));
					if (num == 0)
					{
						return OperateResult.CreateSuccessResult();
					}
					return new OperateResult(num, "The request MID " + @string + " Select parameter set failed: " + GetErrorText(num));
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
		}
	}
}
