using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Profinet.FATEK.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.FATEK
{
	/// <summary>
	/// 台湾永宏公司的编程口协议，此处是基于tcp的实现，地址信息请查阅api文档信息，地址可以携带站号信息，例如 s=2;D100<br />
	/// The programming port protocol of Taiwan Yonghong company, here is the implementation based on TCP, 
	/// please refer to the API information for the address information, The address can carry station number information, such as s=2;D100
	/// </summary>
	/// <remarks>
	/// 支持位访问：M,X,Y,S,T(触点),C(触点)，字访问：RT(当前值),RC(当前值)，D，R；具体参照API文档
	/// </remarks>
	public class FatekProgramOverTcp : DeviceTcpNet, IFatekProgram, IReadWriteNet
	{
		private byte station = 1;

		/// <summary>
		/// PLC的站号信息，需要和实际的设置值一致，默认为1<br />
		/// The station number information of the PLC needs to be consistent with the actual setting value. The default is 1.
		/// </summary>
		public byte Station
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

		/// <summary>
		/// 实例化默认的构造方法<br />
		/// Instantiate the default constructor
		/// </summary>
		public FatekProgramOverTcp()
		{
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform();
			LogMsgFormatBinary = false;
		}

		/// <summary>
		/// 使用指定的ip地址和端口来实例化一个对象<br />
		/// Instantiate an object with the specified IP address and port
		/// </summary>
		/// <param name="ipAddress">设备的Ip地址</param>
		/// <param name="port">设备的端口号</param>
		public FatekProgramOverTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(3);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return FatekProgramHelper.Read(this, station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return FatekProgramHelper.Write(this, station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await FatekProgramHelper.ReadAsync(this, station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await FatekProgramHelper.WriteAsync(this, station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return FatekProgramHelper.ReadBool(this, station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			return FatekProgramHelper.Write(this, station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await FatekProgramHelper.ReadBoolAsync(this, station, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Write(System.String,System.Boolean[])" />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await FatekProgramHelper.WriteAsync(this, station, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Run(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public OperateResult Run(byte station)
		{
			return FatekProgramHelper.Run(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Run(System.Byte)" />
		[HslMqttApi("Run", "使PLC处于RUN状态")]
		public OperateResult Run()
		{
			return Run(Station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Stop(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public OperateResult Stop(byte station)
		{
			return FatekProgramHelper.Stop(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Stop(System.Byte)" />
		[HslMqttApi("Stop", "使PLC处于STOP状态")]
		public OperateResult Stop()
		{
			return Stop(Station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.ReadStatus(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public OperateResult<bool[]> ReadStatus(byte station)
		{
			return FatekProgramHelper.ReadStatus(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.ReadStatus(System.Byte)" />
		[HslMqttApi("ReadStatus", "读取PLC基本的状态信息")]
		public OperateResult<bool[]> ReadStatus()
		{
			return ReadStatus(Station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Run(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public async Task<OperateResult> RunAsync(byte station)
		{
			return await FatekProgramHelper.RunAsync(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.RunAsync(System.Byte)" />
		public async Task<OperateResult> RunAsync()
		{
			return await RunAsync(Station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Stop(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public async Task<OperateResult> StopAsync(byte station)
		{
			return await FatekProgramHelper.StopAsync(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.StopAsync(System.Byte)" />
		public async Task<OperateResult> StopAsync()
		{
			return await StopAsync(Station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.ReadStatus(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public async Task<OperateResult<bool[]>> ReadStatusAsync(byte station)
		{
			return await FatekProgramHelper.ReadStatusAsync(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.ReadStatus(System.Byte)" />
		public async Task<OperateResult<bool[]>> ReadStatusAsync()
		{
			return await ReadStatusAsync(Station);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FatekProgramOverTcp[{IpAddress}:{Port}]";
		}
	}
}
