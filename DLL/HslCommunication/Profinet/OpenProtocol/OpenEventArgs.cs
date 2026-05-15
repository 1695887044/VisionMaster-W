using System;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// Open协议的消息对象信息
	/// </summary>
	public class OpenEventArgs : EventArgs
	{
		/// <summary>
		/// Open协议的消息内容
		/// </summary>
		public string Content { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public OpenEventArgs()
		{
		}

		/// <summary>
		/// 指定Open的消息来实例化对象
		/// </summary>
		/// <param name="content">Open内容</param>
		public OpenEventArgs(string content)
		{
			Content = content;
		}
	}
}
