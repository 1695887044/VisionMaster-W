namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// 基本的控制码信息
	/// </summary>
	public class DLTControl
	{
		/// <summary>
		/// 保留
		/// </summary>
		public const byte DLT2007_Retain = 0;

		/// <summary>
		/// 广播
		/// </summary>
		public const byte DLT2007_Broadcast = 8;

		/// <summary>
		/// 读数据
		/// </summary>
		public const byte DLT2007_ReadData = 17;

		/// <summary>
		/// 读后续数据
		/// </summary>
		public const byte DLT2007_ReadFollowData = 18;

		/// <summary>
		/// 读通信地址
		/// </summary>
		public const byte DLT2007_ReadAddress = 19;

		/// <summary>
		/// 写数据
		/// </summary>
		public const byte DLT2007_WriteData = 20;

		/// <summary>
		/// 写通信地址
		/// </summary>
		public const byte DLT2007_WriteAddress = 21;

		/// <summary>
		/// 冻结命令
		/// </summary>
		public const byte DLT2007_FreezeCommand = 22;

		/// <summary>
		/// 更改通信速率
		/// </summary>
		public const byte DLT2007_ChangeBaudRate = 23;

		/// <summary>
		/// 修改密码
		/// </summary>
		public const byte DLT2007_ChangePassword = 24;

		/// <summary>
		/// 最大需求量清零
		/// </summary>
		public const byte DLT2007_ClearMaxQuantityDemanded = 25;

		/// <summary>
		/// 电表清零
		/// </summary>
		public const byte DLT2007_ElectricityReset = 26;

		/// <summary>
		/// 事件清零
		/// </summary>
		public const byte DLT2007_EventReset = 27;

		/// <summary>
		/// 跳合闸、报警、保电
		/// </summary>
		public const byte DLT2007_ClosingAlarmPowerpProtection = 28;

		/// <summary>
		/// 多功能端子输出控制命令
		/// </summary>
		public const byte DLT2007_MultiFunctionTerminalOutputControlCommand = 29;

		/// <summary>
		/// 安全认证命令
		/// </summary>
		public const byte DLT2007_SecurityAuthenticationCommand = 3;

		/// <summary>
		/// 保留
		/// </summary>
		public const byte DLT1997_Retain = 0;

		/// <summary>
		/// 读数据
		/// </summary>
		public const byte DLT1997_ReadData = 1;

		/// <summary>
		/// 读后续数据
		/// </summary>
		public const byte DLT1997_ReadFollowData = 2;

		/// <summary>
		/// 重读数据
		/// </summary>
		public const byte DLT1997_ReadAgain = 3;

		/// <summary>
		/// 写数据
		/// </summary>
		public const byte DLT1997_WriteData = 4;

		/// <summary>
		/// 广播校时
		/// </summary>
		public const byte DLT1997_Broadcast = 8;

		/// <summary>
		/// 写设备地址
		/// </summary>
		public const byte DLT1997_WriteAddress = 10;

		/// <summary>
		/// 更改通信速率
		/// </summary>
		public const byte DLT1997_ChangeBaudRate = 12;

		/// <summary>
		/// 修改密码
		/// </summary>
		public const byte DLT1997_ChangePassword = 15;

		/// <summary>
		/// 最大需量清零
		/// </summary>
		public const byte DLT1997_ClearMaxQuantityDemanded = 16;
	}
}
