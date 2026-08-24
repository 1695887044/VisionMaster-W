using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 基于TCP、IP通信的类
	/// </summary>
	public class TcpNetCommunication : BinaryCommunication
	{
		private PipeTcpNet pipeTcpNet;

		private Lazy<Ping> ping = new Lazy<Ping>(() => new Ping());

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeTcpNet.ConnectTimeOut" />
		[HslMqttApi(HttpMethod = "GET", Description = "Gets or sets the timeout for the connection, in milliseconds")]
		public virtual int ConnectTimeOut
		{
			get
			{
				return (CommunicationPipe as PipeTcpNet)?.ConnectTimeOut ?? pipeTcpNet.ConnectTimeOut;
			}
			set
			{
				PipeTcpNet pipeTcpNet = default(PipeTcpNet);
				int num;
				if (value >= 0)
				{
					pipeTcpNet = CommunicationPipe as PipeTcpNet;
					num = ((pipeTcpNet != null) ? 1 : 0);
				}
				else
				{
					num = 0;
				}
				if (num != 0)
				{
					pipeTcpNet.ConnectTimeOut = value;
				}
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeTcpNet.IpAddress" />
		[HslMqttApi(HttpMethod = "GET", Description = "Get or set the IP address of the remote server. If it is a local test, then it needs to be set to 127.0.0.1")]
		public virtual string IpAddress
		{
			get
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					return pipeTcpNet.IpAddress;
				}
				return this.pipeTcpNet.IpAddress;
			}
			set
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					pipeTcpNet.IpAddress = value;
				}
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeTcpNet.Port" />
		[HslMqttApi(HttpMethod = "GET", Description = "Gets or sets the port number of the server. The specific value depends on the configuration of the other party.")]
		public virtual int Port
		{
			get
			{
				return (CommunicationPipe as PipeTcpNet)?.Port ?? pipeTcpNet.Port;
			}
			set
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					pipeTcpNet.Port = value;
				}
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeTcpNet.LocalBinding" />
		public IPEndPoint LocalBinding
		{
			get
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					return pipeTcpNet.LocalBinding;
				}
				return this.pipeTcpNet.LocalBinding;
			}
			set
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					pipeTcpNet.LocalBinding = value;
				}
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Pipe.PipeTcpNet.SocketKeepAliveTime" />
		public int SocketKeepAliveTime
		{
			get
			{
				return (CommunicationPipe as PipeTcpNet)?.SocketKeepAliveTime ?? pipeTcpNet.SocketKeepAliveTime;
			}
			set
			{
				PipeTcpNet pipeTcpNet = CommunicationPipe as PipeTcpNet;
				if (pipeTcpNet != null)
				{
					pipeTcpNet.SocketKeepAliveTime = value;
				}
			}
		}

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public TcpNetCommunication()
			: this("127.0.0.1", 5000)
		{
		}

		/// <summary>
		/// 指定IP地址以及端口号信息来初始化对象
		/// </summary>
		/// <param name="ipAddress">IP地址信息，可以是IPv4, IPv6, 也可以是域名</param>
		/// <param name="port">设备方的端口号信息</param>
		public TcpNetCommunication(string ipAddress, int port)
		{
			pipeTcpNet = new PipeTcpNet();
			pipeTcpNet.IpAddress = ipAddress;
			pipeTcpNet.Port = port;
			CommunicationPipe = pipeTcpNet;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceTcpNet.IpAddressPing" />
		public IPStatus IpAddressPing()
		{
			return ping.Value.Send(IpAddress).Status;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceTcpNet.ConnectServer" />
		public OperateResult ConnectServer()
		{
			CommunicationPipe?.CloseCommunication();
			OperateResult<bool> operateResult = CommunicationPipe.OpenCommunication();
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.ConnectedSuccess);
			if (operateResult.Content)
			{
				return InitializationOnConnect();
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.TcpNetCommunication.ConnectServer" />
		public async Task<OperateResult> ConnectServerAsync()
		{
			await CommunicationPipe.CloseCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
			OperateResult<bool> open = await CommunicationPipe.OpenCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (!open.IsSuccess)
			{
				return open;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.ConnectedSuccess);
			if (open.Content)
			{
				return await InitializationOnConnectAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 手动断开与远程服务器的连接，如果当前是长连接模式，那么就会切换到短连接模式<br />
		/// Manually disconnect from the remote server, if it is currently in long connection mode, it will switch to short connection mode
		/// </summary>
		/// <returns>关闭连接，不需要查看IsSuccess属性查看</returns>
		/// <example>
		/// 直接关闭连接即可，基本上是不需要进行成功的判定
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="ConnectCloseExample" title="关闭连接结果" />
		/// </example>
		public OperateResult ConnectClose()
		{
			OperateResult operateResult = ExtraOnDisconnect();
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Close);
			return CommunicationPipe.CloseCommunication();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.TcpNetCommunication.ConnectClose" />
		public async Task<OperateResult> ConnectCloseAsync()
		{
			OperateResult result = await ExtraOnDisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (!result.IsSuccess)
			{
				return result;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Close);
			return await CommunicationPipe.CloseCommunicationAsync().ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"TcpNetCommunication<{CommunicationPipe}>";
		}
	}
}
