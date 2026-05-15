using System;
using HslCommunication.Core.Pipe;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 管道会话信息，包含了一个管道对象，管道可以是TCP,UDP，也可以是串口，甚至是其他自定义的。<br />
	/// Pipeline session information, which contains a pipeline object, which can be TCP, UDP, serial port, or even other custom interfaces.
	/// </summary>
	public class PipeSession
	{
		/// <summary>
		/// 获取当前的客户端的上线时间<br />
		/// Get the online time of the current client
		/// </summary>
		public DateTime OnlineTime { get; set; } = DateTime.Now;


		/// <summary>
		/// 获取心跳验证的时间点<br />
		/// Get the time point of heartbeat verification
		/// </summary>
		public DateTime HeartTime { get; set; } = DateTime.Now;


		/// <summary>
		/// 当前会话绑定的自定义的对象内容<br />
		/// The content of the custom object bound to the current session
		/// </summary>
		public object Tag { get; set; }

		/// <summary>
		/// 当前的会话的管道信息<br />
		/// Pipeline information for the current session
		/// </summary>
		public CommunicationPipe Communication { get; set; }

		/// <summary>
		/// 获取或设置当前会话的ID信息，某些特殊的会话需要用到该字段<br />
		/// Obtain or set the ID information of the current session, which is required for some special sessions
		/// </summary>
		public string SessionID { get; set; }

		/// <summary>
		/// 关闭当前的会话状态<br />
		/// Close the current session state
		/// </summary>
		public virtual void Close()
		{
			Communication?.CloseCommunication();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (Communication == null)
			{
				return "Session<NULL>";
			}
			return $"Session<{Communication}>";
		}
	}
}
