using HslCommunication.Core;
using HslCommunication.Core.Pipe;

namespace HslCommunication.Profinet.Freedom
{
	/// <summary>
	/// 基于UDP/IP协议的自由协议，需要在地址里传入报文信息，也可以传入数据偏移信息，<see cref="P:HslCommunication.Core.Device.DeviceCommunication.ByteTransform" />默认为<see cref="T:HslCommunication.Core.RegularByteTransform" />
	/// </summary>
	/// <example>
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\FreedomExample.cs" region="Sample3" title="实例化" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\FreedomExample.cs" region="Sample4" title="读取" />
	/// </example>
	public class FreedomUdpNet : FreedomTcpNet
	{
		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public FreedomUdpNet()
		{
			base.ByteTransform = new RegularByteTransform();
			CommunicationPipe = new PipeUdpNet();
		}

		/// <summary>
		/// 指定IP地址及端口号来实例化自由的TCP协议
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口</param>
		public FreedomUdpNet(string ipAddress, int port)
		{
			CommunicationPipe = new PipeUdpNet();
			IpAddress = ipAddress;
			Port = port;
			base.ByteTransform = new RegularByteTransform();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FreedomUdpNet<{base.ByteTransform.GetType()}>[{IpAddress}:{Port}]";
		}
	}
}
