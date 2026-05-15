using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 一个自定义的支持序列化反序列化的异常类，具体用法参照第四版《CLR Via C#》P414
	/// </summary>
	/// <typeparam name="TExceptionArgs">泛型异常</typeparam>
	[Serializable]
	public sealed class Exception<TExceptionArgs> : Exception, ISerializable where TExceptionArgs : ExceptionArgs
	{
		/// <summary>
		/// 用于反序列化的
		/// </summary>
		private const string c_args = "Args";

		private readonly TExceptionArgs m_args;

		/// <summary>
		/// 消息
		/// </summary>
		public TExceptionArgs Args => m_args;

		/// <summary>
		/// 获取描述当前异常的消息
		/// </summary>
		public override string Message
		{
			get
			{
				string message = base.Message;
				return (m_args == null) ? message : (message + " (" + m_args.Message + ")");
			}
		}

		/// <summary>
		/// 实例化一个异常对象
		/// </summary>
		/// <param name="message">消息</param>
		/// <param name="innerException">内部异常类</param>
		public Exception(string message = null, Exception innerException = null)
			: this((TExceptionArgs)null, message, innerException)
		{
		}

		/// <summary>
		/// 实例化一个异常对象
		/// </summary>
		/// <param name="args">异常消息</param>
		/// <param name="message">消息</param>
		/// <param name="innerException">内部异常类</param>
		public Exception(TExceptionArgs args, string message = null, Exception innerException = null)
			: base(message, innerException)
		{
			m_args = args;
		}

		[SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
		private Exception(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
			m_args = (TExceptionArgs)info.GetValue("Args", typeof(TExceptionArgs));
		}

		/// <summary>
		/// 获取存储对象的序列化数据
		/// </summary>
		/// <param name="info">序列化的信息</param>
		/// <param name="context">流的上下文</param>
		[SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Args", m_args);
			base.GetObjectData(info, context);
		}

		/// <summary>
		/// 确定指定的object是否等于当前的object
		/// </summary>
		/// <param name="obj">异常对象</param>
		/// <returns>是否一致</returns>
		public override bool Equals(object obj)
		{
			Exception<TExceptionArgs> ex = obj as Exception<TExceptionArgs>;
			if (ex == null)
			{
				return false;
			}
			return object.Equals(m_args, ex.m_args) && base.Equals(obj);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
	}
}
