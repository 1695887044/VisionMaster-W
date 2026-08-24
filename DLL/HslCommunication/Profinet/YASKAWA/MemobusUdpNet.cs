using HslCommunication.Core;
using HslCommunication.Core.Pipe;
using HslCommunication.Profinet.YASKAWA.Helper;

namespace HslCommunication.Profinet.YASKAWA
{
	/// <inheritdoc cref="T:HslCommunication.Profinet.YASKAWA.MemobusTcpNet" />
	public class MemobusUdpNet : MemobusTcpNet, IMemobus, IReadWriteDevice, IReadWriteNet
	{
		/// <summary>
		/// 实例化一个Memobus-Tcp协议的客户端对象<br />
		/// Instantiate a client object of the Memobus-Tcp protocol
		/// </summary>
		public MemobusUdpNet()
		{
			CommunicationPipe = new PipeUdpNet();
		}

		/// <summary>
		/// 指定服务器地址，端口号，客户端自己的站号来初始化<br />
		/// Specify the server address, port number, and client's own station number to initialize
		/// </summary>
		/// <param name="ipAddress">服务器的Ip地址</param>
		/// <param name="port">服务器的端口号</param>
		public MemobusUdpNet(string ipAddress, int port = 502)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MemobusUdpNet[{IpAddress}:{Port}]";
		}
	}
}
