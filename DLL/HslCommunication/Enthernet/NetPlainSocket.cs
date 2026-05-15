using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 一个基于明文的socket中心
	/// </summary>
	public class NetPlainSocket : NetworkXBase
	{
		private Encoding encoding;

		private object connectLock = new object();

		private string ipAddress = "127.0.0.1";

		private int port = 10000;

		private int bufferLength = 2048;

		private byte[] buffer = null;

		/// <summary>
		/// 当前的编码器
		/// </summary>
		public Encoding Encoding
		{
			get
			{
				return encoding;
			}
			set
			{
				encoding = value;
			}
		}

		/// <summary>
		/// 当接收到字符串时候的触发事件
		/// </summary>
		public event Action<string> ReceivedString;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public NetPlainSocket()
		{
			buffer = new byte[bufferLength];
			encoding = Encoding.UTF8;
		}

		/// <summary>
		/// 使用指定的ip地址和端口号来实例化这个对象
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		public NetPlainSocket(string ipAddress, int port)
		{
			buffer = new byte[bufferLength];
			encoding = Encoding.UTF8;
			this.ipAddress = ipAddress;
			this.port = port;
		}

		/// <summary>
		/// 连接服务器
		/// </summary>
		/// <returns>返回是否连接成功</returns>
		public OperateResult ConnectServer()
		{
			CoreSocket?.Close();
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ipAddress, port, 5000);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			try
			{
				CoreSocket = operateResult.Content;
				CoreSocket.BeginReceive(buffer, 0, bufferLength, SocketFlags.None, ReceiveCallBack, CoreSocket);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
		}

		/// <summary>
		/// 关闭当前的连接对象
		/// </summary>
		/// <returns>错误信息</returns>
		public OperateResult ConnectClose()
		{
			try
			{
				CoreSocket?.Close();
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
		}

		/// <summary>
		/// 发送字符串到网络上去
		/// </summary>
		/// <param name="text">文本信息</param>
		/// <returns>发送是否成功</returns>
		public OperateResult SendString(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return OperateResult.CreateSuccessResult();
			}
			return Send(CoreSocket, encoding.GetBytes(text));
		}

		private void ReceiveCallBack(IAsyncResult ar)
		{
			Socket socket = ar.AsyncState as Socket;
			if (socket == null)
			{
				return;
			}
			byte[] array = null;
			try
			{
				int num = socket.EndReceive(ar);
				socket.BeginReceive(buffer, 0, bufferLength, SocketFlags.None, ReceiveCallBack, socket);
				if (num == 0)
				{
					CoreSocket?.Close();
					return;
				}
				array = new byte[num];
				Array.Copy(buffer, 0, array, 0, num);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex2)
			{
				base.LogNet?.WriteWarn(StringResources.Language.SocketContentReceiveException + ":" + ex2.Message);
				ThreadPool.QueueUserWorkItem(ReConnectServer, null);
			}
			if (array != null)
			{
				this.ReceivedString?.Invoke(encoding.GetString(array));
			}
		}

		/// <summary>
		/// 是否是处于重连的状态
		/// </summary>
		/// <param name="obj">无用的对象</param>
		private void ReConnectServer(object obj)
		{
			base.LogNet?.WriteWarn(StringResources.Language.ReConnectServerAfterTenSeconds);
			for (int i = 0; i < 10; i++)
			{
				HslHelper.ThreadSleep(1000);
				base.LogNet?.WriteWarn($"Wait for connecting server after {9 - i} seconds");
			}
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ipAddress, port, 5000);
			if (!operateResult.IsSuccess)
			{
				ThreadPool.QueueUserWorkItem(ReConnectServer, obj);
				return;
			}
			lock (connectLock)
			{
				try
				{
					CoreSocket?.Close();
					CoreSocket = operateResult.Content;
					CoreSocket.BeginReceive(buffer, 0, bufferLength, SocketFlags.None, ReceiveCallBack, CoreSocket);
					base.LogNet?.WriteWarn(StringResources.Language.ReConnectServerSuccess);
				}
				catch (Exception ex)
				{
					base.LogNet?.WriteWarn(StringResources.Language.RemoteClosedConnection + ":" + ex.Message);
					ThreadPool.QueueUserWorkItem(ReConnectServer, obj);
				}
			}
		}

		/// <summary>
		/// 返回表示当前对象的字符串
		/// </summary>
		/// <returns>字符串</returns>
		public override string ToString()
		{
			return $"NetPlainSocket[{ipAddress}:{port}]";
		}
	}
}
