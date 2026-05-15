using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 一个基于异步高性能的客户端网络类，支持主动接收服务器的消息
	/// </summary>
	/// <remarks>
	/// 详细的使用说明，请参照博客<a href="http://www.cnblogs.com/dathlin/p/7697782.html">http://www.cnblogs.com/dathlin/p/7697782.html</a>
	/// </remarks>
	/// <example>
	/// 此处贴上了Demo项目的服务器配置的示例代码
	/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormComplexNet.cs" region="NetComplexClient" title="NetComplexClient示例" />
	/// </example>
	public class NetComplexClient : NetworkXBase
	{
		private AppSession session;

		private int isConnecting = 0;

		private bool closed = false;

		private Thread thread_heart_check = null;

		/// <summary>
		/// 客户端系统是否启动
		/// </summary>
		public bool IsClientStart { get; set; }

		/// <summary>
		/// 重连接失败的次数
		/// </summary>
		public int ConnectFailedCount { get; private set; }

		/// <summary>
		/// 客户端登录的标识名称，可以为ID号，也可以为登录名
		/// </summary>
		public string ClientAlias { get; set; } = string.Empty;


		/// <summary>
		/// 远程服务器的IP地址和端口
		/// </summary>
		public IPEndPoint EndPointServer { get; set; }

		/// <summary>
		/// 服务器的时间，自动实现和服务器同步
		/// </summary>
		public DateTime ServerTime { get; private set; }

		/// <summary>
		/// 系统与服务器的延时时间，单位毫秒
		/// </summary>
		public int DelayTime { get; private set; }

		/// <summary>
		/// 客户端启动成功的事件，重连成功也将触发此事件
		/// </summary>
		public event Action LoginSuccess;

		/// <summary>
		/// 连接失败时触发的事件
		/// </summary>
		public event Action<int> LoginFailed;

		/// <summary>
		/// 服务器的异常，启动，等等一般消息产生的时候，出发此事件
		/// </summary>
		public event Action<string> MessageAlerts;

		/// <summary>
		/// 在客户端断开后并在重连服务器之前触发，用于清理系统资源
		/// </summary>
		public event Action BeforReConnected;

		/// <summary>
		/// 当接收到文本数据的时候,触发此事件
		/// </summary>
		public event Action<AppSession, NetHandle, string> AcceptString;

		/// <summary>
		/// 当接收到字节数据的时候,触发此事件
		/// </summary>
		public event Action<AppSession, NetHandle, byte[]> AcceptByte;

		/// <summary>
		/// 实例化一个对象
		/// </summary>
		public NetComplexClient()
		{
			session = new AppSession();
			ServerTime = DateTime.Now;
			EndPointServer = new IPEndPoint(IPAddress.Any, 0);
		}

		/// <summary>
		/// 关闭该客户端引擎
		/// </summary>
		public void ClientClose()
		{
			closed = true;
			if (IsClientStart)
			{
				Send(session.WorkSocket, HslProtocol.CommandBytes(2, 0, base.Token, null));
			}
			IsClientStart = false;
			thread_heart_check = null;
			this.LoginSuccess = null;
			this.LoginFailed = null;
			this.MessageAlerts = null;
			this.AcceptByte = null;
			this.AcceptString = null;
			HslHelper.ThreadSleep(20);
			session.WorkSocket?.Close();
			base.LogNet?.WriteDebug(ToString(), "Client Close.");
		}

		/// <summary>
		/// 启动客户端引擎，连接服务器系统
		/// </summary>
		public void ClientStart()
		{
			if (Interlocked.CompareExchange(ref isConnecting, 1, 0) == 0)
			{
				Thread thread = new Thread(ThreadLogin);
				thread.IsBackground = true;
				thread.Start();
				if (thread_heart_check == null)
				{
					thread_heart_check = new Thread(ThreadHeartCheck)
					{
						Priority = ThreadPriority.AboveNormal,
						IsBackground = true
					};
					thread_heart_check.Start();
				}
			}
		}

		/// <summary>
		/// 连接服务器之前的消息提示，如果是重连的话，就提示10秒等待信息
		/// </summary>
		private void AwaitToConnect()
		{
			if (ConnectFailedCount == 0)
			{
				this.MessageAlerts?.Invoke(StringResources.Language.ConnectingServer);
				return;
			}
			int num = 10;
			while (num > 0)
			{
				if (closed)
				{
					return;
				}
				num--;
				this.MessageAlerts?.Invoke(string.Format(StringResources.Language.ConnectFailedAndWait, num));
				HslHelper.ThreadSleep(1000);
			}
			this.MessageAlerts?.Invoke(string.Format(StringResources.Language.AttemptConnectServer, ConnectFailedCount));
		}

		private void ConnectFailed()
		{
			ConnectFailedCount++;
			Interlocked.Exchange(ref isConnecting, 0);
			this.LoginFailed?.Invoke(ConnectFailedCount);
			base.LogNet?.WriteDebug(ToString(), "Connected Failed, Times: " + ConnectFailedCount);
		}

		private OperateResult<Socket> ConnectServer()
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(EndPointServer, 10000);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 1, ClientAlias);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<Socket>(operateResult2);
			}
			this.MessageAlerts?.Invoke(StringResources.Language.ConnectServerSuccess);
			return operateResult;
		}

		private void LoginSuccessMethod(Socket socket)
		{
			ConnectFailedCount = 0;
			try
			{
				session.UpdateSocket(socket);
				session.LoginAlias = ClientAlias;
				session.UpdateHeartTime();
				IsClientStart = true;
				session.WorkSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallback, session);
			}
			catch
			{
				ThreadPool.QueueUserWorkItem(ReconnectServer, null);
			}
		}

		private void ThreadLogin()
		{
			AwaitToConnect();
			OperateResult<Socket> operateResult = ConnectServer();
			if (!operateResult.IsSuccess)
			{
				ConnectFailed();
				ThreadPool.QueueUserWorkItem(ReconnectServer, null);
				return;
			}
			LoginSuccessMethod(operateResult.Content);
			this.LoginSuccess?.Invoke();
			Interlocked.Exchange(ref isConnecting, 0);
			HslHelper.ThreadSleep(200);
		}

		private void ReconnectServer(object obj = null)
		{
			if (isConnecting != 1 && !closed)
			{
				this.BeforReConnected?.Invoke();
				session?.WorkSocket?.Close();
				ClientStart();
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
				ThreadPool.QueueUserWorkItem(ReconnectServer, null);
				return;
			}
			OperateResult<int, int, byte[]> read = await ReceiveHslMessageAsync(appSession.WorkSocket);
			if (!read.IsSuccess)
			{
				ThreadPool.QueueUserWorkItem(ReconnectServer, null);
				return;
			}
			try
			{
				appSession.WorkSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallback, appSession);
			}
			catch
			{
				ThreadPool.QueueUserWorkItem(ReconnectServer, null);
				return;
			}
			int protocol = read.Content1;
			int customer = read.Content2;
			byte[] content = read.Content3;
			switch (protocol)
			{
			case 1:
			{
				DateTime dt = new DateTime(BitConverter.ToInt64(content, 0));
				ServerTime = new DateTime(BitConverter.ToInt64(content, 8));
				DelayTime = (int)(DateTime.Now - dt).TotalMilliseconds;
				session.UpdateHeartTime();
				break;
			}
			case 1002:
				this.AcceptByte?.Invoke(session, customer, content);
				break;
			case 1001:
			{
				string str = Encoding.Unicode.GetString(content);
				this.AcceptString?.Invoke(session, customer, str);
				break;
			}
			}
		}

		/// <summary>
		/// 服务器端用于数据发送文本的方法
		/// </summary>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="str">发送的文本</param>
		public void Send(NetHandle customer, string str)
		{
			if (IsClientStart)
			{
				Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, str));
			}
		}

		/// <summary>
		/// 服务器端用于发送字节的方法
		/// </summary>
		/// <param name="customer">用户自定义的命令头</param>
		/// <param name="bytes">实际发送的数据</param>
		public void Send(NetHandle customer, byte[] bytes)
		{
			if (IsClientStart)
			{
				Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, bytes));
			}
		}

		/// <summary>
		/// 心跳线程的方法
		/// </summary>
		private void ThreadHeartCheck()
		{
			HslHelper.ThreadSleep(2000);
			while (true)
			{
				HslHelper.ThreadSleep(10000);
				if (closed)
				{
					break;
				}
				byte[] array = new byte[16];
				BitConverter.GetBytes(DateTime.Now.Ticks).CopyTo(array, 0);
				Send(session.WorkSocket, HslProtocol.CommandBytes(1, 0, base.Token, array));
				double totalSeconds = (DateTime.Now - session.HeartTime).TotalSeconds;
				if (totalSeconds > 30.0)
				{
					if (isConnecting == 0)
					{
						base.LogNet?.WriteDebug(ToString(), $"Heart Check Failed int {totalSeconds} Seconds.");
						ReconnectServer();
					}
					if (!closed)
					{
						HslHelper.ThreadSleep(1000);
					}
				}
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetComplexClient[{EndPointServer}]";
		}
	}
}
