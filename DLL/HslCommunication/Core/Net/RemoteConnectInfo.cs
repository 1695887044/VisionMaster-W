using System.Net;
using System.Text;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// DTU远程连接的一些信息
	/// </summary>
	public class RemoteConnectInfo
	{
		private bool ack = false;

		/// <summary>
		/// 远程端口号信息
		/// </summary>
		public IPEndPoint EndPoint { get; set; }

		/// <summary>
		/// 会话信息
		/// </summary>
		public PipeSession Session { get; set; }

		/// <summary>
		/// DTU的注册包
		/// </summary>
		public byte[] DtuBytes { get; set; }

		/// <summary>
		/// 是否需要返回
		/// </summary>
		public bool NeedAckResult => ack;

		/// <summary>
		/// 实例化一个远程连接信息对象
		/// </summary>
		/// <param name="ipAddress">IP地址</param>
		/// <param name="port">端口号</param>
		/// <param name="dtu">dtu的注册包</param>
		public RemoteConnectInfo(string ipAddress, int port, byte[] dtu)
		{
			EndPoint = new IPEndPoint(IPAddress.Parse(HslHelper.GetIpAddressFromInput(ipAddress)), port);
			DtuBytes = dtu;
		}

		/// <summary>
		/// 实例化一个远程连接信息对象
		/// </summary>
		/// <param name="ipAddress">IP地址</param>
		/// <param name="port">端口号</param>
		/// <param name="dtuId">DTU信息</param>
		/// <param name="password">密码</param>
		/// <param name="needAckResult">是否需要返回注册结果</param>
		public RemoteConnectInfo(string ipAddress, int port, string dtuId, string password = "", bool needAckResult = true)
		{
			EndPoint = new IPEndPoint(IPAddress.Parse(HslHelper.GetIpAddressFromInput(ipAddress)), port);
			DtuBytes = CreateHslAlienMessage(dtuId, password, needAckResult);
			ack = needAckResult;
		}

		private byte[] CreateHslAlienMessage(string dtuId, string password, bool needAckResult)
		{
			if (dtuId.Length > 11)
			{
				dtuId = dtuId.Substring(11);
			}
			byte[] array = new byte[30]
			{
				72, 83, 76, 0, 25, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0
			};
			if (dtuId.Length > 11)
			{
				dtuId = dtuId.Substring(0, 11);
			}
			Encoding.ASCII.GetBytes(dtuId).CopyTo(array, 5);
			if (!string.IsNullOrEmpty(password))
			{
				if (password.Length > 6)
				{
					password = password.Substring(6);
				}
				Encoding.ASCII.GetBytes(password).CopyTo(array, 16);
			}
			if (!needAckResult)
			{
				array[28] = 1;
			}
			return array;
		}
	}
}
