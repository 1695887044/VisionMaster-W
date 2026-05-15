namespace HslCommunication.Core
{
	/// <summary>
	/// 取消操作的令牌<br />
	/// Token to cancel the operation
	/// </summary>
	public class HslCancelToken
	{
		/// <summary>
		/// 是否取消的操作<br />
		/// Whether to cancel the operation
		/// </summary>
		public bool IsCancelled { get; set; }
	}
}
