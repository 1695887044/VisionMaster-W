using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HslCommunication.Core.Net;

namespace HslCommunication.Core.Device
{
	/// <summary>
	/// 设备服务器类
	/// </summary>
	public class DeviceServer : DeviceCommunication
	{
		/// <summary>
		/// 当接收到来自客户的数据信息时触发的对象，该数据可能来自tcp或是串口<br />
		/// The object that is triggered when receiving data information from the customer, the data may come from tcp or serial port
		/// </summary>
		/// <param name="sender">触发的服务器对象</param>
		/// <param name="session">消息的来源对象</param>
		/// <param name="data">实际的数据信息</param>
		public delegate void DataReceivedDelegate(object sender, PipeSession session, byte[] data);

		/// <summary>
		/// 数据发送的时候委托<br />
		/// Show DataSend To PLC
		/// </summary>
		/// <param name="sender">数据发送对象</param>
		/// <param name="data">数据内容</param>
		public delegate void DataSendDelegate(object sender, byte[] data);

		private Timer timerHeart;

		private CommunicationServer server;

		private int udpServePort = 0;

		/// <summary>
		/// 服务器引擎是否启动<br />
		/// Whether the server engine is started
		/// </summary>
		public bool IsStarted { get; private set; }

		/// <summary>
		/// 获取或设置服务器的端口号，如果是设置，需要在服务器启动前设置完成，才能生效。<br />
		/// Gets or sets the port number of the server. If it is set, it needs to be set before the server starts to take effect.
		/// </summary>
		/// <remarks>需要在服务器启动之前设置为有效</remarks>
		public int Port
		{
			get
			{
				return server.Port;
			}
			set
			{
				server.Port = value;
			}
		}

		/// <summary>
		/// 当服务器同时启动TCP及UDP服务的时候，获取当前的UDP服务的端口号<br />
		/// When the server starts TCP and UDP services at the same time, it obtains the port number of the current UDP service
		/// </summary>
		public int BothModeUdpPort => udpServePort;

		/// <summary>
		/// 获取或设置服务器是否支持IPv6的地址协议信息<br />
		/// Get or set whether the server supports IPv6 address protocol information
		/// </summary>
		/// <remarks>
		/// 默认为 <c>False</c>，也就是不启动
		/// </remarks>
		public bool EnableIPv6
		{
			get
			{
				return server.EnableIPv6;
			}
			set
			{
				server.EnableIPv6 = value;
			}
		}

