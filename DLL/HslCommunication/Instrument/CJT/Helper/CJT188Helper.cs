using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;

namespace HslCommunication.Instrument.CJT.Helper
{
	/// <summary>
	/// CJT188辅助方法
	/// </summary>
	public class CJT188Helper
	{
		/// <summary>
		/// 从用户输入的地址信息中解析出真实的地址及数据标识
		/// </summary>
		/// <param name="address">用户输入的地址信息</param>
		/// <param name="defaultStation">默认的地址域</param>
		/// <returns>解析结果信息</returns>
		public static OperateResult<string, byte[]> AnalysisBytesAddress(string address, string defaultStation)
		{
			try
			{
				string value = defaultStation;
				byte[] array = new byte[3];
				if (address.IndexOf(';') > 0)
				{
					string[] array2 = address.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < array2.Length; i++)
					{
						if (array2[i].StartsWith("s="))
						{
							value = array2[i].Substring(2);
						}
						else
						{
							array2[i].ToHexBytes().Reverse().ToArray()
								.CopyTo(array, 0);
						}
					}
				}
				else
				{
					address.ToHexBytes().Reverse().ToArray()
						.CopyTo(array, 0);
				}
				return OperateResult.CreateSuccessResult(value, array);
			}
			catch (Exception ex)
			{
				return new OperateResult<string, byte[]>("Address prase wrong: " + ex.Message);
			}
		}

