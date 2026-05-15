using HslCommunication.Core;

namespace HslCommunication.Instrument.CJT.Helper
{
	/// <summary>
	/// CJT188设备的接口
	/// </summary>
	public interface ICjt188 : IReadWriteDevice, IReadWriteNet
	{
		/// <summary>
		/// 仪表的类型
		/// </summary>
		byte InstrumentType { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.Station" />
		string Station { get; set; }

		/// <summary>
		/// 获取或设置是否在每一次的报文通信时，增加"FE FE"的命令头<br />
		/// Get or set whether to add the command header of "FE FE" in each message communication
		/// </summary>
		bool EnableCodeFE { get; set; }

		/// <summary>
		/// 激活设备的命令，只发送数据到设备，不等待设备数据返回<br />
		/// The command to activate the device, only send data to the device, do not wait for the device data to return
		/// </summary>
		/// <returns>是否发送成功</returns>
		OperateResult ActiveDeveice();

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.WriteAddress(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String)" />
		OperateResult WriteAddress(string address);

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.ReadAddress(HslCommunication.Instrument.CJT.Helper.ICjt188)" />
		OperateResult<string> ReadAddress();

		/// <inheritdoc cref="M:HslCommunication.Instrument.CJT.Helper.CJT188Helper.ReadStringArray(HslCommunication.Instrument.CJT.Helper.ICjt188,System.String)" />
		OperateResult<string[]> ReadStringArray(string address);
	}
}
