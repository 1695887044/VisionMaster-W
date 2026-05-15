using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.ModBus;

namespace HslCommunication.Profinet.XINJE
{
	/// <summary>
	/// 信捷PLC的相关辅助类
	/// </summary>
	public class XinJEHelper
	{
		private static int CalculateXinJEStartAddress(string address)
		{
			if (address.IndexOf('.') < 0)
			{
				return Convert.ToInt32(address, 8);
			}
			string[] array = address.Split(new char[1] { '.' }, StringSplitOptions.RemoveEmptyEntries);
			return Convert.ToInt32(array[0], 8) * 8 + int.Parse(array[1]);
		}

		/// <summary>
		/// 根据信捷PLC的地址，解析出转换后的modbus协议信息
		/// </summary>
		/// <param name="series">PLC的系列信息</param>
		/// <param name="address">汇川plc的地址信息</param>
		/// <param name="modbusCode">原始的对应的modbus信息</param>
		/// <returns>还原后的modbus地址</returns>
		public static OperateResult<string> PraseXinJEAddress(XinJESeries series, string address, byte modbusCode)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			string text2 = string.Empty;
			OperateResult<int> operateResult2 = HslHelper.ExtractParameter(ref address, "x");
			if (operateResult2.IsSuccess)
			{
				text2 = $"x={operateResult2.Content};";
			}
			if (series == XinJESeries.XC)
			{
				try
				{
					if ((Regex.IsMatch(address, "^X[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^Y[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^M[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^S[0-9]+", RegexOptions.IgnoreCase)) && modbusCode == 3)
					{
						modbusCode = 1;
						text2 = "x=1;";
					}
				}
				catch
				{
				}
				return PraseXinJEXCAddress(text + text2, address, modbusCode);
			}
			try
			{
				if ((Regex.IsMatch(address, "^X[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^Y[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^M[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^S[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^SEM[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^HSC[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^SM[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^ET[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^HM[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^HS[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^HT[0-9]+", RegexOptions.IgnoreCase) || Regex.IsMatch(address, "^HC[0-9]+", RegexOptions.IgnoreCase)) && modbusCode == 3)
				{
					modbusCode = 1;
					text2 = "x=1;";
				}
			}
			catch
			{
			}
			return PraseXinJEXD1XD2XD3XL1XL3Address(text + text2, address, modbusCode);
		}

		private static int CalculateXC_D(string address)
		{
			int num = Convert.ToInt32(address);
			if (num >= 8000)
			{
				return num - 8000 + 16384;
			}
			return num;
		}

