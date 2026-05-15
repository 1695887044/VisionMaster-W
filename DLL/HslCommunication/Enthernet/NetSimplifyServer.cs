using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 消息处理服务器，主要用来实现接收客户端信息并进行消息反馈的操作，适用于客户端进行远程的调用，要求服务器反馈数据。<br />
	/// The message processing server is mainly used to implement the operation of receiving client information and performing message feedback. It is applicable to remote calls made by clients and requires the server to feedback data.
	/// </summary>
	/// <remarks>
	/// 详细的使用说明，请参照博客<a href="http://www.cnblogs.com/dathlin/p/7697782.html">http://www.cnblogs.com/dathlin/p/7697782.html</a>
	/// </remarks>
	/// <example>
	/// 此处贴上了Demo项目的服务器配置的示例代码
	/// <code lang="cs" source="TestProject\SimplifyNetTest\FormServer.cs" region="Simplify Net" title="NetSimplifyServer示例" />
	/// </example>
	public class NetSimplifyServer : NetworkAuthenticationServerBase
	{
		private int clientCount = 0;

		/// <summary>
		/// 当前在线的客户端数量
		/// </summary>
		public int ClientCount => clientCount;

		/// <summary>
		/// 接收字符串信息的事件
		/// </summary>
		public event Action<AppSession, NetHandle, string> ReceiveStringEvent;

		/// <summary>
		/// 接收字符串数组信息的事件
		/// </summary>
		public event Action<AppSession, NetHandle, string[]> ReceiveStringArrayEvent;

		/// <summary>
		/// 接收字节信息的事件
		/// </summary>
		public event Action<AppSession, NetHandle, byte[]> ReceivedBytesEvent;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public NetSimplifyServer()
		{
		}

		/// <summary>
		/// 向指定的通信对象发送字符串数据
		/// </summary>
		/// <param name="session">通信对象</param>
		/// <param name="customer">用户的指令头</param>
		/// <param name="str">实际发送的字符串数据</param>
		public void SendMessage(AppSession session, int customer, string str)
		{
			Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, str));
		}

		/// <summary>
		/// 向指定的通信对象发送字符串数组
		/// </summary>
		/// <param name="session">通信对象</param>
		/// <param name="customer">用户的指令头</param>
		/// <param name="str">实际发送的字符串数组</param>
		public void SendMessage(AppSession session, int customer, string[] str)
		{
			Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, str));
		}

		/// <summary>
		/// 向指定的通信对象发送字节数据
		/// </summary>
		/// <param name="session">连接对象</param>
		/// <param name="customer">用户的指令头</param>
		/// <param name="bytes">实际的数据</param>
		public void SendMessage(AppSession session, int customer, byte[] bytes)
		{
			Send(session.WorkSocket, HslProtocol.CommandBytes(customer, base.Token, bytes));
		}

		/// <summary>
		/// 关闭网络的操作
		/// </summary>
		protected override void CloseAction()
		{
			this.ReceivedBytesEvent = null;
			this.ReceiveStringEvent = null;
			base.CloseAction();
		}

		/// <summary>
		/// 当接收到了新的请求的时候执行的操作
		/// </summary>
		/// <param name="socket">异步对象</param>
		/// <param name="endPoint">终结点</param>
		protected override void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			AppSession appSession = new AppSession(socket);
			base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOnlineInfo, appSession.IpEndPoint));
			try
			{
				appSession.WorkSocket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallback, appSession);
				Interlocked.Increment(ref clientCount);
			}
			catch (Exception ex)
			{
				appSession.WorkSocket?.Close();
				base.LogNet?.WriteException(ToString(), StringResources.Language.NetClientLoginFailed, ex);
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
			if (!appSession.WorkSocket.EndReceiveResult(ar).IsSuccess)
			{
				AppSessionRemoteClose(appSession);
				return;
			}
			OperateResult<int, int, byte[]> read = await ReceiveHslMessageAsync(appSession.WorkSocket);
			if (!read.IsSuccess)
			{
				AppSessionRemoteClose(appSession);
				return;
			}
			int protocol = read.Content1;
			int customer = read.Content2;
			byte[] content = read.Content3;
			switch (protocol)
			{
			case 1:
				appSession.UpdateHeartTime();
				SendMessage(appSession, customer, content);
				base.LogNet?.WriteDebug(ToString(), $"Heart Check From {appSession.IpEndPoint}");
				break;
			case 1002:
				this.ReceivedBytesEvent?.Invoke(appSession, customer, content);
				break;
			case 1001:
				this.ReceiveStringEvent?.Invoke(appSession, customer, Encoding.Unicode.GetString(content));
				break;
			case 1005:
				this.ReceiveStringArrayEvent?.Invoke(appSession, customer, HslProtocol.UnPackStringArrayFromByte(content));
				break;
			case 2:
				AppSessionRemoteClose(appSession);
				return;
			default:
				AppSessionRemoteClose(appSession);
				return;
			}
			if (!appSession.WorkSocket.BeginReceiveResult(ReceiveCallback, appSession).IsSuccess)
			{
				AppSessionRemoteClose(appSession);
			}
		}

		/// <summary>
		/// 让客户端正常下线，调用本方法即可自由控制会话客户端强制下线操作。
		/// </summary>
		/// <param name="session">会话对象</param>
		public void AppSessionRemoteClose(AppSession session)
		{
			session.WorkSocket?.Close();
			Interlocked.Decrement(ref clientCount);
			base.LogNet?.WriteDebug(ToString(), string.Format(StringResources.Language.ClientOfflineInfo, session.IpEndPoint));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetSimplifyServer[{base.Port}]";
		}
	}
}
