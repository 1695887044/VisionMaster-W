using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.CJT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.CJT
{
	/// <summary>
	/// CJT188串口透传协议
	/// </summary>
	public class CJT188OverTcp : DeviceTcpNet, ICjt188, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.CJT.CJT188.InstrumentType" />
		public byte InstrumentType { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Instrument.CJT.CJT188.Station" />
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

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.EnableCodeFE" />
		public bool EnableCodeFE { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Core.IMessage.CJT188Message.StationMatch" />
		public bool StationMatch { get; set; } = false;


		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.CJT188.#ctor(System.String)" />
		public CJT188OverTcp(string station)
		{
			base.ByteTransform = new RegularByteTransform();
			this.station = station;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new CJT188Message(StationMatch);
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			if (EnableCodeFE)
			{
				return SoftBasic.SpliceArray<byte>(new byte[2] { 254, 254 }, command);
			}
			return base.PackCommandWithHeader(command);
		}

		/// <summary>
		/// 激活设备的命令，只发送数据到设备，不等待设备数据返回<br />
		/// The command to activate the device, only send data to the device, do not wait for the device data to return
		/// </summary>
		/// <returns>是否发送成功</returns>
		public OperateResult ActiveDeveice()
		{
			return ReadFromCoreServer(new byte[2] { 254, 254 }, hasResponseData: false, usePackAndUnpack: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.Read(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String,System.Int32)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return CJT188Helper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.Write(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return CJT188Helper.Write(this, address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return CJT188Helper.ReadValue(this, address, length, float.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return CJT188Helper.ReadValue(this, address, length, double.Parse);
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.ReadStringArray(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String)" />
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return CJT188Helper.ReadStringArray(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.CJT188OverTcp.ReadFloat(System.String,System.UInt16)" />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadFloat(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.CJT188OverTcp.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadDouble(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return await Task.Run(() => ReadString(address, length, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.WriteAddress(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return CJT188Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.ReadAddress(HslCommunication.Instrument.CJT.Helper.ICjt188)" />
		public OperateResult<string> ReadAddress()
		{
			return CJT188Helper.ReadAddress(this);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"CJT188OverTcp[{IpAddress}:{Port}]";
		}
	}
}
