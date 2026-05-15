using System;
using System.Linq;
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
	/// 基于多功能电能表通信协议实现的通讯类，参考的文档是DLT645-2007，主要实现了对电表数据的读取和一些功能方法，
	/// 在点对点模式下，需要在连接后调用 <see cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.ReadAddress" /> 方法，数据标识格式为 00-00-00-00，具体参照文档手册。<br />
	/// The communication type based on the communication protocol of the multifunctional electric energy meter. 
	/// The reference document is DLT645-2007, which mainly realizes the reading of the electric meter data and some functional methods. 
	/// In the point-to-point mode, you need to call <see cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.ReadAddress" /> method after connect the device.
	/// the data identification format is 00-00-00-00, refer to the documentation manual for details.
	/// </summary>
	/// <remarks>
	/// 如果一对多的模式，地址可以携带地址域访问，例如 "s=2;00-00-00-00"，主要使用 <see cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.ReadDouble(System.String,System.UInt16)" /> 方法来读取浮点数，
	/// <see cref="M:HslCommunication.Core.Device.DeviceCommunication.ReadString(System.String,System.UInt16)" /> 方法来读取字符串
	/// </remarks>
	/// <example>
	/// <inheritdoc cref="T:HslCommunication.Instrument.DLT.DLT645" path="example" />
	/// </example>
	public class DLT645OverTcp : DeviceTcpNet, IDlt645, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		private string password = "00000000";

		private string opCode = "00000000";

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
		public DLT645Type DLTType { get; } = DLT645Type.DLT2007;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.Password" />
		public string Password
		{
			get
			{
				return password;
			}
			set
			{
				password = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.OpCode" />
		public string OpCode
		{
			get
			{
				return opCode;
			}
			set
			{
				opCode = value;
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.#ctor" />
		public DLT645OverTcp()
		{
			base.ByteTransform = new RegularByteTransform();
			password = "00000000";
			opCode = "00000000";
			base.WordLength = 1;
		}

		/// <summary>
		/// 指定IP地址，端口，地址域，密码，操作者代码来实例化一个对象<br />
		/// Specify the IP address, port, address field, password, and operator code to instantiate an object
		/// </summary>
		/// <param name="ipAddress">TcpServer的IP地址</param>
		/// <param name="port">TcpServer的端口</param>
		/// <param name="station">设备的站号信息</param>
		/// <param name="password">密码，写入的时候进行验证的信息</param>
		/// <param name="opCode">操作者代码</param>
		public DLT645OverTcp(string ipAddress, int port = 502, string station = "1", string password = "", string opCode = "")
		{
			IpAddress = ipAddress;
			Port = port;
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform();
			this.station = station;
			this.password = (string.IsNullOrEmpty(password) ? "00000000" : password);
			this.opCode = (string.IsNullOrEmpty(opCode) ? "00000000" : opCode);
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

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ActiveDeveice" />
		public OperateResult ActiveDeveice()
		{
			return ReadFromCoreServer(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return DLT645Helper.Read(this, address, length);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return DLT645Helper.ReadDouble(this, address, length);
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ReadStringArray(System.String)" />
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return DLT645Helper.ReadStringArray(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(DateTime validTime)
		{
			return Trip(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, password, opCode, station, 26, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(DateTime validTime)
		{
			return SwitchingOn(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, password, opCode, station, 27, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ActiveDeveice" />
		public async Task<OperateResult> ActiveDeveiceAsync()
		{
			return await ReadFromCoreServerAsync(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await DLT645Helper.ReadAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645OverTcp.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await DLT645Helper.ReadDoubleAsync(this, address, length);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadStringArrayAsync(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ReadStringArray(System.String)" />
		public async Task<OperateResult<string[]>> ReadStringArrayAsync(string address)
		{
			return await DLT645Helper.ReadStringArrayAsync(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Write(System.String,System.Byte[])" />
		public override OperateResult Write(string address, byte[] value)
		{
			return DLT645Helper.Write(this, password, opCode, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, short[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((short m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, ushort[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((ushort m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, int[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((int m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, uint[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((uint m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, float[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((float m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override OperateResult Write(string address, double[] values)
		{
			return DLT645Helper.Write(this, password, opCode, address, values.Select((double m) => m.ToString()).ToArray());
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, string value, Encoding encoding)
		{
			return DLT645Helper.Write(this, password, opCode, address, new string[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ReadAddress" />
		public OperateResult<string> ReadAddress()
		{
			return DLT645Helper.ReadAddress(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.WriteAddress(System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return DLT645Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.BroadcastTime(System.DateTime)" />
		public OperateResult BroadcastTime(DateTime dateTime)
		{
			return DLT645Helper.BroadcastTime(this, dateTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.FreezeCommand(System.String)" />
		public OperateResult FreezeCommand(string dataArea)
		{
			return DLT645Helper.FreezeCommand(this, dataArea);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ChangeBaudRate(System.String)" />
		public OperateResult ChangeBaudRate(string baudRate)
		{
			return DLT645Helper.ChangeBaudRate(this, baudRate);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, short[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((short m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, ushort[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((ushort m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, int[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((int m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, uint[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((uint m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, float[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((float m) => m.ToString()).ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public override async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, values.Select((double m) => m.ToString()).ToArray());
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, string value, Encoding encoding)
		{
			return await DLT645Helper.WriteAsync(this, password, opCode, address, new string[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt645)" />
		public async Task<OperateResult<string>> ReadAddressAsync()
		{
			return await DLT645Helper.ReadAddressAsync(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public async Task<OperateResult> WriteAddressAsync(string address)
		{
			return await DLT645Helper.WriteAddressAsync(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.BroadcastTime(HslCommunication.Instrument.DLT.Helper.IDlt645,System.DateTime)" />
		public async Task<OperateResult> BroadcastTimeAsync(DateTime dateTime)
		{
			return await DLT645Helper.BroadcastTimeAsync(this, dateTime, ReadFromCoreServerAsync);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.FreezeCommand(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public async Task<OperateResult> FreezeCommandAsync(string dataArea)
		{
			return await DLT645Helper.FreezeCommandAsync(this, dataArea);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ChangeBaudRate(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public async Task<OperateResult> ChangeBaudRateAsync(string baudRate)
		{
			return await DLT645Helper.ChangeBaudRateAsync(this, baudRate);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT645OverTcp[{IpAddress}:{Port}]";
		}
	}
}
