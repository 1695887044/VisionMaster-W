namespace HslCommunication.Core
{
	/// <summary>
	/// 连接会话信息的上下文，主要是对账户信息的验证<br />
	/// The context of the connection session information, mainly the verification of account information
	/// </summary>
	public interface ISessionContext
	{
		/// <summary>
		/// 当前的用户名信息<br />
		/// current username information
		/// </summary>
		string UserName { get; set; }

		/// <summary>
		/// 当前的会话的ID信息<br />
		/// ID information of the current session
		/// </summary>
		string ClientId { get; set; }

		/// <summary>
		/// 当前的会话信息关联的自定义信息<br />
		/// Custom information associated with the current session information
		/// </summary>
		object Tag { get; set; }
	}
}
