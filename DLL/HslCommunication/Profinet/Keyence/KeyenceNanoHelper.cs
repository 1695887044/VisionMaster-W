using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// KeyenceNano的基本辅助方法
	/// </summary>
	public class KeyenceNanoHelper
	{
		/// <summary>
		/// 连接PLC的命令报文<br />
		/// Command message to connect to PLC
		/// </summary>
		/// <param name="station">当前PLC的站号信息</param>
		/// <param name="useStation">是否启动站号命令</param>
		public static byte[] GetConnectCmd(byte station, bool useStation)
		{
			return useStation ? Encoding.ASCII.GetBytes($"CR {station:D2}\r") : Encoding.ASCII.GetBytes("CR\r");
		}

		/// <summary>
		/// 断开PLC连接的命令报文<br />
		/// Command message to disconnect PLC
		/// </summary>
		/// <param name="station">当前PLC的站号信息</param>
		/// <param name="useStation">是否启动站号命令</param>
		public static byte[] GetDisConnectCmd(byte station, bool useStation)
		{
			return Encoding.ASCII.GetBytes("CQ\r");
		}

		/// <summary>
		/// 获取当前的地址类型是字数据的倍数关系
		/// </summary>
		/// <param name="type">地址的类型</param>
		/// <returns>倍数关系</returns>
		public static int GetWordAddressMultiple(string type)
		{
			int num;
			switch (type)
			{
			default:
				num = ((type == "AT") ? 1 : 0);
				break;
			case "CTH":
			case "CTC":
			case "C":
			case "T":
			case "TS":
			case "TC":
			case "CS":
			case "CC":
				num = 1;
				break;
			}
			if (num != 0)
			{
				return 2;
			}
			int num2;
			switch (type)
			{
			default:
				num2 = ((type == "VM") ? 1 : 0);
				break;
			case "DM":
			case "CM":
			case "TM":
			case "EM":
			case "FM":
			case "Z":
			case "W":
			case "ZF":
				num2 = 1;
				break;
			}
			if (num2 != 0)
			{
				return 1;
			}
			return 1;
		}

		/// <summary>
		/// 建立读取PLC数据的指令，需要传入地址数据，以及读取的长度，地址示例参照类的说明文档<br />
		/// To create a command to read PLC data, you need to pass in the address data, and the length of the read. For an example of the address, refer to the class documentation
		/// </summary>
		/// <param name="address">软元件地址</param>
		/// <param name="length">读取长度</param>
		/// <returns>是否建立成功</returns>
		public static OperateResult<List<byte[]>> BuildReadCommand(string address, ushort length)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
			}
			return BuildReadCommand(operateResult.Content, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.BuildReadCommand(System.String,System.UInt16)" />
		public static OperateResult<List<byte[]>> BuildReadCommand(KeyenceNanoAddress address, ushort length)
		{
			if (length > 1)
			{
				length = (ushort)((int)length / GetWordAddressMultiple(address.DataCode));
			}
			int[] array = SoftBasic.SplitIntegerToArray(length, address.SplitLength);
			List<byte[]> list = new List<byte[]>();
			for (int i = 0; i < array.Length; i++)
			{
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append("RDS");
				stringBuilder.Append(" ");
				stringBuilder.Append(address.DataCode);
				stringBuilder.Append(address.GetAddressStartFormat());
				stringBuilder.Append(" ");
				stringBuilder.Append(array[i].ToString());
				stringBuilder.Append("\r");
				list.Add(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
				address.AddressStart += array[i];
			}
			return OperateResult.CreateSuccessResult(list);
		}

		/// <summary>
		/// 建立写入PLC数据的指令，需要传入地址数据，以及写入的数据信息，地址示例参照类的说明文档<br />
		/// To create a command to write PLC data, you need to pass in the address data and the written data information. For an example of the address, refer to the class documentation
		/// </summary>
		/// <param name="address">软元件地址</param>
		/// <param name="value">转换后的数据</param>
		/// <returns>是否成功的信息</returns>
		public static OperateResult<byte[]> BuildWriteCommand(string address, byte[] value)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("WRS");
			stringBuilder.Append(" ");
			stringBuilder.Append(operateResult.Content.DataCode);
			stringBuilder.Append(operateResult.Content.GetAddressStartFormat());
			stringBuilder.Append(" ");
			int num = value.Length / (GetWordAddressMultiple(operateResult.Content.DataCode) * 2);
			stringBuilder.Append(num.ToString());
			for (int i = 0; i < num; i++)
			{
				stringBuilder.Append(" ");
				stringBuilder.Append(BitConverter.ToUInt16(value, i * GetWordAddressMultiple(operateResult.Content.DataCode) * 2));
			}
			stringBuilder.Append("\r");
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		/// <summary>
		/// 构建写入扩展单元缓冲寄存器的报文命令，需要传入单元编号，地址，写入的数据，实际写入的数据格式才有无符号的方式<br />
		/// To construct a message command to write to the buffer register of the expansion unit, the unit number, address, 
		/// and data to be written need to be passed in, and the format of the actually written data is unsigned.
		/// </summary>
		/// <param name="unit">单元编号0~48</param>
		/// <param name="address">地址0~32767</param>
		/// <param name="value">写入的数据信息，单次交互最大256个字</param>
		/// <returns>包含是否成功的报文对象</returns>
		public static OperateResult<byte[]> BuildWriteExpansionMemoryCommand(byte unit, ushort address, byte[] value)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("UWR");
			stringBuilder.Append(" ");
			stringBuilder.Append(unit);
			stringBuilder.Append(" ");
			stringBuilder.Append(address);
			stringBuilder.Append(".U");
			stringBuilder.Append(" ");
			int num = value.Length / 2;
			stringBuilder.Append(num.ToString());
			for (int i = 0; i < num; i++)
			{
				stringBuilder.Append(" ");
				stringBuilder.Append(BitConverter.ToUInt16(value, i * 2));
			}
			stringBuilder.Append("\r");
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		/// <summary>
		/// 建立写入bool数据的指令，针对地址类型为 R,CR,MR,LR<br />
		/// Create instructions to write bool data, address type is R, CR, MR, LR
		/// </summary>
		/// <param name="address">软元件地址</param>
		/// <param name="value">转换后的数据</param>
		/// <returns>是否成功的信息</returns>
		public static OperateResult<byte[]> BuildWriteCommand(string address, bool value)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			StringBuilder stringBuilder = new StringBuilder();
			if (value)
			{
				stringBuilder.Append("ST");
			}
			else
			{
				stringBuilder.Append("RS");
			}
			stringBuilder.Append(" ");
			stringBuilder.Append(operateResult.Content.DataCode);
			stringBuilder.Append(operateResult.Content.GetAddressStartFormat());
			stringBuilder.Append("\r");
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		/// <summary>
		/// 批量写入数据位到plc地址，针对地址格式为 R,B,CR,MR,LR,VB<br />
		/// Write data bits in batches to the plc address, and the address format is R, B, CR, MR, LR, VB
		/// </summary>
		/// <param name="address">PLC的地址</param>
		/// <param name="value">等待写入的bool数组</param>
		/// <returns>写入bool数组的命令报文</returns>
		public static OperateResult<byte[]> BuildWriteCommand(string address, bool[] value)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("WRS");
			stringBuilder.Append(" ");
			stringBuilder.Append(operateResult.Content.DataCode);
			stringBuilder.Append(operateResult.Content.GetAddressStartFormat());
			stringBuilder.Append(" ");
			stringBuilder.Append(value.Length.ToString());
			for (int i = 0; i < value.Length; i++)
			{
				stringBuilder.Append(" ");
				stringBuilder.Append(value[i] ? "1" : "0");
			}
			stringBuilder.Append("\r");
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
		}

		private static string GetErrorText(string err)
		{
			if (err.StartsWith("E0"))
			{
				return StringResources.Language.KeyenceNanoE0;
			}
			if (err.StartsWith("E1"))
			{
				return StringResources.Language.KeyenceNanoE1;
			}
			if (err.StartsWith("E2"))
			{
				return StringResources.Language.KeyenceNanoE2;
			}
			if (err.StartsWith("E4"))
			{
				return StringResources.Language.KeyenceNanoE4;
			}
			if (err.StartsWith("E5"))
			{
				return StringResources.Language.KeyenceNanoE5;
			}
			if (err.StartsWith("E6"))
			{
				return StringResources.Language.KeyenceNanoE6;
			}
			return StringResources.Language.UnknownError + " " + err;
		}

		/// <summary>
		/// 校验读取返回数据状态，主要返回的第一个字节是不是E<br />
		/// Check the status of the data returned from reading, whether the first byte returned is E
		/// </summary>
		/// <param name="ack">反馈信息</param>
		/// <returns>是否成功的信息</returns>
		public static OperateResult CheckPlcReadResponse(byte[] ack)
		{
			try
			{
				if (ack.Length == 0)
				{
					return new OperateResult(StringResources.Language.MelsecFxReceiveZero);
				}
				if (ack[0] == 69)
				{
					return new OperateResult(GetErrorText(Encoding.ASCII.GetString(ack)));
				}
				if (ack[ack.Length - 1] != 10 && ack[ack.Length - 2] != 13)
				{
					return new OperateResult(StringResources.Language.MelsecFxAckWrong + " Actual: " + SoftBasic.ByteToHexString(ack, ' '));
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult("CheckPlcReadResponse failed: " + ex.Message + Environment.NewLine + ack.ToHexString(' '));
			}
		}

		/// <summary>
		/// 校验写入返回数据状态，检测返回的数据是不是OK<br />
		/// Verify the status of the returned data written and check whether the returned data is OK
		/// </summary>
		/// <param name="ack">反馈信息</param>
		/// <returns>是否成功的信息</returns>
		public static OperateResult CheckPlcWriteResponse(byte[] ack)
		{
			try
			{
				if (ack.Length == 0)
				{
					return new OperateResult(StringResources.Language.MelsecFxReceiveZero);
				}
				if (ack[0] == 79 && ack[1] == 75)
				{
					return OperateResult.CreateSuccessResult();
				}
				return new OperateResult(GetErrorText(Encoding.ASCII.GetString(ack)));
			}
			catch (Exception ex)
			{
				return new OperateResult("CheckPlcWriteResponse failed: " + ex.Message + Environment.NewLine + ack.ToHexString(' '));
			}
		}

		/// <summary>
		/// 从PLC反馈的数据进行提炼Bool操作<br />
		/// Refine Bool operation from data fed back from PLC
		/// </summary>
		/// <param name="addressType">地址的数据类型</param>
		/// <param name="response">PLC反馈的真实数据</param>
		/// <returns>数据提炼后的真实数据</returns>
		public static OperateResult<bool[]> ExtractActualBoolData(string addressType, byte[] response)
		{
			try
			{
				if (string.IsNullOrEmpty(addressType))
				{
					addressType = "R";
				}
				string @string = Encoding.Default.GetString(response.RemoveLast(2));
				int num;
				switch (addressType)
				{
				default:
					num = ((addressType == "VB") ? 1 : 0);
					break;
				case "R":
				case "CR":
				case "MR":
				case "LR":
				case "B":
					num = 1;
					break;
				}
				if (num != 0)
				{
					return OperateResult.CreateSuccessResult((from m in @string.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
						select m == "1").ToArray());
				}
				int num2;
				switch (addressType)
				{
				default:
					num2 = ((addressType == "CTC") ? 1 : 0);
					break;
				case "T":
				case "C":
				case "CTH":
					num2 = 1;
					break;
				}
				if (num2 != 0)
				{
					return OperateResult.CreateSuccessResult((from m in @string.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
						select m.StartsWith("1")).ToArray());
				}
				return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				OperateResult<bool[]> operateResult = new OperateResult<bool[]>();
				operateResult.Message = "Extract Msg：" + ex.Message + Environment.NewLine + "Data: " + SoftBasic.ByteToHexString(response);
				return operateResult;
			}
		}

		/// <summary>
		/// 从PLC反馈的数据进行提炼操作<br />
		/// Refining operation from data fed back from PLC
		/// </summary>
		/// <param name="addressType">地址的数据类型</param>
		/// <param name="response">PLC反馈的真实数据</param>
		/// <returns>数据提炼后的真实数据</returns>
		public static OperateResult<byte[]> ExtractActualData(string addressType, byte[] response)
		{
			try
			{
				if (string.IsNullOrEmpty(addressType))
				{
					addressType = "R";
				}
				string @string = Encoding.Default.GetString(response.RemoveLast(2));
				string[] array = @string.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				int num;
				switch (addressType)
				{
				default:
					num = ((addressType == "VM") ? 1 : 0);
					break;
				case "DM":
				case "EM":
				case "FM":
				case "ZF":
				case "W":
				case "TM":
				case "Z":
				case "CM":
					num = 1;
					break;
				}
				if (num != 0)
				{
					byte[] array2 = new byte[array.Length * 2];
					for (int i = 0; i < array.Length; i++)
					{
						BitConverter.GetBytes(ushort.Parse(array[i])).CopyTo(array2, i * 2);
					}
					return OperateResult.CreateSuccessResult(array2);
				}
				int num2;
				switch (addressType)
				{
				default:
					num2 = ((addressType == "CS") ? 1 : 0);
					break;
				case "AT":
				case "TC":
				case "CC":
				case "TS":
					num2 = 1;
					break;
				}
				if (num2 != 0)
				{
					byte[] array3 = new byte[array.Length * 4];
					for (int j = 0; j < array.Length; j++)
					{
						BitConverter.GetBytes(uint.Parse(array[j])).CopyTo(array3, j * 4);
					}
					return OperateResult.CreateSuccessResult(array3);
				}
				int num3;
				switch (addressType)
				{
				default:
					num3 = ((addressType == "CTC") ? 1 : 0);
					break;
				case "T":
				case "C":
				case "CTH":
					num3 = 1;
					break;
				}
				if (num3 != 0)
				{
					byte[] array4 = new byte[array.Length * 4];
					for (int k = 0; k < array.Length; k++)
					{
						string[] array5 = array[k].Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
						BitConverter.GetBytes(uint.Parse(array5[1])).CopyTo(array4, k * 4);
					}
					return OperateResult.CreateSuccessResult(array4);
				}
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				OperateResult<byte[]> operateResult = new OperateResult<byte[]>();
				operateResult.Message = "Extract Msg：" + ex.Message + Environment.NewLine + "Data: " + SoftBasic.ByteToHexString(response);
				return operateResult;
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Read(System.String,System.UInt16)" />
		/// <remarks>
		/// 地址支持读取扩展单元缓冲存储器的数据，例如读取扩展单元号1，地址100的数据，地址写为 unit=1;100<br />
		/// The address supports reading the data in the buffer memory of the expansion unit, such as reading the data of the expansion unit number 1, address 100, and the address is written as unit=1;100
		/// </remarks>
		public static OperateResult<byte[]> Read(IReadWriteDevice keyence, string address, ushort length)
		{
			if (address.StartsWith("unit="))
			{
				byte unit = (byte)HslHelper.ExtractParameter(ref address, "unit", 0);
				if (!ushort.TryParse(address, out var _))
				{
					return new OperateResult<byte[]>("Address is not right, convert ushort wrong!");
				}
				return ReadExpansionMemory(keyence, unit, ushort.Parse(address), length);
			}
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<List<byte[]>> operateResult2 = BuildReadCommand(operateResult.Content, length);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			List<byte> list = new List<byte>();
			for (int i = 0; i < operateResult2.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult3 = keyence.ReadFromCoreServer(operateResult2.Content[i]);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult3);
				}
				OperateResult operateResult4 = CheckPlcReadResponse(operateResult3.Content);
				if (!operateResult4.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult4);
				}
				OperateResult<byte[]> operateResult5 = ExtractActualData(operateResult.Content.DataCode, operateResult3.Content);
				if (!operateResult5.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult5);
				}
				list.AddRange(operateResult5.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Byte[])" />
		public static OperateResult Write(IReadWriteDevice keyence, string address, byte[] value)
		{
			OperateResult<byte[]> operateResult = BuildWriteCommand(address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = keyence.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckPlcWriteResponse(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.Read(HslCommunication.Core.IReadWriteDevice,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IReadWriteDevice keyence, string address, ushort length)
		{
			if (address.StartsWith("unit="))
			{
				byte unit = (byte)HslHelper.ExtractParameter(ref address, "unit", 0);
				if (!ushort.TryParse(address, out var _))
				{
					return new OperateResult<byte[]>("Address is not right, convert ushort wrong!");
				}
				return await ReadExpansionMemoryAsync(keyence, unit, ushort.Parse(address), length);
			}
			OperateResult<KeyenceNanoAddress> addressResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!addressResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(addressResult);
			}
			OperateResult<List<byte[]>> command = BuildReadCommand(addressResult.Content, length);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			List<byte> array = new List<byte>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(read);
				}
				OperateResult ackResult = CheckPlcReadResponse(read.Content);
				if (!ackResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(ackResult);
				}
				OperateResult<byte[]> extra = ExtractActualData(addressResult.Content.DataCode, read.Content);
				if (!extra.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(extra);
				}
				array.AddRange(extra.Content);
			}
			return OperateResult.CreateSuccessResult(array.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.Write(HslCommunication.Core.IReadWriteDevice,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice keyence, string address, byte[] value)
		{
			OperateResult<byte[]> command = BuildWriteCommand(address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult checkResult = CheckPlcWriteResponse(read.Content);
			if (!checkResult.IsSuccess)
			{
				return checkResult;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public static OperateResult<bool[]> ReadBool(IReadWriteDevice keyence, string address, ushort length)
		{
			OperateResult<KeyenceNanoAddress> operateResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			OperateResult<List<byte[]>> operateResult2 = BuildReadCommand(operateResult.Content, length);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult2);
			}
			List<bool> list = new List<bool>();
			for (int i = 0; i < operateResult2.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult3 = keyence.ReadFromCoreServer(operateResult2.Content[i]);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult3);
				}
				OperateResult operateResult4 = CheckPlcReadResponse(operateResult3.Content);
				if (!operateResult4.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult4);
				}
				OperateResult<bool[]> operateResult5 = ExtractActualBoolData(operateResult.Content.DataCode, operateResult3.Content);
				if (!operateResult5.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult5);
				}
				list.AddRange(operateResult5.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc />
		public static OperateResult Write(IReadWriteDevice keyence, string address, bool value)
		{
			OperateResult<byte[]> operateResult = BuildWriteCommand(address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = keyence.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckPlcWriteResponse(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		public static OperateResult Write(IReadWriteDevice keyence, string address, bool[] value)
		{
			OperateResult<byte[]> operateResult = BuildWriteCommand(address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = keyence.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckPlcWriteResponse(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(IReadWriteDevice keyence, string address, ushort length)
		{
			OperateResult<KeyenceNanoAddress> addressResult = KeyenceNanoAddress.ParseFrom(address, length);
			if (!addressResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(addressResult);
			}
			OperateResult<List<byte[]>> command = BuildReadCommand(addressResult.Content, length);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			List<bool> array = new List<bool>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				OperateResult ackResult = CheckPlcReadResponse(read.Content);
				if (!ackResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(ackResult);
				}
				OperateResult<bool[]> extra = ExtractActualBoolData(addressResult.Content.DataCode, read.Content);
				if (!extra.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(extra);
				}
				array.AddRange(extra.Content);
			}
			return OperateResult.CreateSuccessResult(array.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean)" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice keyence, string address, bool value)
		{
			OperateResult<byte[]> command = BuildWriteCommand(address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult checkResult = CheckPlcWriteResponse(read.Content);
			if (!checkResult.IsSuccess)
			{
				return checkResult;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice keyence, string address, bool[] value)
		{
			OperateResult<byte[]> command = BuildWriteCommand(address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult checkResult = CheckPlcWriteResponse(read.Content);
			if (!checkResult.IsSuccess)
			{
				return checkResult;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <c>[商业授权]</c> 查询PLC的型号信息<br />
		/// <b>[Authorization]</b> Query PLC model information
		/// </summary>
		/// <param name="keyence">PLC通信对象</param>
		/// <returns>包含型号的结果对象</returns>
		internal static OperateResult<KeyencePLCS> ReadPlcType(IReadWriteDevice keyence)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(Encoding.ASCII.GetBytes("?K\r"));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<KeyencePLCS>();
			}
			OperateResult operateResult2 = CheckPlcReadResponse(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<KeyencePLCS>();
			}
			string @string = Encoding.ASCII.GetString(operateResult.Content.RemoveLast(2));
			switch (@string)
			{
			case "48":
			case "49":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV700);
			case "50":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV1000);
			case "51":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV3000);
			case "52":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV5000);
			case "53":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV5500);
			default:
				return new OperateResult<KeyencePLCS>("Unknow type:" + @string);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadPlcType(HslCommunication.Core.IReadWriteDevice)" />
		internal static async Task<OperateResult<KeyencePLCS>> ReadPlcTypeAsync(IReadWriteDevice keyence)
		{
		
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(Encoding.ASCII.GetBytes("?K\r"));
			if (!read.IsSuccess)
			{
				return read.ConvertFailed<KeyencePLCS>();
			}
			OperateResult check = CheckPlcReadResponse(read.Content);
			if (!check.IsSuccess)
			{
				return check.ConvertFailed<KeyencePLCS>();
			}
			string type = Encoding.ASCII.GetString(read.Content.RemoveLast(2));
			switch (type)
			{
			case "48":
			case "49":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV700);
			case "50":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV1000);
			case "51":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV3000);
			case "52":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV5000);
			case "53":
				return OperateResult.CreateSuccessResult(KeyencePLCS.KV5500);
			default:
				return new OperateResult<KeyencePLCS>("Unknow type:" + type);
			}
		}

		/// <summary>
		/// <c>[商业授权]</c> 读取当前PLC的模式，如果是0，代表 PROG模式或者梯形图未登录，如果为1，代表RUN模式<br />
		/// <b>[Authorization]</b> Read the current PLC mode, if it is 0, it means PROG mode or the ladder diagram is not registered, if it is 1, it means RUN mode
		/// </summary>
		/// <param name="keyence">PLC通信对象</param>
		/// <returns>包含模式的结果对象</returns>
		internal static OperateResult<int> ReadPlcMode(IReadWriteDevice keyence)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(Encoding.ASCII.GetBytes("?M\r"));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<int>();
			}
			OperateResult operateResult2 = CheckPlcReadResponse(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<int>();
			}
			string @string = Encoding.ASCII.GetString(operateResult.Content.RemoveLast(2));
			if (@string == "0")
			{
				return OperateResult.CreateSuccessResult(0);
			}
			return OperateResult.CreateSuccessResult(1);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadPlcMode(HslCommunication.Core.IReadWriteDevice)" />
		internal static async Task<OperateResult<int>> ReadPlcModeAsync(IReadWriteDevice keyence)
		{
			
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(Encoding.ASCII.GetBytes("?M\r"));
			if (!read.IsSuccess)
			{
				return read.ConvertFailed<int>();
			}
			OperateResult check = CheckPlcReadResponse(read.Content);
			if (!check.IsSuccess)
			{
				return check.ConvertFailed<int>();
			}
			string type = Encoding.ASCII.GetString(read.Content.RemoveLast(2));
			if (type == "0")
			{
				return OperateResult.CreateSuccessResult(0);
			}
			return OperateResult.CreateSuccessResult(1);
		}

		/// <summary>
		/// <c>[商业授权]</c> 设置PLC的时间<br />
		/// <b>[Authorization]</b> Set PLC time
		/// </summary>
		/// <param name="keyence">PLC通信对象</param>
		/// <param name="dateTime">时间数据</param>
		/// <returns>是否设置成功</returns>
		public static OperateResult SetPlcDateTime(IReadWriteDevice keyence, DateTime dateTime)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(Encoding.ASCII.GetBytes($"WRT {dateTime.Year - 2000:D2} {dateTime.Month:D2} {dateTime.Day:D2} " + $"{dateTime.Hour:D2} {dateTime.Minute:D2} {dateTime.Second:D2} {(int)dateTime.DayOfWeek}\r"));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<int>();
			}
			return CheckPlcWriteResponse(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.SetPlcDateTime(HslCommunication.Core.IReadWriteDevice,System.DateTime)" />
		public static async Task<OperateResult> SetPlcDateTimeAsync(IReadWriteDevice keyence, DateTime dateTime)
		{
			
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(Encoding.ASCII.GetBytes($"WRT {dateTime.Year - 2000:D2} {dateTime.Month:D2} {dateTime.Day:D2} " + $"{dateTime.Hour:D2} {dateTime.Minute:D2} {dateTime.Second:D2} {(int)dateTime.DayOfWeek}\r"));
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckPlcWriteResponse(read.Content);
		}

		/// <summary>
		/// <c>[商业授权]</c> 读取指定软元件的注释信息<br />
		/// <b>[Authorization]</b> Read the comment information of the specified device
		/// </summary>
		/// <param name="keyence">PLC通信对象</param>
		/// <param name="address">软元件的地址</param>
		/// <returns>软元件的注释信息</returns>
		public static OperateResult<string> ReadAddressAnnotation(IReadWriteDevice keyence, string address)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(Encoding.ASCII.GetBytes("RDC " + address + "\r"));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<string>();
			}
			OperateResult operateResult2 = CheckPlcReadResponse(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<string>();
			}
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetString(operateResult.Content.RemoveLast(2)).Trim(' '));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadAddressAnnotation(HslCommunication.Core.IReadWriteDevice,System.String)" />
		public static async Task<OperateResult<string>> ReadAddressAnnotationAsync(IReadWriteDevice keyence, string address)
		{
			
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(Encoding.ASCII.GetBytes("RDC " + address + "\r"));
			if (!read.IsSuccess)
			{
				return read.ConvertFailed<string>();
			}
			OperateResult check = CheckPlcReadResponse(read.Content);
			if (!check.IsSuccess)
			{
				return check.ConvertFailed<string>();
			}
			return OperateResult.CreateSuccessResult(Encoding.ASCII.GetString(read.Content.RemoveLast(2)).Trim(' '));
		}

		/// <summary>
		/// <c>[商业授权]</c> 从扩展单元缓冲存储器连续读取指定个数的数据，单位为字<br />
		/// <b>[Authorization]</b> Continuously read the specified number of data from the expansion unit buffer memory, the unit is word
		/// </summary>
		/// <param name="keyence">PLC的通信对象</param>
		/// <param name="unit">单元编号</param>
		/// <param name="address">偏移地址</param>
		/// <param name="length">读取的长度，单位为字</param>
		/// <returns>包含是否成功的原始字节数组</returns>
		public static OperateResult<byte[]> ReadExpansionMemory(IReadWriteDevice keyence, byte unit, ushort address, ushort length)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(Encoding.ASCII.GetBytes($"URD {unit} {address}.U {length}\r"));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			OperateResult operateResult2 = CheckPlcReadResponse(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<byte[]>();
			}
			return ExtractActualData("DM", operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadExpansionMemory(HslCommunication.Core.IReadWriteDevice,System.Byte,System.UInt16,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadExpansionMemoryAsync(IReadWriteDevice keyence, byte unit, ushort address, ushort length)
		{
			
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(Encoding.ASCII.GetBytes($"URD {unit} {address}.U {length}\r"));
			if (!read.IsSuccess)
			{
				return read.ConvertFailed<byte[]>();
			}
			OperateResult check = CheckPlcReadResponse(read.Content);
			if (!check.IsSuccess)
			{
				return check.ConvertFailed<byte[]>();
			}
			return ExtractActualData("DM", read.Content);
		}

		/// <summary>
		///             <c>[商业授权]</c> 将原始字节数据写入到扩展的缓冲存储器，需要指定单元编号，偏移地址，写入的数据<br />
		/// <b>[Authorization]</b> To write the original byte data to the extended buffer memory, you need to specify the unit number, offset address, and write data
		/// </summary>
		/// <param name="keyence">PLC通信对象信息</param>
		/// <param name="unit">单元编号</param>
		/// <param name="address">偏移地址</param>
		/// <param name="value">等待写入的原始字节数据</param>
		/// <returns>是否写入成功的结果对象</returns>
		public static OperateResult WriteExpansionMemory(IReadWriteDevice keyence, byte unit, ushort address, byte[] value)
		{
			
			OperateResult<byte[]> operateResult = keyence.ReadFromCoreServer(BuildWriteExpansionMemoryCommand(unit, address, value).Content);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			return CheckPlcWriteResponse(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.WriteExpansionMemory(HslCommunication.Core.IReadWriteDevice,System.Byte,System.UInt16,System.Byte[])" />
		public static async Task<OperateResult> WriteExpansionMemoryAsync(IReadWriteDevice keyence, byte unit, ushort address, byte[] value)
		{
			
			OperateResult<byte[]> read = await keyence.ReadFromCoreServerAsync(BuildWriteExpansionMemoryCommand(unit, address, value).Content);
			if (!read.IsSuccess)
			{
				return read.ConvertFailed<byte[]>();
			}
			return CheckPlcWriteResponse(read.Content);
		}
	}
}
