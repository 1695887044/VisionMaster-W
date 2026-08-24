using System.Threading;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// 订阅的主题信息<br />
	/// Subscribed topic information
	/// </summary>
	public class SubscribeTopic
	{
		private long subscribeTick = 0L;

		/// <summary>
		/// 主题信息<br />
		/// topic information
		/// </summary>
		public string Topic { get; set; }

		/// <summary>
		/// 主题被订阅的次数<br />
		/// The number of times the topic was subscribed
		/// </summary>
		public long SubscribeTick => subscribeTick;

		/// <summary>
		/// 当接收到Mqtt订阅的信息的时候触发的事件<br />
		/// Event triggered when a message subscribed to by Mqtt is received
		/// </summary>
		public event MqttClient.MqttMessageReceiveDelegate OnMqttMessageReceived;

		/// <summary>
		/// 使用指定的主题初始化<br />
		/// Initialize with the specified theme
		/// </summary>
		/// <param name="topic">主题信息</param>
		public SubscribeTopic(string topic)
		{
			Topic = topic;
		}

		/// <summary>
		/// 使用指定的参数，触发订阅主题的事件<br />
		/// Using the specified parameters, trigger the event of the subscribed topic
		/// </summary>
		/// <param name="client">客户端会话信息</param>
		/// <param name="message">Mqtt主题消息</param>
		public void TriggerSubscription(MqttClient client, MqttApplicationMessage message)
		{
			this.OnMqttMessageReceived?.Invoke(client, message);
		}

		/// <summary>
		/// 增加一个订阅的计数信息，不需要手动调用，在 <see cref="T:HslCommunication.MQTT.MqttClient" /> 订阅主题之后，会自动增加<br />
		/// Add a subscription count information, no need to manually call it, it will be automatically added after <see cref="T:HslCommunication.MQTT.MqttClient" /> subscribes to the topic
		/// </summary>
		/// <returns>返回增加计数后的值</returns>
		public long AddSubscribeTick()
		{
			return Interlocked.Increment(ref subscribeTick);
		}

		/// <summary>
		/// 减少一个订阅的计数信息，用户可以手动调用，比如判断是否是最后一次移除，然后决定是否通过 <see cref="T:HslCommunication.MQTT.MqttClient" /> 取消订阅主题<br />
		/// Reduce the count information of a subscription, the user can call it manually, such as judging whether it is the last removal, 
		/// and then decide whether to unsubscribe the topic through <see cref="T:HslCommunication.MQTT.MqttClient" />
		/// </summary>
		/// <returns>返回减少计数后的值</returns>
		public long RemoveSubscribeTick()
		{
			return Interlocked.Decrement(ref subscribeTick);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "SubscribeTopic[" + Topic + "]";
		}
	}
}
