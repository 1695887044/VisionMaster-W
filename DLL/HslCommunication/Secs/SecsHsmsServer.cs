using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Secs.Helper;
using HslCommunication.Secs.Message;
using HslCommunication.Secs.Types;

namespace HslCommunication.Secs
{
	/// <summary>
	/// Secs Hsms的虚拟服务器，可以用来模拟Secs设备，等待客户端的连接，自定义响应客户端的数据
	/// </summary>
	/// <remarks>
	/// </remarks>
	/// <example>
	/// 下面就看看基本的操作内容
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Secs\SecsGemSample.cs" region="Server Sample" title="基本的使用" />
	/// 关于<see cref="T:HslCommunication.Secs.Types.SecsValue" />类型，可以非常灵活的实例化，参考下面的示例代码
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Secs\SecsGemSample.cs" region="Sample2" title="SecsValue说明" />
	/// </example>
	public class SecsHsmsServer : CommunicationServer
	{
		/// <summary>
		/// 当接收到来自客户的Secs信息时触发的对象<br />
		/// Object fired when Secs information from client is received
		/// </summary>
		/// <param name="sender">触发的服务器对象</param>
		/// <param name="session">消息的会话对象信息</param>
		/// <param name="message">实际的数据信息</param>
		public delegate void SecsMessageReceivedDelegate(object sender, PipeSession session, SecsMessage message);

		private Encoding stringEncoding = Encoding.Default;

		private SoftIncrementCount incrementCount;

		private ushort deviceId = 1;

		/// <summary>
		/// 获取或设置用于字符串解析的编码信息<br />
		/// Obtain or set encoding information for string parsing
		/// </summary>
		public Encoding StringEncoding
		{
			get
			{
				return stringEncoding;
			}
			set
			{
				stringEncoding = value;
			}
		}

		/// <summary>
		/// 获取或设置当前服务器的默认的ID信息，在发布消息时将使用当前的值<br />
		/// Gets or sets the default ID information for the current server, and the current value will be used when publishing messages
		/// </summary>
		public ushort DeviceId
		{
			get
			{
				return deviceId;
			}
			set
			{
				deviceId = value;
			}
		}

		/// <summary>
		/// 接收到数据的时候就触发的事件，示例详细参考API文档信息<br />
		/// An event that is triggered when data is received
		/// </summary>
		/// <remarks>
		/// 事件共有三个参数，sender指服务器本地的对象，为 <see cref="T:HslCommunication.Secs.SecsHsmsServer" /> 对象，session 指会话对象，网为 <see cref="T:HslCommunication.Core.Net.AppSession" />，message 为收到的原始数据 <see cref="T:HslCommunication.Secs.Types.SecsMessage" /> 对象
		/// </remarks>
		/// <example>
		/// </example>
		public event SecsMessageReceivedDelegate OnSecsMessageReceived;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public SecsHsmsServer()
		{
			incrementCount = new SoftIncrementCount(4294967295L, 0L);
			base.OnPipeMessageReceived += SecsHsmsServer_OnPipeMessageReceived;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SecsHsmsMessage();
		}

		private void SecsHsmsServer_OnPipeMessageReceived(PipeSession session, byte[] buffer)
		{
			SecsMessage secsMessage = new SecsMessage(buffer, 4);
			secsMessage.StringEncoding = stringEncoding;
			base.LogNet?.WriteDebug(ToString(), $"[{session.Communication}] {StringResources.Language.Receive}：{buffer.ToHexString(' ')}");
			if (secsMessage.StreamNo == 0 && secsMessage.FunctionNo == 0 && secsMessage.BlockNo % 2 == 1)
			{
				session.Communication.Send(Secs1.BuildHSMSMessage(ushort.MaxValue, 0, 0, (ushort)(secsMessage.BlockNo + 1), secsMessage.MessageID, null, wBit: false));
			}
			RaiseDataReceived(this, session, secsMessage);
		}

		/// <summary>
		/// 触发一个数据接收的事件信息<br />
		/// Event information that triggers a data reception
		/// </summary>
		/// <param name="source">数据的发送方</param>
		/// <param name="session">消息的会话对象信息</param>
		/// <param name="message">实际的数据信息</param>
		private void RaiseDataReceived(object source, PipeSession session, SecsMessage message)
		{
			this.OnSecsMessageReceived?.Invoke(source, session, message);
		}

