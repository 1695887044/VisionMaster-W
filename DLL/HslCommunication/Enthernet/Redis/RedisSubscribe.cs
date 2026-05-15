using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HslCommunication.Core;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet.Redis
{
	/// <summary>
	/// Redis协议的订阅操作，一个对象订阅一个或是多个频道的信息，当发生网络异常的时候，内部会进行自动重连，并恢复之前的订阅信息。<br />
	/// In the subscription operation of the Redis protocol, an object subscribes to the information of one or more channels. 
	/// When a network abnormality occurs, the internal will automatically reconnect and restore the previous subscription information.
	/// </summary>
	public class RedisSubscribe : NetworkXBase
	{
		/// <summary>
		/// 当接收到Redis订阅的信息的时候触发<br />
		/// Triggered when receiving Redis subscription information
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <param name="message">数据信息</param>
		public delegate void RedisMessageReceiveDelegate(string topic, string message);

		private IPEndPoint endPoint;

		private List<string> keyWords = null;

		private object listLock = new object();

		private int reconnectTime = 10000;

		private int connectTimeOut = 5000;

		/// <summary>
		/// 如果Redis服务器设置了密码，此处就需要进行设置。必须在 <see cref="M:HslCommunication.Enthernet.Redis.RedisSubscribe.ConnectServer" /> 方法调用前设置。<br />
		/// If the Redis server has set a password, it needs to be set here. Must be set before the <see cref="M:HslCommunication.Enthernet.Redis.RedisSubscribe.ConnectServer" /> method is called.
		/// </summary>
		public string Password { get; set; }

		/// <summary>
		/// 获取或设置当前连接超时时间，主要对 <see cref="M:HslCommunication.Enthernet.Redis.RedisSubscribe.ConnectServer" /> 方法有影响，默认值为 5000，也即是5秒。<br />
		/// Get or set the current connection timeout period, which mainly affects the <see cref="M:HslCommunication.Enthernet.Redis.RedisSubscribe.ConnectServer" /> method. The default value is 5000, which is 5 seconds.
		/// </summary>
		public int ConnectTimeOut
		{
			get
			{
				return connectTimeOut;
			}
			set
			{
				connectTimeOut = value;
			}
		}

		/// <summary>
		/// 当接收到Redis订阅的信息的时候触发
		/// </summary>
		public event RedisMessageReceiveDelegate OnRedisMessageReceived;

		/// <summary>
		/// 实例化一个发布订阅类的客户端，需要指定ip地址，端口。<br />
		/// To instantiate a publish and subscribe client, you need to specify the ip address and port.
		/// </summary>
		/// <param name="ipAddress">服务器的IP地址</param>
		/// <param name="port">服务器的端口号</param>
		public RedisSubscribe(string ipAddress, int port)
		{
			endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
			keyWords = new List<string>();
		}

		/// <summary>
		/// 实例化一个发布订阅类的客户端，需要指定ip地址，端口，及订阅关键字。<br />
		/// To instantiate a publish-subscribe client, you need to specify the ip address, port, and subscription keyword.
		/// </summary>
		/// <param name="ipAddress">服务器的IP地址</param>
		/// <param name="port">服务器的端口号</param>
		/// <param name="keys">订阅关键字</param>
		public RedisSubscribe(string ipAddress, int port, string[] keys)
		{
			endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
			keyWords = new List<string>(keys);
		}

		/// <summary>
		/// 实例化一个发布订阅类的客户端，需要指定ip地址，端口，及订阅关键字。<br />
		/// To instantiate a publish-subscribe client, you need to specify the ip address, port, and subscription keyword.
		/// </summary>
		/// <param name="ipAddress">服务器的IP地址</param>
		/// <param name="port">服务器的端口号</param>
		/// <param name="key">订阅关键字</param>
		public RedisSubscribe(string ipAddress, int port, string key)
		{
			endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
			keyWords = new List<string> { key };
		}

		private OperateResult CreatePush()
		{
			CoreSocket?.Close();
			OperateResult<Socket> operateResult = CreateSocketAndConnect(endPoint, connectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (!string.IsNullOrEmpty(Password))
			{
				OperateResult operateResult2 = Send(operateResult.Content, RedisHelper.PackStringCommand(new string[2] { "AUTH", Password }));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<byte[]> operateResult3 = ReceiveRedisCommand(operateResult.Content);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				string @string = Encoding.UTF8.GetString(operateResult3.Content);
				if (!@string.StartsWith("+OK"))
				{
					return new OperateResult(@string);
				}
			}
			List<string> list = keyWords;
			if (list != null && list.Count > 0)
			{
				OperateResult operateResult4 = Send(operateResult.Content, RedisHelper.PackSubscribeCommand(keyWords.ToArray()));
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
			}
			CoreSocket = operateResult.Content;
			try
			{
				operateResult.Content.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallBack, operateResult.Content);
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
			return OperateResult.CreateSuccessResult();
		}

		private void ReceiveCallBack(IAsyncResult ar)
		{
			Socket socket = ar.AsyncState as Socket;
			if (socket == null)
			{
				return;
			}
			try
			{
				int num = socket.EndReceive(ar);
			}
			catch (ObjectDisposedException)
			{
				base.LogNet?.WriteWarn("Socket Disposed!");
				return;
			}
			catch (Exception ex2)
			{
				SocketReceiveException(ex2);
				return;
			}
			OperateResult<byte[]> operateResult = ReceiveRedisCommand(socket);
			if (!operateResult.IsSuccess)
			{
				SocketReceiveException(null);
				return;
			}
			try
			{
				socket.BeginReceive(new byte[0], 0, 0, SocketFlags.None, ReceiveCallBack, socket);
			}
			catch (Exception ex3)
			{
				SocketReceiveException(ex3);
				return;
			}
			OperateResult<string[]> stringsFromCommandLine = RedisHelper.GetStringsFromCommandLine(operateResult.Content);
			if (!stringsFromCommandLine.IsSuccess)
			{
				base.LogNet?.WriteWarn(stringsFromCommandLine.Message);
			}
			else if (!(stringsFromCommandLine.Content[0].ToUpper() == "SUBSCRIBE"))
			{
				if (stringsFromCommandLine.Content[0].ToUpper() == "MESSAGE")
				{
					this.OnRedisMessageReceived?.Invoke(stringsFromCommandLine.Content[1], stringsFromCommandLine.Content[2]);
				}
				else
				{
					base.LogNet?.WriteWarn(stringsFromCommandLine.Content[0]);
				}
			}
		}

		private void SocketReceiveException(Exception ex)
		{
			do
			{
				if (ex != null)
				{
					base.LogNet?.WriteException("Offline", ex);
				}
				Console.WriteLine(StringResources.Language.ReConnectServerAfterTenSeconds);
				HslHelper.ThreadSleep(reconnectTime);
			}
			while (!CreatePush().IsSuccess);
			Console.WriteLine(StringResources.Language.ReConnectServerSuccess);
		}

		private void AddSubTopics(string[] topics)
		{
			lock (listLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (!keyWords.Contains(topics[i]))
					{
						keyWords.Add(topics[i]);
					}
				}
			}
		}

		private void RemoveSubTopics(string[] topics)
		{
			lock (listLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (keyWords.Contains(topics[i]))
					{
						keyWords.Remove(topics[i]);
					}
				}
			}
		}

		/// <summary>
		/// 从Redis服务器订阅一个或多个主题信息<br />
		/// Subscribe to one or more topics from the redis server
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>订阅结果</returns>
		public OperateResult SubscribeMessage(string topic)
		{
			return SubscribeMessage(new string[1] { topic });
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisSubscribe.SubscribeMessage(System.String)" />
		public OperateResult SubscribeMessage(string[] topics)
		{
			if (topics == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (topics.Length == 0)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (CoreSocket == null)
			{
				OperateResult operateResult = ConnectServer();
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			OperateResult operateResult2 = Send(CoreSocket, RedisHelper.PackSubscribeCommand(topics));
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			AddSubTopics(topics);
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 取消订阅多个主题信息，取消之后，当前的订阅数据就不在接收到。<br />
		/// Unsubscribe from multiple topic information. After cancellation, the current subscription data will not be received.
		/// </summary>
		/// <param name="topics">主题信息</param>
		/// <returns>取消订阅结果</returns>
		public OperateResult UnSubscribeMessage(string[] topics)
		{
			if (CoreSocket == null)
			{
				OperateResult operateResult = ConnectServer();
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			OperateResult operateResult2 = Send(CoreSocket, RedisHelper.PackUnSubscribeCommand(topics));
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			RemoveSubTopics(topics);
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 取消已经订阅的主题信息
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <returns>取消订阅结果</returns>
		public OperateResult UnSubscribeMessage(string topic)
		{
			return UnSubscribeMessage(new string[1] { topic });
		}

		/// <summary>
		/// 连接Redis的服务器，如果已经初始化了订阅的Topic信息，那么就会直接进行订阅操作。
		/// </summary>
		/// <returns>是否创建成功</returns>
		public OperateResult ConnectServer()
		{
			return CreatePush();
		}

		/// <summary>
		/// 关闭消息推送的界面
		/// </summary>
		public void ConnectClose()
		{
			CoreSocket?.Close();
			lock (listLock)
			{
				keyWords.Clear();
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"RedisSubscribe[{endPoint}]";
		}
	}
}
