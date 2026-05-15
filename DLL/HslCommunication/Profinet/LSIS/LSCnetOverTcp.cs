using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.LSIS.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.LSIS
{
	/// <summary>
	/// XGB Cnet I/F module supports Serial Port. On Tcp/ip implementation, The address can carry station number information, for example: s=2;D100
	/// </summary>
	/// <remarks>
	/// XGB 主机的通道 0 仅支持 1:1 通信。 对于具有主从格式的 1:N 系统，在连接 XGL-C41A 模块的通道 1 或 XGB 主机中使用 RS-485 通信。 XGL-C41A 模块支持 RS-422/485 协议。
	/// </remarks>
	public class LSCnetOverTcp : DeviceTcpNet, IReadWriteDeviceStation, IReadWriteDevice, IReadWriteNet
	{
		/// <inheritdoc cref="P:HslCommunication.Core.Net.IReadWriteDeviceStation.Station" />
		public byte Station { get; set; } = 5;


		/// <summary>
		/// Instantiate a Default object
		/// </summary>
		public LSCnetOverTcp()
		{
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform();
			base.SleepTime = 20;
		}

		/// <summary>
		/// Instantiate a Default object
		/// </summary>
		/// <param name="ipAddress">Ip Address</param>
		/// <param name="port">Ip port</param>
		public LSCnetOverTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return LSCnetHelper.UnpackResponseContent(send, response);
		}

		/// <summary>
		/// Read single byte value from plc
		/// </summary>
		/// <param name="address">Start address</param>
		/// <returns>result</returns>
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
		/// Read single byte value from plc
		/// </summary>
		/// <param name="address">Start address</param>
		/// <returns>read result</returns>
		public async Task<OperateResult<byte>> ReadByteAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadAsync(address, 2));
		}

		/// <summary>
		/// Write single byte value to plc
		/// </summary>
		/// <param name="address">Start address</param>
		/// <param name="value">value</param>
		/// <returns>Whether to write the successful</returns>
		public async Task<OperateResult> WriteAsync(string address, byte value)
		{
			return await WriteAsync(address, new byte[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String)" />
		[HslMqttApi("ReadBool", "")]
		public override OperateResult<bool> ReadBool(string address)
		{
			return LSCnetHelper.ReadBool(this, Station, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.ReadBool(HslCommunication.Core.Net.IReadWriteDeviceStation,System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return LSCnetHelper.ReadBool(this, address, length);
		}

		/// <summary>
		/// ReadCoil, same as ReadBool
		/// </summary>
		/// <param name="address">address, for example: MX100, PX100</param>
		/// <returns>Result</returns>
		public OperateResult<bool> ReadCoil(string address)
		{
			return ReadBool(address);
		}

		/// <summary>
		/// ReadCoil, same as ReadBool
		/// </summary>
		/// <param name="address">address, for example: MX100, PX100</param>
		/// <param name="length">array length</param>
		/// <returns>result</returns>
		public OperateResult<bool[]> ReadCoil(string address, ushort length)
		{
			return ReadBool(address, length);
		}

		/// <summary>
		/// WriteCoil
		/// </summary>
		/// <param name="address">Start Address</param>
		/// <param name="value">value for write</param>
		/// <returns>whether write is success</returns>
		public OperateResult WriteCoil(string address, bool value)
		{
			return Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return LSCnetHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.ReadBool(System.String)" />
		public override async Task<OperateResult<bool>> ReadBoolAsync(string address)
		{
			return await LSCnetHelper.ReadBoolAsync(this, Station, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await LSCnetHelper.ReadBoolAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.ReadCoil(System.String)" />
		public async Task<OperateResult<bool>> ReadCoilAsync(string address)
		{
			return await ReadBoolAsync(address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.ReadCoil(System.String,System.UInt16)" />
		public async Task<OperateResult<bool[]>> ReadCoilAsync(string address, ushort length)
		{
			return await ReadBoolAsync(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.WriteCoil(System.String,System.Boolean)" />
		public async Task<OperateResult> WriteCoilAsync(string address, bool value)
		{
			return await WriteAsync(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.Write(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await LSCnetHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Read(HslCommunication.Core.Net.IReadWriteDeviceStation,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return LSCnetHelper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String[])" />
		public OperateResult<byte[]> Read(string[] address)
		{
			return LSCnetHelper.Read(this, Station, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.Helper.LSCnetHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Int32,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return LSCnetHelper.Write(this, Station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await LSCnetHelper.ReadAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.Read(System.String[])" />
		public async Task<OperateResult<byte[]>> ReadAsync(string[] address)
		{
			return await LSCnetHelper.ReadAsync(this, Station, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.LSIS.LSCnetOverTcp.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await LSCnetHelper.WriteAsync(this, Station, address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"LsCnetOverTcp[{IpAddress}:{Port}]";
		}
	}
}
