using System;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;

namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// 文件或是文件夹的信息
	/// </summary>
	public class FileDirInfo
	{
		/// <summary>
		/// 是否为文件夹，True就是文件夹，False就是文件
		/// </summary>
		public bool IsDirectory { get; set; }

		/// <summary>
		/// 文件或是文件夹的名称
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// 最后一次更新时间，当为文件的时候有效
		/// </summary>
		public DateTime LastModified { get; set; }

		/// <summary>
		/// 文件的大小，当为文件的时候有效
		/// </summary>
		public int Size { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public FileDirInfo()
		{
		}

		/// <summary>
		/// 使用原始字节来实例化对象
		/// </summary>
		/// <param name="byteTransform">字节变换对象</param>
		/// <param name="buffer">原始的字节信息</param>
		/// <param name="index">起始的索引信息</param>
		public FileDirInfo(IByteTransform byteTransform, byte[] buffer, int index)
		{
			IsDirectory = byteTransform.TransInt16(buffer, index) == 0;
			Name = buffer.GetStringOrEndChar(index + 28, 36, Encoding.ASCII);
			if (!IsDirectory)
			{
				LastModified = new DateTime(byteTransform.TransInt16(buffer, index + 2), byteTransform.TransInt16(buffer, index + 4), byteTransform.TransInt16(buffer, index + 6), byteTransform.TransInt16(buffer, index + 8), byteTransform.TransInt16(buffer, index + 10), byteTransform.TransInt16(buffer, index + 12));
				Size = byteTransform.TransInt32(buffer, index + 20);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(IsDirectory ? "[PATH]   " : "[FILE]   ");
			stringBuilder.Append(Name.PadRight(40));
			if (!IsDirectory)
			{
				stringBuilder.Append("     ");
				stringBuilder.Append(LastModified.ToString("yyyy-MM-dd HH:mm:ss"));
				stringBuilder.Append("         ");
				stringBuilder.Append(SoftBasic.GetSizeDescription(Size));
			}
			return stringBuilder.ToString();
		}
	}
}
