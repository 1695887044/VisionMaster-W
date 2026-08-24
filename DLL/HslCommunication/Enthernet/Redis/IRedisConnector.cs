using System;
using HslCommunication.Algorithms.ConnectPool;

namespace HslCommunication.Enthernet.Redis
{
	/// <summary>
	/// 关于Redis实现的接口<see cref="T:HslCommunication.Algorithms.ConnectPool.IConnector" />，从而实现了数据连接池的操作信息
	/// </summary>
	public class IRedisConnector : IConnector
	{
		/// <inheritdoc cref="P:HslCommunication.Algorithms.ConnectPool.IConnector.IsConnectUsing" />
		public bool IsConnectUsing { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Algorithms.ConnectPool.IConnector.GuidToken" />
		public string GuidToken { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Algorithms.ConnectPool.IConnector.LastUseTime" />
		public DateTime LastUseTime { get; set; }

		/// <summary>
		/// Redis的连接对象
		/// </summary>
		public RedisClient Redis { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Algorithms.ConnectPool.IConnector.Close" />
		public void Close()
		{
			Redis?.ConnectClose();
		}

		/// <inheritdoc cref="M:HslCommunication.Algorithms.ConnectPool.IConnector.Open" />
		public void Open()
		{
			Redis?.SetPersistentConnection();
		}
	}
}
