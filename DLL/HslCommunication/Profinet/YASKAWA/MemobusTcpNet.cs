using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Profinet.YASKAWA.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.YASKAWA
{
	/// <summary>
	/// 扩展的Memobus协议信息，除了支持普通的线圈，输入继电器，保持寄存器，输入寄存器的读写操作，还支持扩展的保持寄存器和输入寄存器读写操作。<br />
	/// The extended Memobus protocol information not only supports reading and writing operations of ordinary coils, input relays, 
	/// holding registers, and input registers, but also supports reading and writing operations of extended holding registers and input registers.
	/// </summary>
	/// <remarks>
	/// 其中线圈和输入继电器使用<see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.ReadBool(System.String,System.UInt16)" />和<see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean)" />,<see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean[])" />的方法，读取输入继电器地址：x=2;100。
	/// 其他的方法针对的是寄存器，保持型寄存器地址：100或 x=3;100，输入寄存器：x=4;100，扩展保持型寄存器x=9;100，写入x=11;100, 扩展输入寄存器：x=10;100<br />
	/// The coil and input relay use <see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.ReadBool(System.String,System.UInt16)" /> and <see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean)" />,<see cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean[])" /> method, 
	/// read the input relay address: x=2;100. Other methods are for registers, holding register address: 100 or x=3;100, input register: x=4;100, 
	/// extended holding register x=9;100, writing x=11;100, extended input Register: x=10;100
	/// <br /><br />
	/// 读取的最大的字为 2044 个字，写入的最大的字数为 2043 个字
	/// </remarks>
	public class MemobusTcpNet : DeviceTcpNet, IMemobus, IReadWriteDevice, IReadWriteNet
	{
		private byte cpuTo = 2;

		private byte cpuFrom = 1;

		private readonly SoftIncrementCount softIncrementCount;

		/// <inheritdoc cref="P:HslCommunication.Profinet.YASKAWA.Helper.IMemobus.CpuTo" />
		public byte CpuTo
		{
			get
			{
				return cpuTo;
			}
			set
			{
				cpuTo = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Profinet.YASKAWA.Helper.IMemobus.CpuFrom" />
		public byte CpuFrom
		{
			get
			{
				return cpuFrom;
			}
			set
			{
				cpuFrom = value;
			}
		}

		/// <summary>
		/// 实例化一个Memobus-Tcp协议的客户端对象<br />
		/// Instantiate a client object of the Memobus-Tcp protocol
		/// </summary>
		public MemobusTcpNet()
		{
			softIncrementCount = new SoftIncrementCount(255L, 0L);
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
		}

		/// <summary>
		/// 指定服务器地址，端口号，客户端自己的站号来初始化<br />
		/// Specify the server address, port number, and client's own station number to initialize
		/// </summary>
		/// <param name="ipAddress">服务器的Ip地址</param>
		/// <param name="port">服务器的端口号</param>
		public MemobusTcpNet(string ipAddress, int port = 502)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new MemobusMessage();
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			return MemobusHelper.PackCommandWithHeader(command, softIncrementCount.GetCurrentValue());
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return MemobusHelper.UnpackResponseContent(send, response);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.ReadBool(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return MemobusHelper.ReadBool(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Write(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return MemobusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Write(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			return MemobusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Read(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return MemobusHelper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Write(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return MemobusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Write(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.Int16,System.Func{System.String,System.Int16,HslCommunication.OperateResult})" />
		[HslMqttApi("WriteInt16", "")]
		public override OperateResult Write(string address, short value)
		{
			if (ushort.TryParse(address, out var _))
			{
				return MemobusHelper.Write(this, address, value, base.Write);
			}
			return base.Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.Write(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.String,System.UInt16,System.Func{System.String,System.UInt16,HslCommunication.OperateResult})" />
		[HslMqttApi("WriteUInt16", "")]
		public override OperateResult Write(string address, ushort value)
		{
			if (ushort.TryParse(address, out var _))
			{
				return MemobusHelper.Write(this, address, value, base.Write);
			}
			return base.Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await MemobusHelper.ReadBoolAsync(this, address, length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await MemobusHelper.WriteAsync(this, address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Boolean[])" />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await MemobusHelper.WriteAsync(this, address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await MemobusHelper.ReadAsync(this, address, length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await MemobusHelper.WriteAsync(this, address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.Int16)" />
		public override async Task<OperateResult> WriteAsync(string address, short value)
		{
			if (ushort.TryParse(address, out var _))
			{
				return await MemobusHelper.WriteAsync(this, address, value, (string address, short value) => base.WriteAsync(address, value)).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.WriteAsync(address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.MemobusTcpNet.Write(System.String,System.UInt16)" />
		public override async Task<OperateResult> WriteAsync(string address, ushort value)
		{
			if (ushort.TryParse(address, out var _))
			{
				return await MemobusHelper.WriteAsync(this, address, value, (string address, ushort value) => base.WriteAsync(address, value)).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.WriteAsync(address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.ReadRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[])" />
		public OperateResult<byte[]> ReadRandom(string[] address)
		{
			return MemobusHelper.ReadRandom(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.ReadRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[])" />
		public OperateResult<byte[]> ReadRandom(ushort[] address)
		{
			return MemobusHelper.ReadRandom(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.WriteRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[],System.Byte[])" />
		public OperateResult WriteRandom(ushort[] address, byte[] value)
		{
			return MemobusHelper.WriteRandom(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.ReadRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[])" />
		public async Task<OperateResult<byte[]>> ReadRandomAsync(string[] address)
		{
			return await MemobusHelper.ReadRandomAsync(this, address).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.ReadRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[])" />
		public async Task<OperateResult<byte[]>> ReadRandomAsync(ushort[] address)
		{
			return await MemobusHelper.ReadRandomAsync(this, address).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.YASKAWA.Helper.MemobusHelper.WriteRandom(HslCommunication.Profinet.YASKAWA.Helper.IMemobus,System.UInt16[],System.Byte[])" />
		public async Task<OperateResult> WriteRandomAsync(ushort[] address, byte[] value)
		{
			return await MemobusHelper.WriteRandomAsync(this, address, value).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MemobusTcpNet[{IpAddress}:{Port}]";
		}
	}
}
