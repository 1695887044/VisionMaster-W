using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.MQTT;

namespace HslCommunication.WebSocket
{
	/// <summary>
	/// websocket 的会话客户端
	/// </summary>
	public class WebSocketSession : PipeSession
	{
		private object objLock = new object();

		private NetworkStream networkStream = null;

		private SslStream ssl = null;

		private bool sslSecure = false;

		/// <summary>
		/// 当前客户端订阅的所有的Topic信息
		/// </summary>
		public List<string> Topics { get; set; }

		/// <summary>
		/// 远程的客户端的ip及端口信息
		/// </summary>
		public IPEndPoint Remote { get; set; }

		/// <summary>
		/// 当前的会话是否是问答客户端，如果是问答客户端的话，数据的推送是无效的。
		/// </summary>
		public bool IsQASession { get; set; }

		/// <summary>
		/// 客户端请求的url信息，可能携带一些参数信息
		/// </summary>
		public string Url { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public WebSocketSession()
		{
			Topics = new List<string>();
			base.OnlineTime = DateTime.Now;
		}

		/// <summary>
		/// 检查当前的连接对象是否在
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <param name="willcard">是否启用通配符订阅操作</param>
		/// <returns>是否包含的结果信息</returns>
		public bool IsClientSubscribe(string topic, bool willcard)
		{
			bool result = false;
			lock (objLock)
			{
				if (willcard)
				{
					for (int i = 0; i < Topics.Count; i++)
					{
						if (MqttHelper.CheckMqttTopicWildcards(topic, Topics[i]))
						{
							result = true;
							break;
						}
					}
				}
				else
				{
					result = Topics.Contains(topic);
				}
			}
			return result;
		}

		/// <summary>
		/// 动态增加一个订阅的信息
		/// </summary>
		/// <param name="topic">订阅的主题</param>
		public void AddTopic(string topic)
		{
			lock (objLock)
			{
				if (!Topics.Contains(topic))
				{
					Topics.Add(topic);
				}
			}
		}

		/// <summary>
		/// 动态移除一个订阅的信息
		/// </summary>
		/// <param name="topic">订阅的主题</param>
		public bool RemoveTopic(string topic)
		{
			bool result = false;
			lock (objLock)
			{
				result = Topics.Remove(topic);
			}
			return result;
		}

		private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			if (sslPolicyErrors == SslPolicyErrors.None)
			{
				return true;
			}
			return !sslSecure;
		}

		internal OperateResult<SslStream> CreateSslStream(bool createNew = false, X509Certificate cert = null)
		{
			if (createNew)
			{
				networkStream?.Close();
				ssl?.Close();
				PipeTcpNet pipeTcpNet = base.Communication as PipeTcpNet;
				networkStream = new NetworkStream(pipeTcpNet.Socket, ownsSocket: false);
				ssl = new SslStream(networkStream, leaveInnerStreamOpen: false, ValidateServerCertificate, null);
				try
				{
					ssl.AuthenticateAsServer(cert, clientCertificateRequired: false, SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12, checkCertificateRevocation: true);
					return OperateResult.CreateSuccessResult(ssl);
				}
				catch (Exception ex)
				{
					return new OperateResult<SslStream>(ex.Message);
				}
			}
			return OperateResult.CreateSuccessResult(ssl);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"WebSocketSession[{Remote}][{SoftBasic.GetTimeSpanDescription(DateTime.Now - base.OnlineTime)}]";
		}
	}
}
