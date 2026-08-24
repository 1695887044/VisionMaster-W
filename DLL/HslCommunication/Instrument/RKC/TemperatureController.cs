using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.RKC.Helper;

namespace HslCommunication.Instrument.RKC
{
	/// <summary>
	/// RKC的CD/CH系列数字式温度控制器的串口类对象，可以读取测量值，CT1输入值，CT2输入值等等，地址的地址需要参考API文档的示例<br />
	/// The serial port object of RKC's CD/CH series digital temperature controller can read the measured value, CT1 input value, 
	/// CT2 input value, etc. The address of the address needs to refer to the example of the API document
	/// </summary>
	/// <remarks>
	/// 只能使用ReadDouble(string),Write(string,double)方法来读写数据，设备的串口默认参数为 8-1-N,8 个数据位，一个停止位，无奇偶校验<br />
	/// 地址支持站号信息，例如 s=2;M1
	/// </remarks>
	public class TemperatureController : DeviceSerialPort
	{
		private byte station = 1;

		/// <inheritdoc cref="P:HslCommunication.Instrument.RKC.TemperatureControllerOverTcp.Station" />
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

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.TemperatureControllerOverTcp.#ctor" />
		public TemperatureController()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new RkcTemperatureMessage();
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.ReadDouble(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String)" />
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			OperateResult<double> operateResult = TemperatureControllerHelper.ReadDouble(this, station, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult(new double[1] { operateResult.Content });
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Double)" />
		public override OperateResult Write(string address, double[] values)
		{
			if (values == null || values.Length == 0)
			{
				return OperateResult.CreateSuccessResult();
			}
			return TemperatureControllerHelper.Write(this, station, address, values[0]);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.ReadDouble(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadDouble(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Double)" />
		public override async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"RkcTemperatureController[{base.PortName}:{base.BaudRate}]";
		}
	}
}
