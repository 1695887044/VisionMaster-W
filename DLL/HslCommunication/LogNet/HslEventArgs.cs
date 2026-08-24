using System;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 带有日志消息的事件
	/// </summary>
	public class HslEventArgs : EventArgs
	{
		/// <summary>
		/// 消息信息
		/// </summary>
		public HslMessageItem HslMessage { get; set; }
	}
}
