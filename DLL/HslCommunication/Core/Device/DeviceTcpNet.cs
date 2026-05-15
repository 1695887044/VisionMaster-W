using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Device
{
	/// <summary>
	/// 基于TCP管道的设备基类信息
	/// </summary>
	public class DeviceTcpNet : DeviceCommunication
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
		public DeviceTcpNet()
			: this("127.0.0.1", 5000)
		{
		}

		/// <summary>
		/// 指定IP地址以及端口号信息来初始化对象
		/// </summary>
		/// <param name="ipAddress">IP地址信息，可以是IPv4, IPv6, 也可以是域名</param>
		/// <param name="port">设备方的端口号信息</param>
		public DeviceTcpNet(string ipAddress, int port)
		{
			pipeTcpNet = new PipeTcpNet();
			pipeTcpNet.IpAddress = ipAddress;
			pipeTcpNet.Port = port;
			CommunicationPipe = pipeTcpNet;
		}

		/// <summary>
		/// V11版本及之前设置长连接的方法，在V12版本以上中没有任何效果，默认长连接，删除调用即可，此处保留方法是为了部分用户保持兼容性升级。<br />
		/// The method of setting the long connection in V11 and before, has no effect in V12 and above. this method can be deleted. 
		/// The method is retained here to maintain compatibility upgrades for some users.
		/// </summary>
		[Obsolete]
		public void SetPersistentConnection()
		{
		}

		/// <summary>
		/// 对当前设备的IP地址进行PING的操作，返回PING的结果，正常来说，返回<see cref="F:System.Net.NetworkInformation.IPStatus.Success" /><br />
		/// PING the IP address of the current device and return the PING result. Normally, it returns <see cref="F:System.Net.NetworkInformation.IPStatus.Success" />
		/// </summary>
		/// <returns>返回PING的结果</returns>
		public IPStatus IpAddressPing()
		{
			return ping.Value.Send(IpAddress).Status;
		}

		/// <summary>
		/// 尝试连接远程的服务器，如果连接成功，就切换短连接模式到长连接模式，后面的每次请求都共享一个通道，使得通讯速度更快速<br />
		/// Try to connect to a remote server. If the connection is successful, switch the short connection mode to the long connection mode. 
		/// Each subsequent request will share a channel, making the communication speed faster.
		/// </summary>
		/// <returns>返回连接结果，如果失败的话（也即IsSuccess为False），包含失败信息</returns>
		/// <example>
		///   简单的连接示例，调用该方法后，连接设备，创建一个长连接的对象，后续的读写操作均公用一个连接对象。
		///   <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="Connect1" title="连接设备" />
		///   如果想知道是否连接成功，请参照下面的代码。
		///   <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="Connect2" title="判断连接结果" />
		/// </example> 
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

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceTcpNet.ConnectServer" />
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

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceTcpNet.ConnectClose" />
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
			return $"DeviceTcpNet<{base.ByteTransform}>{{{CommunicationPipe}}}";
		}
	}
}
