using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Core.Security;
using HslCommunication.LogNet;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// 一个Mqtt的服务器类对象，本服务器支持发布订阅操作，支持从服务器强制推送数据，支持往指定的客户端推送，支持基于一问一答的远程过程调用（RPC）的数据交互，支持文件上传下载。根据这些功能从而定制化出满足各个场景的服务器，详细的使用说明可以参见代码api文档示例。<br />
	/// An Mqtt server class object. This server supports publish and subscribe operations, supports forced push data from the server, 
	/// supports push to designated clients, supports data interaction based on one-question-one-answer remote procedure calls (RPC), 
	/// and supports file upload and download . According to these functions, the server can be customized to meet various scenarios. 
	/// For detailed instructions, please refer to the code api document example.
	/// </summary>
	/// <remarks>
	/// 本MQTT服务器功能丰富，可以同时实现，用户名密码验证，在线客户端的管理，数据订阅推送，单纯的数据收发，心跳检测，订阅通配符，同步数据访问，文件上传，下载，删除，遍历，详细参照下面的示例说明<br />
	/// 通配符请查看<see cref="P:HslCommunication.MQTT.MqttServer.TopicWildcard" />属性，规则参考：http://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html#appendix-a
	/// </remarks>
	/// <example>
	/// 最简单的使用，就是实例化，启动服务即可
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample1" title="简单的实例化" />
	/// 当然了，我们可以稍微的复杂一点，加一个功能，验证连接的客户端操作
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample2" title="增加验证" />
	/// 我们可以对ClientID，用户名，密码进行验证，那么我们可以动态修改client id么？比如用户名密码验证成功后，client ID我想设置为权限等级。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample2_1" title="动态修改Client ID" />
	/// 如果我想强制该客户端不能主动发布主题，可以这么操作。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample2_2" title="禁止发布主题" />
	/// 你也可以对clientid进行过滤验证，只要结果返回不是0，就可以了。接下来我们实现一个功能，所有客户端的发布的消息在控制台打印出来,
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample3" title="打印所有发布" />
	/// 捕获客户端刚刚上线的时候，方便我们进行一些额外的操作信息。下面的意思就是返回一个数据，将数据发送到指定的会话内容上去
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample4" title="客户端上线信息" />
	/// 下面演示如何从服务器端发布数据信息，包括多种发布的方法，消息是否驻留，详细看说明即可
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample5" title="服务器发布" />
	/// 下面演示如何支持同步网络访问，当客户端是同步网络访问时，协议内容会变成HUSL，即被视为同步客户端，进行相关的操作，主要进行远程调用RPC，以及查询MQTT的主题列表。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample6" title="同步访问支持" />
	/// 如果需要查看在线信息，可以随时获取<see cref="P:HslCommunication.MQTT.MqttServer.OnlineCount" />属性，如果需要查看报文信息，可以实例化日志，参考日志的说明即可。<br /><br />
	/// 针对上面同步网络访问，虽然比较灵活，但是什么都要自己控制，无疑增加了代码的复杂度，举个例子，当你的topic分类很多的时候，已经客户端协议多个参数的时候，需要大量的手动解析的代码，
	/// 影响代码美观，而且让代码更加的杂乱，除此之外，还有个巨大的麻烦，服务器提供了很多的topic处理程序（可以换个称呼，暴露的API接口），
	/// 客户端没法清晰的浏览到，需要查找服务器代码才能知晓，而且服务器更新了接口，客户端有需要同步查看服务器的代码才行，以及做权限控制也很麻烦。<br />
	/// 所以在Hsl里面的MQTT服务器，提供了注册API接口的功能，只需要一行注册代码，你的类的方法自动就会变为API解析，所有的参数都是同步解析的，如果你返回的是
	/// OperateResult&lt;T&gt;类型对象，还支持是否成功的结果报告，否则一律视为json字符串，返回给调用方。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample7" title="基于MQTT的RPC接口实现" />
	/// 如果需要查看在线信息，可以随时获取<see cref="P:HslCommunication.MQTT.MqttServer.OnlineCount" />属性，如果需要查看报文信息，可以实例化日志，参考日志的说明即可。<br /><br />
	/// 最后介绍一下文件管理服务是如何启动的，在启动了文件管理服务之后，其匹配的客户端 <see cref="T:HslCommunication.MQTT.MqttSyncClient" /> 就可以上传下载，遍历文件了。
	/// 而服务器端做的就是启用服务，如果你需要一些更加自由的权限控制，比如某个账户只能下载，不能其他操作，都是可以实现的。更加多的示例参考DEMO程序。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttServerSample.cs" region="Sample8" title="基于MQTT的文件管理服务启动" />
	/// </example>
	public class MqttServer : NetworkServerBase, IDisposable
	{
		/// <summary>
		/// 当客户端进行文件操作时，校验客户端合法性的委托，操作码具体查看<seealso cref="T:HslCommunication.MQTT.MqttControlMessage" />的常量值<br />
		/// When client performing file operations, verify the legitimacy of the client, and check the constant value of <seealso cref="T:HslCommunication.MQTT.MqttControlMessage" /> for the operation code.
		/// </summary>
		/// <param name="session">会话状态</param>
		/// <param name="code">操作码</param>
		/// <param name="groups">分类信息</param>
		/// <param name="fileNames">文件名</param>
		/// <returns>是否成功</returns>
		public delegate OperateResult FileOperateVerificationDelegate(MqttSession session, byte code, string[] groups, string[] fileNames);

		/// <summary>
		/// 文件变化的委托信息
		/// </summary>
		/// <param name="session">当前的会话信息，包含用户的基本信息</param>
		/// <param name="operateInfo">当前的文件操作信息，具体指示上传，下载，删除操作</param>
		public delegate void FileChangedDelegate(MqttSession session, MqttFileOperateInfo operateInfo);

		/// <summary>
		/// Mqtt的消息收到委托
		/// </summary>
		/// <param name="session">当前会话的内容</param>
		/// <param name="message">Mqtt的消息</param>
		public delegate void OnClientApplicationMessageReceiveDelegate(MqttSession session, MqttClientApplicationMessage message);

		/// <summary>
		/// 当前mqtt客户端连接上服务器的事件委托
		/// </summary>
		/// <param name="session">当前的会话对象</param>
		public delegate void OnClientConnectedDelegate(MqttSession session);

		/// <summary>
		/// 验证的委托
		/// </summary>
		/// <param name="mqttSession">当前的MQTT的会话内容</param>
		/// <param name="clientId">客户端的id</param>
		/// <param name="userName">用户名</param>
		/// <param name="password">密码</param>
		/// <returns>0则是通过，否则，就是连接失败</returns>
		public delegate int ClientVerificationDelegate(MqttSession mqttSession, string clientId, string userName, string password);

		private Dictionary<string, MqttRpcApiInfo> apiTopicServiceDict;

		private object rpcApiLock;

		private readonly Dictionary<string, FileMarkId> dictionaryFilesMarks;

		private readonly object dictHybirdLock;

		private string filesDirectoryPath = null;

		private bool fileServerEnabled = false;

		private Dictionary<string, GroupFileContainer> m_dictionary_group_marks = new Dictionary<string, GroupFileContainer>();

		private SimpleHybirdLock group_marks_lock = new SimpleHybirdLock();

		private MqttFileMonitor fileMonitor = new MqttFileMonitor();

		private readonly Dictionary<string, MqttClientApplicationMessage> retainKeys;

		private readonly object keysLock;

		private readonly List<MqttSession> mqttSessions = new List<MqttSession>();

		private readonly object sessionsLock = new object();

		private Timer timerHeart;

		private LogStatisticsDict statisticsDict;

		private bool disposedValue;

		private RSACryptoServiceProvider providerMqttServer = null;

		private bool topicWildcard = false;

		private LogStatistics topicStatistics;

		/// <inheritdoc cref="P:HslCommunication.Enthernet.HttpServer.LogStatistics" />
		public LogStatisticsDict LogStatistics => statisticsDict;

		/// <summary>
		/// 获取或设置是否启用订阅主题通配符的功能，默认为 False<br />
		/// Gets or sets whether to enable the function of subscribing to the topic wildcard, the default is False
		/// </summary>
		/// <remarks>
		/// 启动之后，通配符示例：finance/stock/ibm/#; finance/+; '#' 是匹配所有主题，'+' 是匹配一级主题树。<br />
		/// 通配符的规则参考如下的网址：http://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html#appendix-a
		/// </remarks>
		public bool TopicWildcard
		{
			get
			{
				return topicWildcard;
			}
			set
			{
				topicWildcard = value;
			}
		}

		/// <summary>
		/// 获取当前的在线的客户端数量<br />
		/// Gets the number of clients currently online
		/// </summary>
		public int OnlineCount => mqttSessions.Count;

		/// <summary>
		/// 获得当前所有的在线的MQTT客户端信息，包括异步的客户端及同步请求的客户端。<br />
		/// Obtain all current online MQTT client information, including asynchronous client and synchronous request client.
		/// </summary>
		public MqttSession[] OnlineSessions
		{
			get
			{
				MqttSession[] result = null;
				lock (sessionsLock)
				{
					result = mqttSessions.ToArray();
				}
				return result;
			}
		}

		/// <summary>
		/// 获得当前异步客户端在线的MQTT客户端信息。<br />
		/// Get the MQTT client information of the current asynchronous client online.
		/// </summary>
		public MqttSession[] MqttOnlineSessions
		{
			get
			{
				MqttSession[] result = null;
				lock (sessionsLock)
				{
					result = mqttSessions.Where((MqttSession m) => m.Protocol == "MQTT").ToArray();
				}
				return result;
			}
		}

		/// <summary>
		/// 获得当前同步客户端在线的MQTT客户端信息，如果客户端是短连接，将难以捕获在在线信息。<br />
		/// Obtain the MQTT client information of the current synchronization client online. If the client is a short connection, it will be difficult to capture the online information. <br />
		/// </summary>
		public MqttSession[] SyncOnlineSessions
		{
			get
			{
				MqttSession[] result = null;
				lock (sessionsLock)
				{
					result = mqttSessions.Where((MqttSession m) => m.Protocol == "HUSL").ToArray();
				}
				return result;
			}
		}

		/// <summary>
		/// 获取或设置当前的主题统计信息，默认按照天进行统计，连续统计30天。<br />
		/// You can obtain or set the statistics of the current topic. By default, the statistics are collected every day for 30 consecutive days.
		/// </summary>
		public LogStatistics TopicStatistics
		{
			get
			{
				return topicStatistics;
			}
			set
			{
				topicStatistics = value;
			}
		}

		/// <summary>
		/// 当客户端进行文件操作时，校验客户端合法性的事件，操作码具体查看<seealso cref="T:HslCommunication.MQTT.MqttControlMessage" />的常量值<br />
		/// When client performing file operations, it is an event to verify the legitimacy of the client. For the operation code, check the constant value of <seealso cref="T:HslCommunication.MQTT.MqttControlMessage" />
		/// </summary>
		public event FileOperateVerificationDelegate FileOperateVerification;

		/// <summary>
		/// 文件变化的事件，当文件上传的时候，文件下载的时候，文件被删除的时候触发。<br />
		/// The file change event is triggered when the file is uploaded, when the file is downloaded, or when the file is deleted.
		/// </summary>
		public event FileChangedDelegate OnFileChangedEvent;

		/// <summary>
		/// 当收到客户端发来的<see cref="T:HslCommunication.MQTT.MqttClientApplicationMessage" />消息时触发<br />
		/// Triggered when a <see cref="T:HslCommunication.MQTT.MqttClientApplicationMessage" /> message is received from the client
		///             </summary>
		public event OnClientApplicationMessageReceiveDelegate OnClientApplicationMessageReceive;

		/// <summary>
		/// Mqtt的客户端连接上来时触发<br />
		/// Triggered when Mqtt client connects
		/// </summary>
		public event OnClientConnectedDelegate OnClientConnected;

		/// <summary>
		/// Mqtt的客户端下线时触发<br />
		/// Triggered when Mqtt client connects
		/// </summary>
		public event OnClientConnectedDelegate OnClientDisConnected;

		/// <summary>
		/// 当客户端连接时，触发的验证事件<br />
		/// Validation event triggered when the client connects
		/// </summary>
		public event ClientVerificationDelegate ClientVerification;

		/// <summary>
		/// 实例化一个MQTT协议的服务器<br />
		/// Instantiate a MQTT protocol server
		/// </summary>
		/// <param name="providerServer">
		/// RSA秘钥对象，默认为空，表示使用随机生成的秘钥信息，如果需要自定义的RSA密钥，则需要实例化当前参数对象，具体通信的时候加密与否，取决于客户端连接的方式。<br />
		/// The RSA key object, which is empty by default, indicates that the randomly generated key information is used. If a custom RSA key is required, 
		/// the current parameter object needs to be instantiated. Whether the specific communication is encrypted or not depends on the way the client connects. .
		/// </param>
		public MqttServer(RSACryptoServiceProvider providerServer = null)
		{
			statisticsDict = new LogStatisticsDict(GenerateMode.ByEveryDay, 60);
			retainKeys = new Dictionary<string, MqttClientApplicationMessage>();
			apiTopicServiceDict = new Dictionary<string, MqttRpcApiInfo>();
			keysLock = new object();
			rpcApiLock = new object();
			timerHeart = new Timer(ThreadTimerHeartCheck, null, 2000, 10000);
			dictionaryFilesMarks = new Dictionary<string, FileMarkId>();
			dictHybirdLock = new object();
			providerMqttServer = providerServer;
			topicStatistics = new LogStatistics(GenerateMode.ByEveryDay, 30);
		}

		/// <inheritdoc />
		protected override async void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			OperateResult<byte, byte[]> readMqtt = await ReceiveMqttMessageAsync(socket, 10000);
			if (!readMqtt.IsSuccess)
			{
				return;
			}
			RSACryptoServiceProvider clientKey = null;
			RSACryptoServiceProvider serverKey = null;
			if (readMqtt.Content1 == byte.MaxValue)
			{
				try
				{
					serverKey = providerMqttServer ?? new RSACryptoServiceProvider();
					clientKey = RSAHelper.CreateRsaProviderFromPublicKey(HslSecurity.ByteDecrypt(readMqtt.Content2));
					OperateResult send = Send(socket, MqttHelper.BuildMqttCommand(byte.MaxValue, null, HslSecurity.ByteEncrypt(clientKey.EncryptLargeData(serverKey.GetPEMPublicKey()))).Content);
					if (!send.IsSuccess)
					{
						return;
					}
				}
				catch (Exception ex2)
				{
					Exception ex = ex2;
					base.LogNet?.WriteError("创建客户端的公钥发生了异常！" + ex.Message);
					socket?.Close();
					return;
				}
				readMqtt = await ReceiveMqttMessageAsync(socket, 10000);
				if (!readMqtt.IsSuccess)
				{
					return;
				}
			}
			HandleMqttConnection(socket, endPoint, readMqtt, clientKey, serverKey);
		}

		private async void SocketReceiveCallback(IAsyncResult ar)
		{
			object asyncState = ar.AsyncState;
			MqttSession mqttSession = asyncState as MqttSession;
			if (mqttSession == null)
			{
				return;
			}
			try
			{
				mqttSession.MqttSocket.EndReceive(ar);
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				RemoveAndCloseSession(mqttSession, "Socket EndReceive -> " + ex.Message);
				return;
			}
			if (mqttSession.Protocol == "FILE")
			{
				if (fileServerEnabled)
				{
					await HandleFileMessageAsync(mqttSession);
				}
				RemoveAndCloseSession(mqttSession, string.Empty);
			}
			else
			{
				await HandleWithReceiveMqtt(readMqtt: (!(mqttSession.Protocol == "MQTT")) ? (await ReceiveMqttMessageAsync(mqttSession.MqttSocket, 60000, delegate(long already, long total)
				{
					SyncMqttReceiveProgressBack(mqttSession.MqttSocket, already, total);
				})) : (await ReceiveMqttMessageAsync(mqttSession.MqttSocket, 60000)), mqttSession: mqttSession);
			}
		}

		private void SyncMqttReceiveProgressBack(Socket socket, long already, long total)
		{
			string message = ((total > 0) ? (already * 100 / total).ToString() : "100");
			byte[] array = new byte[16];
			BitConverter.GetBytes(already).CopyTo(array, 0);
			BitConverter.GetBytes(total).CopyTo(array, 8);
			Send(socket, MqttHelper.BuildMqttCommand(15, 0, MqttHelper.BuildSegCommandByString(message), array).Content);
		}

		private void HandleMqttConnection(Socket socket, IPEndPoint endPoint, OperateResult<byte, byte[]> readMqtt, RSACryptoServiceProvider providerClient, RSACryptoServiceProvider providerServer)
		{
			if (!readMqtt.IsSuccess)
			{
				return;
			}
			byte[] array = readMqtt.Content2;
			if (providerClient != null)
			{
				try
				{
					array = providerServer.DecryptLargeData(array);
				}
				catch (Exception ex)
				{
					base.LogNet?.WriteError(ToString(), $"[{endPoint}] Decrypt the client's logon data exception！" + ex.Message);
					socket?.Close();
					return;
				}
			}
			OperateResult<int, MqttSession> operateResult = CheckMqttConnection(readMqtt.Content1, array, socket, endPoint);
			if (!operateResult.IsSuccess)
			{
				base.LogNet?.WriteInfo(ToString(), $"[{endPoint}] Check client login failure: " + operateResult.Message);
				socket?.Close();
				return;
			}
			if (operateResult.Content1 != 0)
			{
				Send(socket, MqttHelper.BuildMqttCommand(2, 0, null, new byte[2]
				{
					0,
					(byte)operateResult.Content1
				}).Content);
				socket?.Close();
				return;
			}
			if (providerClient == null)
			{
				Send(socket, MqttHelper.BuildMqttCommand(2, 0, null, new byte[2]).Content);
			}
			else
			{
				operateResult.Content2.AesCryptography = new AesCryptography(HslHelper.HslRandom.GetBytes(16).ToHexString());
				byte[] payLoad = providerClient.Encrypt(Encoding.UTF8.GetBytes(operateResult.Content2.AesCryptography.Key), fOAEP: false);
				Send(socket, MqttHelper.BuildMqttCommand(2, 0, new byte[2], payLoad).Content);
			}
			try
			{
				socket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, SocketReceiveCallback, operateResult.Content2);
				AddMqttSession(operateResult.Content2);
			}
			catch (Exception ex2)
			{
				base.LogNet?.WriteDebug(ToString(), "Client Online Exception : " + ex2.Message);
				return;
			}
			if (operateResult.Content2.Protocol == "MQTT")
			{
				this.OnClientConnected?.Invoke(operateResult.Content2);
			}
		}

		private OperateResult<int, MqttSession> CheckMqttConnection(byte mqttCode, byte[] content, Socket socket, IPEndPoint endPoint)
		{
			if (mqttCode >> 4 != 1)
			{
				return new OperateResult<int, MqttSession>("Client Send Faied, And Close!");
			}
			if (content.Length < 10)
			{
				return new OperateResult<int, MqttSession>("Receive Data Too Short:" + SoftBasic.ByteToHexString(content, ' '));
			}
			string @string = Encoding.ASCII.GetString(content, 2, 4);
			if (!(@string == "MQTT") && !(@string == "HUSL") && !(@string == "FILE"))
			{
				return new OperateResult<int, MqttSession>("Not Mqtt Client Connection");
			}
			try
			{
				int index = 10;
				string clientId = MqttHelper.ExtraMsgFromBytes(content, ref index);
				string willTopic = (((content[7] & 4) == 4) ? MqttHelper.ExtraMsgFromBytes(content, ref index) : string.Empty);
				string s = (((content[7] & 4) == 4) ? MqttHelper.ExtraMsgFromBytes(content, ref index) : string.Empty);
				string userName = (((content[7] & 0x80) == 128) ? MqttHelper.ExtraMsgFromBytes(content, ref index) : string.Empty);
				string password = (((content[7] & 0x40) == 64) ? MqttHelper.ExtraMsgFromBytes(content, ref index) : string.Empty);
				int num = content[8] * 256 + content[9];
				MqttSession mqttSession = new MqttSession(endPoint, @string)
				{
					MqttSocket = socket,
					ClientId = clientId,
					UserName = userName,
					WillTopic = willTopic,
					WillMessage = Encoding.UTF8.GetBytes(s)
				};
				if (@string == "MQTT")
				{
					mqttSession.DeveloperPermissions = false;
				}
				else if (string.Equals(mqttSession.UserName, "admin", StringComparison.OrdinalIgnoreCase))
				{
					mqttSession.DeveloperPermissions = true;
				}
				int value = ((this.ClientVerification != null) ? this.ClientVerification(mqttSession, clientId, userName, password) : 0);
				if (num > 0)
				{
					mqttSession.ActiveTimeSpan = TimeSpan.FromSeconds((double)num * 1.5);
				}
				return OperateResult.CreateSuccessResult(value, mqttSession);
			}
			catch (Exception ex)
			{
				return new OperateResult<int, MqttSession>("Client Online Exception : " + ex.Message);
			}
		}

		private async Task HandleWithReceiveMqtt(MqttSession mqttSession, OperateResult<byte, byte[]> readMqtt)
		{
			if (!readMqtt.IsSuccess)
			{
				RemoveAndCloseSession(mqttSession, readMqtt.Message);
				return;
			}
			byte code = readMqtt.Content1;
			byte[] data = readMqtt.Content2;
			try
			{
				if (code >> 4 == 14)
				{
					RemoveAndCloseSession(mqttSession, string.Empty);
					return;
				}
				mqttSession.MqttSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, SocketReceiveCallback, mqttSession);
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				RemoveAndCloseSession(mqttSession, "HandleWithReceiveMqtt exception:" + ex.Message);
				return;
			}
			mqttSession.ActiveTime = DateTime.Now;
			if (mqttSession.Protocol != "MQTT")
			{
				await DealWithPublish(mqttSession, code, data);
			}
			else if (code >> 4 == 3)
			{
				await DealWithPublish(mqttSession, code, data);
			}
			else if (code >> 4 != 4 && code >> 4 != 5)
			{
				if (code >> 4 == 6)
				{
					Send(mqttSession.MqttSocket, MqttHelper.BuildMqttCommand(7, 0, null, data).Content);
				}
				else if (code >> 4 == 8)
				{
					DealWithSubscribe(mqttSession, code, data);
				}
				else if (code >> 4 == 10)
				{
					DealWithUnSubscribe(mqttSession, code, data);
				}
				else if (code >> 4 == 12)
				{
					Send(mqttSession.MqttSocket, MqttHelper.BuildMqttCommand(13, 0, null, null).Content);
				}
			}
		}

		/// <inheritdoc />
		protected override void StartInitialization()
		{
		}

		/// <inheritdoc />
		protected override void CloseAction()
		{
			base.CloseAction();
			lock (sessionsLock)
			{
				for (int i = 0; i < mqttSessions.Count; i++)
				{
					mqttSessions[i].MqttSocket?.Close();
				}
				mqttSessions.Clear();
			}
		}

		private void ThreadTimerHeartCheck(object obj)
		{
			MqttSession[] array = null;
			lock (sessionsLock)
			{
				array = mqttSessions.ToArray();
			}
			if (array == null || array.Length == 0)
			{
				return;
			}
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].Protocol == "MQTT" && DateTime.Now - array[i].ActiveTime > array[i].ActiveTimeSpan)
				{
					RemoveAndCloseSession(array[i], "Thread Timer Heart Check failed:" + SoftBasic.GetTimeSpanDescription(DateTime.Now - array[i].ActiveTime));
				}
			}
		}

		private async Task DealWithPublish(MqttSession session, byte code, byte[] data)
		{
			OperateResult<MqttClientApplicationMessage> messageResult = MqttHelper.ParseMqttClientApplicationMessage(session, code, data);
			if (!messageResult.IsSuccess)
			{
				RemoveAndCloseSession(session, messageResult.Message);
				return;
			}
			MqttClientApplicationMessage mqttClientApplicationMessage = messageResult.Content;
			if (session.Protocol == "MQTT")
			{
				MqttQualityOfServiceLevel mqttQuality = mqttClientApplicationMessage.QualityOfServiceLevel;
				switch (mqttQuality)
				{
				case MqttQualityOfServiceLevel.AtLeastOnce:
					Send(session.MqttSocket, MqttHelper.BuildMqttCommand(4, 0, null, MqttHelper.BuildIntBytes(mqttClientApplicationMessage.MsgID)).Content);
					break;
				case MqttQualityOfServiceLevel.ExactlyOnce:
					Send(session.MqttSocket, MqttHelper.BuildMqttCommand(5, 0, null, MqttHelper.BuildIntBytes(mqttClientApplicationMessage.MsgID)).Content);
					break;
				}
				if (session.ForbidPublishTopic)
				{
					return;
				}
				this.OnClientApplicationMessageReceive?.Invoke(session, mqttClientApplicationMessage);
				if (mqttQuality != MqttQualityOfServiceLevel.OnlyTransfer && !mqttClientApplicationMessage.IsCancelPublish)
				{
					PublishTopicPayload(mqttClientApplicationMessage.Topic, mqttClientApplicationMessage.Payload, retain: false);
					if (mqttClientApplicationMessage.Retain)
					{
						RetainTopicPayload(mqttClientApplicationMessage.Topic, mqttClientApplicationMessage);
					}
					topicStatistics.StatisticsAdd(1L);
				}
			}
			else if (code >> 4 == 3)
			{
				string apiName = mqttClientApplicationMessage.Topic.Trim('/');
				MqttRpcApiInfo apiInformation = GetMqttRpcApiInfo(apiName);
				if (apiInformation == null)
				{
					this.OnClientApplicationMessageReceive?.Invoke(session, mqttClientApplicationMessage);
					return;
				}
				DateTime dateTime = DateTime.Now;
				OperateResult<string> result = await MqttHelper.HandleObjectMethod(session, mqttClientApplicationMessage, apiInformation);
				double timeSpend = Math.Round((DateTime.Now - dateTime).TotalSeconds, 5);
				apiInformation.CalledCountAddOne((long)(timeSpend * 100000.0));
				statisticsDict.StatisticsAdd(apiInformation.ApiTopic, 1L);
				base.LogNet?.WriteDebug(ToString(), $"{session} RPC: [{mqttClientApplicationMessage.Topic}] Spend:[{timeSpend * 1000.0:F2} ms] Count:[{apiInformation.CalledCount}] Return:[{result.IsSuccess}]");
				ReportOperateResult(session, result);
			}
			else if (code >> 4 == 8)
			{
				
					ReportOperateResult(session, OperateResult.CreateSuccessResult(JArray.FromObject(GetAllMqttRpcApiInfo()).ToString()));
				
			}
			else if (code >> 4 == 4)
			{
				
					PublishTopicPayload(session, "", HslProtocol.PackStringArrayToByte(GetAllRetainTopics()));
				
			}
			else if (code >> 4 == 6)
			{
				
					long[] logs = (string.IsNullOrEmpty(mqttClientApplicationMessage.Topic) ? LogStatistics.LogStat.GetStatisticsSnapshot() : LogStatistics.GetStatisticsSnapshot(mqttClientApplicationMessage.Topic));
					if (logs == null)
					{
						ReportOperateResult(session, new OperateResult<string>($"{session} RPC:{mqttClientApplicationMessage.Topic} has no data or not exist."));
					}
					else
					{
						ReportOperateResult(session, OperateResult.CreateSuccessResult(logs.ToArrayString()));
					}
				
				
			}
			else if (code >> 4 == 11)
			{
				
					ReportOperateResult(session, OperateResult.CreateSuccessResult(JArray.FromObject(OnlineSessions.Select((MqttSession m) => m.GetSessionInfo())).ToString()));
				
			}
			else
			{
				if (code >> 4 != 5)
				{
					return;
				}
				
					lock (keysLock)
					{
						if (retainKeys.ContainsKey(mqttClientApplicationMessage.Topic))
						{
							PublishTopicPayload(payload: Encoding.UTF8.GetBytes(retainKeys[mqttClientApplicationMessage.Topic].ToJsonString()), session: session, topic: mqttClientApplicationMessage.Topic);
							return;
						}
						ReportOperateResult(session, StringResources.Language.KeyIsNotExist);
					}
				
			}
		}

		/// <summary>
		/// 将消息进行驻留到内存词典，方便进行其他的功能操作。
		/// </summary>
		/// <param name="topic">消息的主题</param>
		/// <param name="payload">当前的数据负载</param>
		private void RetainTopicPayload(string topic, byte[] payload)
		{
			MqttClientApplicationMessage value = new MqttClientApplicationMessage
			{
				ClientId = "MqttServer",
				QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
				Retain = true,
				Topic = topic,
				UserName = "MqttServer",
				Payload = payload
			};
			lock (keysLock)
			{
				if (retainKeys.ContainsKey(topic))
				{
					retainKeys[topic] = value;
				}
				else
				{
					retainKeys.Add(topic, value);
				}
			}
		}

		/// <summary>
		/// 将消息进行驻留到内存词典，方便进行其他的功能操作。
		/// </summary>
		/// <param name="topic">消息的主题</param>
		/// <param name="message">当前的Mqtt消息</param>
		private void RetainTopicPayload(string topic, MqttClientApplicationMessage message)
		{
			lock (keysLock)
			{
				if (retainKeys.ContainsKey(topic))
				{
					retainKeys[topic] = message;
				}
				else
				{
					retainKeys.Add(topic, message);
				}
			}
		}

		private void DealWithSubscribe(MqttSession session, byte code, byte[] data)
		{
			int num = 0;
			int index = 0;
			num = MqttHelper.ExtraIntFromBytes(data, ref index);
			List<string> list = new List<string>();
			List<byte> list2 = new List<byte>();
			try
			{
				while (index < data.Length - 1)
				{
					MqttHelper.ExtraSubscribeMsgFromBytes(data, ref index, list, list2);
				}
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteError(ToString(), $"{session} DealWithSubscribe exception: " + ex.Message + " Source: " + data.ToHexString(' '));
				return;
			}
			Send(session.MqttSocket, MqttHelper.BuildMqttCommand(9, 0, MqttHelper.BuildIntBytes(num), list2.ToArray()).Content);
			lock (keysLock)
			{
				if (topicWildcard)
				{
					foreach (KeyValuePair<string, MqttClientApplicationMessage> retainKey in retainKeys)
					{
						for (int i = 0; i < list.Count; i++)
						{
							if (MqttHelper.CheckMqttTopicWildcards(retainKey.Key, list[i]))
							{
								Send(session.MqttSocket, MqttHelper.BuildPublishMqttCommand(retainKey.Key, retainKey.Value.Payload, retain: true, session.IsAesCryptography ? session.AesCryptography : null).Content);
							}
						}
					}
				}
				else
				{
					for (int j = 0; j < list.Count; j++)
					{
						if (retainKeys.ContainsKey(list[j]))
						{
							Send(session.MqttSocket, MqttHelper.BuildPublishMqttCommand(list[j], retainKeys[list[j]].Payload, retain: true, session.IsAesCryptography ? session.AesCryptography : null).Content);
						}
					}
				}
			}
			session.AddSubscribe(list.ToArray());
			base.LogNet?.WriteDebug(ToString(), session.ToString() + " Subscribe: " + list.ToArray().ToArrayString());
		}

		private void DealWithUnSubscribe(MqttSession session, byte code, byte[] data)
		{
			int num = 0;
			int index = 0;
			num = MqttHelper.ExtraIntFromBytes(data, ref index);
			List<string> list = new List<string>();
			while (index < data.Length)
			{
				list.Add(MqttHelper.ExtraMsgFromBytes(data, ref index));
			}
			Send(session.MqttSocket, MqttHelper.BuildMqttCommand(11, 0, null, MqttHelper.BuildIntBytes(num)).Content);
			session.RemoveSubscribe(list.ToArray());
			base.LogNet?.WriteDebug(ToString(), session.ToString() + " UnSubscribe: " + list.ToArray().ToArrayString());
		}

		/// <summary>
		/// 向指定的客户端发送主题及负载数据<br />
		/// Sends the topic and payload data to the specified client
		/// </summary>
		/// <param name="session">会话内容</param>
		/// <param name="topic">主题</param>
		/// <param name="payload">消息内容</param>
		public void PublishTopicPayload(MqttSession session, string topic, byte[] payload)
		{
			OperateResult operateResult = Send(session.MqttSocket, MqttHelper.BuildPublishMqttCommand(topic, payload, retain: false, session.IsAesCryptography ? session.AesCryptography : null).Content);
			if (!operateResult.IsSuccess)
			{
				base.LogNet?.WriteError(ToString(), $"{session} PublishTopicPayload Failed:" + operateResult.Message);
			}
			topicStatistics.StatisticsAdd(1L);
		}

		/// <summary>
		/// 使用指定的规则向客户端发布主题及负载数据，可以根据会话的登录用户名，客户端ID信息进行筛选，例如只发布用户名admin的账户：( session ) =&gt; session.UserName == "admin"<br />
		/// Use the specified rules to publish topic and load data to the client, which can be filtered according to the session login user name and client ID information. 
		/// For example, only the account with the user name admin is published: ( session ) =&gt; session.UserName == "admin"
		/// </summary>
		/// <param name="topic">主题</param>
		/// <param name="payload">消息内容</param>
		/// <param name="retain">是否在服务器驻留</param>
		/// <param name="check">会话的检查委托</param>
		public void PublishTopicPayload(string topic, byte[] payload, bool retain, Func<MqttSession, bool> check)
		{
			lock (sessionsLock)
			{
				byte[] array = null;
				for (int i = 0; i < mqttSessions.Count; i++)
				{
					MqttSession mqttSession = mqttSessions[i];
					byte[] array2 = null;
					if (!(mqttSession.Protocol == "MQTT") || !check(mqttSession))
					{
						continue;
					}
					if (mqttSession.IsAesCryptography)
					{
						if (array2 == null)
						{
							array2 = MqttHelper.BuildPublishMqttCommand(topic, payload, retain: false, mqttSession.AesCryptography).Content;
						}
					}
					else if (array == null)
					{
						array = MqttHelper.BuildPublishMqttCommand(topic, payload).Content;
					}
					OperateResult operateResult = Send(mqttSession.MqttSocket, mqttSession.IsAesCryptography ? array2 : array);
					if (!operateResult.IsSuccess)
					{
						base.LogNet?.WriteError(ToString(), $"{mqttSession} PublishTopicPayload Failed:" + operateResult.Message);
					}
				}
			}
			if (retain)
			{
				RetainTopicPayload(topic, payload);
			}
			topicStatistics.StatisticsAdd(1L);
		}

		/// <summary>
		/// 从服务器向订阅了指定的主题的客户端发送消息，默认消息不驻留<br />
		/// Sends a message from the server to a client that subscribes to the specified topic; the default message does not retain
		/// </summary>
		/// <param name="topic">主题</param>
		/// <param name="payload">消息内容</param>
		/// <param name="retain">指示消息是否驻留</param>
		public void PublishTopicPayload(string topic, byte[] payload, bool retain = true)
		{
			PublishTopicPayload(topic, payload, retain, (MqttSession session) => session.IsClientSubscribe(topic, topicWildcard));
		}

		/// <summary>
		/// 向所有的客户端强制发送主题及负载数据，默认消息不驻留<br />
		/// Send subject and payload data to all clients compulsively, and the default message does not retain
		/// </summary>
		/// <param name="topic">主题</param>
		/// <param name="payload">消息内容</param>
		/// <param name="retain">指示消息是否驻留</param>
		public void PublishAllClientTopicPayload(string topic, byte[] payload, bool retain = false)
		{
			PublishTopicPayload(topic, payload, retain, (MqttSession session) => true);
		}

		/// <summary>
		/// 向指定的客户端ID强制发送消息，默认消息不驻留<br />
		/// Forces a message to the specified client ID, and the default message does not retain
		/// </summary>
		/// <param name="clientId">指定的客户端ID信息</param>
		/// <param name="topic">主题</param>
		/// <param name="payload">消息内容</param>
		/// <param name="retain">指示消息是否驻留</param>
		public void PublishTopicPayload(string clientId, string topic, byte[] payload, bool retain = false)
		{
			PublishTopicPayload(topic, payload, retain, (MqttSession session) => session.ClientId == clientId);
		}

		/// <summary>
		/// 向客户端发布一个进度报告的信息，仅用于同步网络的时候才支持进度报告，将进度及消息发送给客户端，比如你的服务器需要分成5个部分完成，可以按照百分比提示给客户端当前服务器发生了什么<br />
		/// Publish the information of a progress report to the client. The progress report is only supported when the network is synchronized. 
		/// The progress and the message are sent to the client. For example, your server needs to be divided into 5 parts to complete. 
		/// You can prompt the client according to the percentage. What happened to the server
		/// </summary>
		/// <param name="session">当前的网络会话</param>
		/// <param name="topic">回发客户端的关键数据，可以是百分比字符串，甚至是自定义的任意功能</param>
		/// <param name="payload">数据消息</param>
		public void ReportProgress(MqttSession session, string topic, string payload)
		{
			if (session.Protocol == "HUSL")
			{
				payload = payload ?? string.Empty;
				OperateResult operateResult = Send(session.MqttSocket, MqttHelper.BuildMqttCommand(15, 0, MqttHelper.BuildSegCommandByString(topic), Encoding.UTF8.GetBytes(payload)).Content);
				if (!operateResult.IsSuccess)
				{
					base.LogNet?.WriteError(ToString(), $"{session} PublishTopicPayload Failed:" + operateResult.Message);
				}
				return;
			}
			throw new Exception("ReportProgress only support sync communication");
		}

		/// <summary>
		/// 向客户端发布一个失败的操作信息，仅用于同步网络的时候反馈失败结果，将错误的信息反馈回客户端，客户端就知道服务器发生了什么，为什么反馈失败。<br />
		/// Publish a failed operation information to the client, which is only used to feed back the failure result when synchronizing the network. 
		/// If the error information is fed back to the client, the client will know what happened to the server and why the feedback failed.
		/// </summary>
		/// <param name="session">当前的网络会话</param>
		/// <param name="message">错误的消息文本信息</param>
		public void ReportOperateResult(MqttSession session, string message)
		{
			ReportOperateResult(session, new OperateResult<string>(message));
		}

		/// <summary>
		/// 向客户端发布一个操作结果的信息，仅用于同步网络的时候反馈操作结果，该操作可能成功，可能失败，客户端就知道服务器发生了什么，以及结果如何。<br />
		/// Publish an operation result information to the client, which is only used to feed back the operation result when synchronizing the network. 
		/// The operation may succeed or fail, and the client knows what happened to the server and the result.
		/// </summary>
		/// <param name="session">当前的网络会话</param>
		/// <param name="result">结果对象内容</param>
		public void ReportOperateResult(MqttSession session, OperateResult<string> result)
		{
			if (session.Protocol == "HUSL")
			{
				if (result.IsSuccess)
				{
					byte[] payload = (string.IsNullOrEmpty(result.Content) ? new byte[0] : Encoding.UTF8.GetBytes(result.Content));
					PublishTopicPayload(session, result.ErrorCode.ToString(), payload);
					return;
				}
				OperateResult operateResult = Send(session.MqttSocket, MqttHelper.BuildMqttCommand(0, 0, MqttHelper.BuildSegCommandByString(result.ErrorCode.ToString()), string.IsNullOrEmpty(result.Message) ? new byte[0] : Encoding.UTF8.GetBytes(result.Message), session.IsAesCryptography ? session.AesCryptography : null).Content);
				if (!operateResult.IsSuccess)
				{
					base.LogNet?.WriteError(ToString(), $"{session} PublishTopicPayload Failed:" + operateResult.Message);
				}
				return;
			}
			throw new Exception("Report Result Message only support sync communication, client is MqttSyncClient");
		}

		/// <summary>
		/// 使用指定的对象来返回网络的API接口，前提是传入的数据为json参数，返回的数据为 <c>OperateResult&lt;string&gt;</c> 数据，详细参照说明<br />
		/// Use the specified object to return the API interface of the network, 
		/// provided that the incoming data is json parameters and the returned data is <c>OperateResult&lt;string&gt;</c> data, 
		/// please refer to the description for details
		/// </summary>
		/// <param name="session">当前的会话内容</param>
		/// <param name="message">客户端发送的消息，其中的payload将会解析为一个json字符串，然后提取参数信息。</param>
		/// <param name="apiObject">当前的对象的内容信息</param>
		public async Task ReportObjectApiMethod(MqttSession session, MqttClientApplicationMessage message, object apiObject)
		{
			if (session.Protocol == "HUSL")
			{
				ReportOperateResult(session, await MqttHelper.HandleObjectMethod(session, message, apiObject));
				return;
			}
			throw new Exception("Report Result Message only support sync communication, client is MqttSyncClient");
		}

		private void MqttRpcAdd(string apiTopic, MqttRpcApiInfo apiInfo)
		{
			if (apiTopicServiceDict.ContainsKey(apiTopic))
			{
				apiTopicServiceDict[apiTopic] = apiInfo;
			}
			else
			{
				apiTopicServiceDict.Add(apiTopic, apiInfo);
			}
		}

		private void MqttRpcRemove(string apiTopic)
		{
			if (apiTopicServiceDict.ContainsKey(apiTopic))
			{
				apiTopicServiceDict.Remove(apiTopic);
			}
		}

		private MqttRpcApiInfo GetMqttRpcApiInfo(string apiTopic)
		{
			MqttRpcApiInfo result = null;
			lock (rpcApiLock)
			{
				if (apiTopicServiceDict.ContainsKey(apiTopic))
				{
					result = apiTopicServiceDict[apiTopic];
				}
			}
			return result;
		}

		/// <summary>
		/// 获取当前所有注册的RPC接口信息，将返回一个数据列表。<br />
		/// Get all currently registered RPC interface information, and a data list will be returned.
		/// </summary>
		/// <returns>信息列表</returns>
		public MqttRpcApiInfo[] GetAllMqttRpcApiInfo()
		{
			MqttRpcApiInfo[] result = null;
			lock (rpcApiLock)
			{
				result = apiTopicServiceDict.Values.ToArray();
			}
			return result;
		}

		/// <summary>
		/// 注册一个RPC的服务接口，可以指定当前的控制器名称，以及提供RPC服务的原始对象，指定统一的权限控制。<br />
		/// Register an RPC service interface, you can specify the current controller name, and the original object that provides the RPC service, Specify unified access control
		/// </summary>
		/// <remarks>
		/// 如果设置了<paramref name="permissionAttribute" />的参数信息，那么请确保调用时获取了企业商业授权的权限，否则，只能使用24小时，然后因为权限问题报错。
		/// </remarks>
		/// <param name="api">前置的接口信息，可以理解为MVC模式的控制器</param>
		/// <param name="obj">原始对象信息，也可以是类型对象本身</param>
		/// <param name="permissionAttribute">统一的权限访问配置，将会覆盖单个方法的权限控制。</param>
		public void RegisterMqttRpcApi(string api, object obj, HslMqttPermissionAttribute permissionAttribute)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(api, obj, permissionAttribute))
				{
					MqttRpcAdd(item.ApiTopic, item);
				}
			}
		}

		/// <summary>
		/// 注册一个RPC的服务接口，可以指定当前的控制器名称，以及提供RPC服务的原始对象<br />
		/// Register an RPC service interface, you can specify the current controller name, and the original object that provides the RPC service
		/// </summary>
		/// <param name="api">前置的接口信息，可以理解为MVC模式的控制器</param>
		/// <param name="obj">原始对象信息，也可以是类型对象本身</param>
		public void RegisterMqttRpcApi(string api, object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(api, obj))
				{
					MqttRpcAdd(item.ApiTopic, item);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttServer.RegisterMqttRpcApi(System.String,System.Object)" />
		public void RegisterMqttRpcApi(object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(obj))
				{
					MqttRpcAdd(item.ApiTopic, item);
				}
			}
		}

		/// <summary>
		/// 卸载一个已经注册的RPC接口，想要卸载指定的接口，此处就需要传入注册时一样的参数<br />
		/// Uninstall a registered RPC interface. If you want to uninstall the specified interface, you need to pass in the same parameters as the registration
		/// </summary>
		/// <param name="api">前置的接口信息，可以理解为MVC模式的控制器</param>
		/// <param name="obj">原始对象信息，也可以是类型对象本身</param>
		public void UnRegisterMqttRpcApi(string api, object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(api, obj))
				{
					MqttRpcRemove(item.ApiTopic);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttServer.UnRegisterMqttRpcApi(System.String,System.Object)" />
		public void UnRegisterMqttRpcApi(object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(obj))
				{
					MqttRpcRemove(item.ApiTopic);
				}
			}
		}

		/// <summary>
		/// 启动文件服务功能，协议头为FILE，需要指定服务器存储的文件路径<br />
		/// Start the file service function, the protocol header is FILE, you need to specify the file path stored by the server
		/// </summary>
		/// <param name="filePath">文件的存储路径</param>
		public void UseFileServer(string filePath)
		{
			filesDirectoryPath = filePath;
			fileServerEnabled = true;
			CheckFolderAndCreate();
		}

		/// <summary>
		/// 关闭文件服务功能
		/// </summary>
		public void CloseFileServer()
		{
			fileServerEnabled = false;
		}

		/// <summary>
		/// 获取当前的针对文件夹的文件管理容器的数量<br />
		/// Get the current number of file management containers for the folder
		/// </summary>
		[HslMqttApi(Description = "Get the current number of file management containers for the folder")]
		public int GroupFileContainerCount()
		{
			return m_dictionary_group_marks.Count;
		}

		/// <summary>
		/// 获取当前实时的文件上传下载的监控信息，操作的客户端信息，文件分类，文件名，上传或下载的速度等<br />
		/// Obtain current real-time file upload and download monitoring information, operating client information, file classification, file name, upload or download speed, etc.
		/// </summary>
		/// <returns>文件的监控信息</returns>
		[HslMqttApi(Description = "Obtain current real-time file upload and download monitoring information, operating client information, file classification, file name, upload or download speed, etc.")]
		public MqttFileMonitorItem[] GetMonitorItemsSnapShoot()
		{
			return fileMonitor.GetMonitorItemsSnapShoot();
		}

		private bool CheckPathAndFilenameLegal(string input)
		{
			return Regex.IsMatch(input, "[:?*/\\<>|\"]");
		}

		private async Task HandleFileMessageAsync(MqttSession session)
		{
			OperateResult<byte, byte[]> receiveGroupInfo = await ReceiveMqttMessageAsync(session.MqttSocket, 60000);
			if (!receiveGroupInfo.IsSuccess)
			{
				return;
			}
			string[] groupInfo = HslProtocol.UnPackStringArrayFromByte(receiveGroupInfo.Content2);
			OperateResult<byte, byte[]> receiveFileNames = await ReceiveMqttMessageAsync(session.MqttSocket, 60000);
			if (!receiveFileNames.IsSuccess)
			{
				return;
			}
			string[] fileNames = HslProtocol.UnPackStringArrayFromByte(receiveFileNames.Content2);
			for (int i = 0; i < groupInfo.Length; i++)
			{
				if (CheckPathAndFilenameLegal(groupInfo[i]))
				{
					Send(session.MqttSocket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes("Path Invalid, not include '\\/:*?\"<>|'")).Content);
					RemoveAndCloseSession(session, "CheckPathAndFilenameLegal:" + groupInfo[i]);
					return;
				}
			}
			for (int j = 0; j < fileNames.Length; j++)
			{
				if (receiveFileNames.Content1 != 111)
				{
					if (CheckPathAndFilenameLegal(fileNames[j]))
					{
						Send(session.MqttSocket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes("FileName Invalid, not include '\\/:*?\"<>|'")).Content);
						RemoveAndCloseSession(session, "CheckPathAndFilenameLegal:" + fileNames[j]);
						return;
					}
				}
				else if (Regex.IsMatch(fileNames[j], "[:?*<>|\"]"))
				{
					Send(session.MqttSocket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes("FileName Invalid, not include ':*?\"<>|'")).Content);
					RemoveAndCloseSession(session, "CheckPathAndFilenameLegal:" + fileNames[j]);
					return;
				}
			}
			OperateResult opLegal = this.FileOperateVerification?.Invoke(session, receiveFileNames.Content1, groupInfo, fileNames);
			if (opLegal == null)
			{
				opLegal = OperateResult.CreateSuccessResult();
			}
			OperateResult sendLegal = await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand((byte)(opLegal.IsSuccess ? 100 : 0), null, HslHelper.GetUTF8Bytes(opLegal.Message)).Content);
			if (!opLegal.IsSuccess)
			{
				RemoveAndCloseSession(session, "FileOperateVerification:" + opLegal.Message);
				return;
			}
			if (!sendLegal.IsSuccess)
			{
				RemoveAndCloseSession(session, "FileOperate SendLegal:" + sendLegal.Message);
				return;
			}
			string relativeName2 = GetRelativeFileName(groupInfo, (fileNames != null && fileNames.Length != 0) ? fileNames[0] : string.Empty);
			if (receiveFileNames.Content1 == 101)
			{
				string fileName2 = fileNames[0];
				string guidName = TransformFactFileName(groupInfo, fileName2);
				FileMarkId fileMarkId = GetFileMarksFromDictionaryWithFileName(guidName);
				fileMarkId.EnterReadOperator();
				DateTime dateTimeStart5 = DateTime.Now;
				MqttFileMonitorItem monitorItem = new MqttFileMonitorItem
				{
					EndPoint = session.EndPoint,
					ClientId = session.ClientId,
					UserName = session.UserName,
					FileName = fileName2,
					MappingName = guidName,
					Operate = "Download",
					Groups = HslHelper.PathCombine(groupInfo)
				};
				fileMonitor.Add(monitorItem);
				OperateResult send = await SendMqttFileAsync(session.MqttSocket, ReturnAbsoluteFileName(groupInfo, guidName), fileName2, "", monitorItem.UpdateProgress, session.IsAesCryptography ? session.AesCryptography : null);
				fileMarkId.LeaveReadOperator();
				fileMonitor.Remove(monitorItem.UniqueId);
				this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
				{
					Groups = HslHelper.PathCombine(groupInfo),
					FileNames = fileNames,
					MappingNames = new string[1] { guidName },
					Operate = "Download",
					TimeCost = DateTime.Now - dateTimeStart5
				});
				if (!send.IsSuccess)
				{
					base.LogNet?.WriteError(ToString(), $"{session} {StringResources.Language.FileDownloadFailed}[{send.Message}]:{relativeName2} Name:{session.UserName}" + " Spend:" + SoftBasic.GetTimeSpanDescription(DateTime.Now - dateTimeStart5));
				}
				else
				{
					base.LogNet?.WriteInfo(ToString(), $"{session} {StringResources.Language.FileDownloadSuccess}:{relativeName2} Spend:{SoftBasic.GetTimeSpanDescription(DateTime.Now - dateTimeStart5)}");
				}
			}
			else if (receiveFileNames.Content1 == 102)
			{
				string fileName3 = fileNames[0];
				string fullFileName5 = ReturnAbsoluteFileName(groupInfo, fileName3);
				CheckFolderAndCreate();
				FileInfo info5 = new FileInfo(fullFileName5);
				try
				{
					if (!Directory.Exists(info5.DirectoryName))
					{
						Directory.CreateDirectory(info5.DirectoryName);
					}
				}
				catch (Exception ex2)
				{
					Exception ex = ex2;
					base.LogNet?.WriteException(ToString(), StringResources.Language.FilePathCreateFailed + fullFileName5, ex);
					return;
				}
				string guidName2 = SoftBasic.GetUniqueStringByGuidAndRandom();
				DateTime dateTimeStart6 = DateTime.Now;
				MqttFileMonitorItem monitorItem2 = new MqttFileMonitorItem
				{
					EndPoint = session.EndPoint,
					ClientId = session.ClientId,
					UserName = session.UserName,
					FileName = fileName3,
					MappingName = guidName2,
					Operate = "Upload",
					Groups = HslHelper.PathCombine(groupInfo)
				};
				fileMonitor.Add(monitorItem2);
				OperateResult<FileBaseInfo> receive = await ReceiveMqttFileAndUpdateGroupAsync(session, info5, guidName2, monitorItem2.UpdateProgress);
				fileMonitor.Remove(monitorItem2.UniqueId);
				if (receive.IsSuccess)
				{
					this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
					{
						Groups = HslHelper.PathCombine(groupInfo),
						FileNames = fileNames,
						MappingNames = new string[1] { guidName2 },
						Operate = "Upload",
						TimeCost = DateTime.Now - dateTimeStart6
					});
					base.LogNet?.WriteInfo(ToString(), $"{session} {StringResources.Language.FileUploadSuccess}:{relativeName2} Spend:{SoftBasic.GetTimeSpanDescription(DateTime.Now - dateTimeStart6)}");
				}
				else
				{
					base.LogNet?.WriteError(ToString(), $"{session} {StringResources.Language.FileUploadFailed}[{receive.Message}]:{relativeName2} Spend:{SoftBasic.GetTimeSpanDescription(DateTime.Now - dateTimeStart6)}");
				}
			}
			else if (receiveFileNames.Content1 == 103)
			{
				DateTime dateTimeStart4 = DateTime.Now;
				List<string> mappings = new List<string>();
				string[] array = fileNames;
				foreach (string item in array)
				{
					string fullFileName4 = ReturnAbsoluteFileName(groupInfo, item);
					FileInfo info4 = new FileInfo(fullFileName4);
					GroupFileContainer fileManagment7 = GetGroupFromFilePath(info4.DirectoryName);
					string guid = fileManagment7.DeleteFile(info4.Name);
					DeleteExsistingFile(info4.DirectoryName, guid);
					mappings.Add(guid);
					relativeName2 = GetRelativeFileName(groupInfo, item);
					base.LogNet?.WriteInfo(ToString(), $"{session} {StringResources.Language.FileDeleteSuccess}:{relativeName2}");
				}
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(103, null, null).Content);
				this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
				{
					Groups = HslHelper.PathCombine(groupInfo),
					FileNames = fileNames,
					MappingNames = mappings.ToArray(),
					Operate = "Delete",
					TimeCost = DateTime.Now - dateTimeStart4
				});
			}
			else if (receiveFileNames.Content1 == 110)
			{
				DateTime dateTimeStart3 = DateTime.Now;
				string fullFileName3 = ReturnAbsoluteFileName(groupInfo, "123.txt");
				FileInfo info3 = new FileInfo(fullFileName3);
				GroupFileContainer fileManagment6 = GetGroupFromFilePath(info3.DirectoryName);
				List<string> file_names2;
				List<string> guid_names2 = fileManagment6.ClearAllFiles(out file_names2);
				DeleteExsistingFile(info3.DirectoryName, guid_names2);
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(110, null, null).Content);
				this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
				{
					Groups = HslHelper.PathCombine(groupInfo),
					FileNames = file_names2.ToArray(),
					MappingNames = guid_names2.ToArray(),
					Operate = "ClearFolder",
					TimeCost = DateTime.Now - dateTimeStart3
				});
				base.LogNet?.WriteInfo(ToString(), session.ToString() + "ClearFolder : " + relativeName2);
			}
			else if (receiveFileNames.Content1 == 104)
			{
				DateTime dateTimeStart2 = DateTime.Now;
				string fullFileName2 = ReturnAbsoluteFileName(groupInfo, "123.txt");
				FileInfo info2 = new FileInfo(fullFileName2);
				GroupFileContainer fileManagment5 = GetGroupFromFilePath(info2.DirectoryName);
				List<string> file_names;
				List<string> guid_names = fileManagment5.GetAllFiles(out file_names);
				DeleteGroupFileContainer(info2.DirectoryName);
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(104, null, null).Content);
				this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
				{
					Groups = HslHelper.PathCombine(groupInfo),
					FileNames = file_names.ToArray(),
					MappingNames = guid_names.ToArray(),
					Operate = "DeleteFolder",
					TimeCost = DateTime.Now - dateTimeStart2
				});
				base.LogNet?.WriteInfo(ToString(), session.ToString() + " FolderDelete : " + relativeName2);
			}
			else if (receiveFileNames.Content1 == 111)
			{
				DateTime dateTimeStart = DateTime.Now;
				string fullFileName = ReturnAbsoluteFileName(groupInfo, "123.txt");
				FileInfo info = new FileInfo(fullFileName);
				OperateResult rename = RenameGroupFileContainer(newPath: Path.Combine(path2: HslHelper.PathCombine(fileNames[0].Split('/', '\\')), path1: filesDirectoryPath), filePath: info.DirectoryName);
				if (!rename.IsSuccess)
				{
					base.LogNet?.WriteInfo(ToString(), session.ToString() + " RenameFolder : " + HslHelper.PathCombine(groupInfo) + " -> " + fileNames[0] + " failed: " + rename.Message);
					Send(session.MqttSocket, MqttHelper.BuildMqttCommand(0, null, Encoding.UTF8.GetBytes("File path already exist")).Content);
				}
				else
				{
					await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(111, null, null).Content);
					this.OnFileChangedEvent?.Invoke(session, new MqttFileOperateInfo
					{
						Groups = HslHelper.PathCombine(groupInfo),
						FileNames = null,
						Operate = "RenameFolder",
						TimeCost = DateTime.Now - dateTimeStart
					});
					base.LogNet?.WriteInfo(ToString(), session.ToString() + " RenameFolder : " + HslHelper.PathCombine(groupInfo) + " -> " + fileNames[0]);
				}
			}
			else if (receiveFileNames.Content1 == 105)
			{
				GroupFileContainer fileManagment4 = GetGroupFromFilePath(ReturnAbsoluteFilePath(groupInfo));
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(104, null, Encoding.UTF8.GetBytes(fileManagment4.JsonArrayContent)).Content);
			}
			else if (receiveFileNames.Content1 == 108)
			{
				GroupFileContainer fileManagment3 = GetGroupFromFilePath(ReturnAbsoluteFilePath(groupInfo));
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(108, null, Encoding.UTF8.GetBytes(fileManagment3.GetGroupFileInfo(withLastFileInfo: true).ToJsonString())).Content);
			}
			else if (receiveFileNames.Content1 == 109)
			{
				bool withLastFileInfo = fileNames != null && fileNames.Length != 0 && fileNames[0] == "1";
				List<GroupFileInfo> folders2 = new List<GroupFileInfo>();
				string[] directories = GetDirectories(groupInfo);
				foreach (string l in directories)
				{
					DirectoryInfo directory2 = new DirectoryInfo(l);
					GroupFileContainer fileManagment2 = GetGroupFromFilePath(ReturnAbsoluteFilePath(new List<string>(groupInfo) { directory2.Name }.ToArray()));
					GroupFileInfo groupFileInfo = fileManagment2.GetGroupFileInfo(withLastFileInfo);
					groupFileInfo.PathName = directory2.Name;
					folders2.Add(groupFileInfo);
				}
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(108, null, Encoding.UTF8.GetBytes(folders2.ToJsonString())).Content);
			}
			else if (receiveFileNames.Content1 == 106)
			{
				List<string> folders = new List<string>();
				string[] directories2 = GetDirectories(groupInfo);
				foreach (string k in directories2)
				{
					DirectoryInfo directory = new DirectoryInfo(k);
					folders.Add(directory.Name);
				}
				JArray jArray = JArray.FromObject(folders.ToArray());
				await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(106, null, Encoding.UTF8.GetBytes(jArray.ToString())).Content);
			}
			else if (receiveFileNames.Content1 == 107)
			{
				string fileName = fileNames[0];
				string fullPath = ReturnAbsoluteFilePath(groupInfo);
				GroupFileContainer fileManagment = GetGroupFromFilePath(fullPath);
				await SendAsync(data: MqttHelper.BuildMqttCommand((byte)(fileManagment.FileExists(fileName) ? 1 : 0), null, Encoding.UTF8.GetBytes(StringResources.Language.FileNotExist)).Content, socket: session.MqttSocket);
			}
		}

		/// <summary>
		/// 从套接字接收文件并保存，更新文件列表
		/// </summary>
		/// <param name="session">当前的会话信息</param>
		/// <param name="info">保存的信息</param>
		/// <param name="guidName">映射的文件名称</param>
		/// <param name="reportProgress">当前的委托信息</param>
		/// <returns>是否成功的结果对象</returns>
		private async Task<OperateResult<FileBaseInfo>> ReceiveMqttFileAndUpdateGroupAsync(MqttSession session, FileInfo info, string guidName, Action<long, long> reportProgress)
		{
			string fileName = Path.Combine(info.DirectoryName, guidName);
			OperateResult<FileBaseInfo> receive = await ReceiveMqttFileAsync(session.MqttSocket, fileName, reportProgress, session.IsAesCryptography ? session.AesCryptography : null);
			if (!receive.IsSuccess)
			{
				DeleteFileByName(fileName);
				return receive;
			}
			GroupFileContainer fileManagment = GetGroupFromFilePath(info.DirectoryName);
			DeleteExsistingFile(fileName: fileManagment.UpdateFileMappingName(info.Name, receive.Content.Size, guidName, session.UserName, receive.Content.Tag), path: info.DirectoryName);
			OperateResult sendBack = await SendAsync(session.MqttSocket, MqttHelper.BuildMqttCommand(100, null, Encoding.UTF8.GetBytes(StringResources.Language.SuccessText)).Content);
			if (!sendBack.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(sendBack);
			}
			return OperateResult.CreateSuccessResult(receive.Content);
		}

		/// <summary>
		/// 返回相对路径的名称
		/// </summary>
		/// <param name="groups">文件的分类路径信息</param>
		/// <param name="fileName">文件名</param>
		/// <returns>是否成功的结果对象</returns>
		private string GetRelativeFileName(string[] groups, string fileName)
		{
			string path = "";
			for (int i = 0; i < groups.Length; i++)
			{
				if (!string.IsNullOrEmpty(groups[i]))
				{
					path = Path.Combine(path, groups[i]);
				}
			}
			return Path.Combine(path, fileName);
		}

		/// <summary>
		/// 返回服务器的绝对路径，包含根目录的信息  [Root Dir][A][B][C]... 信息
		/// </summary>
		/// <param name="groups">文件的路径分类信息</param>
		/// <returns>是否成功的结果对象</returns>
		private string ReturnAbsoluteFilePath(string[] groups)
		{
			return Path.Combine(filesDirectoryPath, Path.Combine(groups));
		}

		/// <summary>
		/// 返回服务器的绝对路径，包含根目录的信息  [Root Dir][A][B][C]...[FileName] 信息
		/// </summary>
		/// <param name="groups">路径分类信息</param>
		/// <param name="fileName">文件名</param>
		/// <returns>是否成功的结果对象</returns>
		protected string ReturnAbsoluteFileName(string[] groups, string fileName)
		{
			return Path.Combine(ReturnAbsoluteFilePath(groups), fileName);
		}

		/// <summary>
		/// 根据文件的显示名称转化为真实存储的名称，例如 123.txt 获取到在文件服务器里映射的文件名称，例如返回 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="groups">文件的分类信息</param>
		/// <param name="fileName">文件显示名称</param>
		/// <returns>是否成功的结果对象</returns>
		private string TransformFactFileName(string[] groups, string fileName)
		{
			string filePath = ReturnAbsoluteFilePath(groups);
			GroupFileContainer groupFromFilePath = GetGroupFromFilePath(filePath);
			return groupFromFilePath.GetCurrentFileMappingName(fileName);
		}

		/// <summary>
		/// 获取当前目录的文件列表管理容器，如果没有会自动创建，通过该容器可以实现对当前目录的文件进行访问<br />
		/// Get the file list management container of the current directory. If not, it will be created automatically. 
		/// Through this container, you can access files in the current directory.
		/// </summary>
		/// <param name="filePath">路径信息</param>
		/// <param name="create">是否创建容器，当没有发现该路径的容器的情况</param>
		/// <returns>文件管理容器信息</returns>
		private GroupFileContainer GetGroupFromFilePath(string filePath, bool create = true)
		{
			GroupFileContainer groupFileContainer = null;
			filePath = filePath.ToUpper();
			group_marks_lock.Enter();
			if (m_dictionary_group_marks.ContainsKey(filePath))
			{
				groupFileContainer = m_dictionary_group_marks[filePath];
			}
			else if (create)
			{
				groupFileContainer = new GroupFileContainer(base.LogNet, filePath);
				m_dictionary_group_marks.Add(filePath, groupFileContainer);
			}
			group_marks_lock.Leave();
			return groupFileContainer;
		}

		/// <summary>
		/// 彻底删除当前的目录信息，整个目录及其所有的子文件夹，子文件信息<br />
		/// Completely delete the current directory information, the entire directory and all its subfolders, subfile information
		/// </summary>
		/// <param name="filePath">文件路径信息</param>
		private void DeleteGroupFileContainer(string filePath)
		{
			filePath = filePath.ToUpper();
			group_marks_lock.Enter();
			if (m_dictionary_group_marks.ContainsKey(filePath))
			{
				GroupFileContainer groupFileContainer = m_dictionary_group_marks[filePath];
				groupFileContainer.DeleteFolder();
				m_dictionary_group_marks.Remove(filePath);
			}
			group_marks_lock.Leave();
		}

		/// <summary>
		/// 重命名当前的目录文件信息，如果修改后的名称还是存在，则返回错误信息
		/// </summary>
		/// <param name="filePath">旧的路径信息</param>
		/// <param name="newPath">新的路径信息</param>
		/// <returns>是否成功</returns>
		private OperateResult RenameGroupFileContainer(string filePath, string newPath)
		{
			filePath = filePath.ToUpper();
			string text = newPath.ToUpper();
			OperateResult result = OperateResult.CreateSuccessResult();
			group_marks_lock.Enter();
			if (m_dictionary_group_marks.ContainsKey(text) || Directory.Exists(text))
			{
				result = new OperateResult("修改的目录名称已经存在！");
			}
			else if (m_dictionary_group_marks.ContainsKey(filePath))
			{
				GroupFileContainer groupFileContainer = m_dictionary_group_marks[filePath];
				groupFileContainer.RenameFolder(newPath);
				m_dictionary_group_marks.Remove(filePath);
				m_dictionary_group_marks.Add(text, groupFileContainer);
			}
			group_marks_lock.Leave();
			return result;
		}

		/// <summary>
		/// 根据路径信息获取到文件列表管理容器，如果返回空，表示不存在。<br />
		/// The file list management container is obtained according to the path information. If the return is empty, it means that it does not exist.
		/// </summary>
		/// <param name="groups">文件路径信息</param>
		/// <returns>文件列表管理容器</returns>
		public GroupFileContainer GetGroupFromFilePath(string[] groups)
		{
			return GetGroupFromFilePath(ReturnAbsoluteFilePath(groups), create: false);
		}

		/// <summary>
		/// 获取文件夹的所有文件夹列表
		/// </summary>
		/// <param name="groups">分类信息</param>
		/// <returns>文件夹列表</returns>
		private string[] GetDirectories(string[] groups)
		{
			if (string.IsNullOrEmpty(filesDirectoryPath))
			{
				return new string[0];
			}
			string path = ReturnAbsoluteFilePath(groups);
			if (!Directory.Exists(path))
			{
				return new string[0];
			}
			return Directory.GetDirectories(path);
		}

		/// <summary>
		/// 获取当前文件的读写锁，如果没有会自动创建，文件名应该是guid文件名，例如 b35a11ec533147ca80c7f7d1713f015b7909<br />
		/// Acquire the read-write lock of the current file. If not, it will be created automatically. 
		/// The file name should be the guid file name, for example, b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="fileName">完整的文件路径</param>
		/// <returns>返回携带文件信息的读写锁</returns>
		private FileMarkId GetFileMarksFromDictionaryWithFileName(string fileName)
		{
			FileMarkId fileMarkId;
			lock (dictHybirdLock)
			{
				if (dictionaryFilesMarks.ContainsKey(fileName))
				{
					fileMarkId = dictionaryFilesMarks[fileName];
				}
				else
				{
					fileMarkId = new FileMarkId(base.LogNet, fileName);
					dictionaryFilesMarks.Add(fileName, fileMarkId);
				}
			}
			return fileMarkId;
		}

		/// <summary>
		/// 检查文件夹是否存在，不存在就创建
		/// </summary>
		private void CheckFolderAndCreate()
		{
			if (!Directory.Exists(filesDirectoryPath))
			{
				Directory.CreateDirectory(filesDirectoryPath);
			}
		}

		/// <summary>
		/// 删除已经存在的文件信息，文件的名称需要是guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="path">文件的路径</param>
		/// <param name="fileName">文件的guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909</param>
		private void DeleteExsistingFile(string path, string fileName)
		{
			DeleteExsistingFile(path, new List<string> { fileName });
		}

		/// <summary>
		/// 删除已经存在的文件信息，文件的名称需要是guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="path">文件的路径</param>
		/// <param name="fileNames">文件的guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909</param>
		private void DeleteExsistingFile(string path, List<string> fileNames)
		{
			foreach (string fileName in fileNames)
			{
				if (string.IsNullOrEmpty(fileName))
				{
					continue;
				}
				string fileUltimatePath = Path.Combine(path, fileName);
				FileMarkId fileMarksFromDictionaryWithFileName = GetFileMarksFromDictionaryWithFileName(fileName);
				fileMarksFromDictionaryWithFileName.AddOperation(delegate
				{
					if (!DeleteFileByName(fileUltimatePath))
					{
						base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteFailed + fileUltimatePath);
					}
					else
					{
						base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteSuccess + fileUltimatePath);
					}
				});
			}
		}

		private void AddMqttSession(MqttSession session)
		{
			lock (sessionsLock)
			{
				mqttSessions.Add(session);
			}
			base.LogNet?.WriteDebug(ToString(), $"{session} Online");
		}

		/// <summary>
		/// 让MQTT客户端正常下线，调用本方法即可自由控制会话客户端强制下线操作。
		/// </summary>
		/// <param name="session">当前的会话信息</param>
		/// <param name="reason">当前下线的原因，如果没有，代表正常下线</param>
		public void RemoveAndCloseSession(MqttSession session, string reason)
		{
			bool flag = false;
			lock (sessionsLock)
			{
				flag = mqttSessions.Remove(session);
			}
			if (!flag)
			{
				return;
			}
			NetSupport.CloseSocket(session.MqttSocket);
			base.LogNet?.WriteDebug(ToString(), $"{session} Offline {reason}");
			if (session.Protocol == "MQTT")
			{
				this.OnClientDisConnected?.Invoke(session);
				if (!string.IsNullOrEmpty(reason) && !string.IsNullOrEmpty(session.WillTopic))
				{
					PublishTopicPayload(session.WillTopic, session.WillMessage);
				}
			}
		}

		/// <summary>
		/// 删除服务器里的指定主题的驻留消息。<br />
		/// Delete the resident message of the specified topic in the server.
		/// </summary>
		/// <param name="topic">等待删除的主题关键字</param>
		public void DeleteRetainTopic(string topic)
		{
			lock (keysLock)
			{
				if (retainKeys.ContainsKey(topic))
				{
					retainKeys.Remove(topic);
				}
			}
		}

		/// <summary>
		/// 获取所有的驻留的消息的主题，如果消息发布的时候没有使用Retain属性，就无法通过本方法查到<br />
		/// Get the subject of all resident messages. If the Retain attribute is not used when the message is published, it cannot be found by this method
		/// </summary>
		/// <returns>主题的数组</returns>
		public string[] GetAllRetainTopics()
		{
			string[] result = null;
			lock (keysLock)
			{
				result = retainKeys.Select((KeyValuePair<string, MqttClientApplicationMessage> m) => m.Key).ToArray();
			}
			return result;
		}

		/// <summary>
		/// 获取订阅了某个主题的所有的会话列表信息<br />
		/// Get all the conversation list information subscribed to a topic
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>会话列表</returns>
		public MqttSession[] GetMqttSessionsByTopic(string topic)
		{
			MqttSession[] result = null;
			lock (sessionsLock)
			{
				result = mqttSessions.Where((MqttSession m) => m.Protocol == "MQTT" && m.IsClientSubscribe(topic, topicWildcard)).ToArray();
			}
			return result;
		}

		/// <summary>
		/// 释放当前的对象
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					timerHeart?.Dispose();
					group_marks_lock?.Dispose();
					this.ClientVerification = null;
					this.FileOperateVerification = null;
					this.OnClientApplicationMessageReceive = null;
					this.OnClientConnected = null;
					this.OnClientDisConnected = null;
					this.OnFileChangedEvent = null;
				}
				disposedValue = true;
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MqttServer[{base.Port}]";
		}
	}
}
