using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Core.Security;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// Mqtt协议的客户端实现，支持订阅消息，发布消息，详细的使用例子参考api文档<br />
	/// The client implementation of the Mqtt protocol supports subscription messages and publishing messages. For detailed usage examples, refer to the api documentation. 
	/// </summary>
	/// <remarks>
	/// 这是一个MQTT的客户端实现，参照MQTT协议的3.1.1版本设计实现的。服务器可以是其他的组件提供的，其他的可以参考示例<br />
	/// This is an MQTT client implementation, designed and implemented with reference to version 3.1.1 of the MQTT protocol. The server can be provided by other components.
	/// </remarks>
	/// <example>
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test" title="简单的实例化" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test2" title="带用户名密码的实例化" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test9" title="如果使用证书的情况" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test10" title="简单的加密操作" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test3" title="连接示例" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test4" title="发布示例" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test5" title="订阅示例" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test8" title="网络重连示例" />
	/// 当我们在一个多窗体的客户端里使用了<see cref="T:HslCommunication.MQTT.MqttClient" />类，可能很多界面都需要订阅主题，显示一些实时数据信息。只由主窗体来订阅再把数据传递给子窗体却不是很容易操作。
	/// 所以在hsl里提供了更加便捷的操作方法。方便在每个子窗体界面中，订阅，显示，取消订阅操作。核心代码如下：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test9" title="子窗体订阅操作" />
	/// 以下的例子是DEMO程序的一个例子代码，也可以作为参考
	/// <code lang="cs" source="TestProject\HslCommunicationDemo\MQTT\FormMqttSubscribe.cs" region="Sample" title="DEMO子窗体" />
	/// </example>
	public class MqttClient : NetworkXBase, IDisposable
	{
		/// <summary>
		/// 当接收到Mqtt订阅的信息的时候触发<br />
		/// Triggered when receiving Mqtt subscription information
		/// </summary>
		/// <param name="client">收到消息时候的client实例对象</param>
		/// <param name="message">Mqtt的消息对象</param>
		public delegate void MqttMessageReceiveDelegate(MqttClient client, MqttApplicationMessage message);

		/// <summary>
		/// 连接服务器成功的委托<br />
		/// Connection server successfully delegated
		/// </summary>
		public delegate void OnClientConnectedDelegate(MqttClient client);

		private NetworkStream networkStream;

		private SslStream sslStream = null;

		private DateTime activeTime;

		private int isReConnectServer = 0;

		private List<MqttPublishMessage> publishMessages;

		private object listLock;

		private Dictionary<string, SubscribeTopic> subscribeTopics;

		private object connectLock;

		private object subscribeLock;

		private SoftIncrementCount incrementCount;

		private bool closed = false;

		private MqttConnectionOptions connectionOptions;

		private Timer timerCheck;

		private bool disposedValue;

		private RSACryptoServiceProvider cryptoServiceProvider = null;

		private AesCryptography aesCryptography = null;

		/// <summary>
		/// 获取当前的连接配置参数信息<br />
		/// Get current connection configuration parameter information
		/// </summary>
		public MqttConnectionOptions ConnectionOptions => connectionOptions;

		/// <summary>
		/// 获取或设置是否启动定时器去检测当前客户端是否超时掉线。默认为 <c>True</c><br />
		/// Get or set whether to start the timer to detect whether the current client timeout and disconnection. Default is <c>True</c>
		/// </summary>
		public bool UseTimerCheckDropped { get; set; } = true;


		/// <summary>
		/// 获取或设置当前的服务器连接是否成功，定时获取本属性可用于实时更新连接状态信息。<br />
		/// Get or set whether the current server connection is successful or not. 
		/// This property can be obtained regularly and can be used to update the connection status information in real time.
		/// </summary>
		public bool IsConnected { get; private set; } = false;


		/// <summary>
		/// 获取当前的客户端对象已经订阅的所有的Topic信息<br />
		/// Get all Topic information that the current client object has subscribed to
		/// </summary>
		public string[] SubcribeTopics
		{
			get
			{
				lock (subscribeLock)
				{
					return subscribeTopics.Keys.ToArray();
				}
			}
		}

		/// <summary>
		/// 获取或设置当前客户端关联的自定义的对象内容<br />
		/// Gets or sets the custom object content associated with the current client
		/// </summary>
		public object Tag { get; set; }

		/// <summary>
		/// 当接收到Mqtt订阅的信息的时候触发
		/// </summary>
		public event MqttMessageReceiveDelegate OnMqttMessageReceived;

		/// <summary>
		/// 当网络发生异常的时候触发的事件，用户应该在事件里进行重连服务器
		/// </summary>
		public event EventHandler OnNetworkError;

		/// <summary>
		/// 当客户端连接成功触发事件，就算是重新连接服务器后，也是会触发的<br />
		/// The event is triggered when the client is connected successfully, even after reconnecting to the server.
		/// </summary>
		public event OnClientConnectedDelegate OnClientConnected;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		/// <param name="options">配置信息</param>
		public MqttClient(MqttConnectionOptions options)
		{
			connectionOptions = options;
			incrementCount = new SoftIncrementCount(65535L, 1L);
			listLock = new object();
			publishMessages = new List<MqttPublishMessage>();
			subscribeTopics = new Dictionary<string, SubscribeTopic>();
			activeTime = DateTime.Now;
			subscribeLock = new object();
			connectLock = new object();
		}

		/// <summary>
		/// 连接服务器，如果连接失败，请稍候重试。<br />
		/// Connect to the server. If the connection fails, try again later.
		/// </summary>
		/// <returns>连接是否成功</returns>
		public OperateResult ConnectServer()
		{
			if (connectionOptions == null)
			{
				return new OperateResult("Optines is null");
			}
			OperateResult<Socket> operateResult = CreateSocketAndConnect(connectionOptions.IpAddress, connectionOptions.Port, connectionOptions.ConnectTimeout);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			RSACryptoServiceProvider rsa = null;
			if (connectionOptions.UseRSAProvider)
			{
				cryptoServiceProvider = new RSACryptoServiceProvider();
				OperateResult operateResult2 = Send(operateResult.Content, MqttHelper.BuildMqttCommand(byte.MaxValue, null, HslSecurity.ByteEncrypt(cryptoServiceProvider.GetPEMPublicKey())).Content);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<byte, byte[]> operateResult3 = ReceiveMqttMessage(operateResult.Content, 10000);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				try
				{
					byte[] publicKey = cryptoServiceProvider.DecryptLargeData(HslSecurity.ByteDecrypt(operateResult3.Content2));
					rsa = RSAHelper.CreateRsaProviderFromPublicKey(publicKey);
				}
				catch (Exception ex)
				{
					operateResult.Content?.Close();
					return new OperateResult("RSA check failed: " + ex.Message);
				}
			}
			OperateResult<byte[]> operateResult4 = MqttHelper.BuildConnectMqttCommand(connectionOptions, "MQTT", rsa);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			if (!ConnectionOptions.UseSSL)
			{
				OperateResult operateResult5 = Send(operateResult.Content, operateResult4.Content);
				if (!operateResult5.IsSuccess)
				{
					return operateResult5;
				}
				OperateResult<byte, byte[]> operateResult6 = ReceiveMqttMessage(operateResult.Content, 30000);
				if (!operateResult6.IsSuccess)
				{
					return operateResult6;
				}
				OperateResult operateResult7 = MqttHelper.CheckConnectBack(operateResult6.Content1, operateResult6.Content2);
				if (!operateResult7.IsSuccess)
				{
					operateResult.Content?.Close();
					return operateResult7;
				}
				if (connectionOptions.UseRSAProvider)
				{
					string @string = Encoding.UTF8.GetString(cryptoServiceProvider.Decrypt(operateResult6.Content2.RemoveBegin(2), fOAEP: false));
					aesCryptography = new AesCryptography(@string);
				}
			}
			else
			{
				OperateResult<SslStream> operateResult8 = CreateSslStream(operateResult.Content, createNew: true);
				if (!operateResult8.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(operateResult8);
				}
				OperateResult operateResult9 = Send(operateResult8.Content, operateResult4.Content);
				if (!operateResult9.IsSuccess)
				{
					return operateResult9;
				}
				OperateResult<byte, byte[]> operateResult10 = ReceiveMqttMessage(operateResult8.Content, 30000);
				if (!operateResult10.IsSuccess)
				{
					return operateResult10;
				}
				OperateResult operateResult11 = MqttHelper.CheckConnectBack(operateResult10.Content1, operateResult10.Content2);
				if (!operateResult11.IsSuccess)
				{
					operateResult.Content?.Close();
					return operateResult11;
				}
			}
			incrementCount.ResetCurrentValue();
			closed = false;
			try
			{
				operateResult.Content.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveAsyncCallback, operateResult.Content);
			}
			catch (Exception ex2)
			{
				return new OperateResult(ex2.Message);
			}
			CoreSocket?.Close();
			CoreSocket = operateResult.Content;
			IsConnected = true;
			this.OnClientConnected?.Invoke(this);
			timerCheck?.Dispose();
			activeTime = DateTime.Now;
			if (UseTimerCheckDropped && (int)connectionOptions.KeepAliveSendInterval.TotalMilliseconds > 0)
			{
				timerCheck = new Timer(TimerCheckServer, null, 2000, (int)connectionOptions.KeepAliveSendInterval.TotalMilliseconds);
			}
			return OperateResult.CreateSuccessResult();
		}

		private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			if (sslPolicyErrors == SslPolicyErrors.None)
			{
				return true;
			}
			return !connectionOptions.SSLSecure;
		}

		/// <summary>
		/// 关闭Mqtt服务器的连接。<br />
		/// Close the connection to the Mqtt server.
		/// </summary>
		public void ConnectClose()
		{
			lock (connectLock)
			{
				closed = true;
				IsConnected = false;
			}
			OperateResult<byte[]> operateResult = MqttHelper.BuildMqttCommand(14, 0, null, null);
			if (operateResult.IsSuccess)
			{
				SendMqttBytes(operateResult.Content);
			}
			timerCheck?.Dispose();
			HslHelper.ThreadSleep(20);
			CoreSocket?.Close();
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.ConnectServer" />
		public async Task<OperateResult> ConnectServerAsync()
		{
			if (connectionOptions == null)
			{
				return new OperateResult("Optines is null");
			}
			OperateResult<Socket> connect = await CreateSocketAndConnectAsync(connectionOptions.IpAddress, connectionOptions.Port, connectionOptions.ConnectTimeout);
			if (!connect.IsSuccess)
			{
				return connect;
			}
			RSACryptoServiceProvider rsa = null;
			if (connectionOptions.UseRSAProvider)
			{
				cryptoServiceProvider = new RSACryptoServiceProvider();
				OperateResult sendKey = await SendAsync(connect.Content, MqttHelper.BuildMqttCommand(byte.MaxValue, null, HslSecurity.ByteEncrypt(cryptoServiceProvider.GetPEMPublicKey())).Content);
				if (!sendKey.IsSuccess)
				{
					return sendKey;
				}
				OperateResult<byte, byte[]> key = await ReceiveMqttMessageAsync(connect.Content, 10000);
				if (!key.IsSuccess)
				{
					return key;
				}
				try
				{
					byte[] serverPublicToken = cryptoServiceProvider.DecryptLargeData(HslSecurity.ByteDecrypt(key.Content2));
					rsa = RSAHelper.CreateRsaProviderFromPublicKey(serverPublicToken);
				}
				catch (Exception ex2)
				{
					connect.Content?.Close();
					return new OperateResult("RSA check failed: " + ex2.Message);
				}
			}
			OperateResult<byte[]> command = MqttHelper.BuildConnectMqttCommand(connectionOptions, "MQTT", rsa);
			if (!command.IsSuccess)
			{
				return command;
			}
			if (!ConnectionOptions.UseSSL)
			{
				OperateResult send2 = await SendAsync(connect.Content, command.Content);
				if (!send2.IsSuccess)
				{
					return send2;
				}
				OperateResult<byte, byte[]> receive2 = await ReceiveMqttMessageAsync(connect.Content, 30000);
				if (!receive2.IsSuccess)
				{
					return receive2;
				}
				OperateResult check2 = MqttHelper.CheckConnectBack(receive2.Content1, receive2.Content2);
				if (!check2.IsSuccess)
				{
					connect.Content?.Close();
					return check2;
				}
				if (connectionOptions.UseRSAProvider)
				{
					string key2 = Encoding.UTF8.GetString(cryptoServiceProvider.Decrypt(receive2.Content2.RemoveBegin(2), fOAEP: false));
					aesCryptography = new AesCryptography(key2);
				}
			}
			else
			{
				OperateResult<SslStream> ssl = CreateSslStream(connect.Content, createNew: true);
				if (!ssl.IsSuccess)
				{
					return OperateResult.CreateFailedResult<string>(ssl);
				}
				OperateResult send = await SendAsync(ssl.Content, command.Content);
				if (!send.IsSuccess)
				{
					return send;
				}
				OperateResult<byte, byte[]> receive = await ReceiveMqttMessageAsync(ssl.Content, 30000);
				if (!receive.IsSuccess)
				{
					return receive;
				}
				OperateResult check = MqttHelper.CheckConnectBack(receive.Content1, receive.Content2);
				if (!check.IsSuccess)
				{
					connect.Content?.Close();
					return check;
				}
			}
			incrementCount.ResetCurrentValue();
			closed = false;
			try
			{
				connect.Content.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveAsyncCallback, connect.Content);
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
			CoreSocket?.Close();
			CoreSocket = connect.Content;
			IsConnected = true;
			this.OnClientConnected?.Invoke(this);
			timerCheck?.Dispose();
			activeTime = DateTime.Now;
			if (UseTimerCheckDropped && (int)connectionOptions.KeepAliveSendInterval.TotalMilliseconds > 0)
			{
				timerCheck = new Timer(TimerCheckServer, null, 2000, (int)connectionOptions.KeepAliveSendInterval.TotalMilliseconds);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.ConnectClose" />
		public async Task ConnectCloseAsync()
		{
			lock (connectLock)
			{
				closed = true;
				IsConnected = true;
			}
			OperateResult<byte[]> command = MqttHelper.BuildMqttCommand(14, 0, null, null);
			if (command.IsSuccess)
			{
				await SendMqttBytesAsync(command.Content);
			}
			timerCheck?.Dispose();
			HslHelper.ThreadSleep(20);
			CoreSocket?.Close();
		}

		/// <summary>
		/// 发布一个MQTT协议的消息到服务器。该消息包含主题，负载数据，消息等级，是否保留信息。<br />
		/// Publish an MQTT protocol message to the server. The message contains the subject, payload data, message level, and whether to retain information.
		/// </summary>
		/// <param name="message">消息</param>
		/// <returns>发布结果</returns>
		/// <example>
		/// 参照 <see cref="T:HslCommunication.MQTT.MqttClient" /> 的示例说明。
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test" title="简单的实例化" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test4" title="发布示例" />
		/// </example>
		public OperateResult PublishMessage(MqttApplicationMessage message)
		{
			MqttPublishMessage mqttPublishMessage = new MqttPublishMessage
			{
				Identifier = (int)((message.QualityOfServiceLevel != 0) ? incrementCount.GetCurrentValue() : 0),
				Message = message
			};
			OperateResult<byte[]> operateResult = MqttHelper.BuildPublishMqttCommand(mqttPublishMessage, aesCryptography);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtMostOnce)
			{
				return SendMqttBytes(operateResult.Content);
			}
			AddPublishMessage(mqttPublishMessage);
			return SendMqttBytes(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.PublishMessage(HslCommunication.MQTT.MqttApplicationMessage)" />
		public async Task<OperateResult> PublishMessageAsync(MqttApplicationMessage message)
		{
			MqttPublishMessage publishMessage = new MqttPublishMessage
			{
				Identifier = (int)((message.QualityOfServiceLevel != 0) ? incrementCount.GetCurrentValue() : 0),
				Message = message
			};
			OperateResult<byte[]> command = MqttHelper.BuildPublishMqttCommand(publishMessage, aesCryptography);
			if (!command.IsSuccess)
			{
				return command;
			}
			if (message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtMostOnce)
			{
				return await SendMqttBytesAsync(command.Content);
			}
			AddPublishMessage(publishMessage);
			return await SendMqttBytesAsync(command.Content);
		}

		/// <summary>
		/// 从服务器订阅一个或多个主题信息<br />
		/// Subscribe to one or more topics from the server
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>订阅结果</returns>
		/// <example>
		/// 参照 <see cref="T:HslCommunication.MQTT.MqttClient" /> 的示例说明。
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test" title="简单的实例化" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test5" title="订阅示例" />
		/// </example>
		public OperateResult SubscribeMessage(string topic)
		{
			return SubscribeMessage(new string[1] { topic });
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.SubscribeMessage(System.String)" />
		public OperateResult SubscribeMessage(string[] topics)
		{
			MqttSubscribeMessage subcribeMessage = new MqttSubscribeMessage
			{
				Identifier = (int)incrementCount.GetCurrentValue(),
				Topics = topics
			};
			return SubscribeMessage(subcribeMessage);
		}

		/// <summary>
		/// 向服务器订阅一个主题消息，可以指定订阅的主题数组，订阅的质量等级，还有消息标识符<br />
		/// To subscribe to a topic message from the server, you can specify the subscribed topic array, 
		/// the subscription quality level, and the message identifier
		/// </summary>
		/// <param name="subcribeMessage">订阅的消息本体</param>
		/// <returns>是否订阅成功</returns>
		public OperateResult SubscribeMessage(MqttSubscribeMessage subcribeMessage)
		{
			if (subcribeMessage.Topics == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (subcribeMessage.Topics.Length == 0)
			{
				return OperateResult.CreateSuccessResult();
			}
			OperateResult<byte[]> operateResult = MqttHelper.BuildSubscribeMqttCommand(subcribeMessage);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendMqttBytes(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			AddSubTopics(subcribeMessage.Topics);
			return OperateResult.CreateSuccessResult();
		}

		private void AddSubTopics(string[] topics)
		{
			lock (subscribeLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (!subscribeTopics.ContainsKey(topics[i]))
					{
						subscribeTopics.Add(topics[i], new SubscribeTopic(topics[i]));
					}
					subscribeTopics[topics[i]].AddSubscribeTick();
				}
			}
		}

		/// <summary>
		/// 获取已经订阅的主题信息，方便针对不同的界面订阅不同的主题。<br />
		/// Get subscribed topic information, which is convenient for subscribing to different topics for different interfaces.
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>订阅主题信息</returns>
		public SubscribeTopic GetSubscribeTopic(string topic)
		{
			SubscribeTopic result = null;
			lock (subscribeLock)
			{
				if (subscribeTopics.ContainsKey(topic))
				{
					result = subscribeTopics[topic];
				}
			}
			return result;
		}

		/// <summary>
		/// 取消订阅多个主题信息，取消之后，当前的订阅数据就不在接收到，除非服务器强制推送。<br />
		/// Unsubscribe from multiple topic information. After cancellation, the current subscription data will not be received unless the server forces it to push it.
		/// </summary>
		/// <param name="topics">主题信息</param>
		/// <returns>取消订阅结果</returns>
		/// <example>
		/// 参照 <see cref="T:HslCommunication.MQTT.MqttClient" /> 的示例说明。
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test" title="简单的实例化" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test7" title="订阅示例" />
		/// </example>
		public OperateResult UnSubscribeMessage(string[] topics)
		{
			MqttSubscribeMessage message = new MqttSubscribeMessage
			{
				Identifier = (int)incrementCount.GetCurrentValue(),
				Topics = topics
			};
			OperateResult<byte[]> operateResult = MqttHelper.BuildUnSubscribeMqttCommand(message);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendMqttBytes(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			RemoveSubTopics(topics);
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 取消订阅指定的主题信息，取消之后，就不再接收当前主题的数据，除非服务器强制推送<br />
		/// Unsubscribe from the specified topic information. After cancellation, the data of the current topic will no longer be received unless the server forces push
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>取消订阅结果</returns>
		/// <example>
		/// 参照 <see cref="T:HslCommunication.MQTT.MqttClient" /> 的示例说明。
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test" title="简单的实例化" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MQTTClient.cs" region="Test7" title="订阅示例" />
		/// </example>
		public OperateResult UnSubscribeMessage(string topic)
		{
			return UnSubscribeMessage(new string[1] { topic });
		}

		private bool RemoveSubTopics(string[] topics)
		{
			bool result = true;
			lock (subscribeLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (subscribeTopics.ContainsKey(topics[i]))
					{
						subscribeTopics.Remove(topics[i]);
					}
				}
			}
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.SubscribeMessage(System.String)" />
		public async Task<OperateResult> SubscribeMessageAsync(string topic)
		{
			return await SubscribeMessageAsync(new string[1] { topic });
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.SubscribeMessage(System.String[])" />
		public async Task<OperateResult> SubscribeMessageAsync(string[] topics)
		{
			if (topics == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (topics.Length == 0)
			{
				return OperateResult.CreateSuccessResult();
			}
			MqttSubscribeMessage subcribeMessage = new MqttSubscribeMessage
			{
				Identifier = (int)incrementCount.GetCurrentValue(),
				Topics = topics
			};
			OperateResult<byte[]> command = MqttHelper.BuildSubscribeMqttCommand(subcribeMessage);
			if (!command.IsSuccess)
			{
				return command;
			}
			AddSubTopics(topics);
			return await SendMqttBytesAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.UnSubscribeMessage(System.String[])" />
		public async Task<OperateResult> UnSubscribeMessageAsync(string[] topics)
		{
			MqttSubscribeMessage subcribeMessage = new MqttSubscribeMessage
			{
				Identifier = (int)incrementCount.GetCurrentValue(),
				Topics = topics
			};
			OperateResult<byte[]> command = MqttHelper.BuildUnSubscribeMqttCommand(subcribeMessage);
			RemoveSubTopics(topics);
			return await SendMqttBytesAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttClient.UnSubscribeMessage(System.String)" />
		public async Task<OperateResult> UnSubscribeMessageAsync(string topic)
		{
			return await UnSubscribeMessageAsync(new string[1] { topic });
		}

		private void OnMqttNetworkError()
		{
			if (closed)
			{
				base.LogNet?.WriteDebug(ToString(), "Closed");
			}
			else
			{
				if (Interlocked.CompareExchange(ref isReConnectServer, 1, 0) != 0)
				{
					return;
				}
				try
				{
					IsConnected = false;
					timerCheck?.Dispose();
					timerCheck = null;
					if (this.OnNetworkError == null)
					{
						base.LogNet?.WriteInfo(ToString(), "The network is abnormal, and the system is ready to automatically reconnect after 10 seconds.");
						while (true)
						{
							for (int i = 0; i < 10; i++)
							{
								HslHelper.ThreadSleep(1000);
								base.LogNet?.WriteInfo(ToString(), $"Wait for {10 - i} second to connect to the server ...");
								if (closed)
								{
									base.LogNet?.WriteDebug(ToString(), "Closed");
									Interlocked.Exchange(ref isReConnectServer, 0);
									return;
								}
							}
							lock (connectLock)
							{
								if (closed)
								{
									base.LogNet?.WriteDebug(ToString(), "Closed");
									Interlocked.Exchange(ref isReConnectServer, 0);
									return;
								}
								OperateResult operateResult = ConnectServer();
								if (operateResult.IsSuccess)
								{
									base.LogNet?.WriteInfo(ToString(), "Successfully connected to the server!");
									break;
								}
								base.LogNet?.WriteInfo(ToString(), "The connection failed. Prepare to reconnect after 10 seconds.");
								if (closed)
								{
									base.LogNet?.WriteDebug(ToString(), "Closed");
									Interlocked.Exchange(ref isReConnectServer, 0);
									return;
								}
								continue;
							}
						}
					}
					else
					{
						this.OnNetworkError?.Invoke(this, new EventArgs());
					}
					Interlocked.Exchange(ref isReConnectServer, 0);
				}
				catch
				{
					Interlocked.Exchange(ref isReConnectServer, 0);
					throw;
				}
			}
		}

		private async void ReceiveAsyncCallback(IAsyncResult ar)
		{
			object asyncState = ar.AsyncState;
			Socket socket = asyncState as Socket;
			if (socket == null)
			{
				return;
			}
			try
			{
				socket.EndReceive(ar);
			}
			catch (ObjectDisposedException)
			{
				socket?.Close();
				base.LogNet?.WriteDebug(ToString(), "Closed");
				return;
			}
			catch (Exception ex4)
			{
				Exception ex2 = ex4;
				socket?.Close();
				base.LogNet?.WriteDebug(ToString(), "ReceiveCallback Failed:" + ex2.Message);
				OnMqttNetworkError();
				return;
			}
			if (closed)
			{
				base.LogNet?.WriteDebug(ToString(), "Closed");
				return;
			}
			OperateResult<byte, byte[]> read;
			if (string.IsNullOrEmpty(connectionOptions.CertificateFile))
			{
				read = await ReceiveMqttMessageAsync(socket, 30000);
			}
			else
			{
				OperateResult<SslStream> ssl = CreateSslStream(socket);
				if (!ssl.IsSuccess)
				{
					socket?.Close();
					base.LogNet?.WriteDebug(ToString(), "CreateSslStream Failed:" + ssl.Message);
					OnMqttNetworkError();
					return;
				}
				read = await ReceiveMqttMessageAsync(ssl.Content, 30000);
			}
			if (!read.IsSuccess)
			{
				OnMqttNetworkError();
				return;
			}
			byte mqttCode = read.Content1;
			byte[] data = read.Content2;
			switch (mqttCode >> 4)
			{
			case 4:
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] Publish Ack: {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			case 5:
				SendMqttBytes(MqttHelper.BuildMqttCommand(6, 2, data, new byte[0]).Content);
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] Publish Rec: {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			case 7:
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] Publish Complete: {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			case 13:
				activeTime = DateTime.Now;
				base.LogNet?.WriteDebug(ToString(), "Heart Code Check!");
				break;
			case 9:
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] Subscribe Ack: {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			case 11:
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] UnSubscribe Ack: {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			case 3:
				ExtraPublishData(mqttCode, data);
				break;
			default:
				base.LogNet?.WriteDebug(ToString(), $"Code[{mqttCode:X2}] {SoftBasic.ByteToHexString(data, ' ')}");
				break;
			}
			try
			{
				socket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveAsyncCallback, socket);
			}
			catch (Exception ex)
			{
				socket?.Close();
				base.LogNet?.WriteDebug(ToString(), "BeginReceive Failed:" + ex.Message);
				OnMqttNetworkError();
			}
		}

		private void ExtraPublishData(byte mqttCode, byte[] data)
		{
			activeTime = DateTime.Now;
			OperateResult<string, byte[]> operateResult = MqttHelper.ExtraMqttReceiveData(mqttCode, data, aesCryptography);
			if (!operateResult.IsSuccess)
			{
				base.LogNet?.WriteDebug(ToString(), operateResult.Message);
				return;
			}
			int qos = MqttHelper.ExtraQosFromMqttCode(mqttCode);
			MqttApplicationMessage mqttApplicationMessage = new MqttApplicationMessage();
			mqttApplicationMessage.Topic = operateResult.Content1;
			mqttApplicationMessage.Retain = (mqttCode & 1) == 1;
			mqttApplicationMessage.QualityOfServiceLevel = MqttHelper.GetFromQos(qos);
			mqttApplicationMessage.Payload = operateResult.Content2;
			this.OnMqttMessageReceived?.Invoke(this, mqttApplicationMessage);
			GetSubscribeTopic(operateResult.Content1)?.TriggerSubscription(this, mqttApplicationMessage);
		}

		private void TimerCheckServer(object obj)
		{
			if (CoreSocket != null)
			{
				if ((DateTime.Now - activeTime).TotalSeconds > connectionOptions.KeepAliveSendInterval.TotalSeconds * 3.0)
				{
					OnMqttNetworkError();
				}
				else if (!SendMqttBytes(MqttHelper.BuildMqttCommand(12, 0, new byte[0], new byte[0]).Content).IsSuccess)
				{
					OnMqttNetworkError();
				}
			}
		}

		private void AddPublishMessage(MqttPublishMessage publishMessage)
		{
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
					incrementCount?.Dispose();
					timerCheck?.Dispose();
					this.OnClientConnected = null;
					this.OnMqttMessageReceived = null;
					this.OnNetworkError = null;
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

		private OperateResult<SslStream> CreateSslStream(Socket socket, bool createNew = false)
		{
			if (createNew)
			{
				networkStream?.Close();
				sslStream?.Close();
				networkStream = new NetworkStream(socket, ownsSocket: false);
				sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false, ValidateServerCertificate, null);
				try
				{
					if (string.IsNullOrEmpty(ConnectionOptions.CertificateFile))
					{
						sslStream.AuthenticateAsClient(connectionOptions.HostName);
					}
					else
					{
						X509CertificateCollection clientCertificates = new X509CertificateCollection(new X509Certificate[1] { X509Certificate.CreateFromCertFile(ConnectionOptions.CertificateFile) });
						sslStream.AuthenticateAsClient(connectionOptions.HostName, clientCertificates, SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, checkCertificateRevocation: false);
					}
					return OperateResult.CreateSuccessResult(sslStream);
				}
				catch (Exception ex)
				{
					return new OperateResult<SslStream>(ex.Message);
				}
			}
			return OperateResult.CreateSuccessResult(sslStream);
		}

		private OperateResult SendMqttBytes(byte[] data)
		{
			if (string.IsNullOrEmpty(connectionOptions.CertificateFile))
			{
				return Send(CoreSocket, data);
			}
			OperateResult<SslStream> operateResult = CreateSslStream(CoreSocket);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			return Send(operateResult.Content, data);
		}

		private async Task<OperateResult> SendMqttBytesAsync(byte[] data)
		{
			if (string.IsNullOrEmpty(connectionOptions.CertificateFile))
			{
				return await SendAsync(CoreSocket, data);
			}
			OperateResult<SslStream> ssl = CreateSslStream(CoreSocket);
			if (!ssl.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(ssl);
			}
			return await SendAsync(ssl.Content, data);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MqttClient[{connectionOptions.IpAddress}:{connectionOptions.Port}]";
		}
	}
}
