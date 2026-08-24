using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;

namespace HslCommunication.WebSocket
{
	/// <summary>
	/// WebSocket的问答机制的客户端，本客户端将会在请求头上追加 RequestAndAnswer: true，本客户端将会请求服务器的信息，然后等待服务器的返回<br />
	/// Client of WebSocket Q &amp; A mechanism, this client will append RequestAndAnswer: true to the request header, this client will request the server information, and then wait for the server to return
	/// </summary>
	public class WebSocketQANet : TcpNetCommunication
	{
		/// <summary>
		/// 根据指定的ip地址及端口号，实例化一个默认的对象<br />
		/// Instantiates a default object based on the specified IP address and port number
		/// </summary>
		/// <param name="ipAddress">远程服务器的ip地址</param>
		/// <param name="port">端口号信息</param>
		public WebSocketQANet(string ipAddress, int port)
		{
			IpAddress = HslHelper.GetIpAddressFromInput(ipAddress);
			Port = port;
		}

		/// <inheritdoc />
		protected override OperateResult InitializationOnConnect()
		{
			byte[] data = WebSocketHelper.BuildWsQARequest(IpAddress, Port);
			OperateResult operateResult = CommunicationPipe.Send(data);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = CommunicationPipe.Receive(-1, 10000);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(CommunicationPipe pipe, byte[] send, bool hasResponseData = true, bool usePackHeader = true)
		{
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Send + " : " + SoftBasic.ByteToHexString(send, ' '));
			OperateResult operateResult = pipe.Send(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (base.ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<WebSocketMessage> operateResult2 = WebSocketHelper.ReceiveWebSocketPayload(pipe);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			base.LogNet?.WriteDebug(ToString(), $"{StringResources.Language.Receive} : OpCode[{operateResult2.Content.OpCode}] Mask[{operateResult2.Content.HasMask}] {SoftBasic.ByteToHexString(operateResult2.Content.Payload, ' ')}");
			return OperateResult.CreateSuccessResult(operateResult2.Content.Payload);
		}

		/// <inheritdoc />
		protected override async Task<OperateResult> InitializationOnConnectAsync()
		{
			byte[] command = WebSocketHelper.BuildWsQARequest(IpAddress, Port);
			OperateResult send = await CommunicationPipe.SendAsync(command);
			if (!send.IsSuccess)
			{
				return send;
			}
			OperateResult<byte[]> rece = await CommunicationPipe.ReceiveAsync(-1, 10000);
			if (!rece.IsSuccess)
			{
				return rece;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(CommunicationPipe pipe, byte[] send, bool hasResponseData = true, bool usePackHeader = true)
		{
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Send + " : " + SoftBasic.ByteToHexString(send, ' '));
			OperateResult sendResult = await pipe.SendAsync(send);
			if (!sendResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(sendResult);
			}
			if (base.ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<WebSocketMessage> read = await WebSocketHelper.ReceiveWebSocketPayloadAsync(pipe).ConfigureAwait(continueOnCapturedContext: false);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			base.LogNet?.WriteDebug(ToString(), $"{StringResources.Language.Receive} : OpCode[{read.Content.OpCode}] Mask[{read.Content.HasMask}] {SoftBasic.ByteToHexString(read.Content.Payload, ' ')}");
			return OperateResult.CreateSuccessResult(read.Content.Payload);
		}

		/// <summary>
		/// 和websocket的服务器交互，将负载数据发送到服务器端，然后等待接收服务器的数据<br />
		/// Interact with the websocket server, send the load data to the server, and then wait to receive data from the server
		/// </summary>
		/// <param name="payload">数据负载</param>
		/// <returns>返回的结果数据</returns>
		public OperateResult<string> ReadFromServer(string payload)
		{
			return ByteTransformHelper.GetSuccessResultFromOther(ReadFromCoreServer(WebSocketHelper.WebScoketPackData(1, isMask: true, payload)), Encoding.UTF8.GetString);
		}

		/// <inheritdoc cref="M:HslCommunication.WebSocket.WebSocketQANet.ReadFromServer(System.String)" />
		public async Task<OperateResult<string>> ReadFromServerAsync(string payload)
		{
			return ByteTransformHelper.GetSuccessResultFromOther(await ReadFromCoreServerAsync(WebSocketHelper.WebScoketPackData(1, isMask: true, payload)), Encoding.UTF8.GetString);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"WebSocketQANet[{IpAddress}:{Port}]";
		}
	}
}
