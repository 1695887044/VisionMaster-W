using HslCommunication.Core.IMessage;

namespace HslCommunication.ModBus
{
	/// <summary>
	/// Modbus-Ascii通讯协议的网口透传类，基于rtu类库完善过来，支持标准的功能码，也支持扩展的功能码实现，地址采用富文本的形式，详细见备注说明<br />
	/// The client communication class of Modbus-Ascii protocol is convenient for data interaction with the server. It supports standard function codes and also supports extended function codes. 
	/// The address is in rich text. For details, see the remarks.
	/// </summary>
	/// <remarks>
	/// 本客户端支持的标准的modbus-ascii协议，地址支持富文本格式，具体参考示例代码。<br />
	/// 读取线圈，输入线圈，寄存器，输入寄存器的方法中的读取长度对商业授权用户不限制，内部自动切割读取，结果合并。
	/// </remarks>
	/// <example>
	/// 基本的用法请参照下面的代码示例，初始化部分的代码省略
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Modbus\ModbusAsciiExample.cs" region="Example" title="Modbus示例" />
	/// 复杂的读取数据的代码示例如下：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Modbus\ModbusAsciiExample.cs" region="ReadExample" title="read示例" />
	/// 写入数据的代码如下：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Modbus\ModbusAsciiExample.cs" region="WriteExample" title="write示例" />
	/// </example>
	public class ModbusAsciiOverTcp : ModbusRtuOverTcp
	{
		/// <summary>
		/// 实例化一个Modbus-ascii协议的客户端对象<br />
		/// Instantiate a client object of the Modbus-ascii protocol
		/// </summary>
		public ModbusAsciiOverTcp()
		{
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.#ctor(System.String,System.Int32,System.Byte)" />
		public ModbusAsciiOverTcp(string ipAddress, int port = 502, byte station = 1)
			: base(ipAddress, port, station)
		{
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(13, 10);
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			return ModbusInfo.TransModbusCoreToAsciiPackCommand(command);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return ModbusHelper.ExtraAsciiResponseContent(send, response, base.BroadcastStation);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ModbusAsciiOverTcp[{IpAddress}:{Port}]";
		}
	}
}
