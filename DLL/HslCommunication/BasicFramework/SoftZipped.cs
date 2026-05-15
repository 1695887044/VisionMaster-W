using System;
using System.IO;
using System.IO.Compression;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 一个负责压缩解压数据字节的类
	/// </summary>
	public class SoftZipped
	{
		/// <summary>
		/// 压缩字节数据
		/// </summary>
		/// <param name="bytes">等待被压缩的数据</param>
		/// <exception cref="T:System.ArgumentNullException"></exception>
		/// <returns>压缩之后的字节数据</returns>
		public static byte[] CompressBytes(byte[] bytes)
		{
			if (bytes == null)
			{
				throw new ArgumentNullException("bytes");
			}
			using MemoryStream memoryStream = new MemoryStream();
			using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionMode.Compress))
			{
				gZipStream.Write(bytes, 0, bytes.Length);
			}
			return memoryStream.ToArray();
		}

		/// <summary>
		/// 解压压缩后的数据
		/// </summary>
		/// <param name="bytes">压缩后的数据</param>
		/// <exception cref="T:System.ArgumentNullException"></exception>
		/// <returns>压缩前的原始字节数据</returns>
		public static byte[] Decompress(byte[] bytes)
		{
			if (bytes == null)
			{
				throw new ArgumentNullException("bytes");
			}
			using MemoryStream stream = new MemoryStream(bytes);
			using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
			using MemoryStream memoryStream = new MemoryStream();
			int num = 1024;
			byte[] buffer = new byte[num];
			int num2 = 0;
			while ((num2 = gZipStream.Read(buffer, 0, num)) > 0)
			{
				memoryStream.Write(buffer, 0, num2);
			}
			return memoryStream.ToArray();
		}
	}
}
