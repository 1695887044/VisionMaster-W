namespace HslCommunication.Core
{
	/// <summary>
	/// 文件在服务器上的信息
	/// </summary>
	public class FileServerInfo : FileBaseInfo
	{
		/// <summary>
		/// 文件的真实路径
		/// </summary>
		public string ActualFileFullName { get; set; }
	}
}
