using System.Threading.Tasks;
using HslCommunication.ModBus;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.MegMeet
{
	/// <summary>
	/// 深圳麦格米特PLC的通信对象，基于ModbusRtu转以太网协议实现，适用机型为 MC80/MC100/MC200/MC280/MC200E，具体支持的地址及范围参见API文档：http://api.hslcommunication.cn<br />
	/// The communication object of Shenzhen MegMeet PLC is based on the ModbusRtu over Ethernet protocol, and the applicable model is MC80/MC100/MC200/MC280/MC200E, 
	/// and the specific supported address and range are described in API document: http://api.hslcommunication.cn
	/// </summary>
	/// <remarks>
	/// 位读写地址支持：X,Y,M,SM,S,T,C，字读写地址为：D,SD,Z,R,T,C，期中 C200以上使用int/uint类型进行读写操作
	/// </remarks>
	public class MegMeetSerialOverTcp : ModbusRtuOverTcp
	{
		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public MegMeetSerialOverTcp()
		{
		}

		/// <summary>
		/// 通过指定站号，ip地址，端口号来实例化一个新的对象
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		/// <param name="station">站号信息</param>
		public MegMeetSerialOverTcp(string ipAddress, int port = 502, byte station = 1)
			: base(ipAddress, port, station)
		{
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return MegMeetHelper.ReadBool(base.ReadBool, address, length);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return MegMeetHelper.Read(base.Read, address, length);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await MegMeetHelper.ReadBoolAsync((string address, ushort length) => base.ReadBoolAsync(address, length), address, length);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await MegMeetHelper.ReadAsync((string address, ushort length) => base.ReadAsync(address, length), address, length);
		}

		/// <inheritdoc />
		public override OperateResult<string> TranslateToModbusAddress(string address, byte modbusCode)
		{
			return MegMeetHelper.PraseMegMeetAddress(address, modbusCode);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MegMeetSerialOverTcp[{IpAddress}:{Port}]";
		}
	}
}
