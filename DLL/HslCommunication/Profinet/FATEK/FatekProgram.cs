using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Profinet.FATEK.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.FATEK
{
	/// <summary>
	/// 台湾永宏公司的编程口协议，具体的地址信息请查阅api文档信息，地址允许携带站号信息，例如：s=2;D100<br />
	/// The programming port protocol of Taiwan Yonghong company, 
	/// please refer to the api document for specific address information, The address can carry station number information, such as s=2;D100
	/// </summary>
	/// <remarks>
	/// <inheritdoc cref="T:HslCommunication.Profinet.FATEK.FatekProgramOverTcp" path="remarks" />
	/// </remarks>
	public class FatekProgram : DeviceSerialPort, IFatekProgram, IReadWriteNet
	{
		private byte station = 1;

		/// <inheritdoc cref="P:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.Station" />
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

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgramOverTcp.#ctor" />
		public FatekProgram()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
			LogMsgFormatBinary = false;
			base.ReceiveEmptyDataCount = 5;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new FatekProgramMessage();
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

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Run(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		public OperateResult Run(byte station)
		{
			return FatekProgramHelper.Run(this, station);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgram.Run(System.Byte)" />
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

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgram.Stop(System.Byte)" />
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

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.FatekProgram.ReadStatus(System.Byte)" />
		[HslMqttApi("ReadStatus", "读取PLC基本的状态信息")]
		public OperateResult<bool[]> ReadStatus()
		{
			return ReadStatus(Station);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FatekProgram[{base.PortName}:{base.BaudRate}]";
		}
	}
}
