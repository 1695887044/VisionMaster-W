using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Core.Device
{
	/// <summary>
	/// 所有设备的基类信息
	/// </summary>
	public class DeviceCommunication : BinaryCommunication, IReadWriteDevice, IReadWriteNet, IDisposable
	{
		private bool disposedValue = false;

		private IByteTransform byteTransform = new RegularByteTransform();

		/// <summary>
		/// 一个字单位的数据表示的地址长度，西门子为2，三菱，欧姆龙，modbusTcp就为1，AB PLC无效<br />
		/// The address length represented by one word of data, Siemens is 2, Mitsubishi, Omron, modbusTcp is 1, AB PLC is invalid
		/// </summary>
		/// <remarks>
		/// 对设备来说，一个地址的数据对应的字节数，或是1个字节或是2个字节，4个字节，通常是这四个选择，当设置为0时，则表示4字节的地址长度信息<br />
		/// For the device, the number of bytes corresponding to the data of an address, either 1 byte or 2 bytes, 4 bytes, usually these four choices, when set to 0, it means 4 words Section address length information
		/// </remarks>
		protected ushort WordLength { get; set; } = 1;


		/// <summary>
		/// 当前的数据变换机制，当你需要从字节数据转换类型数据的时候需要。<br />
		/// The current data transformation mechanism is required when you need to convert type data from byte data.
		/// </summary>
		/// <remarks>
		/// 在HSL里提供了三种数据变换机制，分别是 <see cref="T:HslCommunication.Core.RegularByteTransform" />, <see cref="T:HslCommunication.Core.ReverseBytesTransform" />,
		/// <see cref="T:HslCommunication.Core.ReverseWordTransform" />，各自的<see cref="T:HslCommunication.Core.DataFormat" />属性也可以自定调整，基本满足所有的情况使用。<br />
		/// Three data transformation mechanisms are provided in HSL, namely <see cref="T:HslCommunication.Core.RegularByteTransform" />, <see cref="T:HslCommunication.Core.ReverseBytesTransform" />, 
		/// <see cref="T:HslCommunication.Core.ReverseWordTransform" />, and their respective <see cref="T:HslCommunication.Core.DataFormat" /> property can also be adjusted by itself, basically satisfying all situations.
		/// </remarks>
		/// <example>
		/// 主要是用来转换数据类型的，下面仅仅演示了2个方法，其他的类型转换，类似处理。
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkDoubleBase.cs" region="ByteTransform" title="ByteTransform示例" />
		/// </example>
		public IByteTransform ByteTransform
		{
			get
			{
				return byteTransform;
			}
			set
			{
				byteTransform = value;
			}
		}

		/// <summary>
		/// 默认的无参构造函数 <br />
		/// Default no-parameter constructor
		/// </summary>
		public DeviceCommunication()
		{
		}

		/// <summary>
		/// 一个字单位的数据表示的地址长度，西门子为2，三菱，欧姆龙，modbusTcp就为1，AB PLC无效<br />
		/// The address length represented by one word of data, Siemens is 2, Mitsubishi, Omron, modbusTcp is 1, AB PLC is invalid
		/// </summary>
		/// <remarks>
		/// 对设备来说，一个地址的数据对应的字节数，或是1个字节或是2个字节，通常是这两个选择。<br />
		/// 当前也可以重写来根据不同的地址动态控制不同的地址长度，比如有的地址是一个地址一个字节的，有的地址是一个地址两个字节的
		/// </remarks>
		/// <param name="address">读取的设备的地址信息</param>
		/// <param name="length">读取的数据长度信息</param>
		/// <param name="dataTypeLength">数据类型的字节长度信息，比如short, 就是2，int,float就是4</param>
		protected virtual ushort GetWordLength(string address, int length, int dataTypeLength)
		{
			if (WordLength == 0)
			{
				int num = length * dataTypeLength * 2 / 4;
				return (ushort)((num == 0) ? 1 : ((ushort)num));
			}
			return (ushort)(WordLength * length * dataTypeLength);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public virtual OperateResult<byte[]> Read(string address, ushort length)
		{
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public virtual OperateResult Write(string address, byte[] value)
		{
			return new OperateResult(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public virtual OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String)" />
		[HslMqttApi("ReadBool", "")]
		public virtual OperateResult<bool> ReadBool(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadBool(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public virtual OperateResult Write(string address, bool[] value)
		{
			return new OperateResult(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public virtual OperateResult Write(string address, bool value)
		{
			return Write(address, new bool[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadCustomer``1(System.String)" />
		public OperateResult<T> ReadCustomer<T>(string address) where T : IDataTransfer, new()
		{
			return ReadWriteNetHelper.ReadCustomer<T>(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadCustomer``1(System.String,``0)" />
		public OperateResult<T> ReadCustomer<T>(string address, T obj) where T : IDataTransfer, new()
		{
			return ReadWriteNetHelper.ReadCustomer(this, address, obj);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteCustomer``1(System.String,``0)" />
		public OperateResult WriteCustomer<T>(string address, T data) where T : IDataTransfer, new()
		{
			return ReadWriteNetHelper.WriteCustomer(this, address, data);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Read``1" />
		public virtual OperateResult<T> Read<T>() where T : class, new()
		{
			return HslReflectionHelper.Read<T>(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write``1(``0)" />
		public virtual OperateResult Write<T>(T data) where T : class, new()
		{
			return HslReflectionHelper.Write(data, this);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStruct``1(System.String,System.UInt16)" />
		public virtual OperateResult<T> ReadStruct<T>(string address, ushort length) where T : class, new()
		{
			return ReadWriteNetHelper.ReadStruct<T>(this, address, length, ByteTransform);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16(System.String)" />
		[HslMqttApi("ReadInt16", "")]
		public OperateResult<short> ReadInt16(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadInt16(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt16Array", "")]
		public virtual OperateResult<short[]> ReadInt16(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 1)), (byte[] m) => ByteTransform.TransInt16(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16(System.String)" />
		[HslMqttApi("ReadUInt16", "")]
		public OperateResult<ushort> ReadUInt16(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadUInt16(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt16Array", "")]
		public virtual OperateResult<ushort[]> ReadUInt16(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 1)), (byte[] m) => ByteTransform.TransUInt16(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String)" />
		[HslMqttApi("ReadInt32", "")]
		public OperateResult<int> ReadInt32(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadInt32(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt32Array", "")]
		public virtual OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String)" />
		[HslMqttApi("ReadUInt32", "")]
		public OperateResult<uint> ReadUInt32(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadUInt32(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt32Array", "")]
		public virtual OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String)" />
		[HslMqttApi("ReadFloat", "")]
		public OperateResult<float> ReadFloat(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadFloat(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String,System.UInt16)" />
		[HslMqttApi("ReadFloatArray", "")]
		public virtual OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String)" />
		[HslMqttApi("ReadInt64", "")]
		public OperateResult<long> ReadInt64(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadInt64(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt64Array", "")]
		public virtual OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String)" />
		[HslMqttApi("ReadUInt64", "")]
		public OperateResult<ulong> ReadUInt64(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadUInt64(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt64Array", "")]
		public virtual OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String)" />
		[HslMqttApi("ReadDouble", "")]
		public OperateResult<double> ReadDouble(string address)
		{
			return ByteTransformHelper.GetResultFromArray(ReadDouble(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public virtual OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransDouble(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadString(System.String,System.UInt16)" />
		[HslMqttApi("ReadString", "")]
		public virtual OperateResult<string> ReadString(string address, ushort length)
		{
			return ReadString(address, length, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadString(System.String,System.UInt16,System.Text.Encoding)" />
		public virtual OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => ByteTransform.TransString(m, 0, m.Length, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int16[])" />
		[HslMqttApi("WriteInt16Array", "")]
		public virtual OperateResult Write(string address, short[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int16)" />
		[HslMqttApi("WriteInt16", "")]
		public virtual OperateResult Write(string address, short value)
		{
			return Write(address, new short[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt16[])" />
		[HslMqttApi("WriteUInt16Array", "")]
		public virtual OperateResult Write(string address, ushort[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt16)" />
		[HslMqttApi("WriteUInt16", "")]
		public virtual OperateResult Write(string address, ushort value)
		{
			return Write(address, new ushort[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int32[])" />
		[HslMqttApi("WriteInt32Array", "")]
		public virtual OperateResult Write(string address, int[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int32)" />
		[HslMqttApi("WriteInt32", "")]
		public virtual OperateResult Write(string address, int value)
		{
			return Write(address, new int[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt32[])" />
		[HslMqttApi("WriteUInt32Array", "")]
		public virtual OperateResult Write(string address, uint[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt32)" />
		[HslMqttApi("WriteUInt32", "")]
		public virtual OperateResult Write(string address, uint value)
		{
			return Write(address, new uint[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Single[])" />
		[HslMqttApi("WriteFloatArray", "")]
		public virtual OperateResult Write(string address, float[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Single)" />
		[HslMqttApi("WriteFloat", "")]
		public virtual OperateResult Write(string address, float value)
		{
			return Write(address, new float[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int64[])" />
		[HslMqttApi("WriteInt64Array", "")]
		public virtual OperateResult Write(string address, long[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int64)" />
		[HslMqttApi("WriteInt64", "")]
		public virtual OperateResult Write(string address, long value)
		{
			return Write(address, new long[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt64[])" />
		[HslMqttApi("WriteUInt64Array", "")]
		public virtual OperateResult Write(string address, ulong[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt64)" />
		[HslMqttApi("WriteUInt64", "")]
		public virtual OperateResult Write(string address, ulong value)
		{
			return Write(address, new ulong[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double[])" />
		[HslMqttApi("WriteDoubleArray", "")]
		public virtual OperateResult Write(string address, double[] values)
		{
			return Write(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double)" />
		[HslMqttApi("WriteDouble", "")]
		public virtual OperateResult Write(string address, double value)
		{
			return Write(address, new double[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String)" />
		[HslMqttApi("WriteString", "")]
		public virtual OperateResult Write(string address, string value)
		{
			return Write(address, value, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Int32)" />
		public virtual OperateResult Write(string address, string value, int length)
		{
			return Write(address, value, length, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Text.Encoding)" />
		public virtual OperateResult Write(string address, string value, Encoding encoding)
		{
			byte[] array = ByteTransform.TransByte(value, encoding);
			if (WordLength == 1)
			{
				array = SoftBasic.ArrayExpandToLengthEven(array);
			}
			return Write(address, array);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Int32,System.Text.Encoding)" />
		public virtual OperateResult Write(string address, string value, int length, Encoding encoding)
		{
			byte[] data = ByteTransform.TransByte(value, encoding);
			if (WordLength == 1)
			{
				data = SoftBasic.ArrayExpandToLengthEven(data);
			}
			data = SoftBasic.ArrayExpandToLength(data, length);
			return Write(address, data);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Boolean,System.Int32,System.Int32)" />
		[HslMqttApi("WaitBool", "")]
		public OperateResult<TimeSpan> Wait(string address, bool waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int16,System.Int32,System.Int32)" />
		[HslMqttApi("WaitInt16", "")]
		public OperateResult<TimeSpan> Wait(string address, short waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt16,System.Int32,System.Int32)" />
		[HslMqttApi("WaitUInt16", "")]
		public OperateResult<TimeSpan> Wait(string address, ushort waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int32,System.Int32,System.Int32)" />
		[HslMqttApi("WaitInt32", "")]
		public OperateResult<TimeSpan> Wait(string address, int waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt32,System.Int32,System.Int32)" />
		[HslMqttApi("WaitUInt32", "")]
		public OperateResult<TimeSpan> Wait(string address, uint waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int64,System.Int32,System.Int32)" />
		[HslMqttApi("WaitInt64", "")]
		public OperateResult<TimeSpan> Wait(string address, long waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt64,System.Int32,System.Int32)" />
		[HslMqttApi("WaitUInt64", "")]
		public OperateResult<TimeSpan> Wait(string address, ulong waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return ReadWriteNetHelper.Wait(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Boolean,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, bool waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int16,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, short waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt16,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, ushort waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int32,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, int waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt32,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, uint waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.Int64,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, long waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Wait(System.String,System.UInt64,System.Int32,System.Int32)" />
		public async Task<OperateResult<TimeSpan>> WaitAsync(string address, ulong waitValue, int readInterval = 100, int waitTimeout = -1)
		{
			return await ReadWriteNetHelper.WaitAsync(this, address, waitValue, readInterval, waitTimeout);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			return await Task.Run(() => Read(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Byte[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBoolAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadBool(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBoolAsync(System.String)" />
		public virtual async Task<OperateResult<bool>> ReadBoolAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadBoolAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Boolean[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Boolean)" />
		public virtual async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await WriteAsync(address, new bool[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadCustomerAsync``1(System.String)" />
		public async Task<OperateResult<T>> ReadCustomerAsync<T>(string address) where T : IDataTransfer, new()
		{
			return await ReadWriteNetHelper.ReadCustomerAsync<T>(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadCustomerAsync``1(System.String,``0)" />
		public async Task<OperateResult<T>> ReadCustomerAsync<T>(string address, T obj) where T : IDataTransfer, new()
		{
			return await ReadWriteNetHelper.ReadCustomerAsync(this, address, obj);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteCustomerAsync``1(System.String,``0)" />
		public async Task<OperateResult> WriteCustomerAsync<T>(string address, T data) where T : IDataTransfer, new()
		{
			return await ReadWriteNetHelper.WriteCustomerAsync(this, address, data);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadAsync``1" />
		public virtual async Task<OperateResult<T>> ReadAsync<T>() where T : class, new()
		{
			return await HslReflectionHelper.ReadAsync<T>(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync``1(``0)" />
		public virtual async Task<OperateResult> WriteAsync<T>(T data) where T : class, new()
		{
			return await HslReflectionHelper.WriteAsync(data, this);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStruct``1(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<T>> ReadStructAsync<T>(string address, ushort length) where T : class, new()
		{
			return await ReadWriteNetHelper.ReadStructAsync<T>(this, address, length, ByteTransform);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16Async(System.String)" />
		public async Task<OperateResult<short>> ReadInt16Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadInt16Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<short[]>> ReadInt16Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 1)), (byte[] m) => ByteTransform.TransInt16(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16Async(System.String)" />
		public async Task<OperateResult<ushort>> ReadUInt16Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadUInt16Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<ushort[]>> ReadUInt16Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 1)), (byte[] m) => ByteTransform.TransUInt16(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32Async(System.String)" />
		public async Task<OperateResult<int>> ReadInt32Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadInt32Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String)" />
		public async Task<OperateResult<uint>> ReadUInt32Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadUInt32Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String)" />
		public async Task<OperateResult<float>> ReadFloatAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadFloatAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 2)), (byte[] m) => ByteTransform.TransSingle(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String)" />
		public async Task<OperateResult<long>> ReadInt64Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadInt64Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String)" />
		public async Task<OperateResult<ulong>> ReadUInt64Async(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadUInt64Async(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String)" />
		public async Task<OperateResult<double>> ReadDoubleAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadDoubleAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, GetWordLength(address, length, 4)), (byte[] m) => ByteTransform.TransDouble(m, 0, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStringAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<string>> ReadStringAsync(string address, ushort length)
		{
			return await ReadStringAsync(address, length, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStringAsync(System.String,System.UInt16,System.Text.Encoding)" />
		public virtual async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => ByteTransform.TransString(m, 0, m.Length, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int16[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, short[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int16)" />
		public virtual async Task<OperateResult> WriteAsync(string address, short value)
		{
			return await WriteAsync(address, new short[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt16[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, ushort[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult> WriteAsync(string address, ushort value)
		{
			return await WriteAsync(address, new ushort[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int32[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, int[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int32)" />
		public virtual async Task<OperateResult> WriteAsync(string address, int value)
		{
			return await WriteAsync(address, new int[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt32[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, uint[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt32)" />
		public virtual async Task<OperateResult> WriteAsync(string address, uint value)
		{
			return await WriteAsync(address, new uint[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Single[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, float[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Single)" />
		public virtual async Task<OperateResult> WriteAsync(string address, float value)
		{
			return await WriteAsync(address, new float[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int64[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, long[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int64)" />
		public virtual async Task<OperateResult> WriteAsync(string address, long value)
		{
			return await WriteAsync(address, new long[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt64[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, ulong[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt64)" />
		public virtual async Task<OperateResult> WriteAsync(string address, ulong value)
		{
			return await WriteAsync(address, new ulong[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Double[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await WriteAsync(address, ByteTransform.TransByte(values));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Double)" />
		public virtual async Task<OperateResult> WriteAsync(string address, double value)
		{
			return await WriteAsync(address, new double[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value)
		{
			return await WriteAsync(address, value, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String,System.Text.Encoding)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value, Encoding encoding)
		{
			byte[] temp = ByteTransform.TransByte(value, encoding);
			if (WordLength == 1)
			{
				temp = SoftBasic.ArrayExpandToLengthEven(temp);
			}
			return await WriteAsync(address, temp);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String,System.Int32)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value, int length)
		{
			return await WriteAsync(address, value, length, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String,System.Int32,System.Text.Encoding)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value, int length, Encoding encoding)
		{
			byte[] temp2 = ByteTransform.TransByte(value, encoding);
			if (WordLength == 1)
			{
				temp2 = SoftBasic.ArrayExpandToLengthEven(temp2);
			}
			temp2 = SoftBasic.ArrayExpandToLength(temp2, length);
			return await WriteAsync(address, temp2);
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				CommunicationPipe?.CloseCommunication();
				CommunicationPipe?.Dispose();
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			if (!disposedValue)
			{
				Dispose(disposing: true);
				disposedValue = true;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DeviceCommunication<{byteTransform}>{{{CommunicationPipe}}}";
		}
	}
}
