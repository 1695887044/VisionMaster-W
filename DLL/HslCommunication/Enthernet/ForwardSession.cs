using System;
using System.Net;
using System.Net.Sockets;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 转发过程中的中间会话信息
	/// </summary>
	public class ForwardSession : PipeSession
	{
		/// <summary>
		/// 连接服务端的socket
		/// </summary>
		public Socket ServerSocket { get; set; }

		/// <summary>
		/// 服务端的缓存数据信息
		/// </summary>
		public byte[] ServerBuffer { get; set; }

		/// <summary>
		/// 转发客户端的缓存数据
		/// </summary>
		public byte[] BytesBuffer { get; set; }

		/// <summary>
		/// 客户端的远程终结点
		/// </summary>
		public IPEndPoint IpEndPoint { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public ForwardSession()
		{
			ServerBuffer = new byte[2048];
			BytesBuffer = new byte[2048];
		}

		/// <summary>
		/// 指定客户端的 socket 来实例化一个对象
		/// </summary>
		/// <param name="pipe">客户端的管道信息</param>
		/// <param name="endPoint">客户端的远程连接点</param>
		/// <param name="cacheSize">指定当前的缓冲区的大小</param>
		public ForwardSession(PipeTcpNet pipe, IPEndPoint endPoint, int cacheSize = 2048)
		{
			base.Communication = pipe;
			IpEndPoint = endPoint;
			ServerBuffer = new byte[cacheSize];
			BytesBuffer = new byte[cacheSize];
		}

		/// <inheritdoc />
		public override void Close()
		{
			base.Close();
			NetSupport.CloseSocket(ServerSocket);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"Server[{ServerSocket.RemoteEndPoint}] Local[{IpEndPoint}] Online:{SoftBasic.GetTimeSpanDescription(DateTime.Now - base.OnlineTime)}";
		}
	}
}
