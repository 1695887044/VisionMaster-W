using System;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Freedom
{
	/// <summary>
	/// 基于串口的自由协议，需要在地址里传入报文信息，也可以传入数据偏移信息，<see cref="P:HslCommunication.Core.Device.DeviceCommunication.ByteTransform" />默认为<see cref="T:HslCommunication.Core.RegularByteTransform" />
	/// </summary>
	/// <example>
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\FreedomExample.cs" region="Sample5" title="实例化" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\FreedomExample.cs" region="Sample6" title="读取" />
	/// </example>
	public class FreedomSerial : DeviceSerialPort
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Freedom.FreedomTcpNet.CheckResponseStatus" />
		public Func<byte[], byte[], OperateResult> CheckResponseStatus { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public FreedomSerial()
		{
			base.ByteTransform = new RegularByteTransform();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Freedom.FreedomTcpNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "特殊的地址格式，需要采用解析包起始地址的报文，例如 modbus 协议为 stx=9;00 00 00 00 00 06 01 03 00 64 00 01")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			int num = HslHelper.ExtractParameter(ref address, "stx", 0);
			byte[] array = address.ToHexBytes();
			OperateResult<byte[]> operateResult = ReadFromCoreServer(array);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (CheckResponseStatus != null)
			{
				OperateResult operateResult2 = CheckResponseStatus(array, operateResult.Content);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult2);
				}
			}
			if (num >= operateResult.Content.Length)
			{
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataLengthTooShort);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content.RemoveBegin(num));
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, byte[] value)
		{
			return Read(address, 0);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FreedomSerial<{base.ByteTransform.GetType()}>[{base.PortName}:{base.BaudRate}]";
		}
	}
}
