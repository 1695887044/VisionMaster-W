using System;
using HslCommunication.Core.Device;
using HslCommunication.ModBus;
using HslCommunication.Profinet.AllenBradley;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json.Linq;

namespace HslCommunication.DTU
{
	/// <summary>
	/// DTU的类型设置器
	/// </summary>
	public class DTUSettingType
	{
		/// <summary>
		/// 设备的唯一ID信息
		/// </summary>
		public string DtuId { get; set; }

		/// <summary>
		/// 当前的设备的类型
		/// </summary>
		public string DtuType { get; set; } = "ModbusRtuOverTcp";


		/// <summary>
		/// 额外的参数都存放在json里面
		/// </summary>
		public string JsonParameter { get; set; } = "{}";


		/// <inheritdoc />
		public override string ToString()
		{
			return DtuId + " [" + DtuType + "]";
		}

		/// <summary>
		/// 根据类型，获取连接对象
		/// </summary>
		/// <returns>获取设备的连接对象</returns>
		public virtual DeviceTcpNet GetClient()
		{
			JObject jObject = JObject.Parse(JsonParameter);
			if (DtuType == "ModbusRtuOverTcp")
			{
				return new ModbusRtuOverTcp("127.0.0.1", 502, jObject["Station"].Value<byte>())
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "ModbusTcpNet")
			{
				return new ModbusTcpNet("127.0.0.1", 502, jObject["Station"].Value<byte>())
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecMcNet")
			{
				return new MelsecMcNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecMcAsciiNet")
			{
				return new MelsecMcAsciiNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecA1ENet")
			{
				return new MelsecA1ENet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecA1EAsciiNet")
			{
				return new MelsecA1EAsciiNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecA3CNetOverTcp")
			{
				return new MelsecA3CNetOverTcp("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecFxLinksOverTcp")
			{
				return new MelsecFxLinksOverTcp("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "MelsecFxSerialOverTcp")
			{
				return new MelsecFxSerialOverTcp("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "SiemensS7Net")
			{
				return new SiemensS7Net((SiemensPLCS)Enum.Parse(typeof(SiemensPLCS), jObject["SiemensPLCS"].Value<string>()))
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "SiemensFetchWriteNet")
			{
				return new SiemensFetchWriteNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "SiemensPPIOverTcp")
			{
				return new SiemensPPIOverTcp("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "OmronFinsNet")
			{
				return new OmronFinsNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "OmronHostLinkOverTcp")
			{
				return new OmronHostLinkOverTcp("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			if (DtuType == "AllenBradleyNet")
			{
				return new AllenBradleyNet("127.0.0.1", 5000)
				{
					ConnectionId = DtuId
				};
			}
			throw new NotImplementedException();
		}
	}
}
