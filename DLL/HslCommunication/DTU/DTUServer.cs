using System.Collections.Generic;
using System.Linq;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;

namespace HslCommunication.DTU
{
	/// <summary>
	/// DTU的服务器信息，本服务器支持任意的hsl支持的网络对象，包括plc信息，modbus设备等等，通过DTU来连接，
	/// 然后支持多个连接对象。如果需要支持非hsl的注册报文，需要重写相关的方法<br />
	/// DTU server information, the server supports any network objects supported by hsl, 
	/// including plc information, modbus devices, etc., connected through DTU, and then supports multiple connection objects. 
	/// If you need to support non-HSL registration messages, you need to rewrite the relevant methods
	/// </summary>
	/// <remarks>
	/// 针对异形客户端进行扩展信息
	/// </remarks>
	public class DTUServer : NetworkAlienClient
	{
		private Dictionary<string, DeviceCommunication> devices;

		/// <summary>
		/// 根据DTU信息获取设备的连接对象<br />
		/// Obtain the connection object of the device according to the DTU information
		/// </summary>
		/// <param name="dtuId">设备的id信息</param>
		/// <returns>设备的对象</returns>
		public DeviceCommunication this[string dtuId] => devices.ContainsKey(dtuId) ? devices[dtuId] : null;

		/// <summary>
		/// 根据配置的列表信息来实例化相关的DTU服务器<br />
		/// Instantiate the relevant DTU server according to the configured list information
		/// </summary>
		/// <param name="dTUSettings">DTU的配置信息</param>
		public DTUServer(List<DTUSettingType> dTUSettings)
		{
			devices = new Dictionary<string, DeviceCommunication>();
			SetTrustClients(dTUSettings.Select((DTUSettingType m) => m.DtuId).ToArray());
			for (int i = 0; i < dTUSettings.Count; i++)
			{
				devices.Add(dTUSettings[i].DtuId, dTUSettings[i].GetClient());
				devices[dTUSettings[i].DtuId].SetDtuPipe(new PipeDtuNet
				{
					DTU = dTUSettings[i].DtuId
				});
			}
			base.OnClientConnected += DTUServer_OnClientConnected;
		}

		/// <summary>
		/// 根据配置的列表信息来实例化相关的DTU服务器<br />
		/// Instantiate the relevant DTU server according to the configured list information
		/// </summary>
		/// <param name="dtuId">Dtu信息</param>
		/// <param name="networkDevices">设备信息</param>
		public DTUServer(string[] dtuId, DeviceTcpNet[] networkDevices)
		{
			devices = new Dictionary<string, DeviceCommunication>();
			SetTrustClients(dtuId);
			for (int i = 0; i < dtuId.Length; i++)
			{
				devices.Add(dtuId[i], networkDevices[i]);
				devices[dtuId[i]].SetDtuPipe(new PipeDtuNet
				{
					DTU = dtuId[i]
				});
			}
		}

		/// <inheritdoc />
		protected override void ExtraOnClose()
		{
			foreach (KeyValuePair<string, DeviceCommunication> device in devices)
			{
				(device.Value.CommunicationPipe as PipeDtuNet)?.CloseCommunication();
			}
			base.ExtraOnClose();
		}

		/// <inheritdoc />
		public override int IsClientOnline(PipeDtuNet pipe)
		{
			if (devices[pipe.DTU].CommunicationPipe.IsConnectError())
			{
				return 0;
			}
			return 1;
		}

		private void DTUServer_OnClientConnected(PipeDtuNet dtu)
		{
			devices[dtu.DTU].SetDtuPipe(dtu);
		}

		/// <summary>
		/// 获取所有的会话信息，是否在线，上线的基本信息<br />
		/// Get all the session information, whether it is online, online basic information
		/// </summary>
		/// <returns>会话列表</returns>
		public PipeDtuNet[] GetPipeSessions()
		{
			return devices.Values.Select((DeviceCommunication m) => m.CommunicationPipe as PipeDtuNet).ToArray();
		}

		/// <summary>
		/// 获取所有的设备的信息，可以用来读写设备的数据信息<br />
		/// Get all device information, can be used to read and write device data information
		/// </summary>
		/// <returns>设备数组</returns>
		public DeviceCommunication[] GetDevices()
		{
			return devices.Values.ToArray();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DTUServer[{base.Port}]";
		}
	}
}
