using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;
using HslCommunication.Serial;

namespace HslCommunication.ModBus
{
	/// <summary>
	/// Modbus的虚拟服务器，同时支持Tcp，Rtu，Ascii的机制，支持线圈，离散输入，寄存器和输入寄存器的读写操作，同时支持掩码写入功能，可以用来当做系统的数据交换池<br />
	/// Modbus virtual server supports Tcp and Rtu mechanisms at the same time, supports read and write operations of coils, discrete inputs, r
	/// egisters and input registers, and supports mask write function, which can be used as a system data exchange pool
	/// </summary>
	/// <remarks>
	/// 可以基于本类实现一个功能复杂的modbus服务器，支持Modbus-Tcp，启动串口后，还支持Modbus-Rtu和Modbus-ASCII，会根据报文进行动态的适配。<br />
	/// 线圈，功能码对应01，05，15<br />
	/// 离散输入，功能码对应02，服务器写入离散输入的地址使用 x=2;100<br />
	/// 寄存器，功能码对应03，06，16<br />
	/// 输入寄存器，功能码对应04，输入寄存器在服务器写入使用地址 x=4;100<br />
	/// 掩码写入，功能码对应22，可以对字寄存器进行位操作<br />
	/// 特别说明1: <see cref="P:HslCommunication.ModBus.ModbusTcpServer.StationDataIsolation" /> 属性如果设置为 True 的话，则服务器为每一个站号（0-255）都创建一个数据区，客户端使用站号作为区分可以写入不同的数据区，服务器也可以读取不同数据区的数据，例如 s=2;100<br />
	/// 特别说明2: 如果多个modbus server使用485总线连接，那么属性 <see cref="P:HslCommunication.Core.Device.DeviceServer.ForceSerialReceiveOnce" /> 需要设置为 <c>True</c>
	/// </remarks>
	/// <example>
	/// <list type="number">
	/// <item>线圈，功能码对应01，05，15</item>
	/// <item>离散输入，功能码对应02</item>
	/// <item>寄存器，功能码对应03，06，16</item>
	/// <item>输入寄存器，功能码对应04，输入寄存器在服务器端可以实现读写的操作</item>
	/// <item>掩码写入，功能码对应22，可以对字寄存器进行位操作</item>
	/// </list>
	/// 读写的地址格式为富文本地址，具体请参照下面的示例代码。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Modbus\ModbusTcpServer.cs" region="ModbusTcpServerExample" title="ModbusTcpServer示例" />
	/// </example>
	public class ModbusTcpServer : DeviceServer
	{
		private List<ModBusMonitorAddress> subscriptions;

		private SimpleHybirdLock subcriptionHybirdLock;

		private ModbusDataDict dictModbusDataPool;

		private byte station = 1;

		private bool stationDataIsolation = false;

