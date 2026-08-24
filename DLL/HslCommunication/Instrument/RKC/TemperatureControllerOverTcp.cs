using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.RKC.Helper;

namespace HslCommunication.Instrument.RKC
{
	/// <summary>
	/// RKC的CD/CH系列数字式温度控制器的网口透传类对象，可以读取测量值，CT1输入值，CT2输入值等等，地址的地址需要参考API文档的示例<br />
	/// RKC's CD/CH series digital temperature controller's network port transparently transmits objects, which can read measured values, CT1 input values, 
	/// CT2 input values, etc. The address of the address needs to refer to the example of the API document
	/// </summary>
	/// <remarks>
	/// 只能使用ReadDouble(string),Write(string,double)方法来读写数据<br />
	/// 地址支持站号信息，例如 s=2;M1 其他的地址直接参考demo程序演示
	/// </remarks>
	public class TemperatureControllerOverTcp : DeviceTcpNet
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
		public TemperatureControllerOverTcp()
		{
			base.WordLength = 1;
			base.ByteTransform = new RegularByteTransform();
			base.SleepTime = 20;
		}

		/// <summary>
		/// 使用指定的ip地址和端口来实例化一个对象<br />
		/// Instantiate an object with the specified IP address and port
		/// </summary>
		/// <param name="ipAddress">设备的Ip地址</param>
		/// <param name="port">设备的端口号</param>
		public TemperatureControllerOverTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
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
			OperateResult<double> read = await TemperatureControllerHelper.ReadDoubleAsync(this, station, address);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double[]>(read);
			}
			return OperateResult.CreateSuccessResult(new double[1] { read.Content });
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Double)" />
		public override async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			if (values == null || values.Length == 0)
			{
				return OperateResult.CreateSuccessResult();
			}
			return await TemperatureControllerHelper.WriteAsync(this, station, address, values[0]);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"RkcTemperatureControllerOverTcp[{IpAddress}:{Port}]";
		}
	}
}
