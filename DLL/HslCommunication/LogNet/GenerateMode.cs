namespace HslCommunication.LogNet
{
	/// <summary>
	/// 日志文件输出模式
	/// </summary>
	public enum GenerateMode
	{
		/// <summary>
		/// 按每分钟生成日志文件
		/// </summary>
		ByEveryMinute = 1,
		/// <summary>
		/// 按每个小时生成日志文件
		/// </summary>
		ByEveryHour,
		/// <summary>
		/// 按每天生成日志文件
		/// </summary>
		ByEveryDay,
		/// <summary>
		/// 按每个周生成日志文件
		/// </summary>
		ByEveryWeek,
		/// <summary>
		/// 按每个月生成日志文件
		/// </summary>
		ByEveryMonth,
		/// <summary>
		/// 按每季度生成日志文件
		/// </summary>
		ByEverySeason,
		/// <summary>
		/// 按每年生成日志文件
		/// </summary>
		ByEveryYear
	}
}