		/// <summary>
		/// 将地址解析成BCD码的地址，并且扩充到14位，不够的补0操作
		/// </summary>
		/// <param name="address">地址域信息</param>
		/// <returns>实际的结果</returns>
		public static OperateResult<byte[]> GetAddressByteFromString(string address)
		{
			if (address == null || address.Length == 0)
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressCannotNull);
			}
			if (address.Length > 14)
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressCannotMoreThan12);
			}
			if (!Regex.IsMatch(address, "^[0-9A-A]+$"))
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressMatchFailed);
			}
			if (address.Length < 14)
			{
				address = address.PadLeft(14, '0');
			}
			return OperateResult.CreateSuccessResult(address.ToHexBytes().Reverse().ToArray());
		}

		/// <summary>
		/// 将指定的地址信息，控制码信息，数据域信息打包成完整的报文命令
		/// </summary>
		/// <param name="address">地址域信息，地址域由7个字节构成，每字节2位BCD码，地址长度可达14位十进制数。地址域支持锁位寻址，即从若干低位起，剩余高位补AAH作为通配符进行读表操作</param>
		/// <param name="type">仪表类型</param>
		/// <param name="control">控制码信息</param>
		/// <param name="dataArea">数据域的内容</param>
		/// <returns>返回是否报文创建成功</returns>
		public static OperateResult<byte[]> Build188EntireCommand(string address, byte type, byte control, byte[] dataArea)
		{
			if (dataArea == null)
			{
				dataArea = new byte[0];
			}
			OperateResult<byte[]> addressByteFromString = GetAddressByteFromString(address);
			if (!addressByteFromString.IsSuccess)
			{
				return addressByteFromString;
			}
			byte[] array = new byte[13 + dataArea.Length];
			array[0] = 104;
			array[1] = type;
			addressByteFromString.Content.CopyTo(array, 2);
			array[9] = control;
			array[10] = (byte)dataArea.Length;
			if (dataArea.Length != 0)
			{
				dataArea.CopyTo(array, 11);
			}
			int num = 0;
			for (int i = 0; i < array.Length - 2; i++)
			{
				num += array[i];
			}
			array[array.Length - 2] = (byte)num;
			array[array.Length - 1] = 22;
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 检查当前的反馈数据信息是否正确
		/// </summary>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <param name="response">从仪表反馈的数据信息</param>
		/// <returns>是否校验成功</returns>
		public static OperateResult CheckResponse(ICjt188 cjt, byte[] response)
		{
			if (response.Length < 13)
			{
				return new OperateResult(StringResources.Language.ReceiveDataLengthTooShort);
			}
			if ((response[9] & 0x40) == 64)
			{
				byte b = response[12];
				if (b.GetBoolByIndex(0))
				{
					return new OperateResult("阀门关");
				}
				if (b.GetBoolByIndex(1))
				{
					return new OperateResult("阀门异常");
				}
				if (b.GetBoolByIndex(2))
				{
					return new OperateResult("电池欠压");
				}
				if (b > 0)
				{
					return new OperateResult(b, "厂商定义的异常");
				}
				return OperateResult.CreateSuccessResult();
			}
			return OperateResult.CreateSuccessResult();
		}

		private static OperateResult<byte[]> ExtraActualContent(byte[] dataArea, byte[] content)
		{
			try
			{
				if (dataArea[0] == 10 && dataArea[1] == 129)
				{
					return OperateResult.CreateSuccessResult(content.SelectMiddle(2, 7).Reverse().ToArray());
				}
				if (content.Length < 16)
				{
					return OperateResult.CreateSuccessResult(new byte[0]);
				}
				return OperateResult.CreateSuccessResult(content.SelectMiddle(14, content[10] - 3));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>("ExtraActualContent failed: " + ex.Message + Environment.NewLine + "Content: " + content.ToHexString(' '));
			}
		}

		private static OperateResult<byte[]> ReadWithAddress(ICjt188 cjt, string address, byte[] dataArea)
		{
			OperateResult<byte[]> operateResult = Build188EntireCommand(address, cjt.InstrumentType, 1, dataArea);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = cjt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckResponse(cjt, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			return ExtraActualContent(dataArea, operateResult2.Content);
		}

		private static async Task<OperateResult<byte[]>> ReadWithAddressAsync(ICjt188 cjt, string address, byte[] dataArea)
		{
			OperateResult<byte[]> command = Build188EntireCommand(address, cjt.InstrumentType, 1, dataArea);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await cjt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult check = CheckResponse(cjt, read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(check);
			}
			return ExtraActualContent(dataArea, read.Content);
		}

		/// <summary>
		/// 根据指定的数据标识来读取相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 91-1F，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as 91-1F. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;90-1F" 或是 "s=100000;90-1F"，关于数据域信息，需要查找手册，例如:D1-20 表示： 上一月结算日累积流量
		/// </remarks>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <param name="length">数据长度信息</param>
		/// <returns>结果信息</returns>
		public static OperateResult<byte[]> Read(ICjt188 cjt, string address, int length)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(address, cjt.Station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return ReadWithAddress(cjt, operateResult.Content1, operateResult.Content2);
		}

		/// <summary>
		/// 根据指定的数据标识来写入相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 90-1F，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as 90-1F. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;90-1F" 或是 "s=100000;90-1F"，关于数据域信息，需要查找手册，例如:00-01-00-00 表示： (当前)正向有功总电能<br />
		/// 注意：本命令必须与编程键配合使用
		/// </remarks>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <param name="address">地址信息</param>
		/// <param name="value">写入的数据值</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(ICjt188 cjt, string address, byte[] value)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(address, cjt.Station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			byte[] dataArea = SoftBasic.SpliceArray<byte>(operateResult.Content2, value);
			OperateResult<byte[]> operateResult2 = Build188EntireCommand(operateResult.Content1, cjt.InstrumentType, 4, dataArea);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = cjt.ReadFromCoreServer(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return CheckResponse(cjt, operateResult3.Content);
		}

		/// <summary>
		/// 读取数据的数组信息，需要指定如何从字符串转换的功能方法
		/// </summary>
		/// <typeparam name="T">类型信息</typeparam>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <param name="address">地址信息</param>
		/// <param name="length">数据长度</param>
		/// <param name="trans">转换方法</param>
		/// <returns>包含泛型数组的结果对象</returns>
		public static OperateResult<T[]> ReadValue<T>(ICjt188 cjt, string address, ushort length, Func<string, T> trans)
		{
			OperateResult<string[]> operateResult = ReadStringArray(cjt, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult((from m in operateResult.Content.SelectBegin(length)
				select trans(m)).ToArray());
		}

		/// <summary>
		/// 读取设备的通信地址，仅支持点对点通讯的情况，返回地址域数据，例如：14910000729012<br />
		/// Read the communication address of the device, only support point-to-point communication, and return the address field data, for example: 149100007290
		/// </summary>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <returns>设备的通信地址</returns>
		public static OperateResult<string> ReadAddress(ICjt188 cjt)
		{
			OperateResult<byte[]> operateResult = Build188EntireCommand("AAAAAAAAAAAAAA", cjt.InstrumentType, 3, new byte[3] { 10, 129, 0 });
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = cjt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			OperateResult operateResult3 = CheckResponse(cjt, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult3);
			}
			if (operateResult2.Content.Length >= 9)
			{
				cjt.Station = operateResult2.Content.SelectMiddle(2, 7).Reverse().ToArray()
					.ToHexString();
				return OperateResult.CreateSuccessResult(operateResult2.Content.SelectMiddle(2, 7).Reverse().ToArray()
					.ToHexString());
			}
			return new OperateResult<string>(StringResources.Language.ReceiveDataLengthTooShort + " Content: " + operateResult2.Content.ToHexString(' '));
		}

		/// <summary>
		/// 写入设备的地址域信息，仅支持点对点通讯的情况，需要指定地址域信息，例如：14910000729011<br />
		/// Write the address domain information of the device, only support point-to-point communication, 
		/// you need to specify the address domain information, for example: 14910000729011
		/// </summary>
		/// <param name="cjt">CJT188的通信对象</param>
		/// <param name="address">等待写入的地址域</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult WriteAddress(ICjt188 cjt, string address)
		{
			OperateResult<byte[]> addressByteFromString = GetAddressByteFromString(address);
			if (!addressByteFromString.IsSuccess)
			{
				return addressByteFromString;
			}
			OperateResult<byte[]> operateResult = Build188EntireCommand(cjt.Station, cjt.InstrumentType, 21, addressByteFromString.Content);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = cjt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckResponse(cjt, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (SoftBasic.IsTwoBytesEquel(operateResult2.Content.SelectMiddle(2, 7), GetAddressByteFromString(address).Content))
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.DLTErrorWriteReadCheckFailed);
		}

		private static OperateResult<double, string> GetActualValueAndUnit(byte[] source, int index, int length, double scale, bool hasUnit, string defaultUnit)
		{
			string text = source.SelectMiddle(index, length).Reverse().ToArray()
				.ToHexString();
			double num = 1.0;
			if (hasUnit)
			{
				switch (source[index + length])
				{
				case 2:
					defaultUnit = "Wh";
					num = 1.0;
					break;
				case 3:
					defaultUnit = "Wh";
					num = 10.0;
					break;
				case 4:
					defaultUnit = "Wh";
					num = 100.0;
					break;
				case 5:
					defaultUnit = "Wh";
					num = 1000.0;
					break;
				case 6:
					defaultUnit = "Wh";
					num = 10000.0;
					break;
				case 7:
					defaultUnit = "Wh";
					num = 100000.0;
					break;
				case 8:
					defaultUnit = "Wh";
					num = 1000000.0;
					break;
				case 9:
					defaultUnit = "Wh";
					num = 10000000.0;
					break;
				case 10:
					defaultUnit = "Wh";
					num = 100000000.0;
					break;
				case 1:
					defaultUnit = "J";
					num = 1.0;
					break;
				case 11:
					defaultUnit = "J";
					num = 1000.0;
					break;
				case 12:
					defaultUnit = "J";
					num = 10000.0;
					break;
				case 13:
					defaultUnit = "J";
					num = 100000.0;
					break;
				case 14:
					defaultUnit = "J";
					num = 1000000.0;
					break;
				case 15:
					defaultUnit = "J";
					num = 10000000.0;
					break;
				case 16:
					defaultUnit = "J";
					num = 100000000.0;
					break;
				case 17:
					defaultUnit = "J";
					num = 1000000000.0;
					break;
				case 18:
					defaultUnit = "J";
					num = 10000000000.0;
					break;
				case 19:
					defaultUnit = "J";
					num = 100000000000.0;
					break;
				case 41:
					defaultUnit = "L";
					num = 1.0;
					break;
				case 42:
					defaultUnit = "L";
					num = 10.0;
					break;
				case 43:
					defaultUnit = "L";
					num = 100.0;
					break;
				case 44:
					defaultUnit = "m³";
					num = 1.0;
					break;
				case 45:
					defaultUnit = "m³";
					num = 10.0;
					break;
				case 46:
					defaultUnit = "m³";
					num = 100.0;
					break;
				case 20:
					defaultUnit = "W";
					num = 1.0;
					break;
				case 21:
					defaultUnit = "W";
					num = 10.0;
					break;
				case 22:
					defaultUnit = "W";
					num = 100.0;
					break;
				case 23:
					defaultUnit = "W";
					num = 1000.0;
					break;
				case 24:
					defaultUnit = "W";
					num = 10000.0;
					break;
				case 25:
					defaultUnit = "W";
					num = 100000.0;
					break;
				case 26:
					defaultUnit = "W";
					num = 1000000.0;
					break;
				case 27:
					defaultUnit = "W";
					num = 10000000.0;
					break;
				case 28:
					defaultUnit = "W";
					num = 100000000.0;
					break;
				case 64:
					defaultUnit = "J/h";
					num = 1.0;
					break;
				case 67:
					defaultUnit = "J/h";
					num = 1000.0;
					break;
				case 68:
					defaultUnit = "J/h";
					num = 10000.0;
					break;
				case 69:
					defaultUnit = "J/h";
					num = 100000.0;
					break;
				case 70:
					defaultUnit = "J/h";
					num = 1000000.0;
					break;
				case 71:
					defaultUnit = "J/h";
					num = 10000000.0;
					break;
				case 72:
					defaultUnit = "J/h";
					num = 100000000.0;
					break;
				case 73:
					defaultUnit = "J/h";
					num = 1000000000.0;
					break;
				case 74:
					defaultUnit = "J/h";
					num = 10000000000.0;
					break;
				case 75:
					defaultUnit = "J/h";
					num = 100000000000.0;
					break;
				case 50:
					defaultUnit = "L/h";
					num = 1.0;
					break;
				case 51:
					defaultUnit = "L/h";
					num = 10.0;
					break;
				case 52:
					defaultUnit = "L/h";
					num = 100.0;
					break;
				case 53:
					defaultUnit = "m³/h";
					num = 1.0;
					break;
				case 54:
					defaultUnit = "m³/h";
					num = 10.0;
					break;
				case 55:
					defaultUnit = "m³/h";
					num = 100.0;
					break;
				}
			}
			if (text.Contains("FF"))
			{
				return OperateResult.CreateSuccessResult(double.NaN, defaultUnit);
			}
			try
			{
				return OperateResult.CreateSuccessResult(Convert.ToDouble(text) / scale * num, defaultUnit);
			}
			catch
			{
				return OperateResult.CreateSuccessResult(double.NaN, defaultUnit);
			}
		}

		private static string GetDateTime(byte[] source, int index)
		{
			return source.SelectMiddle(index, 2).ToHexString() + "-" + source.SelectMiddle(index + 2, 1).ToHexString() + "-" + source.SelectMiddle(index + 3, 1).ToHexString() + " " + source.SelectMiddle(index + 4, 1).ToHexString() + ":" + source.SelectMiddle(index + 5, 1).ToHexString() + ":" + source.SelectMiddle(index + 6, 1).ToHexString();
		}

		private static string GetUnitScale(string unit, double unitScale)
		{
			if (unitScale == 1.0)
			{
				return unit;
			}
			if (unitScale == 10.0)
			{
				return unit + "*10";
			}
			if (unitScale == 100.0)
			{
				return unit + "*100";
			}
			if (unitScale == 1000.0)
			{
				return "k" + unit;
			}
			if (unitScale == 10000.0)
			{
				return "k" + unit + "*10";
			}
			if (unitScale == 100000.0)
			{
				return "k" + unit + "*100";
			}
			if (unitScale == 1000000.0)
			{
				return "M" + unit;
			}
			if (unitScale == 10000000.0)
			{
				return "M" + unit + "*10";
			}
			if (unitScale == 100000000.0)
			{
				return "M" + unit + "*100";
			}
			if (unitScale == 1000000000.0)
			{
				return "G" + unit;
			}
			if (unitScale == 10000000000.0)
			{
				return "G" + unit + "*10";
			}
			if (unitScale == 100000000000.0)
			{
				return "G" + unit + "*100";
			}
			return unit + "*" + unitScale;
		}

		private static string[] TransStringsFromCJT(ICjt188 cjt, byte[] source, ushort dido)
		{
			if (dido == 36895)
			{
				OperateResult<double, string> actualValueAndUnit = GetActualValueAndUnit(source, 0, 4, 100.0, hasUnit: true, string.Empty);
				OperateResult<double, string> actualValueAndUnit2 = GetActualValueAndUnit(source, 5, 4, 100.0, hasUnit: true, string.Empty);
				return new string[6]
				{
					actualValueAndUnit.Content1.ToString(),
					actualValueAndUnit.Content2,
					actualValueAndUnit2.Content1.ToString(),
					actualValueAndUnit2.Content2,
					GetDateTime(source, 10),
					BitConverter.ToUInt16(source, 17).ToString()
				};
			}
			if (dido >= 53536 && dido < 53552)
			{
				OperateResult<double, string> actualValueAndUnit3 = GetActualValueAndUnit(source, 0, 4, 100.0, hasUnit: true, string.Empty);
				return new string[2]
				{
					actualValueAndUnit3.Content1.ToString(),
					actualValueAndUnit3.Content2
				};
			}
			int num;
			switch (dido)
			{
			case 33026:
			{
				List<string> list = new List<string>();
				for (int i = 0; i < 3; i++)
				{
					OperateResult<double, string> actualValueAndUnit4 = GetActualValueAndUnit(source, i * 6, 3, 100.0, hasUnit: false, "元/单位用量");
					OperateResult<double, string> actualValueAndUnit5 = GetActualValueAndUnit(source, i * 6 + 3, 3, 1.0, hasUnit: false, "m³");
					list.Add(actualValueAndUnit4.Content1.ToString());
					list.Add(actualValueAndUnit4.Content2);
					list.Add(actualValueAndUnit5.Content1.ToString());
					list.Add(actualValueAndUnit5.Content2);
				}
				return list.ToArray();
			}
			default:
				num = ((dido == 33028) ? 1 : 0);
				break;
			case 33027:
				num = 1;
				break;
			}
			if (num != 0)
			{
				return new string[1] { source.ToHexString() };
			}
			return dido switch
			{
				33029 => new string[5]
				{
					source.SelectMiddle(0, 1).ToHexString(),
					GetActualValueAndUnit(source, 1, 4, 2.0, hasUnit: false, "").Content1.ToString(),
					GetActualValueAndUnit(source, 5, 4, 2.0, hasUnit: false, "").Content1.ToString(),
					GetActualValueAndUnit(source, 9, 4, 2.0, hasUnit: false, "").Content1.ToString(),
					BitConverter.ToUInt16(source, 13).ToString()
				}, 
				33030 => new string[1] { source.SelectMiddle(0, 1).ToHexString() }, 
				33034 => new string[1] { source.ToHexString() }, 
				_ => new string[1] { source.ToHexString() }, 
			};
		}

		/// <summary>
		/// 读取指定地址的所有的字符串数据信息，一般来说，一个地址只有一个数据
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;90-1F" 或是 "s=100000;90-1F"
		/// </remarks>
		/// <param name="cjt">CJT通信对象</param>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <returns>字符串数组信息</returns>
		public static OperateResult<string[]> ReadStringArray(ICjt188 cjt, string address)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(address, cjt.Station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = ReadWithAddress(cjt, operateResult.Content1, operateResult.Content2);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult2);
			}
			try
			{
				return OperateResult.CreateSuccessResult(TransStringsFromCJT(cjt, operateResult2.Content, BitConverter.ToUInt16(operateResult.Content2, 0)));
			}
			catch (Exception ex)
			{
				return new OperateResult<string[]>("TransStringsFromCJT failed: " + ex.Message + Environment.NewLine + "Content: " + operateResult2.Content.ToHexString(' '));
			}
		}
	}
}
