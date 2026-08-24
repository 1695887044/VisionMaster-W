using System;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Fuji
{
	/// <summary>
	/// 富士Command-Setting-type协议实现的虚拟服务器，支持的地址为 B,M,K,D,W9,BD,F,A,WL,W21,W9。具体的支持列表参考api文档<br />
	/// The virtual server implemented by Fuji Command-Setting-type protocol supports addresses B, M, K, D, W9, BD, F, A, WL, W21, W9. 
	/// For the specific support list, please refer to the api documentation
	/// </summary>
	/// <remarks>
	/// 当前的虚拟服务器实例化之后，调用<see cref="M:HslCommunication.Core.Net.NetworkServerBase.ServerStart" /> 方法即可，还有个参数<see cref="P:HslCommunication.Profinet.Fuji.FujiCommandSettingTypeServer.DataSwap" />需要特别注意，约定了数据的大小端形式。
	/// </remarks>
	public class FujiCommandSettingTypeServer : DeviceServer
	{
		private bool dataSwap = false;

		private SoftBuffer bBuffer;

		private SoftBuffer mBuffer;

		private SoftBuffer kBuffer;

		private SoftBuffer fBuffer;

		private SoftBuffer aBuffer;

		private SoftBuffer dBuffer;

		private SoftBuffer sBuffer;

		private SoftBuffer w9Buffer;

		private SoftBuffer bdBuffer;

		private SoftBuffer wlBuffer;

		private SoftBuffer w21Buffer;

		private const int DataPoolLength = 65536;

		/// <inheritdoc cref="P:HslCommunication.Profinet.Fuji.FujiCommandSettingType.DataSwap" />
		public bool DataSwap
		{
			get
			{
				return dataSwap;
			}
			set
			{
				dataSwap = value;
				if (value)
				{
					base.ByteTransform = new RegularByteTransform();
				}
				else
				{
					base.ByteTransform = new ReverseBytesTransform();
				}
			}
		}

		/// <summary>
		/// 实例化一个富士的服务器<br />
		/// </summary>
		public FujiCommandSettingTypeServer()
		{
			bBuffer = new SoftBuffer(65536);
			mBuffer = new SoftBuffer(65536);
			kBuffer = new SoftBuffer(65536);
			dBuffer = new SoftBuffer(65536);
			sBuffer = new SoftBuffer(65536);
			w9Buffer = new SoftBuffer(65536);
			bdBuffer = new SoftBuffer(65536);
			fBuffer = new SoftBuffer(65536);
			aBuffer = new SoftBuffer(65536);
			wlBuffer = new SoftBuffer(65536);
			w21Buffer = new SoftBuffer(65536);
			base.WordLength = 2;
			base.ByteTransform = new ReverseBytesTransform();
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			try
			{
				OperateResult<byte[]> operateResult = FujiCommandSettingType.BuildReadCommand(address, length);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				byte[] response = ReadByMessage(operateResult.Content);
				return FujiCommandSettingType.UnpackResponseContentHelper(operateResult.Content, response);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			try
			{
				OperateResult<byte[]> operateResult = FujiCommandSettingType.BuildWriteCommand(address, value);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				byte[] response = WriteByMessage(operateResult.Content);
				return FujiCommandSettingType.UnpackResponseContentHelper(operateResult.Content, response);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 从PLC读取byte类型的数据信息，通常针对步进寄存器，也就是 S100 的地址
		/// </summary>
		/// <param name="address">PLC地址数据，例如 S100</param>
		/// <returns>是否读取成功结果对象</returns>
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <summary>
		/// 将Byte输入写入到PLC之中，通常针对步进寄存器，也就是 S100 的地址
		/// </summary>
		/// <param name="address">PLC地址数据，例如 S100</param>
		/// <param name="value">数据信息</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBool", "")]
		public override OperateResult<bool> ReadBool(string address)
		{
			return base.ReadBool(address);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return base.Write(address, value);
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new FujiCommandSettingTypeMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			byte[] array = null;
			array = ((receive[0] == 0 && receive[2] == 0) ? ReadByMessage(receive) : ((receive[0] != 1 || receive[2] != 0) ? PackResponseResult(receive, 32, null) : WriteByMessage(receive)));
			return OperateResult.CreateSuccessResult(array);
		}

		private byte[] PackResponseResult(byte[] command, byte err, byte[] value)
		{
			if (err > 0 || command[0] == 1)
			{
				byte[] array = new byte[9];
				Array.Copy(command, 0, array, 0, 9);
				array[1] = err;
				array[4] = 4;
				return array;
			}
			if (value == null)
			{
				value = new byte[0];
			}
			byte[] array2 = new byte[10 + value.Length];
			Array.Copy(command, 0, array2, 0, 9);
			array2[4] = (byte)(5 + value.Length);
			value.CopyTo(array2, 10);
			return array2;
		}

		private byte[] ReadByMessage(byte[] command)
		{
			int num = command[5] + command[6] * 256;
			int length = command[7] + command[8] * 256;
			if (command[3] == 0)
			{
				return PackResponseResult(command, 0, bBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 1)
			{
				return PackResponseResult(command, 0, mBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 2)
			{
				return PackResponseResult(command, 0, kBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 3)
			{
				return PackResponseResult(command, 0, fBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 4)
			{
				return PackResponseResult(command, 0, aBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 5)
			{
				return PackResponseResult(command, 0, dBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 8)
			{
				return PackResponseResult(command, 0, sBuffer.GetBytes(num, length));
			}
			if (command[3] == 9)
			{
				return PackResponseResult(command, 0, w9Buffer.GetBytes(num * 4, length));
			}
			if (command[3] == 14)
			{
				return PackResponseResult(command, 0, bdBuffer.GetBytes(num * 4, length));
			}
			if (command[3] == 20)
			{
				return PackResponseResult(command, 0, wlBuffer.GetBytes(num * 2, length));
			}
			if (command[3] == 21)
			{
				return PackResponseResult(command, 0, w21Buffer.GetBytes(num * 2, length));
			}
			return PackResponseResult(command, 36, null);
		}

		private byte[] WriteByMessage(byte[] command)
		{
			if (!base.EnableWrite)
			{
				return PackResponseResult(command, 34, null);
			}
			int num = command[5] + command[6] * 256;
			int num2 = command[7] + command[8] * 256;
			byte[] data = command.RemoveBegin(9);
			if (command[3] == 0)
			{
				bBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 1)
			{
				mBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 2)
			{
				kBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 3)
			{
				fBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 4)
			{
				aBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 5)
			{
				dBuffer.SetBytes(data, num * 2);
			}
			else if (command[3] == 8)
			{
				sBuffer.SetBytes(data, num);
			}
			else if (command[3] == 9)
			{
				w9Buffer.SetBytes(data, num * 4);
			}
			else if (command[3] == 14)
			{
				bdBuffer.SetBytes(data, num * 4);
			}
			else if (command[3] == 20)
			{
				wlBuffer.SetBytes(data, num * 2);
			}
			else
			{
				if (command[3] != 21)
				{
					return PackResponseResult(command, 36, null);
				}
				w21Buffer.SetBytes(data, num * 2);
			}
			return PackResponseResult(command, 0, null);
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 720896)
			{
				throw new Exception("File is not correct");
			}
			bBuffer.SetBytes(content, 0, 0, 65536);
			mBuffer.SetBytes(content, 65536, 0, 65536);
			kBuffer.SetBytes(content, 131072, 0, 65536);
			fBuffer.SetBytes(content, 196608, 0, 65536);
			aBuffer.SetBytes(content, 262144, 0, 65536);
			dBuffer.SetBytes(content, 327680, 0, 65536);
			sBuffer.SetBytes(content, 393216, 0, 65536);
			w9Buffer.SetBytes(content, 458752, 0, 65536);
			bdBuffer.SetBytes(content, 524288, 0, 65536);
			wlBuffer.SetBytes(content, 589824, 0, 65536);
			w21Buffer.SetBytes(content, 655360, 0, 65536);
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[720896];
			Array.Copy(bBuffer.GetBytes(), 0, array, 0, 65536);
			Array.Copy(mBuffer.GetBytes(), 0, array, 65536, 65536);
			Array.Copy(kBuffer.GetBytes(), 0, array, 131072, 65536);
			Array.Copy(fBuffer.GetBytes(), 0, array, 196608, 65536);
			Array.Copy(aBuffer.GetBytes(), 0, array, 262144, 65536);
			Array.Copy(dBuffer.GetBytes(), 0, array, 327680, 65536);
			Array.Copy(sBuffer.GetBytes(), 0, array, 393216, 65536);
			Array.Copy(w9Buffer.GetBytes(), 0, array, 458752, 65536);
			Array.Copy(bdBuffer.GetBytes(), 0, array, 524288, 65536);
			Array.Copy(wlBuffer.GetBytes(), 0, array, 589824, 65536);
			Array.Copy(w21Buffer.GetBytes(), 0, array, 655360, 65536);
			return array;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				bBuffer?.Dispose();
				mBuffer?.Dispose();
				kBuffer?.Dispose();
				fBuffer?.Dispose();
				aBuffer?.Dispose();
				dBuffer?.Dispose();
				sBuffer?.Dispose();
				w9Buffer?.Dispose();
				bdBuffer?.Dispose();
				wlBuffer?.Dispose();
				w21Buffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FujiCommandSettingTypeServer[{base.Port}]";
		}
	}
}
