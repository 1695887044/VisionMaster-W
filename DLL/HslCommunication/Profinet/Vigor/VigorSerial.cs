using System.Text.RegularExpressions;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Profinet.Vigor.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Vigor
{
	/// <summary>
	/// 丰炜通信协议的串口通信，支持VS系列，地址支持携带站号，例如 s=2;D100, 字地址支持 D,SD,R,T,C(C200-C255是32位寄存器), 位地址支持X,Y,M,SM,S,TS(定时器触点),TC（定时器线圈）,CS(计数器触点),CC（计数器线圈)<br />
	/// The network port transparent transmission version of Fengwei communication protocol supports VS series, and the address supports carrying station number, 
	/// such as s=2;D100, word address supports D, SD, R, T, C (C200-C255 are 32-bit registers), Bit address supports X, Y, M, SM, S, TS (timer contact), 
	/// TC (timer coil), CS (counter contact), CC (counter coil)
	/// </summary>
	/// <remarks>
	/// 串口默认的参数为 19200-8-N-1方式，暂时不支持对字寄存器(D,R)进行读写位操作，感谢随时关注库的更新日志
	/// </remarks>
	public class VigorSerial : DeviceSerialPort
	{
		/// <summary>
		/// 获取或设置当前PLC的站号信息
		/// </summary>
		public byte Station { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public VigorSerial()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
			base.ReceiveEmptyDataCount = 5;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new VigorSerialMessage();
		}

		/// <inheritdoc />
		protected override ushort GetWordLength(string address, int length, int dataTypeLength)
		{
			if (Regex.IsMatch(address, "^C2[0-9][0-9]$"))
			{
				int num = length * dataTypeLength * 2 / 4;
				return (ushort)((num == 0) ? 1 : ((ushort)num));
			}
			return base.GetWordLength(address, length, dataTypeLength);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return VigorHelper.Read(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return VigorHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return VigorHelper.ReadBool(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			return VigorHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"VigorSerial[{base.PortName}:{base.BaudRate}]";
		}
	}
}
