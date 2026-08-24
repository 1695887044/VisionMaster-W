using System;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.ModBus;

namespace HslCommunication.Profinet.Delta.Helper
{
	/// <summary>
	/// 台达PLC的相关的帮助类，公共的地址解析的方法。<br />
	/// Delta PLC related help classes, public address resolution methods.
	/// </summary>
	public class DeltaDvpHelper
	{
		private static int TransDAdressToModbusAddress(string address)
		{
			int num = Convert.ToInt32(address);
			if (num >= 4096)
			{
				return num - 4096 + 36864;
			}
			return num + 4096;
		}

		/// <summary>
		/// 根据台达PLC的地址，解析出转换后的modbus协议信息，适用DVP系列，当前的地址仍然支持站号指定，例如s=2;D100<br />
		/// According to the address of Delta PLC, the converted modbus protocol information is parsed out, applicable to DVP series, 
		/// the current address still supports station number designation, such as s=2;D100
		/// </summary>
		/// <param name="address">台达plc的地址信息</param>
		/// <param name="modbusCode">原始的对应的modbus信息</param>
		/// <returns>还原后的modbus地址</returns>
		public static OperateResult<string> ParseDeltaDvpAddress(string address, byte modbusCode)
		{
			try
			{
				string text = string.Empty;
				OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
				if (operateResult.IsSuccess)
				{
					text = $"s={operateResult.Content};";
				}
				if (modbusCode == 1 || modbusCode == 15 || modbusCode == 5)
				{
					if (address.StartsWithAndNumber("S"))
					{
						return OperateResult.CreateSuccessResult(text + Convert.ToInt32(address.Substring(1)));
					}
					if (address.StartsWithAndNumber("X"))
					{
						return OperateResult.CreateSuccessResult(text + "x=2;" + (Convert.ToInt32(address.Substring(1), 8) + 1024));
					}
					if (address.StartsWithAndNumber("Y"))
					{
						return OperateResult.CreateSuccessResult(text + (Convert.ToInt32(address.Substring(1), 8) + 1280));
					}
					if (address.StartsWithAndNumber("T"))
					{
						return OperateResult.CreateSuccessResult(text + (Convert.ToInt32(address.Substring(1)) + 1536));
					}
					if (address.StartsWithAndNumber("C"))
					{
						return OperateResult.CreateSuccessResult(text + (Convert.ToInt32(address.Substring(1)) + 3584));
					}
					if (address.StartsWithAndNumber("M"))
					{
						int num = Convert.ToInt32(address.Substring(1));
						if (num >= 1536)
						{
							return OperateResult.CreateSuccessResult(text + (num - 1536 + 45056));
						}
						return OperateResult.CreateSuccessResult(text + (num + 2048));
					}
					if (ModbusHelper.TransPointAddressToModbus(text, address, new string[1] { "D" }, new int[1], TransDAdressToModbusAddress, out var newAddress))
					{
						return OperateResult.CreateSuccessResult(newAddress);
					}
				}
				else
				{
					if (address.StartsWithAndNumber("D"))
					{
						return OperateResult.CreateSuccessResult(text + TransDAdressToModbusAddress(address.Substring(1)));
					}
					if (address.StartsWithAndNumber("C"))
					{
						int num2 = Convert.ToInt32(address.Substring(1));
						if (num2 >= 200)
						{
							return OperateResult.CreateSuccessResult(text + (num2 - 200 + 3784));
						}
						return OperateResult.CreateSuccessResult(text + (num2 + 3584));
					}
					if (address.StartsWithAndNumber("T"))
					{
						return OperateResult.CreateSuccessResult(text + (Convert.ToInt32(address.Substring(1)) + 1536));
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
		/// 读取台达PLC的bool变量，重写了读M地址时，跨区域读1536地址时，将会分割多次读取
		/// </summary>
		/// <param name="readBoolFunc">底层基础的读取方法</param>
		/// <param name="address">PLC的地址信息</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>读取的结果</returns>
		public static OperateResult<bool[]> ReadBool(Func<string, ushort, OperateResult<bool[]>> readBoolFunc, string address, ushort length)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			if (address.StartsWith("M") && int.TryParse(address.Substring(1), out var result) && result < 1536 && result + length > 1536)
			{
				ushort num = (ushort)(1536 - result);
				ushort arg = (ushort)(length - num);
				OperateResult<bool[]> operateResult2 = readBoolFunc(text + address, num);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<bool[]> operateResult3 = readBoolFunc(text + "M1536", arg);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<bool>(operateResult2.Content, operateResult3.Content));
			}
			return readBoolFunc(address, length);
		}

		/// <summary>
		/// 写入台达PLC的bool数据，当发现是M类型的数据，并且地址出现跨1536时，进行切割写入操作
		/// </summary>
		/// <param name="writeBoolFunc">底层的写入操作方法</param>
		/// <param name="address">PLC的起始地址信息</param>
		/// <param name="value">等待写入的数据信息</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(Func<string, bool[], OperateResult> writeBoolFunc, string address, bool[] value)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			if (address.StartsWith("M") && int.TryParse(address.Substring(1), out var result) && result < 1536 && result + value.Length > 1536)
			{
				ushort length = (ushort)(1536 - result);
				OperateResult operateResult2 = writeBoolFunc(text + address, value.SelectBegin(length));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult operateResult3 = writeBoolFunc(text + "M1536", value.RemoveBegin(length));
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				return OperateResult.CreateSuccessResult();
			}
			return writeBoolFunc(address, value);
		}

		/// <summary>
		/// 读取台达PLC的原始字节变量，重写了读D地址时，跨区域读4096地址时，将会分割多次读取
		/// </summary>
		/// <param name="readFunc">底层基础的读取方法</param>
		/// <param name="address">PLC的地址信息</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>读取的结果</returns>
		public static OperateResult<byte[]> Read(Func<string, ushort, OperateResult<byte[]>> readFunc, string address, ushort length)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			if (address.StartsWith("D") && int.TryParse(address.Substring(1), out var result) && result < 4096 && result + length > 4096)
			{
				ushort num = (ushort)(4096 - result);
				ushort arg = (ushort)(length - num);
				OperateResult<byte[]> operateResult2 = readFunc(text + address, num);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<byte[]> operateResult3 = readFunc(text + "D4096", arg);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(operateResult2.Content, operateResult3.Content));
			}
			return readFunc(address, length);
		}

