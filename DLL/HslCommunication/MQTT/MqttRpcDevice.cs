using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// 基于MRPC实现的远程设备访问的接口，实现了和基础PLC一样的访问功能，适用的设备为 <see cref="T:HslCommunication.MQTT.MqttServer" /> 将PLC实际的通信对象注册为 RPC 接口服务。<br />
	/// The interface for remote device access based on MRPC implements the same access function as the basic PLC. The applicable device is <see cref="T:HslCommunication.MQTT.MqttServer" /> to register the actual communication object of the PLC as an RPC interface service.
	/// </summary>
	/// <remarks>
	/// 什么时候会用到本设备对象呢？如果你得PLC端口只有一个，只能支持一个连接，但是实际通信的客户端不止一个时，就可以使用本类实现一对多通信。或是你希望在最终通信的客户端和PLC之间做个隔离，并增加安全校验。详细的例子参考API文档。<br />
	/// When will this device object be used? If you have only one PLC port and can only support one connection, but there are more than one client for actual communication, you can use this class to implement one-to-many communication. 
	/// Or you want to isolate the final communication client from the PLC and add security checks. For a detailed example, refer to the API documentation.
	/// </remarks>
	/// <example>
	/// 如何和服务器进行配套调用使用呢？先在服务器端创建服务，并注册Api接口对象
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttRpcDeviceSample.cs" region="Server" title="服务器侧示例" />
	/// 然后客户端就支持多连接了，客户端的代码如下
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\MQTT\MqttRpcDeviceSample.cs" region="Client" title="客户端侧示例" />
	/// </example>
	public class MqttRpcDevice : MqttSyncClient, IReadWriteDevice, IReadWriteNet
	{
		private IByteTransform byteTransform = new RegularByteTransform();

		private string deviceTopic;

		/// <inheritdoc cref="P:HslCommunication.Core.Device.DeviceCommunication.ByteTransform" />
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
		/// 获取或设置当前的设备主题信息<br />
		/// Get or set the current device theme information.
		/// </summary>
		public string DeviceTopic
		{
			get
			{
				return deviceTopic;
			}
			set
			{
				deviceTopic = value;
			}
		}

		/// <summary>
		/// 实例化一个MQTT的同步客户端<br />
		/// Instantiate an MQTT synchronization client
		/// </summary>
		/// <param name="options">连接的参数信息，可以指定IP地址，端口，账户名，密码，客户端ID信息</param>
		/// <param name="topic">设备关联的主题信息</param>
		public MqttRpcDevice(MqttConnectionOptions options, string topic = null)
			: base(options)
		{
			deviceTopic = topic;
		}

		/// <summary>
		/// 通过指定的ip地址及端口来实例化一个同步的MQTT客户端<br />
		/// Instantiate a synchronized MQTT client with the specified IP address and port
		/// </summary>
		/// <param name="ipAddress">IP地址信息</param>
		/// <param name="port">端口号信息</param>
		/// <param name="topic">设备关联的主题信息</param>
		public MqttRpcDevice(string ipAddress, int port, string topic = null)
			: base(ipAddress, port)
		{
			deviceTopic = topic;
		}

		private string GetTopic(string topic)
		{
			if (string.IsNullOrEmpty(deviceTopic))
			{
				return topic;
			}
			if (deviceTopic.EndsWith("/"))
			{
				return deviceTopic + topic;
			}
			return deviceTopic + "/" + topic;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Read(System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public virtual OperateResult<byte[]> Read(string address, ushort length)
		{
			return ReadRpc<byte[]>(GetTopic("ReadByteArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Byte[])" />
		[HslMqttApi("WriteByteArray", "")]
		public virtual OperateResult Write(string address, byte[] value)
		{
			return ReadRpc<string>(GetTopic("WriteByteArray"), new
			{
				address = address,
				value = value.ToHexString()
			});
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		[HslMqttApi("ReadBoolArray", "")]
		public virtual OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return ReadRpc<bool[]>(GetTopic("ReadBoolArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String)" />
		[HslMqttApi("ReadBool", "")]
		public virtual OperateResult<bool> ReadBool(string address)
		{
			return ReadRpc<bool>(GetTopic("ReadBool"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public virtual OperateResult Write(string address, bool[] value)
		{
			return ReadRpc<string>(GetTopic("WriteBoolArray"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean)" />
		[HslMqttApi("WriteBool", "")]
		public virtual OperateResult Write(string address, bool value)
		{
			return ReadRpc<string>(GetTopic("WriteBool"), new { address, value });
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
			return ReadRpc<short>(GetTopic("ReadInt16"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt16Array", "")]
		public virtual OperateResult<short[]> ReadInt16(string address, ushort length)
		{
			return ReadRpc<short[]>(GetTopic("ReadInt16Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16(System.String)" />
		[HslMqttApi("ReadUInt16", "")]
		public OperateResult<ushort> ReadUInt16(string address)
		{
			return ReadRpc<ushort>(GetTopic("ReadUInt16"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt16Array", "")]
		public virtual OperateResult<ushort[]> ReadUInt16(string address, ushort length)
		{
			return ReadRpc<ushort[]>(GetTopic("ReadUInt16Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String)" />
		[HslMqttApi("ReadInt32", "")]
		public OperateResult<int> ReadInt32(string address)
		{
			return ReadRpc<int>(GetTopic("ReadInt32"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt32Array", "")]
		public virtual OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			return ReadRpc<int[]>(GetTopic("ReadInt32Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String)" />
		[HslMqttApi("ReadUInt32", "")]
		public OperateResult<uint> ReadUInt32(string address)
		{
			return ReadRpc<uint>(GetTopic("ReadUInt32"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt32Array", "")]
		public virtual OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			return ReadRpc<uint[]>(GetTopic("ReadUInt32Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String)" />
		[HslMqttApi("ReadFloat", "")]
		public OperateResult<float> ReadFloat(string address)
		{
			return ReadRpc<float>(GetTopic("ReadFloat"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloat(System.String,System.UInt16)" />
		[HslMqttApi("ReadFloatArray", "")]
		public virtual OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return ReadRpc<float[]>(GetTopic("ReadFloatArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String)" />
		[HslMqttApi("ReadInt64", "")]
		public OperateResult<long> ReadInt64(string address)
		{
			return ReadRpc<long>(GetTopic("ReadInt64"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadInt64Array", "")]
		public virtual OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			return ReadRpc<long[]>(GetTopic("ReadInt64Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String)" />
		[HslMqttApi("ReadUInt64", "")]
		public OperateResult<ulong> ReadUInt64(string address)
		{
			return ReadRpc<ulong>(GetTopic("ReadUInt64"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64(System.String,System.UInt16)" />
		[HslMqttApi("ReadUInt64Array", "")]
		public virtual OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			return ReadRpc<ulong[]>(GetTopic("ReadUInt64Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String)" />
		[HslMqttApi("ReadDouble", "")]
		public OperateResult<double> ReadDouble(string address)
		{
			return ReadRpc<double>(GetTopic("ReadDouble"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDouble(System.String,System.UInt16)" />
		[HslMqttApi("ReadDoubleArray", "")]
		public virtual OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return ReadRpc<double[]>(GetTopic("ReadDoubleArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadString(System.String,System.UInt16)" />
		[HslMqttApi("ReadString", "")]
		public virtual OperateResult<string> ReadString(string address, ushort length)
		{
			return ReadRpc<string>(GetTopic("ReadString"), new { address, length });
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
			return ReadRpc<string>(GetTopic("WriteInt16Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int16)" />
		[HslMqttApi("WriteInt16", "")]
		public virtual OperateResult Write(string address, short value)
		{
			return ReadRpc<string>(GetTopic("WriteInt16"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt16[])" />
		[HslMqttApi("WriteUInt16Array", "")]
		public virtual OperateResult Write(string address, ushort[] values)
		{
			return ReadRpc<string>(GetTopic("WriteUInt16Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt16)" />
		[HslMqttApi("WriteUInt16", "")]
		public virtual OperateResult Write(string address, ushort value)
		{
			return ReadRpc<string>(GetTopic("WriteUInt16"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int32[])" />
		[HslMqttApi("WriteInt32Array", "")]
		public virtual OperateResult Write(string address, int[] values)
		{
			return ReadRpc<string>(GetTopic("WriteInt32Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int32)" />
		[HslMqttApi("WriteInt32", "")]
		public OperateResult Write(string address, int value)
		{
			return ReadRpc<string>(GetTopic("WriteInt32"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt32[])" />
		[HslMqttApi("WriteUInt32Array", "")]
		public virtual OperateResult Write(string address, uint[] values)
		{
			return ReadRpc<string>(GetTopic("WriteUInt32Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt32)" />
		[HslMqttApi("WriteUInt32", "")]
		public OperateResult Write(string address, uint value)
		{
			return ReadRpc<string>(GetTopic("WriteUInt32"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Single[])" />
		[HslMqttApi("WriteFloatArray", "")]
		public virtual OperateResult Write(string address, float[] values)
		{
			return ReadRpc<string>(GetTopic("WriteFloatArray"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Single)" />
		[HslMqttApi("WriteFloat", "")]
		public OperateResult Write(string address, float value)
		{
			return ReadRpc<string>(GetTopic("WriteFloat"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int64[])" />
		[HslMqttApi("WriteInt64Array", "")]
		public virtual OperateResult Write(string address, long[] values)
		{
			return ReadRpc<string>(GetTopic("WriteInt64Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Int64)" />
		[HslMqttApi("WriteInt64", "")]
		public OperateResult Write(string address, long value)
		{
			return ReadRpc<string>(GetTopic("WriteInt64"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt64[])" />
		[HslMqttApi("WriteUInt64Array", "")]
		public virtual OperateResult Write(string address, ulong[] values)
		{
			return ReadRpc<string>(GetTopic("WriteUInt64Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.UInt64)" />
		[HslMqttApi("WriteUInt64", "")]
		public OperateResult Write(string address, ulong value)
		{
			return ReadRpc<string>(GetTopic("WriteUInt64"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double[])" />
		[HslMqttApi("WriteDoubleArray", "")]
		public virtual OperateResult Write(string address, double[] values)
		{
			return ReadRpc<string>(GetTopic("WriteDoubleArray"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Double)" />
		[HslMqttApi("WriteDouble", "")]
		public OperateResult Write(string address, double value)
		{
			return ReadRpc<string>(GetTopic("WriteDouble"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String)" />
		[HslMqttApi("WriteString", "")]
		public virtual OperateResult Write(string address, string value)
		{
			return ReadRpc<string>(GetTopic("WriteString"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Int32)" />
		public virtual OperateResult Write(string address, string value, int length)
		{
			return Write(address, value, length, Encoding.ASCII);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Text.Encoding)" />
		public virtual OperateResult Write(string address, string value, Encoding encoding)
		{
			byte[] value2 = ByteTransform.TransByte(value, encoding);
			return Write(address, value2);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.String,System.Int32,System.Text.Encoding)" />
		public virtual OperateResult Write(string address, string value, int length, Encoding encoding)
		{
			byte[] data = ByteTransform.TransByte(value, encoding);
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
			return await ReadRpcAsync<byte[]>(GetTopic("ReadByteArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Byte[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteByteArray"), new
			{
				address = address,
				value = value.ToHexString()
			});
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBoolAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			return await ReadRpcAsync<bool[]>(GetTopic("ReadBoolArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBoolAsync(System.String)" />
		public virtual async Task<OperateResult<bool>> ReadBoolAsync(string address)
		{
			return await ReadRpcAsync<bool>(GetTopic("ReadBool"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Boolean[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteBoolArray"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Boolean)" />
		public virtual async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteBool"), new { address, value });
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
			return await ReadRpcAsync<short>(GetTopic("ReadInt16"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt16Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<short[]>> ReadInt16Async(string address, ushort length)
		{
			return await ReadRpcAsync<short[]>(GetTopic("ReadInt16Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16Async(System.String)" />
		public async Task<OperateResult<ushort>> ReadUInt16Async(string address)
		{
			return await ReadRpcAsync<ushort>(GetTopic("ReadUInt16"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt16Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<ushort[]>> ReadUInt16Async(string address, ushort length)
		{
			return await ReadRpcAsync<ushort[]>(GetTopic("ReadUInt16Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32Async(System.String)" />
		public async Task<OperateResult<int>> ReadInt32Async(string address)
		{
			return await ReadRpcAsync<int>(GetTopic("ReadInt32"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt32Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			return await ReadRpcAsync<int[]>(GetTopic("ReadInt32Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String)" />
		public async Task<OperateResult<uint>> ReadUInt32Async(string address)
		{
			return await ReadRpcAsync<uint>(GetTopic("ReadUInt32"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt32Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			return await ReadRpcAsync<uint[]>(GetTopic("ReadUInt32Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String)" />
		public async Task<OperateResult<float>> ReadFloatAsync(string address)
		{
			return await ReadRpcAsync<float>(GetTopic("ReadFloat"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadFloatAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return await ReadRpcAsync<float[]>(GetTopic("ReadFloatArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String)" />
		public async Task<OperateResult<long>> ReadInt64Async(string address)
		{
			return await ReadRpcAsync<long>(GetTopic("ReadInt64"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadInt64Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			return await ReadRpcAsync<long[]>(GetTopic("ReadInt64Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String)" />
		public async Task<OperateResult<ulong>> ReadUInt64Async(string address)
		{
			return await ReadRpcAsync<ulong>(GetTopic("ReadUInt64"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadUInt64Async(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			return await ReadRpcAsync<ulong[]>(GetTopic("ReadUInt64Array"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String)" />
		public async Task<OperateResult<double>> ReadDoubleAsync(string address)
		{
			return await ReadRpcAsync<double>(GetTopic("ReadDouble"), new { address });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadDoubleAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await ReadRpcAsync<double[]>(GetTopic("ReadDoubleArray"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStringAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult<string>> ReadStringAsync(string address, ushort length)
		{
			return await ReadRpcAsync<string>(GetTopic("ReadString"), new { address, length });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadStringAsync(System.String,System.UInt16,System.Text.Encoding)" />
		public virtual async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => ByteTransform.TransString(m, 0, m.Length, encoding));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int16[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, short[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt16Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int16)" />
		public virtual async Task<OperateResult> WriteAsync(string address, short value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt16"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt16[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, ushort[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt16Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt16)" />
		public virtual async Task<OperateResult> WriteAsync(string address, ushort value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt16"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int32[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, int[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt32Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int32)" />
		public async Task<OperateResult> WriteAsync(string address, int value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt32"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt32[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, uint[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt32Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt32)" />
		public async Task<OperateResult> WriteAsync(string address, uint value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt32"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Single[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, float[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteFloatArray"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Single)" />
		public async Task<OperateResult> WriteAsync(string address, float value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteFloat"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int64[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, long[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt64Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Int64)" />
		public async Task<OperateResult> WriteAsync(string address, long value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteInt64"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt64[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, ulong[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt64Array"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.UInt64)" />
		public async Task<OperateResult> WriteAsync(string address, ulong value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteUInt64"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Double[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteDoubleArray"), new { address, values });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.Double)" />
		public async Task<OperateResult> WriteAsync(string address, double value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteDouble"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value)
		{
			return await ReadRpcAsync<string>(GetTopic("WriteString"), new { address, value });
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.WriteAsync(System.String,System.String,System.Text.Encoding)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value, Encoding encoding)
		{
			byte[] temp = ByteTransform.TransByte(value, encoding);
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
			temp2 = SoftBasic.ArrayExpandToLength(temp2, length);
			return await WriteAsync(address, temp2);
		}
	}
}
