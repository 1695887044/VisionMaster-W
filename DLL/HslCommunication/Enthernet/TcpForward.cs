using System;
using System.Net;
using System.Net.Sockets;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 用于转发的TCP服务类，可以用来实现TCP协议的转发功能，需要指定本机端口，服务器的ip及端口信息<br />
	/// The TCP service class used for forwarding can be used to implement the forwarding function of the TCP protocol. It is necessary to specify the local port, the server's ip and port information.
	/// </summary>
	public class TcpForward : CommunicationServer
	{
		/// <summary>
		/// 接收消息触发的委托信息
		/// </summary>
		/// <param name="session">会话对象</param>
		/// <param name="data">原始报文数据信息</param>
		public delegate void OnMessageReceivedDelegate(ForwardSession session, byte[] data);

		private string _hostIp = string.Empty;

		private int _port = 0;

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkDoubleBase.ConnectTimeOut" />
		[HslMqttApi(HttpMethod = "GET", Description = "Gets or sets the timeout for the connection, in milliseconds")]
		public virtual int ConnectTimeOut { get; set; }

		/// <summary>
		/// 获取或设置当前缓冲区的大小，以字节为单位，默认 2048<br />
		/// Gets or sets the size of the current buffer, in bytes, with a default of 2048
		/// </summary>
		public int CacheSize { get; set; } = 2048;


		/// <summary>
		/// 获取当前的用于中转数据的会话数量
		/// </summary>
		public int OnlineSessionsCount => GetPipeSessions().Length;

		/// <inheritdoc cref="F:HslCommunication.Core.Net.BinaryCommunication.LogMsgFormatBinary" />
		public bool LogMsgFormatBinary { get; set; } = true;


		/// <summary>
		/// 当接收到远程的数据触发的事件
		/// </summary>
		public event OnMessageReceivedDelegate OnRemoteMessageReceived;

		/// <summary>
		/// 当接收到客户端数据触发的事件
		/// </summary>
		public event OnMessageReceivedDelegate OnClientMessageReceive;

		/// <summary>
		/// 实例化一个TCP转发的对象，需要本机端口号，远程ip地址及远程端口号
		/// </summary>
		/// <param name="localPort">本机侦听的端口号</param>
		/// <param name="host">远程的IP地址</param>
		/// <param name="hostPort">远程的端口号信息</param>
		public TcpForward(int localPort, string host, int hostPort)
		{
			base.Port = localPort;
			_hostIp = host;
			_port = hostPort;
			ConnectTimeOut = 5000;
			base.CreatePipeSession = (CommunicationPipe m) => new ForwardSession
			{
				Communication = m
			};
		}

		/// <inheritdoc />
		protected override void ThreadPoolLogin(PipeTcpNet pipe, IPEndPoint endPoint)
		{
			base.LogNet?.WriteInfo(ToString(), $"Local client[{endPoint}] connected");
			OperateResult<Socket> operateResult = NetSupport.CreateSocketAndConnect(_hostIp, _port, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				base.LogNet?.WriteError(ToString(), "Connect server failed, local client close: " + operateResult.Message);
				pipe?.CloseCommunication();
				return;
			}
			base.LogNet?.WriteInfo(ToString(), $"Connect [{_hostIp}:{_port}] success");
			ForwardSession forwardSession = new ForwardSession(pipe, endPoint, CacheSize);
			forwardSession.ServerSocket = operateResult.Content;
			try
			{
				forwardSession.ServerSocket.BeginReceive(forwardSession.ServerBuffer, 0, forwardSession.ServerBuffer.Length, SocketFlags.None, ServerReceiveAsync, forwardSession);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteError(ToString(), "Server begin receive failed, local client close. " + ex.Message);
				forwardSession.Close();
				return;
			}
			try
			{
				PipeTcpNet pipeTcpNet = forwardSession.Communication as PipeTcpNet;
				pipeTcpNet.Socket.BeginReceive(forwardSession.BytesBuffer, 0, forwardSession.BytesBuffer.Length, SocketFlags.None, LocalReceiveAsync, forwardSession);
			}
			catch (Exception ex2)
			{
				base.LogNet?.WriteError(ToString(), "Local begin receive failed, server close. " + ex2.Message);
				forwardSession.Close();
				return;
			}
			AddSession(forwardSession);
		}

		private void ServerReceiveAsync(IAsyncResult ar)
		{
			ForwardSession forwardSession = ar.AsyncState as ForwardSession;
			if (forwardSession == null)
			{
				return;
			}
			forwardSession.HeartTime = DateTime.Now;
			int num = 0;
			try
			{
				num = forwardSession.ServerSocket.EndReceive(ar);
			}
			catch (ObjectDisposedException)
			{
				RemoveSession(forwardSession, string.Empty);
			}
			catch (Exception ex2)
			{
				RemoveSession(forwardSession, "Server socket endreceive failed: " + ex2.Message);
				return;
			}
			if (num == 0)
			{
				RemoveSession(forwardSession, $"Server socket [{_hostIp}:{_port}], local closed");
				return;
			}
			byte[] array = forwardSession.ServerBuffer.SelectBegin(num);
			try
			{
				forwardSession.ServerSocket.BeginReceive(forwardSession.ServerBuffer, 0, forwardSession.ServerBuffer.Length, SocketFlags.None, ServerReceiveAsync, forwardSession);
			}
			catch (Exception ex3)
			{
				RemoveSession(forwardSession, "Server socket beginReceive failed, local client close. " + ex3.Message);
				return;
			}
			LogBuffer("Remote->Client", array);
			this.OnRemoteMessageReceived?.Invoke(forwardSession, array);
			try
			{
				forwardSession.Communication.Send(array);
			}
			catch (Exception ex4)
			{
				RemoveSession(forwardSession, "Local send failed, server closed: " + ex4.Message);
			}
		}

		private void LocalReceiveAsync(IAsyncResult ar)
		{
			ForwardSession forwardSession = ar.AsyncState as ForwardSession;
			if (forwardSession == null)
			{
				return;
			}
			forwardSession.HeartTime = DateTime.Now;
			PipeTcpNet pipeTcpNet = forwardSession.Communication as PipeTcpNet;
			int num = 0;
			try
			{
				num = pipeTcpNet.Socket.EndReceive(ar);
			}
			catch (Exception ex)
			{
				RemoveSession(forwardSession, "local socket endreceive failed: " + ex.Message);
				return;
			}
			if (num == 0)
			{
				RemoveSession(forwardSession, $"local socket closed[{forwardSession.IpEndPoint}], server[{_hostIp}:{_port}] closed");
				return;
			}
			byte[] array = forwardSession.BytesBuffer.SelectBegin(num);
			try
			{
				pipeTcpNet.Socket.BeginReceive(forwardSession.BytesBuffer, 0, forwardSession.BytesBuffer.Length, SocketFlags.None, LocalReceiveAsync, forwardSession);
			}
			catch (Exception ex2)
			{
				RemoveSession(forwardSession, "local socket beginReceive failed, server socket close. " + ex2.Message);
				return;
			}
			LogBuffer("Client->Remote", array);
			this.OnClientMessageReceive?.Invoke(forwardSession, array);
			try
			{
				forwardSession.ServerSocket.Send(array);
			}
			catch (Exception ex3)
			{
				RemoveSession(forwardSession, "Server send failed, local closed: " + ex3.Message);
			}
		}

		private void LogBuffer(string info, byte[] buffer)
		{
			base.LogNet?.WriteInfo(ToString(), "[" + info + "] " + (LogMsgFormatBinary ? SoftBasic.ByteToHexString(buffer, ' ') : SoftBasic.GetAsciiStringRender(buffer)));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"TcpForward[{base.Port}->{_hostIp}:{_port}]";
		}
	}
}
