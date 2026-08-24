namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// CNC的运行状态
	/// </summary>
	public enum CNCRunStatus
	{
		/// <summary>
		/// 重置
		/// </summary>
		RESET,
		/// <summary>
		/// 停止
		/// </summary>
		STOP,
		/// <summary>
		/// 等待
		/// </summary>
		HOLD,
		/// <summary>
		/// 启动
		/// </summary>
		START,
		/// <summary>
		/// MSTR
		/// </summary>
		MSTR
	}
}
