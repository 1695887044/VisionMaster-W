namespace HslCommunication.Instrument.CJT.Helper
{
	/// <summary>
	/// 控制码信息
	/// </summary>
	public class CJTControl
	{
		/// <summary>
		/// 读数据
		/// </summary>
		public const byte ReadData = 1;

		/// <summary>
		/// 写数据
		/// </summary>
		public const byte WriteData = 4;

		/// <summary>
		/// 读地址
		/// </summary>
		public const byte ReadAddress = 3;

		/// <summary>
		/// 写地址
		/// </summary>
		public const byte WriteAddress = 21;
	}
}
