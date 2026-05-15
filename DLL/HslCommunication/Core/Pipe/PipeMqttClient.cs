using System;
using System.IO;
using HslCommunication.Core.IMessage;
using HslCommunication.MQTT;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// 基于MQTT通信实现的管道信息
	/// </summary>
	public class PipeMqttClient : CommunicationPipe
	{
		private MqttClient mqttClient;

		private string writeTopic = string.Empty;

		private string readTopic = string.Empty;

		/// <summary>
		/// 获取当前管道绑定的<see cref="T:HslCommunication.MQTT.MqttClient" />对象信息<br />
		/// Obtain the <see cref="T:HslCommunication.MQTT.MqttClient" /> object bound to the current pipeline
		/// </summary>
		public MqttClient MqttClient => mqttClient;

		/// <summary>
		/// 获取当前管道里的用于读取操作的主题名称<br />
		/// Gets the name of the topic used for the read operation in the current pipeline
		/// </summary>
		public string ReadTopic => readTopic;

		/// <summary>
		/// 获取当前管道里的用于写入操作的主题名称<br />
		/// Gets the name of the topic used for the write operation in the current pipeline
		/// </summary>
		public string WriteTopic => writeTopic;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public PipeMqttClient(MqttClient mqttClient, string readTopic, string writeTopic)
		{
			this.readTopic = readTopic;
			this.writeTopic = writeTopic;
			if (mqttClient != null)
			{
				SubscribeTopic subscribeTopic = mqttClient.GetSubscribeTopic(readTopic);
				if (subscribeTopic == null)
				{
					mqttClient.OnClientConnected += MqttClient_OnClientConnected;
					mqttClient.SubscribeMessage(readTopic);
					subscribeTopic = mqttClient.GetSubscribeTopic(readTopic);
					subscribeTopic.OnMqttMessageReceived += SubscribeTopic_OnMqttMessageReceived;
				}
			}
			base.UseServerActivePush = true;
			this.mqttClient = mqttClient;
		}

		private void MqttClient_OnClientConnected(MqttClient client)
		{
			client.SubscribeMessage(readTopic);
		}

		private void SubscribeTopic_OnMqttMessageReceived(MqttClient client, MqttApplicationMessage message)
		{
			SetBufferQA(message.Payload);
		}

		/// <inheritdoc />
		public override OperateResult<bool> OpenCommunication()
		{
			SubscribeTopic subscribeTopic = mqttClient.GetSubscribeTopic(readTopic);
			if (subscribeTopic == null)
			{
				mqttClient.OnClientConnected += MqttClient_OnClientConnected;
				mqttClient.SubscribeMessage(readTopic);
				subscribeTopic = mqttClient.GetSubscribeTopic(readTopic);
				subscribeTopic.OnMqttMessageReceived += SubscribeTopic_OnMqttMessageReceived;
			}
			return OperateResult.CreateSuccessResult(value: false);
		}

		/// <inheritdoc />
		public override OperateResult CloseCommunication()
		{
			SubscribeTopic subscribeTopic = mqttClient.GetSubscribeTopic(readTopic);
			if (subscribeTopic != null)
			{
				subscribeTopic.OnMqttMessageReceived -= SubscribeTopic_OnMqttMessageReceived;
			}
			mqttClient.OnClientConnected -= MqttClient_OnClientConnected;
			mqttClient.UnSubscribeMessage(readTopic);
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult Send(byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return mqttClient.PublishMessage(new MqttApplicationMessage
			{
				Topic = writeTopic,
				Payload = ((size == data.Length) ? data : data.SelectMiddle(offset, size))
			});
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReceiveMessage(INetMessage netMessage, byte[] sendValue, bool useActivePush = true, Action<long, long> reportProgress = null, Action<byte[]> logMessage = null)
		{
			DateTime now = DateTime.Now;
			MemoryStream ms = new MemoryStream();
			do
			{
				if (base.ReceiveTimeOut >= 0 && (DateTime.Now - now).TotalMilliseconds > (double)base.ReceiveTimeOut)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout + base.ReceiveTimeOut);
				}
				if (autoResetEvent.WaitOne(base.ReceiveTimeOut))
				{
					byte[] array = bufferQA;
					if (array != null && array.Length != 0)
					{
						ms.Write(bufferQA);
						logMessage?.Invoke(bufferQA);
					}
					continue;
				}
				return new OperateResult<byte[]>(-10000, StringResources.Language.ReceiveDataTimeout + base.ReceiveTimeOut + " Received: " + ms.ToArray().ToHexString(' '));
			}
			while (netMessage != null && !CheckMessageComplete(netMessage, sendValue, ref ms));
			return OperateResult.CreateSuccessResult(ms.ToArray());
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"PipeMqttClient[{mqttClient}]";
		}
	}
}
