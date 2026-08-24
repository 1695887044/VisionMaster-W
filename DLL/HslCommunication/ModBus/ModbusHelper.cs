using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Net;
using HslCommunication.Serial;

namespace HslCommunication.ModBus
{
	/// <summary>
	/// Modbus协议相关辅助类，关于Modbus消息的组装以及拆分<br />
	/// Modbus protocol related auxiliary classes, about the assembly and splitting of Modbus messages
	/// </summary>
	internal class ModbusHelper
	{
		public static OperateResult<byte[]> ExtraRtuResponseContent(byte[] send, byte[] response, bool crcCheck = true, int broadcastStation = -1)
		{
			if (broadcastStation >= 0 && send[0] == broadcastStation)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (response == null || response.Length < 5)
			{
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataLengthTooShort + "5 Content: " + response.ToHexString(' '));
			}
			byte[] array = response.ToArray();
			for (int i = 0; i < 2; i++)
			{
				if (array[1] == 1 || array[1] == 2 || array[1] == 3 || array[1] == 4 || array[1] == 23)
				{
					if (array.Length > 5 + array[2])
					{
						array = array.SelectBegin(5 + array[2]);
					}
				}
				else if (array[1] == 5 || array[1] == 6 || array[1] == 15 || array[1] == 16)
				{
					if (array.Length > 8)
					{
						array = array.SelectBegin(8);
					}
				}
				else if (array[1] > 128 && array.Length > 5)
				{
					array = array.SelectBegin(5);
				}
				if (crcCheck && !SoftCRC16.CheckCRC16(array))
				{
					if (i == 0)
					{
						array = response.RemoveBegin(1);
						continue;
					}
					return new OperateResult<byte[]>(int.MinValue, StringResources.Language.ModbusCRCCheckFailed + SoftBasic.ByteToHexString(response, ' '));
				}
				break;
			}
			if (send[0] != array[0])
			{
				return new OperateResult<byte[]>($"Station not match, request: {send[0]}, but response is {array[0]}");
			}
			if (send[1] + 128 == array[1])
			{
				return new OperateResult<byte[]>(array[2], ModbusInfo.GetDescriptionByErrorCode(array[2]));
			}
			if (send[1] != array[1])
			{
				return new OperateResult<byte[]>(array[1], "Receive Command Check Failed: ");
			}
			return ModbusInfo.ExtractActualData(ModbusInfo.ExplodeRtuCommandToCore(array));
		}

