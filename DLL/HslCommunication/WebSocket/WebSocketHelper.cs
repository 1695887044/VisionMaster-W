using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Pipe;

namespace HslCommunication.WebSocket
{
	/// <summary>
	/// websocket的相关辅助的方法
	/// </summary>
	public class WebSocketHelper
	{
		/// <summary>
		/// 计算websocket返回得令牌
		/// </summary>
		/// <param name="webSocketKey">请求的令牌</param>
		/// <returns>返回的令牌</returns>
		public static string CalculateWebscoketSha1(string webSocketKey)
		{
			SHA1 sHA = new SHA1CryptoServiceProvider();
			byte[] inArray = sHA.ComputeHash(Encoding.UTF8.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
			sHA.Dispose();
			return Convert.ToBase64String(inArray);
		}

		/// <summary>
		/// 根据http网页的信息，计算出返回的安全令牌
		/// </summary>
		/// <param name="httpGet">网页信息</param>
		/// <returns>返回的安全令牌</returns>
		public static string GetSecKeyAccetp(string httpGet)
		{
			string webSocketKey = string.Empty;
			Regex regex = new Regex("Sec\\-WebSocket\\-Key:(.*?)\\r\\n");
			Match match = regex.Match(httpGet);
			if (match.Success)
			{
				webSocketKey = Regex.Replace(match.Value, "Sec\\-WebSocket\\-Key:(.*?)\\r\\n", "$1").Trim();
			}
			return CalculateWebscoketSha1(webSocketKey);
		}

		/// <summary>
		/// 检测当前的反馈对象是否是标准的websocket请求
		/// </summary>
		/// <param name="httpGet">http的请求内容</param>
		/// <returns>是否验证成功</returns>
		public static OperateResult CheckWebSocketLegality(string httpGet)
		{
			if (Regex.IsMatch(httpGet, "Connection:[ ]*Upgrade", RegexOptions.IgnoreCase) || Regex.IsMatch(httpGet, "Upgrade:[ ]*websocket", RegexOptions.IgnoreCase))
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult("Can't find Connection: Upgrade or Upgrade: websocket");
		}

		/// <summary>
		/// 从当前的websocket的HTTP请求头里，分析出订阅的主题内容
		/// </summary>
		/// <param name="httpGet">http的请求内容</param>
		/// <returns>是否验证成功</returns>
		public static string[] GetWebSocketSubscribes(string httpGet)
		{
			Regex regex = new Regex("HslSubscribes:[^\\r\\n]+");
			Match match = regex.Match(httpGet);
			if (!match.Success)
			{
				return null;
			}
			return SoftBasic.UrlDecode(match.Value.Substring(14).Trim(), Encoding.UTF8).Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>
		/// 从当前的Websocket的Url里，分析出订阅的主题内容
		/// </summary>
		/// <param name="url">URL内容，例如 ws://127.0.0.1:1883/HslSubscribes=A,B</param>
		/// <returns>消息主题</returns>
		public static string[] GetWebSocketSubscribesFromUrl(string url)
		{
			Regex regex = new Regex("HslSubscribes=[\\s\\S]+$");
			Match match = regex.Match(url);
			if (!match.Success)
			{
				return null;
			}
			return SoftBasic.UrlDecode(match.Value.Substring(14).Trim(), Encoding.UTF8).Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>
		/// 获取初步握手的时候的完整返回的数据信息
		/// </summary>
		/// <param name="httpGet">请求的网页信息</param>
		/// <returns>完整的返回信息</returns>
		public static OperateResult<byte[]> GetResponse(string httpGet)
		{
			try
			{
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append("HTTP/1.1 101 Switching Protocols" + Environment.NewLine);
				stringBuilder.Append("Connection: Upgrade" + Environment.NewLine);
				stringBuilder.Append("Upgrade: websocket" + Environment.NewLine);
				stringBuilder.Append("Server:hsl websocket server" + Environment.NewLine);
				stringBuilder.Append("Access-Control-Allow-Credentials:true" + Environment.NewLine);
				stringBuilder.Append("Access-Control-Allow-Headers:content-type" + Environment.NewLine);
				stringBuilder.Append("Sec-WebSocket-Accept: " + GetSecKeyAccetp(httpGet) + Environment.NewLine + Environment.NewLine);
				return OperateResult.CreateSuccessResult(Encoding.UTF8.GetBytes(stringBuilder.ToString()));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 创建连接服务器的http请求，输入订阅的主题信息
		/// </summary>
		/// <param name="ipAddress">远程服务器的ip地址</param>
		/// <param name="port">远程服务器的端口号</param>
		/// <param name="url">参数信息</param>
		/// <param name="subscribes">通知hsl的服务器，需要订阅的topic信息</param>
		/// <param name="getCarryHostAndPort">GET命令是否携带 Host 信息。</param>
		/// <returns>报文信息</returns>
		public static byte[] BuildWsSubRequest(string ipAddress, int port, string url, string[] subscribes, bool getCarryHostAndPort)
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (subscribes != null)
			{
				stringBuilder.Append("HslSubscribes: ");
				for (int i = 0; i < subscribes.Length; i++)
				{
					stringBuilder.Append(subscribes[i]);
					if (i != subscribes.Length - 1)
					{
						stringBuilder.Append(",");
					}
				}
			}
			return BuildWsRequest(ipAddress, port, url, stringBuilder.ToString(), getCarryHostAndPort);
		}

		/// <summary>
		/// 创建连接服务器的http请求，采用问答的机制
		/// </summary>
		/// <param name="ipAddress">远程服务器的ip地址</param>
		/// <param name="port">远程服务器的端口号</param>
		/// <returns>报文信息</returns>
		public static byte[] BuildWsQARequest(string ipAddress, int port)
		{
			return BuildWsRequest(ipAddress, port, string.Empty, "HslRequestAndAnswer: true", getCarryHostAndPort: false);
		}

		/// <summary>
		/// 根据额外的参数信息，创建新的websocket的请求信息
		/// </summary>
		/// <param name="ipAddress">ip地址</param>
		/// <param name="port">端口号</param>
		/// <param name="url">跟在端口号后面的额外的参数信息</param>
		/// <param name="extra">额外的参数信息</param>
		/// <param name="getCarryHostAndPort">GET命令是否携带 Host 信息。</param>
		/// <returns>报文信息</returns>
		public static byte[] BuildWsRequest(string ipAddress, int port, string url, string extra, bool getCarryHostAndPort)
		{
			if (!url.StartsWith("/"))
			{
				url = "/" + url;
			}
			StringBuilder stringBuilder = new StringBuilder();
			if (getCarryHostAndPort)
			{
				stringBuilder.Append($"GET ws://{ipAddress}:{port}{url} HTTP/1.1");
			}
			else
			{
				stringBuilder.Append("GET " + url + " HTTP/1.1");
			}
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append($"Host: {ipAddress}:{port}");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Connection: Upgrade");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Pragma: no-cache");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Cache-Control: no-cache");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Upgrade: websocket");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append($"Origin: http://{ipAddress}:{port}");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Sec-WebSocket-Version: 13");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("User-Agent: Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/65.0.3314.0 Safari/537.36 SE 2.X MetaSr 1.0");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Accept-Encoding: gzip, deflate, br");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Accept-Language: zh-CN,zh;q=0.9");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Sec-WebSocket-Key: ia36apzXapB4YVxRfVyTuw==");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits");
			stringBuilder.Append(Environment.NewLine);
			if (!string.IsNullOrEmpty(extra))
			{
				stringBuilder.Append(extra);
				stringBuilder.Append(Environment.NewLine);
			}
			stringBuilder.Append(Environment.NewLine);
			return Encoding.UTF8.GetBytes(stringBuilder.ToString());
		}

