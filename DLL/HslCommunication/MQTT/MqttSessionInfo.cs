using System;
using System.Text;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// 用于客户端获取服务器会话状态监视数据的类
	/// </summary>
	public class MqttSessionInfo
	{
		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.EndPoint" />
		public string EndPoint { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.ClientId" />
		public string ClientId { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.ActiveTime" />
		public DateTime ActiveTime { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.OnlineTime" />
		public DateTime OnlineTime { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.Topics" />
		public string[] Topics { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.UserName" />
		public string UserName { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.Protocol" />
		public string Protocol { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.WillTopic" />
		public string WillTopic { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.DeveloperPermissions" />
		public bool DeveloperPermissions { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.IsAesCryptography" />
		public bool IsAesCryptography { get; set; }

		/// <inheritdoc cref="P:HslCommunication.MQTT.MqttSession.ForbidPublishTopic" />
		public bool ForbidPublishTopic { get; set; }

		/// <inheritdoc />
		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder(Protocol + " Session[IP:" + EndPoint + "]");
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
