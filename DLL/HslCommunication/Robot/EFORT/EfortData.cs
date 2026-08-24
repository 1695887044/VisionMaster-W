using System;
using System.Text;

namespace HslCommunication.Robot.EFORT
{
	/// <summary>
	/// 埃夫特机器人的数据结构<br />
	/// The data structure of the efort robot
	/// </summary>
	public class EfortData
	{
		/// <summary>
		/// 报文开始的字符串
		/// </summary>
		public string PacketStart { get; set; }

		/// <summary>
		/// 数据命令
		/// </summary>
		public ushort PacketOrders { get; set; }

		/// <summary>
		/// 数据心跳
		/// </summary>
		public ushort PacketHeartbeat { get; set; }

		/// <summary>
		/// 报警状态，1:有报警，0：无报警
		/// </summary>
		public byte ErrorStatus { get; set; }

		/// <summary>
		/// 急停状态，1：无急停，0：有急停
		/// </summary>
		public byte HstopStatus { get; set; }

		/// <summary>
		/// 权限状态，1：有权限，0：无权限
		/// </summary>
		public byte AuthorityStatus { get; set; }

		/// <summary>
		/// 伺服状态，1：有使能，0：未使能
		/// </summary>
		public byte ServoStatus { get; set; }

		/// <summary>
		/// 轴运动状态，1：有运动，0：未运动
		/// </summary>
		public byte AxisMoveStatus { get; set; }

		/// <summary>
		/// 程序运行状态，1：有运行，0：未运行
		/// </summary>
		public byte ProgMoveStatus { get; set; }

		/// <summary>
		/// 程序加载状态，1：有加载，0：无加载
		/// </summary>
		public byte ProgLoadStatus { get; set; }

		/// <summary>
		/// 程序暂停状态，1：有暂停，0：无暂停
		/// </summary>
		public byte ProgHoldStatus { get; set; }

		/// <summary>
		/// 模式状态，1:手动，2:自动，3:远程
		/// </summary>
		public ushort ModeStatus { get; set; }

		/// <summary>
		/// 读读状态，百分比（单位）
		/// </summary>
		public ushort SpeedStatus { get; set; }

		/// <summary>
		/// IoDOut状态
		/// </summary>
		public byte[] IoDOut { get; set; }

		/// <summary>
		/// IoDIn状态
		/// </summary>
		public byte[] IoDIn { get; set; }

		/// <summary>
		/// IoIOut状态
		/// </summary>
		public int[] IoIOut { get; set; }

		/// <summary>
		/// IoIIn状态
		/// </summary>
		public int[] IoIIn { get; set; }

		/// <summary>
		/// 加载工程名
		/// </summary>
		public string ProjectName { get; set; }

		/// <summary>
		/// 加载程序名
		/// </summary>
		public string ProgramName { get; set; }

		/// <summary>
		/// 错误信息
		/// </summary>
		public string ErrorText { get; set; }

		/// <summary>
		/// 一到七轴的角度
		/// </summary>
		public float[] DbAxisPos { get; set; }

		/// <summary>
		/// X,Y,Z,A,B,C方向，也叫笛卡尔坐标系
		/// </summary>
		public float[] DbCartPos { get; set; }

		/// <summary>
		/// 一到七轴的速度
		/// </summary>
		public float[] DbAxisSpeed { get; set; }

		/// <summary>
		/// 一到七轴的加速度
		/// </summary>
		public float[] DbAxisAcc { get; set; }

		/// <summary>
		/// 一到七轴的加加速度
		/// </summary>
		public float[] DbAxisAccAcc { get; set; }

		/// <summary>
		/// 一到七轴的力矩
		/// </summary>
		public float[] DbAxisTorque { get; set; }

		/// <summary>
		/// 轴反向计数
		/// </summary>
		public int[] DbAxisDirCnt { get; set; }

		/// <summary>
		/// 轴工作总时长
		/// </summary>
		public int[] DbAxisTime { get; set; }

		/// <summary>
		/// 设备开机总时长
		/// </summary>
		public int DbDeviceTime { get; set; }

