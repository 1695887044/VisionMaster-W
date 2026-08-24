namespace HslCommunication.Core
{
	/// <inheritdoc cref="T:HslCommunication.Core.ISessionContext" />
	public class SessionContext : ISessionContext
	{
		/// <inheritdoc cref="P:HslCommunication.Core.ISessionContext.UserName" />
		public string UserName { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.ISessionContext.ClientId" />
		public string ClientId { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Core.ISessionContext.Tag" />
		public object Tag { get; set; }
	}
}
