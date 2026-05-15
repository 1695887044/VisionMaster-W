using System.IO.Ports;
using HslCommunication.Reflection;
using HslCommunication.Serial;

namespace HslCommunication.Instrument.Light
{
	/// <summary>
	/// 昱行智造科技（深圳）有限公司的光源控制器，可以控制灯的亮暗，控制灯的颜色，通道等信息。
	/// </summary>
	public class ShineInLightSourceController : SerialBase
	{
		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public ShineInLightSourceController()
		{
		}

		/// <summary>
		/// 初始化串口信息，波特率，8位数据位，1位停止位，偶校验<br />
		/// Initializes serial port information, baud rate, 8-bit data bit, 1-bit stop bit, even parity
		/// </summary>
		/// <param name="portName">端口号信息，例如"COM3"</param>
		/// <param name="baudRate">波特率</param>
		public override void SerialPortInni(string portName, int baudRate)
		{
			SerialPortInni(portName, baudRate, 8, StopBits.One, Parity.Even);
		}

		/// <summary>
		/// 初始化串口信息，57600波特率，8位数据位，1位停止位，偶校验<br />
		/// Initial serial port information, 57600 baud rate, 8 data bits, 1 stop bit, even parity
		/// </summary>
		/// <param name="portName">端口号信息，例如"COM3"</param>
		public override void SerialPortInni(string portName)
		{
			SerialPortInni(portName, 57600);
		}

		/// <summary>
		/// 读取光源控制器的参数信息，需要传入通道号信息，读取到详细的内容参照<see cref="T:HslCommunication.Instrument.Light.ShineInLightData" />的值
		/// </summary>
		/// <param name="channel">读取的通道信息</param>
		/// <returns>读取的参数值</returns>
		[HslMqttApi(ApiTopic = "Read", Description = "读取光源控制器的参数信息，需要传入通道号信息，返回 ShineInLightData 对象")]
		public OperateResult<ShineInLightData> Read(byte channel)
		{
			OperateResult<byte[]> operateResult = ReadFromCoreServer(BuildReadCommand(channel));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<ShineInLightData>();
			}
			OperateResult<byte[]> operateResult2 = ExtractActualData(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<ShineInLightData>();
			}
			return OperateResult.CreateSuccessResult(new ShineInLightData(operateResult2.Content));
		}

		/// <summary>
		/// 将光源控制器的数据写入到设备，返回是否写入成功
		/// </summary>
		/// <param name="data">光源数据</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi(ApiTopic = "Write", Description = "将光源控制器的数据写入到设备，返回是否写入成功")]
		public OperateResult Write(ShineInLightData data)
		{
			OperateResult<byte[]> operateResult = ReadFromCoreServer(BuildWriteCommand(data));
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<ShineInLightData>();
			}
			return ExtractActualData(operateResult.Content);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "ShineInLightSourceController[" + base.PortName + "]";
		}

		/// <summary>
		/// 将命令和数据打包成用于发送的报文
		/// </summary>
		/// <param name="cmd">命令</param>
		/// <param name="data">命令数据</param>
		/// <returns>可用于发送的报文</returns>
		public static byte[] PackCommand(byte cmd, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			byte[] array = new byte[data.Length + 8];
			array[0] = 47;
			array[1] = 42;
			array[2] = 240;
			array[3] = cmd;
			array[4] = (byte)(array.Length - 4);
			data.CopyTo(array, 5);
			array[array.Length - 2] = 42;
			array[array.Length - 1] = 47;
			int num = array[2];
			for (int i = 3; i < array.Length - 3; i++)
			{
				num ^= array[i];
			}
			array[array.Length - 3] = (byte)num;
			return array;
		}

		/// <summary>
		/// 构建写入数据的报文命令
		/// </summary>
		/// <param name="shineInLightData">准备写入的数据</param>
		/// <returns>报文命令</returns>
		public static byte[] BuildWriteCommand(ShineInLightData shineInLightData)
		{
			return PackCommand(1, shineInLightData.GetSourceData());
		}

		/// <summary>
		/// 构建读取数据的报文命令
		/// </summary>
		/// <param name="channel">通道信息</param>
		/// <returns>构建读取的命令</returns>
		public static byte[] BuildReadCommand(byte channel)
		{
			return PackCommand(2, new byte[1] { channel });
		}

		/// <summary>
		/// 把服务器反馈的数据解析成实际的命令
		/// </summary>
		/// <param name="response">反馈的数据</param>
		/// <returns>结果内容</returns>
		public static OperateResult<byte[]> ExtractActualData(byte[] response)
		{
			if (response.Length < 9)
			{
				return new OperateResult<byte[]>("Receive Data is too short; source:" + response.ToHexString(' '));
			}
			if (response[0] != 47 || response[1] != 42 || response[response.Length - 2] != 42 || response[response.Length - 1] != 47)
			{
				return new OperateResult<byte[]>("Receive Data not start with /* or end with */; source:" + response.ToHexString());
			}
			if (response[3] == 1)
			{
				return (response[5] == 170) ? OperateResult.CreateSuccessResult(new byte[0]) : new OperateResult<byte[]>(response[5], "set not success");
			}
			return OperateResult.CreateSuccessResult(response.SelectMiddle(5, response.Length - 8));
		}
	}
}