		private IByteTransform byteTransformSelf = new RegularByteTransform(DataFormat.CDAB);

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.DataFormat" />
		public DataFormat DataFormat
		{
			get
			{
				return base.ByteTransform.DataFormat;
			}
			set
			{
				base.ByteTransform.DataFormat = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.IsStringReverse" />
		public bool IsStringReverse
		{
			get
			{
				return base.ByteTransform.IsStringReverseByteWord;
			}
			set
			{
				base.ByteTransform.IsStringReverseByteWord = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.Station" />
		public byte Station
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
		/// 获取或设置是否对站号进行检测，当服务器只有一个站号的时候，设置为<c>True</c>表示客户端请求站号和服务器不一致的时候，拒绝返回数据给客户端，反之，始终会返回数据给客户端。<br />
		/// When the server has only one station number, setting <c>True</c> means that when the client requests that the station number is inconsistent with the server, 
		/// it refuses to return data to the client, and vice versa, it will always return data to the client.
		/// </summary>
		public bool StationCheck { get; set; } = true;


		/// <summary>
		/// 获取或设置当前的TCP服务器是否使用modbus-rtu报文进行通信，如果设置为 <c>True</c>，那么客户端需要使用 <see cref="T:HslCommunication.ModBus.ModbusRtuOverTcp" /><br />
		/// Get or set whether the current TCP server uses modbus-rtu messages for communication.
		/// If it is set to <c>True</c>, then the client needs to use <see cref="T:HslCommunication.ModBus.ModbusRtuOverTcp" />
		/// </summary>
		/// <remarks>
		/// 需要注意的是，本属性设置为<c>False</c>时，客户端使用<see cref="T:HslCommunication.ModBus.ModbusTcpNet" />，否则，使用<see cref="T:HslCommunication.ModBus.ModbusRtuOverTcp" />，不能混合使用
		/// </remarks>
		public bool UseModbusRtuOverTcp { get; set; }

		/// <summary>
		/// 获取或设置两次请求直接的延时时间，单位毫秒，默认是0，不发生延时，设置为20的话，可以有效防止有客户端疯狂进行请求而导致服务器的CPU占用率上升。<br />
		/// Get or set the direct delay time of two requests, in milliseconds, the default is 0, no delay occurs, if it is set to 20, 
		/// it can effectively prevent the client from making crazy requests and causing the server's CPU usage to increase.
		/// </summary>
		public int RequestDelayTime { get; set; }

		/// <inheritdoc cref="P:HslCommunication.ModBus.IModbus.EnableWriteMaskCode" />
		public bool EnableWriteMaskCode { get; set; } = true;


		/// <summary>
		/// 获取或设置是否启动站点数据隔离功能，默认为 <c>False</c>，也即虚拟服务器模拟一个站点服务器，客户端使用正确的站号才能通信。
		/// 当设置为 <c>True</c> 时，虚拟服务器模式256个站点，无论客户端使用的什么站点，都能读取或是写入对应站点里去。服务器同时也可以访问任意站点自身的数据。<br />
		/// Get or set whether to enable the site data isolation function, the default is <c>False</c>, that is, the virtual server simulates a site server, and the client can communicate with the correct site number.
		/// When set to<c> True</c>, 256 sites in virtual server mode, no matter what site the client uses, can read or write to the corresponding site.The server can also access any site's own data.
		/// </summary>
		/// <remarks>
		/// 当启动站号隔离之后，服务器访问自身的站号2的数据，地址写为 s=2;100
		/// </remarks>
		public bool StationDataIsolation
		{
			get
			{
				return stationDataIsolation;
			}
			set
			{
				stationDataIsolation = value;
				dictModbusDataPool.Set(value);
			}
		}

		/// <summary>
		/// 实例化一个Modbus Tcp及Rtu的服务器，支持数据读写操作
		/// </summary>
		public ModbusTcpServer()
		{
			dictModbusDataPool = new ModbusDataDict();
			subscriptions = new List<ModBusMonitorAddress>();
			subcriptionHybirdLock = new SimpleHybirdLock();
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			base.WordLength = 1;
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			return dictModbusDataPool.GetModbusPool(station).SaveToBytes();
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			dictModbusDataPool.GetModbusPool(station).LoadFromBytes(content, 0);
		}

		/// <inheritdoc cref="M:ModbusDataPool.ReadCoil(System.String)" />
		public bool ReadCoil(string address)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).ReadCoil(address);
		}

		/// <inheritdoc cref="M:ModbusDataPool.ReadCoil(System.String,System.UInt16)" />
		public bool[] ReadCoil(string address, ushort length)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).ReadCoil(address, length);
		}

		/// <inheritdoc cref="M:ModbusDataPool.WriteCoil(System.String,System.Boolean)" />
		public void WriteCoil(string address, bool data)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			dictModbusDataPool.GetModbusPool(b).WriteCoil(address, data);
		}

