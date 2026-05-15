using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using HslCommunication.Core.IMessage;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// 用于TCP/IP协议的传输管道信息<br />
	/// Transport pipe information of the IP protocol
	/// </summary>
	public class PipeTcpNet : CommunicationPipe
	{
		/// <summary>
		/// 配置的远程主机的名称，有可能是网址，也可能是IP
		/// </summary>
		protected string host = "127.0.0.1";

		private string ipAddress = "127.0.0.1";

		private int[] _port = new int[1] { 2000 };

		private int indexPort = -1;

		private Socket socket;

		private int connectTimeOut = 10000;

		/// <summary>
		/// 获取或设置绑定的本地的IP地址和端口号信息，如果端口设置为0，代表任何可用的端口<br />
		/// Get or set the bound local IP address and port number information, if the port is set to 0, it means any available port
		/// </summary>
		/// <remarks>
		/// 默认为NULL, 也即是不绑定任何本地的IP及端口号信息，使用系统自动分配的方式。<br />
		/// The default is NULL, which means that no local IP and port number information are bound, and the system automatically assigns it.
		/// </remarks>
		public IPEndPoint LocalBinding { get; set; }

		/// <summary>
		/// 获取或是设置远程服务器的IP地址，如果是本机测试，那么需要设置为127.0.0.1 <br />
		/// Get or set the IP address of the remote server. If it is a local test, then it needs to be set to 127.0.0.1
		/// </summary>
		/// <remarks>
		/// 最好实在初始化的时候进行指定，当使用短连接的时候，支持动态更改，切换；当使用长连接后，无法动态更改<br />
		/// 支持使用域名的网址方式，例如：www.hslcommunication.cn
		/// </remarks>
		/// <example>
		/// 以下举例modbus-tcp的短连接及动态更改ip地址的示例
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="IpAddressExample" title="IpAddress示例" />
		/// </example>
		public string IpAddress
		{
			get
			{
				return ipAddress;
			}
			set
			{
				host = value;
				ipAddress = HslHelper.GetIpAddressFromInput(value);
			}
		}

		/// <summary>
		/// 获取当前设置的远程的地址，可能是IP地址，也可能是网址，也就是初始设置的地址信息<br />
		/// Obtain the address of the remote address that is currently set, which may be an IP address or a web address, that is, the address information that is initially set
		/// </summary>
		public string Host => host;

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkServerBase.SocketKeepAliveTime" />
		public int SocketKeepAliveTime { get; set; } = -1;


		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkDoubleBase.Port" />
		public int Port
		{
			get
			{
				if (_port.Length == 1)
				{
					return _port[0];
				}
				int num = indexPort;
				if (num < 0 || num >= _port.Length)
				{
					num = 0;
				}
				return _port[num];
			}
			set
			{
				if (_port.Length == 1)
				{
					_port[0] = value;
					return;
				}
				int num = indexPort;
				if (num < 0 || num >= _port.Length)
				{
					num = 0;
				}
				_port[num] = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的客户端用于服务器连接的套接字。<br />
		/// Gets or sets the socket currently used by the client for server connection.
		/// </summary>
		public Socket Socket
		{
			get
			{
				return socket;
			}
			set
			{
				socket = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkDoubleBase.ReceiveTimeOut" />
		public int ConnectTimeOut
		{
			get
			{
				return connectTimeOut;
			}
			set
			{
				connectTimeOut = value;
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSslNet.#ctor(System.Boolean)" />
		public PipeTcpNet()
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSslNet.#ctor(System.String,System.Int32,System.Boolean)" />
		public PipeTcpNet(string ipAddress, int port)
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Pipe.PipeSslNet.#ctor(System.Net.Sockets.Socket,System.Net.IPEndPoint,System.Boolean)" />
		public PipeTcpNet(Socket socket, IPEndPoint iPEndPoint)
		{
			Socket = socket;
			IpAddress = iPEndPoint.Address.ToString();
			Port = iPEndPoint.Port;
		}

		/// <summary>
		/// 设置多个可选的端口号信息，例如在三菱的PLC里，支持配置多个端口号，当一个网络发生异常时，立即切换端口号连接读写，提升系统的稳定性<br />
		/// Set multiple optional port number information. For example, in Mitsubishi PLC, it supports to configure multiple port numbers. 
		/// When an abnormality occurs in a network, the port number is immediately switched to connect to read and write to improve the stability of the system.
		/// </summary>
		/// <param name="ports">端口号数组信息</param>
		public void SetMultiPorts(int[] ports)
		{
			if (ports != null && ports.Length != 0)
			{
				_port = ports;
				indexPort = -1;
			}
		}

		/// <summary>
		/// 获取当前的远程连接信息，如果端口号设置了可选的数组，那么每次获取对象就会发生端口号切换的操作。<br />
		/// Get the current remote connection information. If the port number is set to an optional array, the port number switching operation will occur every time the object is obtained.
		/// </summary>
		/// <returns>远程连接的对象</returns>
		public IPEndPoint GetConnectIPEndPoint()
		{
			if (_port.Length == 1)
			{
				return new IPEndPoint(IPAddress.Parse(IpAddress), _port[0]);
			}
			ChangePorts();
			int port = _port[indexPort];
			return new IPEndPoint(IPAddress.Parse(IpAddress), port);
		}

		/// <summary>
		/// 变更当前的端口号信息，如果设置了多个端口号的话，就切换其他可用的端口<br />
		/// Change the current port number information, and if multiple port numbers are set, switch to other available ports
		/// </summary>
		private void ChangePorts()
		{
			if (_port.Length != 1)
			{
				if (indexPort < _port.Length - 1)
				{
					indexPort++;
				}
				else
				{
					indexPort = 0;
				}
			}
		}

		/// <inheritdoc />
		public override bool IsConnectError()
		{
			if (socket == null)
			{
				return true;
			}
			return base.IsConnectError();
		}

		/// <summary>
		/// 当管道打开成功的时候执行的事件，如果返回失败，则管道的打开操作返回失败
		/// </summary>
		/// <param name="socket">通信的套接字</param>
		/// <returns>是否真的打开成功</returns>
		protected virtual OperateResult OnCommunicationOpen(Socket socket)
		{
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult<bool> OpenCommunication()
		{
			if (IsConnectError())
			{
				NetSupport.CloseSocket(socket);
				IPEndPoint connectIPEndPoint = GetConnectIPEndPoint();
				OperateResult<Socket> operateResult = NetSupport.CreateSocketAndConnect(connectIPEndPoint, ConnectTimeOut, LocalBinding);
				if (operateResult.IsSuccess)
				{
					OperateResult operateResult2 = OnCommunicationOpen(operateResult.Content);
					if (!operateResult2.IsSuccess)
					{
						return operateResult2.ConvertFailed<bool>();
					}
					if (SocketKeepAliveTime > 0)
					{
						operateResult.Content.SetKeepAlive(SocketKeepAliveTime, SocketKeepAliveTime);
					}
					ResetConnectErrorCount();
					socket = operateResult.Content;
					return OperateResult.CreateSuccessResult(value: true);
				}
				return new OperateResult<bool>(-IncrConnectErrorCount(), operateResult.Message);
			}
			return OperateResult.CreateSuccessResult(value: false);
		}

		/// <inheritdoc />
		public override OperateResult CloseCommunication()
		{
			NetSupport.CloseSocket(socket);
			socket = null;
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult Send(byte[] data, int offset, int size)
		{
			OperateResult operateResult = NetSupport.SocketSend(socket, data, offset, size);
			if (!operateResult.IsSuccess && operateResult.ErrorCode == NetSupport.SocketErrorCode)
			{
				CloseCommunication();
				return new OperateResult<byte[]>(-IncrConnectErrorCount(), operateResult.Message);
			}
			return operateResult;
		}

		/// <inheritdoc />
		public override OperateResult<int> Receive(byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			OperateResult<int> operateResult = NetSupport.SocketReceive(socket, buffer, offset, length, timeOut, reportProgress);
			if (!operateResult.IsSuccess && operateResult.ErrorCode == NetSupport.SocketErrorCode)
			{
				CloseCommunication();
				return new OperateResult<int>(-IncrConnectErrorCount(), "Socket Exception -> " + operateResult.Message);
			}
			return operateResult;
		}

		/// <inheritdoc />
		public override OperateResult StartReceiveBackground(INetMessage netMessage)
		{
			if (base.UseServerActivePush)
			{
				OperateResult operateResult = socket.BeginReceiveResult(ServerSocketActivePushAsync, netMessage);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			return base.StartReceiveBackground(netMessage);
		}

		private async void ServerSocketActivePushAsync(IAsyncResult ar)
		{
			object asyncState = ar.AsyncState;
			INetMessage netMessage = asyncState as INetMessage;
			if (netMessage == null)
			{
				return;
			}
			OperateResult<int> endResult = socket.EndReceiveResult(ar);
			if (!endResult.IsSuccess)
			{
				IncrConnectErrorCount();
				return;
			}
			OperateResult<byte[]> receive = await ReceiveMessageAsync(netMessage, null, useActivePush: false).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				IncrConnectErrorCount();
				return;
			}
			OperateResult receiveAgain = socket.BeginReceiveResult(ServerSocketActivePushAsync, netMessage);
			if (!receiveAgain.IsSuccess)
			{
				IncrConnectErrorCount();
			}
			if (base.DecideWhetherQAMessageFunction != null)
			{
				if (base.DecideWhetherQAMessageFunction(this, receive))
				{
					SetBufferQA(receive.Content);
				}
			}
			else
			{
				SetBufferQA(receive.Content);
			}
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool>> OpenCommunicationAsync()
		{
			if (IsConnectError())
			{
				NetSupport.CloseSocket(socket);
				IPEndPoint endPoint = GetConnectIPEndPoint();
				OperateResult<Socket> connect = await NetSupport.CreateSocketAndConnectAsync(endPoint, ConnectTimeOut, LocalBinding).ConfigureAwait(continueOnCapturedContext: false);
				if (connect.IsSuccess)
				{
					OperateResult onOpen = OnCommunicationOpen(connect.Content);
					if (!onOpen.IsSuccess)
					{
						return onOpen.ConvertFailed<bool>();
					}
					if (SocketKeepAliveTime > 0)
					{
						connect.Content.SetKeepAlive(SocketKeepAliveTime, SocketKeepAliveTime);
					}
					ResetConnectErrorCount();
					socket = connect.Content;
					return OperateResult.CreateSuccessResult(value: true);
				}
				return new OperateResult<bool>(-IncrConnectErrorCount(), connect.Message);
			}
			return OperateResult.CreateSuccessResult(value: false);
		}

		/// <inheritdoc />
		public override async Task<OperateResult> CloseCommunicationAsync()
		{
			NetSupport.CloseSocket(socket);
			socket = null;
			return await Task.FromResult(OperateResult.CreateSuccessResult());
		}

		/// <inheritdoc />
		public override async Task<OperateResult> SendAsync(byte[] data, int offset, int size)
		{
			OperateResult send = await NetSupport.SocketSendAsync(socket, data, offset, size).ConfigureAwait(continueOnCapturedContext: false);
			if (!send.IsSuccess && send.ErrorCode == NetSupport.SocketErrorCode)
			{
				await CloseCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
				return new OperateResult<byte[]>(-IncrConnectErrorCount(), send.Message);
			}
			return send;
		}

		/// <inheritdoc />
		public override async Task<OperateResult<int>> ReceiveAsync(byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			OperateResult<int> receive = await NetSupport.SocketReceiveAsync(socket, buffer, offset, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess && receive.ErrorCode == NetSupport.SocketErrorCode)
			{
				await CloseCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
				return new OperateResult<int>(-IncrConnectErrorCount(), "Socket Exception -> " + receive.Message);
			}
			return receive;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"PipeTcpNet[{ipAddress}:{Port}]";
		}
	}
}
