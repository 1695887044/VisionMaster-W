using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.Core.IMessage;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 服务器程序的基础类，提供了启动服务器的基本实现，方便后续的扩展操作。<br />
	/// The basic class of the server program provides the basic implementation of starting the server to facilitate subsequent expansion operations.
	/// </summary>
	public class NetworkServerBase : NetworkXBase
	{
		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.IsStarted" />
		public bool IsStarted { get; protected set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.Port" />
		public int Port { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.EnableIPv6" />
		public bool EnableIPv6 { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.SocketKeepAliveTime" />
		public int SocketKeepAliveTime { get; set; } = -1;


		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public NetworkServerBase()
		{
			IsStarted = false;
			Port = 0;
		}

		/// <summary>
		/// 异步传入的连接申请请求<br />
		/// Asynchronous incoming connection request
		/// </summary>
		/// <param name="iar">异步对象</param>
		protected void AsyncAcceptCallback(IAsyncResult iar)
		{
			Socket socket = iar.AsyncState as Socket;
			if (socket == null)
			{
				return;
			}
			Socket socket2 = null;
			try
			{
				socket2 = socket.EndAccept(iar);
				if (SocketKeepAliveTime > 0)
				{
					socket2.SetKeepAlive(SocketKeepAliveTime, SocketKeepAliveTime);
				}
				ThreadPool.QueueUserWorkItem(ThreadPoolLogin, socket2);
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (Exception ex2)
			{
				NetSupport.CloseSocket(socket2);
				base.LogNet?.WriteException(ToString(), StringResources.Language.SocketAcceptCallbackException, ex2);
			}
			int num = 0;
			while (num < 3)
			{
				try
				{
					socket.BeginAccept(AsyncAcceptCallback, socket);
				}
				catch (Exception ex3)
				{
					HslHelper.ThreadSleep(1000);
					base.LogNet?.WriteException(ToString(), StringResources.Language.SocketReAcceptCallbackException, ex3);
					num++;
					continue;
				}
				break;
			}
			if (num >= 3)
			{
				base.LogNet?.WriteError(ToString(), StringResources.Language.SocketReAcceptCallbackException);
				throw new Exception(StringResources.Language.SocketReAcceptCallbackException);
			}
		}

		private void ThreadPoolLogin(object obj)
		{
			Socket socket = obj as Socket;
			if (socket != null)
			{
				IPEndPoint iPEndPoint = (IPEndPoint)socket.RemoteEndPoint;
				OperateResult operateResult = SocketAcceptExtraCheck(socket, iPEndPoint);
				if (!operateResult.IsSuccess)
				{
					base.LogNet?.WriteDebug(ToString(), $"[{iPEndPoint}] Socket Accept Extra Check Failed : {operateResult.Message}");
					socket?.Close();
				}
				else
				{
					ThreadPoolLogin(socket, iPEndPoint);
				}
			}
		}

		/// <summary>
		/// 当客户端连接到服务器，并听过额外的检查后，进行回调的方法<br />
		/// Callback method when the client connects to the server and has heard additional checks
		/// </summary>
		/// <param name="socket">socket对象</param>
		/// <param name="endPoint">远程的终结点</param>
		protected virtual void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			NetSupport.CloseSocket(socket);
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

		/// <summary>
		/// 指定端口号来启动服务器的引擎<br />
		/// Specify the port number to start the server's engine
		/// </summary>
		/// <param name="port">指定一个端口号</param>
		public virtual void ServerStart(int port)
		{
			if (!IsStarted)
			{
				StartInitialization();
				if (!EnableIPv6)
				{
					CoreSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					CoreSocket.Bind(new IPEndPoint(IPAddress.Any, port));
				}
				else
				{
					CoreSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
					CoreSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
				}
				CoreSocket.Listen(500);
				CoreSocket.BeginAccept(AsyncAcceptCallback, CoreSocket);
				IsStarted = true;
				Port = port;
				base.LogNet?.WriteInfo(ToString(), StringResources.Language.NetEngineStart);
			}
		}

		/// <summary>
		/// 使用已经配置好的端口启动服务器的引擎<br />
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

		/// <summary>
		/// 关闭服务器的引擎<br />
		/// Shut down the server's engine
		/// </summary>
		public virtual void ServerClose()
		{
			if (IsStarted)
			{
				IsStarted = false;
				CloseAction();
				NetSupport.CloseSocket(CoreSocket);
				base.LogNet?.WriteInfo(ToString(), StringResources.Language.NetEngineClose);
			}
		}

		private byte[] CreateHslAlienMessage(string dtuId, string password)
		{
			if (dtuId.Length > 11)
			{
				dtuId = dtuId.Substring(11);
			}
			byte[] array = new byte[28]
			{
				72, 115, 110, 0, 23, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0
			};
			if (dtuId.Length > 11)
			{
				dtuId = dtuId.Substring(0, 11);
			}
			Encoding.ASCII.GetBytes(dtuId).CopyTo(array, 5);
			if (!string.IsNullOrEmpty(password))
			{
				if (password.Length > 6)
				{
					password = password.Substring(6);
				}
				Encoding.ASCII.GetBytes(password).CopyTo(array, 16);
			}
			return array;
		}

		/// <summary>
		/// 创建一个指定的异形客户端连接，使用Hsl协议来发送注册包<br />
		/// Create a specified profiled client connection and use the Hsl protocol to send registration packets
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		/// <param name="dtuId">设备唯一ID号，最长11</param>
		/// <param name="password">密码信息</param>
		/// <returns>是否成功连接</returns>
		public OperateResult ConnectHslAlientClient(string ipAddress, int port, string dtuId, string password = "")
		{
			byte[] data = CreateHslAlienMessage(dtuId, password);
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ipAddress, port, 10000);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = Send(operateResult.Content, data);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = ReceiveByMessage(operateResult.Content, 10000, new AlienMessage());
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			switch (operateResult3.Content[5])
			{
			case 1:
				operateResult.Content?.Close();
				return new OperateResult(StringResources.Language.DeviceCurrentIsLoginRepeat);
			case 2:
				operateResult.Content?.Close();
				return new OperateResult(StringResources.Language.DeviceCurrentIsLoginForbidden);
			case 3:
				operateResult.Content?.Close();
				return new OperateResult(StringResources.Language.PasswordCheckFailed);
			default:
				ThreadPoolLogin(operateResult.Content);
				return OperateResult.CreateSuccessResult();
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkServerBase.ConnectHslAlientClient(System.String,System.Int32,System.String,System.String)" />
		public async Task<OperateResult> ConnectHslAlientClientAsync(string ipAddress, int port, string dtuId, string password = "")
		{
			byte[] sendBytes = CreateHslAlienMessage(dtuId, password);
			OperateResult<Socket> connect = await CreateSocketAndConnectAsync(ipAddress, port, 10000);
			if (!connect.IsSuccess)
			{
				return connect;
			}
			OperateResult send = await SendAsync(connect.Content, sendBytes);
			if (!send.IsSuccess)
			{
				return send;
			}
			OperateResult<byte[]> receive = await ReceiveByMessageAsync(connect.Content, 10000, new AlienMessage());
			if (!receive.IsSuccess)
			{
				return receive;
			}
			switch (receive.Content[5])
			{
			case 1:
				connect.Content?.Close();
				return new OperateResult(StringResources.Language.DeviceCurrentIsLoginRepeat);
			case 2:
				connect.Content?.Close();
				return new OperateResult(StringResources.Language.DeviceCurrentIsLoginForbidden);
			case 3:
				connect.Content?.Close();
				return new OperateResult(StringResources.Language.PasswordCheckFailed);
			default:
				ThreadPoolLogin(connect.Content);
				return OperateResult.CreateSuccessResult();
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetworkServerBase[{Port}]";
		}
	}
}
