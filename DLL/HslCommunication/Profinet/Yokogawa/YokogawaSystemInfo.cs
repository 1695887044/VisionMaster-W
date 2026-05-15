using System;
using System.Text;

namespace HslCommunication.Profinet.Yokogawa
{
	/// <summary>
	/// 横河PLC的系统基本信息<br />
	/// Basic system information of Yokogawa PLC
	/// </summary>
	public class YokogawaSystemInfo
	{
		/// <summary>
		/// 当前系统的ID名称，例如F3SP21-ON<br />
		/// The ID name of the current system, such as F3SP21-ON
		/// </summary>
		public string SystemID { get; set; }

		/// <summary>
		/// 当前系统的修订版本号<br />
		/// The revision number of the current system
		/// </summary>
		public string Revision { get; set; }

		/// <summary>
		/// 当前系统的类型，分为 <b>Sequence</b> 和 <b>BASIC</b> <br />
		/// The type of the current system, divided into <b>Sequence</b> and <b>BASIC</b>
		/// </summary>
		public string CpuType { get; set; }

		/// <summary>
		/// 当前系统的程序大小，如果是Sequence系统，就是步序总量，如果是BASIC系统，就是字节数量<br />
		/// The program size of the current system, if it is a Sequence system, it is the total number of steps, if it is a BASIC system, it is the number of bytes
		/// </summary>
		public int ProgramAreaSize { get; set; }

		/// <inheritdoc />
		public override string ToString()
		{
			return "YokogawaSystemInfo[" + SystemID + "]";
		}

		/// <summary>
		/// 根据原始的数据信息解析出<see cref="T:HslCommunication.Profinet.Yokogawa.YokogawaSystemInfo" />对象<br />
		/// Analyze the <see cref="T:HslCommunication.Profinet.Yokogawa.YokogawaSystemInfo" /> object according to the original data information
		/// </summary>
		/// <param name="content">原始的数据信息</param>
		/// <returns>是否解析成功的结果对象</returns>
		public static OperateResult<YokogawaSystemInfo> Parse(byte[] content)
		{
			try
			{
				YokogawaSystemInfo yokogawaSystemInfo = new YokogawaSystemInfo();
				yokogawaSystemInfo.SystemID = Encoding.ASCII.GetString(content, 0, 16).Trim('\0', ' ');
				yokogawaSystemInfo.Revision = Encoding.ASCII.GetString(content, 16, 8).Trim('\0', ' ');
				if (content[25] == 1 || content[25] == 17)
				{
					yokogawaSystemInfo.CpuType = "Sequence";
				}
				else if (content[25] == 2 || content[25] == 18)
				{
					yokogawaSystemInfo.CpuType = "BASIC";
				}
				else
				{
					yokogawaSystemInfo.CpuType = StringResources.Language.UnknownError;
				}
				yokogawaSystemInfo.ProgramAreaSize = content[26] * 256 + content[27];
				return OperateResult.CreateSuccessResult(yokogawaSystemInfo);
			}
			catch (Exception ex)
			{
				return new OperateResult<YokogawaSystemInfo>("Parse YokogawaSystemInfo failed: " + ex.Message + Environment.NewLine + "Source: " + content.ToHexString(' '));
			}
		}
	}
}