		private OperateResult SendToCommunicationPipe(CommunicationPipe pipe, byte[] data)
		{
			base.LogNet?.WriteDebug(ToString(), $"[{pipe}] {StringResources.Language.Send}：{data.ToHexString(' ')}");
			return pipe.Send(data);
		}

		/// <inheritdoc cref="M:HslCommunication.Secs.SecsHsmsServer.SendByCommand(HslCommunication.Core.Net.PipeSession,HslCommunication.Secs.Types.SecsMessage,System.Byte,System.Byte,System.Byte[],System.Boolean)" />
		public OperateResult SendByCommand(PipeSession session, SecsMessage receiveMessage, byte stream, byte function, byte[] data)
		{
			byte[] data2 = Secs1.BuildHSMSMessage(receiveMessage.DeviceID, stream, function, 0, receiveMessage.MessageID, data, wBit: false);
			return SendToCommunicationPipe(session.Communication, data2);
		}

		/// <summary>
		/// 向指定的会话信息发送SECS格式的原始字节数据信息，session 为当前的会话对象，receiveMessage为接收到数据，后续的参数才是真实的返回数据
		/// </summary>
		/// <param name="session">当前的会话对象</param>
		/// <param name="receiveMessage">接收到的Secs数据</param>
		/// <param name="stream">功能码1</param>
		/// <param name="function">功能码2</param>
		/// <param name="data">原始的字节数据</param>
		/// <param name="wBit">是否必须回复讯息</param>
		/// <returns>是否发送成功</returns>
		public OperateResult SendByCommand(PipeSession session, SecsMessage receiveMessage, byte stream, byte function, byte[] data, bool wBit)
		{
			byte[] data2 = Secs1.BuildHSMSMessage(receiveMessage.DeviceID, stream, function, 0, receiveMessage.MessageID, data, wBit);
			return SendToCommunicationPipe(session.Communication, data2);
		}

		/// <inheritdoc cref="M:HslCommunication.Secs.SecsHsmsServer.SendByCommand(HslCommunication.Core.Net.PipeSession,HslCommunication.Secs.Types.SecsMessage,System.Byte,System.Byte,System.Byte[])" />
		public OperateResult SendByCommand(PipeSession session, SecsMessage receiveMessage, byte stream, byte function, SecsValue data)
		{
			return SendByCommand(session, receiveMessage, stream, function, (data == null) ? new byte[0] : data.ToSourceBytes(stringEncoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Secs.SecsHsmsServer.SendByCommand(HslCommunication.Core.Net.PipeSession,HslCommunication.Secs.Types.SecsMessage,System.Byte,System.Byte,System.Byte[],System.Boolean)" />
		public OperateResult SendByCommand(PipeSession session, SecsMessage receiveMessage, byte stream, byte function, SecsValue data, bool wBit)
		{
			return SendByCommand(session, receiveMessage, stream, function, (data == null) ? new byte[0] : data.ToSourceBytes(stringEncoding), wBit);
		}

		/// <inheritdoc cref="M:HslCommunication.Secs.SecsHsmsServer.PublishSecsMessage(System.Byte,System.Byte,HslCommunication.Secs.Types.SecsValue,System.Boolean)" />
		public OperateResult PublishSecsMessage(byte stream, byte function, SecsValue data)
		{
			return PublishSecsMessage(stream, function, data, wBit: false);
		}

		/// <summary>
		/// 发布数据到所有的在线客户端信息
		/// </summary>
		/// <param name="stream">功能码1</param>
		/// <param name="function">功能码2</param>
		/// <param name="data">数据对象</param>
		/// <param name="wBit">是否必须回复讯息</param>
		/// <returns>是否发送成功</returns>
		public OperateResult PublishSecsMessage(byte stream, byte function, SecsValue data, bool wBit)
		{
			if (data == null)
			{
				data = new SecsValue();
			}
			PipeSession[] pipeSessions = GetPipeSessions();
			for (int i = 0; i < pipeSessions.Length; i++)
			{
				byte[] data2 = Secs1.BuildHSMSMessage(deviceId, stream, function, 0, (uint)incrementCount.GetCurrentValue(), data.ToSourceBytes(stringEncoding), wBit);
				OperateResult operateResult = SendToCommunicationPipe(pipeSessions[i].Communication, data2);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"SecsHsmsServer[{base.Port}]";
		}
	}
}
