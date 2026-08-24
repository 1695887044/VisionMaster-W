using System;
using HslCommunication.BasicFramework;

namespace HslCommunication.Robot.FANUC
{
	/// <summary>
	/// Fanuc的辅助方法信息
	/// </summary>
	public class FanucHelper
	{
		/// <summary>
		/// Q区数据
		/// </summary>
		public const byte SELECTOR_Q = 72;

		/// <summary>
		/// I区数据
		/// </summary>
		public const byte SELECTOR_I = 70;

		/// <summary>
		/// AQ区数据
		/// </summary>
		public const byte SELECTOR_AQ = 12;

		/// <summary>
		/// AI区数据
		/// </summary>
		public const byte SELECTOR_AI = 10;

		/// <summary>
		/// M区数据
		/// </summary>
		public const byte SELECTOR_M = 76;

		/// <summary>
		/// D区数据
		/// </summary>
		public const byte SELECTOR_D = 8;

		/// <summary>
		/// 命令数据
		/// </summary>
		public const byte SELECTOR_G = 56;

		/// <summary>
		/// 当前地址不允许位操作
		/// </summary>
		public const string NotAllowedBool = ", Current address not support bool read/write";

		/// <summary>
		/// 当前地址不允许字操作
		/// </summary>
		public const string NotAllowedWord = ", Current address not support word read/write";

		/// <summary>
		/// 从FANUC机器人地址进行解析数据信息，地址为D,I,Q,M,AI,AQ区<br />
		/// Parse data information from FANUC robot address, the address is D, I, Q, M, AI, AQ area
		/// </summary>
		/// <param name="address">fanuc机器人的地址信息</param>
		/// <param name="isBit">是否使用位操作</param>
		/// <returns>解析结果</returns>
		public static OperateResult<byte, ushort> AnalysisFanucAddress(string address, bool isBit)
		{
			try
			{
				if (isBit)
				{
					if (address.StartsWith("sdo") || address.StartsWith("SDO"))
					{
						int num = Convert.ToInt32(address.Substring(3));
						if (num < 11001)
						{
							return OperateResult.CreateSuccessResult((byte)70, (ushort)num);
						}
						return OperateResult.CreateSuccessResult((byte)76, (ushort)(num - 11000));
					}
					if (address.StartsWith("sdi") || address.StartsWith("SDI"))
					{
						return OperateResult.CreateSuccessResult((byte)72, ushort.Parse(address.Substring(3)));
					}
					if (address.StartsWith("rdi") || address.StartsWith("RDI"))
					{
						return OperateResult.CreateSuccessResult((byte)72, (ushort)(Convert.ToInt32(address.Substring(3)) + 5000));
					}
					if (address.StartsWith("rdo") || address.StartsWith("RDO"))
					{
						return OperateResult.CreateSuccessResult((byte)70, (ushort)(Convert.ToInt32(address.Substring(3)) + 5000));
					}
					if (address.StartsWith("ui") || address.StartsWith("UI"))
					{
						return OperateResult.CreateSuccessResult((byte)72, (ushort)(Convert.ToInt32(address.Substring(2)) + 6000));
					}
					if (address.StartsWith("uo") || address.StartsWith("UO"))
					{
						return OperateResult.CreateSuccessResult((byte)70, (ushort)(Convert.ToInt32(address.Substring(2)) + 6000));
					}
					if (address.StartsWith("si") || address.StartsWith("SI"))
					{
						return OperateResult.CreateSuccessResult((byte)72, (ushort)(Convert.ToInt32(address.Substring(2)) + 7000));
					}
					if (address.StartsWith("so") || address.StartsWith("SO"))
					{
						return OperateResult.CreateSuccessResult((byte)70, (ushort)(Convert.ToInt32(address.Substring(2)) + 7000));
					}
				}
				else
				{
					if (address.StartsWith("gi") || address.StartsWith("GI"))
					{
						return OperateResult.CreateSuccessResult((byte)12, ushort.Parse(address.Substring(2)));
					}
					if (address.StartsWith("go") || address.StartsWith("GO"))
					{
						int num2 = Convert.ToInt32(address.Substring(2));
						if (num2 >= 10001)
						{
							num2 -= 6000;
						}
						return OperateResult.CreateSuccessResult((byte)10, (ushort)num2);
					}
				}
				if (address.StartsWith("aq") || address.StartsWith("AQ"))
				{
					return OperateResult.CreateSuccessResult((byte)12, ushort.Parse(address.Substring(2)));
				}
				if (address.StartsWith("ai") || address.StartsWith("AI"))
				{
					return OperateResult.CreateSuccessResult((byte)10, ushort.Parse(address.Substring(2)));
				}
				if (address.StartsWith("sr") || address.StartsWith("SR"))
				{
					ushort num3 = ushort.Parse(address.Substring(2));
					if (num3 < 1 || num3 > 6)
					{
						return new OperateResult<byte, ushort>("SR type address only support SR1 - SR6");
					}
					return OperateResult.CreateSuccessResult((byte)8, (ushort)(5891 + (num3 - 1) * 40));
				}
				if (address.StartsWith("i") || address.StartsWith("I"))
				{
					return OperateResult.CreateSuccessResult((byte)70, ushort.Parse(address.Substring(1)));
				}
				if (address.StartsWith("q") || address.StartsWith("Q"))
				{
					return OperateResult.CreateSuccessResult((byte)72, ushort.Parse(address.Substring(1)));
				}
				if (address.StartsWith("m") || address.StartsWith("M"))
				{
					return OperateResult.CreateSuccessResult((byte)76, ushort.Parse(address.Substring(1)));
				}
				if (address.StartsWith("d") || address.StartsWith("D"))
				{
					return OperateResult.CreateSuccessResult((byte)8, ushort.Parse(address.Substring(1)));
				}
				if (address.StartsWith("r") || address.StartsWith("R"))
				{
					ushort num4 = ushort.Parse(address.Substring(1));
					if (num4 < 1 || num4 > 10)
					{
						return new OperateResult<byte, ushort>("R type address only support R1 - R10");
					}
					return OperateResult.CreateSuccessResult((byte)8, (ushort)(3451 + (num4 - 1) * 2));
				}
				return new OperateResult<byte, ushort>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte, ushort>(ex.Message);
			}
		}

