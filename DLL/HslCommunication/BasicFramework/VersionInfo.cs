using System;
using System.Text;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 版本信息类，用于展示版本发布信息
	/// </summary>
	public sealed class VersionInfo
	{
		/// <summary>
		/// 版本的发行日期
		/// </summary>
		public DateTime ReleaseDate { get; set; } = DateTime.Now;


		/// <summary>
		/// 版本的更新细节
		/// </summary>
		public StringBuilder UpdateDetails { get; set; } = new StringBuilder();


		/// <summary>
		/// 版本号
		/// </summary>
		public SystemVersion VersionNum { get; set; } = new SystemVersion(1, 0, 0);


		/// <inheritdoc />
		public override string ToString()
		{
			return VersionNum.ToString();
		}
	}
}
