using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.CJT.Helper;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.CJT
{
	/// <summary>
	/// 城市建设部的188协议，基于DJ/T188-2004实现的协议
	/// </summary>
	public class CJT188 : DeviceSerialPort, ICjt188, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <summary>
		/// 获取或设置仪表的类型，通常是 0x10:冷水水表  0x11:生活热水水表  0x12:直饮水水表  0x13:中水水表  0x20:热量表(热量)  0x21:热量表(冷量)  0x30:燃气表  0x40:电度表
		/// </summary>
		public byte InstrumentType { get; set; }

		/// <summary>
		/// 获取或设置当前的地址域信息，是一个14个字符的BCD码，例如：14910000729011<br />
		/// Get or set the current address domain information, which is a 14-character BCD code, for example: 14910000729011
		/// </summary>
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


		/// <summary>
		/// 指定地址域来实例化一个对象，地址域是一个14个字符的BCD码，例如：14910000729011<br />
		/// Specify the address field, to instantiate an object, which address field is a 14-character BCD code, for example: 14910000729011
		/// </summary>
		/// <param name="station">设备的地址信息，是一个14字符的BCD码</param>
		public CJT188(string station)
		{
			base.ByteTransform = new RegularByteTransform();
			this.station = station;
			base.ReceiveEmptyDataCount = 5;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new CJT188Message(StationMatch);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(byte[] send)
		{
			OperateResult<byte[]> operateResult = base.ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			int num = DLT645Helper.FindHeadCode68H(operateResult.Content);
			if (num > 0)
			{
				return OperateResult.CreateSuccessResult(operateResult.Content.RemoveBegin(num));
			}
			return operateResult;
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

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.ICjt188.ActiveDeveice" />
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

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.CJT188.ReadFloat(System.String,System.UInt16)" />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadFloat(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.CJT188.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadDouble(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return await Task.Run(() => ReadString(address, length, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.ReadAddress(HslCommunication.Instrument.CJT.Helper.ICjt188)" />
		public OperateResult<string> ReadAddress()
		{
			return CJT188Helper.ReadAddress(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.WriteAddress(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return CJT188Helper.WriteAddress(this, address);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"CJT188[{base.PortName}:{base.BaudRate}]";
		}
	}
}
