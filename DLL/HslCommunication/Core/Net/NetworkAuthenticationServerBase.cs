using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HslCommunication.BasicFramework;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 带登录认证的服务器类，可以对连接的客户端进行筛选，放行用户名密码正确的连接，并支持对在线的客户端对象进行管理<br />
	/// The server class with login authentication can filter connected clients, allow connections with correct username and password, and support online client objects
	/// </summary>
	public class NetworkAuthenticationServerBase : NetworkServerBase, IDisposable
	{
		/// <summary>
		/// 表示客户端状态变化的委托信息<br />
		/// Delegate information representing the state change of the client
		/// </summary>
		/// <param name="server">当前的服务器对象信息</param>
		/// <param name="session">当前的客户端会话信息</param>
		public delegate void OnClientStatusChangeDelegate(object server, AppSession session);

		private Dictionary<string, string> accounts = new Dictionary<string, string>();

		private SimpleHybirdLock lockLoginAccount = new SimpleHybirdLock();

		private List<string> TrustedClients = null;

		private bool IsTrustedClientsOnly = false;

		private SimpleHybirdLock lock_trusted_clients;

		private Timer timerHeart;

		private List<AppSession> listsOnlineClient;

		private object lockOnlineClient;

		private int onlineCount = 0;

		private bool disposedValue = false;

		/// <summary>
		/// 获取或设置是否对客户端启动账号认证<br />
		/// Gets or sets whether to enable account authentication on the client
		/// </summary>
		public bool IsUseAccountCertificate { get; set; }

		/// <summary>
		/// 获取在线的客户端的数量<br />
		/// Get the number of clients online
		/// </summary>
		public int OnlineCount => onlineCount;

		/// <summary>
		/// 获取当前所有在线的客户端信息，包括IP地址和端口号信息<br />
		/// Get all current online client information, including IP address and port number information
		/// </summary>
		public AppSession[] GetOnlineSessions
		{
			get
			{
				lock (lockOnlineClient)
				{
					return listsOnlineClient.ToArray();
				}
			}
		}

		/// <summary>
		/// 获取或设置两次数据交互时的最小时间间隔，默认为24小时。如果超过该设定的时间不进行数据交互，服务器就会强制断开当前的连接操作。<br />
		/// Get or set the minimum time interval between two data interactions, the default is 24 hours. 
		/// If the data exchange is not performed for more than the set time, the server will forcibly disconnect the current connection operation.
		/// </summary>
		/// <remarks>
		/// 举例设置为10分钟，ActiveTimeSpan = TimeSpan.FromMinutes( 10 );
		/// </remarks>
		public TimeSpan ActiveTimeSpan { get; set; }

		/// <summary>
		/// 当客户端上线时候的触发的事件<br />
		/// Event triggered when the client goes online
		/// </summary>
		public event OnClientStatusChangeDelegate OnClientOnline;

		/// <summary>
		/// 当客户端下线时候的触发的事件<br />
		/// Event triggered when the client goes offline
		/// </summary>
		public event OnClientStatusChangeDelegate OnClientOffline;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// instantiate a default object
		/// </summary>
		public NetworkAuthenticationServerBase()
		{
			lock_trusted_clients = new SimpleHybirdLock();
			lockOnlineClient = new object();
			listsOnlineClient = new List<AppSession>();
			timerHeart = new Timer(ThreadTimerHeartCheck, null, 2000, 10000);
			ActiveTimeSpan = TimeSpan.FromHours(24.0);
		}

		/// <summary>
		/// 当客户端的socket登录的时候额外检查的信息，检查当前会话的用户名和密码<br />
		/// Additional check information when the client's socket logs in, check the username and password of the current session
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="endPoint">终结点</param>
		/// <returns>验证的结果</returns>
		protected override OperateResult SocketAcceptExtraCheck(Socket socket, IPEndPoint endPoint)
		{
			if (IsUseAccountCertificate)
			{
				OperateResult<byte[], byte[]> operateResult = ReceiveAndCheckBytes(socket, 2000);
				if (!operateResult.IsSuccess)
				{
					return new OperateResult($"Client login failed[{endPoint}]");
				}
				if (BitConverter.ToInt32(operateResult.Content1, 0) != 5)
				{
					base.LogNet?.WriteError(ToString(), StringResources.Language.NetClientAccountTimeout);
					socket?.Close();
					return new OperateResult($"Authentication failed[{endPoint}]");
				}
				string[] array = HslProtocol.UnPackStringArrayFromByte(operateResult.Content2);
				string text = CheckAccountLegal(array);
				SendStringAndCheckReceive(socket, (text == "success") ? 1 : 0, new string[1] { text });
				if (text != "success")
				{
					return new OperateResult($"Client login failed[{endPoint}]:{text} {SoftBasic.ArrayFormat(array)}");
				}
				base.LogNet?.WriteDebug(ToString(), $"Account Login:{array[0]} Endpoint:[{endPoint}]");
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 新增账户，如果想要启动账户登录，必须将<see cref="P:HslCommunication.Core.Net.NetworkAuthenticationServerBase.IsUseAccountCertificate" />设置为<c>True</c>。<br />
		/// Add an account. If you want to activate account login, you must set <see cref="P:HslCommunication.Core.Net.NetworkAuthenticationServerBase.IsUseAccountCertificate" /> to <c> True </c>
		/// </summary>
		/// <param name="userName">账户名称</param>
		/// <param name="password">账户名称</param>
		public void AddAccount(string userName, string password)
		{
			if (!string.IsNullOrEmpty(userName))
			{
				lockLoginAccount.Enter();
				if (accounts.ContainsKey(userName))
				{
					accounts[userName] = password;
				}
				else
				{
					accounts.Add(userName, password);
				}
				lockLoginAccount.Leave();
			}
		}

		/// <summary>
		/// 删除一个账户的信息<br />
		/// Delete an account's information
		/// </summary>
		/// <param name="userName">账户名称</param>
		public void DeleteAccount(string userName)
		{
			lockLoginAccount.Enter();
			if (accounts.ContainsKey(userName))
			{
				accounts.Remove(userName);
			}
			lockLoginAccount.Leave();
		}

		private string CheckAccountLegal(string[] infos)
		{
			if (infos != null && infos.Length < 2)
			{
				return "User Name input wrong";
			}
			string text = "";
			lockLoginAccount.Enter();
			text = ((!accounts.ContainsKey(infos[0])) ? "User Name input wrong" : ((!(accounts[infos[0]] != infos[1])) ? "success" : "Password is not corrent"));
			lockLoginAccount.Leave();
			return text;
		}

		/// <summary>
		/// 当客户端登录后，在Ip信息的过滤后，然后触发本方法，进行后续的数据接收，处理，并返回相关的数据信息<br />
		/// When the client logs in, after filtering the IP information, this method is then triggered to perform subsequent data reception, 
		/// processing, and return related data information
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="endPoint">终端节点</param>
		protected virtual void ThreadPoolLoginAfterClientCheck(Socket socket, IPEndPoint endPoint)
		{
			AppSession appSession = new AppSession(socket);
			try
			{
				socket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, SocketAsyncCallBack, appSession);
				AddClient(appSession);
			}
			catch
			{
				socket.Close();
				base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, endPoint));
			}
		}

		/// <summary>
		/// 从远程Socket异步接收的数据信息
		/// </summary>
		/// <param name="ar">异步接收的对象</param>
		protected virtual void SocketAsyncCallBack(IAsyncResult ar)
		{
			AppSession appSession = ar.AsyncState as AppSession;
			if (appSession != null)
			{
				appSession?.WorkSocket?.Close();
			}
		}

		/// <summary>
		/// 当接收到了新的请求的时候执行的操作，此处进行账户的安全验证<br />
		/// The operation performed when a new request is received, and the account security verification is performed here
		/// </summary>
		/// <param name="socket">异步对象</param>
		/// <param name="endPoint">终结点</param>
		protected override void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			string ipAddress = ((endPoint == null) ? string.Empty : endPoint.Address.ToString());
			if (IsTrustedClientsOnly && !CheckIpAddressTrusted(ipAddress))
			{
				base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientDisableLogin, endPoint));
				socket.Close();
				return;
			}
			if (!IsUseAccountCertificate)
			{
				base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOnlineInfo, endPoint));
			}
			ThreadPoolLoginAfterClientCheck(socket, endPoint);
		}

		/// <summary>
		/// 设置并启动受信任的客户端登录并读写，如果为null，将关闭对客户端的ip验证<br />
		/// Set and start the trusted client login and read and write, if it is null, the client's IP verification will be turned off
		/// </summary>
		/// <param name="clients">受信任的客户端列表</param>
		public void SetTrustedIpAddress(List<string> clients)
		{
			lock_trusted_clients.Enter();
			if (clients != null)
			{
				TrustedClients = clients.Select(delegate(string m)
				{
					IPAddress iPAddress = IPAddress.Parse(m);
					return iPAddress.ToString();
				}).ToList();
				IsTrustedClientsOnly = true;
			}
			else
			{
				TrustedClients = new List<string>();
				IsTrustedClientsOnly = false;
			}
			lock_trusted_clients.Leave();
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
				lock_trusted_clients.Enter();
				for (int i = 0; i < TrustedClients.Count; i++)
				{
					if (TrustedClients[i] == ipAddress)
					{
						result = true;
						break;
					}
				}
				lock_trusted_clients.Leave();
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
			lock_trusted_clients.Enter();
			if (TrustedClients != null)
			{
				result = TrustedClients.ToArray();
			}
			lock_trusted_clients.Leave();
			return result;
		}

		/// <inheritdoc />
		protected override void CloseAction()
		{
			lock (lockOnlineClient)
			{
				for (int i = 0; i < listsOnlineClient.Count; i++)
				{
					listsOnlineClient[i]?.WorkSocket?.Close();
					base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, listsOnlineClient[i].IpEndPoint));
				}
				listsOnlineClient.Clear();
				onlineCount = 0;
			}
			base.CloseAction();
		}

		/// <summary>
		/// 新增一个在线的客户端信息<br />
		/// Add an online client information
		/// </summary>
		/// <param name="session">会话内容</param>
		protected void AddClient(AppSession session)
		{
			lock (lockOnlineClient)
			{
				listsOnlineClient.Add(session);
				onlineCount++;
			}
			this.OnClientOnline?.Invoke(this, session);
		}

		/// <summary>
		/// 移除一个在线的客户端信息<br />
		/// Remove an online client message
		/// </summary>
		/// <param name="session">会话内容</param>
		/// <param name="reason">下线的原因</param>
		protected void RemoveClient(AppSession session, string reason = "")
		{
			bool flag = false;
			lock (lockOnlineClient)
			{
				if (listsOnlineClient.Remove(session))
				{
					base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, session.IpEndPoint) + " " + reason);
					session.WorkSocket?.Close();
					onlineCount--;
					flag = true;
				}
			}
			if (flag)
			{
				this.OnClientOffline?.Invoke(this, session);
			}
		}

		private void ThreadTimerHeartCheck(object obj)
		{
			AppSession[] array = null;
			lock (lockOnlineClient)
			{
				array = listsOnlineClient.ToArray();
			}
			if (array == null || array.Length == 0)
			{
				return;
			}
			for (int i = 0; i < array.Length; i++)
			{
				if (DateTime.Now - array[i].HeartTime > ActiveTimeSpan)
				{
					RemoveClient(array[i]);
				}
			}
		}

		/// <summary>
		/// 释放当前的对象
		/// </summary>
		/// <param name="disposing">是否托管对象</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					ServerClose();
					lockLoginAccount?.Dispose();
					lock_trusted_clients?.Dispose();
				}
				disposedValue = true;
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			Dispose(disposing: true);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetworkAuthenticationServerBase[{base.Port}]";
		}
	}
}
