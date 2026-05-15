using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.LSIS.Helper
{
	/// <summary>
	/// Cnet的辅助类
	/// </summary>
	public class LSCnetHelper
	{
		private const string CnetTypes = "PMLKFTCDSQINUZR";

		/// <summary>
		/// 根据错误号，获取到真实的错误描述信息<br />
		/// According to the error number, get the real error description information
		/// </summary>
		/// <param name="err">错误号</param>
		/// <returns>真实的错误描述信息</returns>
		public static string GetErrorText(int err)
		{
			return err switch
			{
				3 => StringResources.Language.LsisCnet0003, 
				4 => StringResources.Language.LsisCnet0004, 
				7 => StringResources.Language.LsisCnet0007, 
				17 => StringResources.Language.LsisCnet0011, 
				144 => StringResources.Language.LsisCnet0090, 
				400 => StringResources.Language.LsisCnet0190, 
				656 => StringResources.Language.LsisCnet0290, 
				4402 => StringResources.Language.LsisCnet1132, 
				4658 => StringResources.Language.LsisCnet1232, 
				4660 => StringResources.Language.LsisCnet1234, 
				4914 => StringResources.Language.LsisCnet1332, 
				5170 => StringResources.Language.LsisCnet1432, 
				28978 => StringResources.Language.LsisCnet7132, 
				_ => StringResources.Language.UnknownError, 
			};
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkDoubleBase.UnpackResponseContent(System.Byte[],System.Byte[])" />
		public static OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			try
			{
				if (response[0] == 6)
				{
					if (response[3] == 87 || response[3] == 119)
					{
						return OperateResult.CreateSuccessResult(response);
					}
					string @string = Encoding.ASCII.GetString(response, 4, 2);
					if (@string == "SS")
					{
						int num = Convert.ToInt32(Encoding.ASCII.GetString(response, 6, 2), 16);
						int num2 = 8;
						List<byte> list = new List<byte>();
						for (int i = 0; i < num; i++)
						{
							int num3 = Convert.ToInt32(Encoding.ASCII.GetString(response, num2, 2), 16);
							list.AddRange(Encoding.ASCII.GetString(response, num2 + 2, num3 * 2).ToHexBytes());
							num2 += 2 + num3 * 2;
						}
						return OperateResult.CreateSuccessResult(list.ToArray());
					}
					if (@string == "SB")
					{
						int num4 = Convert.ToInt32(Encoding.ASCII.GetString(response, 8, 2), 16);
						byte[] value = Encoding.ASCII.GetString(response, 10, num4 * 2).ToHexBytes();
						return OperateResult.CreateSuccessResult(value);
					}
					return new OperateResult<byte[]>(1, "Command Wrong:" + @string + Environment.NewLine + "Source: " + response.ToHexString());
				}
				if (response[0] == 21)
				{
					int err = Convert.ToInt32(Encoding.ASCII.GetString(response, 6, 4), 16);
					return new OperateResult<byte[]>(err, GetErrorText(err));
				}
				return new OperateResult<byte[]>(response[0], "Source: " + SoftBasic.GetAsciiStringRender(response));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(1, "Wrong:" + ex.Message + Environment.NewLine + "Source: " + response.ToHexString());
			}
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="Address"></param>
		/// <param name="IsWrite"></param>
		/// <returns></returns>
		public static string GetAddressOfU_Q_I(string Address, bool IsWrite = false)
		{
			string[] array = Address.Split('.');
			object obj = 0;
			if (array.Length >= 3)
			{
				int num = Convert.ToInt32(array[2].Last().ToString(), 16);
				obj = ((!(LSFastEnet.IsHex(array[2]) && IsWrite)) ? ((object)((int.Parse(array[0]) * 32 + int.Parse(array[1])) * 10 + num)) : (int.Parse(array[0]) * 32 + int.Parse(array[1]) + array[2]));
			}
			else
			{
				obj = int.Parse(array[0]) * 32 + int.Parse(array[1]);
			}
			return $"{obj}";
		}

		/// <summary>
		/// 从输入的地址里解析出真实的可以直接放到协议的地址信息，如果是X的地址，自动转换带小数点的表示方式到位地址，如果是其他类型地址，则一律统一转化为字节为单位的地址<br />
		/// The real address information that can be directly put into the protocol is parsed from the input address. If it is the address of X, 
		/// it will automatically convert the representation with a decimal point to the address. If it is an address of other types, it will be uniformly converted into a unit of bytes. address
		/// </summary>
		/// <param name="address">输入的起始偏移地址</param>
		/// <param name="IsWrite">是否转换为bool地址</param>
		/// <returns>analysis result</returns>
		public static OperateResult<string> AnalysisAddress(string address, bool IsWrite = false)
		{
			bool flag = false;
			StringBuilder stringBuilder = new StringBuilder();
			try
			{
				if (!"PMLKFTCDSQINUZR".Contains(address[0]))
				{
					return new OperateResult<string>(StringResources.Language.NotSupportedDataType);
				}
				stringBuilder.Append("%");
				stringBuilder.Append(address[0]);
				if (address.IndexOf('.') > 0)
				{
					flag = true;
				}
				if (address[1] == 'X')
				{
					stringBuilder.Append("X");
					if (flag)
					{
						if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
						{
							int bitIndexInformation = HslHelper.GetBitIndexInformation(ref address);
							stringBuilder.Append(address.Substring(2));
							stringBuilder.Append(bitIndexInformation.ToString("X1"));
						}
						else
						{
							stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2), IsWrite));
						}
					}
					else
					{
						stringBuilder.Append(address.Substring(2));
					}
				}
				else
				{
					int num = 0;
					int num2 = 0;
					switch (address[1])
					{
					case 'B':
						stringBuilder.Append(flag ? "X" : "B");
						if (flag)
						{
							if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								num2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(num2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
						}
						else if (address[0] == 'I' || address[0] == 'Q')
						{
							stringBuilder.Append(address.Substring(2));
						}
						else
						{
							num = Convert.ToInt32(address.Substring(2)) * 2;
						}
						break;
					case 'W':
						stringBuilder.Append(flag ? "X" : "W");
						if (flag)
						{
							if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								num2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(num2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
						}
						else if (address[0] == 'I' || address[0] == 'Q')
						{
							stringBuilder.Append(address.Substring(2));
						}
						else
						{
							num = Convert.ToInt32(address.Substring(2)) * 2;
						}
						break;
					case 'D':
						stringBuilder.Append(flag ? "X" : "D");
						if (flag)
						{
							if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								num2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(num2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
						}
						else if (address[0] == 'I' || address[0] == 'Q')
						{
							stringBuilder.Append(address.Substring(2));
						}
						else
						{
							num = Convert.ToInt32(address.Substring(2)) * 4;
						}
						break;
					case 'L':
						stringBuilder.Append(flag ? "X" : "L");
						if (flag)
						{
							if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								num2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(num2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
						}
						else if (address[0] == 'I' || address[0] == 'Q')
						{
							stringBuilder.Append(address.Substring(2));
						}
						else
						{
							num = Convert.ToInt32(address.Substring(2)) * 8;
						}
						break;
					default:
						stringBuilder.Append(flag ? "X" : "B");
						if (flag)
						{
							if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								num2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(1));
								stringBuilder.Append(num2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(1)));
							}
						}
						else if (address[0] == 'I' || address[0] == 'Q')
						{
							stringBuilder.Append(address.Substring(1));
						}
						else
						{
							num = Convert.ToInt32(address.Substring(1)) * 2;
						}
						break;
					}
					if (flag)
					{
						stringBuilder.Append(num / 2);
						stringBuilder.Append(num2.ToString("X1"));
					}
					else if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
					{
						stringBuilder.Append(num);
					}
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
			return OperateResult.CreateSuccessResult(stringBuilder.ToString());
		}

		/// <summary>
		/// 往现有的命令数据中增加BCC的内容
		/// </summary>
		/// <param name="command">现有的命令</param>
		private static void AddBccTail(List<byte> command)
		{
			int num = 0;
			for (int i = 0; i < command.Count; i++)
			{
				num += command[i];
			}
			command.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)num));
		}

		/// <summary>
		/// reading address  Type of ReadByte
		/// </summary>
		/// <param name="station">plc station</param>
		/// <param name="address">address, for example: M100, D100, DW100</param>
		/// <param name="length">read length</param>
		/// <returns>command bytes</returns>
		public static OperateResult<List<byte[]>> BuildReadByteCommand(byte station, string address, ushort length)
		{
			OperateResult<LsisCnetAddress> operateResult = LsisCnetAddress.ParseFrom(address, length, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
			}
			List<byte[]> list = new List<byte[]>();
			int[] array = SoftBasic.SplitIntegerToArray(length, 254);
			for (int i = 0; i < array.Length; i++)
			{
				List<byte> list2 = new List<byte>();
				list2.Add(5);
				list2.AddRange(SoftBasic.BuildAsciiBytesFrom(station));
				list2.Add(114);
				list2.Add(83);
				list2.Add(66);
				string addressCommand = operateResult.Content.GetAddressCommand();
				list2.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)addressCommand.Length));
				list2.AddRange(Encoding.ASCII.GetBytes(addressCommand));
				list2.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)array[i]));
				list2.Add(4);
				AddBccTail(list2);
				list.Add(list2.ToArray());
				operateResult.Content.AddressStart += array[i];
			}
			return OperateResult.CreateSuccessResult(list);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.BuildReadIndividualCommand(System.Byte,System.String[])" />
		public static OperateResult<byte[]> BuildReadIndividualCommand(byte station, string address)
		{
			return BuildReadIndividualCommand(station, new string[1] { address });
		}

		/// <summary>
		/// Multi reading address Type of Read Individual
		/// </summary>
		/// <param name="station">plc station</param>
		/// <param name="addresses">address, for example: MX100, PX100</param>
		/// <returns></returns>
		public static OperateResult<byte[]> BuildReadIndividualCommand(byte station, string[] addresses)
		{
			List<byte> list = new List<byte>();
			list.Add(5);
			list.AddRange(SoftBasic.BuildAsciiBytesFrom(station));
			list.Add(114);
			list.Add(83);
			list.Add(83);
			list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)addresses.Length));
			if (addresses.Length > 1)
			{
				foreach (string text in addresses)
				{
					string text2 = (text.StartsWith("%") ? text : ("%" + text));
					list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)text2.Length));
					list.AddRange(Encoding.ASCII.GetBytes(text2));
				}
			}
			else
			{
				foreach (string address in addresses)
				{
					OperateResult<string> operateResult = AnalysisAddress(address);
					if (!operateResult.IsSuccess)
					{
						return OperateResult.CreateFailedResult<byte[]>(operateResult);
					}
					list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)operateResult.Content.Length));
					list.AddRange(Encoding.ASCII.GetBytes(operateResult.Content));
				}
			}
			list.Add(4);
			AddBccTail(list);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// write data to address  Type of ReadByte
		/// </summary>
		/// <param name="station">plc station</param>
		/// <param name="address">address, for example: M100, D100, DW100</param>
		/// <param name="value">source value</param>
		/// <returns>command bytes</returns>
		public static OperateResult<byte[]> BuildWriteByteCommand(byte station, string address, byte[] value)
		{
			OperateResult<LsisCnetAddress> operateResult = LsisCnetAddress.ParseFrom(address, 0, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			list.Add(5);
			list.AddRange(SoftBasic.BuildAsciiBytesFrom(station));
			list.Add(119);
			list.Add(83);
			list.Add(66);
			string addressCommand = operateResult.Content.GetAddressCommand();
			list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)addressCommand.Length));
			list.AddRange(Encoding.ASCII.GetBytes(addressCommand));
			list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)value.Length));
			list.AddRange(SoftBasic.BytesToAsciiBytes(value));
			list.Add(4);
			AddBccTail(list);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// write data to address  Type of One
		/// </summary>
		/// <param name="station">plc station</param>
		/// <param name="address">address, for example: M100, D100, DW100</param>
		/// <param name="value">source value</param>
		/// <returns>command bytes</returns>
		public static OperateResult<byte[]> BuildWriteOneCommand(byte station, string address, byte[] value)
		{
			OperateResult<string> operateResult = AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			list.Add(5);
			list.AddRange(SoftBasic.BuildAsciiBytesFrom(station));
			list.Add(119);
			list.Add(83);
			list.Add(83);
			list.Add(48);
			list.Add(49);
			list.AddRange(SoftBasic.BuildAsciiBytesFrom((byte)operateResult.Content.Length));
			list.AddRange(Encoding.ASCII.GetBytes(operateResult.Content));
			list.AddRange(SoftBasic.BytesToAsciiBytes(value));
			list.Add(4);
			AddBccTail(list);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// write data to address  Type of ReadByte
		/// </summary>
		/// <param name="station">plc station</param>
		/// <param name="address">address, for example: M100, D100, DW100</param>
		/// <param name="value">source value</param>
		/// <returns>command bytes</returns>
		public static OperateResult<byte[]> BuildWriteCommand(byte station, string address, byte[] value)
		{
			OperateResult<string> dataTypeToAddress = LSFastEnet.GetDataTypeToAddress(address);
			if (!dataTypeToAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataTypeToAddress);
			}
			switch (dataTypeToAddress.Content)
			{
			case "Bit":
				return BuildWriteOneCommand(station, address, value);
			case "Word":
			case "DWord":
			case "LWord":
			case "Continuous":
				return BuildWriteByteCommand(station, address, value);
			default:
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
			}
		}

		/// <summary>
		/// 从PLC的指定地址读取原始的字节数据信息，地址示例：MB100, MW100, MD100, 如果输入了M100等同于MB100<br />
		/// Read the original byte data information from the designated address of the PLC. 
		/// Examples of addresses: MB100, MW100, MD100, if the input M100 is equivalent to MB100
		/// </summary>
		/// <remarks>
		/// 地址类型支持 P,M,L,K,F,T,C,D,R,I,Q,W, 支持携带站号的形式，例如 s=2;MW100
		/// </remarks>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="address">PLC的地址信息，例如 M100, MB100, MW100, MD100</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult<byte[]> Read(IReadWriteDeviceStation plc, string address, ushort length)
		{
			byte station = (byte)HslHelper.ExtractParameter(ref address, "s", plc.Station);
			OperateResult<List<byte[]>> operateResult = BuildReadByteCommand(station, address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return plc.ReadFromCoreServer(operateResult.Content);
		}

		/// <summary>
		/// 从PLC设备读取多个地址的数据信息，返回连续的字节数组，需要按照实际情况进行按顺序解析。<br />
		/// Read the data information of multiple addresses from the PLC device and return a continuous byte array, which needs to be parsed in order according to the actual situation.
		/// </summary>
		/// <remarks>按照每16个地址长度进行自动的切割，支持任意的多的长度地址</remarks>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 M100, MB100, MW100, MD100</param>
		/// <returns>结果对象数据</returns>
		public static OperateResult<byte[]> Read(IReadWriteDevice plc, int station, string[] address)
		{
			List<string[]> list = SoftBasic.ArraySplitByLength(address, 16);
			List<byte> list2 = new List<byte>(32);
			for (int i = 0; i < list.Count; i++)
			{
				OperateResult<byte[]> operateResult = BuildReadIndividualCommand((byte)station, list[i]);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				list2.AddRange(operateResult2.Content);
			}
			return OperateResult.CreateSuccessResult(list2.ToArray());
		}

		/// <summary>
		/// 将原始数据写入到PLC的指定的地址里，地址示例：MB100, MW100, MD100, 如果输入了M100等同于MB100<br />
		/// Write the original data to the designated address of the PLC. 
		/// Examples of addresses: MB100, MW100, MD100, if input M100 is equivalent to MB100
		/// </summary>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 M100, MB100, MW100, MD100</param>
		/// <param name="value">等待写入的原始数据内容</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(IReadWriteDevice plc, int station, string address, byte[] value)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> operateResult = BuildWriteByteCommand(station2, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return plc.ReadFromCoreServer(operateResult.Content);
		}

		/// <summary>
		/// 从PLC的指定地址读取原始的位数据信息，地址示例：MX100, MX10A<br />
		/// Read the original bool data information from the designated address of the PLC. 
		/// Examples of addresses: MX100, MX10A
		/// </summary>
		/// <remarks>
		/// 地址类型支持 P,M,L,K,F,T,C,D,R,I,Q,W, 支持携带站号的形式，例如 s=2;MX100
		/// </remarks>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 MX100, MX10A</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult<bool> ReadBool(IReadWriteDevice plc, int station, string address)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			int bitIndexInformation = HslHelper.GetBitIndexInformation(ref address);
			OperateResult<byte[]> operateResult = BuildReadIndividualCommand(station2, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content.ToBoolArray()[bitIndexInformation]);
		}

		/// <summary>
		/// 从PLC的指定地址读取原始的位数据信息，地址示例：MB100.0, MW100.0<br />
		/// Read the original bool data information from the designated address of the PLC. 
		/// Examples of addresses: MB100.0, MW100.0
		/// </summary>
		/// <remarks>
		/// 地址类型支持 P,M,L,K,F,T,C,D,R,I,Q,W, 支持携带站号的形式，例如 s=2;MB100.0
		/// </remarks>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="address">PLC的地址信息，例如 MB100.0, MW100.0</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult<bool[]> ReadBool(IReadWriteDeviceStation plc, string address, ushort length)
		{
			return HslHelper.ReadBool(plc, address, length, 8);
		}

		/// <summary>
		/// 将bool数据写入到PLC的指定的地址里，地址示例：MX100, MX10A<br />
		/// Write the bool data to the designated address of the PLC. Examples of addresses: MX100, MX10A
		/// </summary>
		/// <remarks>
		/// 地址类型支持 P,M,L,K,F,T,C,D,R,I,Q,W, 支持携带站号的形式，例如 s=2;MX100
		/// </remarks>
		/// <param name="plc">PLC通信对象</param>
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 MX100, MX10A</param>
		/// <param name="value">bool值信息</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult Write(IReadWriteDevice plc, int station, string address, bool value)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<string> operateResult = AnalysisAddress(address, IsWrite: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = BuildWriteOneCommand(station2, operateResult.Content.Substring(1), new byte[1] { (byte)(value ? 1u : 0u) });
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return plc.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Read(HslCommunication.Core.Net.IReadWriteDeviceStation,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IReadWriteDeviceStation plc, string address, ushort length)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", plc.Station);
			OperateResult<List<byte[]>> command = BuildReadByteCommand(stat, address, length);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await plc.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String[])" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IReadWriteDevice plc, int station, string[] address)
		{
			List<string[]> list = SoftBasic.ArraySplitByLength(address, 16);
			List<byte> result = new List<byte>(32);
			for (int i = 0; i < list.Count; i++)
			{
				OperateResult<byte[]> command = BuildReadIndividualCommand((byte)station, list[i]);
				if (!command.IsSuccess)
				{
					return command;
				}
				OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content);
				if (!read.IsSuccess)
				{
					return read;
				}
				result.AddRange(read.Content);
			}
			return OperateResult.CreateSuccessResult(result.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, int station, string address, byte[] value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> command = BuildWriteByteCommand(stat, address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await plc.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String)" />
		public static async Task<OperateResult<bool>> ReadBoolAsync(IReadWriteDevice plc, int station, string address)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			int bitIndex = HslHelper.GetBitIndexInformation(ref address);
			OperateResult<byte[]> command = BuildReadIndividualCommand(stat, address);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(command);
			}
			OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(read);
			}
			return OperateResult.CreateSuccessResult(read.Content.ToBoolArray()[bitIndex]);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.ReadBool(HslCommunication.Core.Net.IReadWriteDeviceStation,System.String,System.UInt16)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(IReadWriteDeviceStation plc, string address, ushort length)
		{
			return await HslHelper.ReadBoolAsync(plc, address, length, 8);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Boolean)" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, int station, string address, bool value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<string> analysis = AnalysisAddress(address, IsWrite: true);
			if (!analysis.IsSuccess)
			{
				return analysis;
			}
			OperateResult<byte[]> command = BuildWriteOneCommand(stat, analysis.Content.Substring(1), new byte[1] { (byte)(value ? 1u : 0u) });
			if (!command.IsSuccess)
			{
				return command;
			}
			return await plc.ReadFromCoreServerAsync(command.Content);
		}
	}
}