		/// <summary>
		/// 根据信捷PLC的地址，解析出转换后的modbus协议信息，适用XC系列
		/// </summary>
		/// <param name="station">站号的特殊指定信息，可以为空</param>
		/// <param name="address">信捷plc的地址信息</param>
		/// <param name="modbusCode">原始的对应的modbus信息</param>
		/// <returns>还原后的modbus地址</returns>
		public static OperateResult<string> PraseXinJEXCAddress(string station, string address, byte modbusCode)
		{
			try
			{
				if (modbusCode == 1 || modbusCode == 2 || modbusCode == 15 || modbusCode == 5)
				{
					if (ModbusHelper.TransAddressToModbus(station, address, new string[2] { "X", "Y" }, new int[2] { 16384, 18432 }, CalculateXinJEStartAddress, out var newAddress))
					{
						return OperateResult.CreateSuccessResult(newAddress);
					}
					if (ModbusHelper.TransAddressToModbus(station, address, new string[3] { "S", "T", "C" }, new int[3] { 20480, 25600, 27648 }, out var newAddress2))
					{
						return OperateResult.CreateSuccessResult(newAddress2);
					}
					if (address.StartsWithAndNumber("M"))
					{
						int num = Convert.ToInt32(address.Substring(1));
						if (num >= 8000)
						{
							return OperateResult.CreateSuccessResult(station + (num - 8000 + 24576));
						}
						return OperateResult.CreateSuccessResult(station + num);
					}
					if (ModbusHelper.TransPointAddressToModbus(station, address, new string[1] { "D" }, new int[1], CalculateXC_D, out var newAddress3))
					{
						return OperateResult.CreateSuccessResult(newAddress3);
					}
				}
				else
				{
					if (address.StartsWithAndNumber("D"))
					{
						return OperateResult.CreateSuccessResult(station + CalculateXC_D(address.Substring(1)));
					}
					if (address.StartsWithAndNumber("F"))
					{
						int num2 = Convert.ToInt32(address.Substring(1));
						if (num2 >= 8000)
						{
							return OperateResult.CreateSuccessResult(station + (num2 - 8000 + 26624));
						}
						return OperateResult.CreateSuccessResult(station + (num2 + 18432));
					}
					if (ModbusHelper.TransAddressToModbus(station, address, new string[3] { "E", "T", "C" }, new int[3] { 28672, 12288, 14336 }, out var newAddress4))
					{
						return OperateResult.CreateSuccessResult(newAddress4);
					}
				}
				return new OperateResult<string>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
		}

		/// <summary>
		/// 解析信捷的XD1,XD2,XD3,XL1,XL3系列的PLC的Modbus地址和内部软元件的对照
		/// </summary>
		/// <remarks>适用 XD1、XD2、XD3、XL1、XL3、XD5、XDM、XDC、XD5E、XDME、XL5、XL5E、XLME, XDH 只是支持的地址范围不一样而已</remarks>
		/// <param name="station">站号的特殊指定信息，可以为空</param>
		/// <param name="address">PLC内部的软元件的地址</param>
		/// <param name="modbusCode">默认的Modbus功能码</param>
		/// <returns>解析后的Modbus地址</returns>
		public static OperateResult<string> PraseXinJEXD1XD2XD3XL1XL3Address(string station, string address, byte modbusCode)
		{
			try
			{
				if (modbusCode == 1 || modbusCode == 2 || modbusCode == 15 || modbusCode == 5)
				{
					if (address.StartsWith("X") || address.StartsWith("x"))
					{
						int num = CalculateXinJEStartAddress(address.Substring(1));
						if (num < 4096)
						{
							return OperateResult.CreateSuccessResult(station + (num + 20480));
						}
						if (num < 8192)
						{
							return OperateResult.CreateSuccessResult(station + (num - 4096 + 20736));
						}
						if (num < 12288)
						{
							return OperateResult.CreateSuccessResult(station + (num - 8192 + 22736));
						}
						return OperateResult.CreateSuccessResult(station + (num - 12288 + 23536));
					}
					if (address.StartsWith("Y") || address.StartsWith("y"))
					{
						int num2 = CalculateXinJEStartAddress(address.Substring(1));
						if (num2 < 4096)
						{
							return OperateResult.CreateSuccessResult(station + (num2 + 24576));
						}
						if (num2 < 8192)
						{
							return OperateResult.CreateSuccessResult(station + (num2 - 4096 + 24832));
						}
						if (num2 < 12288)
						{
							return OperateResult.CreateSuccessResult(station + (num2 - 8192 + 26832));
						}
						return OperateResult.CreateSuccessResult(station + (num2 - 12288 + 27632));
					}
					if (ModbusHelper.TransAddressToModbus(station, address, new string[12]
					{
						"SEM", "HSC", "SM", "ET", "HM", "HS", "HT", "HC", "S", "T",
						"C", "M"
					}, new int[12]
					{
						49280, 59648, 36864, 49152, 49408, 55552, 57600, 58624, 28672, 40960,
						45056, 0
					}, out var newAddress))
					{
						return OperateResult.CreateSuccessResult(newAddress);
					}
					if (ModbusHelper.TransPointAddressToModbus(station, address, new string[3] { "D", "SD", "HD" }, new int[3] { 0, 28672, 41088 }, out var newAddress2))
					{
						return OperateResult.CreateSuccessResult(newAddress2);
					}
				}
				else
				{
					if (address.StartsWith("ID") || address.StartsWith("id"))
					{
						int num3 = Convert.ToInt32(address.Substring(2));
						if (num3 < 10000)
						{
							return OperateResult.CreateSuccessResult(station + (num3 + 20480));
						}
						if (num3 < 20000)
						{
							return OperateResult.CreateSuccessResult(station + (num3 - 10000 + 20736));
						}
						if (num3 < 30000)
						{
							return OperateResult.CreateSuccessResult(station + (num3 - 20000 + 22736));
						}
						return OperateResult.CreateSuccessResult(station + (num3 - 30000 + 23536));
					}
					if (address.StartsWith("QD") || address.StartsWith("qd"))
					{
						int num4 = Convert.ToInt32(address.Substring(2));
						if (num4 < 10000)
						{
							return OperateResult.CreateSuccessResult(station + (num4 + 24576));
						}
						if (num4 < 20000)
						{
							return OperateResult.CreateSuccessResult(station + (num4 - 10000 + 24832));
						}
						if (num4 < 30000)
						{
							return OperateResult.CreateSuccessResult(station + (num4 - 20000 + 26832));
						}
						return OperateResult.CreateSuccessResult(station + (num4 - 30000 + 27632));
					}
					if (ModbusHelper.TransAddressToModbus(station, address, new string[13]
					{
						"HSCD", "ETD", "HSD", "HTD", "HCD", "SFD", "SD", "TD", "CD", "HD",
						"FD", "FS", "D"
					}, new int[13]
					{
						50304, 40960, 47232, 48256, 49280, 58560, 28672, 32768, 36864, 41088,
						50368, 62656, 0
					}, out var newAddress3))
					{
						return OperateResult.CreateSuccessResult(newAddress3);
					}
				}
				return new OperateResult<string>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
		}

		internal static OperateResult<List<byte[]>> BuildReadCommand(byte station, string address, ushort length, bool isBit)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
			}
			return BuildReadCommand(operateResult.Content, length, isBit);
		}