		public static OperateResult<byte[]> ExtraAsciiResponseContent(byte[] send, byte[] response, int broadcastStation = -1)
		{
			if (broadcastStation >= 0)
			{
				try
				{
					int num = Convert.ToInt32(Encoding.ASCII.GetString(send, 1, 2), 16);
					if (num == broadcastStation)
					{
						return OperateResult.CreateSuccessResult(new byte[0]);
					}
				}
				catch
				{
				}
			}
			OperateResult<byte[]> operateResult = ModbusInfo.TransAsciiPackCommandToCore(response);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content.Length < 3)
			{
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataLengthTooShort + " 3, Content: " + operateResult.Content.ToHexString(' '));
			}
			if (send[1] + 128 == operateResult.Content[1])
			{
				return new OperateResult<byte[]>(operateResult.Content[2], ModbusInfo.GetDescriptionByErrorCode(operateResult.Content[2]));
			}
			return ModbusInfo.ExtractActualData(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Read(System.String,System.UInt16)" />
		public static OperateResult<byte[]> Read(IModbus modbus, string address, ushort length)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 3);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			OperateResult<byte[][]> operateResult2 = ModbusInfo.BuildReadModbusCommand(operateResult.Content, length, modbus.Station, modbus.AddressStartWithZero, 3);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <summary>
		/// 使用0x17功能码来实现同时写入并读取数据的操作，使用一条报文来实现，需要指定读取的地址，长度，写入的地址，写入的数据信息，返回读取的结果数据。<br />
		/// Use 0x17 function code to write and read data at the same time, and use a message to implement it, 
		/// you need to specify the read address, length, written address, written data information, and return the read result data.
		/// </summary>
		/// <param name="modbus">Modbus通信对象</param>
		/// <param name="readAddress">读取的地址信息</param>
		/// <param name="length">读取的长度信息</param>
		/// <param name="writeAddress">写入的地址信息</param>
		/// <param name="value">写入的字节数据信息</param>
		/// <returns>读取的结果对象</returns>
		public static OperateResult<byte[]> ReadWrite(IModbus modbus, string readAddress, ushort length, string writeAddress, byte[] value)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(readAddress, 23);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			OperateResult<string> operateResult2 = modbus.TranslateToModbusAddress(writeAddress, 23);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<byte[]>();
			}
			OperateResult<byte[]> operateResult3 = ModbusInfo.BuildReadWriteModbusCommand(operateResult.Content, length, operateResult2.Content, value, modbus.Station, modbus.AddressStartWithZero, 23);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			return modbus.ReadFromCoreServer(operateResult3.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Read(HslCommunication.ModBus.IModbus,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IModbus modbus, string address, ushort length)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 3);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress.ConvertFailed<byte[]>();
			}
			OperateResult<byte[][]> command = ModbusInfo.BuildReadModbusCommand(modbusAddress.Content, length, modbus.Station, modbus.AddressStartWithZero, 3);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.ReadWrite(HslCommunication.ModBus.IModbus,System.String,System.UInt16,System.String,System.Byte[])" />
		public static async Task<OperateResult<byte[]>> ReadWriteAsync(IModbus modbus, string readAddress, ushort length, string writeAddress, byte[] value)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(readAddress, 23);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress.ConvertFailed<byte[]>();
			}
			OperateResult<string> modbusAddress2 = modbus.TranslateToModbusAddress(writeAddress, 23);
			if (!modbusAddress2.IsSuccess)
			{
				return modbusAddress2.ConvertFailed<byte[]>();
			}
			OperateResult<byte[]> command = ModbusInfo.BuildReadWriteModbusCommand(modbusAddress.Content, length, modbusAddress2.Content, value, modbus.Station, modbus.AddressStartWithZero, 23);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Byte[])" />
		public static OperateResult Write(IModbus modbus, string address, byte[] value)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 16);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteWordModbusCommand(operateResult.Content, value, modbus.Station, modbus.AddressStartWithZero, 16);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Write(HslCommunication.ModBus.IModbus,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IModbus modbus, string address, byte[] value)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 16);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteWordModbusCommand(modbusAddress.Content, value, modbus.Station, modbus.AddressStartWithZero, 16);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Int16)" />
		public static OperateResult Write(IModbus modbus, string address, short value)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 6);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteWordModbusCommand(operateResult.Content, value, modbus.Station, modbus.AddressStartWithZero, 6, modbus.ByteTransform);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Write(HslCommunication.ModBus.IModbus,System.String,System.Int16)" />
		public static async Task<OperateResult> WriteAsync(IModbus modbus, string address, short value)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 6);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteWordModbusCommand(modbusAddress.Content, value, modbus.Station, modbus.AddressStartWithZero, 6, modbus.ByteTransform);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.UInt16)" />
		public static OperateResult Write(IModbus modbus, string address, ushort value)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 6);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteWordModbusCommand(operateResult.Content, value, modbus.Station, modbus.AddressStartWithZero, 6, modbus.ByteTransform);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Write(HslCommunication.ModBus.IModbus,System.String,System.UInt16)" />
		public static async Task<OperateResult> WriteAsync(IModbus modbus, string address, ushort value)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 6);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteWordModbusCommand(modbusAddress.Content, value, modbus.Station, modbus.AddressStartWithZero, 6, modbus.ByteTransform);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteMask(System.String,System.UInt16,System.UInt16)" />
		public static OperateResult WriteMask(IModbus modbus, string address, ushort andMask, ushort orMask)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 22);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteMaskModbusCommand(operateResult.Content, andMask, orMask, modbus.Station, modbus.AddressStartWithZero, 22);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.WriteMask(HslCommunication.ModBus.IModbus,System.String,System.UInt16,System.UInt16)" />
		public static async Task<OperateResult> WriteMaskAsync(IModbus modbus, string address, ushort andMask, ushort orMask)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 22);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteMaskModbusCommand(modbusAddress.Content, andMask, orMask, modbus.Station, modbus.AddressStartWithZero, 22);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await modbus.ReadFromCoreServerAsync(command.Content);
		}

		public static OperateResult<bool[]> ReadBoolHelper(IModbus modbus, string address, ushort length, byte function)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, function);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<bool[]>();
			}
			if (operateResult.Content.IndexOf('.') > 0)
			{
				string[] array = address.SplitDot();
				int num = 0;
				try
				{
					string[] array2 = operateResult.Content.SplitDot();
					num = Convert.ToInt32(array2[1]);
				}
				catch (Exception ex)
				{
					return new OperateResult<bool[]>("Bit Index format wrong, " + ex.Message);
				}
				ushort length2 = (ushort)((length + num + 15) / 16);
				OperateResult<byte[]> operateResult2 = modbus.Read(array[0], length2);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult2);
				}
				return OperateResult.CreateSuccessResult(SoftBasic.BytesReverseByWord(operateResult2.Content).ToBoolArray().SelectMiddle(num, length));
			}
			OperateResult<byte[][]> operateResult3 = ModbusInfo.BuildReadModbusCommand(operateResult.Content, length, modbus.Station, modbus.AddressStartWithZero, function);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult3);
			}
			List<bool> list = new List<bool>();
			for (int i = 0; i < operateResult3.Content.Length; i++)
			{
				OperateResult<byte[]> operateResult4 = modbus.ReadFromCoreServer(operateResult3.Content[i]);
				if (!operateResult4.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult4);
				}
				int length3 = operateResult3.Content[i][4] * 256 + operateResult3.Content[i][5];
				list.AddRange(SoftBasic.ByteToBoolArray(operateResult4.Content, length3));
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		internal static async Task<OperateResult<bool[]>> ReadBoolHelperAsync(IModbus modbus, string address, ushort length, byte function)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, function);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress.ConvertFailed<bool[]>();
			}
			if (modbusAddress.Content.IndexOf('.') > 0)
			{
				string[] addressSplits = address.SplitDot();
				int bitIndex;
				try
				{
					string[] modbusSplits = modbusAddress.Content.SplitDot();
					bitIndex = Convert.ToInt32(modbusSplits[1]);
				}
				catch (Exception ex2)
				{
					Exception ex = ex2;
					return new OperateResult<bool[]>("Bit Index format wrong, " + ex.Message);
				}
				OperateResult<byte[]> read2 = await modbus.ReadAsync(length: (ushort)((length + bitIndex + 15) / 16), address: addressSplits[0]);
				if (!read2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read2);
				}
				return OperateResult.CreateSuccessResult(SoftBasic.BytesReverseByWord(read2.Content).ToBoolArray().SelectMiddle(bitIndex, length));
			}
			OperateResult<byte[][]> command = ModbusInfo.BuildReadModbusCommand(modbusAddress.Content, length, modbus.Station, modbus.AddressStartWithZero, function);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			List<bool> resultArray = new List<bool>();
			for (int i = 0; i < command.Content.Length; i++)
			{
				OperateResult<byte[]> read = await modbus.ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				resultArray.AddRange(SoftBasic.ByteToBoolArray(length: command.Content[i][4] * 256 + command.Content[i][5], inBytes: read.Content));
			}
			return OperateResult.CreateSuccessResult(resultArray.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Boolean[])" />
		public static OperateResult Write(IModbus modbus, string address, bool[] values)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 15);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content.IndexOf('.') > 0)
			{
				return ReadWriteNetHelper.WriteBoolWithWord(modbus, address, values, 16, reverseWord: true, operateResult.Content.SplitDot()[1]);
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteBoolModbusCommand(operateResult.Content, values, modbus.Station, modbus.AddressStartWithZero, 15);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return modbus.ReadFromCoreServer(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Write(HslCommunication.ModBus.IModbus,System.String,System.Boolean[])" />
		public static async Task<OperateResult> WriteAsync(IModbus modbus, string address, bool[] values)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 15);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			if (modbusAddress.Content.IndexOf('.') > 0)
			{
				return await ReadWriteNetHelper.WriteBoolWithWordAsync(modbus, address, values, 16, reverseWord: true, modbusAddress.Content.SplitDot()[1]);
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteBoolModbusCommand(modbusAddress.Content, values, modbus.Station, modbus.AddressStartWithZero, 15);
			if (!command.IsSuccess)
			{
				return command;
			}
			return await modbus.ReadFromCoreServerAsync(command.Content).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Boolean)" />
		public static OperateResult Write(IModbus modbus, string address, bool value)
		{
			OperateResult<string> operateResult = modbus.TranslateToModbusAddress(address, 5);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (address.IndexOf('.') > 0 && !modbus.EnableWriteMaskCode)
			{
				return Write(modbus, address, new bool[1] { value });
			}
			OperateResult<byte[]> operateResult2 = ModbusInfo.BuildWriteBoolModbusCommand(operateResult.Content, value, modbus.Station, modbus.AddressStartWithZero, 5);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = modbus.ReadFromCoreServer(operateResult2.Content);
			if (operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (address.IndexOf('.') > 0 && operateResult3.ErrorCode == 1)
			{
				modbus.EnableWriteMaskCode = false;
				return Write(modbus, address, new bool[1] { value });
			}
			return operateResult3;
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.Write(HslCommunication.ModBus.IModbus,System.String,System.Boolean)" />
		public static async Task<OperateResult> WriteAsync(IModbus modbus, string address, bool value)
		{
			OperateResult<string> modbusAddress = modbus.TranslateToModbusAddress(address, 5);
			if (!modbusAddress.IsSuccess)
			{
				return modbusAddress;
			}
			if (address.IndexOf('.') > 0 && !modbus.EnableWriteMaskCode)
			{
				return await WriteAsync(modbus, address, new bool[1] { value }).ConfigureAwait(continueOnCapturedContext: false);
			}
			OperateResult<byte[]> command = ModbusInfo.BuildWriteBoolModbusCommand(modbusAddress.Content, value, modbus.Station, modbus.AddressStartWithZero, 5);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult write = await modbus.ReadFromCoreServerAsync(command.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (write.IsSuccess)
			{
				return write;
			}
			if (address.IndexOf('.') > 0 && write.ErrorCode == 1)
			{
				modbus.EnableWriteMaskCode = false;
				return await WriteAsync(modbus, address, new bool[1] { value }).ConfigureAwait(continueOnCapturedContext: false);
			}
			return write;
		}

		public static bool TransAddressToModbus(string station, string address, string[] code, int[] offset, Func<string, int> prase, out string newAddress)
		{
			newAddress = string.Empty;
			for (int i = 0; i < code.Length; i++)
			{
				if (address.StartsWithAndNumber(code[i]))
				{
					newAddress = station + (prase(address.Substring(code[i].Length)) + offset[i]);
					return true;
				}
			}
			return false;
		}

		public static bool TransAddressToModbus(string station, string address, string[] code, int[] offset, out string newAddress)
		{
			return TransAddressToModbus(station, address, code, offset, int.Parse, out newAddress);
		}

		public static bool TransPointAddressToModbus(string station, string address, string[] code, int[] offset, Func<string, int> prase, out string newAddress)
		{
			newAddress = string.Empty;
			int num = address.IndexOf('.');
			if (num > 0)
			{
				string text = address.Substring(num);
				address = address.Substring(0, num);
				if (TransAddressToModbus(station, address, code, offset, prase, out newAddress))
				{
					newAddress += text;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// 针对带有小数点的地址进行转换，例如 D100.0 转成 100.0 
		/// </summary>
		/// <param name="station">站号信息</param>
		/// <param name="address">地址</param>
		/// <param name="code">地址类型</param>
		/// <param name="offset">起始偏移地址</param>
		/// <param name="newAddress">返回的新的地址</param>
		/// <returns>是否匹配当前的地址类型</returns>
		public static bool TransPointAddressToModbus(string station, string address, string[] code, int[] offset, out string newAddress)
		{
			return TransPointAddressToModbus(station, address, code, offset, int.Parse, out newAddress);
		}
	}
}
