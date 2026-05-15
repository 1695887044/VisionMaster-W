using System;
using System.Threading.Tasks;
using HslCommunication.Algorithms.ConnectPool;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// <b>[商业授权]</b> MqttSyncClient客户端的连接池类对象，用于共享当前的连接池，合理的动态调整连接对象，然后进行高效通信的操作，默认连接数无限大。<br />
	/// <b>[Authorization]</b> The connection pool class object of the MqttSyncClient is used to share the current connection pool, 
	/// reasonably dynamically adjust the connection object, and then perform efficient communication operations, 
	/// The default number of connections is unlimited
	/// </summary>
	/// <remarks>
	/// 本连接池用于提供高并发的读写性能，仅对商业授权用户开放。使用起来和<see cref="T:HslCommunication.MQTT.MqttSyncClient" />一致，但是更加的高性能，在密集型数据交互时，优势尤为明显。
	/// </remarks>
	public class MqttSyncClientPool
	{
		private MqttConnectionOptions connectionOptions;

		private ConnectPool<IMqttSyncConnector> mqttConnectPool;

		/// <summary>
		/// 获取当前的连接池管理对象信息<br />
		/// Get current connection pool management object information
		/// </summary>
		public ConnectPool<IMqttSyncConnector> GetMqttSyncConnectPool => mqttConnectPool;

		/// <inheritdoc cref="P:HslCommunication.Algorithms.ConnectPool.ConnectPool`1.MaxConnector" />
		public int MaxConnector
		{
			get
			{
				return mqttConnectPool.MaxConnector;
			}
			set
			{
				mqttConnectPool.MaxConnector = value;
			}
		}

		/// <summary>
		/// 通过MQTT连接参数实例化一个对象<br />
		/// Instantiate an object through MQTT connection parameters
		/// </summary>
		/// <param name="options">MQTT的连接参数信息</param>
		public MqttSyncClientPool(MqttConnectionOptions options)
		{
			connectionOptions = options;
			
				mqttConnectPool = new ConnectPool<IMqttSyncConnector>(() => new IMqttSyncConnector(options));
				mqttConnectPool.MaxConnector = int.MaxValue;
				return;
		
		}

		/// <summary>
		/// 通过MQTT连接参数以及自定义的初始化方法来实例化一个对象<br />
		/// Instantiate an object through MQTT connection parameters and custom initialization methods
		/// </summary>
		/// <param name="options">MQTT的连接参数信息</param>
		/// <param name="initialize">自定义的初始化方法</param>
		public MqttSyncClientPool(MqttConnectionOptions options, Action<MqttSyncClient> initialize)
		{
			connectionOptions = options;
			
				mqttConnectPool = new ConnectPool<IMqttSyncConnector>(delegate
				{
					MqttSyncClient mqttSyncClient = new MqttSyncClient(options);
					initialize(mqttSyncClient);
					return new IMqttSyncConnector
					{
						SyncClient = mqttSyncClient
					};
				});
				mqttConnectPool.MaxConnector = int.MaxValue;
				return;
			
		}

		private OperateResult<T> ConnectPoolExecute<T>(Func<MqttSyncClient, OperateResult<T>> exec)
		{
			
				IMqttSyncConnector availableConnector = mqttConnectPool.GetAvailableConnector();
				OperateResult<T> result = exec(availableConnector.SyncClient);
				mqttConnectPool.ReturnConnector(availableConnector);
				return result;
			
		}

		private OperateResult<T1, T2> ConnectPoolExecute<T1, T2>(Func<MqttSyncClient, OperateResult<T1, T2>> exec)
		{
			
				IMqttSyncConnector availableConnector = mqttConnectPool.GetAvailableConnector();
				OperateResult<T1, T2> result = exec(availableConnector.SyncClient);
				mqttConnectPool.ReturnConnector(availableConnector);
				return result;
			
		}

		private async Task<OperateResult<T>> ConnectPoolExecuteAsync<T>(Func<MqttSyncClient, Task<OperateResult<T>>> exec)
		{
			
				IMqttSyncConnector client = mqttConnectPool.GetAvailableConnector();
				OperateResult<T> result = await exec(client.SyncClient);
				mqttConnectPool.ReturnConnector(client);
				return result;
			
		}

		private async Task<OperateResult<T1, T2>> ConnectPoolExecuteAsync<T1, T2>(Func<MqttSyncClient, Task<OperateResult<T1, T2>>> execAsync)
		{
			
				IMqttSyncConnector client = mqttConnectPool.GetAvailableConnector();
				OperateResult<T1, T2> result = await execAsync(client.SyncClient);
				mqttConnectPool.ReturnConnector(client);
				return result;
			
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.Read(System.String,System.Byte[],System.Action{System.Int64,System.Int64},System.Action{System.String,System.String},System.Action{System.Int64,System.Int64})" />
		public OperateResult<string, byte[]> Read(string topic, byte[] payload, Action<long, long> sendProgress = null, Action<string, string> handleProgress = null, Action<long, long> receiveProgress = null)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.Read(topic, payload, sendProgress, handleProgress, receiveProgress));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadString(System.String,System.String,System.Action{System.Int64,System.Int64},System.Action{System.String,System.String},System.Action{System.Int64,System.Int64})" />
		public OperateResult<string, string> ReadString(string topic, string payload, Action<long, long> sendProgress = null, Action<string, string> handleProgress = null, Action<long, long> receiveProgress = null)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadString(topic, payload, sendProgress, handleProgress, receiveProgress));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRpc``1(System.String,System.String)" />
		public OperateResult<T> ReadRpc<T>(string topic, string payload)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadRpc<T>(topic, payload));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRpc``1(System.String,System.Object)" />
		public OperateResult<T> ReadRpc<T>(string topic, object payload)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadRpc<T>(topic, payload));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRpcApis" />
		public OperateResult<MqttRpcApiInfo[]> ReadRpcApis()
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadRpcApis());
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRpcApiLog(System.String)" />
		public OperateResult<long[]> ReadRpcApiLog(string api)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadRpcApiLog(api));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRetainTopics" />
		public OperateResult<string[]> ReadRetainTopics()
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadRetainTopics());
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadTopicPayload(System.String,System.Action{System.Int64,System.Int64})" />
		public OperateResult<MqttClientApplicationMessage> ReadTopicPayload(string topic, Action<long, long> receiveProgress = null)
		{
			return ConnectPoolExecute((MqttSyncClient m) => m.ReadTopicPayload(topic, receiveProgress));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.Read(System.String,System.Byte[],System.Action{System.Int64,System.Int64},System.Action{System.String,System.String},System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult<string, byte[]>> ReadAsync(string topic, byte[] payload, Action<long, long> sendProgress = null, Action<string, string> handleProgress = null, Action<long, long> receiveProgress = null)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadAsync(topic, payload, sendProgress, handleProgress, receiveProgress));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.ReadString(System.String,System.String,System.Action{System.Int64,System.Int64},System.Action{System.String,System.String},System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult<string, string>> ReadStringAsync(string topic, string payload, Action<long, long> sendProgress = null, Action<string, string> handleProgress = null, Action<long, long> receiveProgress = null)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadStringAsync(topic, payload, sendProgress, handleProgress, receiveProgress));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.ReadRpc``1(System.String,System.String)" />
		public async Task<OperateResult<T>> ReadRpcAsync<T>(string topic, string payload)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadRpcAsync<T>(topic, payload));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.ReadRpc``1(System.String,System.Object)" />
		public async Task<OperateResult<T>> ReadRpcAsync<T>(string topic, object payload)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadRpcAsync<T>(topic, payload));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.ReadRpcApis" />
		public async Task<OperateResult<MqttRpcApiInfo[]>> ReadRpcApisAsync()
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadRpcApisAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClientPool.ReadRpcApiLog(System.String)" />
		public async Task<OperateResult<long[]>> ReadRpcApiLogAsync(string api)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadRpcApiLogAsync(api));
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadRetainTopics" />
		public async Task<OperateResult<string[]>> ReadRetainTopicsAsync()
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadRetainTopicsAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttSyncClient.ReadTopicPayload(System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult<MqttClientApplicationMessage>> ReadTopicPayloadAsync(string topic, Action<long, long> receiveProgress = null)
		{
			return await ConnectPoolExecuteAsync((MqttSyncClient m) => m.ReadTopicPayloadAsync(topic, receiveProgress));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MqttSyncClientPool[{mqttConnectPool.MaxConnector}]";
		}
	}
}
