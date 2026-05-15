using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.LogNet;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 基于webapi的数据访问的基类，提供了基本的http接口的交互功能<br />
	/// A base class for data access based on webapi that provides basic HTTP interface interaction
	/// </summary>
	/// <remarks>
	/// 当前的基类在.net framework2.0上存在问题，在.net framework4.5及.net standard上运行稳定而且正常
	/// </remarks>
	public class NetworkWebApiBase
	{
		private string host = "127.0.0.1";

		private string ipAddress = "127.0.0.1";

		private int port = 80;

		private string name = string.Empty;

		private string password = string.Empty;

		private HttpClient httpClient;

		/// <summary>
		/// 针对请求的头信息进行额外的处理
		/// </summary>
		public Action<HttpContentHeaders> AddRequestHeadersAction { get; set; }

		/// <summary>
		/// 获取或设置远程服务器的IP地址<br />
		/// Gets or sets the IP address of the remote server
		/// </summary>
		public string IpAddress
		{
			get
			{
				return ipAddress;
			}
			set
			{
				ipAddress = value;
			}
		}

		/// <summary>
		/// 获取或设置远程服务器的端口号信息<br />
		/// Gets or sets the port number information for the remote server
		/// </summary>
		public int Port
		{
			get
			{
				return port;
			}
			set
			{
				port = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的用户名<br />
		/// Get or set the current username
		/// </summary>
		public string UserName
		{
			get
			{
				return name;
			}
			set
			{
				name = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的密码<br />
		/// Get or set the current password
		/// </summary>
		public string Password
		{
			get
			{
				return password;
			}
			set
			{
				password = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkBase.LogNet" />
		public ILogNet LogNet { get; set; }

		/// <summary>
		/// 是否启用Https的协议访问，对于Https来说，端口号默认为 443<br />
		/// Whether to enable Https protocol access, for Https, the port number defaults to 443
		/// </summary>
		public bool UseHttps { get; set; }

		/// <summary>
		/// 默认的内容类型，如果为空，则不进行设置操作。例如设置为 "text/plain", "application/json", "text/html" 等等。<br />
		/// The default content type, if it is empty, no setting operation will be performed. For example, set to "text/plain", "application/json", "text/html" and so on.
		/// </summary>
		public string DefaultContentType { get; set; }

		/// <summary>
		/// 获取或设置是否使用ISO的编码信息，默认为 False<br />
		/// Get or set whether to use ISO encoding information, the default is False
		/// </summary>
		/// <remarks>
		/// 在访问某些特殊的API的时候，会发生异常"The character set provided in ContentType is invalid...."，这时候，只需要将本属性设置为 True 即可。
		/// </remarks>
		public bool UseEncodingISO { get; set; } = false;


		/// <summary>
		/// 获取当前的HttpClinet的客户端<br />
		/// Get the current HttpClinet client
		/// </summary>
		public HttpClient Client => httpClient;

		/// <summary>
		/// 获取当前的远程服务器的地址，可能是ip，也可能是网址。<br />
		/// Get the address of the current remote server, which may be an IP address or a web address.
		/// </summary>
		public string Host => host;

		/// <summary>
		/// 使用指定的ip地址来初始化对象<br />
		/// Initializes the object using the specified IP address
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		public NetworkWebApiBase(string ipAddress)
			: this(ipAddress, 80, string.Empty, string.Empty)
		{
		}

		/// <summary>
		/// 使用指定的ip地址及端口号来初始化对象<br />
		/// Initializes the object with the specified IP address and port number
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		/// <param name="port">端口号信息</param>
		public NetworkWebApiBase(string ipAddress, int port)
			: this(ipAddress, port, string.Empty, string.Empty)
		{
		}

		/// <summary>
		/// 使用指定的ip地址，端口号，用户名，密码来初始化对象<br />
		/// Initialize the object with the specified IP address, port number, username, and password
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		/// <param name="port">端口号信息</param>
		/// <param name="name">用户名</param>
		/// <param name="password">密码</param>
		public NetworkWebApiBase(string ipAddress, int port, string name, string password)
		{
			host = ipAddress;
			this.ipAddress = HslHelper.GetIpAddressFromInput(ipAddress);
			this.port = port;
			this.name = name;
			this.password = password;
			if (!string.IsNullOrEmpty(name))
			{
				HttpClientHandler httpClientHandler = new HttpClientHandler
				{
					Credentials = new NetworkCredential(name, password)
				};
				httpClientHandler.Proxy = null;
				httpClientHandler.UseProxy = false;
				ServicePointManager.ServerCertificateValidationCallback = TrustAllValidationCallback;
				httpClient = new HttpClient(httpClientHandler);
				ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
			}
			else
			{
				ServicePointManager.ServerCertificateValidationCallback = TrustAllValidationCallback;
				httpClient = new HttpClient();
				ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
			}
		}

		private bool TrustAllValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
		{
			return true;
		}

		/// <summary>
		/// 针对请求的头信息进行额外的处理，可以重写用来实现一些特殊的信息添加到请求头中
		/// </summary>
		/// <param name="headers">头信息</param>
		protected virtual void AddRequestHeaders(HttpContentHeaders headers)
		{
		}

		private string GetEntireUrl(string url)
		{
			if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				return url;
			}
			return string.Format("{0}://{1}:{2}/{3}", UseHttps ? "https" : "http", host, port, url.StartsWith("/") ? url.Substring(1) : url);
		}

		/// <summary>
		/// 使用GET操作从网络中获取到数据信息，地址
		/// </summary>
		/// <param name="rawUrl">除去ip地址和端口的地址</param>
		/// <returns>返回的数据内容</returns>
		public OperateResult<string> Get(string rawUrl)
		{
			string entireUrl = GetEntireUrl(rawUrl);
			try
			{
				using HttpResponseMessage httpResponseMessage = httpClient.GetAsync(entireUrl).Result;
				using HttpContent httpContent = httpResponseMessage.Content;
				httpResponseMessage.EnsureSuccessStatusCode();
				if (UseEncodingISO)
				{
					using (StreamReader streamReader = new StreamReader(httpContent.ReadAsStreamAsync().Result, Encoding.GetEncoding("iso-8859-1")))
					{
						return OperateResult.CreateSuccessResult(streamReader.ReadToEnd());
					}
				}
				return OperateResult.CreateSuccessResult(httpContent.ReadAsStringAsync().Result);
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
		}

		/// <summary>
		/// 使用POST命令去提交数据内容，然后返回相关的数据信息
		/// </summary>
		/// <param name="rawUrl">已经去除ip地址，端口号的api信息</param>
		/// <param name="body">数据内容</param>
		/// <returns>从服务器返回的内容</returns>
		public OperateResult<string> Post(string rawUrl, string body)
		{
			string entireUrl = GetEntireUrl(rawUrl);
			try
			{
				using StringContent stringContent = new StringContent(body);
				if (!string.IsNullOrEmpty(DefaultContentType))
				{
					stringContent.Headers.ContentType = new MediaTypeHeaderValue(DefaultContentType);
				}
				AddRequestHeaders(stringContent.Headers);
				AddRequestHeadersAction?.Invoke(stringContent.Headers);
				using HttpResponseMessage httpResponseMessage = httpClient.PostAsync(entireUrl, stringContent).Result;
				using HttpContent httpContent = httpResponseMessage.Content;
				httpResponseMessage.EnsureSuccessStatusCode();
				if (UseEncodingISO)
				{
					using (StreamReader streamReader = new StreamReader(httpContent.ReadAsStreamAsync().Result, Encoding.GetEncoding("iso-8859-1")))
					{
						return OperateResult.CreateSuccessResult(streamReader.ReadToEnd());
					}
				}
				return OperateResult.CreateSuccessResult(httpContent.ReadAsStringAsync().Result);
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message + Environment.NewLine + "Url: " + entireUrl);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.Get(System.String)" />
		public async Task<OperateResult<string>> GetAsync(string rawUrl)
		{
			string url = GetEntireUrl(rawUrl);
			try
			{
				using HttpResponseMessage response = await httpClient.GetAsync(url);
				using HttpContent content = response.Content;
				response.EnsureSuccessStatusCode();
				if (UseEncodingISO)
				{
					using (StreamReader sr = new StreamReader(await content.ReadAsStreamAsync(), Encoding.GetEncoding("iso-8859-1")))
					{
						return OperateResult.CreateSuccessResult(sr.ReadToEnd());
					}
				}
				return OperateResult.CreateSuccessResult(await content.ReadAsStringAsync());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return new OperateResult<string>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkWebApiBase.Post(System.String,System.String)" />
		public async Task<OperateResult<string>> PostAsync(string rawUrl, string body)
		{
			string url = GetEntireUrl(rawUrl);
			try
			{
				using StringContent stringContent = new StringContent(body);
				if (!string.IsNullOrEmpty(DefaultContentType))
				{
					stringContent.Headers.ContentType = new MediaTypeHeaderValue(DefaultContentType);
				}
				AddRequestHeaders(stringContent.Headers);
				AddRequestHeadersAction?.Invoke(stringContent.Headers);
				using HttpResponseMessage response = await httpClient.PostAsync(url, stringContent);
				using HttpContent content = response.Content;
				response.EnsureSuccessStatusCode();
				if (UseEncodingISO)
				{
					using (StreamReader sr = new StreamReader(await content.ReadAsStreamAsync(), Encoding.GetEncoding("iso-8859-1")))
					{
						return OperateResult.CreateSuccessResult(sr.ReadToEnd());
					}
				}
				return OperateResult.CreateSuccessResult(await content.ReadAsStringAsync());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return new OperateResult<string>(ex.Message);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"NetworkWebApiBase[{ipAddress}:{port}]";
		}
	}
}
