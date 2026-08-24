using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Security;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// Mqtt的会话信息，包含了一些基本的信息内容，客户端的IP地址及端口，Client ID，用户名，活动时间，是否允许发布数据等等<br />
	/// Mqtt's session information includes some basic information content, the client's IP address and port, Client ID, user name, activity time, whether it is allowed to publish data, etc.
	/// </summary>
	public class MqttSession : ISessionContext
	{
		private object objLock = new object();

		/// <summary>
		/// 远程的ip地址端口信息<br />
		/// Remote ip address port information
		/// </summary>
		public IPEndPoint EndPoint { get; set; }

		/// <summary>
		/// 当前接收的客户端ID信息<br />
		/// Client ID information currently received
		/// </summary>
		public string ClientId { get; set; }

		/// <summary>
		/// 当前客户端的激活时间<br />
		/// The activation time of the current client
		/// </summary>
		public DateTime ActiveTime { get; set; }

		/// <summary>
		/// 获取当前的客户端的上线时间<br />
		/// Get the online time of the current client
		/// </summary>
		public DateTime OnlineTime { get; private set; }

		/// <summary>
		/// 两次活动的最小时间间隔<br />
		/// Minimum time interval between two activities
		/// </summary>
		public TimeSpan ActiveTimeSpan { get; set; }

		/// <summary>
		/// 当前客户端绑定的套接字对象
		/// </summary>
		internal Socket MqttSocket { get; set; }

		/// <summary>
		/// 当前客户端订阅的所有的Topic信息<br />
		/// All Topic information subscribed by the current client
		/// </summary>
		private List<string> Topics { get; set; }

		/// <summary>
		/// 当前的用户名<br />
		/// Current username
		/// </summary>
		public string UserName { get; set; }

		/// <summary>
		/// 当前的协议信息，一般为 "MQTT"，如果是同步客户端那么是 "HUSL"，如果是文件客户端就是 "FILE"<br />
		/// The current protocol information, generally "MQTT", if it is a synchronous client then it is "HUSL", if it is a file client it is "FILE"
		/// </summary>
		public string Protocol { get; private set; }

		/// <summary>
		/// 遗嘱主题<br />
		/// will topic
		/// </summary>
		/// <remarks>
		/// 当前的会话如果因为非正常原因下线的时候，服务器立即发布该客户端的遗嘱主题及数据信息
		/// </remarks>
		public string WillTopic { get; set; }

		/// <summary>
		/// 遗嘱的消息内容<br />
		/// The message content of the will
		/// </summary>
		/// <remarks>
		/// 当前的会话如果因为非正常原因下线的时候，服务器立即发布该客户端的遗嘱主题及数据信息
		/// </remarks>
		public byte[] WillMessage { get; set; }

		/// <summary>
		/// 获取或设置当前的会话是否拥有开发者权限，在开发者权限下，可以遍历接口信息，默认只有admin账户支持开发者权限，如果自定义设置账户权限，则需要在会话登录的事件里，将本属性值设置为 True<br />
		/// Obtain or set whether the current session has developer permissions, under the developer permissions, you can traverse the interface information, by default, 
		/// only the admin account supports developer permissions, if you customize the account permissions, you need to set the value of this property to True in the session login event
		/// </summary>
		public bool DeveloperPermissions { get; set; } = false;


		/// <summary>
		/// 获取设置客户端的加密信息
		/// </summary>
		public bool IsAesCryptography => AesCryptography != null;

		/// <summary>
		/// 获取或设置当前客户端的AES加密的对象信息
		/// </summary>
		internal AesCryptography AesCryptography { get; set; }

		/// <summary>
		/// 当前的会话信息关联的自定义信息<br />
		/// Custom information associated with the current session information
		/// </summary>
		public object Tag { get; set; }

		/// <summary>
		/// 获取或设置当前的MQTT客户端是否允许发布消息，默认为False，如果设置为True，就是禁止发布消息，服务器不会触发收到消息的事件。<br />
		/// Gets or sets whether the current MQTT client is allowed to publish messages, the default is False, 
		/// if set to True, it is forbidden to publish messages, The server does not trigger the event of receiving a message.
		/// </summary>
		public bool ForbidPublishTopic { get; set; }

		/// <summary>
		/// 实例化一个对象，指定ip地址及端口，以及协议内容<br />
		/// Instantiate an object, specify ip address and port, and protocol content
		/// </summary>
		/// <param name="endPoint">远程客户端的IP地址</param>
		/// <param name="protocol">协议信息</param>
		public MqttSession(IPEndPoint endPoint, string protocol)
		{
			Topics = new List<string>();
			ActiveTime = DateTime.Now;
			OnlineTime = DateTime.Now;
			ActiveTimeSpan = TimeSpan.FromSeconds(1000000.0);
			EndPoint = endPoint;
			Protocol = protocol;
		}

		/// <summary>
		/// 检查当前的会话对象里是否订阅了指定的主题内容<br />
		/// Check whether the specified topic content is subscribed in the current session object
		/// </summary>
		/// <param name="topic">主题信息</param>
		/// <param name="willcard">订阅的主题是否使用了通配符</param>
		/// <returns>如果订阅了，返回 True, 否则，返回 False</returns>
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
		/// 获取当前客户端订阅的所有的Topic信息<br />
		/// Get all Topic information subscribed by the current client
		/// </summary>
		/// <returns>主题列表</returns>
		public string[] GetTopics()
		{
			lock (objLock)
			{
				return Topics.ToArray();
			}
		}

		/// <summary>
		/// 当前的会话信息新增一个订阅的主题信息<br />
		/// The current session information adds a subscribed topic information
		/// </summary>
		/// <param name="topic">主题的信息</param>
		public void AddSubscribe(string topic)
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
		/// 当前的会话信息新增多个订阅的主题信息<br />
		/// The current session information adds multiple subscribed topic information
		/// </summary>
		/// <param name="topics">主题的信息</param>
		public void AddSubscribe(string[] topics)
		{
			if (topics == null)
			{
				return;
			}
			lock (objLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (!Topics.Contains(topics[i]))
					{
						Topics.Add(topics[i]);
					}
				}
			}
		}

		/// <summary>
		/// 移除会话信息的一个订阅的主题
		/// </summary>
		/// <param name="topic">主题</param>
		public void RemoveSubscribe(string topic)
		{
			lock (objLock)
			{
				if (Topics.Contains(topic))
				{
					Topics.Remove(topic);
				}
			}
		}

		/// <summary>
		/// 移除会话信息的一个订阅的主题<br />
		/// Remove a subscribed topic from session information
		/// </summary>
		/// <param name="topics">主题</param>
		public void RemoveSubscribe(string[] topics)
		{
			if (topics == null)
			{
				return;
			}
			lock (objLock)
			{
				for (int i = 0; i < topics.Length; i++)
				{
					if (Topics.Contains(topics[i]))
					{
						Topics.Remove(topics[i]);
					}
				}
			}
		}

		/// <summary>
		/// 获取当前的会话信息，包含在线时间的信息<br />
		/// Get current session information, including online time information
		/// </summary>
		/// <returns>会话信息，包含在线时间</returns>
		public string GetSessionOnlineInfo()
		{
			StringBuilder stringBuilder = new StringBuilder(ToString());
			stringBuilder.Append(" [" + SoftBasic.GetTimeSpanDescription(DateTime.Now - OnlineTime) + "]");
			return stringBuilder.ToString();
		}

		/// <summary>
		/// 获取当前会话的基本信息<br />
		/// Obtain basic information about the current session
		/// </summary>
		/// <returns>用于JSON发送的会话的基本信息</returns>
		public MqttSessionInfo GetSessionInfo()
		{
			return new MqttSessionInfo
			{
				EndPoint = EndPoint.ToString(),
				ClientId = ClientId,
				ActiveTime = ActiveTime,
				OnlineTime = OnlineTime,
				Topics = Topics.ToArray(),
				UserName = UserName,
				Protocol = Protocol,
				WillTopic = WillTopic,
				DeveloperPermissions = DeveloperPermissions,
				IsAesCryptography = IsAesCryptography,
				ForbidPublishTopic = ForbidPublishTopic
			};
		}

		/// <inheritdoc />
		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder($"{Protocol} Session[IP:{EndPoint}]");
			if (!string.IsNullOrEmpty(ClientId))
			{
				stringBuilder.Append(" [ID:" + ClientId + "]");
			}
			if (!string.IsNullOrEmpty(UserName))
			{
				stringBuilder.Append(" [Name:" + UserName + "]");
			}
			if (IsAesCryptography)
			{
				stringBuilder.Append("[RSA/AES]");
			}
			return stringBuilder.ToString();
		}
	}
}
