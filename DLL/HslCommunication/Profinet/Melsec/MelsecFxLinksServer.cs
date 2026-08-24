using System;
using System.Linq;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;
using HslCommunication.Serial;

namespace HslCommunication.Profinet.Melsec
{
	/// <summary>
	/// 三菱的虚拟的FxLinks服务器
	/// </summary>
	public class MelsecFxLinksServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer dBuffer;

		private SoftBuffer rBuffer;

		private const int DataPoolLength = 65536;

		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.Station" />
		public byte Station { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.SumCheck" />
		public bool SumCheck { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.Format" />
		public int Format { get; set; } = 1;


		/// <summary>
		/// 实例化一个虚拟的FxLinks服务器
		/// </summary>
		public MelsecFxLinksServer()
		{
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			LogMsgFormatBinary = false;
			xBuffer = new SoftBuffer(131072);
			yBuffer = new SoftBuffer(131072);
			mBuffer = new SoftBuffer(131072);
			sBuffer = new SoftBuffer(131072);
			dBuffer = new SoftBuffer(131072);
			rBuffer = new SoftBuffer(131072);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<MelsecFxLinksAddress> operateResult = MelsecFxLinksAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content.TypeCode == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.TypeCode == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.TypeCode == "M")
			{
				return OperateResult.CreateSuccessResult(mBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.TypeCode == "S")
			{
				return OperateResult.CreateSuccessResult(sBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.TypeCode == "D")
			{
				return OperateResult.CreateSuccessResult(dBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			if (operateResult.Content.TypeCode == "R")
			{
				return OperateResult.CreateSuccessResult(rBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<MelsecFxLinksAddress> operateResult = MelsecFxLinksAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content.TypeCode == "X")
			{
				xBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "Y")
			{
				yBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "M")
			{
				mBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "S")
			{
				sBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "D")
			{
				dBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else
			{
				if (!(operateResult.Content.TypeCode == "R"))
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
				}
				rBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<MelsecFxLinksAddress> operateResult = MelsecFxLinksAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			if (operateResult.Content.TypeCode == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.TypeCode == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.TypeCode == "M")
			{
				return OperateResult.CreateSuccessResult(mBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.TypeCode == "S")
			{
				return OperateResult.CreateSuccessResult(sBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<MelsecFxLinksAddress> operateResult = MelsecFxLinksAddress.ParseFrom(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content.TypeCode == "X")
			{
				xBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "Y")
			{
				yBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.TypeCode == "M")
			{
				mBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else
			{
				if (!(operateResult.Content.TypeCode == "S"))
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
				}
				sBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return null;
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			OperateResult<byte[]> operateResult = ExtraMcCore(receive, Format);
			if (!operateResult.IsSuccess)
			{
				if (operateResult.ErrorCode < 256 && operateResult.ErrorCode > 0)
				{
					return OperateResult.CreateSuccessResult(PackCommand((byte)operateResult.ErrorCode, null, Format));
				}
				return operateResult;
			}
			string @string = Encoding.ASCII.GetString(operateResult.Content, 0, 2);
			if (@string == "BR")
			{
				return ReadBoolByCommand(operateResult.Content);
			}
			if (@string == "WR" || @string == "QR")
			{
				return ReadWordByCommand(operateResult.Content);
			}
			if (@string == "BW")
			{
				return WriteBoolByCommand(operateResult.Content);
			}
			if (@string == "WW" || @string == "QW")
			{
				return WriteWordByCommand(operateResult.Content);
			}
			return @string switch
			{
				"RR" => OperateResult.CreateSuccessResult(PackCommand(0, null, Format)), 
				"RS" => OperateResult.CreateSuccessResult(PackCommand(0, null, Format)), 
				"PC" => OperateResult.CreateSuccessResult(PackCommand(0, Encoding.ASCII.GetBytes("F3"), Format)), 
				_ => OperateResult.CreateSuccessResult(PackCommand(6, null, Format)), 
			};
		}

		private OperateResult<byte[]> ExtraMcCore(byte[] command, int format)
		{
			byte b = Convert.ToByte(Encoding.ASCII.GetString(command, 1, 2), 16);
			if (Station != b)
			{
				return new OperateResult<byte[]>($"Station Not Match, need: {Station}  but: {b}");
			}
			switch (format)
			{
			case 1:
				if (command[0] != 5)
				{
					return new OperateResult<byte[]>("First Byte Must Start with ENQ(0x05)");
				}
				if (SumCheck)
				{
					if (!SoftLRC.CalculateAccAndCheck(command, 1, 2))
					{
						return new OperateResult<byte[]>(2, "Sum Check Failed!");
					}
					return OperateResult.CreateSuccessResult(command.SelectMiddle(5, command.Length - 7));
				}
				return OperateResult.CreateSuccessResult(command.SelectMiddle(5, command.Length - 5));
			case 4:
				if (command[command.Length - 1] == 10 && command[command.Length - 2] == 13)
				{
					return ExtraMcCore(command.RemoveLast(2), 1);
				}
				return new OperateResult<byte[]>("In format 4 case, last two char must be CR(0x0d) and LF(0x0a)");
			default:
				return OperateResult.CreateSuccessResult(command);
			}
		}

		private int GetAddressOctOrTen(byte address)
		{
			return (address == 88 || address == 89) ? 8 : 10;
		}

		private SoftBuffer GetAddressBuffer(byte address)
		{
			return address switch
			{
				88 => xBuffer, 
				89 => yBuffer, 
				77 => mBuffer, 
				83 => sBuffer, 
				68 => dBuffer, 
				82 => rBuffer, 
				_ => null, 
			};
		}

		private OperateResult<byte[]> ReadBoolByCommand(byte[] command)
		{
			if (command[3] == 68 || command[3] == 82)
			{
				return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
			}
			int destIndex = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4), GetAddressOctOrTen(command[3]));
			int num = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
			if (num == 0)
			{
				num = 256;
			}
			SoftBuffer addressBuffer = GetAddressBuffer(command[3]);
			if (addressBuffer == null)
			{
				return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
			}
			return OperateResult.CreateSuccessResult(PackCommand(0, (from m in addressBuffer.GetBool(destIndex, num)
				select (byte)(m ? 49 : 48)).ToArray(), Format));
		}

		private OperateResult<byte[]> WriteBoolByCommand(byte[] command)
		{
			if (command[3] == 68 || command[3] == 82)
			{
				return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
			}
			int destIndex = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4), GetAddressOctOrTen(command[3]));
			int length = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
			bool[] value = (from m in command.SelectMiddle(10, length)
				select m == 49).ToArray();
			SoftBuffer addressBuffer = GetAddressBuffer(command[3]);
			if (addressBuffer == null)
			{
				return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
			}
			addressBuffer.SetBool(value, destIndex);
			return OperateResult.CreateSuccessResult(PackCommand(0, null, Format));
		}

		private OperateResult<byte[]> ReadWordByCommand(byte[] command)
		{
			if (command[3] == 88 || command[3] == 89 || command[3] == 77 || command[3] == 83)
			{
				int num = 0;
				int num2 = 0;
				if (command[0] == 81)
				{
					num = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 6), GetAddressOctOrTen(command[3]));
					num2 = Convert.ToInt32(Encoding.ASCII.GetString(command, 10, 2), 16);
				}
				else
				{
					num = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4), GetAddressOctOrTen(command[3]));
					num2 = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
				}
				SoftBuffer addressBuffer = GetAddressBuffer(command[3]);
				return OperateResult.CreateSuccessResult(PackCommand(0, Encoding.ASCII.GetBytes(addressBuffer.GetBool(num, num2 * 16).ToByteArray().ToHexString()), Format));
			}
			if (command[3] == 68 || command[3] == 82)
			{
				int num3 = 0;
				int num4 = 0;
				if (command[0] == 81)
				{
					num3 = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 6));
					num4 = Convert.ToInt32(Encoding.ASCII.GetString(command, 10, 2), 16);
				}
				else
				{
					num3 = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4));
					num4 = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
				}
				SoftBuffer addressBuffer2 = GetAddressBuffer(command[3]);
				return OperateResult.CreateSuccessResult(PackCommand(0, Encoding.ASCII.GetBytes(addressBuffer2.GetBytes(num3 * 2, num4 * 2).ToHexString()), Format));
			}
			return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
		}

		private OperateResult<byte[]> WriteWordByCommand(byte[] command)
		{
			if (command[3] == 88 || command[3] == 89 || command[3] == 77 || command[3] == 83)
			{
				int num = 0;
				int num2 = 0;
				bool[] value;
				if (command[0] == 81)
				{
					num = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 6), GetAddressOctOrTen(command[3]));
					num2 = Convert.ToInt32(Encoding.ASCII.GetString(command, 10, 2), 16);
					value = Encoding.ASCII.GetString(command, 12, num2 * 4).ToHexBytes().ToBoolArray();
				}
				else
				{
					num = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4), GetAddressOctOrTen(command[3]));
					num2 = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
					value = Encoding.ASCII.GetString(command, 10, num2 * 4).ToHexBytes().ToBoolArray();
				}
				SoftBuffer addressBuffer = GetAddressBuffer(command[3]);
				addressBuffer.SetBool(value, num);
				return OperateResult.CreateSuccessResult(PackCommand(0, null, Format));
			}
			if (command[3] == 68 || command[3] == 82)
			{
				int num3 = 0;
				int num4 = 0;
				byte[] data;
				if (command[0] == 81)
				{
					num3 = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 6));
					num4 = Convert.ToInt32(Encoding.ASCII.GetString(command, 10, 2), 16);
					data = Encoding.ASCII.GetString(command, 12, num4 * 4).ToHexBytes();
				}
				else
				{
					num3 = Convert.ToInt32(Encoding.ASCII.GetString(command, 4, 4));
					num4 = Convert.ToInt32(Encoding.ASCII.GetString(command, 8, 2), 16);
					data = Encoding.ASCII.GetString(command, 10, num4 * 4).ToHexBytes();
				}
				SoftBuffer addressBuffer2 = GetAddressBuffer(command[3]);
				addressBuffer2.SetBytes(data, num3 * 2);
				return OperateResult.CreateSuccessResult(PackCommand(0, null, Format));
			}
			return OperateResult.CreateSuccessResult(PackCommand(6, null, Format));
		}

		/// <inheritdoc />
		protected byte[] PackCommand(byte status, byte[] data, int format)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			if (data.Length == 0)
			{
				switch (format)
				{
				case 1:
				{
					if (status == 0)
					{
						byte[] bytes = Encoding.ASCII.GetBytes("\u0006F9FF");
						SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes, 1);
						return bytes;
					}
					byte[] bytes2 = Encoding.ASCII.GetBytes("\u001500FF00");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes2, 1);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes2, 5);
					return bytes2;
				}
				case 4:
				{
					byte[] array = PackCommand(status, data, 1);
					return SoftBasic.SpliceArray<byte>(array, new byte[2] { 13, 10 });
				}
				default:
					return null;
				}
			}
			switch (format)
			{
			case 1:
			{
				if (status != 0)
				{
					byte[] bytes3 = Encoding.ASCII.GetBytes("\u001500FF00");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes3, 1);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes3, 5);
					return bytes3;
				}
				byte[] array3 = new byte[(SumCheck ? 8 : 6) + data.Length];
				array3[0] = 2;
				SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(array3, 1);
				Encoding.ASCII.GetBytes("FF").CopyTo(array3, 3);
				data.CopyTo(array3, 5);
				array3[array3.Length - ((!SumCheck) ? 1 : 3)] = 3;
				if (SumCheck)
				{
					SoftLRC.CalculateAccAndFill(array3, 1, 2);
				}
				return array3;
			}
			case 4:
			{
				byte[] array2 = PackCommand(status, data, 1);
				return SoftBasic.SpliceArray<byte>(array2, new byte[2] { 13, 10 });
			}
			default:
				return null;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MelsecFxLinksServer[{base.Port}]";
		}
	}
}
