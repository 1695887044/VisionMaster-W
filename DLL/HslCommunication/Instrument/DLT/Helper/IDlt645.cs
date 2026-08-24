using System;
using HslCommunication.Core;

namespace HslCommunication.Instrument.DLT.Helper
{
	/// <summary>
	/// DLT645的接口实现
	/// </summary>
	public interface IDlt645 : IReadWriteDevice, IReadWriteNet
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
		/// 获取当前的DLT645的类型信息<br />
		/// Gets the type information of the current DLT645
		/// </summary>
		DLT645Type DLTType { get; }

		/// <summary>
		/// 获取或设置当前DLT645的密码，当进行写入数据操作的时候，需要正确的密码才能写入<br />
		/// Obtain or set the password of the current DLT645, and the correct password is required to write data operations
		/// </summary>
		/// <remarks>
		/// 对于 DLT645/1997 协议来说无效
		/// </remarks>
		string Password { get; set; }

		/// <summary>
		/// 获取或设置当前DLT645的操作者代码，当进行写入数据操作的时候，需要指定正确的值<br />
		/// Obtain or set the operator code of the current DLT645, and specify the correct value when writing data
		/// </summary>
		/// <remarks>
		/// 对于 DLT645/1997 协议来说无效
		/// </remarks>
		string OpCode { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ReadFromCoreServer(System.Net.Sockets.Socket,System.Byte[],System.Boolean,System.Boolean)" />
		OperateResult<byte[]> ReadFromCoreServer(byte[] send, bool hasResponseData, bool usePackAndUnpack = true);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ActiveDeveice" />
		OperateResult ActiveDeveice();

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.BroadcastTime(HslCommunication.Instrument.DLT.Helper.IDlt645,System.DateTime)" />
		OperateResult BroadcastTime(DateTime dateTime);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		OperateResult WriteAddress(string address);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt645)" />
		OperateResult<string> ReadAddress();

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadStringArray(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		OperateResult<string[]> ReadStringArray(string address);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.IDlt645.Trip(System.String,System.DateTime)" />
		OperateResult Trip(DateTime validTime);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Trip(System.String,System.DateTime)" />
		OperateResult Trip(string station, DateTime validTime);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.IDlt645.SwitchingOn(System.String,System.DateTime)" />
		OperateResult SwitchingOn(DateTime validTime);

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.SwitchingOn(System.String,System.DateTime)" />
		OperateResult SwitchingOn(string station, DateTime validTime);
	}
}