		/// <summary>
		/// 构建读取数据的报文内容
		/// </summary>
		/// <param name="sel">数据类别</param>
		/// <param name="address">偏移地址</param>
		/// <param name="length">长度</param>
		/// <returns>报文内容</returns>
		public static byte[] BulidReadData(byte sel, ushort address, ushort length)
		{
			byte[] array = new byte[56]
			{
				2, 0, 6, 0, 0, 0, 0, 0, 0, 1,
				0, 0, 0, 0, 0, 0, 0, 1, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				6, 192, 0, 0, 0, 0, 16, 14, 0, 0,
				1, 1, 4, 8, 0, 0, 2, 0, 0, 0,
				0, 0, 0, 0, 0, 0
			};
			array[43] = sel;
			array[44] = BitConverter.GetBytes(address - 1)[0];
			array[45] = BitConverter.GetBytes(address - 1)[1];
			array[46] = BitConverter.GetBytes(length)[0];
			array[47] = BitConverter.GetBytes(length)[1];
			return array;
		}

		/// <summary>
		/// 构建读取返回的数据信息
		/// </summary>
		/// <param name="data">数据</param>
		/// <returns>结果</returns>
		public static byte[] BuildReadResponseData(byte[] data)
		{
			byte[] array = SoftBasic.HexStringToBytes("\r\n03 00 06 00 e4 2f 00 00 00 01 00 00 00 00 00 00\r\n00 01 00 00 00 00 00 00 00 00 00 00 00 00 06 94\r\n10 0e 00 00 30 3a 00 00 01 01 00 00 00 00 00 00\r\n01 01 ff 04 00 00 7c 21");
			if (data.Length > 6)
			{
				array = SoftBasic.SpliceArray<byte>(array, data);
				array[4] = BitConverter.GetBytes(data.Length)[0];
				array[5] = BitConverter.GetBytes(data.Length)[1];
				return array;
			}
			array[4] = 0;
			array[5] = 0;
			array[31] = 212;
			data.CopyTo(array, 44);
			return array;
		}

		/// <summary>
		/// 构建写入的数据报文，需要指定相关的参数信息
		/// </summary>
		/// <param name="sel">数据类别</param>
		/// <param name="address">偏移地址</param>
		/// <param name="value">原始数据内容</param>
		/// <param name="length">写入的数据长度</param>
		/// <returns>报文内容</returns>
		public static byte[] BuildWriteData(byte sel, ushort address, byte[] value, int length)
		{
			if (value == null)
			{
				value = new byte[0];
			}
			if (value.Length > 6)
			{
				byte[] array = new byte[56 + value.Length];
				byte[] array2 = new byte[56]
				{
					2, 0, 9, 0, 50, 0, 0, 0, 0, 2,
					0, 0, 0, 0, 0, 0, 0, 2, 0, 0,
					0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
					9, 128, 0, 0, 0, 0, 16, 14, 0, 0,
					1, 1, 50, 0, 0, 0, 0, 0, 1, 1,
					7, 8, 49, 0, 25, 0
				};
				array2.CopyTo(array, 0);
				value.CopyTo(array, 56);
				array[4] = BitConverter.GetBytes(value.Length)[0];
				array[5] = BitConverter.GetBytes(value.Length)[1];
				array[51] = sel;
				array[52] = BitConverter.GetBytes(address - 1)[0];
				array[53] = BitConverter.GetBytes(address - 1)[1];
				array[54] = BitConverter.GetBytes(length)[0];
				array[55] = BitConverter.GetBytes(length)[1];
				return array;
			}
			byte[] array3 = new byte[56]
			{
				2, 0, 8, 0, 0, 0, 0, 0, 0, 1,
				0, 0, 0, 0, 0, 0, 0, 1, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				8, 192, 0, 0, 0, 0, 16, 14, 0, 0,
				1, 1, 7, 8, 9, 0, 4, 0, 1, 0,
				2, 0, 3, 0, 4, 0
			};
			array3[43] = sel;
			array3[44] = BitConverter.GetBytes(address - 1)[0];
			array3[45] = BitConverter.GetBytes(address - 1)[1];
			array3[46] = BitConverter.GetBytes(length)[0];
			array3[47] = BitConverter.GetBytes(length)[1];
			value.CopyTo(array3, 48);
			return array3;
		}

