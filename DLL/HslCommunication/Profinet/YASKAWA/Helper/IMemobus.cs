using HslCommunication.Core;

namespace HslCommunication.Profinet.YASKAWA.Helper
{
	/// <summary>
	/// Memobus协议的接口信息
	/// </summary>
	public interface IMemobus : IReadWriteDevice, IReadWriteNet
	{
		/// <summary>
		/// 获取或设置发送目标的CPU的编号信息，默认为 2<br />
		/// Get or set the CPU number information of the sending destination, the default is 2
		/// </summary>
		byte CpuTo { get; set; }

		/// <summary>
		/// 获取或设置发送源的CPU的编号信息，默认为 1<br />
		/// Get or set the number information of the sending source CPU, the default is 1
		/// </summary>
		byte CpuFrom { get; set; }
	}
}
