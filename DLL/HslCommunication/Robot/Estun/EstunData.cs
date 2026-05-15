using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;

namespace HslCommunication.Robot.Estun
{
	/// <summary>
	/// 埃斯顿的数据类对象
	/// </summary>
	public class EstunData
	{
		/// <summary>
		/// 获取或设置当前的手动操作模式
		/// </summary>
		public bool ManualMode { get; set; }

		/// <summary>
		/// 获取或设置当前的自动操作模式
		/// </summary>
		public bool AutoMode { get; set; }

		/// <summary>
		/// 获取或设置当前的远程操作模式
		/// </summary>
		public bool RemoteMode { get; set; }

		/// <summary>
		/// 获取或设置使能状态
		/// </summary>
		public bool EnableStatus { get; set; }

		/// <summary>
		/// 获取或设置运行状态
		/// </summary>
		public bool RunStatus { get; set; }

		/// <summary>
		/// 获取或设置错误状态
		/// </summary>
		public bool ErrorStatus { get; set; }

		/// <summary>
		/// 获取或设置程序运行状态
		/// </summary>
		public bool ProgramRunStatus { get; set; }

		/// <summary>
		/// 机器人正在动作
		/// </summary>
		public bool RobotMoving { get; set; }

		/// <summary>
		/// 获取或设置当前加载的工程名
		/// </summary>
		public string ProjectName { get; set; }

		/// <summary>
		/// SimDout, 共计64位长度
		/// </summary>
		public bool[] DO { get; set; }

		/// <summary>
		/// 机器人的执行命令状态，16长度的bool数组
		/// </summary>
		public ushort RobotCommandStatus { get; set; }

		/// <summary>
		/// 用户的AO，32个长度
		/// </summary>
		public float[] AO { get; set; }

		/// <summary>
		/// 全局的速度值
		/// </summary>
		public short GlobalSpeedValue { get; set; }

		/// <summary>
		/// SimDI, 共计64个bit
		/// </summary>
		public bool[] DI { get; set; }

		/// <summary>
		/// 用户的AI，32个长度
		/// </summary>
		public float[] AI { get; set; }

		/// <summary>
		/// 读写标志位
		/// </summary>
		public short ReadWriteFlag { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public EstunData()
		{
		}

		/// <summary>
		/// 使用指定的原始字节数据来实例化埃斯顿机器人对象
		/// </summary>
		/// <param name="source">原始字节数组</param>
		/// <param name="byteTransform">字节转换</param>
		public EstunData(byte[] source, IByteTransform byteTransform)
		{
			LoadBySourceData(source, byteTransform);
		}

		/// <summary>
		/// 从原始的字节数据里加载
		/// </summary>
		/// <param name="source">原始字节数据</param>
		/// <param name="byteTransform">字节转换的类</param>
		public void LoadBySourceData(byte[] source, IByteTransform byteTransform)
		{
			ManualMode = source[7].GetBoolByIndex(0);
			AutoMode = source[7].GetBoolByIndex(1);
			RemoteMode = source[7].GetBoolByIndex(2);
			EnableStatus = source[7].GetBoolByIndex(3);
			RunStatus = source[7].GetBoolByIndex(4);
			ErrorStatus = source[7].GetBoolByIndex(5);
			ProgramRunStatus = source[7].GetBoolByIndex(6);
			RobotMoving = source[7].GetBoolByIndex(7);
			GlobalSpeedValue = byteTransform.TransInt16(source, 2);
			ProjectName = Encoding.ASCII.GetString(SoftBasic.BytesReverseByWord(source.SelectMiddle(8, 20))).TrimEnd(default(char));
			DO = SoftBasic.BytesReverseByWord(source.SelectMiddle(28, 8)).ToBoolArray();
			RobotCommandStatus = byteTransform.TransUInt16(source, 36);
			AO = byteTransform.TransSingle(source, 38, 16);
			DI = SoftBasic.BytesReverseByWord(source.SelectMiddle(126, 8)).ToBoolArray();
			AI = byteTransform.TransSingle(source, 134, 16);
			ReadWriteFlag = byteTransform.TransInt16(source, 198);
		}
	}
}
