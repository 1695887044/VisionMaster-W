using System.Threading.Tasks;
using HslCommunication.Core.Address;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Melsec.Helper;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士PLC的数据通信类，使用QnA兼容3E帧的通信协议实现，使用ASCII的格式，地址格式需要进行转换成三菱的格式，详细参照备注说明<br />
	/// Keyence PLC's data communication class is implemented using QnA compatible 3E frame communication protocol. 
	/// It uses ascii format. The address format needs to be converted to Mitsubishi format.
	/// </summary>
	/// <remarks>
	/// <inheritdoc cref="T:HslCommunication.Profinet.Keyence.KeyenceMcNet" path="remarks" />
	/// </remarks>
	public class KeyenceMcAsciiNet : MelsecMcAsciiNet
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceMcNet.#ctor" />
		public KeyenceMcAsciiNet()
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceMcNet.#ctor(System.String,System.Int32)" />
		public KeyenceMcAsciiNet(string ipAddress, int port)
			: base(ipAddress, port)
		{
		}

		/// <inheritdoc />
		public override OperateResult<McAddressData> McAnalysisAddress(string address, ushort length, bool isBit)
		{
			return McAddressData.ParseKeyenceFrom(address, length, isBit);
		}

		/// <inheritdoc />
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			if (KeyenceMcNet.CheckKeyenceBoolAddress(address))
			{
				return McHelper.ReadBool(this, address, length, supportWordAdd: false);
			}
			return base.ReadBool(address, length);
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, bool[] values)
		{
			if (KeyenceMcNet.CheckKeyenceBoolAddress(address))
			{
				return McHelper.Write(this, address, values, supportWordAdd: false);
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			if (KeyenceMcNet.CheckKeyenceBoolAddress(address))
			{
				return await McHelper.ReadBoolAsync(this, address, length, supportWordAdd: false).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.ReadBoolAsync(address, length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, bool[] values)
		{
			if (KeyenceMcNet.CheckKeyenceBoolAddress(address))
			{
				return await McHelper.WriteAsync(this, address, values, supportWordAdd: false).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await base.WriteAsync(address, values).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceMcAsciiNet[{IpAddress}:{Port}]";
		}
	}
}
