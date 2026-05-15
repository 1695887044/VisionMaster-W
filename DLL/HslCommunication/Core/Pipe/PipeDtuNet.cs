using System.Threading.Tasks;
using HslCommunication.Core.Net;

namespace HslCommunication.Core.Pipe
{
	/// <summary>
	/// DTU(数据转换模块)的管道信息<br />
	/// Pipeline information of the DTU (Data Transfer unit) 
	/// </summary>
	public class PipeDtuNet : PipeTcpNet
	{
		/// <summary>
		/// 唯一的标识<br />
		/// Unique identification
		/// </summary>
		public string DTU { get; set; }

		/// <summary>
		/// 注册报文里的端口号信息，实际的使用过程中，你可以用来做一些额外的标记<br />
		/// You can use the port number information in the registration packet to make some other markings during actual use
		/// </summary>
		public ushort DTUPort { get; set; }

		/// <summary>
		/// 注册报文里的IP地址信息，实际的使用过程中，你也可以用来做一些额外的标记<br />
		/// You can also use the IP address information in the registration packet to make some other markings during actual use
		/// </summary>
		public int DTUIpAddress { get; set; }

		/// <summary>
		/// 密码信息<br />
		/// Password information
		/// </summary>
		public string Pwd { get; set; }

		/// <summary>
		/// 当前的DTU会话关联的服务器信息
		/// </summary>
		public NetworkAlienClient DtuServer { get; set; }

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public PipeDtuNet()
		{
		}

		/// <summary>
		/// 根据传入的TCP管道来初始化新的DTU管道实例<br />
		/// Initialize the new DTU pipe instance based on the incoming TCP pipe
		/// </summary>
		/// <param name="pipeTcpNet">TCP管道</param>
		public PipeDtuNet(PipeTcpNet pipeTcpNet)
		{
			base.Socket = pipeTcpNet.Socket;
			base.ReceiveTimeOut = pipeTcpNet.ReceiveTimeOut;
			base.IpAddress = pipeTcpNet.IpAddress;
			base.Port = pipeTcpNet.Port;
		}

		/// <inheritdoc />
		public override OperateResult<bool> OpenCommunication()
		{
			if (IsConnectError())
			{
				NetSupport.CloseSocket(base.Socket);
				return new OperateResult<bool>(StringResources.Language.ConnectionIsNotAvailable);
			}
			return OperateResult.CreateSuccessResult(value: false);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool>> OpenCommunicationAsync()
		{
			if (IsConnectError())
			{
				return await Task.FromResult(new OperateResult<bool>(StringResources.Language.ConnectionIsNotAvailable)).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await Task.FromResult(OperateResult.CreateSuccessResult(value: false)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"PipeDtuNet[{base.IpAddress}:{base.Port}-{DTU}]";
		}
	}
}
