using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HslCommunication.Core.Pipe;
using HslCommunication.LogNet;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 仅仅包含 TCP 服务器
	/// </summary>
	public class CommunicationTcpServer
	{
		private List<string> TrustedClients = null;

		private bool IsTrustedClientsOnly = false;

		private object lock_trusted_clients = new object();

		/// <summary>
		/// 核心的socket服务器
		/// </summary>
		protected Socket socketServer = null;

		private bool useSSL = false;

		private X509Certificate certificate;

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.IsStarted" />
		public bool IsStarted { get; protected set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.Port" />
		public int Port { get; set; } = 10000;


		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.EnableIPv6" />
		public bool EnableIPv6 { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceServer.SocketKeepAliveTime" />
		public int SocketKeepAliveTime { get; set; } = -1;


		/// <inheritdoc cref="P:HslCommunication.Core.Net.BinaryCommunication.LogNet" />
		public ILogNet LogNet { get; set; }

		/// <summary>
		/// 记录一些调试日志的委托，将会进行输出调试文本。<br />
		/// The delegate that records some debug logs will output debug text.
		/// </summary>
		public Action<string> LogDebugMessage { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public CommunicationTcpServer()
		{
		}

		/// <summary>
		/// 使用SSL通信，传递一个证书的对象
		/// </summary>
		/// <param name="cert">证书对象</param>
		public void UseSSL(X509Certificate cert)
		{
			useSSL = true;
			certificate = cert;
		}

		/// <summary>
		/// 使用SSL通信，传递一个证书的路径，以及证书的密码
		/// </summary>
		/// <param name="cert">传递一个证书</param>
		/// <param name="password">证书的密码</param>
		public void UseSSL(string cert, string password = "")
		{
			useSSL = true;
			certificate = (string.IsNullOrEmpty(password) ? new X509Certificate(cert) : new X509Certificate(cert, password));
		}

		/// <summary>
		/// 指定端口号来启动服务器的引擎<br />
		/// Specify the port number to start the server's engine
		/// </summary>
		/// <param name="port">指定一个端口号</param>
		public void ServerStart(int port)
		{
			if (!IsStarted)
			{
				if (!EnableIPv6)
				{
					socketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					socketServer.Bind(new IPEndPoint(IPAddress.Any, port));
				}
				else
				{
					socketServer = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
					socketServer.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
				}
				socketServer.Listen(500);
				socketServer.BeginAccept(AsyncAcceptCallback, socketServer);
				IsStarted = true;
				Port = port;
				ExtraOnStart();
				LogDebugMsg(StringResources.Language.NetEngineStart);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.CommunicationTcpServer.ServerStart(System.Int32)" />
		public void ServerStart()
		{
			ServerStart(Port);
		}

		/// <summary>
		/// 关闭服务器的引擎<br />
		/// Shut down the server's engine
		/// </summary>
		public void ServerClose()
		{
			if (IsStarted)
			{
				IsStarted = false;
				ExtraOnClose();
				NetSupport.CloseSocket(socketServer);
				LogDebugMsg(StringResources.Language.NetEngineClose);
			}
		}

		/// <summary>
		/// 设置并启动受信任的客户端登录并读写，如果为null，将关闭对客户端的ip验证<br />
		/// Set and start the trusted client login and read and write, if it is null, the client's IP verification will be turned off
		/// </summary>
		/// <param name="clients">受信任的客户端列表</param>
		public void SetTrustedIpAddress(List<string> clients)
		{
			lock (lock_trusted_clients)
			{
				if (clients != null && clients.Count > 0)
				{
					TrustedClients = clients.Select((string m) => HslHelper.GetIpAddressFromInput(m)).ToList();
					IsTrustedClientsOnly = true;
				}
				else
				{
					TrustedClients = new List<string>();
					IsTrustedClientsOnly = false;
				}
			}
		}

		/// <summary>
		/// 检查该Ip地址是否是受信任的<br />
		/// Check if the IP address is trusted
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		/// <returns>是受信任的返回<c>True</c>，否则返回<c>False</c></returns>
		private bool CheckIpAddressTrusted(string ipAddress)
		{
			if (IsTrustedClientsOnly)
			{
				bool result = false;
				lock (lock_trusted_clients)
				{
					for (int i = 0; i < TrustedClients.Count; i++)
					{
						if (TrustedClients[i] == ipAddress)
						{
							result = true;
							break;
						}
					}
				}
				return result;
			}
			return false;
		}

		/// <summary>
		/// 获取受信任的客户端列表<br />
		/// Get a list of trusted clients
		/// </summary>
		/// <returns>字符串数据信息</returns>
		public string[] GetTrustedClients()
		{
			string[] result = new string[0];
			lock (lock_trusted_clients)
			{
				if (TrustedClients != null)
				{
					result = TrustedClients.ToArray();
				}
			}
			return result;
		}

		private void AsyncAcceptCallback(IAsyncResult iar)
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
				LogDebugMsg(StringResources.Language.SocketAcceptCallbackException + " " + ex2.Message);
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
					HslHelper.ThreadSleep(100);
					LogDebugMsg(StringResources.Language.SocketReAcceptCallbackException + " " + ex3.Message);
					num++;
					continue;
				}
				break;
			}
			if (num >= 3)
			{
				LogDebugMsg(StringResources.Language.SocketReAcceptCallbackException);
				throw new Exception(StringResources.Language.SocketReAcceptCallbackException);
			}
		}

		private void ThreadPoolLogin(object obj)
		{
			Socket socket = obj as Socket;
			if (socket == null)
			{
				return;
			}
			IPEndPoint iPEndPoint = (IPEndPoint)socket.RemoteEndPoint;
			if (IsTrustedClientsOnly)
			{
				string ipAddress = ((iPEndPoint == null) ? string.Empty : iPEndPoint.Address.ToString());
				if (!CheckIpAddressTrusted(ipAddress))
				{
					LogDebugMsg(string.Format(StringResources.Language.ClientDisableLogin, iPEndPoint));
					NetSupport.CloseSocket(socket);
					return;
				}
			}
			OperateResult operateResult = SocketAcceptExtraCheck(socket, iPEndPoint);
			if (!operateResult.IsSuccess)
			{
				LogDebugMsg($"Client <{iPEndPoint}> SocketAcceptExtraCheck failed: {operateResult.Message}");
				NetSupport.CloseSocket(socket);
				return;
			}
			PipeTcpNet pipeTcpNet = null;
			if (useSSL)
			{
				PipeSslNet pipeSslNet = new PipeSslNet(socket, iPEndPoint, serverMode: true);
				pipeSslNet.Certificate = certificate;
				OperateResult<SslStream> operateResult2 = pipeSslNet.CreateSslStream(socket, createNew: true);
				if (!operateResult2.IsSuccess)
				{
					pipeSslNet.CloseCommunication();
					LogNet?.WriteDebug(ToString(), $"[{iPEndPoint}] WebScoket SSL Check Failed:" + operateResult2.Message);
					return;
				}
				pipeTcpNet = pipeSslNet;
			}
			else
			{
				pipeTcpNet = new PipeTcpNet(socket, iPEndPoint);
			}
			ThreadPoolLogin(pipeTcpNet, iPEndPoint);
		}

		/// <summary>
		/// 当客户端连接到服务器，并听过额外的检查后，进行回调的方法<br />
		/// Callback method when the client connects to the server and has heard additional checks
		/// </summary>
		/// <param name="pipe">socket对象</param>
		/// <param name="endPoint">远程的终结点</param>
		protected virtual void ThreadPoolLogin(PipeTcpNet pipe, IPEndPoint endPoint)
		{
		}

		/// <summary>
		/// 关闭的时候额外执行的功能代码
		/// </summary>
		protected virtual void ExtraOnClose()
		{
		}

		/// <summary>
		/// 服务器启动的时候额外执行的功能代码
		/// </summary>
		protected virtual void ExtraOnStart()
		{
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
		/// 记录当前的日志信息
		/// </summary>
		/// <param name="message">消息文本</param>
		protected void LogDebugMsg(string message)
		{
			LogNet?.WriteDebug(ToString(), message);
			LogDebugMessage?.Invoke(message);
		}
	}
}
