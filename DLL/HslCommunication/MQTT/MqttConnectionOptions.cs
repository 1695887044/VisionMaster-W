using System;
using HslCommunication.Core;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// 连接MQTT服务器的一些参数信息，适用<see cref="T:HslCommunication.MQTT.MqttClient" />消息发布订阅客户端以及<see cref="T:HslCommunication.MQTT.MqttSyncClient" />同步请求客户端。<br />
	/// Some parameter information for connecting to the MQTT server is applicable to the <see cref="T:HslCommunication.MQTT.MqttClient" /> message publishing and subscription client and the <see cref="T:HslCommunication.MQTT.MqttSyncClient" /> synchronization request client.
	/// </summary>
	public class MqttConnectionOptions
	{
		private string ipAddress = "127.0.0.1";

		private string hostName = string.Empty;

		/// <summary>
		/// Mqtt服务器的ip地址<br />
		/// IP address of Mqtt server
		/// </summary>
		public string IpAddress
		{
			get
			{
				return ipAddress;
			}
			set
			{
				hostName = value;
				ipAddress = HslHelper.GetIpAddressFromInput(value);
			}
		}

		/// <summary>
		/// 端口号。默认1883<br />
		/// The port number. Default 1883
		/// </summary>
		public int Port { get; set; }

		/// <summary>
		/// 客户端的id的标识<br />
		/// ID of the client
		/// </summary>
		/// <remarks>
		/// 实际在传输的时候，采用的是UTF8编码的方式来实现。
		/// </remarks>
		public string ClientId { get; set; }

		/// <summary>
		/// 连接到服务器的超时时间，默认是5秒，单位是毫秒<br />
		/// The timeout period for connecting to the server, the default is 5 seconds, the unit is milliseconds
		/// </summary>
		public int ConnectTimeout { get; set; }

		/// <summary>
		/// 遗嘱消息，为空或是主题为空则表示不使用遗嘱，该遗嘱对于 <see cref="T:HslCommunication.MQTT.MqttSyncClient" /> 无效
		/// </summary>
		public MqttApplicationMessage WillMessage { get; set; }

		/// <summary>
		/// 登录服务器的凭证，包含用户名和密码，可以为空<br />
		/// The credentials for logging in to the server, including the username and password, can be null
		/// </summary>
		public MqttCredential Credentials { get; set; }

		/// <summary>
		/// 获取或设置是否使用CA证书的方式来通信，当为空的时候，默认不使用证书，不为空则使用证书，需要传入证书的完整路径信息<br />
		/// Obtain or set whether to use the certificate authority to communicate, when empty, the default is not to use the certificate, 
		/// not empty to use the certificate, you need to pass in the full path information of the certificate
		/// </summary>
		public string CertificateFile { get; set; }

		/// <summary>
		/// 获取或设置是否使用 SSL/TLS 加密的方式来验证
		/// </summary>
		public bool UseSSL { get; set; }

		/// <summary>
		/// 在使用证书的情况下，获取或设置是否对服务器的证书进行检查并校验的操作，如果设置为<c>True</c>，也就是检查，安全性更高，反之不检查服务器的证书是否合法<br />
		/// In the case of using a certificate, obtain or set whether to check and verify the server's certificate, if set to <c>True</c>, 
		/// that is, check, the security is higher, and vice versa, the server's certificate is not checked
		/// </summary>
		public bool SSLSecure { get; set; }

		/// <summary>
		/// 设置的参数，最小单位为1s，当超过设置的时间间隔没有发送数据的时候，必须发送PINGREQ报文，否则服务器认定为掉线。<br />
		/// The minimum unit of the set parameter is 1s. When no data is sent beyond the set time interval, the PINGREQ message must be sent, otherwise the server considers it to be offline.
		/// </summary>
		/// <remarks>
		/// 保持连接（Keep Alive）是一个以秒为单位的时间间隔，表示为一个16位的字，它是指在客户端传输完成一个控制报文的时刻到发送下一个报文的时刻，
		/// 两者之间允许空闲的最大时间间隔。客户端负责保证控制报文发送的时间间隔不超过保持连接的值。如果没有任何其它的控制报文可以发送，
		/// 客户端必须发送一个PINGREQ报文，详细参见 [MQTT-3.1.2-23]
		/// </remarks>
		public TimeSpan KeepAlivePeriod { get; set; }

		/// <summary>
		/// 获取或是设置心跳时间的发送间隔。默认30秒钟<br />
		/// Get or set the heartbeat time interval. 30 seconds by default
		/// </summary>
		public TimeSpan KeepAliveSendInterval { get; set; }

		/// <summary>
		/// 是否清理会话，如果清理会话（CleanSession）标志被设置为1，客户端和服务端必须丢弃之前的任何会话并开始一个新的会话。
		/// 会话仅持续和网络连接同样长的时间。与这个会话关联的状态数据不能被任何之后的会话重用 [MQTT-3.1.2-6]。默认为清理会话。<br />
		/// Whether to clean the session. If the CleanSession flag is set to 1, the client and server must discard any previous session and start a new session. 
		/// The session only lasts as long as the network connection. The state data associated with this session cannot be reused by any subsequent sessions [MQTT-3.1.2-6]. 
		/// The default is to clean up the session.
		/// </summary>
		public bool CleanSession { get; set; }

		/// <summary>
		/// 获取或设置当前的连接是否加密处理，防止第三方对注册报文进行抓包处理，从而分析出用户名和密码，只适用于基于HslCommunication创建的MQTT Server。<br />
		/// Get or set whether the current connection is encrypted or not, to prevent the third party from capturing the registration message, 
		/// so as to analyze the user name and password. It is only applicable to the MQTT Server created based on HslCommunication.
		/// </summary>
		public bool UseRSAProvider { get; set; }

		/// <summary>
		/// 获取当前设置的服务器的网址信息，或是IP地址信息
		/// </summary>
		public string HostName => hostName;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public MqttConnectionOptions()
		{
			ClientId = string.Empty;
			IpAddress = "127.0.0.1";
			Port = 1883;
			KeepAlivePeriod = TimeSpan.FromSeconds(100.0);
			KeepAliveSendInterval = TimeSpan.FromSeconds(30.0);
			CleanSession = true;
			ConnectTimeout = 5000;
		}
	}
}
