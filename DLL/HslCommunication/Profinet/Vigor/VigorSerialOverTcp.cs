using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Profinet.Vigor.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Vigor
{
	/// <summary>
	/// 丰炜通信协议的网口透传版本，支持VS系列，地址支持携带站号，例如 s=2;D100, 字地址支持 D,SD,R,T,C(C200-C255是32位寄存器), 位地址支持X,Y,M,SM,S,TS(定时器触点),TC（定时器线圈）,CS(计数器触点),CC（计数器线圈)<br />
	/// The network port transparent transmission version of Fengwei communication protocol supports VS series, and the address supports carrying station number, 
	/// such as s=2;D100, word address supports D, SD, R, T, C (C200-C255 are 32-bit registers), Bit address supports X, Y, M, SM, S, TS (timer contact), 
	/// TC (timer coil), CS (counter contact), CC (counter coil)
	/// </summary>
	/// <remarks>
	/// 暂时不支持对字寄存器(D,R)进行读写位操作，感谢随时关注库的更新日志
	/// </remarks>
	public class VigorSerialOverTcp : DeviceTcpNet
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Vigor.VigorSerial.Station" />
		public byte Station { get; set; }

		/// <summary>
		/// 实例化默认的构造方法<br />
		/// Instantiate the default constructor
		/// </summary>
		public VigorSerialOverTcp()
		{
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform();
		}

		/// <summary>
		/// 使用指定的ip地址和端口来实例化一个对象<br />
		/// Instantiate an object with the specified IP address and port
		/// </summary>
		/// <param name="ipAddress">设备的Ip地址</param>
		/// <param name="port">设备的端口号</param>
		public VigorSerialOverTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new VigorSerialMessage();
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

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await VigorHelper.ReadAsync(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await VigorHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await VigorHelper.ReadBoolAsync(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Boolean[])" />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await VigorHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"VigorSerialOverTcp[{IpAddress}:{Port}]";
		}
	}
}
