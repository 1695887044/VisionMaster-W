using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.Profinet.Siemens
{
	/// <summary>
	/// 西门子的基于WebApi协议读写数据对象，支持对PLC的标签进行读取，适用于1500系列，该数据标签需要共享开放出来。<br />
	/// Siemens reads and writes data objects based on the WebApi protocol, supports reading PLC tags, 
	/// and is suitable for the 1500 series. The data tags need to be shared and opened.
	/// </summary>
	public class SiemensWebApi : DeviceWebApi
	{
		private string rawUrl = "api/jsonrpc";

		private string token = string.Empty;

		private SoftIncrementCount incrementCount = new SoftIncrementCount(65535L, 1L);

		/// <summary>
		/// 当前PLC的通信令牌，当调用<see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" />时，会自动获取，当然你也可以手动赋值一个合法的令牌，跳过<see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" />直接进行读写操作。<br />
		/// The communication token of the current PLC will be automatically obtained when <see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" /> is called. Of course, 
		/// you can also manually assign a valid token, skip <see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" /> and read it directly Write operation.
		/// </summary>
		public string Token
		{
			get
			{
				return token;
			}
			set
			{
				token = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的西门子WebApi通信对象<br />
		/// Instantiate a default Siemens WebApi communication object
		/// </summary>
		public SiemensWebApi()
			: this("127.0.0.1")
		{
		}

		/// <summary>
		/// 使用指定的ip地址及端口号来实例化一个对象，端口号默认使用443，如果是http访问，使用80端口号<br />
		/// Use the specified ip address and port number to instantiate an object, the port number is 443 by default, if it is http access, port 80 is used
		/// </summary>
		/// <param name="ipAddress">ip地址信息</param>
		/// <param name="port">端口号信息</param>
		public SiemensWebApi(string ipAddress, int port = 443)
			: base(ipAddress, port)
		{
			base.WordLength = 2;
			base.UseHttps = true;
			base.DefaultContentType = "application/json";
			base.ByteTransform = new ReverseBytesTransform();
		}

		/// <inheritdoc />
		protected override void AddRequestHeaders(HttpContentHeaders headers)
		{
			if (!string.IsNullOrEmpty(token))
			{
				headers.Add("X-Auth-Token", token);
			}
		}

		/// <summary>
		/// 根据设置好的用户名和密码信息，登录远程的PLC，返回是否登录成功！在读写之前，必须成功调用当前的方法，获取到token，否则无法进行通信。<br />
		/// According to the set user name and password information, log in to the remote PLC, and return whether the login is successful! 
		/// Before reading and writing, the current method must be successfully called to obtain the token, 
		/// otherwise communication cannot be carried out.
		/// </summary>
		/// <returns>是否连接成功</returns>
		public OperateResult ConnectServer()
		{
			JArray jArray = BuildConnectBody(incrementCount.GetCurrentValue(), base.UserName, base.Password);
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return CheckLoginResult(operateResult.Content);
		}

		/// <summary>
		/// 和PLC断开当前的连接信息，主要是使得Token信息失效。<br />
		/// Disconnecting the current connection information from the PLC mainly makes the token information invalid.
		/// </summary>
		/// <returns>是否断开成功</returns>
		public OperateResult ConnectClose()
		{
			return Logout();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" />
		public async Task<OperateResult> ConnectServerAsync()
		{
			OperateResult<string> read = await PostAsync(body: BuildConnectBody(incrementCount.GetCurrentValue(), base.UserName, base.Password).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckLoginResult(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectClose" />
		public async Task<OperateResult> ConnectCloseAsync()
		{
			return await LogoutAsync();
		}

		private OperateResult CheckLoginResult(string response)
		{
			try
			{
				JArray jArray = JArray.Parse(response);
				JObject jObject = (JObject)jArray[0];
				OperateResult operateResult = CheckErrorResult(jObject);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<JToken>(operateResult);
				}
				if (jObject.ContainsKey("result"))
				{
					JObject jObject2 = jObject["result"] as JObject;
					token = jObject2.Value<string>("token");
					return OperateResult.CreateSuccessResult();
				}
				return new OperateResult("Can't find result key and none token, login failed:" + Environment.NewLine + response);
			}
			catch (Exception ex)
			{
				return new OperateResult("CheckLoginResult failed:" + ex.Message + Environment.NewLine + response);
			}
		}

		/// <summary>
		/// 从PLC中根据输入的数据标签名称，读取出原始的字节数组信息，长度参数无效，需要二次解析<br />
		/// According to the input data tag name, read the original byte array information from the PLC, 
		/// the length parameter is invalid, and a second analysis is required
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="length">无效的参数</param>
		/// <returns>原始的字节数组信息</returns>
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			JArray jArray = BuildReadRawBody(incrementCount.GetCurrentValue(), address);
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return CheckReadRawResult(operateResult.Content);
		}

		/// <summary>
		/// 从PLC中根据输入的数据标签名称，按照类型byte进行读取出数据信息<br />
		/// According to the input data tag name from the PLC, read the data information according to the type byte
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <returns>包含是否成功的结果对象</returns>
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<byte[]> operateResult = Read(address, 0);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content.ToBoolArray().SelectBegin(length));
		}

		/// <summary>
		/// 将原始的字节数组信息写入到PLC的指定的数据标签里，写入方式为raw写入，是否写入成功取决于PLC的返回信息<br />
		/// Write the original byte array information to the designated data tag of the PLC. 
		/// The writing method is raw writing. Whether the writing is successful depends on the return information of the PLC.
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="value">原始的字节数据信息</param>
		/// <returns>是否成功写入PLC的结果对象</returns>
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			JArray jArray = BuildWriteRawBody(incrementCount.GetCurrentValue(), address, value);
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return CheckWriteResult(operateResult.Content);
		}

		/// <summary>
		/// 写入<see cref="T:System.Byte" />数组数据，返回是否成功<br />
		/// Write <see cref="T:System.Byte" /> array data, return whether the write was successful
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="value">写入值</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <summary>
		/// 批量写入<see cref="T:System.Boolean" />数组数据，返回是否成功<br />
		/// Batch write <see cref="T:System.Boolean" /> array data, return whether the write was successful
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="value">写入值</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			byte[] value2 = value.ToByteArray();
			return Write(address, value2);
		}

		/// <summary>
		/// 从PLC中读取字符串内容，需要指定数据标签名称，使用JSON方式读取，所以无论字符串是中英文都是支持读取的。<br />
		/// To read the string content from the PLC, you need to specify the data tag name and use JSON to read it, 
		/// so no matter whether the string is in Chinese or English, it supports reading.
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="length">无效参数</param>
		/// <returns>包含是否成功的结果对象</returns>
		[HslMqttApi("ReadString", "读取字符串")]
		public override OperateResult<string> ReadString(string address, ushort length)
		{
			JArray jArray = BuildReadJTokenBody(incrementCount.GetCurrentValue(), address);
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult<JToken> operateResult2 = CheckAndExtraOneJsonResult(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content.Value<string>());
		}

		/// <summary>
		/// 将字符串信息写入到PLC中，需要指定数据标签名称，如果PLC指定了类型为WString，才支持写入中文，否则会出现乱码。<br />
		/// To write string information into the PLC, you need to specify the data tag name. 
		/// If the PLC specifies the type as WString, it supports writing in Chinese, otherwise garbled characters will appear.
		/// </summary>
		/// <param name="address">标签的地址信息，例如 "全局DB".Static_21</param>
		/// <param name="value">字符串数据信息</param>
		/// <returns>是否成功写入</returns>
		[HslMqttApi("WriteString", "")]
		public override OperateResult Write(string address, string value)
		{
			JArray jArray = BuildWriteJTokenBody(incrementCount.GetCurrentValue(), address, new JValue(value));
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return CheckWriteResult(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Read(System.String,System.UInt16)" />
		public override async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
		{
			OperateResult<string> read = await PostAsync(body: BuildReadRawBody(incrementCount.GetCurrentValue(), address).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return CheckReadRawResult(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadByte(System.String)" />
		public async Task<OperateResult<byte>> ReadByteAsync(string address)
		{
			return ByteTransformHelper.GetResultFromArray(await ReadAsync(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadBool(System.String,System.UInt16)" />
		public override async Task<OperateResult<bool[]>> ReadBoolAsync(string address, ushort length)
		{
			OperateResult<byte[]> read = await ReadAsync(address, 0);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(read);
			}
			return OperateResult.CreateSuccessResult(read.Content.ToBoolArray().SelectBegin(length));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadString(System.String,System.UInt16)" />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length)
		{
			OperateResult<string> read = await PostAsync(body: BuildReadJTokenBody(incrementCount.GetCurrentValue(), address).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			OperateResult<JToken> extra = CheckAndExtraOneJsonResult(read.Content);
			if (!extra.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(extra);
			}
			return OperateResult.CreateSuccessResult(extra.Content.Value<string>());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Write(System.String,System.Byte[])" />
		public override async Task<OperateResult> WriteAsync(string address, byte[] value)
		{
			OperateResult<string> read = await PostAsync(body: BuildWriteRawBody(incrementCount.GetCurrentValue(), address, value).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckWriteResult(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Write(System.String,System.Byte)" />
		public async Task<OperateResult> WriteAsync(string address, byte value)
		{
			return await WriteAsync(address, new byte[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Write(System.String,System.Boolean[])" />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			byte[] buffer = value.ToByteArray();
			return await WriteAsync(address, buffer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Write(System.String,System.String)" />
		public override async Task<OperateResult> WriteAsync(string address, string value)
		{
			OperateResult<string> read = await PostAsync(body: BuildWriteJTokenBody(incrementCount.GetCurrentValue(), address, new JValue(value)).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckWriteResult(read.Content);
		}

		/// <summary>
		/// 读取当前PLC的操作模式，如果读取成功，结果将会是如下值之一：STOP, STARTUP, RUN, HOLD, -<br />
		/// Read the current operating mode of the PLC. If the reading is successful, 
		/// the result will be one of the following values: STOP, STARTUP, RUN, HOLD,-
		/// </summary>
		/// <returns>结果对象</returns>
		[HslMqttApi("读取当前PLC的操作模式，如果读取成功，结果将会是如下值之一：STOP, STARTUP, RUN, HOLD, -")]
		public OperateResult<string> ReadOperatingMode()
		{
			JArray jArray = BuildRequestBody("Plc.ReadOperatingMode", null, incrementCount.GetCurrentValue());
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult<JToken> operateResult2 = CheckAndExtraOneJsonResult(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content.Value<string>());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadOperatingMode" />
		public async Task<OperateResult<string>> ReadOperatingModeAsync()
		{
			OperateResult<string> read = await PostAsync(body: BuildRequestBody("Plc.ReadOperatingMode", null, incrementCount.GetCurrentValue()).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			OperateResult<JToken> extra = CheckAndExtraOneJsonResult(read.Content);
			if (!extra.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(extra);
			}
			return OperateResult.CreateSuccessResult(extra.Content.Value<string>());
		}

		/// <summary>
		/// <b>[商业授权]</b> 从PLC读取多个地址的数据信息，每个地址的数据类型可以不一致，需要自动从<see cref="T:Newtonsoft.Json.Linq.JToken" />中提取出正确的数据<br />
		/// <b>[Authorization]</b> Read the data information of multiple addresses from the PLC, the data type of each address can be inconsistent, 
		/// you need to automatically extract the correct data from <see cref="T:Newtonsoft.Json.Linq.JToken" />
		/// </summary>
		/// <remarks>
		/// 一旦中间有一个地址失败了，本方法就会返回失败，所以在调用本方法时，需要确保所有的地址正确。
		/// </remarks>
		/// <param name="address">"全局DB".Static_21</param>
		/// <returns>返回是否读取成功的结果对象</returns>
		[HslMqttApi("ReadJTokens", "从PLC读取多个地址的数据信息，每个地址的数据类型可以不一致")]
		public OperateResult<JToken[]> Read(string[] address)
		{
			
			JArray jArray = BuildReadJTokenBody(incrementCount.GetCurrentValue(), address);
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<JToken[]>(operateResult);
			}
			return CheckAndExtraJsonResult(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Read(System.String[])" />
		public async Task<OperateResult<JToken[]>> ReadAsync(string[] address)
		{
			
			OperateResult<string> read = await PostAsync(body: BuildReadJTokenBody(incrementCount.GetCurrentValue(), address).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<JToken[]>(read);
			}
			return CheckAndExtraJsonResult(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.ReadDateTime(System.String)" />
		[HslMqttApi("ReadDateTime", "读取PLC的时间格式的数据，这个格式是s7格式的一种")]
		public OperateResult<DateTime> ReadDateTime(string address)
		{
			OperateResult<byte[]> operateResult = Read(address, 8);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(operateResult);
			}
			return SiemensDateTime.FromByteArray(operateResult.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensS7Net.Write(System.String,System.DateTime)" />
		[HslMqttApi("WriteDateTime", "写入PLC的时间格式的数据，这个格式是s7格式的一种")]
		public OperateResult Write(string address, DateTime dateTime)
		{
			return Write(address, SiemensDateTime.ToByteArray(dateTime));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadDateTime(System.String)" />
		public async Task<OperateResult<DateTime>> ReadDateTimeAsync(string address)
		{
			OperateResult<byte[]> read = await ReadAsync(address, 8);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<DateTime>(read);
			}
			return SiemensDateTime.FromByteArray(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Write(System.String,System.DateTime)" />
		public async Task<OperateResult> WriteAsync(string address, DateTime dateTime)
		{
			return await WriteAsync(address, SiemensDateTime.ToByteArray(dateTime));
		}

		/// <summary>
		/// <b>[商业授权]</b> 读取PLC的RPC接口的版本号信息<br />
		/// <b>[Authorization]</b> Read the version number information of the PLC's RPC interface
		/// </summary>
		/// <returns>包含是否成功的结果对象</returns>
		[HslMqttApi("读取PLC的RPC接口的版本号信息")]
		public OperateResult<double> ReadVersion()
		{
			
			JArray jArray = BuildRequestBody("Api.Version", null, incrementCount.GetCurrentValue());
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(operateResult);
			}
			OperateResult<JToken> operateResult2 = CheckAndExtraOneJsonResult(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(operateResult2);
			}
			return OperateResult.CreateSuccessResult(operateResult2.Content.Value<double>());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadVersion" />
		public async Task<OperateResult<double>> ReadVersionAsync()
		{
			
			OperateResult<string> read = await PostAsync(body: BuildRequestBody("Api.Version", null, incrementCount.GetCurrentValue()).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(read);
			}
			OperateResult<JToken> extra = CheckAndExtraOneJsonResult(read.Content);
			if (!extra.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(extra);
			}
			return OperateResult.CreateSuccessResult(extra.Content.Value<double>());
		}

		/// <summary>
		/// <b>[商业授权]</b> 对PLC对象进行PING操作<br />
		/// <b>[Authorization]</b> PING the PLC object
		/// </summary>
		/// <returns>是否PING成功</returns>
		[HslMqttApi("对PLC对象进行PING操作")]
		public OperateResult ReadPing()
		{
			
			JArray jArray = BuildRequestBody("Api.Ping", null, incrementCount.GetCurrentValue());
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return CheckErrorResult(JArray.Parse(operateResult.Content)[0] as JObject);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ReadPing" />
		public async Task<OperateResult> ReadPingAsync()
		{
			
			OperateResult<string> read = await PostAsync(body: BuildRequestBody("Api.Ping", null, incrementCount.GetCurrentValue()).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckErrorResult(JArray.Parse(read.Content)[0] as JObject);
		}

		/// <summary>
		/// 从PLC退出登录，当前的token信息失效，需要再次调用<see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" />获取新的token信息才可以。<br />
		/// Log out from the PLC, the current token information is invalid, you need to call <see cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.ConnectServer" /> again to get the new token information.
		/// </summary>
		/// <returns>是否成功</returns>
		[HslMqttApi("从PLC退出登录，当前的token信息失效，需要再次调用ConnectServer获取新的token信息才可以")]
		public OperateResult Logout()
		{
			JArray jArray = BuildRequestBody("Api.Logout", null, incrementCount.GetCurrentValue());
			OperateResult<string> operateResult = Post(rawUrl, jArray.ToString());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return CheckErrorResult(JArray.Parse(operateResult.Content)[0] as JObject);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Siemens.SiemensWebApi.Logout" />
		public async Task<OperateResult> LogoutAsync()
		{
			OperateResult<string> read = await PostAsync(body: BuildRequestBody("Api.Logout", null, incrementCount.GetCurrentValue()).ToString(), rawUrl: rawUrl);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckErrorResult(JArray.Parse(read.Content)[0] as JObject);
		}

		private static JObject GetJsonRpc(string method, JObject paramsJson, long id)
		{
			JObject jObject = new JObject();
			jObject.Add("jsonrpc", new JValue("2.0"));
			jObject.Add("method", new JValue(method));
			jObject.Add("id", new JValue(id));
			if (paramsJson != null)
			{
				jObject.Add("params", paramsJson);
			}
			return jObject;
		}

		private static JArray BuildRequestBody(string method, JObject paramsJson, long id)
		{
			return new JArray { GetJsonRpc(method, paramsJson, id) };
		}

		private static JArray BuildConnectBody(long id, string name, string password)
		{
			JObject jObject = new JObject();
			jObject.Add("user", new JValue(name));
			jObject.Add("password", new JValue(password));
			return BuildRequestBody("Api.Login", jObject, id);
		}

		private static JArray BuildReadRawBody(long id, string address)
		{
			JObject jObject = new JObject();
			jObject.Add("var", new JValue(address));
			jObject.Add("mode", new JValue("raw"));
			return BuildRequestBody("PlcProgram.Read", jObject, id);
		}

		private static JArray BuildWriteRawBody(long id, string address, byte[] value)
		{
			JObject jObject = new JObject();
			jObject.Add("var", new JValue(address));
			jObject.Add("mode", new JValue("raw"));
			jObject.Add("value", new JArray(((IEnumerable<byte>)value).Select((Func<byte, int>)((byte m) => m)).ToArray()));
			return BuildRequestBody("PlcProgram.Write", jObject, id);
		}

		private static JArray BuildWriteJTokenBody(long id, string address, JToken value)
		{
			JObject jObject = new JObject();
			jObject.Add("var", new JValue(address));
			jObject.Add("value", value);
			return BuildRequestBody("PlcProgram.Write", jObject, id);
		}

		private static JArray BuildReadJTokenBody(long id, string address)
		{
			JObject jObject = new JObject();
			jObject.Add("var", new JValue(address));
			return BuildRequestBody("PlcProgram.Read", jObject, id);
		}

		private static JArray BuildReadJTokenBody(long id, string[] address)
		{
			JArray jArray = new JArray();
			for (int i = 0; i < address.Length; i++)
			{
				JObject jObject = new JObject();
				jObject.Add("var", new JValue(address[i]));
				jArray.Add(GetJsonRpc("PlcProgram.Read", jObject, id + i));
			}
			return jArray;
		}

		private static OperateResult CheckErrorResult(JObject json)
		{
			if (json.ContainsKey("error"))
			{
				JObject jObject = json["error"] as JObject;
				int err = jObject.Value<int>("code");
				string msg = jObject.Value<string>("message");
				return new OperateResult(err, msg);
			}
			return OperateResult.CreateSuccessResult();
		}

		private static OperateResult<byte[]> CheckReadRawResult(string response)
		{
			OperateResult<JToken> operateResult = CheckAndExtraOneJsonResult(response);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			JArray source = operateResult.Content as JArray;
			return OperateResult.CreateSuccessResult(source.Select((JToken m) => m.Value<byte>()).ToArray());
		}

		private static OperateResult CheckWriteResult(string response)
		{
			JArray jArray = JArray.Parse(response);
			JObject jObject = (JObject)jArray[0];
			OperateResult operateResult = CheckErrorResult(jObject);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<JToken>(operateResult);
			}
			if (jObject.ContainsKey("result"))
			{
				return jObject["result"].Value<bool>() ? OperateResult.CreateSuccessResult() : new OperateResult(jObject.ToString());
			}
			return new OperateResult<JToken>("Can't find result key and none token, login failed:" + Environment.NewLine + response);
		}

		private static OperateResult<JToken> CheckAndExtraOneJsonResult(string response)
		{
			OperateResult<JToken[]> operateResult = CheckAndExtraJsonResult(response);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<JToken>(operateResult);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content[0]);
		}

		private static OperateResult<JToken[]> CheckAndExtraJsonResult(string response)
		{
			try
			{
				JArray jArray = JArray.Parse(response);
				List<JToken> list = new List<JToken>();
				for (int i = 0; i < jArray.Count; i++)
				{
					JObject jObject = jArray[i] as JObject;
					if (jObject != null)
					{
						OperateResult operateResult = CheckErrorResult(jObject);
						if (!operateResult.IsSuccess)
						{
							return OperateResult.CreateFailedResult<JToken[]>(operateResult);
						}
						if (!jObject.ContainsKey("result"))
						{
							return new OperateResult<JToken[]>("Can't find result key and none token, login failed:" + Environment.NewLine + response);
						}
						list.Add(jObject["result"]);
					}
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<JToken[]>("CheckAndExtraJsonResult failed: " + ex.Message + Environment.NewLine + "Content: " + response);
			}
		}
	}
}
