namespace HslCommunication.Core
{
	/// <summary>
	/// 文件的分类信息
	/// </summary>
	public class FileGroupInfo
	{
		/// <summary>
		/// 命令码
		/// </summary>
		public int Command { get; set; }

		/// <summary>
		/// 文件名
		/// </summary>
		public string FileName { get; set; }

		/// <summary>
		/// 文件名列表
		/// </summary>
		public string[] FileNames { get; set; }

		/// <summary>
		/// 第一级分类信息
		/// </summary>
		public string Factory { get; set; }

		/// <summary>
		/// 第二级分类信息
		/// </summary>
		public string Group { get; set; }

		/// <summary>
		/// 第三级分类信息
		/// </summary>
		public string Identify { get; set; }
	}
}
