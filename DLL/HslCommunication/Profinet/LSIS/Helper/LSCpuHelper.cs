using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;

namespace HslCommunication.Profinet.LSIS.Helper
{
	/// <summary>
	/// <see cref="T:HslCommunication.Profinet.LSIS.LSCpu" />相关的辅助类，提供了一些辅助的静态方法信息
	/// </summary>
	public class LSCpuHelper
	{
		private const string CpuTypes = "PMLKFTCDSQINUZR";

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
					if (response[1] == 110 || response[1] == 111 || response[1] == 119)
					{
						return OperateResult.CreateSuccessResult(response);
					}
					string @string = Encoding.ASCII.GetString(response);
					string text = @string.Substring(1, @string.Length - 2);
					text = text.Substring(1, text.Length - 3);
					return OperateResult.CreateSuccessResult(GetBytesFromHex(text));
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
		/// GetBytesFromHex
		/// </summary>
		/// <param name="IP"></param>
		/// <returns></returns>
		public static byte[] GetBytesFromHex(string IP)
		{
			byte[] array = new byte[IP.Length / 2];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = Convert.ToByte(IP.Substring(i * 2, 2), 16);
			}
			return array;
		}

		/// <summary>
		/// 从输入的地址里解析出真实的可以直接放到协议的地址信息，如果是X的地址，自动转换带小数点的表示方式到位地址，如果是其他类型地址，则一律统一转化为字节为单位的地址<br />
		/// The real address information that can be directly put into the protocol is parsed from the input address. If it is the address of X, 
		/// it will automatically convert the representation with a decimal point to the address. If it is an address of other types, it will be uniformly converted into a unit of bytes. address
		/// </summary>
		/// <param name="address">输入的起始偏移地址</param>
		/// <param name="transBit">是否转换为bool地址</param>
		/// <returns>analysis result</returns>
		public static OperateResult<string> AnalysisAddress(string address, bool transBit = false)
		{
			StringBuilder stringBuilder = new StringBuilder();
			try
			{
				if (!"PMLKFTCDSQINUZR".Contains(address[0]))
				{
					return new OperateResult<string>(StringResources.Language.NotSupportedDataType);
				}
				stringBuilder.Append(address[0]);
				if (address[0] == 'M')
				{
					stringBuilder.Append("X");
					if (transBit & (address.IndexOf('.') > 0))
					{
						int bitIndexInformation = HslHelper.GetBitIndexInformation(ref address);
						stringBuilder.Append(address.Substring(2));
						stringBuilder.Append(bitIndexInformation.ToString("X1"));
					}
					else
					{
						stringBuilder.Append(address.Substring(2, address.Length - 2));
					}
				}
				else
				{
					stringBuilder.Append("W");
					stringBuilder.Append(Convert.ToInt32(address.Substring(2, address.Length - 2)));
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
		public static OperateResult<byte[]> BuildReadByteCommand(byte station, string address, ushort length)
		{
			List<byte> list = new List<byte>();
			byte b = 0;
			list.Clear();
			int memoryType = GetMemoryType(address);
			int dataType = GetDataType(address);
			int staraddress = HexToOct(address.Substring(1, address.Length - 1));
			list.Add(2);
			list.AddRange(Encoding.ASCII.GetBytes(sprintf("r%C", select_data_code(memoryType))));
			list.AddRange(Encoding.ASCII.GetBytes($"{(byte)0:X4}"));
			list.AddRange(Encoding.ASCII.GetBytes($"{(byte)0:X2}"));
			list.AddRange(Encoding.ASCII.GetBytes($"{(byte)GetDataSize2(staraddress, length, dataType):X2}"));
			for (int i = 1; i <= 10; i++)
			{
				b = (byte)(b + list[i]);
			}
			list.AddRange(Encoding.ASCII.GetBytes($"{b:X2}"));
			list.Add(3);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// build read command. 
		/// </summary>
		/// <param name="station">station</param>
		/// <param name="address">start address</param>
		/// <param name="length">address length</param>
		/// <returns> command</returns>
		public static OperateResult<byte[]> BuildReadCommand(byte station, string address, ushort length)
		{
			return BuildReadByteCommand(station, address, length);
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
			OperateResult<string> operateResult = AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			string text = new string(new char[60]);
			ushort num = 0;
			float[] array = new float[3];
			char[] array2 = new char[4];
			short num2 = BitConverter.ToInt16(value, 0);
			int dataType = GetDataType(operateResult.Content);
			int memoryType = GetMemoryType(operateResult.Content);
			int num3 = int.Parse(operateResult.Content.Substring(2, operateResult.Content.Length - 2));
			if (memoryType == 128)
			{
				Console.WriteLine("Memory Type Input Error. Memory Type = P, M, K, T, C, U, Z, S, L, N, D, R, ZR", num3);
			}
			else
			{
				list.Clear();
				list.Add(2);
				list.AddRange(Encoding.ASCII.GetBytes(sprintf("w%C", select_data_code(memoryType))));
				if (text != null)
				{
					list.AddRange(Encoding.ASCII.GetBytes($"{GetDataSize((ushort)Convert.ToInt32(num3), 2):X2}{(byte)0:X2}"));
				}
				list.AddRange(Encoding.ASCII.GetBytes($"{(byte)0:X2}"));
				list.AddRange(Encoding.ASCII.GetBytes($"{(byte)GetDataSize2(num3, 1, dataType):X2}"));
				int num4;
				switch (dataType)
				{
				case 1:
					list.AddRange(Encoding.ASCII.GetBytes($"{(byte)num2:X2}"));
					num4 = 13;
					break;
				case 3:
				{
					short num5 = (short)((ulong)num2 >> 16);
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)num2:X2}{(byte)num2:X2}"));
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)num5:X2}{(ulong)num2 >> 24:X2}"));
					num4 = 18;
					break;
				}
				case 4:
					array[0] = num2;
					Array.Copy(array2, array, 2);
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)array2[0]:X2}{(uint)array2[1]:X2}"));
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)array2[2]:X2}{(uint)array2[3]:X2}"));
					num4 = 18;
					break;
				case 5:
					array[0] = num2;
					Array.Copy(array2, array, 2);
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)array2[3]:X2}{(uint)array2[2]:X2}"));
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)array2[1]:X2}{(uint)array2[0]:X2}"));
					num4 = 18;
					break;
				default:
					list.AddRange(Encoding.ASCII.GetBytes($"{(sbyte)num2:X2}{num2 >> 8:X2}"));
					num4 = 14;
					break;
				}
				for (int i = 1; i <= num4; i++)
				{
					num = (ushort)(num + list[i]);
				}
				list.AddRange(Encoding.ASCII.GetBytes($"{(byte)num:X2}"));
				list.Add(3);
			}
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
			string text = new string(new char[31]);
			ushort num = 0;
			int memoryType = GetMemoryType(operateResult.Content);
			int num2 = HexToOct(operateResult.Content.Substring(2, operateResult.Content.Length - 2));
			string text2 = value[0].ToString("X2");
			if (memoryType == 128)
			{
				Console.WriteLine("Memory Type Input Error. Memory Type = P, M, K, T, C, U, Z, S, L, N, D, R, ZR", address);
			}
			else
			{
				list.Clear();
				list.Add(2);
				if (text2 == "01")
				{
					list.AddRange(Encoding.ASCII.GetBytes(sprintf("o%C", select_data_code(memoryType))));
				}
				else
				{
					list.AddRange(Encoding.ASCII.GetBytes(sprintf("n%C", select_data_code(memoryType))));
				}
				if (memoryType != 1)
				{
					text = $"{num2 >> 4:X4}";
					list.AddRange(Encoding.ASCII.GetBytes($"{GetDataSize((ushort)Convert.ToInt32(text), 2):X2}{(byte)0:X2}"));
				}
				else
				{
					list.AddRange(Encoding.ASCII.GetBytes($"{(uint)(sbyte)((int)(ushort)num2 / 16):X2}{(ushort)((int)(ushort)num2 / 16) >> 16:X2}"));
				}
				list.AddRange(Encoding.ASCII.GetBytes($"{(byte)0:X2}"));
				short num3 = ((!(text2 != "00")) ? ((short)(-1.0 - Math.Pow(2.0, num2 % 16))) : ((short)Math.Pow(2.0, num2 % 16)));
				list.AddRange(Encoding.ASCII.GetBytes($"{(sbyte)num3:X2}{HIBYTE(num3):X2}"));
				for (int i = 1; i <= 12; i++)
				{
					num = (ushort)(num + list[i]);
				}
				list.AddRange(Encoding.ASCII.GetBytes($"{(byte)num:X2}"));
				list.Add(3);
			}
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
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 M100, MB100, MW100, MD100</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult<byte[]> Read(IReadWriteDevice plc, int station, string address, ushort length)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> operateResult = BuildReadCommand(station2, address, length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return plc.ReadFromCoreServer(operateResult.Content);
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
			OperateResult<byte[]> operateResult = BuildWriteCommand(station2, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return plc.ReadFromCoreServer(operateResult.Content);
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
		/// <param name="station">站号信息</param>
		/// <param name="address">PLC的地址信息，例如 MB100.0, MW100.0</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		public static OperateResult<bool[]> ReadBool(IReadWriteDevice plc, int station, string address, ushort length)
		{
			int bitIndexInformation = HslHelper.GetBitIndexInformation(ref address);
			int num = HslHelper.CalculateOccupyLength(bitIndexInformation, length);
			OperateResult<byte[]> operateResult = Read(plc, station, address, (ushort)num);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content.ToBoolArray().SelectMiddle(bitIndexInformation, length));
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
			OperateResult<string> operateResult = AnalysisAddress(address, transBit: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = BuildWriteOneCommand(station2, operateResult.Content, new byte[1] { (byte)(value ? 1u : 0u) });
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return plc.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IReadWriteDevice plc, int station, string address, ushort length)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> command = BuildReadCommand(stat, address, length);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await plc.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, int station, string address, byte[] value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> command = BuildWriteCommand(stat, address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await plc.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.UInt16)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(IReadWriteDevice plc, int station, string address, ushort length)
		{
			int bitIndex = HslHelper.GetBitIndexInformation(ref address);
			OperateResult<byte[]> read = await ReadAsync(length: (ushort)HslHelper.CalculateOccupyLength(bitIndex, length), plc: plc, station: station, address: address);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(read);
			}
			return OperateResult.CreateSuccessResult(read.Content.ToBoolArray().SelectMiddle(bitIndex, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Boolean)" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, int station, string address, bool value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<string> analysis = AnalysisAddress(address, transBit: true);
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

		private static int GetMemoryType(string address)
		{
			return address[0] switch
			{
				'P' => 0, 
				'M' => 1, 
				'K' => 2, 
				'F' => 3, 
				'T' => 4, 
				'C' => 5, 
				'U' => 6, 
				'Z' => 7, 
				'S' => 8, 
				'L' => 9, 
				'N' => 10, 
				'D' => 11, 
				'R' => 12, 
				_ => 128, 
			};
		}

		private static int GetDataType(string address)
		{
			return address[1] switch
			{
				'X' => 1, 
				'W' => 2, 
				'D' => 4, 
				'F' => 8, 
				_ => 1, 
			};
		}

		private static string sprintf(string input, params object[] inpVars)
		{
			int i = -1;
			input = Regex.Replace(input, "%.", (Match m) => "{" + ++i + "}");
			return string.Format(input, inpVars);
		}

		private static char select_data_code(int MemoryType)
		{
			return MemoryType switch
			{
				0 => 'h', 
				2 => 'k', 
				3 => 'n', 
				4 => 'd', 
				5 => 'm', 
				6 => 'q', 
				7 => 'z', 
				8 => 'o', 
				9 => 'j', 
				10 => 'p', 
				11 => 'a', 
				12 => 'r', 
				13 => '{', 
				_ => 'i', 
			};
		}

		private static int GetDataSize(int address, int datatype)
		{
			if (datatype == 2)
			{
				return 2 * address;
			}
			if (datatype > 2 && datatype <= 5)
			{
				return 4 * address;
			}
			return 10 * address / 8;
		}

		private static int GetDataSize2(int Staraddress, int DataSize, int datatype)
		{
			int num;
			switch (datatype)
			{
			case 2:
				return 2 * DataSize;
			default:
				num = ((datatype == 5) ? 1 : 0);
				break;
			case 3:
			case 4:
				num = 1;
				break;
			}
			if (num != 0)
			{
				return 4 * DataSize;
			}
			int num2 = 10 * DataSize / 8;
			int num3 = (8 - 10 * Staraddress % 8) % 8;
			if (num3 + 8 * num2 < 10 * DataSize)
			{
				num2++;
			}
			if (num3 != 0)
			{
				num2++;
			}
			return num2;
		}

		private static byte? HIBYTE(short n)
		{
			byte[] array = new byte[4]
			{
				(byte)((uint)(n >> 24) & 0xFFu),
				(byte)((uint)(n >> 16) & 0xFFu),
				(byte)((uint)(n >> 8) & 0xFFu),
				(byte)((uint)n & 0xFFu)
			};
			if ((byte)((n >> 8) & 0xFF) != byte.MaxValue && (byte)((uint)(n >> 8) & 0xFFu) != 0)
			{
				return (byte)((uint)(n >> 8) & 0xFFu);
			}
			if ((byte)(n & 0xFF) != byte.MaxValue && (byte)((uint)n & 0xFFu) != 0)
			{
				return (byte)((uint)n & 0xFFu);
			}
			return array[3];
		}

		private static int HexToOct(string hexNum)
		{
			Dictionary<char, int> dictionary = new Dictionary<char, int>
			{
				{ '0', 0 },
				{ '1', 1 },
				{ '2', 2 },
				{ '3', 3 },
				{ '4', 4 },
				{ '5', 5 },
				{ '6', 6 },
				{ '7', 7 },
				{ '8', 8 },
				{ '9', 9 },
				{ 'A', 10 },
				{ 'B', 11 },
				{ 'C', 12 },
				{ 'D', 13 },
				{ 'E', 14 },
				{ 'F', 15 }
			};
			int num = 0;
			foreach (char key in hexNum)
			{
				num = num * 16 + dictionary[key];
			}
			return num;
		}
	}
}
