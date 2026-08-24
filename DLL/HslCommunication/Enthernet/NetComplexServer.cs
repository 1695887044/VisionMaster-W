using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 高性能的异步网络服务器类，适合搭建局域网聊天程序，消息推送程序
	/// </summary>
	/// <remarks>
	/// 详细的使用说明，请参照博客<a href="http://www.cnblogs.com/dathlin/p/8097897.html">http://www.cnblogs.com/dathlin/p/8097897.html</a>
	/// </remarks>
	/// <example>
	/// 此处贴上了Demo项目的服务器配置的示例代码
	/// <code lang="cs" source="TestProject\ComplexNetServer\FormServer.cs" region="NetComplexServer" title="NetComplexServer示例" />
	/// </example>
	public class NetComplexServer : NetworkServerBase
	{
		private int connectMaxClient = 10000;

		private readonly List<AppSession> appSessions = null;

		private readonly object lockSessions = null;

		/// <summary>
		/// 所支持的同时在线客户端的最大数量，默认为10000个
		/// </summary>
		public int ConnectMax
		{
			get
			{
				return connectMaxClient;
			}
			set
			{
				connectMaxClient = value;
			}
		}

		/// <summary>
		/// 获取或设置服务器是否记录客户端上下线信息，默认为true
		/// </summary>
		public bool IsSaveLogClientLineChange { get; set; } = true;


		/// <summary>
		/// 所有在线客户端的数量
		/// </summary>
		public int ClientCount => appSessions.Count;

		private Thread Thread_heart_check { get; set; } = null;


		/// <summary>
		/// 客户端的上下限状态变更时触发，仅作为在线客户端识别
		/// </summary>
		public event Action<int> AllClientsStatusChange;

		/// <summary>
		/// 当客户端上线的时候，触发此事件
		/// </summary>
		public event Action<AppSession> ClientOnline;

		/// <summary>
		/// 当客户端下线的时候，触发此事件
		/// </summary>
		public event Action<AppSession, string> ClientOffline;

		/// <summary>
		/// 当接收到文本数据的时候,触发此事件
		/// </summary>
		public event Action<AppSession, NetHandle, string> AcceptString;

		/// <summary>
		/// 当接收到字节数据的时候,触发此事件
		/// </summary>
		public event Action<AppSession, NetHandle, byte[]> AcceptByte;

		/// <summary>
		/// 实例化一个网络服务器类对象
		/// </summary>
		public NetComplexServer()
		{
			appSessions = new List<AppSession>();
			lockSessions = new object();
		}

		/// <summary>
		/// 初始化操作
		/// </summary>
		protected override void StartInitialization()
		{
			Thread_heart_check = new Thread(ThreadHeartCheck)
			{
				IsBackground = true,
				Priority = ThreadPriority.AboveNormal
			};
			Thread_heart_check.Start();
			base.StartInitialization();
		}

		/// <summary>
		/// 关闭网络时的操作
		/// </summary>
		protected override void CloseAction()
		{
			Thread_heart_check?.Abort();
			this.ClientOffline = null;
			this.ClientOnline = null;
			this.AcceptString = null;
			this.AcceptByte = null;
			lock (lockSessions)
			{
				appSessions.ForEach(delegate(AppSession m)
				{
					m.WorkSocket?.Close();
				});
			}
			base.CloseAction();
		}

		private void TcpStateUpLine(AppSession session)
		{
			lock (lockSessions)
			{
				appSessions.Add(session);
			}
			this.ClientOnline?.Invoke(session);
			this.AllClientsStatusChange?.Invoke(ClientCount);
			if (IsSaveLogClientLineChange)
			{
				base.LogNet?.WriteDebug(ToString(), $"[{session.IpEndPoint}] Name:{session?.LoginAlias} {StringResources.Language.NetClientOnline}");
			}
		}

		private void TcpStateDownLine(AppSession session, bool regular, bool logSave = true)
		{
			lock (lockSessions)
			{
				if (!appSessions.Remove(session))
				{
					return;
				}
			}
			session.WorkSocket?.Close();
			string arg = (regular ? StringResources.Language.NetClientOffline : StringResources.Language.NetClientBreak);
			this.ClientOffline?.Invoke(session, arg);
			this.AllClientsStatusChange?.Invoke(ClientCount);
			if (IsSaveLogClientLineChange && logSave)
			{
				base.LogNet?.WriteInfo(ToString(), $"[{session.IpEndPoint}] Name:{session?.LoginAlias} {arg}");
			}
		}

		/// <summary>
		/// 让客户端正常下线，调用本方法即可自由控制会话客户端强制下线操作。
		/// </summary>
		/// <param name="session">会话对象</param>
		public void AppSessionRemoteClose(AppSession session)
		{
			TcpStateDownLine(session, regular: true);
		}

		/// <summary>
		/// 当接收到了新的请求的时候执行的操作
		/// </summary>
		/// <param name="socket">异步对象</param>
		/// <param name="endPoint">终结点</param>
		protected override void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			if (appSessions.Count > ConnectMax)
			{
				socket?.Close();
				base.LogNet?.WriteWarn(ToString(), StringResources.Language.NetClientFull);
				return;
			}
			OperateResult<int, string> operateResult = ReceiveStringContentFromSocket(socket);
			if (operateResult.IsSuccess)
			{
				AppSession appSession = new AppSession(socket)
				{
					LoginAlias = operateResult.Content2
				};
				try
				{
					appSession.WorkSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallback, appSession);
					TcpStateUpLine(appSession);
					HslHelper.ThreadSleep(20);
				}
				catch (Exception ex)
				{
					appSession.WorkSocket?.Close();
					base.LogNet?.WriteException(ToString(), StringResources.Language.NetClientLoginFailed, ex);
				}
			}
		}

		private async void ReceiveCallback(IAsyncResult ar)
		{
			object asyncState = ar.AsyncState;
			AppSession appSession = asyncState as AppSession;
			if (appSession == null)
			{
				return;
			}
			try
			{
				appSession.WorkSocket.EndReceive(ar);
			}
			catch
			{
				TcpStateDownLine(appSession, regular: false);
				return;
			}
			OperateResult<int, int, byte[]> read = await ReceiveHslMessageAsync(appSession.WorkSocket);
			if (!read.IsSuccess)
			{
				TcpStateDownLine(appSession, regular: false);
				return;
			}
			try
			{
				appSession.WorkSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallback, appSession);
			}
			catch
			{
				TcpStateDownLine(appSession, regular: false);
				return;
			}
			int protocol = read.Content1;
			int customer = read.Content2;
			byte[] content = read.Content3;
			switch (protocol)
			{
			case 1:
				BitConverter.GetBytes(DateTime.Now.Ticks).CopyTo(content, 8);
				base.LogNet?.WriteDebug(ToString(), $"Heart Check From {appSession.IpEndPoint}");
				if (Send(appSession.WorkSocket, HslProtocol.CommandBytes(1, customer, base.Token, content)).IsSuccess)
				{
					appSession.UpdateHeartTime();
				}
				break;
			case 2:
				TcpStateDownLine(appSession, regular: true);
				break;
			case 1002:
				this.AcceptByte?.Invoke(appSession, customer, content);
				break;
			case 1001:
			{
				string str = Encoding.Unicode.GetString(content);
				this.AcceptString?.Invoke(appSession, customer, str);
				break;
			}
			}
		}

		/// <summary>
		/// 服务器端用于数据发送文本的方法
		/// </summary>
		/// <param name="session">数据发送对象</param>
		/// <param name="customer">用户自定义的数据对象，如不需要，赋值为0</param>
		/// <param name="str">发送的文本</param>
		public void Send(AppSession session, NetHandle customer, string str)
		{
			Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, str));
		}

		/// <summary>
		/// 服务器端用于发送字节的方法
		/// </summary>
		/// <param name="session">数据发送对象</param>
		/// <param name="customer">用户自定义的数据对象，如不需要，赋值为0</param>
		/// <param name="bytes">实际发送的数据</param>
		public void Send(AppSession session, NetHandle customer, byte[] bytes)
		{
			Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, bytes));
		}

		/// <summary>
		/// 服务端用于发送所有数据到所有的客户端
		/// </summary>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="str">需要传送的实际的数据</param>
		public void SendAllClients(NetHandle customer, string str)
		{
			lock (lockSessions)
			{
				for (int i = 0; i < appSessions.Count; i++)
				{
					Send(appSessions[i], customer, str);
				}
			}
		}

		/// <summary>
		/// 服务端用于发送所有数据到所有的客户端
		/// </summary>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="data">需要群发客户端的字节数据</param>
		public void SendAllClients(NetHandle customer, byte[] data)
		{
			lock (lockSessions)
			{
				for (int i = 0; i < appSessions.Count; i++)
				{
					Send(appSessions[i], customer, data);
				}
			}
		}

		/// <summary>
		/// 根据客户端设置的别名进行发送消息
		/// </summary>
		/// <param name="Alias">客户端上线的别名</param>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="str">需要传送的实际的数据</param>
		public void SendClientByAlias(string Alias, NetHandle customer, string str)
		{
			lock (lockSessions)
			{
				for (int i = 0; i < appSessions.Count; i++)
				{
					if (appSessions[i].LoginAlias == Alias)
					{
						Send(appSessions[i], customer, str);
					}
				}
			}
		}

		/// <summary>
		/// 根据客户端设置的别名进行发送消息
		/// </summary>
		/// <param name="Alias">客户端上线的别名</param>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="data">需要传送的实际的数据</param>
		public void SendClientByAlias(string Alias, NetHandle customer, byte[] data)
		{
			lock (lockSessions)
			{
				for (int i = 0; i < appSessions.Count; i++)
				{
					if (appSessions[i].LoginAlias == Alias)
					{
						Send(appSessions[i], customer, data);
					}
				}
			}
		}

		private void ThreadHeartCheck()
		{
			do
			{
				HslHelper.ThreadSleep(2000);
				try
				{
					AppSession[] array = null;
					lock (lockSessions)
					{
						array = appSessions.ToArray();
					}
					for (int num = array.Length - 1; num >= 0; num--)
					{
						if (array[num] != null && (DateTime.Now - array[num].HeartTime).TotalSeconds > 30.0)
						{
							base.LogNet?.WriteWarn(ToString(), StringResources.Language.NetHeartCheckTimeout + array[num].IpAddress.ToString());
							TcpStateDownLine(array[num], regular: false, logSave: false);
						}
					}
				}
				catch (Exception ex)
				{
					base.LogNet?.WriteException(ToString(), StringResources.Language.NetHeartCheckFailed, ex);
				}
			}
			while (base.IsStarted);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetComplexServer[{base.Port}]";
		}
	}
}
