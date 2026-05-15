using System;
using System.Net;
using System.Net.Sockets;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 会话的基类信息
	/// </summary>
	public abstract class SessionBase
	{
		/// <summary>
		/// 实际传输数据的对象，除非你清楚自己在收发什么数据信息，否则尽量不直接使用本<see cref="T:System.Net.Sockets.Socket" />对象<br />
		/// The actual data transmission object, unless you know what data information you are sending and receiving, try not to directly use this <see cref="T:System.Net.Sockets.Socket" /> object
		/// </summary>
		public Socket WorkSocket { get; private set; }

		/// <summary>
		/// 获取当前的客户端的上线时间<br />
		/// Get the online time of the current client
		/// </summary>
		public DateTime OnlineTime { get; protected set; }

		/// <summary>
		/// 获取当前的远程连接对象的IP地址<br />
		/// Get the IP address of the current remote connection object
		/// </summary>
		public string IpAddress { get; protected set; }

		/// <summary>
		/// 获取当前的连接对象连接的远程客户端<br />
		/// Get the remote client connected by the current connection object
		/// </summary>
		public IPEndPoint IpEndPoint { get; protected set; }

		/// <summary>
		/// 获取心跳验证的时间点<br />
		/// Get the time point of heartbeat verification
		/// </summary>
		public DateTime HeartTime { get; private set; } = DateTime.Now;


		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public SessionBase()
		{
			OnlineTime = DateTime.Now;
		}

		/// <summary>
		/// 通过指定的<see cref="T:System.Net.Sockets.Socket" />对象来初始化一个会话内容
		/// </summary>
		/// <param name="socket">连接的Socket对象</param>
		public SessionBase(Socket socket)
			: this()
		{
			UpdateSocket(socket);
		}

		/// <summary>
		/// 更新当前的心跳时间<br />
		/// Update the current heartbeat time
		/// </summary>
		public void UpdateHeartTime()
		{
			HeartTime = DateTime.Now;
		}

		/// <summary>
		/// 更新当前的<see cref="T:System.Net.Sockets.Socket" />连接对象信息
		/// </summary>
		/// <param name="socket">连接的对象</param>
		public void UpdateSocket(Socket socket)
		{
			if (socket != null)
			{
				WorkSocket = socket;
				try
				{
					IpEndPoint = WorkSocket.RemoteEndPoint as IPEndPoint;
					IpAddress = ((IpEndPoint == null) ? string.Empty : IpEndPoint.Address.ToString());
				}
				catch
				{
				}
			}
		}
	}
}
