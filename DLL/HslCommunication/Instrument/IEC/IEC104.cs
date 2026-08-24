using System;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Pipe;
using HslCommunication.Instrument.IEC.Helper;

namespace HslCommunication.Instrument.IEC
{
	/// <summary>
	/// IEC104规约实现的电力协议，支持总召唤，支持回调任意的突发上传事件
	/// </summary>
	/// <remarks>
	/// 手动绑定事件 <see cref="E:HslCommunication.Instrument.IEC.IEC104.OnIEC104MessageReceived" />，当收到设备的数据时就可以触发，如果需要总召唤，调用方法 <see cref="M:HslCommunication.Instrument.IEC.IEC104.TotalSubscriptions(System.Byte)" />
	/// </remarks>
	/// <example>
	/// 一般来说，实例化之后，绑定事件，连接即可，然后定时调用总召唤（定时调用时还可以判断是否连接成功）
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Instrument\IEC104Sample.cs" region="Sample1" title="自定义的使用" />
	/// 当然可以使用hsl辅助解析数据
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Instrument\IEC104Sample.cs" region="Sample2" title="便捷的解析" />
	/// </example>
	public class IEC104 : DeviceTcpNet
	{
		/// <summary>
		/// 类型信息资源
		/// </summary>
		public class TypeID
		{
			/// <summary>
			/// 单点遥信，带品质描述，不带时标
			/// </summary>
			public const byte M_SP_NA_1 = 1;

			/// <summary>
			/// 双点遥信，带品质描述，不带时标
			/// </summary>
			public const byte M_DP_NA_1 = 3;

			/// <summary>
			/// 步位置信息，带品质描述，不带时标
			/// </summary>
			public const byte M_ST_NA_1 = 5;

			/// <summary>
			/// 32比特串，带品质描述，不带时标
			/// </summary>
			public const byte M_BO_NA_1 = 7;

			/// <summary>
			/// 归一化遥测值，带品质描述，不带时标
			/// </summary>
			public const byte M_ME_NA_1 = 9;

			/// <summary>
			/// 标度化遥测值，带品质描述，不带时标
			/// </summary>
			public const byte M_ME_NB_1 = 11;

			/// <summary>
			/// 短浮点遥测值，带品质描述，不带时标
			/// </summary>
			public const byte M_ME_NC_1 = 13;

			/// <summary>
			/// 累计量，带品质描述，不带时标
			/// </summary>
			public const byte M_IT_NA_1 = 15;

			/// <summary>
			/// 成组单点遥信，只带变位标志
			/// </summary>
			public const byte M_PS_NA_1 = 20;

			/// <summary>
			/// 归一化遥测值，不带品质描述，不带时标
			/// </summary>
			public const byte M_ME_ND_1 = 21;

			/// <summary>
			/// 单点遥信，带品质描述，带绝对时标
			/// </summary>
			public const byte M_SP_TB_1 = 30;

			/// <summary>
			/// 双点遥信，带品质描述，带绝对时标
			/// </summary>
			public const byte M_DP_TB_1 = 31;

			/// <summary>
			/// 步位置信息，带品质描述，带绝对时标
			/// </summary>
			public const byte M_ST_TB_1 = 32;

			/// <summary>
			/// 32比特串，带品质描述，带绝对时标
			/// </summary>
			public const byte M_BO_TB_1 = 33;

			/// <summary>
			/// 归一化遥测值，带品质描述，带绝对时标
			/// </summary>
			public const byte M_ME_TD_1 = 34;

			/// <summary>
			/// 标度化遥测值，带品质描述，带绝对时标
			/// </summary>
			public const byte M_ME_TE_1 = 35;

			/// <summary>
			/// 短浮点遥测值，带品质描述，带绝对时标
			/// </summary>
			public const byte M_ME_TF_1 = 36;

			/// <summary>
			/// 累计量，带品质描述，带绝对时标
			/// </summary>
			public const byte M_IT_TB_1 = 37;

			/// <summary>
			/// 单点遥控，一个报文只有一个遥控信息体，不带时标
			/// </summary>
			public const byte C_SC_NA_1 = 45;

