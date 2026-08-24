using HslCommunication.Core;

namespace HslCommunication.Instrument.DLT.Helper
{
	/// <summary>
	/// DLT698的接口实现
	/// </summary>
	public interface IDlt698 : IReadWriteDevice, IReadWriteNet
	{
		/// <summary>
		/// 获取或设置当前的地址域信息，是一个12个字符的BCD码，例如：149100007290<br />
		/// Get or set the current address domain information, which is a 12-character BCD code, for example: 149100007290
		/// </summary>
		string Station { get; set; }

		/// <summary>
		/// 获取或设置是否在每一次的报文通信时，增加"FE FE FE FE"的命令头<br />
		/// Get or set whether to add the command header of "FE FE FE FE" in each message communication
		/// </summary>
		bool EnableCodeFE { get; set; }

		/// <summary>
		/// 获取或设置是否使用安全的请求模式，对于有些仪表来说，不支持使用安全的模式，就需要设置为<c>False</c>。<br />
		/// Get or set whether to use the secure request mode, for some meters, the safe mode is not supported, so you need to set it to <c>False</c>.
		/// </summary>
		bool UseSecurityResquest { get; set; }

		/// <summary>
		/// 客户机地址CA用1字节表示， 0表示不关注客户机地址。<br />
		/// Client address CA is represented by 1 byte, and 0 means that the client address is not concerned.
		/// </summary>
		byte CA { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ReadFromCoreServer(System.Net.Sockets.Socket,System.Byte[],System.Boolean,System.Boolean)" />
		OperateResult<byte[]> ReadFromCoreServer(byte[] send, bool hasResponseData, bool usePackAndUnpack = true);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ActiveDeveice" />
		OperateResult ActiveDeveice();

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		OperateResult WriteAddress(string address);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt645)" />
		OperateResult<string> ReadAddress();

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadStringArray(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		OperateResult<string[]> ReadStringArray(string address);
	}
}