		/// <summary>
		/// 报文结束标记
		/// </summary>
		public string PacketEnd { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public EfortData()
		{
			IoDOut = new byte[32];
			IoDIn = new byte[32];
			IoIOut = new int[32];
			IoIIn = new int[32];
			DbAxisPos = new float[7];
			DbCartPos = new float[6];
			DbAxisSpeed = new float[7];
			DbAxisAcc = new float[7];
			DbAxisAccAcc = new float[7];
			DbAxisTorque = new float[7];
			DbAxisDirCnt = new int[7];
			DbAxisTime = new int[7];
		}

		/// <summary>
		/// 从之前的版本数据构造一个埃夫特机器人的数据类型
		/// </summary>
		/// <param name="data">真实的数据内容</param>
		/// <returns>转换的结果内容</returns>
		public static OperateResult<EfortData> PraseFromPrevious(byte[] data)
		{
			if (data.Length < 784)
			{
				return new OperateResult<EfortData>(string.Format(StringResources.Language.DataLengthIsNotEnough, 784, data.Length));
			}
			EfortData efortData = new EfortData();
			efortData.PacketStart = Encoding.ASCII.GetString(data, 0, 15).Trim();
			efortData.PacketOrders = BitConverter.ToUInt16(data, 17);
			efortData.PacketHeartbeat = BitConverter.ToUInt16(data, 19);
			efortData.ErrorStatus = data[21];
			efortData.HstopStatus = data[22];
			efortData.AuthorityStatus = data[23];
			efortData.ServoStatus = data[24];
			efortData.AxisMoveStatus = data[25];
			efortData.ProgMoveStatus = data[26];
			efortData.ProgLoadStatus = data[27];
			efortData.ProgHoldStatus = data[28];
			efortData.ModeStatus = BitConverter.ToUInt16(data, 29);
			efortData.SpeedStatus = BitConverter.ToUInt16(data, 31);
			for (int i = 0; i < 32; i++)
			{
				efortData.IoDOut[i] = data[33 + i];
			}
			for (int j = 0; j < 32; j++)
			{
				efortData.IoDIn[j] = data[65 + j];
			}
			for (int k = 0; k < 32; k++)
			{
				efortData.IoIOut[k] = BitConverter.ToInt32(data, 97 + 4 * k);
			}
			for (int l = 0; l < 32; l++)
			{
				efortData.IoIIn[l] = BitConverter.ToInt32(data, 225 + 4 * l);
			}
			efortData.ProjectName = Encoding.ASCII.GetString(data, 353, 32).Trim(default(char));
			efortData.ProgramName = Encoding.ASCII.GetString(data, 385, 32).Trim(default(char));
			efortData.ErrorText = Encoding.ASCII.GetString(data, 417, 128).Trim(default(char));
			for (int m = 0; m < 7; m++)
			{
				efortData.DbAxisPos[m] = BitConverter.ToSingle(data, 545 + 4 * m);
			}
			for (int n = 0; n < 6; n++)
			{
				efortData.DbCartPos[n] = BitConverter.ToSingle(data, 573 + 4 * n);
			}
			for (int num = 0; num < 7; num++)
			{
				efortData.DbAxisSpeed[num] = BitConverter.ToSingle(data, 597 + 4 * num);
			}
			for (int num2 = 0; num2 < 7; num2++)
			{
				efortData.DbAxisAcc[num2] = BitConverter.ToSingle(data, 625 + 4 * num2);
			}
			for (int num3 = 0; num3 < 7; num3++)
			{
				efortData.DbAxisAccAcc[num3] = BitConverter.ToSingle(data, 653 + 4 * num3);
			}
			for (int num4 = 0; num4 < 7; num4++)
			{
				efortData.DbAxisTorque[num4] = BitConverter.ToSingle(data, 681 + 4 * num4);
			}
			for (int num5 = 0; num5 < 7; num5++)
			{
				efortData.DbAxisDirCnt[num5] = BitConverter.ToInt32(data, 709 + 4 * num5);
			}
			for (int num6 = 0; num6 < 7; num6++)
			{
				efortData.DbAxisTime[num6] = BitConverter.ToInt32(data, 737 + 4 * num6);
			}
			efortData.DbDeviceTime = BitConverter.ToInt32(data, 765);
			efortData.PacketEnd = Encoding.ASCII.GetString(data, 769, 15).Trim();
			return OperateResult.CreateSuccessResult(efortData);
		}

		/// <summary>
		/// 从新版本数据构造一个埃夫特机器人的数据类型
		/// </summary>
		/// <param name="data">真实的数据内容</param>
		/// <returns>转换的结果内容</returns>
		public static OperateResult<EfortData> PraseFrom(byte[] data)
		{
			if (data.Length < 788)
			{
				return new OperateResult<EfortData>(string.Format(StringResources.Language.DataLengthIsNotEnough, 788, data.Length));
			}
			EfortData efortData = new EfortData();
			efortData.PacketStart = Encoding.ASCII.GetString(data, 0, 16).Trim();
			efortData.PacketOrders = BitConverter.ToUInt16(data, 18);
			efortData.PacketHeartbeat = BitConverter.ToUInt16(data, 20);
			efortData.ErrorStatus = data[22];
			efortData.HstopStatus = data[23];
			efortData.AuthorityStatus = data[24];
			efortData.ServoStatus = data[25];
			efortData.AxisMoveStatus = data[26];
			efortData.ProgMoveStatus = data[27];
			efortData.ProgLoadStatus = data[28];
			efortData.ProgHoldStatus = data[29];
			efortData.ModeStatus = BitConverter.ToUInt16(data, 30);
			efortData.SpeedStatus = BitConverter.ToUInt16(data, 32);
			for (int i = 0; i < 32; i++)
			{
				efortData.IoDOut[i] = data[34 + i];
			}
			for (int j = 0; j < 32; j++)
			{
				efortData.IoDIn[j] = data[66 + j];
			}
			for (int k = 0; k < 32; k++)
			{
				efortData.IoIOut[k] = BitConverter.ToInt32(data, 100 + 4 * k);
			}
			for (int l = 0; l < 32; l++)
			{
				efortData.IoIIn[l] = BitConverter.ToInt32(data, 228 + 4 * l);
			}
			efortData.ProjectName = Encoding.ASCII.GetString(data, 356, 32).Trim(default(char));
			efortData.ProgramName = Encoding.ASCII.GetString(data, 388, 32).Trim(default(char));
			efortData.ErrorText = Encoding.ASCII.GetString(data, 420, 128).Trim(default(char));
			for (int m = 0; m < 7; m++)
			{
				efortData.DbAxisPos[m] = BitConverter.ToSingle(data, 548 + 4 * m);
			}
			for (int n = 0; n < 6; n++)
			{
				efortData.DbCartPos[n] = BitConverter.ToSingle(data, 576 + 4 * n);
			}
			for (int num = 0; num < 7; num++)
			{
				efortData.DbAxisSpeed[num] = BitConverter.ToSingle(data, 600 + 4 * num);
			}
			for (int num2 = 0; num2 < 7; num2++)
			{
				efortData.DbAxisAcc[num2] = BitConverter.ToSingle(data, 628 + 4 * num2);
			}
			for (int num3 = 0; num3 < 7; num3++)
			{
				efortData.DbAxisAccAcc[num3] = BitConverter.ToSingle(data, 656 + 4 * num3);
			}
			for (int num4 = 0; num4 < 7; num4++)
			{
				efortData.DbAxisTorque[num4] = BitConverter.ToSingle(data, 684 + 4 * num4);
			}
			for (int num5 = 0; num5 < 7; num5++)
			{
				efortData.DbAxisDirCnt[num5] = BitConverter.ToInt32(data, 712 + 4 * num5);
			}
			for (int num6 = 0; num6 < 7; num6++)
			{
				efortData.DbAxisTime[num6] = BitConverter.ToInt32(data, 740 + 4 * num6);
			}
			efortData.DbDeviceTime = BitConverter.ToInt32(data, 768);
			efortData.PacketEnd = Encoding.ASCII.GetString(data, 772, 16).Trim();
			return OperateResult.CreateSuccessResult(efortData);
		}
	}
}
