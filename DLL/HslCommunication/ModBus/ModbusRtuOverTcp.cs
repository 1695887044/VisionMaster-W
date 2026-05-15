using System;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.ModBus
{
	/// <inheritdoc cref="T:HslCommunication.ModBus.ModbusRtu" />
	public class ModbusRtuOverTcp : DeviceTcpNet, IModbus, IReadWriteDevice, IReadWriteNet
	{
		private byte station = 1;

		private bool isAddressStartWithZero = true;

		private Func<string, byte, OperateResult<string>> addressMapping = (string address, byte modbusCode) => OperateResult.CreateSuccessResult(address);

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.AddressStartWithZero" />
		public bool AddressStartWithZero
		{
			get
			{
				return isAddressStartWithZero;
			}
			set
			{
				isAddressStartWithZero = value;
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

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusRtu.Crc16CheckEnable" />
		public bool Crc16CheckEnable { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusRtu.StationCheckMacth" />
		public bool StationCheckMacth { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.ModBus.IModbus.EnableWriteMaskCode" />
		public bool EnableWriteMaskCode { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.ModBus.IModbus.BroadcastStation" />
		public int BroadcastStation { get; set; } = -1;


		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtu.#ctor" />
		public ModbusRtuOverTcp()
		{
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			base.WordLength = 1;
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.#ctor(System.String,System.Int32,System.Byte)" />
		public ModbusRtuOverTcp(string ipAddress, int port = 502, byte station = 1)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
			this.station = station;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new ModbusRtuMessage(StationCheckMacth);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.IModbus.TranslateToModbusAddress(System.String,System.Byte)" />
		public virtual OperateResult<string> TranslateToModbusAddress(string address, byte modbusCode)
		{
			if (addressMapping != null)
			{
				return addressMapping(address, modbusCode);
			}
			return OperateResult.CreateSuccessResult(address);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.IModbus.RegisteredAddressMapping(System.Func{System.String,System.Byte,HslCommunication.OperateResult{System.String}})" />
		public void RegisteredAddressMapping(Func<string, byte, OperateResult<string>> mapping)
		{
			addressMapping = mapping;
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			return ModbusInfo.PackCommandToRtu(command);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			return ModbusHelper.ExtraRtuResponseContent(send, response, Crc16CheckEnable, BroadcastStation);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(byte[] send)
		{
			if (BroadcastStation >= 0 && send[0] == BroadcastStation)
			{
				return ReadFromCoreServer(send, hasResponseData: false, usePackAndUnpack: true);
			}
			return base.ReadFromCoreServer(send);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.ReadFromCoreServer(System.Byte[])" />
		public override async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(byte[] send)
		{
			if (BroadcastStation >= 0 && send[0] == BroadcastStation)
			{
				return ReadFromCoreServer(send, hasResponseData: false, usePackAndUnpack: true);
			}
			return await base.ReadFromCoreServerAsync(send);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadCoil(System.String)" />
		public OperateResult<bool> ReadCoil(string address)
		{
			return ReadBool(address);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadCoil(System.String,System.UInt16)" />
		public OperateResult<bool[]> ReadCoil(string address, ushort length)
		{
			return ReadBool(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadDiscrete(System.String)" />
		public OperateResult<bool> ReadDiscrete(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadDiscrete(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadDiscrete(System.String,System.UInt16)" />
		public OperateResult<bool[]> ReadDiscrete(string address, ushort length)
		{
			return ModbusHelper.ReadBoolHelper(this, address, length, 2);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return ModbusHelper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.ReadWrite(HslCommunication.ModBus.IModbus,System.String,System.UInt16,System.String,System.Byte[])" />
		[HslMqttApi("ReadWrite", "Use 0x17 function code to write and read data at the same time, and use a message to implement it")]
		public OperateResult<byte[]> ReadWrite(string readAddress, ushort length, string writeAddress, byte[] value)
		{
			return ModbusHelper.ReadWrite(this, readAddress, length, writeAddress, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return ModbusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Int16)" />
		[HslMqttApi("WriteInt16", "")]
		public override OperateResult Write(string address, short value)
		{
			return ModbusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.UInt16)" />
		[HslMqttApi("WriteUInt16", "")]
		public override OperateResult Write(string address, ushort value)
		{
			return ModbusHelper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteMask(System.String,System.UInt16,System.UInt16)" />
		[HslMqttApi("WriteMask", "")]
		public OperateResult WriteMask(string address, ushort andMask, ushort orMask)
		{
			return ModbusHelper.WriteMask(this, address, andMask, orMask);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.Write(System.String,System.Int16)" />
		public OperateResult WriteOneRegister(string address, short value)
		{
			return Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.Write(System.String,System.UInt16)" />
		public OperateResult WriteOneRegister(string address, ushort value)
		{
			return Write(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadCoilAsync(System.String)" />
		public async Task<OperateResult<bool>> ReadCoilAsync(string address)
		{
			return await ReadBoolAsync(address);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadCoilAsync(System.String,System.UInt16)" />
		public async Task<OperateResult<bool[]>> ReadCoilAsync(string address, ushort length)
		{
			return await ReadBoolAsync(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadDiscreteAsync(System.String)" />
		public async Task<OperateResult<bool>> ReadDiscreteAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadDiscreteAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadDiscreteAsync(System.String,System.UInt16)" />
		public async Task<OperateResult<bool[]>> ReadDiscreteAsync(string address, ushort length)
		{
			return await ReadBoolHelperAsync(address, length, 2);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await ModbusHelper.ReadAsync(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteAsync(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await ModbusHelper.WriteAsync(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.Write(System.String,System.Int16)" />
		public override async Task<OperateResult> WriteAsync(string address, short value)
		{
			return await ModbusHelper.WriteAsync(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.Write(System.String,System.UInt16)" />
		public override async Task<OperateResult> WriteAsync(string address, ushort value)
		{
			return await ModbusHelper.WriteAsync(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteOneRegisterAsync(System.String,System.Int16)" />
		public async Task<OperateResult> WriteOneRegisterAsync(string address, short value)
		{
			return await WriteAsync(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteOneRegisterAsync(System.String,System.UInt16)" />
		public async Task<OperateResult> WriteOneRegisterAsync(string address, ushort value)
		{
			return await WriteAsync(address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusRtuOverTcp.WriteMask(System.String,System.UInt16,System.UInt16)" />
		public async Task<OperateResult> WriteMaskAsync(string address, ushort andMask, ushort orMask)
		{
			return await Task.Run(() => WriteMask(address, andMask, orMask));
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return ModbusHelper.ReadBoolHelper(this, address, length, 1);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] values)
		{
			return ModbusHelper.Write(this, address, values);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.Write(System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			return ModbusHelper.Write(this, address, value);
		}

		private async Task<OperateResult<bool[]>> ReadBoolHelperAsync(string address, ushort length, byte function)
		{
			return await ModbusHelper.ReadBoolHelperAsync(this, address, length, function);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.ReadBoolAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await ReadBoolHelperAsync(address, length, 1);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteAsync(System.String,System.Boolean[])" />
		public override async Task<OperateResult> WriteAsync(string address, bool[] values)
		{
			return await ModbusHelper.WriteAsync(this, address, values);
		}

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpNet.WriteAsync(System.String,System.Boolean)" />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await ModbusHelper.WriteAsync(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt32Array", "")]
		public override OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt32Array", "")]
		public override OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String,System.UInt16)" />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt64Array", "")]
		public override OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt64Array", "")]
		public override OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransDouble(m, 0, length));
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
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => transform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String,System.UInt16)" />
		public override async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			IByteTransform transform = HslHelper.ExtractTransformParameter(ref address, base.ByteTransform);
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => transform.TransDouble(m, 0, length));
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
			return $"ModbusRtuOverTcp[{IpAddress}:{Port}]";
		}
	}
}
