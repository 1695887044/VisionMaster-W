using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 与服务器文件引擎交互的客户端类，支持操作Advanced引擎和Ultimate引擎，用来上传，下载，删除服务器中的文件操作。<br />
	/// The client class that interacts with the server file engine, supports the operation of the Advanced engine and the Ultimate engine,
	/// and is used to upload, download, and delete file operations on the server.
	/// </summary>
	/// <remarks>
	/// 这里需要需要的是，本客户端支持Advanced引擎和Ultimate引擎文件服务器，服务的类型需要您根据自己的需求来选择。
	/// <note type="important">需要注意的是，三个分类信息，factory, group, id 的字符串是不区分大小写的。</note>
	/// </remarks>
	/// <example>
	/// 此处只演示创建实例，具体的上传，下载，删除的例子请参照对应的方法
	/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="Intergration File Client" title="IntegrationFileClient示例" />
	/// </example>
	public class IntegrationFileClient : FileClientBase
	{
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
		/// 实例化一个默认的对象，需要提前指定服务器的远程地址<br />
		/// Instantiate a default object, you need to specify the remote address of the server in advance
		/// </summary>
		public IntegrationFileClient()
		{
		}

		/// <summary>
		/// 通过指定的Ip地址及端口号实例化一个对象<br />
		/// Instantiate an object with the specified IP address and port number
		/// </summary>
		/// <param name="ipAddress">服务器的ip地址</param>
		/// <param name="port">端口号信息</param>
		public IntegrationFileClient(string ipAddress, int port)
		{
			base.ServerIpEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
		}

		/// <summary>
		/// 删除服务器的文件操作，需要指定文件名称，文件的三级分类信息<br />
		/// Delete the file operation of the server, you need to specify the file name and the three-level classification information of the file
		/// </summary>
		/// <param name="fileName">文件名称，带后缀</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		[HslMqttApi(ApiTopic = "DeleteFileFactoryGroupId", Description = "Delete the file operation of the server, you need to specify the file name and the three-level classification information of the file")]
		public OperateResult DeleteFile(string fileName, string factory, string group, string id)
		{
			return DeleteFileBase(fileName, factory, group, id);
		}

		/// <summary>
		/// 删除服务器的文件操作，此处文件的分类为空<br />
		/// Delete the file operation of the server, the classification of the file is empty here
		/// </summary>
		/// <param name="fileName">文件名称，带后缀</param>
		/// <returns>是否成功的结果对象</returns>
		[HslMqttApi(Description = "Delete the file operation of the server, the classification of the file is empty here")]
		public OperateResult DeleteFile(string fileName)
		{
			return DeleteFileBase(fileName, "", "", "");
		}

		/// <summary>
		/// 删除服务器的文件数组操作，需要指定文件名称，文件的三级分类信息<br />
		/// Delete the file operation of the server, you need to specify the file names and the three-level classification information of the file
		/// </summary>
		/// <param name="fileNames">文件名称数组，带后缀</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		[HslMqttApi(ApiTopic = "DeleteFilesFactoryGroupId", Description = "Delete the file operation of the server, you need to specify the file names and the three-level classification information of the file")]
		public OperateResult DeleteFile(string[] fileNames, string factory, string group, string id)
		{
			return DeleteFileBase(fileNames, factory, group, id);
		}

		/// <summary>
		/// 删除服务器的文件夹的所有文件操作，文件的三级分类信息<br />
		/// Delete all file operations of the server folder, the three-level classification information of the file
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		[HslMqttApi(Description = "Delete all file operations of the server folder, the three-level classification information of the file")]
		public OperateResult DeleteFolderFiles(string factory, string group, string id)
		{
			return DeleteFolderBase(factory, group, id);
		}

		/// <summary>
		/// 删除服务器的文件夹的所有空的子文件目录操作，需要传入文件的三级分类信息<br />
		/// To delete all empty sub-file directories of the server's folder, you need to pass in the three-level classification information of the file
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		[HslMqttApi(Description = "To delete all empty sub-file directories of the server's folder, you need to pass in the three-level classification information of the file")]
		public OperateResult DeleteEmptyFolders(string factory, string group, string id)
		{
			return DeleteEmptyFoldersBase(factory, group, id);
		}

		/// <summary>
		/// 获取服务器文件夹的指定目录的文件统计信息，包括文件数量，总大小，最后更新时间<br />
		/// Get the file statistics of the specified directory of the server folder, including the number of files, the total size, and the last update time
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>返回路径的信息，包含文件大小，数量，最后更新时间</returns>
		[HslMqttApi(Description = "Get the file statistics of the specified directory of the server folder, including the number of files, the total size, and the last update time")]
		public OperateResult<GroupFileInfo> GetGroupFileInfo(string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(operateResult);
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2016, "");
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(operateResult2);
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(operateResult3);
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(operateResult4);
			}
			return OperateResult.CreateSuccessResult(JObject.Parse(operateResult4.Content2).ToObject<GroupFileInfo>());
		}

		/// <summary>
		/// 获取服务器文件夹的指定目录的所有子目录的文件信息，包括每个子目录的文件数量，总大小，最后更新时间<br />
		/// Get the file information of all subdirectories of the specified directory of the server folder, including the number of files in each subdirectory, the total size, and the last update time
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>返回路径的信息，包含文件大小，数量，最后更新时间</returns>
		[HslMqttApi(Description = "Get the file information of all subdirectories of the specified directory of the server folder, including the number of files in each subdirectory, the total size, and the last update time")]
		public OperateResult<GroupFileInfo[]> GetSubGroupFileInfos(string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(operateResult);
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2016, "");
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(operateResult2);
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(operateResult3);
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(operateResult4);
			}
			return OperateResult.CreateSuccessResult(JArray.Parse(operateResult4.Content2).ToObject<GroupFileInfo[]>());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DeleteFile(System.String,System.String,System.String,System.String)" />
		public async Task<OperateResult> DeleteFileAsync(string fileName, string factory, string group, string id)
		{
			return await DeleteFileBaseAsync(fileName, factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DeleteFile(System.String)" />
		public async Task<OperateResult> DeleteFileAsync(string fileName)
		{
			return await DeleteFileBaseAsync(fileName, "", "", "");
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DeleteFile(System.String[],System.String,System.String,System.String)" />
		public async Task<OperateResult> DeleteFileAsync(string[] fileNames, string factory, string group, string id)
		{
			return await DeleteFileBaseAsync(fileNames, factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DeleteFolderFiles(System.String,System.String,System.String)" />
		public async Task<OperateResult> DeleteFolderFilesAsync(string factory, string group, string id)
		{
			return await DeleteFolderBaseAsync(factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DeleteEmptyFolders(System.String,System.String,System.String)" />
		public async Task<OperateResult> DeleteEmptyFoldersAsync(string factory, string group, string id)
		{
			return await DeleteEmptyFoldersBaseAsync(factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.GetGroupFileInfo(System.String,System.String,System.String)" />
		public async Task<OperateResult<GroupFileInfo>> GetGroupFileInfoAsync(string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(socketResult);
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2015, "");
			if (!sendString.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(sendString);
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(sendFileInfo);
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo>(receiveBack);
			}
			return OperateResult.CreateSuccessResult(JObject.Parse(receiveBack.Content2).ToObject<GroupFileInfo>());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.GetGroupFileInfo(System.String,System.String,System.String)" />
		public async Task<OperateResult<GroupFileInfo[]>> GetSubGroupFileInfosAsync(string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(socketResult);
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2016, "");
			if (!sendString.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(sendString);
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(sendFileInfo);
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return OperateResult.CreateFailedResult<GroupFileInfo[]>(receiveBack);
			}
			return OperateResult.CreateSuccessResult(JArray.Parse(receiveBack.Content2).ToObject<GroupFileInfo[]>());
		}

		/// <summary>
		/// 下载服务器的文件到本地的文件操作，需要指定下载的文件的名字，三级分类信息，本次保存的文件名，支持进度报告。<br />
		/// To download a file from the server to a local file, you need to specify the name of the downloaded file, 
		/// the three-level classification information, the name of the file saved this time, and support for progress reports.
		/// </summary>
		/// <param name="fileName">文件名称，带后缀</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="processReport">下载的进度报告，第一个数据是已完成总接字节数，第二个数据是总字节数。</param>
		/// <param name="fileSaveName">准备本地保存的名称</param>
		/// <returns>是否成功的结果对象</returns>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常，或是服务器不存在文件。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="Download File" title="DownloadFile示例" />
		/// </example>
		[HslMqttApi(Description = "To download a file from the server to a local file, you need to specify the name of the downloaded file and three-level classification information")]
		public OperateResult DownloadFile(string fileName, string factory, string group, string id, Action<long, long> processReport, string fileSaveName)
		{
			return DownloadFileBase(factory, group, id, fileName, processReport, fileSaveName);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},System.String)" />
		public OperateResult DownloadFile(string fileName, string factory, string group, string id, Action<long, long> processReport, Stream stream)
		{
			return DownloadFileBase(factory, group, id, fileName, processReport, stream);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},System.String)" />
		public OperateResult<Bitmap> DownloadFile(string fileName, string factory, string group, string id, Action<long, long> processReport)
		{
			MemoryStream memoryStream = new MemoryStream();
			Bitmap bitmap = null;
			OperateResult operateResult = DownloadFileBase(factory, group, id, fileName, processReport, memoryStream);
			if (!operateResult.IsSuccess)
			{
				memoryStream.Dispose();
				return OperateResult.CreateFailedResult<Bitmap>(operateResult);
			}
			bitmap = new Bitmap(memoryStream);
			return OperateResult.CreateSuccessResult(bitmap);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},System.String)" />
		public async Task<OperateResult> DownloadFileAsync(string fileName, string factory, string group, string id, Action<long, long> processReport, string fileSaveName)
		{
			return await DownloadFileBaseAsync(factory, group, id, fileName, processReport, fileSaveName);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},System.IO.Stream)" />
		public async Task<OperateResult> DownloadFileAsync(string fileName, string factory, string group, string id, Action<long, long> processReport, Stream stream)
		{
			return await DownloadFileBaseAsync(factory, group, id, fileName, processReport, stream);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult<Bitmap>> DownloadFileAsync(string fileName, string factory, string group, string id, Action<long, long> processReport)
		{
			MemoryStream stream = new MemoryStream();
			OperateResult result = await DownloadFileBaseAsync(factory, group, id, fileName, processReport, stream);
			if (!result.IsSuccess)
			{
				stream.Dispose();
				return OperateResult.CreateFailedResult<Bitmap>(result);
			}
			Bitmap bitmap = new Bitmap(stream);
			stream.Dispose();
			return OperateResult.CreateSuccessResult(bitmap);
		}

		/// <summary>
		/// 上传本地的文件到服务器操作，如果该文件已经存在，那么就更新这个文件。<br />
		/// Upload a local file to the server. If the file already exists, update the file.
		/// </summary>
		/// <param name="fileName">本地的完整路径的文件名称</param>
		/// <param name="serverName">服务器存储的文件名称，带后缀，例如123.txt</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileTag">文件的额外描述</param>
		/// <param name="fileUpload">文件的上传人</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常，或是客户端不存在文件。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="Upload File" title="UploadFile示例" />
		/// </example>
		[HslMqttApi(Description = "Upload a local file to the server. If the file already exists, update the file.")]
		public OperateResult UploadFile(string fileName, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			return UploadFileBase(fileName, serverName, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <summary>
		/// 上传本地的文件到服务器操作，服务器存储的文件名就是当前文件默认的名称
		/// </summary>
		/// <param name="fileName">本地的完整路径的文件名称</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileTag">文件的额外描述</param>
		/// <param name="fileUpload">文件的上传人</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult UploadFile(string fileName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return UploadFileBase(fileName, fileInfo.Name, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <summary>
		/// 上传本地的文件到服务器操作，服务器存储的文件名就是当前文件默认的名称
		/// </summary>
		/// <param name="fileName">本地的完整路径的文件名称</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult UploadFile(string fileName, string factory, string group, string id, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return UploadFileBase(fileName, fileInfo.Name, factory, group, id, "", "", processReport);
		}

		/// <summary>
		/// 上传本地的文件到服务器操作，服务器存储的文件名就是当前文件默认的名称，其余参数默认为空
		/// </summary>
		/// <param name="fileName">本地的完整路径的文件名称</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult UploadFile(string fileName, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return UploadFileBase(fileName, fileInfo.Name, "", "", "", "", "", processReport);
		}

		/// <summary>
		/// 上传数据流到服务器操作
		/// </summary>
		/// <param name="stream">数据流内容</param>
		/// <param name="serverName">服务器存储的文件名称，带后缀</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileTag">文件的额外描述</param>
		/// <param name="fileUpload">文件的上传人</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常，或是客户端不存在文件。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="Upload File" title="UploadFile示例" />
		/// </example>
		public OperateResult UploadFile(Stream stream, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			return UploadFileBase(stream, serverName, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <summary>
		/// 上传内存图片到服务器操作
		/// </summary>
		/// <param name="bitmap">内存图片，不能为空</param>
		/// <param name="serverName">服务器存储的文件名称，带后缀</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <param name="fileTag">文件的额外描述</param>
		/// <param name="fileUpload">文件的上传人</param>
		/// <param name="processReport">上传的进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		/// <exception cref="T:System.ArgumentNullException"></exception>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常，或是客户端不存在文件。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="Upload File" title="UploadFile示例" />
		/// </example>
		public OperateResult UploadFile(Bitmap bitmap, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			MemoryStream memoryStream = new MemoryStream();
			if (bitmap.RawFormat != null)
			{
				bitmap.Save(memoryStream, bitmap.RawFormat);
			}
			else
			{
				bitmap.Save(memoryStream, ImageFormat.Bmp);
			}
			OperateResult result = UploadFileBase(memoryStream, serverName, factory, group, id, fileTag, fileUpload, processReport);
			memoryStream.Dispose();
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.String,System.String,System.String,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(string fileName, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			return await UploadFileBaseAsync(fileName, serverName, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.String,System.String,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(string fileName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return await UploadFileBaseAsync(fileName, fileInfo.Name, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(string fileName, string factory, string group, string id, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return await UploadFileBaseAsync(fileName, fileInfo.Name, factory, group, id, "", "", processReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(string fileName, Action<long, long> processReport)
		{
			if (!File.Exists(fileName))
			{
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			FileInfo fileInfo = new FileInfo(fileName);
			return await UploadFileBaseAsync(fileName, fileInfo.Name, "", "", "", "", "", processReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.IO.Stream,System.String,System.String,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(Stream stream, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			return await UploadFileBaseAsync(stream, serverName, factory, group, id, fileTag, fileUpload, processReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.UploadFile(System.Drawing.Bitmap,System.String,System.String,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		public async Task<OperateResult> UploadFileAsync(Bitmap bitmap, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			MemoryStream stream = new MemoryStream();
			if (bitmap.RawFormat != null)
			{
				bitmap.Save(stream, bitmap.RawFormat);
			}
			else
			{
				bitmap.Save(stream, ImageFormat.Bmp);
			}
			OperateResult result = await UploadFileBaseAsync(stream, serverName, factory, group, id, fileTag, fileUpload, processReport);
			stream.Dispose();
			return result;
		}

		/// <summary>
		/// 获取指定路径下的所有的文档<br />
		/// Get all documents in the specified path
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="DownloadPathFileNames" title="DownloadPathFileNames示例" />
		/// </example>
		[HslMqttApi(Description = "Get all documents in the specified path")]
		public OperateResult<GroupFileItem[]> DownloadPathFileNames(string factory, string group, string id)
		{
			return DownloadStringArrays<GroupFileItem>(2007, factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadPathFileNames(System.String,System.String,System.String)" />
		public async Task<OperateResult<GroupFileItem[]>> DownloadPathFileNamesAsync(string factory, string group, string id)
		{
			return await DownloadStringArraysAsync<GroupFileItem>(2007, factory, group, id);
		}

		/// <summary>
		/// 获取指定路径下的所有的目录<br />
		/// Get all directories under the specified path
		/// </summary>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <returns>是否成功的结果对象</returns>
		/// <remarks>
		/// 用于分类的参数<paramref name="factory" />，<paramref name="group" />，<paramref name="id" />中间不需要的可以为空，对应的是服务器上的路径系统。
		/// <br /><br />
		/// <note type="warning">
		/// 失败的原因大多数来自于网络的接收异常。
		/// </note>
		/// </remarks>
		/// <example>
		/// <code lang="cs" source="TestProject\HslCommunicationDemo\Hsl\FormFileClient.cs" region="DownloadPathFolders" title="DownloadPathFolders示例" />
		/// </example>
		[HslMqttApi(Description = "Get all directories under the specified path")]
		public OperateResult<string[]> DownloadPathFolders(string factory, string group, string id)
		{
			return DownloadStringArrays<string>(2008, factory, group, id);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadPathFolders(System.String,System.String,System.String)" />
		public async Task<OperateResult<string[]>> DownloadPathFoldersAsync(string factory, string group, string id)
		{
			return await DownloadStringArraysAsync<string>(2008, factory, group, id);
		}

		/// <summary>
		/// 检查当前的文件是否在服务器端存在，列表中需要存在文件的名称，映射的文件也需要存在。<br />
		/// Check whether the current file exists on the server side, the name of the file must exist in the list, and the mapped file must also exist.
		/// </summary>
		/// <param name="fileName">当前的文件名称，举例123.txt</param>
		/// <param name="factory">第一级分类信息</param>
		/// <param name="group">第二级分类信息</param>
		/// <param name="id">第三级分类信息</param>
		/// <returns>是否存在，存在返回true, 否则，返回false</returns>
		public OperateResult<bool> IsFileExists(string fileName, string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult);
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2013, fileName);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult2);
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult3);
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult4);
			}
			OperateResult<bool> result = OperateResult.CreateSuccessResult(operateResult4.Content1 == 1);
			operateResult.Content?.Close();
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.IsFileExists(System.String,System.String,System.String,System.String)" />
		public async Task<OperateResult<bool>> IsFileExistsAsync(string fileName, string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(socketResult);
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2013, fileName);
			if (!sendString.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(sendString);
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(sendFileInfo);
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(receiveBack);
			}
			OperateResult<bool> result = OperateResult.CreateSuccessResult(receiveBack.Content1 == 1);
			socketResult.Content?.Close();
			return result;
		}

		/// <summary>
		/// 获取指定路径下的所有的路径或是文档信息
		/// </summary>
		/// <param name="protocol">指令</param>
		/// <param name="factory">第一大类</param>
		/// <param name="group">第二大类</param>
		/// <param name="id">第三大类</param>
		/// <typeparam name="T">数组的类型</typeparam>
		/// <returns>是否成功的结果对象</returns>
		private OperateResult<T[]> DownloadStringArrays<T>(int protocol, string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(operateResult);
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, protocol, "nosense");
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(operateResult2);
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(operateResult3);
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(operateResult4);
			}
			operateResult.Content?.Close();
			try
			{
				return OperateResult.CreateSuccessResult(JArray.Parse(operateResult4.Content2).ToObject<T[]>());
			}
			catch (Exception ex)
			{
				return new OperateResult<T[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.IntegrationFileClient.DownloadStringArrays``1(System.Int32,System.String,System.String,System.String)" />
		private async Task<OperateResult<T[]>> DownloadStringArraysAsync<T>(int protocol, string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(base.ServerIpEndPoint, base.ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(socketResult);
			}
			OperateResult send = await SendStringAndCheckReceiveAsync(socketResult.Content, protocol, "nosense");
			if (!send.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(send);
			}
			OperateResult sendClass = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendClass.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(sendClass);
			}
			OperateResult<int, string> receive = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<T[]>(receive);
			}
			socketResult.Content?.Close();
			try
			{
				return OperateResult.CreateSuccessResult(JArray.Parse(receive.Content2).ToObject<T[]>());
			}
			catch (Exception ex)
			{
				return new OperateResult<T[]>(ex.Message);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"IntegrationFileClient[{base.ServerIpEndPoint}]";
		}
	}
}
