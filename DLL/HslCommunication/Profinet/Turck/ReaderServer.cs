using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;

namespace HslCommunication.Profinet.Turck
{
	/// <summary>
	/// 图尔克reader协议的虚拟服务器
	/// </summary>
	public class ReaderServer : DeviceServer
	{
		private int bytesOfBlock = 8;

		private SoftBuffer buffer;

		private const int DataPoolLength = 65536;

		/// <summary>
		/// 获取或设置每个block占用的字节数信息
		/// </summary>
		public int BytesOfBlock
		{
			get
			{
				return bytesOfBlock;
			}
			set
			{
				bytesOfBlock = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的图尔克虚拟服务器
		/// </summary>
		public ReaderServer()
		{
			base.WordLength = 2;
			base.ByteTransform = new ReverseBytesTransform();
			buffer = new SoftBuffer(65536);
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new TurckReaderMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (!ReaderNet.CheckCRC(receive, receive.Length - 2))
			{
				return OperateResult.CreateSuccessResult(ReaderNet.PackReaderCommand(new byte[5]
				{
					receive[3],
					131,
					0,
					1,
					0
				}));
			}
			CommunicationPipe communication = session.Communication;
			byte[] array = null;
			if (receive[3] == 104)
			{
				byte[] array2 = ReaderNet.PackReaderCommand(new byte[2] { 104, 137 });
				LogSendMessage(array2, session);
				communication.Send(array2);
				array = ReadByMessage(receive);
			}
			else if (receive[3] == 105)
			{
				byte[] array3 = ReaderNet.PackReaderCommand(new byte[2] { 105, 137 });
				LogSendMessage(array3, session);
				communication.Send(array3);
				array = WriteByMessage(receive);
			}
			else
			{
				if (receive[3] != 112)
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
				}
				byte[] array4 = ReaderNet.PackReaderCommand(new byte[2] { 112, 137 });
				LogSendMessage(array4, session);
				communication.Send(array4);
				byte[] obj = new byte[15]
				{
					112, 138, 113, 38, 208, 229, 215, 1, 8, 224,
					0, 0, 249, 0, 116
				};
				obj[13] = (byte)(bytesOfBlock - 1);
				array = ReaderNet.PackReaderCommand(obj);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		private byte[] ReadByMessage(byte[] receive)
		{
			byte[] bytes = buffer.GetBytes(receive[5] * bytesOfBlock, (receive[6] + 1) * bytesOfBlock);
			return ReaderNet.PackReaderCommand(SoftBasic.SpliceArray<byte>(new byte[2]
			{
				receive[3],
				154
			}, bytes));
		}

		private byte[] WriteByMessage(byte[] receive)
		{
			if (!base.EnableWrite)
			{
				return ReaderNet.PackReaderCommand(new byte[5]
				{
					receive[3],
					131,
					18,
					0,
					0
				});
			}
			if ((receive[6] + 1) * bytesOfBlock != receive.Length - 9)
			{
				return ReaderNet.PackReaderCommand(new byte[5]
				{
					receive[3],
					131,
					1,
					0,
					0
				});
			}
			buffer.SetBytes(receive.SelectMiddle(7, receive.Length - 9), receive[5] * bytesOfBlock);
			return ReaderNet.PackReaderCommand(new byte[2]
			{
				receive[3],
				138
			});
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ReaderServer[{base.Port}]";
		}
	}
}
