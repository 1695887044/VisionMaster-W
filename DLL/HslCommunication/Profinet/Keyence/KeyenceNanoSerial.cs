using System;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士KV上位链路串口通信的对象,适用于Nano系列串口数据,KV1000以及L20V通信模块，地址格式参考api文档<br />
	/// Keyence KV upper link serial communication object, suitable for Nano series serial data, and L20V communication module, please refer to api document for address format
	/// </summary>
	/// <remarks>
	/// 位读写的数据类型为 R,B,MR,LR,CR,VB,以及读定时器的计数器的触点，字读写的数据类型为 DM,EM,FM,ZF,W,TM,Z,AT,CM,VM 双字读写为T,C,TC,CC,TS,CS。如果想要读写扩展的缓存器，地址示例：unit=2;1000  前面的是单元编号，后面的是偏移地址<br />
	/// 注意：在端口 2 以多分支连接 KV-L21V 时，请一定加上站号。在将端口 2 设定为使用 RS-422A、 RS-485 时， KV-L21V 即使接收本站以外的带站号的指令，也将变为无应答，不返回响应消息。
	/// </remarks>
	public class KeyenceNanoSerial : DeviceSerialPort
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Keyence.KeyenceNanoSerialOverTcp.Station" />
		public byte Station { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Profinet.Keyence.KeyenceNanoSerialOverTcp.UseStation" />
		public bool UseStation { get; set; }

		/// <summary>
		/// 实例化基恩士的串口协议的通讯对象<br />
		/// Instantiate the communication object of Keyence's serial protocol
		/// </summary>
		public KeyenceNanoSerial()
		{
			base.ByteTransform = new RegularByteTransform();
			base.WordLength = 1;
			base.ByteTransform.IsStringReverseByteWord = true;
			LogMsgFormatBinary = false;
			base.ReceiveEmptyDataCount = 5;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new KeyenceNanoSerialMessage();
		}

		/// <inheritdoc />
		protected override OperateResult InitializationOnConnect()
		{
			OperateResult<byte[]> operateResult = ReadFromCoreServer(CommunicationPipe, KeyenceNanoHelper.GetConnectCmd(Station, UseStation), hasResponseData: true, usePackAndUnpack: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content.Length > 2 && operateResult.Content[0] == 67 && operateResult.Content[1] == 67)
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult("Check Failed: " + SoftBasic.ByteToHexString(operateResult.Content, ' '));
		}

		/// <inheritdoc />
		protected override OperateResult ExtraOnDisconnect()
		{
			OperateResult<byte[]> operateResult = ReadFromCoreServer(CommunicationPipe, KeyenceNanoHelper.GetDisConnectCmd(Station, UseStation), hasResponseData: true, usePackAndUnpack: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content.Length > 2 && operateResult.Content[0] == 67 && operateResult.Content[1] == 70)
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult("Check Failed: " + SoftBasic.ByteToHexString(operateResult.Content, ' '));
		}

		/// <inheritdoc />
		protected override async Task<OperateResult> InitializationOnConnectAsync()
		{
			return await Task.Run(() => InitializationOnConnect()).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc />
		protected override Task<OperateResult> ExtraOnDisconnectAsync()
		{
			return base.ExtraOnDisconnectAsync();
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return KeyenceNanoHelper.Read(this, address, length);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return KeyenceNanoHelper.Write(this, address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return KeyenceNanoHelper.ReadBool(this, address, length);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return KeyenceNanoHelper.Write(this, address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			return KeyenceNanoHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoSerial.Write(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadPlcType(HslCommunication.Core.IReadWriteDevice)" />
		[HslMqttApi("查询PLC的型号信息")]
		public OperateResult<KeyencePLCS> ReadPlcType()
		{
			return KeyenceNanoHelper.ReadPlcType(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadPlcMode(HslCommunication.Core.IReadWriteDevice)" />
		[HslMqttApi("读取当前PLC的模式，如果是0，代表 PROG模式或者梯形图未登录，如果为1，代表RUN模式")]
		public OperateResult<int> ReadPlcMode()
		{
			return KeyenceNanoHelper.ReadPlcMode(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.SetPlcDateTime(HslCommunication.Core.IReadWriteDevice,System.DateTime)" />
		[HslMqttApi("设置PLC的时间")]
		public OperateResult SetPlcDateTime(DateTime dateTime)
		{
			return KeyenceNanoHelper.SetPlcDateTime(this, dateTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadAddressAnnotation(HslCommunication.Core.IReadWriteDevice,System.String)" />
		[HslMqttApi("读取指定软元件的注释信息")]
		public OperateResult<string> ReadAddressAnnotation(string address)
		{
			return KeyenceNanoHelper.ReadAddressAnnotation(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.ReadExpansionMemory(HslCommunication.Core.IReadWriteDevice,System.Byte,System.UInt16,System.UInt16)" />
		[HslMqttApi("从扩展单元缓冲存储器连续读取指定个数的数据，单位为字")]
		public OperateResult<byte[]> ReadExpansionMemory(byte unit, ushort address, ushort length)
		{
			return KeyenceNanoHelper.ReadExpansionMemory(this, unit, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceNanoHelper.WriteExpansionMemory(HslCommunication.Core.IReadWriteDevice,System.Byte,System.UInt16,System.Byte[])" />
		[HslMqttApi("将原始字节数据写入到扩展的缓冲存储器，需要指定单元编号，偏移地址，写入的数据")]
		public OperateResult WriteExpansionMemory(byte unit, ushort address, byte[] value)
		{
			return KeyenceNanoHelper.WriteExpansionMemory(this, unit, address, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceNanoSerial[{base.PortName}:{base.BaudRate}]";
		}
	}
}
