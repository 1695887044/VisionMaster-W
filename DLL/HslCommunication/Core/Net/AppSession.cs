using System.Net;
using System.Net.Sockets;
using HslCommunication.BasicFramework;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 当前的网络会话信息，还包含了一些客户端相关的基本的参数信息<br />
	/// The current network session information also contains some basic parameter information related to the client
	/// </summary>
	public class AppSession : SessionBase
	{
		/// <summary>
		/// UDP通信中的远程端<br />
		/// Remote side in UDP communication
		/// </summary>
		internal EndPoint UdpEndPoint = null;

		/// <summary>
		/// 远程对象的别名信息<br />
		/// Alias information for remote objects
		/// </summary>
		public string LoginAlias { get; set; }

		/// <summary>
		/// 客户端唯一的标识，在NetPushServer及客户端类里有使用<br />
		/// The unique identifier of the client, used in the NetPushServer and client classes
		/// </summary>
		public string ClientUniqueID { get; set; }

		/// <summary>
		/// 数据内容缓存<br />
		/// data content cache
		/// </summary>
		internal byte[] BytesBuffer { get; set; }

		/// <summary>
		/// 用于关键字分类使用<br />
		/// Used for keyword classification
		/// </summary>
		internal string KeyGroup { get; set; }

		/// <summary>
		/// 当前会话绑定的自定义的对象内容<br />
		/// The content of the custom object bound to the current session
		/// </summary>
		public object Tag { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.SessionBase.#ctor" />
		public AppSession()
		{
			ClientUniqueID = SoftBasic.GetUniqueStringByGuidAndRandom();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.SessionBase.#ctor(System.Net.Sockets.Socket)" />
		public AppSession(Socket socket)
			: base(socket)
		{
			ClientUniqueID = SoftBasic.GetUniqueStringByGuidAndRandom();
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return this == obj;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return string.IsNullOrEmpty(LoginAlias) ? $"AppSession[{base.IpEndPoint}]" : $"AppSession[{base.IpEndPoint}] [{LoginAlias}]";
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
	}
}
