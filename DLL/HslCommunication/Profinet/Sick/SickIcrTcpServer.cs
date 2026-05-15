using System.Text;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;

namespace HslCommunication.Profinet.Sick
{
	/// <summary>
	/// Sick的扫码器的服务器信息，只要启动服务器之后，扫码器配置将条码发送到PC的指定端口上来即可，就可以持续的接收条码信息，同样也适用于海康，基恩士，DATELOGIC 。<br />
	/// The server information of Sick's code scanner, as long as the server is started, the code scanner is configured to send the barcode to the designated port of the PC, and it can continuously receive the barcode information.
	/// </summary>
	public class SickIcrTcpServer : CommunicationServer
	{
		/// <summary>
		/// 接收条码数据的委托信息<br />
		/// Entrusted information to receive barcode data
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		/// <param name="barCode">条码信息</param>
		public delegate void ReceivedBarCodeDelegate(string ipAddress, string barCode);

		/// <summary>
		/// 当接收到条码数据的时候触发<br />
		/// Triggered when barcode data is received
		/// </summary>
		public event ReceivedBarCodeDelegate OnReceivedBarCode;

		/// <summary>
		/// 实例化一个默认的服务器对象<br />
		/// Instantiate a default server object
		/// </summary>
		public SickIcrTcpServer()
		{
			base.OnPipeMessageReceived += SickIcrTcpServer_OnPipeMessageReceived;
		}

		private void SickIcrTcpServer_OnPipeMessageReceived(PipeSession session, byte[] buffer)
		{
			string ipAddress = string.Empty;
			PipeTcpNet pipeTcpNet = session.Communication as PipeTcpNet;
			if (pipeTcpNet != null)
			{
				ipAddress = pipeTcpNet.IpAddress;
			}
			if (session != null)
			{
				base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> Recv: " + buffer.ToHexString(' '));
			}
			this.OnReceivedBarCode?.Invoke(ipAddress, TranslateCode(Encoding.ASCII.GetString(buffer)));
		}

		private string TranslateCode(string code)
		{
			StringBuilder stringBuilder = new StringBuilder("");
			for (int i = 0; i < code.Length; i++)
			{
				if (char.IsLetterOrDigit(code, i))
				{
					stringBuilder.Append(code[i]);
				}
			}
			return stringBuilder.ToString();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SickIcrTcpServer[{base.Port}]";
		}
	}
}
