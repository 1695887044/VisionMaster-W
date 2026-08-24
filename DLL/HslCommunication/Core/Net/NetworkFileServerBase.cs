using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 文件服务器类的基类，为直接映射文件模式和间接映射文件模式提供基础的方法支持，主要包含了对文件的一些操作的功能<br />
	/// The base class of the file server class, which provides basic method support for the direct mapping file mode and the indirect mapping file mode, and mainly includes the functions of some operations on files
	/// </summary>
	public class NetworkFileServerBase : NetworkServerBase
	{
		/// <summary>
		/// 文件上传的委托
		/// </summary>
		/// <param name="fileInfo">文件的基本信息</param>
		public delegate void FileUploadDelegate(FileServerInfo fileInfo);

		private readonly Dictionary<string, FileMarkId> dictionaryFilesMarks;

		private readonly object dictHybirdLock;

		private string m_FilesDirectoryPath = null;

		/// <summary>
		/// 文件所存储的路径
		/// </summary>
		public string FilesDirectoryPath
		{
			get
			{
				return m_FilesDirectoryPath;
			}
			set
			{
				m_FilesDirectoryPath = value;
			}
		}

		/// <summary>
		/// 获取当前的文件标记的对象数量<br />
		/// Get the number of objects marked by the current file
		/// </summary>
		public int FileMarkIdCount => dictionaryFilesMarks.Count;

		/// <inheritdoc cref="F:HslCommunication.Core.Net.NetworkBase.fileCacheSize" />
		public int FileCacheSize
		{
			get
			{
				return fileCacheSize;
			}
			set
			{
				fileCacheSize = value;
			}
		}

		/// <summary>
		/// 文件上传的事件，当文件上传的时候触发。
		/// </summary>
		public event FileUploadDelegate OnFileUploadEvent;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public NetworkFileServerBase()
		{
			dictionaryFilesMarks = new Dictionary<string, FileMarkId>(100);
			dictHybirdLock = new object();
		}

		/// <summary>
		/// 获取当前文件的读写锁，如果没有会自动创建，文件名应该是guid文件名，例如 b35a11ec533147ca80c7f7d1713f015b7909<br />
		/// Acquire the read-write lock of the current file. If not, it will be created automatically. 
		/// The file name should be the guid file name, for example, b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="fileName">完整的文件路径</param>
		/// <returns>返回携带文件信息的读写锁</returns>
		protected FileMarkId GetFileMarksFromDictionaryWithFileName(string fileName)
		{
			FileMarkId fileMarkId;
			lock (dictHybirdLock)
			{
				if (dictionaryFilesMarks.ContainsKey(fileName))
				{
					fileMarkId = dictionaryFilesMarks[fileName];
				}
				else
				{
					fileMarkId = new FileMarkId(base.LogNet, fileName);
					dictionaryFilesMarks.Add(fileName, fileMarkId);
				}
			}
			return fileMarkId;
		}

		/// <summary>
		/// 接收本次操作的信息头数据
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult<FileGroupInfo> ReceiveInformationHead(Socket socket)
		{
			FileGroupInfo fileGroupInfo = new FileGroupInfo();
			OperateResult<byte[], byte[]> operateResult = ReceiveAndCheckBytes(socket, 30000);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(operateResult);
			}
			fileGroupInfo.Command = BitConverter.ToInt32(operateResult.Content1, 4);
			switch (BitConverter.ToInt32(operateResult.Content1, 0))
			{
			case 1001:
				fileGroupInfo.FileName = Encoding.Unicode.GetString(operateResult.Content2);
				break;
			case 1005:
				fileGroupInfo.FileNames = HslProtocol.UnPackStringArrayFromByte(operateResult.Content2);
				break;
			}
			OperateResult<int, string> operateResult2 = ReceiveStringContentFromSocket(socket);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(operateResult2);
			}
			fileGroupInfo.Factory = operateResult2.Content2;
			OperateResult<int, string> operateResult3 = ReceiveStringContentFromSocket(socket);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(operateResult3);
			}
			fileGroupInfo.Group = operateResult3.Content2;
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(socket);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(operateResult4);
			}
			fileGroupInfo.Identify = operateResult4.Content2;
			return OperateResult.CreateSuccessResult(fileGroupInfo);
		}

		/// <summary>
		/// 接收本次操作的信息头数据
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <returns>是否成功的结果对象</returns>
		protected async Task<OperateResult<FileGroupInfo>> ReceiveInformationHeadAsync(Socket socket)
		{
			FileGroupInfo ret = new FileGroupInfo();
			OperateResult<byte[], byte[]> receive = await ReceiveAndCheckBytesAsync(socket, 30000);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(receive);
			}
			ret.Command = BitConverter.ToInt32(receive.Content1, 4);
			switch (BitConverter.ToInt32(receive.Content1, 0))
			{
			case 1001:
				ret.FileName = Encoding.Unicode.GetString(receive.Content2);
				break;
			case 1005:
				ret.FileNames = HslProtocol.UnPackStringArrayFromByte(receive.Content2);
				break;
			}
			OperateResult<int, string> factoryResult = await ReceiveStringContentFromSocketAsync(socket);
			if (!factoryResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(factoryResult);
			}
			ret.Factory = factoryResult.Content2;
			OperateResult<int, string> groupResult = await ReceiveStringContentFromSocketAsync(socket);
			if (!groupResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(groupResult);
			}
			ret.Group = groupResult.Content2;
			OperateResult<int, string> idResult = await ReceiveStringContentFromSocketAsync(socket);
			if (!idResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileGroupInfo>(idResult);
			}
			ret.Identify = idResult.Content2;
			return OperateResult.CreateSuccessResult(ret);
		}

		/// <summary>
		/// 获取一个随机的文件名，由GUID码和随机数字组成
		/// </summary>
		/// <returns>文件名</returns>
		protected string CreateRandomFileName()
		{
			return SoftBasic.GetUniqueStringByGuidAndRandom();
		}

		/// <summary>
		/// 返回服务器的绝对路径，包含根目录的信息  [Root Dir][Factory][Group][Id] 信息
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		protected string ReturnAbsoluteFilePath(string factory, string group, string id)
		{
			string text = m_FilesDirectoryPath;
			if (!string.IsNullOrEmpty(factory))
			{
				text = text + "\\" + factory;
			}
			if (!string.IsNullOrEmpty(group))
			{
				text = text + "\\" + group;
			}
			if (!string.IsNullOrEmpty(id))
			{
				text = text + "\\" + id;
			}
			return text;
		}

		/// <summary>
		/// 返回服务器的绝对路径，包含根目录的信息  [Root Dir][Factory][Group][Id][FileName] 信息
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileName">文件名</param>
		/// <returns>是否成功的结果对象</returns>
		protected string ReturnAbsoluteFileName(string factory, string group, string id, string fileName)
		{
			return ReturnAbsoluteFilePath(factory, group, id) + "\\" + fileName;
		}

		/// <summary>
		/// 返回相对路径的名称
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileName">文件名</param>
		/// <returns>是否成功的结果对象</returns>
		protected string GetRelativeFileName(string factory, string group, string id, string fileName)
		{
			string text = "";
			if (!string.IsNullOrEmpty(factory))
			{
				text = text + factory + "\\";
			}
			if (!string.IsNullOrEmpty(group))
			{
				text = text + group + "\\";
			}
			if (!string.IsNullOrEmpty(id))
			{
				text = text + id + "\\";
			}
			return text + fileName;
		}

		/// <summary>
		/// 移动一个文件到新的文件去
		/// </summary>
		/// <param name="fileNameOld">旧的文件名称</param>
		/// <param name="fileNameNew">新的文件名称</param>
		/// <returns>是否成功</returns>
		protected bool MoveFileToNewFile(string fileNameOld, string fileNameNew)
		{
			try
			{
				FileInfo fileInfo = new FileInfo(fileNameNew);
				if (!Directory.Exists(fileInfo.DirectoryName))
				{
					Directory.CreateDirectory(fileInfo.DirectoryName);
				}
				if (File.Exists(fileNameNew))
				{
					File.Delete(fileNameNew);
				}
				File.Move(fileNameOld, fileNameNew);
				return true;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), "Move a file to new file failed: ", ex);
				return false;
			}
		}

		/// <summary>
		/// 删除文件并回发确认信息，如果结果异常，则结束通讯
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="fullname">完整路径的文件名称</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DeleteFileAndCheck(Socket socket, string fullname)
		{
			int customer = 0;
			int num = 0;
			while (num < 3)
			{
				num++;
				if (DeleteFileByName(fullname))
				{
					customer = 1;
					break;
				}
				HslHelper.ThreadSleep(500);
			}
			return SendStringAndCheckReceive(socket, customer, StringResources.Language.SuccessText);
		}

		/// <summary>
		/// 触发一个文件上传的事件。
		/// </summary>
		/// <param name="fileInfo">文件的基本信息</param>
		protected void OnFileUpload(FileServerInfo fileInfo)
		{
			this.OnFileUploadEvent?.Invoke(fileInfo);
		}

		/// <summary>
		/// 服务器启动时的操作
		/// </summary>
		protected override void StartInitialization()
		{
			if (string.IsNullOrEmpty(FilesDirectoryPath))
			{
				throw new ArgumentNullException("FilesDirectoryPath", "No saved path is specified");
			}
			CheckFolderAndCreate();
			base.StartInitialization();
		}

		/// <summary>
		/// 检查文件夹是否存在，不存在就创建
		/// </summary>
		protected virtual void CheckFolderAndCreate()
		{
			if (!Directory.Exists(FilesDirectoryPath))
			{
				Directory.CreateDirectory(FilesDirectoryPath);
			}
		}

		/// <summary>
		/// 获取文件夹的所有文件列表
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>文件列表</returns>
		public virtual string[] GetDirectoryFiles(string factory, string group, string id)
		{
			if (string.IsNullOrEmpty(FilesDirectoryPath))
			{
				return new string[0];
			}
			string path = ReturnAbsoluteFilePath(factory, group, id);
			if (!Directory.Exists(path))
			{
				return new string[0];
			}
			return Directory.GetFiles(path);
		}

		/// <summary>
		/// 获取文件夹的所有文件夹列表
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>文件夹列表</returns>
		public string[] GetDirectories(string factory, string group, string id)
		{
			if (string.IsNullOrEmpty(FilesDirectoryPath))
			{
				return new string[0];
			}
			string path = ReturnAbsoluteFilePath(factory, group, id);
			if (!Directory.Exists(path))
			{
				return new string[0];
			}
			return Directory.GetDirectories(path);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "NetworkFileServerBase";
		}
	}
}
