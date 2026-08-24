using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using Newtonsoft.Json.Linq;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 包含了主动异步接收的方法实现和文件类异步读写的实现<br />
	/// Contains the implementation of the active asynchronous receiving method and the implementation of asynchronous reading and writing of the file class
	/// </summary>
	public class NetworkXBase : NetworkBase
	{
		/// <summary>
		/// 对客户端而言是的通讯用的套接字，对服务器来说是用于侦听的套接字<br />
		/// A communication socket for the client, or a listening socket for the server
		/// </summary>
		protected Socket CoreSocket = null;

		/// <summary>
		/// 默认的无参构造方法<br />
		/// The default parameterless constructor
		/// </summary>
		public NetworkXBase()
		{
		}

		/// <summary>
		/// [自校验] 将文件数据发送至套接字，如果结果异常，则结束通讯<br />
		/// [Self-check] Send the file data to the socket. If the result is abnormal, the communication is ended.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="filename">完整的文件路径</param>
		/// <param name="filelength">文件的长度</param>
		/// <param name="report">进度报告器</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendFileStreamToSocket(Socket socket, string filename, long filelength, Action<long, long> report = null)
		{
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = SendStreamToSocket(socket, stream, filelength, report, reportByPercent: true);
				}
				return result;
			}
			catch (Exception ex)
			{
				socket?.Close();
				base.LogNet?.WriteException(ToString(), ex);
				return new OperateResult(ex.Message);
			}
		}

		/// <summary>
		/// [自校验] 将文件数据发送至套接字，具体发送细节将在继承类中实现，如果结果异常，则结束通讯<br />
		/// [Self-checking] Send the file data to the socket. The specific sending details will be implemented in the inherited class. If the result is abnormal, the communication will end
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="filename">文件名称，文件必须存在</param>
		/// <param name="servername">远程端的文件名称</param>
		/// <param name="filetag">文件的额外标签</param>
		/// <param name="fileupload">文件的上传人</param>
		/// <param name="sendReport">发送进度报告</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendFileAndCheckReceive(Socket socket, string filename, string servername, string filetag, string fileupload, Action<long, long> sendReport = null)
		{
			FileInfo fileInfo = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult operateResult = SendStringAndCheckReceive(socket, 0, "");
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				socket?.Close();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			JObject jObject = new JObject
			{
				{
					"FileName",
					new JValue(servername)
				},
				{
					"FileSize",
					new JValue(fileInfo.Length)
				},
				{
					"FileTag",
					new JValue(filetag)
				},
				{
					"FileUpload",
					new JValue(fileupload)
				}
			};
			OperateResult operateResult2 = SendStringAndCheckReceive(socket, 1, jObject.ToString());
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return SendFileStreamToSocket(socket, filename, fileInfo.Length, sendReport);
		}

		/// <summary>
		/// [自校验] 将流数据发送至套接字，具体发送细节将在继承类中实现，如果结果异常，则结束通讯<br />
		/// [Self-checking] Send stream data to the socket. The specific sending details will be implemented in the inherited class. 
		/// If the result is abnormal, the communication will be terminated
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="stream">文件名称，文件必须存在</param>
		/// <param name="servername">远程端的文件名称</param>
		/// <param name="filetag">文件的额外标签</param>
		/// <param name="fileupload">文件的上传人</param>
		/// <param name="sendReport">发送进度报告</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult SendFileAndCheckReceive(Socket socket, Stream stream, string servername, string filetag, string fileupload, Action<long, long> sendReport = null)
		{
			JObject jObject = new JObject
			{
				{
					"FileName",
					new JValue(servername)
				},
				{
					"FileSize",
					new JValue(stream.Length)
				},
				{
					"FileTag",
					new JValue(filetag)
				},
				{
					"FileUpload",
					new JValue(fileupload)
				}
			};
			OperateResult operateResult = SendStringAndCheckReceive(socket, 1, jObject.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return SendStreamToSocket(socket, stream, stream.Length, sendReport, reportByPercent: true);
		}

		/// <summary>
		/// [自校验] 从套接字中接收文件头信息<br />
		/// [Self-checking] Receive file header information from socket
		/// </summary>
		/// <param name="socket">套接字的网络</param>
		/// <returns>包含文件信息的结果对象</returns>
		protected OperateResult<FileBaseInfo> ReceiveFileHeadFromSocket(Socket socket)
		{
			OperateResult<int, string> operateResult = ReceiveStringContentFromSocket(socket);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult);
			}
			if (operateResult.Content1 == 0)
			{
				socket?.Close();
				base.LogNet?.WriteWarn(ToString(), StringResources.Language.FileRemoteNotExist);
				return new OperateResult<FileBaseInfo>(StringResources.Language.FileNotExist);
			}
			OperateResult<FileBaseInfo> operateResult2 = new OperateResult<FileBaseInfo>
			{
				Content = new FileBaseInfo()
			};
			try
			{
				JObject json = JObject.Parse(operateResult.Content2);
				operateResult2.Content.Name = SoftBasic.GetValueFromJsonObject(json, "FileName", "");
				operateResult2.Content.Size = SoftBasic.GetValueFromJsonObject(json, "FileSize", 0L);
				operateResult2.Content.Tag = SoftBasic.GetValueFromJsonObject(json, "FileTag", "");
				operateResult2.Content.Upload = SoftBasic.GetValueFromJsonObject(json, "FileUpload", "");
				operateResult2.IsSuccess = true;
			}
			catch (Exception ex)
			{
				socket?.Close();
				operateResult2.Message = "Extra File Head Wrong:" + ex.Message;
			}
			return operateResult2;
		}

		/// <summary>
		/// [自校验] 从网络中接收一个文件，如果结果异常，则结束通讯<br />
		/// [Self-checking] Receive a file from the network. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="savename">接收文件后保存的文件名</param>
		/// <param name="receiveReport">接收进度报告</param>
		/// <returns>包含文件信息的结果对象</returns>
		protected OperateResult<FileBaseInfo> ReceiveFileFromSocket(Socket socket, string savename, Action<long, long> receiveReport)
		{
			OperateResult<FileBaseInfo> operateResult = ReceiveFileHeadFromSocket(socket);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			try
			{
				OperateResult operateResult2 = null;
				using (FileStream stream = new FileStream(savename, FileMode.Create, FileAccess.Write))
				{
					operateResult2 = WriteStreamFromSocket(socket, stream, operateResult.Content.Size, receiveReport, reportByPercent: true);
				}
				if (!operateResult2.IsSuccess)
				{
					if (File.Exists(savename))
					{
						File.Delete(savename);
					}
					return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult2);
				}
				return operateResult;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), ex);
				socket?.Close();
				return new OperateResult<FileBaseInfo>
				{
					Message = ex.Message
				};
			}
		}

		/// <summary>
		/// [自校验] 从网络中接收一个文件，写入数据流，如果结果异常，则结束通讯，参数顺序文件名，文件大小，文件标识，上传人<br />
		/// [Self-checking] Receive a file from the network. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="stream">等待写入的数据流</param>
		/// <param name="receiveReport">接收进度报告</param>
		/// <returns>文件头结果</returns>
		protected OperateResult<FileBaseInfo> ReceiveFileFromSocket(Socket socket, Stream stream, Action<long, long> receiveReport)
		{
			OperateResult<FileBaseInfo> operateResult = ReceiveFileHeadFromSocket(socket);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			try
			{
				WriteStreamFromSocket(socket, stream, operateResult.Content.Size, receiveReport, reportByPercent: true);
				return operateResult;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), ex);
				socket?.Close();
				return new OperateResult<FileBaseInfo>
				{
					Message = ex.Message
				};
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.SendFileStreamToSocket(System.Net.Sockets.Socket,System.String,System.Int64,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult> SendFileStreamToSocketAsync(Socket socket, string filename, long filelength, Action<long, long> report = null)
		{
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = await SendStreamToSocketAsync(socket, fs, filelength, report, reportByPercent: true);
				}
				return result;
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				socket?.Close();
				base.LogNet?.WriteException(ToString(), ex);
				return new OperateResult(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.SendFileAndCheckReceive(System.Net.Sockets.Socket,System.String,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult> SendFileAndCheckReceiveAsync(Socket socket, string filename, string servername, string filetag, string fileupload, Action<long, long> sendReport = null)
		{
			FileInfo info = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult stringResult = await SendStringAndCheckReceiveAsync(socket, 0, "");
				if (!stringResult.IsSuccess)
				{
					return stringResult;
				}
				socket?.Close();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			JObject json = new JObject
			{
				{
					"FileName",
					new JValue(servername)
				},
				{
					"FileSize",
					new JValue(info.Length)
				},
				{
					"FileTag",
					new JValue(filetag)
				},
				{
					"FileUpload",
					new JValue(fileupload)
				}
			};
			OperateResult sendResult = await SendStringAndCheckReceiveAsync(socket, 1, json.ToString());
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			return await SendFileStreamToSocketAsync(socket, filename, info.Length, sendReport);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.SendFileAndCheckReceive(System.Net.Sockets.Socket,System.IO.Stream,System.String,System.String,System.String,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult> SendFileAndCheckReceiveAsync(Socket socket, Stream stream, string servername, string filetag, string fileupload, Action<long, long> sendReport = null)
		{
			JObject json = new JObject
			{
				{
					"FileName",
					new JValue(servername)
				},
				{
					"FileSize",
					new JValue(stream.Length)
				},
				{
					"FileTag",
					new JValue(filetag)
				},
				{
					"FileUpload",
					new JValue(fileupload)
				}
			};
			OperateResult fileResult = await SendStringAndCheckReceiveAsync(socket, 1, json.ToString());
			if (!fileResult.IsSuccess)
			{
				return fileResult;
			}
			return await SendStreamToSocketAsync(socket, stream, stream.Length, sendReport, reportByPercent: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.ReceiveFileHeadFromSocket(System.Net.Sockets.Socket)" />
		protected async Task<OperateResult<FileBaseInfo>> ReceiveFileHeadFromSocketAsync(Socket socket)
		{
			OperateResult<int, string> receiveString = await ReceiveStringContentFromSocketAsync(socket);
			if (!receiveString.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(receiveString);
			}
			if (receiveString.Content1 == 0)
			{
				socket?.Close();
				base.LogNet?.WriteWarn(ToString(), StringResources.Language.FileRemoteNotExist);
				return new OperateResult<FileBaseInfo>(StringResources.Language.FileNotExist);
			}
			OperateResult<FileBaseInfo> result = new OperateResult<FileBaseInfo>
			{
				Content = new FileBaseInfo()
			};
			try
			{
				JObject json = JObject.Parse(receiveString.Content2);
				result.Content.Name = SoftBasic.GetValueFromJsonObject(json, "FileName", "");
				result.Content.Size = SoftBasic.GetValueFromJsonObject(json, "FileSize", 0L);
				result.Content.Tag = SoftBasic.GetValueFromJsonObject(json, "FileTag", "");
				result.Content.Upload = SoftBasic.GetValueFromJsonObject(json, "FileUpload", "");
				result.IsSuccess = true;
			}
			catch (Exception ex)
			{
				socket?.Close();
				result.Message = "Extra File Head Wrong:" + ex.Message;
			}
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.ReceiveFileFromSocket(System.Net.Sockets.Socket,System.String,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<FileBaseInfo>> ReceiveFileFromSocketAsync(Socket socket, string savename, Action<long, long> receiveReport)
		{
			OperateResult<FileBaseInfo> fileResult = await ReceiveFileHeadFromSocketAsync(socket);
			if (!fileResult.IsSuccess)
			{
				return fileResult;
			}
			try
			{
				OperateResult write = null;
				using (FileStream fs = new FileStream(savename, FileMode.Create, FileAccess.Write))
				{
					write = await WriteStreamFromSocketAsync(socket, fs, fileResult.Content.Size, receiveReport, reportByPercent: true);
				}
				if (!write.IsSuccess)
				{
					if (File.Exists(savename))
					{
						File.Delete(savename);
					}
					return OperateResult.CreateFailedResult<FileBaseInfo>(write);
				}
				return fileResult;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), ex);
				socket?.Close();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkXBase.ReceiveFileFromSocket(System.Net.Sockets.Socket,System.IO.Stream,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<FileBaseInfo>> ReceiveFileFromSocketAsync(Socket socket, Stream stream, Action<long, long> receiveReport)
		{
			OperateResult<FileBaseInfo> fileResult = await ReceiveFileHeadFromSocketAsync(socket);
			if (!fileResult.IsSuccess)
			{
				return fileResult;
			}
			try
			{
				await WriteStreamFromSocketAsync(socket, stream, fileResult.Content.Size, receiveReport, reportByPercent: true);
				return fileResult;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), ex);
				socket?.Close();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "NetworkXBase";
		}
	}
}