			/// <summary>
			/// 双点遥控，一个报文只有一个遥控信息体，不带时标
			/// </summary>
			public const byte C_DC_NA_1 = 46;

			/// <summary>
			/// 升降遥控，一个报文只有一个遥控信息体，不带时标
			/// </summary>
			public const byte C_RC_NA_1 = 47;

			/// <summary>
			/// 归一化设定值，一个报文只有一个设定值，不带时标
			/// </summary>
			public const byte C_SE_NA_1 = 48;

			/// <summary>
			/// 标度化设定值，一个报文只有一个设定值，不带时标
			/// </summary>
			public const byte C_SE_NB_1 = 49;

			/// <summary>
			/// 短浮点设定值，一个报文只有一个设定值，不带时标
			/// </summary>
			public const byte C_SE_NC_1 = 50;

			/// <summary>
			/// 32比特串设定，一个报文只有一个设定值，不带时标
			/// </summary>
			public const byte C_SE_ND_1 = 51;

			/// <summary>
			/// 单点遥控，一个报文只有一个遥控信息体，带时标
			/// </summary>
			public const byte C_SE_TA_1 = 58;

			/// <summary>
			/// 双点遥控，一个报文只有一个遥控信息体，带时标
			/// </summary>
			public const byte C_SE_TB_1 = 59;

			/// <summary>
			/// 升降遥控，一个报文只有一个遥控信息体，带时标
			/// </summary>
			public const byte C_SE_TC_1 = 60;

			/// <summary>
			/// 归一化设定值，一个报文只有一个设定值，带时标
			/// </summary>
			public const byte C_SE_TD_1 = 61;

			/// <summary>
			/// 标度化设定值，一个报文只有一个设定值，带时标
			/// </summary>
			public const byte C_SE_TE_1 = 62;

			/// <summary>
			/// 短浮点设定值，一个报文只有一个设定值，带时标
			/// </summary>
			public const byte C_SE_TF_1 = 63;

			/// <summary>
			/// 32比特串设定，一个报文只有一个设定值，带时标
			/// </summary>
			public const byte C_SE_TG_1 = 64;

			/// <summary>
			/// 归一化设定值，一个报文可以包含多个设定值，不带时标
			/// </summary>
			public const byte C_SE_NE_1 = 136;

			/// <summary>
			/// 初始化结束，报告厂站初始化完成
			/// </summary>
			public const byte M_EI_NA_1 = 70;

			/// <summary>
			/// 总召唤，带不同的限定词可以用于组召唤
			/// </summary>
			public const byte C_IC_NA_1 = 100;

			/// <summary>
			/// 累计量召唤，带不同的限定词可以用于组召唤
			/// </summary>
			public const byte C_CI_NA_1 = 101;

			/// <summary>
			/// 读命令，读取单个的信息对象值
			/// </summary>
			public const byte C_RD_NA_1 = 102;

			/// <summary>
			/// 时钟同步命令，需要通过测量通道延迟加以校正
			/// </summary>
			public const byte C_CS_NA_1 = 103;

			/// <summary>
			/// 复位进程命令，使用前需要双方验证
			/// </summary>
			public const byte C_RS_NA_1 = 105;

			/// <summary>
			/// 带时标的测试命令
			/// </summary>
			public const byte C_TS_TA_1 = 107;
		}

		private readonly SoftIncrementCount sendIncrementCount;

		private int receiveIncrementCount = 0;

		private int station = 1;

		/// <summary>
		/// 公共单元地址，在低版本远动程序中，站地址范围一般在1~254，255表示全局地址。新远动程序则可支持1~65534为站地址，而65535为全局地址。默认值为1
		/// </summary>
		public int Station
		{
			get
			{
				return station;
			}
			set
			{
				station = value;
			}
		}

		/// <summary>
		/// 当接收到了IEC104的消息触发的事件
		/// </summary>
		public event EventHandler<IEC104MessageEventArgs> OnIEC104MessageReceived;

		/// <summary>
		/// 实例化IEC104协议的通讯对象<br />
		/// Instantiate the communication object of the IEC104 protocol
		/// </summary>
		public IEC104()
		{
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform();
			sendIncrementCount = new SoftIncrementCount(32767L, 0L);
		}

