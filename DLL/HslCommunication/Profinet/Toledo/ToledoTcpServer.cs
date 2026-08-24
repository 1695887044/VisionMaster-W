using System;
using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.Toledo
{
	/// <summary>
	/// 托利多电子秤的TCP服务器，启动服务器后，等待电子秤的数据连接。
	/// </summary>
	public class ToledoTcpServer : CommunicationServer
	{
		/// <summary>
		/// 托利多数据接收时的委托
		/// </summary>
		/// <param name="sender">数据发送对象</param>
		/// <param name="toledoStandardData">数据对象</param>
		public delegate void ToledoStandardDataReceivedDelegate(object sender, ToledoStandardData toledoStandardData);

		/// <summary>
		/// 获取或设置当前的报文否是含有校验的，默认为含有校验
		/// </summary>
		public bool HasChk { get; set; } = true;


		/// <summary>
		/// 当接收到一条新的托利多的数据的时候触发
		/// </summary>
		public event ToledoStandardDataReceivedDelegate OnToledoStandardDataReceived;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public ToledoTcpServer()
		{
			base.OnPipeMessageReceived += ToledoTcpServer_OnPipeMessageReceived;
		}

		private void ToledoTcpServer_OnPipeMessageReceived(PipeSession session, byte[] buffer)
		{
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Receive + " : " + buffer.ToHexString(' '));
			ToledoStandardData toledoStandardData = null;
			try
			{
				toledoStandardData = new ToledoStandardData(buffer);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), "ToledoStandardData new failed: " + buffer.ToHexString(' '), ex);
			}
			if (toledoStandardData != null)
			{
				this.OnToledoStandardDataReceived?.Invoke(session, toledoStandardData);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ToledoTcpServer[{base.Port}]";
		}
	}
}
