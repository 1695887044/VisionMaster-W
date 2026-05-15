using System.Collections.Generic;
using System.IO;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 基于路径实现的日志类的基类，提供几个基础的方法信息。<br />
	/// The base class of the log class implemented based on the path provides several basic method information.
	/// </summary>
	public abstract class LogPathBase : LogNetBase
	{
		/// <summary>
		/// 当前正在存储的文件名<br />
		/// File name currently being stored
		/// </summary>
		protected string fileName = string.Empty;

		/// <summary>
		/// 存储文件的路径，如果设置为空，就不进行存储。<br />
		/// The path for storing the file. If it is set to empty, it will not be stored.
		/// </summary>
		protected string filePath = string.Empty;

		/// <summary>
		/// 控制文件的数量，小于1则不进行任何操作，当设置为10的时候，就限制文件数量为10。<br />
		/// Control the number of files. If it is less than 1, no operation is performed. When it is set to 10, the number of files is limited to 10.
		/// </summary>
		protected int controlFileQuantity = -1;

		/// <inheritdoc />
		protected override void OnWriteCompleted(bool createNewLogFile)
		{
			if (!createNewLogFile || controlFileQuantity <= 1)
			{
				return;
			}
			try
			{
				string[] existLogFileNames = GetExistLogFileNames();
				if (existLogFileNames.Length > controlFileQuantity)
				{
					List<FileInfo> list = new List<FileInfo>();
					for (int i = 0; i < existLogFileNames.Length; i++)
					{
						list.Add(new FileInfo(existLogFileNames[i]));
					}
					list.Sort((FileInfo m, FileInfo n) => m.CreationTime.CompareTo(n.CreationTime));
					for (int j = 0; j < list.Count - controlFileQuantity; j++)
					{
						File.Delete(list[j].FullName);
					}
				}
			}
			catch
			{
			}
		}

		/// <summary>
		/// 返回所有的日志文件名称，返回一个列表<br />
		/// Returns all log file names, returns a list
		/// </summary>
		/// <returns>所有的日志文件信息</returns>
		public string[] GetExistLogFileNames()
		{
			if (!string.IsNullOrEmpty(filePath))
			{
				return Directory.GetFiles(filePath, "Logs_*.txt");
			}
			return new string[0];
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "LogPathBase";
		}
	}
}