		/// <summary>
		/// 写入台达PLC的原始字节数据，当发现是D类型的数据，并且地址出现跨4096时，进行切割写入操作
		/// </summary>
		/// <param name="writeFunc">底层的写入操作方法</param>
		/// <param name="address">PLC的起始地址信息</param>
		/// <param name="value">等待写入的数据信息</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(Func<string, byte[], OperateResult> writeFunc, string address, byte[] value)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			if (address.StartsWith("D") && int.TryParse(address.Substring(1), out var result) && result < 4096 && result + value.Length / 2 > 4096)
			{
				ushort num = (ushort)(4096 - result);
				OperateResult operateResult2 = writeFunc(text + address, value.SelectBegin(num * 2));
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult operateResult3 = writeFunc(text + "D4096", value.RemoveBegin(num * 2));
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				return OperateResult.CreateSuccessResult();
			}
			return writeFunc(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Delta.Helper.DeltaDvpHelper.ReadBool(System.Func{System.String,System.UInt16,HslCommunication.OperateResult{System.Boolean[]}},System.String,System.UInt16)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(Func<string, ushort, Task<OperateResult<bool[]>>> readBoolFunc, string address, ushort length)
		{
			string station = string.Empty;
			OperateResult<int> stationPara = HslHelper.ExtractParameter(ref address, "s");
			if (stationPara.IsSuccess)
			{
				station = $"s={stationPara.Content};";
			}
			if (address.StartsWith("M") && int.TryParse(address.Substring(1), out var add) && add < 1536 && add + length > 1536)
			{
				ushort len1 = (ushort)(1536 - add);
				ushort len2 = (ushort)(length - len1);
				OperateResult<bool[]> read1 = await readBoolFunc(station + address, len1);
				if (!read1.IsSuccess)
				{
					return read1;
				}
				OperateResult<bool[]> read2 = await readBoolFunc(station + "M1536", len2);
				if (!read2.IsSuccess)
				{
					return read2;
				}
				return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<bool>(read1.Content, read2.Content));
			}
			return await readBoolFunc(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Delta.Helper.DeltaDvpHelper.Write(System.Func{System.String,System.Boolean[],HslCommunication.OperateResult},System.String,System.Boolean[])" />
		public static async Task<OperateResult> WriteAsync(Func<string, bool[], Task<OperateResult>> writeBoolFunc, string address, bool[] value)
		{
			string station = string.Empty;
			OperateResult<int> stationPara = HslHelper.ExtractParameter(ref address, "s");
			if (stationPara.IsSuccess)
			{
				station = $"s={stationPara.Content};";
			}
			if (address.StartsWith("M") && int.TryParse(address.Substring(1), out var add) && add < 1536 && add + value.Length > 1536)
			{
				ushort len1 = (ushort)(1536 - add);
				OperateResult write1 = await writeBoolFunc(station + address, value.SelectBegin(len1));
				if (!write1.IsSuccess)
				{
					return write1;
				}
				OperateResult write2 = await writeBoolFunc(station + "M1536", value.RemoveBegin(len1));
				if (!write2.IsSuccess)
				{
					return write2;
				}
				return OperateResult.CreateSuccessResult();
			}
			return await writeBoolFunc(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Delta.Helper.DeltaDvpHelper.Read(System.Func{System.String,System.UInt16,HslCommunication.OperateResult{System.Byte[]}},System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(Func<string, ushort, Task<OperateResult<byte[]>>> readFunc, string address, ushort length)
		{
			string station = string.Empty;
			OperateResult<int> stationPara = HslHelper.ExtractParameter(ref address, "s");
			if (stationPara.IsSuccess)
			{
				station = $"s={stationPara.Content};";
			}
			if (address.StartsWith("D") && int.TryParse(address.Substring(1), out var add) && add < 4096 && add + length > 4096)
			{
				ushort len1 = (ushort)(4096 - add);
				ushort len2 = (ushort)(length - len1);
				OperateResult<byte[]> read1 = await readFunc(station + address, len1);
				if (!read1.IsSuccess)
				{
					return read1;
				}
				OperateResult<byte[]> read2 = await readFunc(station + "D4096", len2);
				if (!read2.IsSuccess)
				{
					return read2;
				}
				return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(read1.Content, read2.Content));
			}
			return await readFunc(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Delta.Helper.DeltaDvpHelper.Write(System.Func{System.String,System.Byte[],HslCommunication.OperateResult},System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(Func<string, byte[], Task<OperateResult>> writeFunc, string address, byte[] value)
		{
			string station = string.Empty;
			OperateResult<int> stationPara = HslHelper.ExtractParameter(ref address, "s");
			if (stationPara.IsSuccess)
			{
				station = $"s={stationPara.Content};";
			}
			if (address.StartsWith("D") && int.TryParse(address.Substring(1), out var add) && add < 4096 && add + value.Length / 2 > 4096)
			{
				ushort len1 = (ushort)(4096 - add);
				OperateResult write1 = await writeFunc(station + address, value.SelectBegin(len1 * 2));
				if (!write1.IsSuccess)
				{
					return write1;
				}
				OperateResult write2 = await writeFunc(station + "D4096", value.RemoveBegin(len1 * 2));
				if (!write2.IsSuccess)
				{
					return write2;
				}
				return OperateResult.CreateSuccessResult();
			}
			return await writeFunc(address, value);
		}
	}
}
