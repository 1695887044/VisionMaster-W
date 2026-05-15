using HslCommunication.Core;

namespace HslCommunication.Profinet.FATEK.Helper
{
	/// <summary>
	/// FatekProgram协议的接口
	/// </summary>
	public interface IFatekProgram : IReadWriteNet
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Run(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		OperateResult Run(byte station);

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.IFatekProgram.Run(System.Byte)" />
		OperateResult Run();

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.Stop(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		OperateResult Stop(byte station);

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.IFatekProgram.Stop(System.Byte)" />
		OperateResult Stop();

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.FatekProgramHelper.ReadStatus(HslCommunication.Core.IReadWriteDevice,System.Byte)" />
		OperateResult<bool[]> ReadStatus(byte station);

		/// <inheritdoc cref="M:HslCommunication.Profinet.FATEK.Helper.IFatekProgram.ReadStatus(System.Byte)" />
		OperateResult<bool[]> ReadStatus();
	}
}
