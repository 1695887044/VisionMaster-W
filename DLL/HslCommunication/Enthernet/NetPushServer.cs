using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 发布订阅服务器的类，支持按照关键字进行数据信息的订阅
	/// </summary>
	/// <remarks>
	/// 详细的使用说明，请参照博客<a href="http://www.cnblogs.com/dathlin/p/8992315.html">http://www.cnblogs.com/dathlin/p/8992315.html</a>
	/// </remarks>
	/// <example>
	/// 此处贴上了Demo项目的服务器配置的示例代码
	/// <code lang="cs" source="TestProject\PushNetServer\FormServer.cs" region="NetPushServer" title="NetPushServer示例" />
	/// </example>
	public class NetPushServer : NetworkServerBase
	{
		private Dictionary<string, string> dictSendHistory;

		private Dictionary<string, PushGroupClient> dictPushClients;

		private readonly object dicHybirdLock;

		private readonly object dicSendCacheLock;

		private Action<AppSession, string> sendAction;

		private int onlineCount = 0;

		private List<NetPushClient> pushClients;

		private object pushClientsLock;

		private bool isPushCacheAfterConnect = true;

		/// <summary>
		/// 在线客户端的数量
		/// </summary>
		public int OnlineCount => onlineCount;

		/// <summary>
		/// 在客户端上线之后，是否推送缓存的数据，默认设置为true
		/// </summary>
		public bool PushCacheAfterConnect
		{
			get
			{
				return isPushCacheAfterConnect;
			}
			set
			{
				isPushCacheAfterConnect = value;
			}
		}

		/// <summary>
		/// 实例化一个对象
		/// </summary>
		public NetPushServer()
		{
			dictPushClients = new Dictionary<string, PushGroupClient>();
			dictSendHistory = new Dictionary<string, string>();
			dicHybirdLock = new object();
			dicSendCacheLock = new object();
			sendAction = SendString;
			pushClientsLock = new object();
			pushClients = new List<NetPushClient>();
		}

		/// <inheritdoc />
		protected override void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			OperateResult<int, string> operateResult = ReceiveStringContentFromSocket(socket);
			if (!operateResult.IsSuccess)
			{
				return;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(socket, 0, "");
			if (!operateResult2.IsSuccess)
			{
				return;
			}
			AppSession appSession = new AppSession(socket)
			{
				KeyGroup = operateResult.Content2
			};
			appSession.BytesBuffer = new byte[4];
			try
			{
				socket.BeginReceive(appSession.BytesBuffer, 0, appSession.BytesBuffer.Length, SocketFlags.None, ReceiveCallback, appSession);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.SocketReceiveException, ex);
				return;
			}
			base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOnlineInfo, appSession.IpEndPoint));
			PushGroupClient pushGroupClient = GetPushGroupClient(operateResult.Content2);
			if (pushGroupClient == null)
			{
				return;
			}
			Interlocked.Increment(ref onlineCount);
			pushGroupClient.AddPushClient(appSession);
			lock (dicSendCacheLock)
			{
				if (dictSendHistory.ContainsKey(operateResult.Content2) && isPushCacheAfterConnect)
				{
					SendString(appSession, dictSendHistory[operateResult.Content2]);
				}
			}
		}

		/// <inheritdoc />
		public override void ServerClose()
		{
			base.ServerClose();
		}

		/// <summary>
		/// 主动推送数据内容
		/// </summary>
		/// <param name="key">关键字</param>
		/// <param name="content">数据内容</param>
		public void PushString(string key, string content)
		{
			lock (dicSendCacheLock)
			{
				if (dictSendHistory.ContainsKey(key))
				{
					dictSendHistory[key] = content;
				}
				else
				{
					dictSendHistory.Add(key, content);
				}
			}
			AddPushKey(key);
			GetPushGroupClient(key)?.PushString(content, sendAction);
		}

		/// <summary>
		/// 移除关键字信息，通常应用于一些特殊临时用途的关键字
		/// </summary>
		/// <param name="key">关键字</param>
		public void RemoveKey(string key)
		{
			lock (dicHybirdLock)
			{
				if (dictPushClients.ContainsKey(key))
				{
					int num = dictPushClients[key].RemoveAllClient();
					for (int i = 0; i < num; i++)
					{
						Interlocked.Decrement(ref onlineCount);
					}
					dictPushClients.Remove(key);
				}
			}
		}

		/// <summary>
		/// 创建一个远程服务器的数据推送操作，以便推送给子客户端
		/// </summary>
		/// <param name="ipAddress">远程的IP地址</param>
		/// <param name="port">远程的端口号</param>
		/// <param name="key">订阅的关键字</param>
		public OperateResult CreatePushRemote(string ipAddress, int port, string key)
		{
			OperateResult operateResult;
			lock (pushClientsLock)
			{
				if (pushClients.Find((NetPushClient m) => m.KeyWord == key) == null)
				{
					NetPushClient netPushClient = new NetPushClient(ipAddress, port, key);
					operateResult = netPushClient.CreatePush(GetPushFromServer);
					if (operateResult.IsSuccess)
					{
						pushClients.Add(netPushClient);
					}
				}
				else
				{
					operateResult = new OperateResult(StringResources.Language.KeyIsExistAlready);
				}
			}
			return operateResult;
		}

		private void ReceiveCallback(IAsyncResult ar)
		{
			AppSession appSession = ar.AsyncState as AppSession;
			if (appSession == null)
			{
				return;
			}
			try
			{
				Socket workSocket = appSession.WorkSocket;
				int num = workSocket.EndReceive(ar);
				if (num <= 4)
				{
					base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, appSession.IpEndPoint));
					RemoveGroupOnline(appSession.KeyGroup, appSession.ClientUniqueID);
				}
				else
				{
					appSession.UpdateHeartTime();
				}
			}
			catch (Exception ex)
			{
				if (ex.Message.Contains(StringResources.Language.SocketRemoteCloseException))
				{
					base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, appSession.IpEndPoint));
					RemoveGroupOnline(appSession.KeyGroup, appSession.ClientUniqueID);
				}
				else
				{
					base.LogNet?.WriteException(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, appSession.IpEndPoint), ex);
					RemoveGroupOnline(appSession.KeyGroup, appSession.ClientUniqueID);
				}
			}
		}

		private void AddPushKey(string key)
		{
			lock (dicHybirdLock)
			{
				if (!dictPushClients.ContainsKey(key))
				{
					dictPushClients.Add(key, new PushGroupClient());
				}
			}
		}

		private PushGroupClient GetPushGroupClient(string key)
		{
			PushGroupClient pushGroupClient = null;
			lock (dicHybirdLock)
			{
				if (dictPushClients.ContainsKey(key))
				{
					pushGroupClient = dictPushClients[key];
				}
				else
				{
					pushGroupClient = new PushGroupClient();
					dictPushClients.Add(key, pushGroupClient);
				}
			}
			return pushGroupClient;
		}

		/// <summary>
		/// 移除客户端的数据信息
		/// </summary>
		/// <param name="key">指定的客户端</param>
		/// <param name="clientID">指定的客户端唯一的id信息</param>
		private void RemoveGroupOnline(string key, string clientID)
		{
			PushGroupClient pushGroupClient = GetPushGroupClient(key);
			if (pushGroupClient != null && pushGroupClient.RemovePushClient(clientID))
			{
				Interlocked.Decrement(ref onlineCount);
			}
		}

		private void SendString(AppSession appSession, string content)
		{
			if (!Send(appSession.WorkSocket, HslProtocol.CommandBytes(0, base.Token, content)).IsSuccess)
			{
				RemoveGroupOnline(appSession.KeyGroup, appSession.ClientUniqueID);
			}
		}

		private void GetPushFromServer(NetPushClient pushClient, string data)
		{
			PushString(pushClient.KeyWord, data);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetPushServer[{base.Port}]";
		}
	}
}