		/// <summary>
		/// 将普通的文本信息转换成websocket的报文
		/// </summary>
		/// <param name="opCode">操作信息码</param>
		/// <param name="isMask">是否使用掩码</param>
		/// <param name="message">等待转换的数据信息</param>
		/// <returns>数据包</returns>
		public static byte[] WebScoketPackData(int opCode, bool isMask, string message)
		{
			return WebScoketPackData(opCode, isMask, string.IsNullOrEmpty(message) ? new byte[0] : Encoding.UTF8.GetBytes(message));
		}

		/// <summary>
		/// 将普通的文本信息转换成websocket的报文
		/// </summary>
		/// <param name="opCode">操作信息码</param>
		/// <param name="isMask">是否使用掩码</param>
		/// <param name="payload">等待转换的数据信息</param>
		/// <returns>数据包</returns>
		public static byte[] WebScoketPackData(int opCode, bool isMask, byte[] payload)
		{
			if (payload == null)
			{
				payload = new byte[0];
			}
			byte[] array = payload.CopyArray();
			MemoryStream memoryStream = new MemoryStream();
			byte[] array2 = new byte[4] { 155, 3, 161, 168 };
			if (isMask)
			{
				HslHelper.HslRandom.NextBytes(array2);
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = (byte)(array[i] ^ array2[i % 4]);
				}
			}
			memoryStream.WriteByte((byte)(0x80u | (uint)opCode));
			if (array.Length < 126)
			{
				memoryStream.WriteByte((byte)(array.Length + (isMask ? 128 : 0)));
			}
			else if (array.Length <= 65535)
			{
				memoryStream.WriteByte((byte)(126 + (isMask ? 128 : 0)));
				byte[] bytes = BitConverter.GetBytes((ushort)array.Length);
				Array.Reverse(bytes);
				memoryStream.Write(bytes, 0, bytes.Length);
			}
			else
			{
				memoryStream.WriteByte((byte)(127 + (isMask ? 128 : 0)));
				byte[] bytes2 = BitConverter.GetBytes((ulong)array.Length);
				Array.Reverse(bytes2);
				memoryStream.Write(bytes2, 0, bytes2.Length);
			}
			if (isMask)
			{
				memoryStream.Write(array2, 0, array2.Length);
			}
			memoryStream.Write(array, 0, array.Length);
			byte[] result = memoryStream.ToArray();
			memoryStream.Dispose();
			return result;
		}

