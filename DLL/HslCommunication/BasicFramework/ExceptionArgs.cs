using System;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 异常消息基类
	/// </summary>
	[Serializable]
	public abstract class ExceptionArgs
	{
		/// <summary>
		/// 携带的额外的消息类对象
		/// </summary>
		public virtual string Message => string.Empty;
	}
}
