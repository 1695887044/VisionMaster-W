using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 文件传输客户端基类，提供上传，下载，删除的基础服务<br />
	/// File transfer client base class, providing basic services for uploading, downloading, and deleting
	/// </summary>
	public abstract class FileClientBase : NetworkXBase
	{
		private IPEndPoint m_ipEndPoint = null;

		/// <summary>
		/// 文件管理服务器的ip地址及端口<br />
		/// IP address and port of the file management server
		/// </summary>
		public IPEndPoint ServerIpEndPoint
		{
			get
			{
				return m_ipEndPoint;
			}
			set
			{
				m_ipEndPoint = value;
			}
		}

		/// <summary>
		/// 获取或设置连接的超时时间，默认10秒<br />
		/// Gets or sets the connection timeout time. The default is 10 seconds.
		/// </summary>
		public int ConnectTimeOut { get; set; } = 10000;


		/// <summary>
		/// 发送三个文件分类信息到服务器端，方便后续开展其他的操作。<br />
		/// Send the three file classification information to the server to facilitate subsequent operations.
		/// </summary>
		/// <param name="socket">套接字对象</param>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult SendFactoryGroupId(Socket socket, string factory, string group, string id)
		{
			OperateResult operateResult = SendStringAndCheckReceive(socket, 1, factory);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(socket, 2, group);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendStringAndCheckReceive(socket, 3, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.SendFactoryGroupId(System.Net.Sockets.Socket,System.String,System.String,System.String)" />
		protected async Task<OperateResult> SendFactoryGroupIdAsync(Socket socket, string factory, string group, string id)
		{
			OperateResult factoryResult = await SendStringAndCheckReceiveAsync(socket, 1, factory);
			if (!factoryResult.IsSuccess)
			{
				return factoryResult;
			}
			OperateResult groupResult = await SendStringAndCheckReceiveAsync(socket, 2, group);
			if (!groupResult.IsSuccess)
			{
				return groupResult;
			}
			OperateResult idResult = await SendStringAndCheckReceiveAsync(socket, 3, id);
			if (!idResult.IsSuccess)
			{
				return idResult;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 删除服务器上的文件，需要传入文件信息，以及文件绑定的分类信息。<br />
		/// To delete a file on the server, you need to pass in the file information and the classification information of the file binding.
		/// </summary>
		/// <param name="fileName">文件的名称</param>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DeleteFileBase(string fileName, string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2003, fileName);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			OperateResult operateResult5 = new OperateResult();
			if (operateResult4.Content1 == 1)
			{
				operateResult5.IsSuccess = true;
			}
			operateResult5.Message = operateResult4.Message;
			operateResult.Content?.Close();
			return operateResult5;
		}

		/// <summary>
		/// 删除服务器上的文件列表，需要传入文件信息，以及文件绑定的分类信息。<br />
		/// To delete a file on the server, you need to pass in the file information and the classification information of the file binding.
		/// </summary>
		/// <param name="fileNames">所有等待删除的文件的名称</param>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DeleteFileBase(string[] fileNames, string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2011, fileNames);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			OperateResult operateResult5 = new OperateResult();
			if (operateResult4.Content1 == 1)
			{
				operateResult5.IsSuccess = true;
			}
			operateResult5.Message = operateResult4.Message;
			operateResult.Content?.Close();
			return operateResult5;
		}

		/// <summary>
		/// 删除服务器上的指定目录的所有文件，需要传入分类信息。<br />
		/// To delete all files in the specified directory on the server, you need to input classification information
		/// </summary>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DeleteFolderBase(string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2012, "");
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			OperateResult operateResult5 = new OperateResult();
			if (operateResult4.Content1 == 1)
			{
				operateResult5.IsSuccess = true;
			}
			operateResult5.Message = operateResult4.Message;
			operateResult.Content?.Close();
			return operateResult5;
		}

		/// <summary>
		/// 删除服务器上的指定目录的所有空文件目录，需要传入分类信息。<br />
		/// Delete all the empty file directories in the specified directory on the server, need to input classification information
		/// </summary>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DeleteEmptyFoldersBase(string factory, string group, string id)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2014, "");
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			OperateResult<int, string> operateResult4 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult4.IsSuccess)
			{
				return operateResult4;
			}
			OperateResult operateResult5 = new OperateResult();
			if (operateResult4.Content1 == 1)
			{
				operateResult5.IsSuccess = true;
			}
			operateResult5.Message = operateResult4.Message;
			operateResult.Content?.Close();
			return operateResult5;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.DeleteFileBase(System.String,System.String,System.String,System.String)" />
		protected async Task<OperateResult> DeleteFileBaseAsync(string fileName, string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2003, fileName);
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return sendFileInfo;
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return receiveBack;
			}
			OperateResult result = new OperateResult();
			if (receiveBack.Content1 == 1)
			{
				result.IsSuccess = true;
			}
			result.Message = receiveBack.Message;
			socketResult.Content?.Close();
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.DeleteFileBase(System.String[],System.String,System.String,System.String)" />
		protected async Task<OperateResult> DeleteFileBaseAsync(string[] fileNames, string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2011, fileNames);
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return sendFileInfo;
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return receiveBack;
			}
			OperateResult result = new OperateResult();
			if (receiveBack.Content1 == 1)
			{
				result.IsSuccess = true;
			}
			result.Message = receiveBack.Message;
			socketResult.Content?.Close();
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.DeleteFolderBase(System.String,System.String,System.String)" />
		protected async Task<OperateResult> DeleteFolderBaseAsync(string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2012, "");
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return sendFileInfo;
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return receiveBack;
			}
			OperateResult result = new OperateResult();
			if (receiveBack.Content1 == 1)
			{
				result.IsSuccess = true;
			}
			result.Message = receiveBack.Message;
			socketResult.Content?.Close();
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.DeleteEmptyFoldersBase(System.String,System.String,System.String)" />
		protected async Task<OperateResult> DeleteEmptyFoldersBaseAsync(string factory, string group, string id)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2014, "");
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendFileInfo = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendFileInfo.IsSuccess)
			{
				return sendFileInfo;
			}
			OperateResult<int, string> receiveBack = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!receiveBack.IsSuccess)
			{
				return receiveBack;
			}
			OperateResult result = new OperateResult();
			if (receiveBack.Content1 == 1)
			{
				result.IsSuccess = true;
			}
			result.Message = receiveBack.Message;
			socketResult.Content?.Close();
			return result;
		}

		/// <summary>
		/// 下载服务器的文件数据，并且存储到对应的内容里去。<br />
		/// Download the file data of the server and store it in the corresponding content.
		/// </summary>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <param name="fileName">服务器的文件名称</param>
		/// <param name="processReport">下载的进度报告，第一个数据是已完成总接字节数，第二个数据是总字节数。</param>
		/// <param name="source">数据源信息，决定最终存储到哪里去</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult DownloadFileBase(string factory, string group, string id, string fileName, Action<long, long> processReport, object source)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2001, fileName);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			string text = source as string;
			if (text != null)
			{
				OperateResult operateResult4 = ReceiveFileFromSocket(operateResult.Content, text, processReport);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
			}
			else
			{
				Stream stream = source as Stream;
				if (stream == null)
				{
					operateResult.Content?.Close();
					base.LogNet?.WriteError(ToString(), StringResources.Language.NotSupportedDataType);
					return new OperateResult(StringResources.Language.NotSupportedDataType);
				}
				OperateResult operateResult5 = ReceiveFileFromSocket(operateResult.Content, stream, processReport);
				if (!operateResult5.IsSuccess)
				{
					return operateResult5;
				}
			}
			operateResult.Content?.Close();
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.DownloadFileBase(System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},System.Object)" />
		protected async Task<OperateResult> DownloadFileBaseAsync(string factory, string group, string id, string fileName, Action<long, long> processReport, object source)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2001, fileName);
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendClass = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendClass.IsSuccess)
			{
				return sendClass;
			}
			string fileSaveName = source as string;
			if (fileSaveName != null)
			{
				OperateResult result2 = await ReceiveFileFromSocketAsync(socketResult.Content, fileSaveName, processReport);
				if (!result2.IsSuccess)
				{
					return result2;
				}
			}
			else
			{
				Stream stream = source as Stream;
				if (stream == null)
				{
					socketResult.Content?.Close();
					base.LogNet?.WriteError(ToString(), StringResources.Language.NotSupportedDataType);
					return new OperateResult(StringResources.Language.NotSupportedDataType);
				}
				OperateResult result = await ReceiveFileFromSocketAsync(socketResult.Content, stream, processReport);
				if (!result.IsSuccess)
				{
					return result;
				}
			}
			socketResult.Content?.Close();
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 上传文件给服务器，需要指定上传的数据内容，上传到服务器的分类信息，支持进度汇报功能。<br />
		/// To upload files to the server, you need to specify the content of the uploaded data, 
		/// the classification information uploaded to the server, and support the progress report function.
		/// </summary>
		/// <param name="source">数据源，可以是文件名，也可以是数据流</param>
		/// <param name="serverName">在服务器保存的文件名，不包含驱动器路径</param>
		/// <param name="factory">一级分类</param>
		/// <param name="group">二级分类</param>
		/// <param name="id">三级分类</param>
		/// <param name="fileTag">文件的描述</param>
		/// <param name="fileUpload">文件的上传人</param>
		/// <param name="processReport">汇报进度，第一个数据是已完成总接字节数，第二个数据是总字节数。</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult UploadFileBase(object source, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			OperateResult<Socket> operateResult = CreateSocketAndConnect(ServerIpEndPoint, ConnectTimeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = SendStringAndCheckReceive(operateResult.Content, 2002, serverName);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = SendFactoryGroupId(operateResult.Content, factory, group, id);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			string text = source as string;
			if (text != null)
			{
				OperateResult operateResult4 = SendFileAndCheckReceive(operateResult.Content, text, serverName, fileTag, fileUpload, processReport);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
			}
			else
			{
				Stream stream = source as Stream;
				if (stream == null)
				{
					operateResult.Content?.Close();
					base.LogNet?.WriteError(ToString(), StringResources.Language.DataSourceFormatError);
					return new OperateResult(StringResources.Language.DataSourceFormatError);
				}
				OperateResult operateResult5 = SendFileAndCheckReceive(operateResult.Content, stream, serverName, fileTag, fileUpload, processReport);
				if (!operateResult5.IsSuccess)
				{
					return operateResult5;
				}
			}
			OperateResult<int, string> operateResult6 = ReceiveStringContentFromSocket(operateResult.Content);
			if (!operateResult6.IsSuccess)
			{
				return operateResult6;
			}
			return (operateResult6.Content1 == 1) ? OperateResult.CreateSuccessResult() : new OperateResult(StringResources.Language.ServerFileCheckFailed);
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.FileClientBase.UploadFileBase(System.Object,System.String,System.String,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult> UploadFileBaseAsync(object source, string serverName, string factory, string group, string id, string fileTag, string fileUpload, Action<long, long> processReport)
		{
			OperateResult<Socket> socketResult = await CreateSocketAndConnectAsync(ServerIpEndPoint, ConnectTimeOut);
			if (!socketResult.IsSuccess)
			{
				return socketResult;
			}
			OperateResult sendString = await SendStringAndCheckReceiveAsync(socketResult.Content, 2002, serverName);
			if (!sendString.IsSuccess)
			{
				return sendString;
			}
			OperateResult sendClass = await SendFactoryGroupIdAsync(socketResult.Content, factory, group, id);
			if (!sendClass.IsSuccess)
			{
				return sendClass;
			}
			string fileName = source as string;
			if (fileName != null)
			{
				OperateResult result2 = await SendFileAndCheckReceiveAsync(socketResult.Content, fileName, serverName, fileTag, fileUpload, processReport);
				if (!result2.IsSuccess)
				{
					return result2;
				}
			}
			else
			{
				Stream stream = source as Stream;
				if (stream == null)
				{
					socketResult.Content?.Close();
					base.LogNet?.WriteError(ToString(), StringResources.Language.DataSourceFormatError);
					return new OperateResult(StringResources.Language.DataSourceFormatError);
				}
				OperateResult result = await SendFileAndCheckReceiveAsync(socketResult.Content, stream, serverName, fileTag, fileUpload, processReport);
				if (!result.IsSuccess)
				{
					return result;
				}
			}
			OperateResult<int, string> resultCheck = await ReceiveStringContentFromSocketAsync(socketResult.Content);
			if (!resultCheck.IsSuccess)
			{
				return resultCheck;
			}
			if (resultCheck.Content1 == 1)
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.ServerFileCheckFailed);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"FileClientBase[{m_ipEndPoint}]";
		}
	}
}
