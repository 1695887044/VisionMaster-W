namespace HslCommunication.LogNet
{
	/// <summary>
	/// 日志文件的存储模式
	/// </summary>
	public enum LogSaveMode
	{
		/// <summary>
		/// 单个文件的存储模式
		/// </summary>
		SingleFile = 1,
		/// <summary>
		/// 根据文件的大小来存储，固定一个大小，不停的生成文件
		/// </summary>
		FileFixedSize,
		/// <summary>
		/// 根据时间来存储，可以设置年，季，月，日，小时等等
		/// </summary>
		Time
	}
}
