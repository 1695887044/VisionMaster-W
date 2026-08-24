using System;
using System.Text;
using System.Threading.Tasks;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 机器人的基于webapi接口的基础类信息
	/// </summary>
	public class NetworkWebApiRobotBase : NetworkWebApiBase
	{
		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String)" />
		public NetworkWebApiRobotBase(string ipAddress)
			: base(ipAddress)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String,System.Int32)" />
		public NetworkWebApiRobotBase(string ipAddress, int port)
			: base(ipAddress, port)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String,System.Int32,System.String,System.String)" />
		public NetworkWebApiRobotBase(string ipAddress, int port, string name, string password)
			: base(ipAddress, port, name, password)
		{
		}

		/// <summary>
		/// 等待重写的额外的指令信息的支持。除了url的形式之外，还支持基于命令的数据交互<br />
		/// Additional instruction information waiting for rewriting is supported.In addition to the url format, command based data interaction is supported
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <returns>是否读取成功的内容</returns>
		protected virtual OperateResult<string> ReadByAddress(string address)
		{
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiRobotBase.ReadByAddress(System.String)" />
		protected virtual async Task<OperateResult<string>> ReadByAddressAsync(string address)
		{
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <summary>
		/// 读取对方信息的的数据信息，通常是针对GET的方法信息设计的。如果使用了url=开头，就表示是使用了原生的地址访问<br />
		/// Read the other side of the data information, usually designed for the GET method information.If you start with url=, you are using native address access
		/// </summary>
		/// <param name="address">无效参数</param>
		/// <returns>带有成功标识的byte[]数组</returns>
		public virtual OperateResult<byte[]> Read(string address)
		{
			OperateResult<string> operateResult = ReadString(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult(Encoding.UTF8.GetBytes(operateResult.Content));
		}

		/// <summary>
		/// 读取对方信息的的字符串数据信息，通常是针对GET的方法信息设计的。如果使用了url=开头，就表示是使用了原生的地址访问<br />
		/// The string data information that reads the other party information, usually designed for the GET method information.If you start with url=, you are using native address access
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <returns>带有成功标识的字符串数据</returns>
		public virtual OperateResult<string> ReadString(string address)
		{
			
			if (address.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
			{
				return Get(address.Substring(4));
			}
			return ReadByAddress(address);
		}

		/// <summary>
		/// 使用POST的方式来向对方进行请求数据信息，需要使用url=开头，来表示是使用了原生的地址访问<br />
		/// Using POST to request data information from the other party, we need to start with url= to indicate that we are using native address access
		/// </summary>
		/// <param name="address">指定的地址信息，有些设备可能不支持</param>
		/// <param name="value">原始的字节数据信息</param>
		/// <returns>是否成功的写入</returns>
		public virtual OperateResult Write(string address, byte[] value)
		{
			return Write(address, Encoding.Default.GetString(value));
		}

		/// <summary>
		/// 使用POST的方式来向对方进行请求数据信息，需要使用url=开头，来表示是使用了原生的地址访问<br />
		/// Using POST to request data information from the other party, we need to start with url= to indicate that we are using native address access
		/// </summary>
		/// <param name="address">指定的地址信息</param>
		/// <param name="value">字符串的数据信息</param>
		/// <returns>是否成功的写入</returns>
		public virtual OperateResult Write(string address, string value)
		{
			if (address.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
			{
				return Post(address.Substring(4), value);
			}
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiRobotBase.Read(System.String)" />
		public virtual async Task<OperateResult<byte[]>> ReadAsync(string address)
		{
			OperateResult<string> read = await ReadStringAsync(address);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return OperateResult.CreateSuccessResult(Encoding.UTF8.GetBytes(read.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiRobotBase.ReadString(System.String)" />
		public virtual async Task<OperateResult<string>> ReadStringAsync(string address)
		{
			
			if (address.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
			{
				return await GetAsync(address.Substring(4));
			}
			return await ReadByAddressAsync(address);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiRobotBase.Write(System.String,System.Byte[])" />
		public virtual async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			return await WriteAsync(address, Encoding.Default.GetString(value));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiRobotBase.Write(System.String,System.String)" />
		public virtual async Task<OperateResult> WriteAsync(string address, string value)
		{
			if (address.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
			{
				return await PostAsync(address.Substring(4), value);
			}
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetworkWebApiRobotBase[{base.IpAddress}:{base.Port}]";
		}
	}
}