		/// <summary>
		/// 获取或设置客户端的Socket的心跳时间信息，这个是Socket底层自动实现的心跳包，不基于协议层实现。默认小于0，不开启心跳检测，如果需要开启，设置 60_000 比较合适，单位毫秒<br />
		/// Get or set the heartbeat time information of the Socket of the client. This is the heartbeat packet automatically implemented by the bottom layer of the Socket, not based on the protocol layer. 
		/// The default value is less than 0, and heartbeat detection is not enabled. If you need to enable it, it is more appropriate to set 60_000, in milliseconds.
		/// </summary>
		/// <remarks>
		/// 经测试，在linux上，基于.net core3.1的程序运行时，设置了这个值是无效的。
		/// </remarks>
		public int SocketKeepAliveTime
		{
			get
			{
				return server.SocketKeepAliveTime;
			}
			set
			{
				server.SocketKeepAliveTime = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的服务器是否允许远程客户端进行写入数据操作，默认为<c>True</c><br />
		/// Gets or sets whether the current server allows remote clients to write data, the default is <c>True</c>
		/// </summary>
		/// <remarks>
		/// 如果设置为<c>False</c>，那么所有远程客户端的操作都会失败，直接返回错误码或是关闭连接。
		/// </remarks>
		public bool EnableWrite { get; set; } = true;


		/// <summary>
		/// 获取或设置两次数据交互时的最小时间间隔，默认为24小时。如果超过该设定的时间不进行数据交互，服务器就会强制断开当前的连接操作。<br />
		/// Get or set the minimum time interval between two data interactions, the default is 24 hours. 
		/// If the data exchange is not performed for more than the set time, the server will forcibly disconnect the current connection operation.
		/// </summary>
		/// <remarks>
		/// 举例设置为10分钟，ActiveTimeSpan = TimeSpan.FromMinutes( 10 );
		/// </remarks>
		public TimeSpan ActiveTimeSpan { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Net.CommunicationServer.SerialReceiveAtleastTime" />
		public int SerialReceiveAtleastTime
		{
			get
			{
				return server.SerialReceiveAtleastTime;
			}
			set
			{
				server.SerialReceiveAtleastTime = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.CommunicationServer.ForceSerialReceiveOnce" />
		public bool ForceSerialReceiveOnce
		{
			get
			{
				return server.ForceSerialReceiveOnce;
			}
			set
			{
				server.ForceSerialReceiveOnce = value;
			}
		}

		/// <summary>
		/// 获取在线的客户端的数量<br />
		/// Get the number of clients online
		/// </summary>
		public int OnlineCount => server.GetPipeSessions().Length;

		/// <summary>
		/// 当前服务器的模式，0：TCP服务器，1：UDP服务器，2：TCP及UDP服务器<br />
		/// Gets whether the current server is a TCP server or a UDP server
		/// </summary>
		public int ServerMode { get; private set; }

		/// <summary>
		/// 接收到数据的时候就触发的事件，示例详细参考API文档信息<br />
		/// An event that is triggered when data is received
		/// </summary>
		/// <remarks>
		/// 事件共有三个参数，sender指服务器本地的对象，例如 <see cref="T:HslCommunication.ModBus.ModbusTcpServer" /> 对象，source 指会话对象，网口对象为 <see cref="T:HslCommunication.Core.Net.AppSession" />，
		/// 串口为<see cref="T:System.IO.Ports.SerialPort" /> 对象，需要根据实际判断，data 为收到的原始数据 byte[] 对象
		/// </remarks>
		/// <example>
		/// 我们以Modbus的Server为例子，其他的虚拟服务器同理，因为都集成自本服务器对象
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDataServerBaseSample.cs" region="OnDataReceivedSample" title="数据接收触发的示例" />
		/// </example>
		public event DataReceivedDelegate OnDataReceived;

		/// <summary>
		/// 数据发送的时候就触发的事件<br />
		/// Events that are triggered when data is sent
		/// </summary>
		public event DataSendDelegate OnDataSend;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public DeviceServer()
		{
			ActiveTimeSpan = TimeSpan.FromHours(24.0);
			server = new CommunicationServer();
			server.OnPipeMessageReceived += Server_OnPipeMessageReceived;
			server.LogDebugMessage = delegate(string m)
			{
				base.LogNet?.WriteDebug(ToString(), m);
			};
			server.CreateNewMessage = GetNewNetMessage;
			server.ThreadPoolLoginAfterClientCheck = ThreadPoolLoginAfterClientCheck;
			server.CheckSerialDataComplete = CheckSerialReceiveDataComplete;
			timerHeart = new Timer(ThreadTimerHeartCheck, null, 2000, 10000);
		}

		/// <summary>
		/// 获取当前的核心服务器信息
		/// </summary>
		/// <returns>核心服务器</returns>
		public CommunicationServer GetCommunicationServer()
		{
			return server;
		}

		/// <summary>
		/// 当客户端的socket登录的时候额外检查的操作，并返回操作的结果信息。<br />
		/// The operation is additionally checked when the client's socket logs in, and the result information of the operation is returned.
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="endPoint">终结点</param>
		/// <returns>验证的结果</returns>
		protected virtual OperateResult SocketAcceptExtraCheck(Socket socket, IPEndPoint endPoint)
		{
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 服务器启动时额外的初始化信息，可以用于启动一些额外的服务的操作。<br />
		/// The extra initialization information when the server starts can be used to start some additional service operations.
		/// </summary>
		/// <remarks>需要在派生类中重写</remarks>
		protected virtual void StartInitialization()
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.ServerStart(System.Int32,System.Boolean)" />
		public virtual void ServerStart(int port, bool modeTcp = true)
		{
			if (!IsStarted)
			{
				IsStarted = true;
				ServerMode = ((!modeTcp) ? 1 : 0);
				StartInitialization();
				server.ServerStart(port, modeTcp);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.ServerStart(System.Int32,System.Int32)" />
		public void ServerStart(int tcpPort, int udpPort)
		{
			if (!IsStarted)
			{
				IsStarted = true;
				ServerMode = 2;
				StartInitialization();
				server.ServerStart(tcpPort, udpPort);
				udpServePort = udpPort;
			}
		}

		/// <summary>
		/// 使用已经配置好的端口启动服务器的引擎，并且使用TCP模式<br />
		/// Use the configured port to start the server's engine
		/// </summary>
		public void ServerStart()
		{
			ServerStart(Port);
		}

		/// <summary>
		/// 服务器关闭的时候需要做的事情<br />
		/// Things to do when the server is down
		/// </summary>
		protected virtual void CloseAction()
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.ServerClose" />
		public virtual void ServerClose()
		{
			if (IsStarted)
			{
				IsStarted = false;
				server.ServerClose();
				CloseAction();
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.CloseSerialSlave" />
		public void CloseSerialSlave()
		{
			server.CloseSerialSlave();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.UseSSL(System.Security.Cryptography.X509Certificates.X509Certificate)" />
		/// <param name="cert">证书对象</param>
		public void UseSSL(X509Certificate cert)
		{
			server.UseSSL(cert);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.UseSSL(System.String,System.String)" />
		public void UseSSL(string cert, string password = "")
		{
			server.UseSSL(cert, password);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.SetTrustedIpAddress(System.Collections.Generic.List{System.String})" />
		public void SetTrustedIpAddress(List<string> clients)
		{
			server.SetTrustedIpAddress(clients);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.GetTrustedClients" />
		public string[] GetTrustedClients()
		{
			return server.GetTrustedClients();
		}

		/// <summary>
		/// 将本系统的数据池数据存储到指定的文件<br />
		/// Store the data pool data of this system to the specified file
		/// </summary>
		/// <param name="path">指定文件的路径</param>
		/// <exception cref="T:System.ArgumentException"></exception>
		/// <exception cref="T:System.ArgumentNullException"></exception>
		/// <exception cref="T:System.IO.PathTooLongException"></exception>
		/// <exception cref="T:System.IO.DirectoryNotFoundException"></exception>
		/// <exception cref="T:System.IO.IOException"></exception>
		/// <exception cref="T:System.UnauthorizedAccessException"></exception>
		/// <exception cref="T:System.NotSupportedException"></exception>
		/// <exception cref="T:System.Security.SecurityException"></exception>
		public void SaveDataPool(string path)
		{
			byte[] bytes = SaveToBytes();
			File.WriteAllBytes(path, bytes);
		}

		/// <summary>
		/// 从文件加载数据池信息<br />
		/// Load datapool information from a file
		/// </summary>
		/// <param name="path">文件路径</param>
		/// <exception cref="T:System.ArgumentException"></exception>
		/// <exception cref="T:System.ArgumentNullException"></exception>
		/// <exception cref="T:System.IO.PathTooLongException"></exception>
		/// <exception cref="T:System.IO.DirectoryNotFoundException"></exception>
		/// <exception cref="T:System.IO.IOException"></exception>
		/// <exception cref="T:System.UnauthorizedAccessException"></exception>
		/// <exception cref="T:System.NotSupportedException"></exception>
		/// <exception cref="T:System.Security.SecurityException"></exception>
		public void LoadDataPool(string path)
		{
			if (File.Exists(path))
			{
				byte[] content = File.ReadAllBytes(path);
				LoadFromBytes(content);
			}
		}

		/// <summary>
		/// 从字节数据加载数据信息，需要进行重写方法<br />
		/// Loading data information from byte data requires rewriting method
		/// </summary>
		/// <param name="content">字节数据</param>
		protected virtual void LoadFromBytes(byte[] content)
		{
		}

		/// <summary>
		/// 将数据信息存储到字节数组去，需要进行重写方法<br />
		/// To store data information into a byte array, a rewrite method is required
		/// </summary>
		/// <returns>所有的内容</returns>
		protected virtual byte[] SaveToBytes()
		{
			return new byte[0];
		}

		/// <summary>
		/// 触发一个数据接收的事件信息<br />
		/// Event information that triggers a data reception
		/// </summary>
		/// <param name="session">管道会话</param>
		/// <param name="receive">接收数据信息</param>
		protected void RaiseDataReceived(PipeSession session, byte[] receive)
		{
			this.OnDataReceived?.Invoke(this, session, receive);
		}

		/// <summary>
		/// 触发一个数据发送的事件信息<br />
		/// Event information that triggers a data transmission
		/// </summary>
		/// <param name="send">数据内容</param>
		protected void RaiseDataSend(byte[] send)
		{
			this.OnDataSend?.Invoke(this, send);
		}

		private void Server_OnPipeMessageReceived(PipeSession session, byte[] buffer)
		{
			LogRevcMessage(buffer, session);
			OperateResult<byte[]> operateResult = null;
			try
			{
				operateResult = ReadFromCoreServer(session, buffer);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException("ReadFromCoreServer", "Source Data: " + buffer.ToHexString(' '), ex);
				return;
			}
			if (!operateResult.IsSuccess)
			{
				if (operateResult.ErrorCode != int.MinValue)
				{
					base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> {operateResult.Message}");
				}
				if (operateResult.Content != null && operateResult.Content.Length != 0)
				{
					operateResult.IsSuccess = true;
				}
			}
			if (operateResult.IsSuccess && operateResult.Content != null && operateResult.Content.Length != 0)
			{
				session.Communication.Send(operateResult.Content);
				RaiseDataSend(operateResult.Content);
				LogSendMessage(operateResult.Content, session);
			}
			RaiseDataReceived(session, buffer);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ReadFromCoreServer(System.Byte[])" />
		protected virtual OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 当客户端登录后，在Ip信息的过滤后，然后触发本方法，进行后续的数据接收，处理，并返回相关的数据信息<br />
		/// When the client logs in, after filtering the IP information, this method is then triggered to perform subsequent data reception, 
		/// processing, and return related data information
		/// </summary>
		/// <param name="session">管道信息</param>
		/// <param name="endPoint">终端节点</param>
		protected virtual OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				this.OnDataSend = null;
				this.OnDataReceived = null;
				ServerClose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.StartSerialSlave(System.String)" />
		public OperateResult StartSerialSlave(string com)
		{
			return server.StartSerialSlave(com);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.StartSerialSlave(System.String,System.Int32)" />
		public OperateResult StartSerialSlave(string com, int baudRate)
		{
			return server.StartSerialSlave(com, baudRate);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.StartSerialSlave(System.String,System.Int32,System.Int32,System.IO.Ports.Parity,System.IO.Ports.StopBits)" />
		public OperateResult StartSerialSlave(string com, int baudRate, int dataBits, Parity parity, StopBits stopBits)
		{
			return server.StartSerialSlave(com, baudRate, dataBits, parity, stopBits);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.StartSerialSlave(System.Action{System.IO.Ports.SerialPort})" />
		public OperateResult StartSerialSlave(Action<SerialPort> inni)
		{
			return server.StartSerialSlave(inni);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationServer.CheckSerialReceiveDataComplete(System.Byte[],System.Int32)" />
		protected virtual bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			return false;
		}

		private void ThreadTimerHeartCheck(object obj)
		{
			server.RemoveSession(ActiveTimeSpan);
		}
	}
}
