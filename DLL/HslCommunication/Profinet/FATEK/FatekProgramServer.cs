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

namespace HslCommunication.Profinet.FATEK
{
	/// <summary>
	/// 永宏编程口协议的虚拟PLC，可以用来和<see cref="T:HslCommunication.Profinet.FATEK.FatekProgram" />及<see cref="T:HslCommunication.Profinet.FATEK.FatekProgramOverTcp" />类做通信测试，支持简单数据的读写操作。<br />
	/// The virtual PLC of Yonghong programming port protocol can be used to communicate with <see cref="T:HslCommunication.Profinet.FATEK.FatekProgram" /> and <see cref="T:HslCommunication.Profinet.FATEK.FatekProgramOverTcp" /> class, 
	/// and support simple data read and write operations.
	/// </summary>
	public class FatekProgramServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer tBuffer;

		private SoftBuffer cBuffer;

		private SoftBuffer tmrBuffer;

		private SoftBuffer ctrBuffer;

		private SoftBuffer hrBuffer;

		private SoftBuffer drBuffer;

		private byte station = 1;

		private const int DataPoolLength = 65536;

		private bool run = false;

		/// <inheritdoc cref="P:HslCommunication.Profinet.FATEK.FatekProgram.Station" />
		public byte Station
		{
			get
			{
				return station;
			}
			set
			{
				station = value;
			}
		}

