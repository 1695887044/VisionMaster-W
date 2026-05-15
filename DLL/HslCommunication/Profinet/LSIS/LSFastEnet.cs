using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.LSIS
{
	/// <summary>
	/// XGB Fast Enet I/F module supports open Ethernet. It provides network configuration that is to connect LSIS and other company PLC, PC on network
	/// </summary>
	public class LSFastEnet : DeviceTcpNet
	{
		/// <summary>
		/// 所有支持的地址信息
		/// </summary>
		public const string AddressTypes = "PMLKFTCDSQINUZR";

		/// <summary>
		/// set plc
		/// </summary>
		public string SetCpuType { get; set; }

		/// <summary>
		/// CPU TYPE
		/// </summary>
		public string CpuType { get; private set; }

		/// <summary>
		/// Cpu is error
		/// </summary>
		public bool CpuError { get; private set; }

		/// <summary>
		/// RUN, STOP, ERROR, DEBUG
		/// </summary>
		public LSCpuStatus LSCpuStatus { get; private set; }

		/// <summary>
		/// FEnet I/F module’s Base No.
		/// </summary>
		public byte BaseNo { get; set; } = 0;


		/// <summary>
		/// FEnet I/F module’s Slot No.
		/// </summary>
		public byte SlotNo { get; set; } = 3;


		/// <summary>
		///
		/// </summary>
		public LSCpuInfo CpuInfo { get; set; } = LSCpuInfo.XGK;


		/// <summary>
		///
		/// </summary>
		public string CompanyID { get; set; } = "LSIS-XGT";


		/// <summary>
		/// Instantiate a Default object
		/// </summary>
		public LSFastEnet()
		{
			base.WordLength = 2;
			IpAddress = "127.0.0.1";
			Port = 2004;
			base.ByteTransform = new RegularByteTransform();
		}

		/// <summary>
		/// Instantiate a object by ipaddress and port
		/// </summary>
		/// <param name="ipAddress">the ip address of the plc</param>
		/// <param name="port">the port of the plc, default is 2004</param>
		public LSFastEnet(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <summary>
		/// Instantiate a object by ipaddress, port, cpuType, slotNo
		/// </summary>
		/// <param name="CpuType">CpuType</param>
		/// <param name="ipAddress">the ip address of the plc</param>
		/// <param name="port">he port of the plc, default is 2004</param>
		/// <param name="slotNo">slot number</param>
		public LSFastEnet(string CpuType, string ipAddress, int port, byte slotNo)
			: this(ipAddress, port)
		{
			SetCpuType = CpuType;
			SlotNo = slotNo;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new LsisFastEnetMessage();
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			byte[] array = new byte[command.Length + 20];
			Encoding.ASCII.GetBytes(CompanyID).CopyTo(array, 0);
			switch (CpuInfo)
			{
			case LSCpuInfo.XGK:
				array[12] = 160;
				break;
			case LSCpuInfo.XGI:
				array[12] = 164;
				break;
			case LSCpuInfo.XGR:
				array[12] = 168;
				break;
			case LSCpuInfo.XGB_MK:
				array[12] = 176;
				break;
			case LSCpuInfo.XGB_IEC:
				array[12] = 180;
				break;
			}
			array[13] = 51;
			BitConverter.GetBytes((short)command.Length).CopyTo(array, 16);
			array[18] = (byte)(BaseNo * 16 + SlotNo);
			int num = 0;
			for (int i = 0; i < 19; i++)
			{
				num += array[i];
			}
			array[19] = (byte)num;
			command.CopyTo(array, 20);
			return array;
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<byte[]> operateResult = BuildReadByteCommand(address, length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return ExtractActualData(operateResult2.Content);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<byte[]> operateResult = BuildWriteByteCommand(address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return ExtractActualData(operateResult2.Content);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			OperateResult<byte[]> coreResult = BuildReadByteCommand(address, length);
			if (!coreResult.IsSuccess)
			{
				return coreResult;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(coreResult.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return ExtractActualData(read.Content);
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			OperateResult<byte[]> coreResult = BuildWriteByteCommand(address, value);
			if (!coreResult.IsSuccess)
			{
				return coreResult;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(coreResult.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return ExtractActualData(read.Content);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<byte[]> operateResult = BuildReadByteCommand(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult2);
			}
			OperateResult<byte[]> operateResult3 = ExtractActualData(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult3);
			}
			return OperateResult.CreateSuccessResult(SoftBasic.ByteToBoolArray(operateResult3.Content, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBool", "")]
		public override OperateResult<bool> ReadBool(string address)
		{
			OperateResult<byte[]> operateResult = BuildReadIndividualCommand(0, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult2);
			}
			OperateResult<byte[]> operateResult3 = ExtractActualData(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult3);
			}
			return OperateResult.CreateSuccessResult(SoftBasic.ByteToBoolArray(operateResult3.Content, 1)[0]);
		}

		/// <summary>
		/// ReadCoil
		/// </summary>
		/// <param name="address">Start address</param>
		/// <returns>Whether to read the successful</returns>
		public OperateResult<bool> ReadCoil(string address)
		{
			return ReadBool(address);
		}

		/// <summary>
		/// ReadCoil
		/// </summary>
		/// <param name="address">Start address</param>
		/// <param name="length">read address length</param>
		/// <returns>Whether to read the successful</returns>
		public OperateResult<bool[]> ReadCoil(string address, ushort length)
		{
			return ReadBool(address, length);
		}

		/// <summary>
		/// Read single byte value from plc
		/// </summary>
		/// <param name="address">Start address</param>
		/// <returns>Whether to write the successful</returns>
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <summary>
		/// Write single byte value to plc
		/// </summary>
		/// <param name="address">Start address</param>
		/// <param name="value">value</param>
		/// <returns>Whether to write the successful</returns>
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <summary>
		/// WriteCoil
		/// </summary>
		/// <param name="address">Start address</param>
		/// <param name="value">bool value</param>
		/// <returns>Whether to write the successful</returns>
		public OperateResult WriteCoil(string address, bool value)
		{
			return Write(address, new byte[2]
			{
				(byte)(value ? 1u : 0u),
				0
			});
		}

		/// <summary>
		/// WriteCoil
		/// </summary>
		/// <param name="address">Start address</param>
		/// <param name="value">bool value</param>
		/// <returns>Whether to write the successful</returns>
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return WriteCoil(address, value);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			OperateResult<byte[]> coreResult = BuildReadByteCommand(address, length);
			if (!coreResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(coreResult);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(coreResult.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(read);
			}
			OperateResult<byte[]> extract = ExtractActualData(read.Content);
			if (!extract.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(extract);
			}
			return OperateResult.CreateSuccessResult(SoftBasic.ByteToBoolArray(extract.Content, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool>> ReadBoolAsync(string address)
		{
			OperateResult<byte[]> coreResult = BuildReadIndividualCommand(0, address);
			if (!coreResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(coreResult);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(coreResult.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(read);
			}
			OperateResult<byte[]> extract = ExtractActualData(read.Content);
			if (!extract.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(extract);
			}
			return OperateResult.CreateSuccessResult(SoftBasic.ByteToBoolArray(extract.Content, 1)[0]);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.ReadCoil(System.String)" />
		public async Task<OperateResult<bool>> ReadCoilAsync(string address)
		{
			return await ReadBoolAsync(address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.ReadCoil(System.String,System.UInt16)" />
		public async Task<OperateResult<bool[]>> ReadCoilAsync(string address, ushort length)
		{
			return await ReadBoolAsync(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.ReadByte(System.String)" />
		public async Task<OperateResult<byte>> ReadByteAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.Write(System.String,System.Byte)" />
		public async Task<OperateResult> WriteAsync(string address, byte value)
		{
			return await WriteAsync(address, new byte[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.WriteCoil(System.String,System.Boolean)" />
		public async Task<OperateResult> WriteCoilAsync(string address, bool value)
		{
			return await WriteAsync(address, new byte[2]
			{
				(byte)(value ? 1u : 0u),
				0
			});
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.Write(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await WriteCoilAsync(address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"LSFastEnet[{IpAddress}:{Port}]";
		}

		/// <summary>
		/// GetAddressOfU IX0.0.0 QX0.0.0
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
				obj = ((!(IsHex(array[2]) && IsWrite)) ? ((object)((int.Parse(array[0]) * 32 + int.Parse(array[1])) * 10 + num)) : (int.Parse(array[0]) * 32 + int.Parse(array[1]) + array[2]));
			}
			else
			{
				obj = int.Parse(array[0]) * 32 + int.Parse(array[1]);
			}
			return $"{obj}";
		}

		/// <summary>
		/// GetAddressOfBitTo_XGK  MX1.0  DX1.0 to PLC LS XGK
		/// </summary>
		/// <param name="address"></param>
		/// <param name="startIndex"></param>
		/// <param name="IsWrite"></param>
		/// <returns></returns>
		public static string GetAddressOfBitTo_XGK(string address, int startIndex, bool IsWrite = false)
		{
			string text = address.Substring(startIndex);
			if (text.IndexOf('.') < 0)
			{
				if (IsWrite)
				{
					return text;
				}
				int num = 0;
				string value = text.Substring(0, text.Length - 1);
				string text2 = text.Substring(text.Length - 1);
				num = ((!text2.Contains(new string[6] { "A", "B", "C", "D", "E", "F" })) ? Convert.ToInt32(text2) : Convert.ToInt32(text2, 16));
				return $"{Convert.ToInt32(value) * 16 + num}";
			}
			string[] array = text.Split('.');
			int bitIndexInformation = HslHelper.GetBitIndexInformation(ref address);
			if (IsHex(array[1]) && IsWrite)
			{
				return address.Substring(2) + bitIndexInformation.ToString("X1");
			}
			return $"{Convert.ToInt32(array[0]) * 16 + bitIndexInformation}";
		}

		/// <summary>
		/// NumberStyles HexNumber
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		public static bool IsHex(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}
			bool result = false;
			for (int i = 0; i < value.Length; i++)
			{
				switch (value[i])
				{
				case 'A':
				case 'B':
				case 'C':
				case 'D':
				case 'E':
				case 'F':
				case 'a':
				case 'b':
				case 'c':
				case 'd':
				case 'e':
				case 'f':
					result = true;
					break;
				}
			}
			return result;
		}

		/// <summary>
		/// AnalysisAddress
		/// </summary>
		/// <param name="address">start address</param>
		/// <param name="IsWrite"></param>
		/// <returns>analysis result</returns>
		public static OperateResult<string> AnalysisAddress(string address, bool IsWrite = false)
		{
			bool flag = false;
			StringBuilder stringBuilder = new StringBuilder();
			try
			{
				stringBuilder.Append("%");
				if (address.IndexOf('.') > 0)
				{
					flag = true;
				}
				bool flag2 = false;
				for (int i = 0; i < "PMLKFTCDSQINUZR".Length; i++)
				{
					if ("PMLKFTCDSQINUZR"[i] != address[0])
					{
						continue;
					}
					stringBuilder.Append("PMLKFTCDSQINUZR"[i]);
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
						string value = string.Empty;
						if (address[1] == 'B')
						{
							stringBuilder.Append(flag ? "X" : "B");
							if (!flag)
							{
								value = ((address[0] != 'I' && address[0] != 'Q') ? $"{Convert.ToInt32(address.Substring(2))}" : address.Substring(2));
							}
							else if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								int bitIndexInformation2 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(bitIndexInformation2.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
							stringBuilder.Append(value);
						}
						else if (address[1] == 'W')
						{
							stringBuilder.Append(flag ? "X" : "W");
							if (!flag)
							{
								value = ((address[0] != 'I' && address[0] != 'Q') ? $"{Convert.ToInt32(address.Substring(2)) * 2}" : address.Substring(2));
							}
							else if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
							{
								int bitIndexInformation3 = HslHelper.GetBitIndexInformation(ref address);
								stringBuilder.Append(address.Substring(2));
								stringBuilder.Append(bitIndexInformation3.ToString("X1"));
							}
							else
							{
								stringBuilder.Append(GetAddressOfU_Q_I(address.Substring(2)));
							}
							stringBuilder.Append(value);
						}
						else if (address[1] == 'D')
						{
							stringBuilder.Append(flag ? "X" : "D");
							if (flag)
							{
								if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
								{
									int bitIndexInformation4 = HslHelper.GetBitIndexInformation(ref address);
									stringBuilder.Append(address.Substring(2));
									stringBuilder.Append(bitIndexInformation4.ToString("X1"));
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
								value = $"{Convert.ToInt32(address.Substring(2)) * 4}";
								stringBuilder.Append(value);
							}
						}
						else if (address[1] == 'L')
						{
							stringBuilder.Append(flag ? "X" : "L");
							if (flag)
							{
								if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
								{
									int bitIndexInformation5 = HslHelper.GetBitIndexInformation(ref address);
									stringBuilder.Append(address.Substring(2));
									stringBuilder.Append(bitIndexInformation5.ToString("X1"));
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
								value = $"{Convert.ToInt32(address.Substring(2)) * 8}";
								stringBuilder.Append(value);
							}
						}
						else
						{
							stringBuilder.Append(flag ? "X" : "B");
							if (flag)
							{
								if (address[0] != 'U' || address[0] != 'I' || address[0] != 'Q')
								{
									int bitIndexInformation6 = HslHelper.GetBitIndexInformation(ref address);
									stringBuilder.Append(address.Substring(1));
									stringBuilder.Append(bitIndexInformation6.ToString("X1"));
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
							else if (IsHex(address.Substring(1)))
							{
								stringBuilder.Append(address.Substring(1));
							}
							else
							{
								stringBuilder.Append($"{Convert.ToInt32(address.Substring(1)) * 2}");
							}
						}
					}
					flag2 = true;
					break;
				}
				if (!flag2)
				{
					throw new Exception(StringResources.Language.NotSupportedDataType);
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
			return OperateResult.CreateSuccessResult(stringBuilder.ToString());
		}

		/// <summary>
		/// Get DataType to Address
		/// </summary>
		/// <param name="address">address</param>
		/// <returns>dataType</returns>
		public static OperateResult<string> GetDataTypeToAddress(string address)
		{
			string value = string.Empty;
			try
			{
				bool flag = false;
				for (int i = 0; i < "PMLKFTCDSQINUZR".Length; i++)
				{
					if ("PMLKFTCDSQINUZR"[i] == address[0])
					{
						value = ((address[1] == 'X') ? "Bit" : ((address[1] == 'W') ? "Word" : ((address[1] == 'D') ? "DWord" : ((address[1] == 'L') ? "LWord" : ((address[1] == 'B') ? "Continuous" : ((address.IndexOf('.') <= 0) ? "Continuous" : "Bit"))))));
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					throw new Exception(StringResources.Language.NotSupportedDataType);
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
			return OperateResult.CreateSuccessResult(value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.BuildReadIndividualCommand(System.Byte,System.String[])" />
		public static OperateResult<byte[]> BuildReadIndividualCommand(byte dataType, string address)
		{
			return BuildReadIndividualCommand(dataType, new string[1] { address });
		}

		/// <summary>
		/// Multi reading address Type of Read Individual
		/// </summary>
		/// <param name="dataType">dataType bit:0x04, byte:0x01, word:0x02, dword:0x03, lword:0x04, continuous:0x14</param>
		/// <param name="addresses">address, for example: MX100, PX100</param>
		/// <returns>Read Individual Command</returns>
		public static OperateResult<byte[]> BuildReadIndividualCommand(byte dataType, string[] addresses)
		{
			MemoryStream memoryStream = new MemoryStream();
			memoryStream.WriteByte(84);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte(dataType);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte((byte)addresses.Length);
			memoryStream.WriteByte(0);
			foreach (string address in addresses)
			{
				OperateResult<string> operateResult = AnalysisAddress(address);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				memoryStream.WriteByte((byte)operateResult.Content.Length);
				memoryStream.WriteByte(0);
				byte[] bytes = Encoding.ASCII.GetBytes(operateResult.Content);
				memoryStream.Write(bytes, 0, bytes.Length);
			}
			return OperateResult.CreateSuccessResult(memoryStream.ToArray());
		}

		private static OperateResult<byte[]> BuildReadByteCommand(string address, ushort length)
		{
			OperateResult<string> operateResult = AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<string> dataTypeToAddress = GetDataTypeToAddress(address);
			if (!dataTypeToAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataTypeToAddress);
			}
			byte[] array = new byte[12 + operateResult.Content.Length];
			array[0] = 84;
			array[1] = 0;
			switch (dataTypeToAddress.Content)
			{
			case "Bit":
				array[2] = 0;
				break;
			case "Byte":
				array[2] = 1;
				break;
			case "Word":
				array[2] = 2;
				break;
			case "DWord":
				array[2] = 3;
				break;
			case "LWord":
				array[2] = 4;
				break;
			case "Continuous":
				array[2] = 20;
				break;
			}
			array[3] = 0;
			array[4] = 0;
			array[5] = 0;
			array[6] = 1;
			array[7] = 0;
			array[8] = (byte)operateResult.Content.Length;
			array[9] = 0;
			Encoding.ASCII.GetBytes(operateResult.Content).CopyTo(array, 10);
			BitConverter.GetBytes(length).CopyTo(array, array.Length - 2);
			return OperateResult.CreateSuccessResult(array);
		}

		private OperateResult<byte[]> BuildWriteByteCommand(string address, byte[] data)
		{
			OperateResult<string> operateResult = AnalysisAddress(address, IsWrite: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<string> dataTypeToAddress = GetDataTypeToAddress(address);
			if (!dataTypeToAddress.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(dataTypeToAddress);
			}
			byte[] array = new byte[12 + operateResult.Content.Length + data.Length];
			switch (dataTypeToAddress.Content)
			{
			case "Bit":
				array[2] = 0;
				break;
			case "Byte":
				array[2] = 1;
				break;
			case "Word":
				array[2] = 2;
				break;
			case "DWord":
				array[2] = 3;
				break;
			case "LWord":
				array[2] = 4;
				break;
			case "Continuous":
				array[2] = 20;
				break;
			}
			array[0] = 88;
			array[1] = 0;
			array[3] = 0;
			array[4] = 0;
			array[5] = 0;
			array[6] = 1;
			array[7] = 0;
			array[8] = (byte)operateResult.Content.Length;
			array[9] = 0;
			Encoding.ASCII.GetBytes(operateResult.Content).CopyTo(array, 10);
			BitConverter.GetBytes(data.Length).CopyTo(array, array.Length - 2 - data.Length);
			data.CopyTo(array, array.Length - data.Length);
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// Returns true data content, supports read and write returns
		/// </summary>
		/// <param name="response">response data</param>
		/// <returns>real data</returns>
		public OperateResult<byte[]> ExtractActualData(byte[] response)
		{
			if (response.Length < 20)
			{
				return new OperateResult<byte[]>("Length is less than 20:" + SoftBasic.ByteToHexString(response));
			}
			try
			{
				ushort num = BitConverter.ToUInt16(response, 10);
				BitArray bitArray = new BitArray(BitConverter.GetBytes(num));
				int num2 = (int)num % 32;
				switch ((int)num % 32)
				{
				case 1:
					CpuType = "XGK/R-CPUH";
					break;
				case 2:
					CpuType = "XGB/XBCU";
					break;
				case 4:
					CpuType = "XGK-CPUE";
					break;
				case 5:
					CpuType = "XGK/R-CPUH";
					break;
				}
				CpuError = bitArray[7];
				if (bitArray[8])
				{
					LSCpuStatus = LSCpuStatus.RUN;
				}
				if (bitArray[9])
				{
					LSCpuStatus = LSCpuStatus.STOP;
				}
				if (bitArray[10])
				{
					LSCpuStatus = LSCpuStatus.ERROR;
				}
				if (bitArray[11])
				{
					LSCpuStatus = LSCpuStatus.DEBUG;
				}
				if (response.Length < 28)
				{
					return new OperateResult<byte[]>("Length is less than 28:" + SoftBasic.ByteToHexString(response));
				}
				ushort num3 = BitConverter.ToUInt16(response, 26);
				if (num3 > 0)
				{
					return new OperateResult<byte[]>(response[28], "Error:" + GetErrorDesciption(response[28]));
				}
				if (response[20] == 89)
				{
					return OperateResult.CreateSuccessResult(new byte[0]);
				}
				if (response[20] == 85)
				{
					try
					{
						ushort num4 = BitConverter.ToUInt16(response, 30);
						byte[] array = new byte[num4];
						Array.Copy(response, 32, array, 0, num4);
						return OperateResult.CreateSuccessResult(array);
					}
					catch (Exception ex)
					{
						return new OperateResult<byte[]>(ex.Message);
					}
				}
				return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
			}
			catch (Exception ex2)
			{
				return new OperateResult<byte[]>("ExtractActualData failed: " + ex2.Message + " Souce: " + response.ToHexString(' '));
			}
		}

		/// <summary>
		/// get the description of the error code meanning
		/// </summary>
		/// <param name="code">code value</param>
		/// <returns>string information</returns>
		public static string GetErrorDesciption(byte code)
		{
			return code switch
			{
				0 => "Normal", 
				1 => "Physical layer error (TX, RX unavailable)", 
				3 => "There is no identifier of Function Block to receive in communication channel", 
				4 => "Mismatch of data type", 
				5 => "Reset is received from partner station", 
				6 => "Communication instruction of partner station is not ready status", 
				7 => "Device status of remote station is not desirable status", 
				8 => "Access to some target is not available", 
				9 => "Can’ t deal with communication instruction of partner station by too many reception", 
				10 => "Time Out error", 
				11 => "Structure error", 
				12 => "Abort", 
				13 => "Reject(local/remote)", 
				14 => "Communication channel establishment error (Connect/Disconnect)", 
				15 => "High speed communication and connection service error", 
				33 => "Can’t find variable identifier", 
				34 => "Address error", 
				50 => "Response error", 
				113 => "Object Access Unsupported", 
				187 => "Unknown error code (communication code of other company) is received", 
				_ => "Unknown error", 
			};
		}
	}
}
