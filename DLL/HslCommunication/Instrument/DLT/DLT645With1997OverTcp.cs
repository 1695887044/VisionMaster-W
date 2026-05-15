using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// 基于多功能电能表通信协议实现的通讯类，参考的文档是DLT645-1997，主要实现了对电表数据的读取和一些功能方法，数据标识格式为 B6-11，具体参照文档手册。<br />
	/// Based on the communication class implemented by the multi-function energy meter communication protocol, the reference document is DLT645-1997, 
	/// which mainly implements the reading of meter data and some functional methods, the data identification format is B6-11, please refer to the document manual for details.
	/// </summary>
	/// <remarks>
	/// 如果一对多的模式，地址可以携带地址域访问，例如 "s=2;B6-11"，主要使用 <see cref="M:HslCommunication.Instrument.DLT.DLT645With1997OverTcp.ReadDouble(System.String,System.UInt16)" /> 方法来读取浮点数，<see cref="M:HslCommunication.Core.Device.DeviceCommunication.ReadString(System.String,System.UInt16)" /> 方法来读取字符串
	/// </remarks>
	/// <example>
	/// <inheritdoc cref="T:HslCommunication.Instrument.DLT.DLT645With1997" path="example" />
	/// </example>
	public class DLT645With1997OverTcp : DeviceTcpNet, IDlt645, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.DLT645.Station" />
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

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.DLTType" />
		public DLT645Type DLTType { get; } = DLT645Type.DLT1997;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.Password" />
		public string Password { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.OpCode" />
		public string OpCode { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.#ctor" />
		public DLT645With1997OverTcp()
		{
			base.ByteTransform = new RegularByteTransform();
		}

		/// <summary>
		/// 指定IP地址，端口，地址域来实例化一个对象<br />
		/// Specify the IP address, port, address field, password, and operator code to instantiate an object
		/// </summary>
		/// <param name="ipAddress">TcpServer的IP地址</param>
		/// <param name="port">TcpServer的端口</param>
		/// <param name="station">设备的站号信息</param>
		public DLT645With1997OverTcp(string ipAddress, int port = 502, string station = "1")
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
			this.station = station;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DLT645Message();
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			if (EnableCodeFE)
			{
				return SoftBasic.SpliceArray<byte>(new byte[4] { 254, 254, 254, 254 }, command);
			}
			return base.PackCommandWithHeader(command);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ActiveDeveice" />
		public OperateResult ActiveDeveice()
		{
			return ReadFromCoreServer(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return DLT645Helper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return DLT645Helper.ReadDouble(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadString(System.String,System.UInt16,System.Text.Encoding)" />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadStringArray(System.String)" />
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return DLT645Helper.ReadStringArray(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ActiveDeveice" />
		public async Task<OperateResult> ActiveDeveiceAsync()
		{
			return await ReadFromCoreServerAsync(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await DLT645Helper.ReadAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997OverTcp.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await DLT645Helper.ReadDoubleAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997OverTcp.ReadString(System.String,System.UInt16,System.Text.Encoding)" />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadStringArrayAsync(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadStringArray(System.String)" />
		public async Task<OperateResult<string[]>> ReadStringArrayAsync(string address)
		{
			return await DLT645Helper.ReadStringArrayAsync(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.Write(System.String,System.Byte[])" />
		public override OperateResult Write(string address, byte[] value)
		{
			return DLT645Helper.Write(this, "", "", address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.WriteAddress(System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return DLT645Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.BroadcastTime(System.DateTime)" />
		public OperateResult BroadcastTime(DateTime dateTime)
		{
			return DLT645Helper.BroadcastTime(this, dateTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ChangeBaudRate(System.String)" />
		public OperateResult ChangeBaudRate(string baudRate)
		{
			return DLT645Helper.ChangeBaudRate(this, baudRate);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ReadAddress" />
		public OperateResult<string> ReadAddress()
		{
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997OverTcp.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(DateTime validTime)
		{
			return Trip(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, "", "", station, 26, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997OverTcp.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(DateTime validTime)
		{
			return SwitchingOn(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, "", "", station, 27, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await DLT645Helper.WriteAsync(this, "", "", address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.WriteAddress(System.String)" />
		public async Task<OperateResult> WriteAddressAsync(string address)
		{
			return await DLT645Helper.WriteAddressAsync(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.BroadcastTime(System.DateTime)" />
		public async Task<OperateResult> BroadcastTimeAsync(DateTime dateTime)
		{
			return await DLT645Helper.BroadcastTimeAsync(this, dateTime, ReadFromCoreServerAsync);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ChangeBaudRate(System.String)" />
		public async Task<OperateResult> ChangeBaudRateAsync(string baudRate)
		{
			return await DLT645Helper.ChangeBaudRateAsync(this, baudRate);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT645With1997OverTcp[{IpAddress}:{Port}]";
		}
	}
}
