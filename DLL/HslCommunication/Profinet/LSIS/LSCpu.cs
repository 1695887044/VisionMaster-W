using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Profinet.LSIS.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.LSIS
{
	/// <summary>
	/// XGB CPU I/F module supports Serial Port. The address can carry station number information, for example: s=2;D100
	/// </summary>
	/// <remarks>
	/// XGB 主机的通道 0 仅支持 1:1 通信。 对于具有主从格式的 1:N 系统，在连接 XGL-C41A 模块的通道 1 或 XGB 主机中使用 RS-485 通信。 XGL-C41A 模块支持 RS-422/485 协议。
	/// </remarks>
	public class LSCpu : DeviceSerialPort
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.LSIS.LSCnet.Station" />
		public byte Station { get; set; } = 5;


		/// <summary>
		/// Instantiate a Default object
		/// </summary>
		public LSCpu()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 2;
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return LSCpuHelper.UnpackResponseContent(send, response);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnet.ReadByte(System.String)" />
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnet.Write(System.String,System.Byte)" />
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return LSCpuHelper.ReadBool(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.ReadCoil(System.String)" />
		public OperateResult<bool> ReadCoil(string address)
		{
			return ReadBool(address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.ReadCoil(System.String,System.UInt16)" />
		public OperateResult<bool[]> ReadCoil(string address, ushort length)
		{
			return ReadBool(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSFastEnet.WriteCoil(System.String,System.Boolean)" />
		public OperateResult WriteCoil(string address, bool value)
		{
			return Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return LSCpuHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await LSCpuHelper.ReadBoolAsync(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.ReadCoil(System.String)" />
		public async Task<OperateResult<bool>> ReadCoilAsync(string address)
		{
			return await ReadBoolAsync(address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.ReadCoil(System.String,System.UInt16)" />
		public async Task<OperateResult<bool[]>> ReadCoilAsync(string address, ushort length)
		{
			return await ReadBoolAsync(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.WriteCoil(System.String,System.Boolean)" />
		public async Task<OperateResult> WriteCoilAsync(string address, bool value)
		{
			return await WriteAsync(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.Write(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await LSCpuHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return LSCpuHelper.Read(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCpuHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return LSCpuHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await LSCpuHelper.ReadAsync(this, Station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCpu.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await LSCpuHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"LSCpu[{base.PortName}:{base.BaudRate}]";
		}
	}
}