		/// <inheritdoc cref="M:ModbusDataPool.WriteCoil(System.String,System.Boolean[])" />
		public void WriteCoil(string address, bool[] data)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			dictModbusDataPool.GetModbusPool(b).WriteCoil(address, data);
		}

		/// <inheritdoc cref="M:ModbusDataPool.ReadDiscrete(System.String)" />
		public bool ReadDiscrete(string address)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).ReadDiscrete(address);
		}

		/// <inheritdoc cref="M:ModbusDataPool.ReadDiscrete(System.String,System.UInt16)" />
		public bool[] ReadDiscrete(string address, ushort length)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).ReadDiscrete(address, length);
		}

		/// <inheritdoc cref="M:ModbusDataPool.WriteDiscrete(System.String,System.Boolean)" />
		public void WriteDiscrete(string address, bool data)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			dictModbusDataPool.GetModbusPool(b).WriteDiscrete(address, data);
		}

		/// <inheritdoc cref="M:ModbusDataPool.WriteDiscrete(System.String,System.Boolean[])" />
		public void WriteDiscrete(string address, bool[] data)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			dictModbusDataPool.GetModbusPool(b).WriteDiscrete(address, data);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).Read(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).ReadBool(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			byte b = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			return dictModbusDataPool.GetModbusPool(b).Write(address, value);
		}

		/// <summary>
		/// 写入寄存器数据，指定字节数据
		/// </summary>
		/// <param name="address">起始地址，示例："100"，如果是输入寄存器："x=4;100"</param>
		/// <param name="high">高位数据</param>
		/// <param name="low">地位数据</param>
		public void Write(string address, byte high, byte low)
		{
			Write(address, new byte[2] { high, low });
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return UseModbusRtuOverTcp ? null : new ModbusTcpMessage();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length < 3)
			{
				return new OperateResult<byte[]>("Uknown Data：" + receive.ToHexString(' '));
			}
			if (RequestDelayTime > 0)
			{
				HslHelper.ThreadSleep(RequestDelayTime);
			}
			PipeSerialPort pipeSerialPort = session.Communication as PipeSerialPort;
			if (pipeSerialPort != null)
			{
				if (receive[0] == 58 && receive[1] >= 48 && receive[1] < 128)
				{
					OperateResult<byte[]> operateResult = ModbusInfo.TransAsciiPackCommandToCore(receive);
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
					byte[] content = operateResult.Content;
					if (!CheckModbusMessageLegal(content))
					{
						return new OperateResult<byte[]>("Unlegal Data：" + receive.ToHexString(' '));
					}
					if (content[0] != 0 && content[0] != byte.MaxValue && !StationDataIsolation && StationCheck && station != content[0])
					{
						return new OperateResult<byte[]>("Station not match Modbus-Ascii : " + SoftBasic.GetAsciiStringRender(receive));
					}
					byte[] value = ModbusInfo.TransModbusCoreToAsciiPackCommand(ReadFromModbusCore(content));
					if (content[0] != 0)
					{
						return OperateResult.CreateSuccessResult(value);
					}
					byte[] value2 = null;
					return OperateResult.CreateSuccessResult(value2);
				}
				if (SoftCRC16.CheckCRC16(receive))
				{
					byte[] array = receive.RemoveLast(2);
					if (!CheckModbusMessageLegal(array))
					{
						return new OperateResult<byte[]>("Unlegal Data：" + receive.ToHexString(' '));
					}
					if (array[0] != 0 && array[0] != byte.MaxValue && !StationDataIsolation && StationCheck && station != array[0])
					{
						return new OperateResult<byte[]>("Station not match Modbus-rtu : " + receive.ToHexString(' '));
					}
					byte[] value3 = ModbusInfo.PackCommandToRtu(ReadFromModbusCore(array));
					if (array[0] != 0)
					{
						return OperateResult.CreateSuccessResult(value3);
					}
					byte[] value4 = null;
					return OperateResult.CreateSuccessResult(value4);
				}
				return new OperateResult<byte[]>("CRC Check Failed : " + receive.ToHexString(' '));
			}
			if (UseModbusRtuOverTcp)
			{
				if (receive[0] == 58 && receive[1] >= 48 && receive[1] < 128)
				{
					OperateResult<byte[]> operateResult2 = ModbusInfo.TransAsciiPackCommandToCore(receive);
					if (!operateResult2.IsSuccess)
					{
						return new OperateResult<byte[]>("ASCII Check Failed: " + operateResult2.Message + " Source: " + receive.ToHexString(' '));
					}
					if (!CheckModbusMessageLegal(operateResult2.Content))
					{
						return new OperateResult<byte[]>("Modbus Ascii message check failed ");
					}
					if (operateResult2.Content[0] != 0 && !StationDataIsolation && StationCheck && station != operateResult2.Content[0])
					{
						return new OperateResult<byte[]>($"Station not match Modbus-ascii, Need {station} actual {operateResult2.Content[0]}");
					}
					byte[] value5 = ModbusInfo.TransModbusCoreToAsciiPackCommand(ReadFromModbusCore(operateResult2.Content));
					if (operateResult2.Content[0] != 0)
					{
						return OperateResult.CreateSuccessResult(value5);
					}
					byte[] value6 = null;
					return OperateResult.CreateSuccessResult(value6);
				}
				if (!SoftCRC16.CheckCRC16(receive))
				{
					return new OperateResult<byte[]>("CRC Check Failed: " + receive.ToHexString(' '));
				}
				byte[] array2 = receive.RemoveLast(2);
				if (!CheckModbusMessageLegal(array2))
				{
					return new OperateResult<byte[]>("Modbus rtu message check failed ");
				}
				if (array2[0] != 0 && array2[0] != byte.MaxValue && !StationDataIsolation && StationCheck && station != array2[0])
				{
					return new OperateResult<byte[]>($"Station not match Modbus-rtu, Need {station} actual {array2[0]}");
				}
				byte[] value7 = ModbusInfo.PackCommandToRtu(ReadFromModbusCore(array2));
				if (array2[0] != 0)
				{
					return OperateResult.CreateSuccessResult(value7);
				}
				byte[] value8 = null;
				return OperateResult.CreateSuccessResult(value8);
			}
			if (!CheckModbusMessageLegal(receive.RemoveBegin(6)))
			{
				return new OperateResult<byte[]>("Modbus message check failed");
			}
			ushort id = (ushort)(receive[0] * 256 + receive[1]);
			if (receive[6] != 0 && receive[6] != byte.MaxValue && !StationDataIsolation && StationCheck && station != receive[6])
			{
				return new OperateResult<byte[]>("Station not match Modbus-tcp ");
			}
			byte[] value9 = ModbusInfo.PackCommandToTcp(ReadFromModbusCore(receive.RemoveBegin(6)), id);
			if (receive[6] == 0)
			{
				byte[] value10 = null;
				return OperateResult.CreateSuccessResult(value10);
			}
			return OperateResult.CreateSuccessResult(value9);
		}

		/// <summary>
		/// 创建特殊的功能标识，然后返回该信息<br />
		/// Create a special feature ID and return this information
		/// </summary>
		/// <param name="modbusCore">modbus核心报文</param>
		/// <param name="error">错误码</param>
		/// <returns>携带错误码的modbus报文</returns>
		private byte[] CreateExceptionBack(byte[] modbusCore, byte error)
		{
			return new byte[3]
			{
				modbusCore[0],
				(byte)(modbusCore[1] + 128),
				error
			};
		}

		/// <summary>
		/// 创建返回消息<br />
		/// Create return message
		/// </summary>
		/// <param name="modbusCore">modbus核心报文</param>
		/// <param name="content">返回的实际数据内容</param>
		/// <returns>携带内容的modbus报文</returns>
		private byte[] CreateReadBack(byte[] modbusCore, byte[] content)
		{
			return SoftBasic.SpliceArray<byte>(new byte[3]
			{
				modbusCore[0],
				modbusCore[1],
				(byte)content.Length
			}, content);
		}

		/// <summary>
		/// 创建写入成功的反馈信号<br />
		/// Create feedback signal for successful write
		/// </summary>
		/// <param name="modbus">modbus核心报文</param>
		/// <returns>携带成功写入的信息</returns>
		private byte[] CreateWriteBack(byte[] modbus)
		{
			return modbus.SelectBegin(6);
		}

		private byte[] ReadCoilBack(byte[] modbus, string addressHead)
		{
			try
			{
				ushort num = byteTransformSelf.TransUInt16(modbus, 2);
				ushort num2 = byteTransformSelf.TransUInt16(modbus, 4);
				if (num + num2 > 65536)
				{
					return CreateExceptionBack(modbus, 2);
				}
				if (num2 > 2040)
				{
					return CreateExceptionBack(modbus, 3);
				}
				bool[] content = dictModbusDataPool.GetModbusPool(modbus[0]).ReadBool(addressHead + num, num2).Content;
				return CreateReadBack(modbus, SoftBasic.BoolArrayToByte(content));
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpReadCoilException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] ReadRegisterBack(byte[] modbus, string addressHead)
		{
			try
			{
				ushort num = byteTransformSelf.TransUInt16(modbus, 2);
				ushort num2 = byteTransformSelf.TransUInt16(modbus, 4);
				if (num + num2 > 65536)
				{
					return CreateExceptionBack(modbus, 2);
				}
				if (num2 > 127)
				{
					return CreateExceptionBack(modbus, 3);
				}
				byte[] content = dictModbusDataPool.GetModbusPool(modbus[0]).Read(addressHead + num, num2).Content;
				return CreateReadBack(modbus, content);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpReadRegisterException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] ReadWriteRegisterBack(byte[] modbus, string addressHead)
		{
			try
			{
				byte[] array = new byte[6]
				{
					modbus[0],
					3,
					modbus[2],
					modbus[3],
					modbus[4],
					modbus[5]
				};
				byte[] array2 = ReadRegisterBack(array, addressHead);
				if (array2[1] > 128)
				{
					return array;
				}
				byte[] array3 = SoftBasic.SpliceArray<byte>(new byte[2]
				{
					modbus[0],
					16
				}, modbus.RemoveBegin(6));
				byte[] array4 = WriteRegisterBack(array3);
				if (array4[1] > 128)
				{
					return array3;
				}
				array2[1] = modbus[1];
				return array2;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpFunctionCodeReadWriteException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] WriteOneCoilBack(byte[] modbus)
		{
			try
			{
				if (!base.EnableWrite)
				{
					return CreateExceptionBack(modbus, 4);
				}
				ushort num = byteTransformSelf.TransUInt16(modbus, 2);
				if (modbus[4] == byte.MaxValue && modbus[5] == 0)
				{
					dictModbusDataPool.GetModbusPool(modbus[0]).Write(num.ToString(), new bool[1] { true });
				}
				else if (modbus[4] == 0 && modbus[5] == 0)
				{
					dictModbusDataPool.GetModbusPool(modbus[0]).Write(num.ToString(), new bool[1]);
				}
				return CreateWriteBack(modbus);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpWriteCoilException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] WriteOneRegisterBack(byte[] modbus)
		{
			try
			{
				if (!base.EnableWrite)
				{
					return CreateExceptionBack(modbus, 4);
				}
				ushort address = byteTransformSelf.TransUInt16(modbus, 2);
				short content = ReadInt16(address.ToString()).Content;
				dictModbusDataPool.GetModbusPool(modbus[0]).Write(address.ToString(), new byte[2]
				{
					modbus[4],
					modbus[5]
				});
				short content2 = ReadInt16(address.ToString()).Content;
				OnRegisterBeforWrite(address, content, content2);
				return CreateWriteBack(modbus);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpWriteRegisterException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] WriteCoilsBack(byte[] modbus)
		{
			try
			{
				if (!base.EnableWrite)
				{
					return CreateExceptionBack(modbus, 4);
				}
				ushort num = byteTransformSelf.TransUInt16(modbus, 2);
				ushort num2 = byteTransformSelf.TransUInt16(modbus, 4);
				if (num + num2 > 65536)
				{
					return CreateExceptionBack(modbus, 2);
				}
				if (num2 > 2040)
				{
					return CreateExceptionBack(modbus, 3);
				}
				dictModbusDataPool.GetModbusPool(modbus[0]).Write(num.ToString(), modbus.RemoveBegin(7).ToBoolArray(num2));
				return CreateWriteBack(modbus);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpWriteCoilException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] WriteRegisterBack(byte[] modbus)
		{
			try
			{
				if (!base.EnableWrite)
				{
					return CreateExceptionBack(modbus, 4);
				}
				ushort num = byteTransformSelf.TransUInt16(modbus, 2);
				ushort num2 = byteTransformSelf.TransUInt16(modbus, 4);
				if (num + num2 > 65536)
				{
					return CreateExceptionBack(modbus, 2);
				}
				if (num2 > 127)
				{
					return CreateExceptionBack(modbus, 3);
				}
				byte[] content = dictModbusDataPool.GetModbusPool(modbus[0]).Read(num.ToString(), num2).Content;
				dictModbusDataPool.GetModbusPool(modbus[0]).Write(num.ToString(), modbus.RemoveBegin(7));
				MonitorAddress[] array = new MonitorAddress[num2];
				for (ushort num3 = 0; num3 < num2; num3 = (ushort)(num3 + 1))
				{
					short valueOrigin = base.ByteTransform.TransInt16(content, num3 * 2);
					short valueNew = base.ByteTransform.TransInt16(modbus, num3 * 2 + 7);
					array[num3] = new MonitorAddress
					{
						Address = (ushort)(num + num3),
						ValueOrigin = valueOrigin,
						ValueNew = valueNew
					};
				}
				for (int i = 0; i < array.Length; i++)
				{
					OnRegisterBeforWrite(array[i].Address, array[i].ValueOrigin, array[i].ValueNew);
				}
				return CreateWriteBack(modbus);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpWriteRegisterException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		private byte[] WriteMaskRegisterBack(byte[] modbus)
		{
			try
			{
				if (!base.EnableWrite)
				{
					return CreateExceptionBack(modbus, 4);
				}
				if (!EnableWriteMaskCode)
				{
					return CreateExceptionBack(modbus, 1);
				}
				ushort address = byteTransformSelf.TransUInt16(modbus, 2);
				int num = base.ByteTransform.TransUInt16(modbus, 4);
				int num2 = base.ByteTransform.TransUInt16(modbus, 6);
				int content = ReadInt16($"s={modbus[0]};" + address).Content;
				short num3 = (short)((content & num) | num2);
				Write($"s={modbus[0]};" + address, num3);
				MonitorAddress monitorAddress = default(MonitorAddress);
				monitorAddress.Address = address;
				monitorAddress.ValueOrigin = (short)content;
				monitorAddress.ValueNew = num3;
				MonitorAddress monitorAddress2 = monitorAddress;
				OnRegisterBeforWrite(monitorAddress2.Address, monitorAddress2.ValueOrigin, monitorAddress2.ValueNew);
				return modbus;
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), StringResources.Language.ModbusTcpWriteRegisterException, ex);
				return CreateExceptionBack(modbus, 4);
			}
		}

		/// <summary>
		/// 新增一个数据监视的任务，针对的是寄存器地址的数据<br />
		/// Added a data monitoring task for data at register addresses
		/// </summary>
		/// <param name="monitor">监视地址对象</param>
		public void AddSubcription(ModBusMonitorAddress monitor)
		{
			subcriptionHybirdLock.Enter();
			subscriptions.Add(monitor);
			subcriptionHybirdLock.Leave();
		}

		/// <summary>
		/// 移除一个数据监视的任务<br />
		/// Remove a data monitoring task
		/// </summary>
		/// <param name="monitor">监视地址对象</param>
		public void RemoveSubcrption(ModBusMonitorAddress monitor)
		{
			subcriptionHybirdLock.Enter();
			subscriptions.Remove(monitor);
			subcriptionHybirdLock.Leave();
		}

		/// <summary>
		/// 在数据变更后，进行触发是否产生订阅<br />
		/// Whether to generate a subscription after triggering data changes
		/// </summary>
		/// <param name="address">数据地址</param>
		/// <param name="before">修改之前的数</param>
		/// <param name="after">修改之后的数</param>
		private void OnRegisterBeforWrite(ushort address, short before, short after)
		{
			subcriptionHybirdLock.Enter();
			for (int i = 0; i < subscriptions.Count; i++)
			{
				if (subscriptions[i].Address == address)
				{
					subscriptions[i].SetValue(after);
					if (before != after)
					{
						subscriptions[i].SetChangeValue(before, after);
					}
				}
			}
			subcriptionHybirdLock.Leave();
		}

		/// <summary>
		/// 检测当前的Modbus接收的指定是否是合法的<br />
		/// Check if the current Modbus datad designation is valid
		/// </summary>
		/// <param name="buffer">缓存数据</param>
		/// <returns>是否合格</returns>
		private bool CheckModbusMessageLegal(byte[] buffer)
		{
			bool flag = false;
			switch (buffer[1])
			{
			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
				flag = buffer.Length == 6;
				break;
			case 15:
			case 16:
				flag = buffer.Length > 6 && buffer[6] == buffer.Length - 7;
				break;
			case 22:
				flag = buffer.Length == 8;
				break;
			default:
				flag = true;
				break;
			}
			if (!flag)
			{
				base.LogNet?.WriteError(ToString(), "Receive Nosense Modbus-rtu : " + buffer.ToHexString(' '));
			}
			return flag;
		}

		/// <summary>
		/// Modbus核心数据交互方法，允许重写自己来实现，报文只剩下核心的Modbus信息，去除了MPAB报头信息<br />
		/// The Modbus core data interaction method allows you to rewrite it to achieve the message. 
		/// Only the core Modbus information is left in the message, and the MPAB header information is removed.
		/// </summary>
		/// <param name="modbusCore">核心的Modbus报文</param>
		/// <returns>进行数据交互之后的结果</returns>
		protected virtual byte[] ReadFromModbusCore(byte[] modbusCore)
		{
			return modbusCore[1] switch
			{
				1 => ReadCoilBack(modbusCore, string.Empty), 
				2 => ReadCoilBack(modbusCore, "x=2;"), 
				3 => ReadRegisterBack(modbusCore, string.Empty), 
				4 => ReadRegisterBack(modbusCore, "x=4;"), 
				5 => WriteOneCoilBack(modbusCore), 
				6 => WriteOneRegisterBack(modbusCore), 
				15 => WriteCoilsBack(modbusCore), 
				16 => WriteRegisterBack(modbusCore), 
				22 => WriteMaskRegisterBack(modbusCore), 
				23 => ReadWriteRegisterBack(modbusCore, string.Empty), 
				_ => CreateExceptionBack(modbusCore, 1), 
			};
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int dataLength)
		{
			if (dataLength > 5)
			{
				if (ModbusInfo.CheckAsciiReceiveDataComplete(buffer, dataLength))
				{
					return true;
				}
				if (ModbusInfo.CheckServerRtuReceiveDataComplete(buffer.SelectBegin(dataLength)))
				{
					return true;
				}
			}
			return false;
		}

		/// <inheritdoc />
		protected override string GetLogTextFromBinary(PipeSession session, byte[] content)
		{
			if (session.Communication is PipeSerialPort)
			{
				if (content[0] == 58 && content[1] >= 48 && content[1] < 128)
				{
					return "[Ascii] " + SoftBasic.GetAsciiStringRender(content);
				}
				return "[Rtu] " + content.ToHexString(' ');
			}
			if (session.Communication is PipeTcpNet && content != null && content.Length > 2 && content[0] == 58 && content[1] >= 48 && content[1] < 128)
			{
				return "[Ascii] " + SoftBasic.GetAsciiStringRender(content);
			}
			return base.GetLogTextFromBinary(session, content);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				subcriptionHybirdLock?.Dispose();
				subscriptions?.Clear();
				dictModbusDataPool?.Dispose();
				GC.Collect();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt32Array", "")]
		public override OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt32Array", "")]
		public override OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String,System.UInt16)" />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt64Array", "")]
		public override OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt64Array", "")]
		public override OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransDouble(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int32[])" />
		[HslMqttApi("WriteInt32Array", "")]
		public override OperateResult Write(string address, int[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt32[])" />
		[HslMqttApi("WriteUInt32Array", "")]
		public override OperateResult Write(string address, uint[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Single[])" />
		[HslMqttApi("WriteFloatArray", "")]
		public override OperateResult Write(string address, float[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int64[])" />
		[HslMqttApi("WriteInt64Array", "")]
		public override OperateResult Write(string address, long[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt64[])" />
		[HslMqttApi("WriteUInt64Array", "")]
		public override OperateResult Write(string address, ulong[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double[])" />
		[HslMqttApi("WriteDoubleArray", "")]
		public override OperateResult Write(string address, double[] values)
		{
			IByteTransform byteTransform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return Write(address, byteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 2)), (byte[] m) => transform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, (ushort)(length * base.WordLength * 4)), (byte[] m) => transform.TransDouble(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int32[])" />
		public override async Task<OperateResult> WriteAsync(string address, int[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt32[])" />
		public override async Task<OperateResult> WriteAsync(string address, uint[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Single[])" />
		public override async Task<OperateResult> WriteAsync(string address, float[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int64[])" />
		public override async Task<OperateResult> WriteAsync(string address, long[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt64[])" />
		public override async Task<OperateResult> WriteAsync(string address, ulong[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Double[])" />
		public override async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await WriteAsync(value: HslHelper.ExtractTransformParameter(ref address, base.ByteTransform).TransByte(values), address: address);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ModbusTcpServer[{base.Port}]";
		}
	}
}
