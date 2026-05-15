using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 一个终极文件管理服务器，可以实现对所有的文件分类管理，本服务器支持读写分离，支持同名文件，
	/// 客户端使用<see cref="T:HslCommunication.Enthernet.IntegrationFileClient" />进行访问，支持上传，下载，删除，请求文件列表，校验文件是否存在操作。<br />
	/// An ultimate file management server, which can realize classified management of all files. This server supports read-write separation, 
	/// supports files with the same name, and the client uses <see cref="T:HslCommunication.Enthernet.IntegrationFileClient" /> to access, 
	/// supports upload, download, delete, and request files List, check whether the file exists operation.
	/// </summary>
	/// <remarks>
	/// 本文件的服务器支持存储文件携带上传人的信息，备注信息，文件名被映射成了新的名称，无法在服务器直接查看文件信息。
	/// </remarks>
	/// <example>
	/// 以下的示例来自Demo项目，创建了一个简单的服务器对象。
	/// <code lang="cs" source="TestProject\FileNetServer\FormFileServer.cs" region="Ultimate Server" title="UltimateFileServer示例" />
	/// </example>
	public class UltimateFileServer : NetworkFileServerBase
	{
		/// <summary>
		/// 所有文件组操作的词典锁
		/// </summary>
		internal Dictionary<string, GroupFileContainer> m_dictionary_group_marks = new Dictionary<string, GroupFileContainer>();

		/// <summary>
		/// 词典的锁
		/// </summary>
		private SimpleHybirdLock hybirdLock = new SimpleHybirdLock();

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public UltimateFileServer()
		{
		}

		/// <summary>
		/// 获取当前的针对文件夹的文件管理容器的数量<br />
		/// Get the current number of file management containers for the folder
		/// </summary>
		[HslMqttApi(Description = "Get the current number of file management containers for the folder")]
		public int GroupFileContainerCount()
		{
			return m_dictionary_group_marks.Count;
		}

		/// <summary>
		/// 获取当前目录的文件列表管理容器，如果没有会自动创建，通过该容器可以实现对当前目录的文件进行访问<br />
		/// Get the file list management container of the current directory. If not, it will be created automatically. 
		/// Through this container, you can access files in the current directory.
		/// </summary>
		/// <param name="filePath">路径信息</param>
		/// <returns>文件管理容器信息</returns>
		public GroupFileContainer GetGroupFromFilePath(string filePath)
		{
			GroupFileContainer groupFileContainer = null;
			filePath = filePath.ToUpper();
			hybirdLock.Enter();
			if (m_dictionary_group_marks.ContainsKey(filePath))
			{
				groupFileContainer = m_dictionary_group_marks[filePath];
			}
			else
			{
				groupFileContainer = new GroupFileContainer(base.LogNet, filePath);
				m_dictionary_group_marks.Add(filePath, groupFileContainer);
			}
			hybirdLock.Leave();
			return groupFileContainer;
		}

		/// <summary>
		/// 清除系统中所有空的路径信息
		/// </summary>
		public void DeleteGroupFile(GroupFileContainer groupFile)
		{
			hybirdLock.Enter();
			if (m_dictionary_group_marks.ContainsKey(groupFile.DirectoryPath))
			{
				m_dictionary_group_marks.Remove(groupFile.DirectoryPath);
			}
			try
			{
				Directory.Delete(groupFile.DirectoryPath, recursive: true);
			}
			catch
			{
			}
			hybirdLock.Leave();
		}

		/// <summary>
		/// 从套接字接收文件并保存，更新文件列表
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="savename">保存的文件名</param>
		/// <returns>是否成功的结果对象</returns>
		private OperateResult<FileBaseInfo> ReceiveFileFromSocketAndUpdateGroup(Socket socket, string savename)
		{
			FileInfo fileInfo = new FileInfo(savename);
			string text = CreateRandomFileName();
			string text2 = Path.Combine(fileInfo.DirectoryName, text);
			OperateResult<FileBaseInfo> operateResult = ReceiveFileFromSocket(socket, text2, null);
			if (!operateResult.IsSuccess)
			{
				DeleteFileByName(text2);
				return operateResult;
			}
			GroupFileContainer groupFromFilePath = GetGroupFromFilePath(fileInfo.DirectoryName);
			string fileName = groupFromFilePath.UpdateFileMappingName(fileInfo.Name, operateResult.Content.Size, text, operateResult.Content.Upload, operateResult.Content.Tag);
			DeleteExsistingFile(fileInfo.DirectoryName, fileName);
			OperateResult operateResult2 = SendStringAndCheckReceive(socket, 1, StringResources.Language.SuccessText);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content);
		}

		/// <summary>
		/// 从套接字接收文件并保存，更新文件列表
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="savename">保存的文件名</param>
		/// <returns>是否成功的结果对象</returns>
		private async Task<OperateResult<FileBaseInfo>> ReceiveFileFromSocketAndUpdateGroupAsync(Socket socket, string savename)
		{
			FileInfo info = new FileInfo(savename);
			string guidName = CreateRandomFileName();
			string fileName = Path.Combine(info.DirectoryName, guidName);
			OperateResult<FileBaseInfo> receive = await ReceiveFileFromSocketAsync(socket, fileName, null);
			if (!receive.IsSuccess)
			{
				DeleteFileByName(fileName);
				return receive;
			}
			GroupFileContainer fileManagment = GetGroupFromFilePath(info.DirectoryName);
			DeleteExsistingFile(fileName: fileManagment.UpdateFileMappingName(info.Name, receive.Content.Size, guidName, receive.Content.Upload, receive.Content.Tag), path: info.DirectoryName);
			OperateResult sendBack = await SendStringAndCheckReceiveAsync(socket, 1, StringResources.Language.SuccessText);
			if (!sendBack.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(sendBack);
			}
			return OperateResult.CreateSuccessResult(receive.Content);
		}

		/// <summary>
		/// 根据文件的显示名称转化为真实存储的名称，例如 123.txt 获取到在文件服务器里映射的文件名称，例如返回 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileName">文件显示名称</param>
		/// <returns>是否成功的结果对象</returns>
		private string TransformFactFileName(string factory, string group, string id, string fileName)
		{
			string filePath = ReturnAbsoluteFilePath(factory, group, id);
			GroupFileContainer groupFromFilePath = GetGroupFromFilePath(filePath);
			return groupFromFilePath.GetCurrentFileMappingName(fileName);
		}

		/// <summary>
		/// 删除已经存在的文件信息，文件的名称需要是guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="path">文件的路径</param>
		/// <param name="fileName">文件的guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909</param>
		private void DeleteExsistingFile(string path, string fileName)
		{
			DeleteExsistingFile(path, new List<string> { fileName });
		}

		/// <summary>
		/// 删除已经存在的文件信息，文件的名称需要是guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909
		/// </summary>
		/// <param name="path">文件的路径</param>
		/// <param name="fileNames">文件的guid名称，例如 b35a11ec533147ca80c7f7d1713f015b7909</param>
		private void DeleteExsistingFile(string path, List<string> fileNames)
		{
			foreach (string fileName in fileNames)
			{
				if (string.IsNullOrEmpty(fileName))
				{
					continue;
				}
				string fileUltimatePath = Path.Combine(path, fileName);
				FileMarkId fileMarksFromDictionaryWithFileName = GetFileMarksFromDictionaryWithFileName(fileName);
				fileMarksFromDictionaryWithFileName.AddOperation(delegate
				{
					if (!DeleteFileByName(fileUltimatePath))
					{
						base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteFailed + fileUltimatePath);
					}
					else
					{
						base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteSuccess + fileUltimatePath);
					}
				});
			}
		}

		/// <inheritdoc />
		protected override async void ThreadPoolLogin(Socket socket, IPEndPoint endPoint)
		{
			string IpAddress = endPoint.Address.ToString();
			OperateResult<FileGroupInfo> infoResult = await ReceiveInformationHeadAsync(socket);
			if (!infoResult.IsSuccess)
			{
				return;
			}
			int customer = infoResult.Content.Command;
			string Factory = infoResult.Content.Factory;
			string Group = infoResult.Content.Group;
			string Identify = infoResult.Content.Identify;
			string fileName = infoResult.Content.FileName;
			string relativeName = GetRelativeFileName(Factory, Group, Identify, fileName);
			switch (customer)
			{
			case 2001:
			{
				string guidName = TransformFactFileName(Factory, Group, Identify, fileName);
				FileMarkId fileMarkId = GetFileMarksFromDictionaryWithFileName(guidName);
				fileMarkId.EnterReadOperator();
				OperateResult send = await SendFileAndCheckReceiveAsync(socket, ReturnAbsoluteFileName(Factory, Group, Identify, guidName), fileName, "", "");
				if (!send.IsSuccess)
				{
					fileMarkId.LeaveReadOperator();
					base.LogNet?.WriteError(ToString(), StringResources.Language.FileDownloadFailed + " : " + send.Message + " :" + relativeName + " ip:" + IpAddress);
				}
				else
				{
					base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDownloadSuccess + ":" + relativeName);
					fileMarkId.LeaveReadOperator();
					socket?.Close();
				}
				break;
			}
			case 2002:
			{
				string fullFileName3 = ReturnAbsoluteFileName(Factory, Group, Identify, fileName);
				CheckFolderAndCreate();
				FileInfo info3 = new FileInfo(fullFileName3);
				try
				{
					if (!Directory.Exists(info3.DirectoryName))
					{
						Directory.CreateDirectory(info3.DirectoryName);
					}
				}
				catch (Exception ex2)
				{
					Exception ex = ex2;
					base.LogNet?.WriteException(ToString(), StringResources.Language.FilePathCreateFailed + fullFileName3, ex);
					socket?.Close();
					return;
				}
				OperateResult<FileBaseInfo> receive = await ReceiveFileFromSocketAndUpdateGroupAsync(socket, fullFileName3);
				if (receive.IsSuccess)
				{
					socket?.Close();
					OnFileUpload(new FileServerInfo
					{
						ActualFileFullName = fullFileName3,
						Name = receive.Content.Name,
						Size = receive.Content.Size,
						Tag = receive.Content.Tag,
						Upload = receive.Content.Upload
					});
					base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileUploadSuccess + ":" + relativeName);
				}
				else
				{
					base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileUploadFailed + ":" + relativeName);
				}
				break;
			}
			case 2003:
			{
				string fullFileName4 = ReturnAbsoluteFileName(Factory, Group, Identify, fileName);
				FileInfo info4 = new FileInfo(fullFileName4);
				DeleteExsistingFile(fileName: GetGroupFromFilePath(info4.DirectoryName).DeleteFile(info4.Name), path: info4.DirectoryName);
				if ((await SendStringAndCheckReceiveAsync(socket, 1, "success")).IsSuccess)
				{
					socket?.Close();
				}
				base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteSuccess + ":" + relativeName);
				break;
			}
			case 2011:
			{
				string[] fileNames = infoResult.Content.FileNames;
				foreach (string item in fileNames)
				{
					string fullFileName2 = ReturnAbsoluteFileName(Factory, Group, Identify, item);
					FileInfo info2 = new FileInfo(fullFileName2);
					DeleteExsistingFile(fileName: GetGroupFromFilePath(info2.DirectoryName).DeleteFile(info2.Name), path: info2.DirectoryName);
					relativeName = GetRelativeFileName(Factory, Group, Identify, fileName);
					base.LogNet?.WriteInfo(ToString(), StringResources.Language.FileDeleteSuccess + ":" + relativeName);
				}
				if ((await SendStringAndCheckReceiveAsync(socket, 1, "success")).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			case 2012:
			{
				string fullFileName = ReturnAbsoluteFileName(Factory, Group, Identify, "123.txt");
				FileInfo info = new FileInfo(fullFileName);
				GroupFileContainer fileManagment8 = GetGroupFromFilePath(info.DirectoryName);
				DeleteGroupFile(fileManagment8);
				if ((await SendStringAndCheckReceiveAsync(socket, 1, "success")).IsSuccess)
				{
					socket?.Close();
				}
				base.LogNet?.WriteInfo(ToString(), "FolderDelete : " + relativeName);
				break;
			}
			case 2014:
			{
				string[] directories2 = GetDirectories(Factory, Group, Identify);
				foreach (string k in directories2)
				{
					DirectoryInfo directory3 = new DirectoryInfo(k);
					GroupFileContainer fileManagment7 = GetGroupFromFilePath(directory3.FullName);
					if (fileManagment7.FileCount == 0)
					{
						DeleteGroupFile(fileManagment7);
					}
				}
				if ((await SendStringAndCheckReceiveAsync(socket, 1, "success")).IsSuccess)
				{
					socket?.Close();
				}
				base.LogNet?.WriteInfo(ToString(), "FolderEmptyDelete : " + relativeName);
				break;
			}
			case 2007:
			{
				GroupFileContainer fileManagment6 = GetGroupFromFilePath(ReturnAbsoluteFilePath(Factory, Group, Identify));
				if ((await SendStringAndCheckReceiveAsync(socket, 2007, fileManagment6.JsonArrayContent)).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			case 2015:
			{
				GroupFileContainer fileManagment5 = GetGroupFromFilePath(ReturnAbsoluteFilePath(Factory, Group, Identify));
				if ((await SendStringAndCheckReceiveAsync(socket, 2007, fileManagment5.GetGroupFileInfo().ToJsonString())).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			case 2016:
			{
				List<GroupFileInfo> folders2 = new List<GroupFileInfo>();
				string[] directories = GetDirectories(Factory, Group, Identify);
				foreach (string j in directories)
				{
					DirectoryInfo directory2 = new DirectoryInfo(j);
					if (string.IsNullOrEmpty(Factory))
					{
						GroupFileContainer fileManagment4 = GetGroupFromFilePath(ReturnAbsoluteFilePath(directory2.Name, string.Empty, string.Empty));
						GroupFileInfo groupFileInfo3 = fileManagment4.GetGroupFileInfo();
						groupFileInfo3.PathName = directory2.Name;
						folders2.Add(groupFileInfo3);
					}
					else if (string.IsNullOrEmpty(Group))
					{
						GroupFileContainer fileManagment3 = GetGroupFromFilePath(ReturnAbsoluteFilePath(Factory, directory2.Name, string.Empty));
						GroupFileInfo groupFileInfo2 = fileManagment3.GetGroupFileInfo();
						groupFileInfo2.PathName = directory2.Name;
						folders2.Add(groupFileInfo2);
					}
					else
					{
						GroupFileContainer fileManagment2 = GetGroupFromFilePath(ReturnAbsoluteFilePath(Factory, Group, directory2.Name));
						GroupFileInfo groupFileInfo = fileManagment2.GetGroupFileInfo();
						groupFileInfo.PathName = directory2.Name;
						folders2.Add(groupFileInfo);
					}
				}
				if ((await SendStringAndCheckReceiveAsync(socket, 2016, folders2.ToJsonString())).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			case 2008:
			{
				List<string> folders = new List<string>();
				string[] directories3 = GetDirectories(Factory, Group, Identify);
				foreach (string i in directories3)
				{
					DirectoryInfo directory = new DirectoryInfo(i);
					folders.Add(directory.Name);
				}
				JArray jArray = JArray.FromObject(folders.ToArray());
				if ((await SendStringAndCheckReceiveAsync(socket, 2015, jArray.ToString())).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			case 2013:
			{
				string fullPath = ReturnAbsoluteFilePath(Factory, Group, Identify);
				GroupFileContainer fileManagment = GetGroupFromFilePath(fullPath);
				bool isExists = fileManagment.FileExists(fileName);
				if ((await SendStringAndCheckReceiveAsync(socket, isExists ? 1 : 0, StringResources.Language.FileNotExist)).IsSuccess)
				{
					socket?.Close();
				}
				break;
			}
			default:
				socket?.Close();
				break;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"UltimateFileServer[{base.Port}]";
		}
	}
}
