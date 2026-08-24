using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HslCommunication.Core.Net;

namespace HslCommunication.Core.Device
{
	/// <summary>
	/// 基于WebApi接口的设备基类
	/// </summary>
	public class DeviceWebApi : DeviceCommunication
	{
		private NetworkWebApiBase webApi;

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.IpAddress" />
		public string IpAddress
		{
			get
			{
				return webApi.IpAddress;
			}
			set
			{
				webApi.IpAddress = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.Port" />
		public int Port
		{
			get
			{
				return webApi.Port;
			}
			set
			{
				webApi.Port = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.UserName" />
		public string UserName
		{
			get
			{
				return webApi.UserName;
			}
			set
			{
				webApi.UserName = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.Password" />
		public string Password
		{
			get
			{
				return webApi.Password;
			}
			set
			{
				webApi.Password = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.UseHttps" />
		public bool UseHttps
		{
			get
			{
				return webApi.UseHttps;
			}
			set
			{
				webApi.UseHttps = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.DefaultContentType" />
		public string DefaultContentType
		{
			get
			{
				return webApi.DefaultContentType;
			}
			set
			{
				webApi.DefaultContentType = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.UseEncodingISO" />
		public bool UseEncodingISO
		{
			get
			{
				return webApi.UseEncodingISO;
			}
			set
			{
				webApi.UseEncodingISO = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkWebApiBase.Client" />
		public HttpClient Client => webApi.Client;

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String)" />
		public DeviceWebApi(string ipAddress)
			: this(ipAddress, 80, string.Empty, string.Empty)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String,System.Int32)" />
		public DeviceWebApi(string ipAddress, int port)
			: this(ipAddress, port, string.Empty, string.Empty)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.#ctor(System.String,System.Int32,System.String,System.String)" />
		public DeviceWebApi(string ipAddress, int port, string name, string password)
		{
			webApi = new NetworkWebApiBase(ipAddress, port, name, password);
			webApi.AddRequestHeadersAction = AddRequestHeaders;
		}

		/// <summary>
		/// 针对请求的头信息进行额外的处理
		/// </summary>
		/// <param name="headers">头信息</param>
		protected virtual void AddRequestHeaders(HttpContentHeaders headers)
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.Get(System.String)" />
		public OperateResult<string> Get(string rawUrl)
		{
			return webApi.Get(rawUrl);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.Post(System.String,System.String)" />
		public OperateResult<string> Post(string rawUrl, string body)
		{
			return webApi.Post(rawUrl, body);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceWebApi.Get(System.String)" />
		public async Task<OperateResult<string>> GetAsync(string rawUrl)
		{
			return await webApi.GetAsync(rawUrl);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Device.DeviceWebApi.Post(System.String,System.String)" />
		public async Task<OperateResult<string>> PostAsync(string rawUrl, string body)
		{
			return await webApi.PostAsync(rawUrl, body);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DeviceWebApi[{IpAddress}:{Port}]";
		}
	}
}
