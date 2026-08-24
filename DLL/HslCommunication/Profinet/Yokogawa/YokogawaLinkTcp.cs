using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Address;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Yokogawa
{
	/// <summary>
	/// 横河PLC的二进制通信类，支持X,Y,I,E,M,T,C,L继电器类型的数据读写，支持D,B,F,R,V,Z,W,TN,CN寄存器类型的数据读写，还支持一些高级的信息读写接口，详细参考Demo界面。<br />
	/// Yokogawa PLC's binary communication type, supports X, Y, I, E, M, T, C, L relay type data read and write, 
	/// supports D, B, F, R, V, Z, W, TN, CN registers Types of data reading and writing, and some advanced information reading and writing interfaces are also supported. 
	/// Please refer to the Demo UI.
	/// </summary>
	/// <remarks>
	/// 基础的数据读写面向VIP用户开放，高级的读写随机数据，启动停止命令，读取程序状态，
	/// 系统信息，PLC时间，读写特殊的模块数据需要商业用户授权，读取的数据长度，读取的随机地址长度，在商业授权下，长度不受限制，可以无限大。
	/// </remarks>
	/// <example>
	/// 例如我们正常读取一个D100的数据如下：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkTcpSample.cs" region="Sample1" title="Read示例" />
	/// 我们在读取的时候可以动态的变更cpu信息，参考下面的代码
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkTcpSample.cs" region="Sample2" title="Read示例" />
	/// 关于随机读写的代码示例，可以读写地址分布很散的地址，参考下面的代码
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkTcpSample.cs" region="Sample3" title="Read示例" />
	/// 最后看一下读取特殊模块的数据，可以读取基本的字节数据，也可以使用富文本的地址读取
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\YokogawaLinkTcpSample.cs" region="Sample4" title="Read示例" />
	/// </example>
	public class YokogawaLinkTcp : DeviceTcpNet
	{
		/// <summary>
		/// 获取或设置当前的CPU Number，默认值为1<br />
		/// Get or set the current CPU Number, the default value is 1
		/// </summary>
		public byte CpuNumber { get; set; }

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public YokogawaLinkTcp()
		{
			base.ByteTransform = new RegularByteTransform(DataFormat.CDAB);
			CpuNumber = 1;
		}

		/// <summary>
		/// 指定IP地址和端口号来实例化一个对象<br />
		/// Specify the IP address and port number to instantiate an object
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		public YokogawaLinkTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new YokogawaLinkBinaryMessage();
		}

		/// <inheritdoc />
		/// <remarks>
		/// 读取的线圈地址支持X,Y,I,E,M,T,C,L，寄存器地址支持D,B,F,R,V,Z,W,TN,CN，举例：D100；也可以携带CPU进行访问，举例：cpu=2;D100<br />
		/// <b>[商业授权]</b> 如果想要读取特殊模块的数据，需要使用 <b>Special:</b> 开头标记，举例：Special:unit=0;slot=1;100<br />
		/// The read coil address supports X, Y, I, E, M, T, C, L, and the register address supports D, B, F, R, V, Z, W, TN, CN, for example: D100; 
		/// it can also be carried CPU access, for example: cpu=2;D100. <br />
		/// <b>[Authorization]</b> If you want to read the data of a special module, you need to use the <b>Special:</b> beginning tag, for example: Special:unit=0;slot=1;100
		/// </remarks>
		[HslMqttApi("ReadByteArray", "Supports X,Y,I,E,M,T,C,L,D,B,F,R,V,Z,W,TN,CN, for example: D100; or cpu=2;D100 or Special:unit=0;slot=1;100")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<List<byte[]>> operateResult;
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				
				operateResult = BuildReadSpecialModule(CpuNumber, address, length);
			}
			else
			{
				operateResult = BuildReadCommand(CpuNumber, address, length, isBit: false);
			}
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			for (int i = 0; i < operateResult.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content[i]);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				list.AddRange(operateResult3.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc />
		/// <remarks>
		/// 写入的线圈地址支持Y,I,E,M,T,C,L，寄存器地址支持D,B,F,R,V,Z,W,TN,CN，举例：D100；也可以携带CPU进行访问，举例：cpu=2;D100<br />
		/// 如果想要写入特殊模块的数据，需要使用 <b>Special:</b> 开头标记，举例：Special:unit=0;slot=1;100<br />
		/// The read coil address supports Y, I, E, M, T, C, L, and the register address supports D, B, F, R, V, Z, W, TN, CN, for example: D100; 
		/// it can also be carried CPU access, for example: cpu=2;D100.
		/// If you want to read the data of a special module, you need to use the <b>Special:</b> beginning tag, for example: Special:unit=0;slot=1;100
		/// </remarks>
		[HslMqttApi("WriteByteArray", "Supports Y,I,E,M,T,C,L,D,B,F,R,V,Z,W,TN,CN, for example: D100; or cpu=2;D100 or Special:unit=0;slot=1;100")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<byte[]> operateResult;
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				
				operateResult = BuildWriteSpecialModule(CpuNumber, address, value);
			}
			else
			{
				operateResult = BuildWriteWordCommand(CpuNumber, address, value);
			}
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return CheckContent(operateResult2.Content);
		}

		/// <inheritdoc />
		/// <remarks>
		/// 读取的线圈地址支持X,Y,I,E,M,T,C,L，举例：Y100；也可以携带CPU进行访问，举例：cpu=2;Y100<br />
		/// The read coil address supports X, Y, I, E, M, T, C, L, for example: Y100; you can also carry the CPU for access, for example: cpu=2;Y100
		/// </remarks>
		[HslMqttApi("ReadBoolArray", "Read coil address supports X, Y, I, E, M, T, C, L, for example: Y100; or cpu=2;Y100")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<List<byte[]>> operateResult = BuildReadCommand(CpuNumber, address, length, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			for (int i = 0; i < operateResult.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content[i]);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult3);
				}
				list.AddRange(operateResult3.Content);
			}
			return OperateResult.CreateSuccessResult((from m in list.ToArray()
				select m != 0).ToArray());
		}

		/// <inheritdoc />
		/// <remarks>
		/// 写入的线圈地址支持Y,I,E,M,T,C,L，举例：Y100；也可以携带CPU进行访问，举例：cpu=2;Y100<br />
		/// The write coil address supports Y, I, E, M, T, C, L, for example: Y100; you can also carry the CPU for access, for example: cpu=2;Y100
		/// </remarks>
		[HslMqttApi("WriteBoolArray", "The write coil address supports Y, I, E, M, T, C, L, for example: Y100; or cpu=2;Y100")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<byte[]> operateResult = BuildWriteBoolCommand(CpuNumber, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return CheckContent(operateResult2.Content);
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机读取<see cref="T:System.Boolean" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，就可以返回批量<see cref="T:System.Boolean" />值<br />
		/// <b>[Authorization]</b> Random read <see cref="T:System.Boolean" /> array information, the master needs to pass in the <see cref="T:System.String" /> array address information, and then the batch can be returned to <see cref="T:System.Boolean" /> value
		/// </summary> 
		/// <param name="address">批量地址信息</param>
		/// <remarks>
		/// 读取的线圈地址支持X,Y,I,E,M,T,C,L，举例：Y100；也可以携带CPU进行访问，举例：cpu=2;Y100<br />
		/// The read coil address supports X, Y, I, E, M, T, C, L, for example: Y100; you can also carry the CPU for access, for example: cpu=2;Y100
		/// </remarks>
		/// <returns>带有成功标志的Bool数组信息</returns>
		[HslMqttApi(Description = "Read random relay, supports X, Y, I, E, M, T, C, L, for example: Y100;")]
		public OperateResult<bool[]> ReadRandomBool(string[] address)
		{
			
			OperateResult<List<byte[]>> operateResult = BuildReadRandomCommand(CpuNumber, address, isBit: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			List<bool> list = new List<bool>();
			foreach (byte[] item in operateResult.Content)
			{
				OperateResult<byte[]> operateResult2 = ReadFromCoreServer(item);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult3);
				}
				list.AddRange(operateResult3.Content.Select((byte m) => m != 0));
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机写入<see cref="T:System.Boolean" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，以及对应的<see cref="T:System.Boolean" />数组值<br />
		/// <b>[Authorization]</b> Randomly write the <see cref="T:System.Boolean" /> array information, the main need to pass in the <see cref="T:System.String" /> array address information, 
		/// and the corresponding <see cref="T:System.Boolean" /> array value
		/// </summary>
		/// <param name="address">批量地址信息</param>
		/// <param name="value">批量的数据值信息</param>
		/// <remarks>
		/// 写入的线圈地址支持Y,I,E,M,T,C,L，举例：Y100；也可以携带CPU进行访问，举例：cpu=2;Y100<br />
		/// The write coil address supports Y, I, E, M, T, C, L, for example: Y100; you can also carry the CPU for access, for example: cpu=2;Y100
		/// </remarks>
		/// <returns>是否写入成功</returns>
		[HslMqttApi(Description = "Write random relay, supports Y, I, E, M, T, C, L, for example: Y100;")]
		public OperateResult WriteRandomBool(string[] address, bool[] value)
		{
			
			if (address.Length != value.Length)
			{
				return new OperateResult(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			OperateResult<byte[]> operateResult = BuildWriteRandomBoolCommand(CpuNumber, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机读取<see cref="T:System.Byte" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，就可以返回批量<see cref="T:System.Byte" />值<br />
		/// <b>[Authorization]</b> Random read <see cref="T:System.Byte" /> array information, the master needs to pass in the <see cref="T:System.String" /> array address information, and then the batch can be returned to <see cref="T:System.Byte" /> value
		/// </summary> 
		/// <remarks>
		/// Supports D,B,F,R,V,Z,W,TN,CN，example: D100
		/// </remarks>
		/// <param name="address">批量地址信息</param>
		/// <returns>带有成功标志的Bool数组信息</returns>
		[HslMqttApi(Description = "Read random register, supports D,B,F,R,V,Z,W,TN,CN，example: D100")]
		public OperateResult<byte[]> ReadRandom(string[] address)
		{
			
			OperateResult<List<byte[]>> operateResult = BuildReadRandomCommand(CpuNumber, address, isBit: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			foreach (byte[] item in operateResult.Content)
			{
				OperateResult<byte[]> operateResult2 = ReadFromCoreServer(item);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult3);
				}
				list.AddRange(operateResult3.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机读取<see cref="T:System.Int16" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，就可以返回批量<see cref="T:System.Int16" />值<br />
		/// <b>[Authorization]</b> Random read <see cref="T:System.Int16" /> array information, the master needs to pass in the <see cref="T:System.Int16" /> array address information, and then the batch can be returned to <see cref="T:System.Int16" /> value
		/// </summary> 
		/// <param name="address">批量地址信息</param>
		/// <returns>带有成功标志的Bool数组信息</returns>
		[HslMqttApi(Description = "Read random register, and get short array, supports D, B, F, R, V, Z, W, TN, CN，example: D100")]
		public OperateResult<short[]> ReadRandomInt16(string[] address)
		{
			return ReadRandom(address).Then((byte[] m) => OperateResult.CreateSuccessResult(base.ByteTransform.TransInt16(m, 0, address.Length)));
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机读取<see cref="T:System.UInt16" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，就可以返回批量<see cref="T:System.UInt16" />值<br />
		/// <b>[Authorization]</b> Random read <see cref="T:System.UInt16" /> array information, the master needs to pass in the <see cref="T:System.UInt16" /> array address information, and then the batch can be returned to <see cref="T:System.UInt16" /> value
		/// </summary> 
		/// <remarks>
		/// Supports D, B, F, R, V, Z, W, TN, CN，example: D100
		/// </remarks>
		/// <param name="address">批量地址信息</param>
		/// <returns>带有成功标志的Bool数组信息</returns>
		[HslMqttApi(Description = "Read random register, and get ushort array, supports D, B, F, R, V, Z, W, TN, CN，example: D100")]
		public OperateResult<ushort[]> ReadRandomUInt16(string[] address)
		{
			return ReadRandom(address).Then((byte[] m) => OperateResult.CreateSuccessResult(base.ByteTransform.TransUInt16(m, 0, address.Length)));
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机写入<see cref="T:System.Byte" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，以及对应的<see cref="T:System.Byte" />数组值<br />
		/// <b>[Authorization]</b> Randomly write the <see cref="T:System.Byte" /> array information, the main need to pass in the <see cref="T:System.String" /> array address information, 
		/// and the corresponding <see cref="T:System.Byte" /> array value
		/// </summary>
		/// <remarks>
		/// Supports D, B, F, R, V, Z, W, TN, CN，example: D100
		/// </remarks>
		/// <param name="address">批量地址信息</param>
		/// <param name="value">批量的数据值信息</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi(ApiTopic = "WriteRandom", Description = "Randomly write the byte array information, the main need to pass in the byte array address information")]
		public OperateResult WriteRandom(string[] address, byte[] value)
		{
			
			if (address.Length * 2 != value.Length)
			{
				return new OperateResult(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			OperateResult<byte[]> operateResult = BuildWriteRandomWordCommand(CpuNumber, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机写入<see cref="T:System.Int16" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，以及对应的<see cref="T:System.Int16" />数组值<br />
		/// <b>[Authorization]</b> Randomly write the <see cref="T:System.Int16" /> array information, the main need to pass in the <see cref="T:System.Int16" /> array address information, 
		/// and the corresponding <see cref="T:System.Int16" /> array value
		/// </summary>
		/// <remarks>
		/// Supports D, B, F, R, V, Z, W, TN, CN，example: D100
		/// </remarks>
		/// <param name="address">批量地址信息</param>
		/// <param name="value">批量的数据值信息</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi(ApiTopic = "WriteRandomInt16", Description = "Randomly write the short array information, the main need to pass in the short array address information")]
		public OperateResult WriteRandom(string[] address, short[] value)
		{
			return WriteRandom(address, base.ByteTransform.TransByte(value));
		}

		/// <summary>
		/// <b>[商业授权]</b> 随机写入<see cref="T:System.UInt16" />数组信息，主需要出传入<see cref="T:System.String" />数组地址信息，以及对应的<see cref="T:System.UInt16" />数组值<br />
		/// <b>[Authorization]</b> Randomly write the <see cref="T:System.UInt16" /> array information, the main need to pass in the <see cref="T:System.UInt16" /> array address information, 
		/// and the corresponding <see cref="T:System.UInt16" /> array value
		/// </summary>
		/// <remarks>
		/// Supports D, B, F, R, V, Z, W, TN, CN，example: D100
		/// </remarks>
		/// <param name="address">批量地址信息</param>
		/// <param name="value">批量的数据值信息</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi(ApiTopic = "WriteRandomUInt16", Description = "Randomly write the ushort array information, the main need to pass in the ushort array address information")]
		public OperateResult WriteRandom(string[] address, ushort[] value)
		{
			return WriteRandom(address, base.ByteTransform.TransByte(value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			OperateResult<List<byte[]>> command;
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				
				command = BuildReadSpecialModule(CpuNumber, address, length);
			}
			else
			{
				command = BuildReadCommand(CpuNumber, address, length, isBit: false);
			}
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			List<byte> content = new List<byte>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return read;
				}
				OperateResult<byte[]> check = CheckContent(read.Content);
				if (!check.IsSuccess)
				{
					return check;
				}
				content.AddRange(check.Content);
			}
			return OperateResult.CreateSuccessResult(content.ToArray());
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			OperateResult<byte[]> command;
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				
				command = BuildWriteSpecialModule(CpuNumber, address, value);
			}
			else
			{
				command = BuildWriteWordCommand(CpuNumber, address, value);
			}
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckContent(read.Content);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			OperateResult<List<byte[]>> command = BuildReadCommand(CpuNumber, address, length, isBit: true);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			List<byte> content = new List<byte>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				OperateResult<byte[]> check = CheckContent(read.Content);
				if (!check.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(check);
				}
				content.AddRange(check.Content);
			}
			return OperateResult.CreateSuccessResult((from m in content.ToArray()
				select m != 0).ToArray());
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			OperateResult<byte[]> command = BuildWriteBoolCommand(CpuNumber, address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckContent(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadRandomBool(System.String[])" />
		public async Task<OperateResult<bool[]>> ReadRandomBoolAsync(string[] address)
		{
			
			OperateResult<List<byte[]>> command = BuildReadRandomCommand(CpuNumber, address, isBit: true);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			List<bool> lists = new List<bool>();
			foreach (byte[] content in command.Content)
			{
				OperateResult<byte[]> read = await ReadFromCoreServerAsync(content);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				OperateResult<byte[]> check = CheckContent(read.Content);
				if (!check.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(check);
				}
				lists.AddRange(check.Content.Select((byte m) => m != 0));
			}
			return OperateResult.CreateSuccessResult(lists.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.WriteRandomBool(System.String[],System.Boolean[])" />
		public async Task<OperateResult> WriteRandomBoolAsync(string[] address, bool[] value)
		{
			
			if (address.Length != value.Length)
			{
				return new OperateResult(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			OperateResult<byte[]> command = BuildWriteRandomBoolCommand(CpuNumber, address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadRandom(System.String[])" />
		public async Task<OperateResult<byte[]>> ReadRandomAsync(string[] address)
		{
			
			OperateResult<List<byte[]>> command = BuildReadRandomCommand(CpuNumber, address, isBit: false);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			List<byte> lists = new List<byte>();
			foreach (byte[] content in command.Content)
			{
				OperateResult<byte[]> read = await ReadFromCoreServerAsync(content);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(read);
				}
				OperateResult<byte[]> check = CheckContent(read.Content);
				if (!check.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(check);
				}
				lists.AddRange(check.Content);
			}
			return OperateResult.CreateSuccessResult(lists.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadRandomInt16(System.String[])" />
		public async Task<OperateResult<short[]>> ReadRandomInt16Async(string[] address)
		{
			return (await ReadRandomAsync(address)).Then((byte[] m) => OperateResult.CreateSuccessResult(base.ByteTransform.TransInt16(m, 0, address.Length)));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadRandomUInt16(System.String[])" />
		public async Task<OperateResult<ushort[]>> ReadRandomUInt16Async(string[] address)
		{
			return (await ReadRandomAsync(address)).Then((byte[] m) => OperateResult.CreateSuccessResult(base.ByteTransform.TransUInt16(m, 0, address.Length)));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.WriteRandom(System.String[],System.Byte[])" />
		public async Task<OperateResult> WriteRandomAsync(string[] address, byte[] value)
		{
			
			if (address.Length * 2 != value.Length)
			{
				return new OperateResult(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			OperateResult<byte[]> command = BuildWriteRandomWordCommand(CpuNumber, address, value);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.WriteRandom(System.String[],System.Int16[])" />
		public async Task<OperateResult> WriteRandomAsync(string[] address, short[] value)
		{
			return await WriteRandomAsync(address, base.ByteTransform.TransByte(value));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.WriteRandom(System.String[],System.UInt16[])" />
		public async Task<OperateResult> WriteRandomAsync(string[] address, ushort[] value)
		{
			return await WriteRandomAsync(address, base.ByteTransform.TransByte(value));
		}

		/// <summary>
		/// <b>[商业授权]</b> 如果未执行程序，则开始执行程序<br />
		/// <b>[Authorization]</b> Starts executing a program if it is not being executed
		/// </summary>
		/// <remarks>
		/// This command will be ignored if it is executed while a program is being executed.<br />
		/// Refer to the users manual for the individual modules for the response formats that are used at error times.
		/// </remarks>
		/// <returns>是否启动成功</returns>
		[HslMqttApi(Description = "Starts executing a program if it is not being executed")]
		public OperateResult Start()
		{
			
			OperateResult<byte[]> operateResult = BuildStartCommand(CpuNumber);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <b>[商业授权]</b> 停止当前正在执行程序<br />
		/// <b>[Authorization]</b> Stops the executing program.
		/// </summary>
		/// <remarks>
		/// This command will be ignored if it is executed when no program is being executed.<br />
		/// Refer to the users manual for the individual modules for the response formats that are used at error times.
		/// </remarks>
		/// <returns>是否启动成功</returns>
		[HslMqttApi(Description = "Stops the executing program.")]
		public OperateResult Stop()
		{
			
			OperateResult<byte[]> operateResult = BuildStopCommand(CpuNumber);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = CheckContent(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <b>[商业授权]</b> 重置当前的模块，当前打开的连接被强制关闭。 模块中所做的设置也将被清除。然后当前对象需要重连PLC。<br />
		/// <b>[Authorization]</b> When this command is executed via an Ethernet interface module or an Ethernet connection of an F3SP66, F3SP67, 
		/// F3SP71 or F3SP76 sequence CPU module, the connection which is currently open is forced to close. 
		/// The settings made in the modules are also cleared. Then the current object needs to reconnect to the PLC.
		/// </summary>
		/// <returns>是否重置成功</returns>
		[HslMqttApi(Description = "Reset the connection which is currently open is forced to close")]
		public OperateResult ModuleReset()
		{
			
			OperateResult<byte[]> operateResult = ReadFromCoreServer(new byte[4] { 97, CpuNumber, 0, 0 }, hasResponseData: false, usePackAndUnpack: true);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// <b>[商业授权]</b> 读取当前PLC的程序状态，返回1：RUN；2：Stop；3：Debug；255：ROM writer<br />
		/// <b>[Authorization]</b> Read the program status. return code 1:RUN; 2:Stop; 3:Debug; 255:ROM writer
		/// </summary>
		/// <returns>当前PLC的程序状态，返回1：RUN；2：Stop；3：Debug；255：ROM writer</returns>
		[HslMqttApi(Description = "Read the program status. return code 1:RUN; 2:Stop; 3:Debug; 255:ROM writer")]
		public OperateResult<int> ReadProgramStatus()
		{
			
			byte[] obj = new byte[6] { 98, 0, 0, 2, 0, 1 };
			obj[1] = CpuNumber;
			OperateResult<byte[]> operateResult = ReadFromCoreServer(obj);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = CheckContent(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int>(operateResult2);
			}
			return OperateResult.CreateSuccessResult((int)operateResult2.Content[1]);
		}

		/// <summary>
		/// <b>[商业授权]</b> 读取当前PLC的系统状态，系统的ID，CPU类型，程序大小信息<br />
		/// <b>[Authorization]</b> Read current PLC system status, system ID, CPU type, program size information
		/// </summary>
		/// <returns>系统信息的结果对象</returns>
		[HslMqttApi(Description = "Read current PLC system status, system ID, CPU type, program size information")]
		public OperateResult<YokogawaSystemInfo> ReadSystemInfo()
		{
		
			byte[] obj = new byte[6] { 98, 0, 0, 2, 0, 2 };
			obj[1] = CpuNumber;
			OperateResult<byte[]> operateResult = ReadFromCoreServer(obj);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<YokogawaSystemInfo>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = CheckContent(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<YokogawaSystemInfo>(operateResult2);
			}
			return YokogawaSystemInfo.Parse(operateResult2.Content);
		}

		/// <summary>
		/// <b>[商业授权]</b> 读取当前PLC的时间信息，包含年月日时分秒<br />
		/// <b>[Authorization]</b> Read current PLC time information, including year, month, day, hour, minute, and second
		/// </summary>
		/// <returns>PLC的当前的时间信息</returns>
		[HslMqttApi(Description = "Read current PLC time information, including year, month, day, hour, minute, and second")]
		public OperateResult<DateTime> ReadDateTime()
		{
			
			OperateResult<byte[]> operateResult = ReadFromCoreServer(new byte[4] { 99, CpuNumber, 0, 0 });
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = CheckContent(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(new DateTime(2000 + base.ByteTransform.TransUInt16(operateResult2.Content, 0), base.ByteTransform.TransUInt16(operateResult2.Content, 2), base.ByteTransform.TransUInt16(operateResult2.Content, 4), base.ByteTransform.TransUInt16(operateResult2.Content, 6), base.ByteTransform.TransUInt16(operateResult2.Content, 8), base.ByteTransform.TransUInt16(operateResult2.Content, 10)));
		}

		/// <summary>
		/// <b>[商业授权]</b> 读取特殊模块的数据信息，需要指定模块单元号，模块站号，数据地址，长度信息。<br />
		/// <b>[Authorization]</b> To read the data information of a special module, you need to specify the module unit number, module slot number, data address, and length information.
		/// </summary>
		/// <param name="moduleUnit">模块的单元号</param>
		/// <param name="moduleSlot">模块的站号</param>
		/// <param name="dataPosition">模块的数据地址</param>
		/// <param name="length">长度信息</param>
		/// <returns>带有成功标识的byte[]，可以自行解析出所需要的各种类型的数据</returns>
		[HslMqttApi(Description = "Read the data information of a special module, you need to specify the module unit number, module slot number, data address, and length information.")]
		public OperateResult<byte[]> ReadSpecialModule(byte moduleUnit, byte moduleSlot, ushort dataPosition, ushort length)
		{
			
			List<byte> list = new List<byte>();
			List<byte[]> list2 = BuildReadSpecialModule(CpuNumber, moduleUnit, moduleSlot, dataPosition, length);
			for (int i = 0; i < list2.Count; i++)
			{
				OperateResult<byte[]> operateResult = ReadFromCoreServer(list2[i]);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				OperateResult<byte[]> operateResult2 = CheckContent(operateResult.Content);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult2);
				}
				list.AddRange(operateResult2.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.Start" />
		public async Task<OperateResult> StartAsync()
		{
			
			OperateResult<byte[]> command = BuildStartCommand(CpuNumber);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.Stop" />
		public async Task<OperateResult> StopAsync()
		{
			
			OperateResult<byte[]> command = BuildStopCommand(CpuNumber);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ModuleReset" />
		public async Task<OperateResult> ModuleResetAsync()
		{
			
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(new byte[4] { 97, CpuNumber, 0, 0 }, hasResponseData: false, usePackAndUnpack: true);
			if (!read.IsSuccess)
			{
				return read;
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadProgramStatus" />
		public async Task<OperateResult<int>> ReadProgramStatusAsync()
		{
			
			YokogawaLinkTcp yokogawaLinkTcp = this;
			byte[] obj = new byte[6] { 98, 0, 0, 2, 0, 1 };
			obj[1] = CpuNumber;
			OperateResult<byte[]> read = await yokogawaLinkTcp.ReadFromCoreServerAsync(obj);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int>(read);
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int>(check);
			}
			return OperateResult.CreateSuccessResult((int)check.Content[1]);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadSystemInfo" />
		public async Task<OperateResult<YokogawaSystemInfo>> ReadSystemInfoAsync()
		{
			
			YokogawaLinkTcp yokogawaLinkTcp = this;
			byte[] obj = new byte[6] { 98, 0, 0, 2, 0, 2 };
			obj[1] = CpuNumber;
			OperateResult<byte[]> read = await yokogawaLinkTcp.ReadFromCoreServerAsync(obj);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<YokogawaSystemInfo>(read);
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<YokogawaSystemInfo>(check);
			}
			return YokogawaSystemInfo.Parse(check.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadDateTime" />
		public async Task<OperateResult<DateTime>> ReadDateTimeAsync()
		{
		
			OperateResult<byte[]> read = await ReadFromCoreServerAsync(new byte[4] { 99, CpuNumber, 0, 0 });
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(read);
			}
			OperateResult<byte[]> check = CheckContent(read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(check);
			}
			return OperateResult.CreateSuccessResult(new DateTime(2000 + base.ByteTransform.TransUInt16(check.Content, 0), base.ByteTransform.TransUInt16(check.Content, 2), base.ByteTransform.TransUInt16(check.Content, 4), base.ByteTransform.TransUInt16(check.Content, 6), base.ByteTransform.TransUInt16(check.Content, 8), base.ByteTransform.TransUInt16(check.Content, 10)));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Yokogawa.YokogawaLinkTcp.ReadSpecialModule(System.Byte,System.Byte,System.UInt16,System.UInt16)" />
		public async Task<OperateResult<byte[]>> ReadSpecialModuleAsync(byte moduleUnit, byte moduleSlot, ushort dataPosition, ushort length)
		{
			
			List<byte> content = new List<byte>();
			List<byte[]> commands = BuildReadSpecialModule(CpuNumber, moduleUnit, moduleSlot, dataPosition, length);
			for (int i = 0; i < commands.Count; i++)
			{
				OperateResult<byte[]> read = await ReadFromCoreServerAsync(commands[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(read);
				}
				OperateResult<byte[]> check = CheckContent(read.Content);
				if (!check.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(check);
				}
				content.AddRange(check.Content);
			}
			return OperateResult.CreateSuccessResult(content.ToArray());
		}

		/// <summary>
		/// 检查当前的反馈内容，如果没有发生错误，就解析出实际的数据内容。<br />
		/// Check the current feedback content, if there is no error, parse out the actual data content.
		/// </summary>
		/// <param name="content">原始的数据内容</param>
		/// <returns>解析之后的数据内容</returns>
		public static OperateResult<byte[]> CheckContent(byte[] content)
		{
			if (content == null || content.Length < 2)
			{
				return new OperateResult<byte[]>(StringResources.Language.ReceiveDataLengthTooShort + " 2 content: " + content.ToHexString(' '));
			}
			if (content[1] != 0)
			{
				return new OperateResult<byte[]>(YokogawaLinkHelper.GetErrorMsg(content[1]));
			}
			if (content.Length > 4)
			{
				return OperateResult.CreateSuccessResult(content.RemoveBegin(4));
			}
			return OperateResult.CreateSuccessResult(new byte[0]);
		}

		/// <summary>
		/// 构建读取命令的原始报文信息
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">地址数据信息</param>
		/// <param name="length">数据长度信息</param>
		/// <param name="isBit">是否位访问</param>
		/// <returns>实际的读取的报文信息</returns>
		public static OperateResult<List<byte[]>> BuildReadCommand(byte cpu, string address, ushort length, bool isBit)
		{
			cpu = (byte)HslHelper.ExtractParameter(ref address, "cpu", cpu);
			OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
			}
            //OperateResult<int[], int[]> operateResult2 = ((!Authorization.asdniasnfaksndiqwhawfskhfaiw()) ? HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 65535) : ((!isBit) ? HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 64) : HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 256)));
            OperateResult<int[], int[]> operateResult2 = ((false) ? HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 65535) : ((!isBit) ? HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 64) : HslHelper.SplitReadLength(operateResult.Content.AddressStart, length, 256)));
            List<byte[]> list = new List<byte[]>();
			for (int i = 0; i < operateResult2.Content1.Length; i++)
			{
				operateResult.Content.AddressStart = operateResult2.Content1[i];
				byte[] array = new byte[12]
				{
					(byte)(isBit ? 1 : 17),
					cpu,
					0,
					8,
					0,
					0,
					0,
					0,
					0,
					0,
					0,
					0
				};
				operateResult.Content.GetAddressBinaryContent().CopyTo(array, 4);
				array[10] = BitConverter.GetBytes(operateResult2.Content2[i])[1];
				array[11] = BitConverter.GetBytes(operateResult2.Content2[i])[0];
				list.Add(array);
			}
			return OperateResult.CreateSuccessResult(list);
		}

		/// <summary>
		/// 构建随机读取的原始报文的初始命令
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">实际的数据地址信息</param>
		/// <param name="isBit">是否是位读取</param>
		/// <returns>实际的读取的报文信息</returns>
		public static OperateResult<List<byte[]>> BuildReadRandomCommand(byte cpu, string[] address, bool isBit)
		{
			List<string[]> list = SoftBasic.ArraySplitByLength(address, 32);
			List<byte[]> list2 = new List<byte[]>();
			foreach (string[] item in list)
			{
				byte[] array = new byte[6 + 6 * item.Length];
				array[0] = (byte)(isBit ? 4 : 20);
				array[1] = cpu;
				array[2] = BitConverter.GetBytes(array.Length - 4)[1];
				array[3] = BitConverter.GetBytes(array.Length - 4)[0];
				array[4] = BitConverter.GetBytes(item.Length)[1];
				array[5] = BitConverter.GetBytes(item.Length)[0];
				for (int i = 0; i < item.Length; i++)
				{
					array[1] = (byte)HslHelper.ExtractParameter(ref item[i], "cpu", cpu);
					OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(item[i], 1);
					if (!operateResult.IsSuccess)
					{
						return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
					}
					operateResult.Content.GetAddressBinaryContent().CopyTo(array, 6 * i + 6);
				}
				list2.Add(array);
			}
			return OperateResult.CreateSuccessResult(list2);
		}

		/// <summary>
		/// 构建批量写入Bool数组的命令，需要指定CPU Number信息和设备地址信息
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">设备地址数据</param>
		/// <param name="value">实际的bool数组</param>
		/// <returns>构建的写入指令</returns>
		public static OperateResult<byte[]> BuildWriteBoolCommand(byte cpu, string address, bool[] value)
		{
			cpu = (byte)HslHelper.ExtractParameter(ref address, "cpu", cpu);
			OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			byte[] array = new byte[12 + value.Length];
			array[0] = 2;
			array[1] = cpu;
			array[2] = 0;
			array[3] = (byte)(8 + value.Length);
			operateResult.Content.GetAddressBinaryContent().CopyTo(array, 4);
			array[10] = BitConverter.GetBytes(value.Length)[1];
			array[11] = BitConverter.GetBytes(value.Length)[0];
			for (int i = 0; i < value.Length; i++)
			{
				array[12 + i] = (byte)(value[i] ? 1 : 0);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 构建批量随机写入Bool数组的命令，需要指定CPU Number信息和设备地址信息
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">设备地址数据</param>
		/// <param name="value">实际的bool数组</param>
		/// <returns>构建的写入指令</returns>
		public static OperateResult<byte[]> BuildWriteRandomBoolCommand(byte cpu, string[] address, bool[] value)
		{
			if (address.Length != value.Length)
			{
				return new OperateResult<byte[]>(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			byte[] array = new byte[6 + address.Length * 8 - 1];
			array[0] = 5;
			array[1] = cpu;
			array[2] = BitConverter.GetBytes(array.Length - 4)[1];
			array[3] = BitConverter.GetBytes(array.Length - 4)[0];
			array[4] = BitConverter.GetBytes(address.Length)[1];
			array[5] = BitConverter.GetBytes(address.Length)[0];
			for (int i = 0; i < address.Length; i++)
			{
				array[1] = (byte)HslHelper.ExtractParameter(ref address[i], "cpu", cpu);
				OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address[i], 0);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				operateResult.Content.GetAddressBinaryContent().CopyTo(array, 6 + 8 * i);
				array[12 + 8 * i] = (byte)(value[i] ? 1 : 0);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 构建字写入的命令报文信息，需要指定设备地址
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">地址</param>
		/// <param name="value">原始的数据值</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<byte[]> BuildWriteWordCommand(byte cpu, string address, byte[] value)
		{
			cpu = (byte)HslHelper.ExtractParameter(ref address, "cpu", cpu);
			OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			byte[] array = new byte[12 + value.Length];
			array[0] = 18;
			array[1] = cpu;
			array[2] = 0;
			array[3] = (byte)(8 + value.Length);
			operateResult.Content.GetAddressBinaryContent().CopyTo(array, 4);
			array[10] = BitConverter.GetBytes(value.Length / 2)[1];
			array[11] = BitConverter.GetBytes(value.Length / 2)[0];
			value.CopyTo(array, 12);
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 构建随机写入字的命令的报文
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">地址</param>
		/// <param name="value">原始的数据值</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<byte[]> BuildWriteRandomWordCommand(byte cpu, string[] address, byte[] value)
		{
			if (address.Length * 2 != value.Length)
			{
				return new OperateResult<byte[]>(StringResources.Language.TwoParametersLengthIsNotSame);
			}
			byte[] array = new byte[6 + address.Length * 8];
			array[0] = 21;
			array[1] = cpu;
			array[2] = BitConverter.GetBytes(array.Length - 4)[1];
			array[3] = BitConverter.GetBytes(array.Length - 4)[0];
			array[4] = BitConverter.GetBytes(address.Length)[1];
			array[5] = BitConverter.GetBytes(address.Length)[0];
			for (int i = 0; i < address.Length; i++)
			{
				array[1] = (byte)HslHelper.ExtractParameter(ref address[i], "cpu", cpu);
				OperateResult<YokogawaLinkAddress> operateResult = YokogawaLinkAddress.ParseFrom(address[i], 0);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult);
				}
				operateResult.Content.GetAddressBinaryContent().CopyTo(array, 6 + 8 * i);
				array[12 + 8 * i] = value[i * 2];
				array[13 + 8 * i] = value[i * 2 + 1];
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 构建启动PLC的命令报文
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<byte[]> BuildStartCommand(byte cpu)
		{
			return OperateResult.CreateSuccessResult(new byte[4] { 69, cpu, 0, 0 });
		}

		/// <summary>
		/// 构建停止PLC的命令报文
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<byte[]> BuildStopCommand(byte cpu)
		{
			return OperateResult.CreateSuccessResult(new byte[4] { 70, cpu, 0, 0 });
		}

		/// <summary>
		/// 构建读取特殊模块的命令报文
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="moduleUnit">模块单元号</param>
		/// <param name="moduleSlot">模块站号</param>
		/// <param name="dataPosition">数据位置</param>
		/// <param name="length">长度信息</param>
		/// <returns>原始的报文命令</returns>
		public static List<byte[]> BuildReadSpecialModule(byte cpu, byte moduleUnit, byte moduleSlot, ushort dataPosition, ushort length)
		{
			List<byte[]> list = new List<byte[]>();
			OperateResult<int[], int[]> operateResult = HslHelper.SplitReadLength(dataPosition, length, 64);
			for (int i = 0; i < operateResult.Content1.Length; i++)
			{
				byte[] array = new byte[10];
				array[0] = 49;
				array[1] = cpu;
				array[2] = BitConverter.GetBytes(array.Length - 4)[1];
				array[3] = BitConverter.GetBytes(array.Length - 4)[0];
				array[4] = moduleUnit;
				array[5] = moduleSlot;
				array[6] = BitConverter.GetBytes(operateResult.Content1[i])[1];
				array[7] = BitConverter.GetBytes(operateResult.Content1[i])[0];
				array[8] = BitConverter.GetBytes(operateResult.Content2[i])[1];
				array[9] = BitConverter.GetBytes(operateResult.Content2[i])[0];
				list.Add(array);
			}
			return list;
		}

		/// <summary>
		/// 构建读取特殊模块的命令报文，需要传入高级地址，必须以 <b>Special:</b> 开头表示特殊模块地址，示例：Special:cpu=1;unit=0;slot=1;100<br />
		/// To construct a command message to read a special module, the advanced address needs to be passed in. 
		/// It must start with <b>Special:</b> to indicate the address of the special module, for example: Special:cpu=1;unit=0;slot=1;100
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">高级的混合地址，除了Cpu可以不携带，例如：Special:unit=0;slot=1;100</param>
		/// <param name="length">长度信息</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<List<byte[]>> BuildReadSpecialModule(byte cpu, string address, ushort length)
		{
			if (address.StartsWith("Special:") || address.StartsWith("special:"))
			{
				address = address.Substring(8);
				cpu = (byte)HslHelper.ExtractParameter(ref address, "cpu", cpu);
				OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "unit");
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<List<byte[]>>(operateResult);
				}
				OperateResult<int> operateResult2 = HslHelper.ExtractParameter(ref address, "slot");
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<List<byte[]>>(operateResult2);
				}
				try
				{
					return OperateResult.CreateSuccessResult(BuildReadSpecialModule(cpu, (byte)operateResult.Content, (byte)operateResult2.Content, ushort.Parse(address), length));
				}
				catch (Exception ex)
				{
					return new OperateResult<List<byte[]>>("Address format wrong: " + ex.Message);
				}
			}
			return new OperateResult<List<byte[]>>("Special module address must start with Special:");
		}

		/// <summary>
		/// 构建读取特殊模块的命令报文
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="moduleUnit">模块单元号</param>
		/// <param name="moduleSlot">模块站号</param>
		/// <param name="dataPosition">数据位置</param>
		/// <param name="data">数据内容</param>
		/// <returns>原始的报文命令</returns>
		public static byte[] BuildWriteSpecialModule(byte cpu, byte moduleUnit, byte moduleSlot, ushort dataPosition, byte[] data)
		{
			byte[] array = new byte[10 + data.Length];
			array[0] = 50;
			array[1] = cpu;
			array[2] = BitConverter.GetBytes(array.Length - 4)[1];
			array[3] = BitConverter.GetBytes(array.Length - 4)[0];
			array[4] = moduleUnit;
			array[5] = moduleSlot;
			array[6] = BitConverter.GetBytes(dataPosition)[1];
			array[7] = BitConverter.GetBytes(dataPosition)[0];
			array[8] = BitConverter.GetBytes(data.Length / 2)[1];
			array[9] = BitConverter.GetBytes(data.Length / 2)[0];
			data.CopyTo(array, 10);
			return array;
		}

		/// <summary>
		/// 构建写入特殊模块的命令报文，需要传入高级地址，必须以 <b>Special:</b> 开头表示特殊模块地址，示例：Special:cpu=1;unit=0;slot=1;100<br />
		/// To construct a command message to write a special module, the advanced address needs to be passed in. 
		/// It must start with <b>Special:</b> to indicate the address of the special module, for example: Special:cpu=1;unit=0;slot=1;100
		/// </summary>
		/// <param name="cpu">Cpu Number</param>
		/// <param name="address">高级的混合地址，除了Cpu可以不携带，例如：Special:unit=0;slot=1;100</param>
		/// <param name="data">写入的原始数据内容</param>
		/// <returns>原始的报文命令</returns>
		public static OperateResult<byte[]> BuildWriteSpecialModule(byte cpu, string address, byte[] data)
		{
			OperateResult<List<byte[]>> operateResult = BuildReadSpecialModule(cpu, address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			byte[] array = new byte[10 + data.Length];
			array[0] = 50;
			array[1] = operateResult.Content[0][1];
			array[2] = BitConverter.GetBytes(array.Length - 4)[1];
			array[3] = BitConverter.GetBytes(array.Length - 4)[0];
			array[4] = operateResult.Content[0][4];
			array[5] = operateResult.Content[0][5];
			array[6] = operateResult.Content[0][6];
			array[7] = operateResult.Content[0][7];
			array[8] = BitConverter.GetBytes(data.Length / 2)[1];
			array[9] = BitConverter.GetBytes(data.Length / 2)[0];
			data.CopyTo(array, 10);
			return OperateResult.CreateSuccessResult(array);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"YokogawaLinkTcp[{IpAddress}:{Port}]";
		}
	}
}