		/// <summary>
		/// 指定ip地址和端口号来实例化一个默认的对象<br />
		/// Specify the IP address and port number to instantiate a default object
		/// </summary>
		/// <param name="ipAddress">IEC104的Ip地址</param>
		/// <param name="port">IEC104的端口, 默认是2404端口</param>
		public IEC104(string ipAddress, int port = 2404)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new IEC104Message();
		}

		/// <inheritdoc />
		protected override OperateResult InitializationOnConnect()
		{
			OperateResult operateResult = CommunicationPipe.Send(IECHelper.BuildFrameUMessage(7));
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			LogSendMessage(IECHelper.BuildFrameUMessage(7));
			OperateResult operateResult2 = CommunicationPipe.ReceiveMessage(GetNewNetMessage(), null, useActivePush: false, null, base.LogRevcMessage);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			CommunicationPipe.UseServerActivePush = true;
			sendIncrementCount.ResetCurrentValue(0L);
			receiveIncrementCount = 0;
			return base.InitializationOnConnect();
		}

		/// <inheritdoc />
		protected override async Task<OperateResult> InitializationOnConnectAsync()
		{
			OperateResult send1 = await CommunicationPipe.SendAsync(IECHelper.BuildFrameUMessage(7)).ConfigureAwait(continueOnCapturedContext: false);
			if (!send1.IsSuccess)
			{
				return send1;
			}
			LogSendMessage(IECHelper.BuildFrameUMessage(7));
			OperateResult recv1 = await CommunicationPipe.ReceiveMessageAsync(GetNewNetMessage(), null, useActivePush: false, null, base.LogRevcMessage).ConfigureAwait(continueOnCapturedContext: false);
			if (!recv1.IsSuccess)
			{
				return recv1;
			}
			CommunicationPipe.UseServerActivePush = true;
			sendIncrementCount.ResetCurrentValue(0L);
			receiveIncrementCount = 0;
			return await base.InitializationOnConnectAsync();
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, bool value)
		{
			byte[] array = new byte[10]
			{
				45,
				1,
				3,
				0,
				BitConverter.GetBytes(station)[0],
				BitConverter.GetBytes(station)[1],
				0,
				0,
				0,
				0
			};
			BitConverter.GetBytes(ushort.Parse(address)).CopyTo(array, 6);
			if (value)
			{
				array[9] = 1;
			}
			return SendFrameIMessage(array);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.IEC.IEC104.WriteIec(System.Byte,System.UInt16,System.UInt16,System.Byte[])" />
		public OperateResult WriteIec(byte type, ushort reason, ushort address, bool value)
		{
			return WriteIec(type, reason, address, (!value) ? new byte[1] : new byte[1] { 1 });
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.IEC.IEC104.WriteIec(System.Byte,System.UInt16,System.UInt16,System.Byte[])" />
		public OperateResult WriteIec(byte type, ushort reason, ushort address, short value)
		{
			return WriteIec(type, reason, address, BitConverter.GetBytes(value));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.IEC.IEC104.WriteIec(System.Byte,System.UInt16,System.UInt16,System.Byte[])" />
		public OperateResult WriteIec(byte type, ushort reason, ushort address, float value)
		{
			return WriteIec(type, reason, address, BitConverter.GetBytes(value));
		}

		/// <summary>
		/// 指定类型标识，传送原因，数据地址，数据值信息来写入到IEC的仪表中去
		/// </summary>
		/// <param name="type">类型标识</param>
		/// <param name="reason">传送原因</param>
		/// <param name="address">信息对象地址</param>
		/// <param name="value">值信息</param>
		/// <returns>是否发送到仪表成功</returns>
		public OperateResult WriteIec(byte type, ushort reason, ushort address, byte[] value)
		{
			return SendFrameIMessage(IECHelper.BuildWriteIec(type, reason, (ushort)station, address, value));
		}

		/// <inheritdoc />
		protected override bool DecideWhetherQAMessage(CommunicationPipe pipe, OperateResult<byte[]> receive)
		{
			if (!receive.IsSuccess)
			{
				return false;
			}
			LogRevcMessage(receive.Content);
			byte[] content = receive.Content;
			if (content.Length < 6)
			{
				return false;
			}
			if (content[2] != 1 || content[3] != 0)
			{
				if ((content[2] & 1) == 1)
				{
					byte[] array = IECHelper.BuildFrameUMessage(131);
					LogSendMessage(array);
					if (content[2] == 67)
					{
						pipe.Send(array);
					}
				}
				else
				{
					int num = (int)BitConverter.ToUInt16(content, 2) / 2;
					int num2 = (int)BitConverter.ToUInt16(content, 4) / 2;
					if (receiveIncrementCount == num)
					{
						receiveIncrementCount++;
						byte[] array2 = IECHelper.BuildFrameSMessage(receiveIncrementCount);
						LogSendMessage(array2);
						pipe.Send(array2);
					}
					if (receiveIncrementCount > 32767)
					{
						receiveIncrementCount = 0;
					}
					this.OnIEC104MessageReceived?.Invoke(this, new IEC104MessageEventArgs(content.RemoveBegin(6)));
				}
			}
			return false;
		}

		/// <summary>
		/// 以I消息的格式发送传入的原始字节数据，传入的消息为asdu信息
		/// </summary>
		/// <param name="asdu">ASDU报文信息</param>
		/// <returns>是否发送成功</returns>
		public OperateResult SendFrameIMessage(byte[] asdu)
		{
			int sendID = (int)sendIncrementCount.GetCurrentValue();
			int receiveID = receiveIncrementCount;
			byte[] array = IECHelper.BuildFrameIMessage(sendID, receiveID, asdu[0], asdu[1], base.ByteTransform.TransUInt16(asdu, 2), base.ByteTransform.TransUInt16(asdu, 4), asdu.RemoveBegin(6));
			LogSendMessage(array);
			if (CommunicationPipe.IsConnectError())
			{
				OperateResult operateResult = ConnectServer();
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			return CommunicationPipe.Send(array);
		}

		/// <summary>
		/// 向设备发送U帧消息的报文信息，传入功能码，STARTDT: 0x07, STOPDT: 0x13; TESTFR: 0x43
		/// </summary>
		/// <param name="controlField">功能码，STARTDT: 0x07, STOPDT: 0x13; TESTFR: 0x43</param>
		/// <returns>是否发送成功</returns>
		public OperateResult SendFrameUMessage(byte controlField)
		{
			byte[] array = IECHelper.BuildFrameUMessage(controlField);
			LogSendMessage(array);
			if (CommunicationPipe.IsConnectError())
			{
				OperateResult operateResult = ConnectServer();
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			return CommunicationPipe.Send(array);
		}

		/// <summary>
		/// 总召唤，支持设置总召唤代码，0x01: 定时主动上送,  0x14: 相应总召唤上送,  0x03: 越限上送
		/// </summary>
		/// <param name="code">总召唤的代码，0x01: 定时主动上送,  0x14: 相应总召唤上送,  0x03: 越限上送</param>
		/// <returns>是否召唤成功</returns>
		public OperateResult TotalSubscriptions(byte code = 20)
		{
			return TotalSubscriptions(code, 6);
		}

		/// <summary>
		/// 总召唤，支持设置总召唤代码，0x01: 定时主动上送,  0x14: 相应总召唤上送,  0x03: 越限上送
		/// </summary>
		/// <param name="code">总召唤的代码，0x01: 定时主动上送,  0x14: 相应总召唤上送,  0x03: 越限上送</param>
		/// <param name="reason">传送原因，通常可选 06:激活，07:激活确认</param>
		/// <returns>是否总召唤成功</returns>
		public OperateResult TotalSubscriptions(byte code, byte reason)
		{
			byte[] obj = new byte[10] { 100, 1, 0, 0, 1, 0, 0, 0, 0, 0 };
			obj[2] = reason;
			obj[9] = code;
			byte[] array = obj;
			array[4] = BitConverter.GetBytes(station)[0];
			array[5] = BitConverter.GetBytes(station)[1];
			return SendFrameIMessage(array);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"IEC104[{IpAddress}:{Port}]";
		}
	}
}
