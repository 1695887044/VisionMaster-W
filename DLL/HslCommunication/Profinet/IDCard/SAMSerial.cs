using System;
using System.Collections.Generic;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;
using HslCommunication.Serial;

namespace HslCommunication.Profinet.IDCard
{
	/// <summary>
	/// 基于SAM协议的串口通信类，支持读取身份证的数据信息，详细参见API文档<br />
	/// Network class implemented by Tcp based on the SAM protocol, which supports reading ID card data information, 
	/// see API documentation for details
	/// </summary>
	/// <example>
	/// 在使用之前需要实例化当前的对象，然后根据实际的情况填写好串口的信息，否则连接不上去。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SAMSerialSample.cs" region="Sample1" title="实例化操作" />
	/// 在实际的读取，我们一般放在后台进行循环扫描的操作，参见下面的代码
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\SAMSerialSample.cs" region="Sample2" title="基本的读取操作" />
	/// </example>
	public class SAMSerial : SerialBase
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.#ctor" />
		public SAMSerial()
		{
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SAMMessage();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.ReadSafeModuleNumber" />
		[HslMqttApi]
		public OperateResult<string> ReadSafeModuleNumber()
		{
			byte[] send = PackToSAMCommand(BuildReadCommand(18, byte.MaxValue, null));
			OperateResult<byte[]> operateResult = ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult operateResult2 = CheckADSCommandAndSum(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			return ExtractSafeModuleNumber(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.CheckSafeModuleStatus" />
		[HslMqttApi]
		public OperateResult CheckSafeModuleStatus()
		{
			byte[] send = PackToSAMCommand(BuildReadCommand(18, byte.MaxValue, null));
			OperateResult<byte[]> operateResult = ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult operateResult2 = CheckADSCommandAndSum(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			if (operateResult.Content[9] != 144)
			{
				return new OperateResult(GetErrorDescription(operateResult.Content[9]));
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.SearchCard" />
		[HslMqttApi]
		public OperateResult SearchCard()
		{
			byte[] send = PackToSAMCommand(BuildReadCommand(32, 1, null));
			OperateResult<byte[]> operateResult = ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult operateResult2 = CheckADSCommandAndSum(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			if (operateResult.Content[9] != 159)
			{
				return new OperateResult(GetErrorDescription(operateResult.Content[9]));
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.SelectCard" />
		[HslMqttApi]
		public OperateResult SelectCard()
		{
			byte[] send = PackToSAMCommand(BuildReadCommand(32, 2, null));
			OperateResult<byte[]> operateResult = ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult operateResult2 = CheckADSCommandAndSum(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			if (operateResult.Content[9] != 144)
			{
				return new OperateResult(GetErrorDescription(operateResult.Content[9]));
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.IDCard.SAMTcpNet.ReadCard" />
		[HslMqttApi]
		public OperateResult<IdentityCard> ReadCard()
		{
			byte[] send = PackToSAMCommand(BuildReadCommand(48, 1, null));
			OperateResult<byte[]> operateResult = ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<IdentityCard>(operateResult);
			}
			OperateResult operateResult2 = CheckADSCommandAndSum(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<IdentityCard>(operateResult2);
			}
			return ExtractIdentityCard(operateResult.Content);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SAMSerial[{base.PortName}:{base.BaudRate}]";
		}

		/// <summary>
		/// 将指令进行打包成可以发送的数据对象
		/// </summary>
		/// <param name="command">命令信息</param>
		/// <returns>字节数组</returns>
		public static byte[] PackToSAMCommand(byte[] command)
		{
			byte[] array = new byte[command.Length + 8];
			array[0] = 170;
			array[1] = 170;
			array[2] = 170;
			array[3] = 150;
			array[4] = 105;
			array[5] = BitConverter.GetBytes(array.Length - 7)[1];
			array[6] = BitConverter.GetBytes(array.Length - 7)[0];
			command.CopyTo(array, 7);
			int num = 0;
			for (int i = 5; i < array.Length - 1; i++)
			{
				num ^= array[i];
			}
			array[array.Length - 1] = (byte)num;
			return array;
		}

		/// <summary>
		/// 根据SAM的实际的指令，来生成实际的指令信息
		/// </summary>
		/// <param name="cmd">命令码</param>
		/// <param name="para">参数信息</param>
		/// <param name="data">数据内容</param>
		/// <returns>字符串的结果信息</returns>
		public static byte[] BuildReadCommand(byte cmd, byte para, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[2 + data.Length];
			array[0] = cmd;
			array[1] = para;
			data.CopyTo(array, 2);
			return array;
		}

		/// <summary>
		/// 检查当前的接收数据信息是否一条完整的数据信息
		/// </summary>
		/// <param name="input">输入的信息</param>
		/// <returns>是否接收完成</returns>
		public static bool CheckADSCommandCompletion(List<byte> input)
		{
			if (input != null && input.Count < 8)
			{
				return false;
			}
			if (input[5] * 256 + input[6] > input.Count - 7)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// 检查当前的指令是否是正确的
		/// </summary>
		/// <param name="input">输入的指令信息</param>
		/// <returns>是否校验成功</returns>
		public static OperateResult CheckADSCommandAndSum(byte[] input)
		{
			if (input != null && input.Length < 8)
			{
				return new OperateResult(StringResources.Language.SAMReceiveLengthMustLargerThan8);
			}
			if (input[0] != 170 || input[1] != 170 || input[2] != 170 || input[3] != 150 || input[4] != 105)
			{
				return new OperateResult(StringResources.Language.SAMHeadCheckFailed);
			}
			if (input[5] * 256 + input[6] != input.Length - 7)
			{
				return new OperateResult(StringResources.Language.SAMLengthCheckFailed);
			}
			int num = 0;
			for (int i = 5; i < input.Length - 1; i++)
			{
				num ^= input[i];
			}
			if (num != input[input.Length - 1])
			{
				return new OperateResult(StringResources.Language.SAMSumCheckFailed);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 提炼安全的模块数据信息
		/// </summary>
		/// <param name="data">数据</param>
		/// <returns>结果对象</returns>
		public static OperateResult<string> ExtractSafeModuleNumber(byte[] data)
		{
			try
			{
				if (data[9] != 144)
				{
					return new OperateResult<string>(GetErrorDescription(data[9]));
				}
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append(data[10].ToString("D2"));
				stringBuilder.Append(".");
				stringBuilder.Append(data[12].ToString("D2"));
				stringBuilder.Append("-");
				stringBuilder.Append(BitConverter.ToInt32(data, 14).ToString());
				stringBuilder.Append("-");
				stringBuilder.Append(BitConverter.ToInt32(data, 18).ToString("D9"));
				stringBuilder.Append("-");
				stringBuilder.Append(BitConverter.ToInt32(data, 22).ToString("D9"));
				return OperateResult.CreateSuccessResult(stringBuilder.ToString());
			}
			catch (Exception ex)
			{
				return new OperateResult<string>("Error:" + ex.Message + "  Source Data: " + SoftBasic.ByteToHexString(data));
			}
		}

		/// <summary>
		/// 从数据中提取出真实的身份证信息
		/// </summary>
		/// <param name="data">原始数据内容</param>
		/// <returns>包含结果对象的身份证数据</returns>
		public static OperateResult<IdentityCard> ExtractIdentityCard(byte[] data)
		{
			try
			{
				if (data[9] != 144)
				{
					return new OperateResult<IdentityCard>(GetErrorDescription(data[9]));
				}
				string @string = Encoding.Unicode.GetString(data, 14, 256);
				byte[] portrait = SoftBasic.ArraySelectMiddle(data, 270, 1024);
				IdentityCard identityCard = new IdentityCard();
				identityCard.Name = @string.Substring(0, 15);
				identityCard.Sex = ((@string.Substring(15, 1) == "1") ? "男" : ((@string.Substring(15, 1) == "2") ? "女" : "未知"));
				identityCard.Nation = GetNationText(Convert.ToInt32(@string.Substring(16, 2)));
				identityCard.Birthday = new DateTime(int.Parse(@string.Substring(18, 4)), int.Parse(@string.Substring(22, 2)), int.Parse(@string.Substring(24, 2)));
				identityCard.Address = @string.Substring(26, 35);
				identityCard.Id = @string.Substring(61, 18);
				identityCard.Organ = @string.Substring(79, 15);
				identityCard.ValidityStartDate = new DateTime(int.Parse(@string.Substring(94, 4)), int.Parse(@string.Substring(98, 2)), int.Parse(@string.Substring(100, 2)));
				identityCard.ValidityEndDate = new DateTime(int.Parse(@string.Substring(102, 4)), int.Parse(@string.Substring(106, 2)), int.Parse(@string.Substring(108, 2)));
				identityCard.Portrait = portrait;
				return OperateResult.CreateSuccessResult(identityCard);
			}
			catch (Exception ex)
			{
				return new OperateResult<IdentityCard>(ex.Message);
			}
		}

		/// <summary>
		/// 根据民族的代号来获取到民族的文本描述信息
		/// </summary>
		/// <param name="nation">民族代码</param>
		/// <returns>民族的文本信息</returns>
		public static string GetNationText(int nation)
		{
			return nation switch
			{
				1 => "汉", 
				2 => "蒙古", 
				3 => "回", 
				4 => "藏", 
				5 => "维吾尔", 
				6 => "苗", 
				7 => "彝", 
				8 => "壮", 
				9 => "布依", 
				10 => "朝鲜", 
				11 => "满", 
				12 => "侗", 
				13 => "瑶", 
				14 => "白", 
				15 => "土家", 
				16 => "哈尼", 
				17 => "哈萨克", 
				18 => "傣", 
				19 => "黎", 
				20 => "傈僳", 
				21 => "佤", 
				22 => "畲", 
				23 => "高山", 
				24 => "拉祜", 
				25 => "水", 
				26 => "东乡", 
				27 => "纳西", 
				28 => "景颇", 
				29 => "柯尔克孜", 
				30 => "土", 
				31 => "达斡尔", 
				32 => "仫佬", 
				33 => "羌", 
				34 => "布朗", 
				35 => "撒拉", 
				36 => "毛南", 
				37 => "仡佬", 
				38 => "锡伯", 
				39 => "阿昌", 
				40 => "普米", 
				41 => "塔吉克", 
				42 => "怒", 
				43 => "乌孜别克", 
				44 => "俄罗斯", 
				45 => "鄂温克", 
				46 => "德昂", 
				47 => "保安", 
				48 => "裕固", 
				49 => "京", 
				50 => "塔塔尔", 
				51 => "独龙", 
				52 => "鄂伦春", 
				53 => "赫哲", 
				54 => "门巴", 
				55 => "珞巴", 
				56 => "基诺", 
				97 => "其他", 
				98 => "外国血统中国籍人士", 
				_ => string.Empty, 
			};
		}

		/// <summary>
		/// 枚举当前的所有的民族信息，共计五十六个民族
		/// </summary>
		/// <returns>枚举信息</returns>
		public static IEnumerator<string> GetNationEnumerator()
		{
			for (int i = 1; i < 57; i++)
			{
				yield return GetNationText(i);
			}
		}

		/// <summary>
		/// 获取错误的文本信息
		/// </summary>
		/// <param name="err">错误号</param>
		/// <returns>错误信息</returns>
		public static string GetErrorDescription(int err)
		{
			return err switch
			{
				145 => StringResources.Language.SAMStatus91, 
				16 => StringResources.Language.SAMStatus10, 
				17 => StringResources.Language.SAMStatus11, 
				33 => StringResources.Language.SAMStatus21, 
				35 => StringResources.Language.SAMStatus23, 
				36 => StringResources.Language.SAMStatus24, 
				49 => StringResources.Language.SAMStatus31, 
				50 => StringResources.Language.SAMStatus32, 
				51 => StringResources.Language.SAMStatus33, 
				64 => StringResources.Language.SAMStatus40, 
				65 => StringResources.Language.SAMStatus41, 
				71 => StringResources.Language.SAMStatus47, 
				96 => StringResources.Language.SAMStatus60, 
				102 => StringResources.Language.SAMStatus66, 
				128 => StringResources.Language.SAMStatus80, 
				129 => StringResources.Language.SAMStatus81, 
				_ => StringResources.Language.UnknownError, 
			};
		}
	}
}
