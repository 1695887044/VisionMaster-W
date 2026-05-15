using HslCommunication.Core.Pipe;

namespace HslCommunication.ModBus
{
	/// <summary>
	/// Modbus-Udp协议的客户端通讯类，方便的和服务器进行数据交互，支持标准的功能码，也支持扩展的功能码实现，地址采用富文本的形式，详细见备注说明<br />
	/// The client communication class of Modbus-Udp protocol is convenient for data interaction with the server. It supports standard function codes and also supports extended function codes. 
	/// The address is in rich text. For details, see the remarks.
	/// </summary>
	/// <remarks>
	/// 本客户端支持的标准的modbus协议，Modbus-Tcp及Modbus-Udp内置的消息号会进行自增，地址支持富文本格式，具体参考示例代码。<br />
	/// 读取线圈，输入线圈，寄存器，输入寄存器的方法中的读取长度对商业授权用户不限制，内部自动切割读取，结果合并。
	/// </remarks>
	/// <example>
	/// <inheritdoc cref="T:HslCommunication.ModBus.ModbusTcpNet" path="example" />
	/// </example>
	public class ModbusUdpNet : ModbusTcpNet
	{
		/// <summary>
		/// 实例化一个MOdbus-Udp协议的客户端对象<br />
		/// Instantiate a client object of the MOdbus-Udp protocol
		/// </summary>
		public ModbusUdpNet()
		{
			CommunicationPipe = new PipeUdpNet();
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.#ctor(System.String,System.Int32,System.Byte)" />
		public ModbusUdpNet(string ipAddress, int port = 502, byte station = 1)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
			base.Station = station;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ModbusUdpNet[{IpAddress}:{Port}]";
		}
	}
}