		/// <summary>
		/// 实例化一个基于Programe协议的虚拟的永宏PLC对象，可以用来和<see cref="T:HslCommunication.Profinet.FATEK.FatekProgram" />进行通信测试。
		/// </summary>
		public FatekProgramServer()
		{
			xBuffer = new SoftBuffer(65536);
			yBuffer = new SoftBuffer(65536);
			mBuffer = new SoftBuffer(65536);
			sBuffer = new SoftBuffer(65536);
			tBuffer = new SoftBuffer(65536);
			cBuffer = new SoftBuffer(65536);
			tmrBuffer = new SoftBuffer(131072);
			ctrBuffer = new SoftBuffer(131072);
			hrBuffer = new SoftBuffer(131072);
			drBuffer = new SoftBuffer(131072);
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			LogMsgFormatBinary = false;
			base.WordLength = 1;
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[917504];
			xBuffer.GetBytes().CopyTo(array, 0);
			yBuffer.GetBytes().CopyTo(array, 65536);
			mBuffer.GetBytes().CopyTo(array, 131072);
			sBuffer.GetBytes().CopyTo(array, 196608);
			tBuffer.GetBytes().CopyTo(array, 262144);
			cBuffer.GetBytes().CopyTo(array, 327680);
			tmrBuffer.GetBytes().CopyTo(array, 393216);
			ctrBuffer.GetBytes().CopyTo(array, 524288);
			hrBuffer.GetBytes().CopyTo(array, 655360);
			drBuffer.GetBytes().CopyTo(array, 786432);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 917504)
			{
				throw new Exception("File is not correct");
			}
			xBuffer.SetBytes(content, 0, 65536);
			yBuffer.SetBytes(content, 65536, 65536);
			mBuffer.SetBytes(content, 131072, 65536);
			sBuffer.SetBytes(content, 196608, 65536);
			tBuffer.SetBytes(content, 262144, 65536);
			cBuffer.SetBytes(content, 327680, 65536);
			tmrBuffer.SetBytes(content, 393216, 131072);
			ctrBuffer.SetBytes(content, 524288, 131072);
			hrBuffer.SetBytes(content, 655360, 131072);
			drBuffer.SetBytes(content, 786432, 131072);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<FatekProgramAddress> operateResult = FatekProgramAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			if (operateResult.Content.DataCode == "D")
			{
				return OperateResult.CreateSuccessResult(drBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			if (operateResult.Content.DataCode == "R")
			{
				return OperateResult.CreateSuccessResult(hrBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			if (operateResult.Content.DataCode == "RT")
			{
				return OperateResult.CreateSuccessResult(tmrBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			if (operateResult.Content.DataCode == "CT")
			{
				return OperateResult.CreateSuccessResult(ctrBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
			}
			if (operateResult.Content.DataCode == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.DataCode == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.DataCode == "M")
			{
				return OperateResult.CreateSuccessResult(mBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.DataCode == "S")
			{
				return OperateResult.CreateSuccessResult(sBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.DataCode == "T")
			{
				return OperateResult.CreateSuccessResult(tBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			if (operateResult.Content.DataCode == "C")
			{
				return OperateResult.CreateSuccessResult(cBuffer.GetBool(operateResult.Content.AddressStart, length * 16).ToByteArray());
			}
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<FatekProgramAddress> operateResult = FatekProgramAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			if (operateResult.Content.DataCode == "D")
			{
				drBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else if (operateResult.Content.DataCode == "R")
			{
				hrBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else if (operateResult.Content.DataCode == "RT")
			{
				tmrBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else if (operateResult.Content.DataCode == "CT")
			{
				ctrBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			}
			else if (operateResult.Content.DataCode == "X")
			{
				xBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "Y")
			{
				yBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "M")
			{
				mBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "S")
			{
				sBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "T")
			{
				tBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			else
			{
				if (!(operateResult.Content.DataCode == "C"))
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
				}
				cBuffer.SetBool(value.ToBoolArray(), operateResult.Content.AddressStart);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<FatekProgramAddress> operateResult = FatekProgramAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<bool[]>();
			}
			if (operateResult.Content.DataCode == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.DataCode == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.DataCode == "M")
			{
				return OperateResult.CreateSuccessResult(mBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.DataCode == "S")
			{
				return OperateResult.CreateSuccessResult(sBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.DataCode == "T")
			{
				return OperateResult.CreateSuccessResult(tBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			if (operateResult.Content.DataCode == "C")
			{
				return OperateResult.CreateSuccessResult(cBuffer.GetBool(operateResult.Content.AddressStart, length));
			}
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<FatekProgramAddress> operateResult = FatekProgramAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[]>();
			}
			if (operateResult.Content.DataCode == "X")
			{
				xBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "Y")
			{
				yBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "M")
			{
				mBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "S")
			{
				sBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else if (operateResult.Content.DataCode == "T")
			{
				tBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			else
			{
				if (!(operateResult.Content.DataCode == "C"))
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
				}
				cBuffer.SetBool(value, operateResult.Content.AddressStart);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(3);
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			if (receivedLength < 5)
			{
				return false;
			}
			return buffer[receivedLength - 1] == 3;
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte b = Convert.ToByte(Encoding.ASCII.GetString(receive, 1, 2), 16);
			if (b != Station)
			{
				return new OperateResult<byte[]>($"Station is not match, need [{station}] but actual is [{b}]");
			}
			switch (Encoding.ASCII.GetString(receive, 3, 2))
			{
			case "44":
				return OperateResult.CreateSuccessResult(ReadBoolByMessage(receive));
			case "45":
				return OperateResult.CreateSuccessResult(WriteBoolByMessage(receive));
			case "46":
				return OperateResult.CreateSuccessResult(ReadWordByMessage(receive));
			case "47":
				return OperateResult.CreateSuccessResult(WriteWordByMessage(receive));
			case "41":
				run = receive[5] == 49;
				return OperateResult.CreateSuccessResult(PackResponseBack(receive, 48, null));
			case "40":
				return OperateResult.CreateSuccessResult(PackResponseBack(receive, 48, Encoding.ASCII.GetBytes(new byte[3]
				{
					(byte)(run ? 1 : 0),
					0,
					0
				}.ToHexString())));
			default:
				return OperateResult.CreateSuccessResult(PackResponseBack(receive, 52, null));
			}
		}

		private byte[] PackResponseBack(byte[] receive, byte err, byte[] value)
		{
			if (value == null)
			{
				value = new byte[0];
			}
			byte[] array = new byte[9 + value.Length];
			array[0] = 2;
			array[1] = receive[1];
			array[2] = receive[2];
			array[3] = receive[3];
			array[4] = receive[4];
			array[5] = err;
			value.CopyTo(array, 6);
			SoftLRC.CalculateAccAndFill(array, 0, 3);
			array[array.Length - 1] = 3;
			return array;
		}

		private SoftBuffer GetBoolBuffer(char code)
		{
			return code switch
			{
				'X' => xBuffer, 
				'Y' => yBuffer, 
				'M' => mBuffer, 
				'S' => sBuffer, 
				'T' => tBuffer, 
				'C' => cBuffer, 
				_ => null, 
			};
		}

		private SoftBuffer GetWordBuffer(byte[] receive, out int address)
		{
			if (Encoding.ASCII.GetString(receive, 7, 2) == "RT")
			{
				address = Convert.ToInt32(Encoding.ASCII.GetString(receive, 9, 4));
				return tmrBuffer;
			}
			if (Encoding.ASCII.GetString(receive, 7, 2) == "RC")
			{
				address = Convert.ToInt32(Encoding.ASCII.GetString(receive, 9, 4));
				return ctrBuffer;
			}
			if (Encoding.ASCII.GetString(receive, 7, 1) == "D")
			{
				address = Convert.ToInt32(Encoding.ASCII.GetString(receive, 8, 5));
				return drBuffer;
			}
			if (Encoding.ASCII.GetString(receive, 7, 1) == "R")
			{
				address = Convert.ToInt32(Encoding.ASCII.GetString(receive, 8, 5));
				return hrBuffer;
			}
			address = 0;
			return null;
		}

		private byte[] ReadBoolByMessage(byte[] receive)
		{
			int num = Convert.ToInt32(Encoding.ASCII.GetString(receive, 5, 2), 16);
			if (num == 0)
			{
				num = 256;
			}
			int destIndex = Convert.ToInt32(Encoding.ASCII.GetString(receive, 8, 4), 10);
			SoftBuffer boolBuffer = GetBoolBuffer((char)receive[7]);
			if (boolBuffer == null)
			{
				return PackResponseBack(receive, 52, null);
			}
			return PackResponseBack(receive, 48, (from m in boolBuffer.GetBool(destIndex, num)
				select (byte)(m ? 49 : 48)).ToArray());
		}

		private byte[] WriteBoolByMessage(byte[] receive)
		{
			int num = Convert.ToInt32(Encoding.ASCII.GetString(receive, 5, 2), 16);
			if (num == 0)
			{
				num = 256;
			}
			int destIndex = Convert.ToInt32(Encoding.ASCII.GetString(receive, 8, 4), 10);
			SoftBuffer boolBuffer = GetBoolBuffer((char)receive[7]);
			if (boolBuffer == null)
			{
				return PackResponseBack(receive, 52, null);
			}
			bool[] value = (from m in receive.SelectMiddle(12, num)
				select (m == 49) ? true : false).ToArray();
			boolBuffer.SetBool(value, destIndex);
			return PackResponseBack(receive, 48, null);
		}

		private byte[] ReadWordByMessage(byte[] receive)
		{
			int num = Convert.ToInt32(Encoding.ASCII.GetString(receive, 5, 2), 16);
			if (num > 64)
			{
				return PackResponseBack(receive, 50, null);
			}
			int address;
			SoftBuffer wordBuffer = GetWordBuffer(receive, out address);
			if (wordBuffer == null)
			{
				return PackResponseBack(receive, 52, null);
			}
			return PackResponseBack(receive, 48, Encoding.ASCII.GetBytes(wordBuffer.GetBytes(address * 2, num * 2).ToHexString()));
		}

		private byte[] WriteWordByMessage(byte[] receive)
		{
			int num = Convert.ToInt32(Encoding.ASCII.GetString(receive, 5, 2), 16);
			if (num > 64)
			{
				return PackResponseBack(receive, 50, null);
			}
			int address;
			SoftBuffer wordBuffer = GetWordBuffer(receive, out address);
			if (wordBuffer == null)
			{
				return PackResponseBack(receive, 52, null);
			}
			byte[] data = Encoding.ASCII.GetString(receive, 13, num * 4).ToHexBytes();
			wordBuffer.SetBytes(data, address * 2);
			return PackResponseBack(receive, 48, null);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				xBuffer.Dispose();
				yBuffer.Dispose();
				mBuffer.Dispose();
				sBuffer.Dispose();
				tBuffer.Dispose();
				cBuffer.Dispose();
				tmrBuffer.Dispose();
				ctrBuffer.Dispose();
				hrBuffer.Dispose();
				drBuffer.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FatekProgramServer[{base.Port}]";
		}
	}
}