		/// <summary>
		/// 从socket接收一条完整的websocket数据，返回<see cref="T:HslCommunication.WebSocket.WebSocketMessage" />的数据信息<br />
		/// Receive a complete websocket data from the socket, return the data information of the <see cref="T:HslCommunication.WebSocket.WebSocketMessage" />
		/// </summary>
		/// <param name="pipe">当前通信的管道信息</param>
		/// <returns>包含websocket消息的结果内容</returns>
		public static OperateResult<WebSocketMessage> ReceiveWebSocketPayload(CommunicationPipe pipe)
		{
			List<byte> list = new List<byte>();
			OperateResult<WebSocketMessage, bool> operateResult;
			do
			{
				operateResult = ReceiveFrameWebSocketPayload(pipe);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage>(operateResult);
				}
				list.AddRange(operateResult.Content1.Payload);
			}
			while (!operateResult.Content2);
			return OperateResult.CreateSuccessResult(new WebSocketMessage
			{
				HasMask = operateResult.Content1.HasMask,
				OpCode = operateResult.Content1.OpCode,
				Payload = list.ToArray()
			});
		}

		/// <summary>
		/// 从socket接收一条<see cref="T:HslCommunication.WebSocket.WebSocketMessage" />片段数据，返回<see cref="T:HslCommunication.WebSocket.WebSocketMessage" />的数据信息和是否最后一条数据内容<br />
		/// Receive a piece of <see cref="T:HslCommunication.WebSocket.WebSocketMessage" /> fragment data from the socket, return the data information of <see cref="T:HslCommunication.WebSocket.WebSocketMessage" /> and whether the last data content
		/// </summary>
		/// <param name="pipe">当前通信的管道信息</param>
		/// <returns>包含websocket消息的结果内容</returns>
		private static OperateResult<WebSocketMessage, bool> ReceiveFrameWebSocketPayload(CommunicationPipe pipe)
		{
			OperateResult<byte[]> operateResult = pipe.Receive(2, 5000);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<WebSocketMessage, bool>(operateResult);
			}
			bool value = (operateResult.Content[0] & 0x80) == 128;
			bool flag = (operateResult.Content[1] & 0x80) == 128;
			int opCode = operateResult.Content[0] & 0xF;
			byte[] array = null;
			int num = operateResult.Content[1] & 0x7F;
			switch (num)
			{
			case 126:
			{
				OperateResult<byte[]> operateResult3 = pipe.Receive(2, 5000);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(operateResult3);
				}
				Array.Reverse(operateResult3.Content);
				num = BitConverter.ToUInt16(operateResult3.Content, 0);
				break;
			}
			case 127:
			{
				OperateResult<byte[]> operateResult2 = pipe.Receive(8, 5000);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(operateResult2);
				}
				Array.Reverse(operateResult2.Content);
				num = (int)BitConverter.ToUInt64(operateResult2.Content, 0);
				break;
			}
			}
			if (flag)
			{
				OperateResult<byte[]> operateResult4 = pipe.Receive(4, 5000);
				if (!operateResult4.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(operateResult4);
				}
				array = operateResult4.Content;
			}
			OperateResult<byte[]> operateResult5 = pipe.Receive(num, 60000);
			if (!operateResult5.IsSuccess)
			{
				return OperateResult.CreateFailedResult<WebSocketMessage, bool>(operateResult5);
			}
			if (flag)
			{
				for (int i = 0; i < operateResult5.Content.Length; i++)
				{
					operateResult5.Content[i] = (byte)(operateResult5.Content[i] ^ array[i % 4]);
				}
			}
			return OperateResult.CreateSuccessResult(new WebSocketMessage
			{
				HasMask = flag,
				OpCode = opCode,
				Payload = operateResult5.Content
			}, value);
		}

		/// <inheritdoc cref="M:HslCommunication.WebSocket.WebSocketHelper.ReceiveWebSocketPayload(HslCommunication.Core.Pipe.CommunicationPipe)" />
		public static async Task<OperateResult<WebSocketMessage>> ReceiveWebSocketPayloadAsync(CommunicationPipe pipe)
		{
			List<byte> data = new List<byte>();
			OperateResult<WebSocketMessage, bool> read;
			do
			{
				read = await ReceiveFrameWebSocketPayloadAsync(pipe).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage>(read);
				}
				data.AddRange(read.Content1.Payload);
			}
			while (!read.Content2);
			return OperateResult.CreateSuccessResult(new WebSocketMessage
			{
				HasMask = read.Content1.HasMask,
				OpCode = read.Content1.OpCode,
				Payload = data.ToArray()
			});
		}

		/// <inheritdoc cref="M:HslCommunication.WebSocket.WebSocketHelper.ReceiveFrameWebSocketPayload(HslCommunication.Core.Pipe.CommunicationPipe)" />
		private static async Task<OperateResult<WebSocketMessage, bool>> ReceiveFrameWebSocketPayloadAsync(CommunicationPipe pipe)
		{
			OperateResult<byte[]> head = await pipe.ReceiveAsync(2, 5000).ConfigureAwait(continueOnCapturedContext: false);
			if (!head.IsSuccess)
			{
				return OperateResult.CreateFailedResult<WebSocketMessage, bool>(head);
			}
			bool isEof = (head.Content[0] & 0x80) == 128;
			bool hasMask = (head.Content[1] & 0x80) == 128;
			int opCode = head.Content[0] & 0xF;
			byte[] mask = null;
			int length = head.Content[1] & 0x7F;
			switch (length)
			{
			case 126:
			{
				OperateResult<byte[]> extended2 = await pipe.ReceiveAsync(2, 5000).ConfigureAwait(continueOnCapturedContext: false);
				if (!extended2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(extended2);
				}
				Array.Reverse(extended2.Content);
				length = BitConverter.ToUInt16(extended2.Content, 0);
				break;
			}
			case 127:
			{
				OperateResult<byte[]> extended = await pipe.ReceiveAsync(8, 5000).ConfigureAwait(continueOnCapturedContext: false);
				if (!extended.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(extended);
				}
				Array.Reverse(extended.Content);
				length = (int)BitConverter.ToUInt64(extended.Content, 0);
				break;
			}
			}
			if (hasMask)
			{
				OperateResult<byte[]> maskResult = await pipe.ReceiveAsync(4, 5000).ConfigureAwait(continueOnCapturedContext: false);
				if (!maskResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<WebSocketMessage, bool>(maskResult);
				}
				mask = maskResult.Content;
			}
			OperateResult<byte[]> payload = await pipe.ReceiveAsync(length, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!payload.IsSuccess)
			{
				return OperateResult.CreateFailedResult<WebSocketMessage, bool>(payload);
			}
			if (hasMask)
			{
				for (int i = 0; i < payload.Content.Length; i++)
				{
					payload.Content[i] = (byte)(payload.Content[i] ^ mask[i % 4]);
				}
			}
			return OperateResult.CreateSuccessResult(new WebSocketMessage
			{
				HasMask = hasMask,
				OpCode = opCode,
				Payload = payload.Content
			}, isEof);
		}
	}
}
