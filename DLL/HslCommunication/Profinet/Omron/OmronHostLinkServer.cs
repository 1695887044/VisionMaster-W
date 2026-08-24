using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.Omron
{
	/// <summary>
	/// 欧姆龙的HostLink虚拟服务器，支持DM区，CIO区，Work区，Hold区，Auxiliary区，可以方便的进行测试<br />
	/// Omron's HostLink virtual server supports DM area, CIO area, Work area, Hold area, and Auxiliary area, which can be easily tested
	/// </summary>
	/// <remarks>
	/// 支持TCP的接口以及串口，方便客户端进行测试，或是开发用于教学的虚拟服务器对象
	/// </remarks>
	public class OmronHostLinkServer : OmronFinsServer
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Omron.OmronHostLink.UnitNumber" />
		public byte UnitNumber { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Profinet.Omron.OmronFinsServer.#ctor" />
		public OmronHostLinkServer()
		{
			connectionInitialization = false;
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(13);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length < 22)
			{
				return new OperateResult<byte[]>("Uknown Data：" + receive.ToHexString(' '));
			}
			string @string = Encoding.ASCII.GetString(receive, 14, receive.Length - 18);
			byte[] array = ReadFromFinsCore(SoftBasic.HexStringToBytes(@string));
			array[13] = receive[12];
			array[14] = receive[13];
			return OperateResult.CreateSuccessResult(array);
		}

		/// <inheritdoc />
		protected override byte[] PackCommand(int status, byte[] finsCore, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			data = SoftBasic.BytesToAsciiBytes(data);
			byte[] array = new byte[27 + data.Length];
			Encoding.ASCII.GetBytes("@00FA0040000000").CopyTo(array, 0);
			Encoding.ASCII.GetBytes(UnitNumber.ToString("X2")).CopyTo(array, 1);
			if (data.Length != 0)
			{
				data.CopyTo(array, 23);
			}
			Encoding.ASCII.GetBytes(finsCore.SelectBegin(2).ToHexString()).CopyTo(array, 15);
			Encoding.ASCII.GetBytes(status.ToString("X4")).CopyTo(array, 19);
			int num = array[0];
			for (int i = 1; i < array.Length - 4; i++)
			{
				num ^= array[i];
			}
			SoftBasic.BuildAsciiBytesFrom((byte)num).CopyTo(array, array.Length - 4);
			array[array.Length - 2] = 42;
			array[array.Length - 1] = 13;
			return array;
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			if (receivedLength > 1)
			{
				return buffer[receivedLength - 1] == 13;
			}
			return false;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"OmronHostLinkServer[{base.Port}]";
		}
	}
}
