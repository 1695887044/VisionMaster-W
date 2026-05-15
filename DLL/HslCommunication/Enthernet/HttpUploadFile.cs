namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 获取或设置Http模式下上传的文件信息
	/// </summary>
	public class HttpUploadFile
	{
		/// <summary>
		/// 获取或设置文本的名称
		/// </summary>
		public string FileName { get; set; }

		/// <summary>
		/// 获取或设置用户设置的名称
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// 获取或设置文件的内容
		/// </summary>
		public byte[] Content { get; set; }
	}
}
