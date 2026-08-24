namespace HslCommunication.WebSocket
{
	/// <summary>
	/// websocket 协议的 op的枚举信息
	/// </summary>
	public enum WSOpCode
	{
		/// <summary>
		/// 连续消息分片
		/// </summary>
		ContinuousMessageFragment = 0,
		/// <summary>
		/// 文本消息分片
		/// </summary>
		TextMessageFragment = 1,
		/// <summary>
		/// 二进制消息分片
		/// </summary>
		BinaryMessageFragment = 2,
		/// <summary>
		/// 连接关闭
		/// </summary>
		ConnectionClose = 8,
		/// <summary>
		/// 心跳检查
		/// </summary>
		HeartbeatPing = 9,
		/// <summary>
		/// 心跳检查
		/// </summary>
		HeartbeatPong = 10
	}
}