		internal static OperateResult<List<byte[]>> BuildReadCommand(XinJEAddress address, ushort length, bool isBit)
		{
			List<byte[]> list = new List<byte[]>();
			int[] array = SoftBasic.SplitIntegerToArray(length, isBit ? 1920 : 120);
			for (int i = 0; i < array.Length; i++)
			{
				byte[] item = new byte[8]
				{
					address.Station,
					(byte)(isBit ? 30 : 32),
					address.DataCode,
					BitConverter.GetBytes(address.AddressStart)[2],
					BitConverter.GetBytes(address.AddressStart)[1],
					BitConverter.GetBytes(address.AddressStart)[0],
					BitConverter.GetBytes(array[i])[1],
					BitConverter.GetBytes(array[i])[0]
				};
				address.AddressStart += array[i];
				list.Add(item);
			}
			return OperateResult.CreateSuccessResult(list);
		}

		internal static OperateResult<byte[]> BuildWriteWordCommand(byte station, string address, byte[] value)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return BuildWriteWordCommand(operateResult.Content, value);
		}

		internal static OperateResult<byte[]> BuildWriteWordCommand(XinJEAddress address, byte[] value)
		{
			byte[] array = new byte[9 + value.Length];
			array[0] = address.Station;
			array[1] = 33;
			array[2] = address.DataCode;
			array[3] = BitConverter.GetBytes(address.AddressStart)[2];
			array[4] = BitConverter.GetBytes(address.AddressStart)[1];
			array[5] = BitConverter.GetBytes(address.AddressStart)[0];
			array[6] = BitConverter.GetBytes(value.Length / 2)[1];
			array[7] = BitConverter.GetBytes(value.Length / 2)[0];
			array[8] = (byte)value.Length;
			value.CopyTo(array, 9);
			return OperateResult.CreateSuccessResult(array);
		}

		internal static OperateResult<byte[]> BuildWriteBoolCommand(byte station, string address, bool[] value)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return BuildWriteBoolCommand(operateResult.Content, value);
		}

		internal static OperateResult<byte[]> BuildWriteBoolCommand(XinJEAddress address, bool[] value)
		{
			byte[] array = value.ToByteArray();
			byte[] array2 = new byte[9 + array.Length];
			array2[0] = address.Station;
			array2[1] = 31;
			array2[2] = address.DataCode;
			array2[3] = BitConverter.GetBytes(address.AddressStart)[2];
			array2[4] = BitConverter.GetBytes(address.AddressStart)[1];
			array2[5] = BitConverter.GetBytes(address.AddressStart)[0];
			array2[6] = BitConverter.GetBytes(value.Length)[1];
			array2[7] = BitConverter.GetBytes(value.Length)[0];
			array2[8] = (byte)array.Length;
			array.CopyTo(array2, 9);
			return OperateResult.CreateSuccessResult(array2);
		}

		internal static OperateResult<byte[]> Read(IModbus modbus, string address, ushort length, Func<string, ushort, OperateResult<byte[]>> funcRead)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, length, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcRead(address, length);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart + length <= content.CriticalAddress)
			{
				return funcRead(address, length);
			}
			List<byte> list = new List<byte>();
			if (content.AddressStart < content.CriticalAddress)
			{
				OperateResult<byte[]> operateResult2 = funcRead(address, (ushort)(content.CriticalAddress - content.AddressStart));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				list.AddRange(operateResult2.Content);
				length = (ushort)(length - (content.CriticalAddress - content.AddressStart));
				content.AddressStart = content.CriticalAddress;
			}
			OperateResult<List<byte[]>> operateResult3 = BuildReadCommand(content, length, isBit: false);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			OperateResult<byte[]> operateResult4 = modbus.ReadFromCoreServer(operateResult3.Content);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			list.AddRange(operateResult4.Content);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		internal static OperateResult Write(IModbus modbus, string address, byte[] value, Func<string, byte[], OperateResult> funcWrite)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcWrite(address, value);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart + value.Length / 2 <= content.CriticalAddress)
			{
				return funcWrite(address, value);
			}
			if (content.AddressStart < content.CriticalAddress)
			{
				OperateResult operateResult2 = funcWrite(address, value.SelectBegin((content.CriticalAddress - content.AddressStart) * 2));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				value = value.RemoveBegin((content.CriticalAddress - content.AddressStart) * 2);
				content.AddressStart = content.CriticalAddress;
			}
			OperateResult<byte[]> operateResult3 = BuildWriteWordCommand(content, value);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			return modbus.ReadFromCoreServer(operateResult3.Content);
		}

		internal static OperateResult Write(IModbus modbus, string address, short value, Func<string, short, OperateResult> funcWrite)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcWrite(address, value);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart < content.CriticalAddress)
			{
				return funcWrite(address, value);
			}
			return modbus.Write(address, modbus.ByteTransform.TransByte(value));
		}

		internal static OperateResult Write(IModbus modbus, string address, ushort value, Func<string, ushort, OperateResult> funcWrite)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcWrite(address, value);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart < content.CriticalAddress)
			{
				return funcWrite(address, value);
			}
			return modbus.Write(address, modbus.ByteTransform.TransByte(value));
		}

		internal static OperateResult<bool[]> ReadBool(IModbus modbus, string address, ushort length, Func<string, ushort, OperateResult<bool[]>> funcRead)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, length, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcRead(address, length);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart + length <= content.CriticalAddress)
			{
				return funcRead(address, length);
			}
			List<bool> list = new List<bool>();
			if (content.AddressStart < content.CriticalAddress)
			{
				OperateResult<bool[]> operateResult2 = funcRead(address, (ushort)(content.CriticalAddress - content.AddressStart));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				list.AddRange(operateResult2.Content);
				length = (ushort)(length - (content.CriticalAddress - content.AddressStart));
				content.AddressStart = content.CriticalAddress;
			}
			OperateResult<List<byte[]>> operateResult3 = BuildReadCommand(content, length, isBit: true);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult3);
			}
			OperateResult<byte[]> operateResult4 = modbus.ReadFromCoreServer(operateResult3.Content);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult4);
			}
			list.AddRange(operateResult4.Content.ToBoolArray().SelectBegin(length));
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		internal static OperateResult Write(IModbus modbus, string address, bool[] values, Func<string, bool[], OperateResult> funcWrite)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcWrite(address, values);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart + values.Length <= content.CriticalAddress)
			{
				return funcWrite(address, values);
			}
			if (content.AddressStart < content.CriticalAddress)
			{
				OperateResult operateResult2 = funcWrite(address, values.SelectBegin(content.CriticalAddress - content.AddressStart));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				values = values.RemoveBegin(content.CriticalAddress - content.AddressStart);
				content.AddressStart = content.CriticalAddress;
			}
			OperateResult<byte[]> operateResult3 = BuildWriteBoolCommand(content, values);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			return modbus.ReadFromCoreServer(operateResult3.Content);
		}

		internal static OperateResult Write(IModbus modbus, string address, bool value, Func<string, bool, OperateResult> funcWrite)
		{
			OperateResult<XinJEAddress> operateResult = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!operateResult.IsSuccess)
			{
				return funcWrite(address, value);
			}
			XinJEAddress content = operateResult.Content;
			if (content.AddressStart < content.CriticalAddress)
			{
				return funcWrite(address, value);
			}
			return modbus.Write(address, new bool[1] { value });
		}

		internal static async Task<OperateResult<byte[]>> ReadAsync(IModbus modbus, string address, ushort length, Func<string, ushort, Task<OperateResult<byte[]>>> funcRead)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcRead(address, length);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart + length <= xinJE.CriticalAddress)
			{
				return await funcRead(address, length);
			}
			List<byte> result = new List<byte>();
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				OperateResult<byte[]> read = await funcRead(address, (ushort)(xinJE.CriticalAddress - xinJE.AddressStart));
				if (!read.IsSuccess)
				{
					return read;
				}
				result.AddRange(read.Content);
				length = (ushort)(length - (xinJE.CriticalAddress - xinJE.AddressStart));
				xinJE.AddressStart = xinJE.CriticalAddress;
			}
			OperateResult<List<byte[]>> command = BuildReadCommand(analysis.Content, length, isBit: false);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			OperateResult<byte[]> readAgain = await modbus.ReadFromCoreServerAsync(command.Content);
			if (!readAgain.IsSuccess)
			{
				return readAgain;
			}
			result.AddRange(readAgain.Content);
			return OperateResult.CreateSuccessResult(result.ToArray());
		}

		internal static async Task<OperateResult> WriteAsync(IModbus modbus, string address, byte[] value, Func<string, byte[], Task<OperateResult>> funcWrite)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcWrite(address, value);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart + value.Length / 2 <= xinJE.CriticalAddress)
			{
				return await funcWrite(address, value);
			}
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				OperateResult write = await funcWrite(address, value.SelectBegin((xinJE.CriticalAddress - xinJE.AddressStart) * 2));
				if (!write.IsSuccess)
				{
					return write;
				}
				value = value.RemoveBegin((xinJE.CriticalAddress - xinJE.AddressStart) * 2);
				xinJE.AddressStart = xinJE.CriticalAddress;
			}
			OperateResult<byte[]> command = BuildWriteWordCommand(xinJE, value);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		internal static async Task<OperateResult> WriteAsync(IModbus modbus, string address, short value, Func<string, short, Task<OperateResult>> funcWrite)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcWrite(address, value);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				return await funcWrite(address, value);
			}
			return await modbus.WriteAsync(address, modbus.ByteTransform.TransByte(value));
		}

		internal static async Task<OperateResult> WriteAsync(IModbus modbus, string address, ushort value, Func<string, ushort, Task<OperateResult>> funcWrite)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcWrite(address, value);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				return await funcWrite(address, value);
			}
			return await modbus.WriteAsync(address, modbus.ByteTransform.TransByte(value));
		}

		internal static async Task<OperateResult<bool[]>> ReadBoolAsync(IModbus modbus, string address, ushort length, Func<string, ushort, Task<OperateResult<bool[]>>> funcRead)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, length, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcRead(address, length);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart + length <= xinJE.CriticalAddress)
			{
				return await funcRead(address, length);
			}
			List<bool> result = new List<bool>();
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				OperateResult<bool[]> read = await funcRead(address, (ushort)(xinJE.CriticalAddress - xinJE.AddressStart));
				if (!read.IsSuccess)
				{
					return read;
				}
				result.AddRange(read.Content);
				length = (ushort)(length - (xinJE.CriticalAddress - xinJE.AddressStart));
				xinJE.AddressStart = xinJE.CriticalAddress;
			}
			OperateResult<List<byte[]>> command = BuildReadCommand(xinJE, length, isBit: true);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			OperateResult<byte[]> readAgain = await modbus.ReadFromCoreServerAsync(command.Content);
			if (!readAgain.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(readAgain);
			}
			result.AddRange(readAgain.Content.ToBoolArray().SelectBegin(length));
			return OperateResult.CreateSuccessResult(result.ToArray());
		}

		internal static async Task<OperateResult> WriteAsync(IModbus modbus, string address, bool[] values, Func<string, bool[], Task<OperateResult>> funcWrite)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcWrite(address, values);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart + values.Length <= xinJE.CriticalAddress)
			{
				return await funcWrite(address, values);
			}
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				OperateResult write = await funcWrite(address, values.SelectBegin(xinJE.CriticalAddress - xinJE.AddressStart));
				if (!write.IsSuccess)
				{
					return write;
				}
				values = values.RemoveBegin(xinJE.CriticalAddress - xinJE.AddressStart);
				xinJE.AddressStart = xinJE.CriticalAddress;
			}
			OperateResult<byte[]> command = BuildWriteBoolCommand(xinJE, values);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		internal static async Task<OperateResult> WriteAsync(IModbus modbus, string address, bool value, Func<string, bool, Task<OperateResult>> funcWrite)
		{
			OperateResult<XinJEAddress> analysis = XinJEAddress.ParseFrom(address, modbus.Station);
			if (!analysis.IsSuccess)
			{
				return await funcWrite(address, value);
			}
			XinJEAddress xinJE = analysis.Content;
			if (xinJE.AddressStart < xinJE.CriticalAddress)
			{
				return await funcWrite(address, value);
			}
			return await modbus.WriteAsync(address, new bool[1] { value });
		}
	}
}
