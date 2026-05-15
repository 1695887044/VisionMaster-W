using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Pipe;
using HslCommunication.ModBus;

namespace HslCommunication.DCS
{
	/// <summary>
	/// 南京自动化研究所的DCS系统，基于modbus实现，但是不是标准的实现
	/// </summary>
	public class DcsNanJingAuto : ModbusTcpNet
	{
		private byte[] headCommand = new byte[12]
		{
			0, 0, 0, 0, 0, 6, 1, 3, 0, 0,
			0, 1
		};

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public DcsNanJingAuto()
		{
		}

		/// <inheritdoc />
		public DcsNanJingAuto(string ipAddress, int port = 502, byte station = 1)
			: base(ipAddress, port, station)
		{
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DcsNanJingAutoMessage();
		}

		/// <inheritdoc />
		protected override OperateResult InitializationOnConnect()
		{
			base.MessageId.ResetCurrentValue(0L);
			headCommand[6] = base.Station;
			OperateResult operateResult = CommunicationPipe.Send(headCommand);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = CommunicationPipe.Receive(-1, 3000);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return CheckResponseStatus(operateResult2.Content) ? base.InitializationOnConnect() : new OperateResult("Check Status Response failed: " + operateResult2.Content.ToHexString(' '));
		}

		/// <inheritdoc />
		protected override async Task<OperateResult> InitializationOnConnectAsync()
		{
			base.MessageId.ResetCurrentValue(0L);
			headCommand[6] = base.Station;
			OperateResult send = await CommunicationPipe.SendAsync(headCommand).ConfigureAwait(continueOnCapturedContext: false);
			if (!send.IsSuccess)
			{
				return send;
			}
			OperateResult<byte[]> receive = await CommunicationPipe.ReceiveAsync(-1, 3000).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return receive;
			}
			return CheckResponseStatus(receive.Content) ? OperateResult.CreateSuccessResult() : new OperateResult("Check Status Response failed: " + receive.Content.ToHexString(' '));
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(CommunicationPipe pipe, byte[] send, bool hasResponseData = true, bool usePackHeader = true)
		{
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Send + " : " + (LogMsgFormatBinary ? send.ToHexString(' ') : Encoding.ASCII.GetString(send)));
			INetMessage newNetMessage = GetNewNetMessage();
			if (newNetMessage != null)
			{
				newNetMessage.SendBytes = send;
			}
			OperateResult operateResult = pipe.Send(send);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (base.ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (!hasResponseData)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (base.SleepTime > 0)
			{
				HslHelper.ThreadSleep(base.SleepTime);
			}
			OperateResult<byte[]> operateResult2 = pipe.ReceiveMessage(newNetMessage, send);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Receive + " : " + (LogMsgFormatBinary ? operateResult2.Content.ToHexString(' ') : Encoding.ASCII.GetString(operateResult2.Content)));
			if (operateResult2.Content.Length == 6 && CheckResponseStatus(operateResult2.Content))
			{
				operateResult2 = pipe.ReceiveMessage(newNetMessage, send);
			}
			if (newNetMessage != null && !newNetMessage.CheckHeadBytesLegal(null))
			{
				return new OperateResult<byte[]>(StringResources.Language.CommandHeadCodeCheckFailed + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(send, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(operateResult2.Content, ' '));
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(CommunicationPipe pipe, byte[] send, bool hasResponseData = true, bool usePackHeader = true)
		{
			byte[] sendValue = (usePackHeader ? PackCommandWithHeader(send) : send);
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Send + " : " + (LogMsgFormatBinary ? sendValue.ToHexString(' ') : Encoding.ASCII.GetString(sendValue)));
			INetMessage netMessage = GetNewNetMessage();
			if (netMessage != null)
			{
				netMessage.SendBytes = sendValue;
			}
			OperateResult sendResult = await pipe.SendAsync(sendValue).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(sendResult);
			}
			if (base.ReceiveTimeOut < 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (!hasResponseData)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			if (base.SleepTime > 0)
			{
				await Task.Delay(base.SleepTime);
			}
			OperateResult<byte[]> resultReceive = await pipe.ReceiveMessageAsync(netMessage, sendValue).ConfigureAwait(continueOnCapturedContext: false);
			if (!resultReceive.IsSuccess)
			{
				return resultReceive;
			}
			base.LogNet?.WriteDebug(ToString(), StringResources.Language.Receive + " : " + (LogMsgFormatBinary ? resultReceive.Content.ToHexString(' ') : Encoding.ASCII.GetString(resultReceive.Content)));
			if (resultReceive.Content.Length == 6 && CheckResponseStatus(resultReceive.Content))
			{
				resultReceive = await pipe.ReceiveMessageAsync(netMessage, sendValue).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (netMessage != null && !netMessage.CheckHeadBytesLegal(null))
			{
				return new OperateResult<byte[]>(StringResources.Language.CommandHeadCodeCheckFailed + Environment.NewLine + StringResources.Language.Send + ": " + SoftBasic.ByteToHexString(send, ' ') + Environment.NewLine + StringResources.Language.Receive + ": " + SoftBasic.ByteToHexString(resultReceive.Content, ' '));
			}
			return UnpackResponseContent(sendValue, resultReceive.Content);
		}

		private bool CheckResponseStatus(byte[] content)
		{
			if (content.Length < 6)
			{
				return false;
			}
			for (int i = content.Length - 4; i < content.Length; i++)
			{
				if (content[i] != 0)
				{
					return false;
				}
			}
			return true;
		}
	}
}
