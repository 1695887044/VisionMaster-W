using System;
using System.Threading.Tasks;
using HslCommunication.Core.Address;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Melsec.Helper;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士PLC的数据通信类，使用QnA兼容3E帧的通信协议实现，使用二进制的格式，地址同时支持三菱的地址格式及基恩士自身的地址格式，详细参照备注说明<br />
	/// The data communication class of KEYENCE PLC is implemented using a QnA-compatible 3E frame communication protocol, using binary format, 
	/// and the address supports both Mitsubishi's address format and Keyence's own address format, please refer to the remarks for details
	/// </summary>
	/// <remarks>
	/// 地址支持 R015, MR015, LR015, CR015, CM0, DM100, EM100, FM100, ZF100, W1A0, TN0, TS0, CN0, CS0, 具体范围参考：http://api.hslcommunication.cn/html/04bd2a21-7ab0-2fb9-f7f1-e0f0ecaf9227.htm
	/// </remarks>
	public class KeyenceMcNet : MelsecMcNet
	{
		/// <summary>
		/// 实例化基恩士的Qna兼容3E帧协议的通讯对象<br />
		/// Instantiate Keyence Qna compatible 3E frame protocol communication object
		/// </summary>
		public KeyenceMcNet()
		{
		}

		/// <summary>
		/// 指定ip地址及端口号来实例化一个基恩士的Qna兼容3E帧协议的通讯对象<br />
		/// Specify an IP address and port number to instantiate a Keynes Qna compatible 3E frame protocol communication object
		/// </summary>
		/// <param name="ipAddress">PLC的Ip地址</param>
		/// <param name="port">PLC的端口</param>
		public KeyenceMcNet(string ipAddress, int port)
			: base(ipAddress, port)
		{
		}

		/// <inheritdoc />
		public override OperateResult<McAddressData> McAnalysisAddress(string address, ushort length, bool isBit)
		{
			return McAddressData.ParseKeyenceFrom(address, length, isBit);
		}

		internal static bool CheckKeyenceBoolAddress(string address)
		{
			if (address.StartsWith("MR", StringComparison.OrdinalIgnoreCase) || address.StartsWith("CR", StringComparison.OrdinalIgnoreCase) || address.StartsWith("LR", StringComparison.OrdinalIgnoreCase) || address.StartsWith("R", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return false;
		}

		/// <inheritdoc />
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			if (CheckKeyenceBoolAddress(address))
			{
				return McHelper.ReadBool(this, address, length, supportWordAdd: false);
			}
			return base.ReadBool(address, length);
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, bool[] values)
		{
			if (CheckKeyenceBoolAddress(address))
			{
				return McHelper.Write(this, address, values, supportWordAdd: false);
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			if (CheckKeyenceBoolAddress(address))
			{
				return await McHelper.ReadBoolAsync(this, address, length, supportWordAdd: false).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.ReadBoolAsync(address, length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, bool[] values)
		{
			if (CheckKeyenceBoolAddress(address))
			{
				return await McHelper.WriteAsync(this, address, values, supportWordAdd: false).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.WriteAsync(address, values).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceMcNet[{IpAddress}:{Port}]";
		}
	}
}
