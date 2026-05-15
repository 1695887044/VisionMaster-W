namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// 拧紧结果消息
	/// </summary>
	public class TighteningResultMessages
	{
		private OpenProtocolNet openProtocol;

		/// <summary>
		/// 指定Open通信类实例化一个对象
		/// </summary>
		/// <param name="openProtocol">开放协议的对象</param>
		public TighteningResultMessages(OpenProtocolNet openProtocol)
		{
			this.openProtocol = openProtocol;
		}

		/// <summary>
		/// Set the subscription for the result tightenings. The result of this command will be the transmission of the tightening result after the tightening is performed( push function ).
		/// </summary>
		/// <remarks>
		/// The MID revision in the header is used to subscribe to different revisions of MID 0061 Last tightening result data upload reply.
		/// </remarks>
		/// <param name="revision">Revision</param>
		/// <returns>是否订阅成功的结果对象</returns>
		public OperateResult LastTighteningResultDataSubscribe(int revision)
		{
			return openProtocol.ReadCustomer(60, revision, -1, -1, null);
		}

		/// <summary>
		/// Reset the last tightening result subscription.
		/// </summary>
		/// <returns>取消订阅是否成功的结果对象</returns>
		public OperateResult LastTighteningResultDataUnsubscribe()
		{
			return openProtocol.ReadCustomer(63, 1, -1, -1, null);
		}
	}
}
