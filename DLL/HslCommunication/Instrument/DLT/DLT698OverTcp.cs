using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// 698.45协议的串口转网口透传通信类(不是TCP通信)，面向对象的用电信息数据交换协议，使用明文的通信方式。支持读取功率，总功，电压，电流，频率，功率因数等数据。<br />
	/// 698.45 protocol serial port to network port transparent transmission communication (not TCP communication), 
	/// object-oriented power consumption information data exchange protocol, using plaintext communication. Support reading power, 
	/// total power, voltage, current, frequency, power factor and other data.
	/// </summary>
	/// <remarks>
	/// <inheritdoc cref="T:HslCommunication.Instrument.DLT.DLT698" path="remarks" />
	/// </remarks>
	/// <example>
	/// <inheritdoc cref="T:HslCommunication.Instrument.DLT.DLT698" path="example" />
	/// /// </example>
	public class DLT698OverTcp : DeviceTcpNet, IDlt698, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt698.UseSecurityResquest" />
		public bool UseSecurityResquest { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt698.CA" />
		public byte CA { get; set; } = 0;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.DLT698.Station" />
		public string Station
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

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.DLT645.EnableCodeFE" />
		public bool EnableCodeFE { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.#ctor" />
		public DLT698OverTcp()
		{
			base.ByteTransform = new ReverseBytesTransform();
		}

		/// <summary>
		/// 通过指定设备站号来初始化一个通信对象信息
		/// </summary>
		/// <param name="station">设备的地址信息，通常是一个12字符的BCD码</param>
		public DLT698OverTcp(string station)
			: this()
		{
			this.station = station;
		}

		/// <summary>
		/// 通过指定IP地址，端口号，设备站号来初始化一个通信对象信息
		/// </summary>
		/// <param name="ipAddress">IP地址信息</param>
		/// <param name="port">端口号信息</param>
		/// <param name="station">设备站号信息</param>
		public DLT698OverTcp(string ipAddress, int port, string station)
			: this(station)
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DLT698Message();
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			return DLT698Helper.PackCommandWithHeader(this, command);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.Read(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return DLT698Helper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.Byte[])" />
		public override OperateResult Write(string address, byte[] value)
		{
			return DLT698Helper.Write(this, address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return DLT698Helper.ReadBool(ReadStringArray(address), length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			OperateResult<byte[]> command = DLT698Helper.BuildReadSingleObject(address, station, this);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return read;
			}
			return DLT698Helper.CheckResponse(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return DLT698Helper.ReadBool(await ReadStringArrayAsync(address), length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			OperateResult<byte[]> build = DLT698Helper.BuildWriteSingleObject(address, station, value, this);
			if (!build.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(build);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(build.Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			return DLT698Helper.CheckResponse(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadByApdu(HslCommunication.Instrument.DLT.Helper.IDlt698,System.Byte[])" />
		public OperateResult<byte[]> ReadByApdu(byte[] apdu)
		{
			return DLT698Helper.ReadByApdu(this, apdu);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ActiveDeveice(HslCommunication.Instrument.DLT.Helper.IDlt698)" />
		public OperateResult ActiveDeveice()
		{
			return DLT698Helper.ActiveDeveice(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadStringArray(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String)" />
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return DLT698Helper.ReadStringArray(this, address);
		}

		private OperateResult<T[]> ReadDataAndParse<T>(string address, ushort length, Func<string, T> trans)
		{
			return DLT698Helper.ReadDataAndParse(ReadStringArray(address), length, trans);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt698)" />
		public OperateResult<string> ReadAddress()
		{
			return DLT698Helper.ReadAddress(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return DLT698Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.WriteDateTime(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.DateTime)" />
		public OperateResult WriteDateTime(string address, DateTime time)
		{
			return DLT698Helper.WriteDateTime(this, address, time);
		}

		private async Task<OperateResult<T[]>> ReadDataAndParseAsync<T>(string address, ushort length, Func<string, T> trans)
		{
			return DLT698Helper.ReadDataAndParse(await ReadStringArrayAsync(address), length, trans);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ReadByApdu(System.Byte[])" />
		public async Task<OperateResult<byte[]>> ReadByApduAsync(byte[] apdu)
		{
			return await DLT698Helper.ReadByApduAsync(this, apdu);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ActiveDeveice" />
		public async Task<OperateResult> ActiveDeveiceAsync()
		{
			return await ReadFromCoreServerAsync(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ReadStringArray(System.String)" />
		public async Task<OperateResult<string[]>> ReadStringArrayAsync(string address)
		{
			OperateResult<byte[]> read = await ReadAsync(address, 1).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(read);
			}
			int index = 8;
			return OperateResult.CreateSuccessResult(DLT698Helper.ExtraStringsValues(base.ByteTransform, read.Content, ref index));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ReadAddress" />
		public async Task<OperateResult<string>> ReadAddressAsync()
		{
			OperateResult<byte[]> build = DLT698Helper.BuildReadSingleObject("40-01-02-00", "AAAAAAAAAAAA", this);
			if (!build.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(build);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(build.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			OperateResult<byte[]> extra = DLT698Helper.CheckResponse(read.Content);
			if (!extra.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(extra);
			}
			station = extra.Content.SelectMiddle(10, extra.Content[9]).ToHexString();
			return OperateResult.CreateSuccessResult(station);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.WriteAddress(System.String)" />
		public async Task<OperateResult> WriteAddressAsync(string address)
		{
			OperateResult<byte[]> build = DLT698Helper.BuildWriteSingleObject("40-01-02-00", "AAAAAAAAAAAA", DLT698Helper.CreateStringValueBuffer(address), this);
			if (!build.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(build);
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(build.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			return DLT698Helper.CheckResponse(read.Content);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt16Array", "")]
		public override OperateResult<short[]> ReadInt16(string address, ushort length)
		{
			return ReadDataAndParse(address, length, short.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt16Array", "")]
		public override OperateResult<ushort[]> ReadUInt16(string address, ushort length)
		{
			return ReadDataAndParse(address, length, ushort.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt32Array", "")]
		public override OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			return ReadDataAndParse(address, length, int.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt32Array", "")]
		public override OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			return ReadDataAndParse(address, length, uint.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt64Array", "")]
		public override OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			return ReadDataAndParse(address, length, long.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt64Array", "")]
		public override OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			return ReadDataAndParse(address, length, ulong.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return ReadDataAndParse(address, length, float.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return ReadDataAndParse(address, length, double.Parse);
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<short[]>> ReadInt16Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, short.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ushort[]>> ReadUInt16Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, ushort.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, int.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, uint.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, long.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, ulong.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, float.Parse);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698OverTcp.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await ReadDataAndParseAsync(address, length, double.Parse);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadStringArrayAsync(address));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT698OverTcp[{IpAddress}:{Port}]";
		}
	}
}
