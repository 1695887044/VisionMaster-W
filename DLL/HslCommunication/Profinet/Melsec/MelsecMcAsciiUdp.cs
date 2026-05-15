using HslCommunication.Core;
using HslCommunication.Core.Pipe;
using HslCommunication.Profinet.Melsec.Helper;

namespace HslCommunication.Profinet.Melsec
{
	/// <summary>
	/// 三菱PLC通讯类，采用UDP的协议实现，采用Qna兼容3E帧协议实现，需要在PLC侧先的以太网模块先进行配置，必须为ascii通讯<br />
	/// Mitsubishi PLC communication class is implemented using UDP protocol and Qna compatible 3E frame protocol. 
	/// The Ethernet module needs to be configured first on the PLC side, and it must be ascii communication.
	/// </summary>
	/// <remarks>
	/// <inheritdoc cref="T:HslCommunication.Profinet.Melsec.MelsecMcNet" path="remarks" />
	/// </remarks>
	/// <example>
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\MelsecAscii.cs" region="Usage" title="简单的短连接使用" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\MelsecAscii.cs" region="Usage2" title="简单的长连接使用" />
	/// </example>
	public class MelsecMcAsciiUdp : MelsecMcAsciiNet, IReadWriteMc, IReadWriteDevice, IReadWriteNet
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.Melsec.MelsecMcNet.#ctor" />
		public MelsecMcAsciiUdp()
			: this("127.0.0.1", 6000)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Melsec.MelsecMcNet.#ctor(System.String,System.Int32)" />
		public MelsecMcAsciiUdp(string ipAddress, int port)
		{
			base.WordLength = 1;
			LogMsgFormatBinary = false;
			base.ByteTransform = new RegularByteTransform();
			CommunicationPipe = new PipeUdpNet();
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MelsecMcAsciiUdp[{IpAddress}:{Port}]";
		}
	}
}
