namespace HslCommunication.Enthernet
{
	/// <summary>
	/// Http的Webapi接口调用的一些信息<br />
	/// Some information about the Webapi interface call for HTTP
	/// </summary>
	public class HttpApiCalledInfo
	{
		/// <summary>
		/// 获取或设置当前的URL的名称<br />
		/// Gets or sets the name of the current url
		/// </summary>
		public string Url { get; set; }

		/// <summary>
		/// 获取或设置当前的接口传入的数据<br />
		/// Gets or sets the data passed in by the current interface
		/// </summary>
		public string Body { get; set; }

		/// <summary>
		/// 获取或设置当前接口的模式，GET 或是 POST<br />
		/// Gets or sets the mode of the current interface, GET or POST
		/// </summary>
		public string HttpMethod { get; set; }

		/// <summary>
		/// 获取或设置当前接口的耗时，单位：毫秒<br />
		/// The time elapsed to get or set the current interface in milliseconds
		/// </summary>
		public double CostTime { get; set; }

		/// <summary>
		/// 获取或设置当前接口的结果数据<br />
		/// Gets or sets the result data for the current interface
		/// </summary>
		public string Result { get; set; }

		/// <summary>
		/// 获取或设置当前接口的总的调用次数<br />
		/// Gets or sets the total number of calls to the current interface
		/// </summary>
		public long CalledCount { get; set; }

		/// <inheritdoc />
		public override string ToString()
		{
			return Url;
		}
	}
}