		/// <summary>
		/// 获取所有的命令信息<br />
		/// Get all command information
		/// </summary>
		/// <returns>命令数组</returns>
		public static string[] GetFanucCmds()
		{
			return new string[60]
			{
				"CLRASG", "SETASG 1 500 ALM[E1] 1", "SETASG 501 100 ALM[1] 1", "SETASG 601 100 ALM[P1] 1", "SETASG 701 50 POS[15] 0.0", "SETASG 751 50 POS[15] 0.0", "SETASG 801 50 POS[G2: 15] 0.0", "SETASG 851 50 POS[G3: 0] 0.0", "SETASG 901 50 POS[G4:0] 0.0", "SETASG 951 50 POS[G5:0] 0.0",
				"SETASG 1001 18 PRG[1] 1", "SETASG 1019 18 PRG[M1] 1", "SETASG 1037 18 PRG[K1] 1", "SETASG 1055 18 PRG[MK1] 1", "SETASG 1073 500 PR[1] 0.0", "SETASG 1573 200 PR[G2:1] 0.0", "SETASG 1773 500 PR[G3:1] 0.0", "SETASG 2273 500 PR[G4: 1] 0.0", "SETASG 2773 500 PR[G5: 1] 0.0", "SETASG 3273 2 $FAST_CLOCK 1",
				"SETASG 3275 2 $TIMER[10].$TIMER_VAL 1", "SETASG 3277 2 $MOR_GRP[1].$CURRENT_ANG[1] 0", "SETASG 3279 2 $DUTY_TEMP 0", "SETASG 3281 40 $TIMER[10].$COMMENT 1", "SETASG 3321 40 $TIMER[2].$COMMENT 1", "SETASG 3361 50 $MNUTOOL[1,1] 0.0", "SETASG 3411 40 $[HTTPKCL]CMDS[1] 1", "SETASG 3451 10 R[1] 1.0", "SETASG 3461 10 R[6] 0", "SETASG 3471 250 PR[1]@1.25 0.0",
				"SETASG 3721 250 PR[1]@1.25 0.0", "SETASG 3971 120 PR[G2:1]@27.12 0.0", "SETASG 4091 120 DI[C1] 1", "SETASG 4211 120 DO[C1] 1", "SETASG 4331 120 RI[C1] 1", "SETASG 4451 120 RO[C1] 1", "SETASG 4571 120 UI[C1] 1", "SETASG 4691 120 UO[C1] 1", "SETASG 4811 120 SI[C1] 1", "SETASG 4931 120 SO[C1] 1",
				"SETASG 5051 120 WI[C1] 1", "SETASG 5171 120 WO[C1] 1", "SETASG 5291 120 WSI[C1] 1", "SETASG 5411 120 AI[C1] 1", "SETASG 5531 120 AO[C1] 1", "SETASG 5651 120 GI[C1] 1", "SETASG 5771 120 GO[C1] 1", "SETASG 5891 120 SR[1] 1", "SETASG 6011 120 SR[C1] 1", "SETASG 6131 10 R[1] 1.0",
				"SETASG 6141 2 $TIMER[1].$TIMER_VAL 1", "SETASG 6143 2 $TIMER[2].$TIMER_VAL 1", "SETASG 6145 2 $TIMER[3].$TIMER_VAL 1", "SETASG 6147 2 $TIMER[4].$TIMER_VAL 1", "SETASG 6149 2 $TIMER[5].$TIMER_VAL 1", "SETASG 6151 2 $TIMER[6].$TIMER_VAL 1", "SETASG 6153 2 $TIMER[7].$TIMER_VAL 1", "SETASG 6155 2 $TIMER[8].$TIMER_VAL 1", "SETASG 6157 2 $TIMER[9].$TIMER_VAL 1", "SETASG 6159 2 $TIMER[10].$TIMER_VAL 1"
			};
		}
	}
}
