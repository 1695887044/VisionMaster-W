using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.LogNet;
using HslCommunication.MQTT;
using HslCommunication.Reflection;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 一个支持完全自定义的Http服务器，支持返回任意的数据信息，方便调试信息，详细的案例请查看API文档信息<br />
	/// A Http server that supports fully customized, supports returning arbitrary data information, which is convenient for debugging information. For detailed cases, please refer to the API documentation information
	/// </summary>
	/// <remarks>
	/// 使用RPC接口注册的方式，可以更加便捷快速的实现webapi接口创建及设计，自带接口列表浏览查看，注释查看，签名查看，甚至调用次数及耗时查看。<br />
	/// Using the RPC interface registration method, you can more conveniently and quickly realize the creation and design of webapi interfaces, browse and view the built-in interface list, 
	/// view comments, view signatures, and even view the number of calls and time-consuming.
	/// </remarks>
	/// <example>
	/// 我们先来看看一个最简单的例子，如何进行实例化的操作。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample1" title="基本的实例化" />
	/// 通常来说，基本的实例化，返回固定的数据并不能满足我们的需求，我们需要返回自定义的数据，有一个委托，我们需要自己指定方法.
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample2" title="自定义返回" />
	/// 我们实际的需求可能会更加的复杂，不同的网址会返回不同的数据，所以接下来我们需要对网址信息进行判断。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample3" title="区分网址" />
	/// 如果我们想增加安全性的验证功能，比如我们的api接口需要增加用户名和密码的功能，那么我们也可以实现
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample4" title="安全实现" />
	/// 当然了，如果我们想反回一个完整的html网页，也是可以实现的，甚至添加一些js的脚本，下面的例子就简单的说明了如何操作
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample5" title="返回html" />
	/// 如果需要实现跨域的操作，可以将属性<see cref="P:HslCommunication.Enthernet.HttpServer.IsCrossDomain" /> 设置为<c>True</c><br /><br />
	/// 上述的代码编写接口还是很费劲的，接口的方法还不能在服务器复用，所以参考下面的代码来编写接口会更加的高级和便捷。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Enthernet\HttpServerSample.cs" region="Sample6" title="高级RPC注册" />
	/// </example>
	public class HttpServer
	{
		private Dictionary<string, MqttRpcApiInfo> apiTopicServiceDict;

		private object rpcApiLock;

		private int receiveBufferSize = 2048;

		private int port = 80;

		private HttpListener listener;

		private ILogNet logNet;

		private Encoding encoding = Encoding.UTF8;

		private Func<HttpListenerRequest, HttpListenerResponse, string, object> handleRequestFunc;

		private LogStatisticsDict statisticsDict;

		private bool loginAccess = false;

		private MqttCredential[] loginCredentials;

		private Action<HttpApiCalledInfo> apiCalledAction;

		private bool useHttps = false;

		/// <summary>
		/// 额外的处理请求信息的委托定义，将可以自定义处理一些特殊的请求头数据，例如一些账户相关的其他属性，语言属性等等。<br />
		/// Additional delegate definitions for processing request information will be able to customize some special request header data, 
		/// such as some other account-related attributes, language attributes, and so on.
		/// </summary>
		public Action<HttpListenerRequest, ISessionContext> DealWithHttpListenerRequest { get; set; }

		/// <summary>
		/// 获取当前的日志统计信息，可以获取到每个API的每天的调度次数信息，缓存60天数据，如果需要存储本地，需要调用<see cref="M:HslCommunication.LogNet.LogStatisticsDict.SaveToFile(System.String)" />方法。<br />
		/// Get the current log statistics, you can get the daily scheduling times information of each API, and cache 60-day data. 
		/// If you need to store it locally, you need to call the <see cref="M:HslCommunication.LogNet.LogStatisticsDict.SaveToFile(System.String)" /> method.
		/// </summary>
		public LogStatisticsDict LogStatistics => statisticsDict;

		/// <inheritdoc cref="P:HslCommunication.Core.Net.NetworkBase.LogNet" />
		public ILogNet LogNet
		{
			get
			{
				return logNet;
			}
			set
			{
				logNet = value;
			}
		}

		/// <summary>
		/// 获取或设置当前服务器的编码信息，默认为UTF8编码<br />
		/// Get or set the encoding information of the current server, the default is UTF8 encoding
		/// </summary>
		public Encoding ServerEncoding
		{
			get
			{
				return encoding;
			}
			set
			{
				encoding = value;
			}
		}

		/// <summary>
		/// 获取或设置是否支持跨域操作<br />
		/// Get or set whether to support cross-domain operations
		/// </summary>
		public bool IsCrossDomain { get; set; }

		/// <summary>
		/// 获取或设置当前的自定义的处理信息，如果不想继承实现方法，可以使用本属性来关联你自定义的方法。<br />
		/// Get or set the current custom processing information. If you don't want to inherit the implementation method, you can use this attribute to associate your custom method.
		/// </summary>
		public Func<HttpListenerRequest, HttpListenerResponse, string, object> HandleRequestFunc
		{
			get
			{
				return handleRequestFunc;
			}
			set
			{
				handleRequestFunc = value;
			}
		}

		/// <summary>
		/// 获取或设置当前的自定义处理文件上传的信息，自动解析好文件的基本信息<br />
		/// Obtain or set the current custom processing file upload information, and automatically parse the basic information of the file
		/// </summary>
		public Func<HttpListenerRequest, HttpListenerResponse, HttpUploadFile, string> HandleFileUpload { get; set; }

		/// <summary>
		/// 获取当前的端口号信息<br />
		/// Get current port number information
		/// </summary>
		public int Port => port;

		/// <summary>
		/// 获取或设置当前接口调用信息处理的委托，可以用于对接口调用的二次分析，在接口调用完成的时候，将触发本委托<br />
		/// Obtain or set the delegate for the current interface call information processing, which can be used for secondary analysis of the interface call, and the delegate will be triggered when the interface call is completed
		/// </summary>
		public Action<HttpApiCalledInfo> ApiCalledAction
		{
			get
			{
				return apiCalledAction;
			}
			set
			{
				apiCalledAction = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的对象，当前的运行，需要使用管理员的模式运行<br />
		/// Instantiate a default object, the current operation, you need to use the administrator mode to run
		/// </summary>
		public HttpServer()
		{
			statisticsDict = new LogStatisticsDict(GenerateMode.ByEveryDay, 60);
			apiTopicServiceDict = new Dictionary<string, MqttRpcApiInfo>();
			rpcApiLock = new object();
		}

		/// <summary>
		/// 使用HTTPS模式，关于如何使用证书，具体的教程可以参考：http://www.hsltechnology.cn/Doc/HslCommunication?chapter=HslCommChapter6-5<br />
		/// For more information about how to use the certificate in the HTTPS mode, please refer to http://www.hsltechnology.cn/Doc/HslCommunication?chapter=HslCommChapter6-5
		/// </summary>
		public void UseHttps()
		{
			useHttps = true;
		}

		/// <summary>
		/// 启动服务器，正常调用该方法时，应该使用try...catch...来捕获错误信息<br />
		/// Start the server and use try...catch... to capture the error message when calling this method normally
		/// </summary>
		/// <param name="port">端口号信息</param>
		/// <exception cref="T:System.Net.HttpListenerException"></exception>
		/// <exception cref="T:System.ObjectDisposedException"></exception>
		public void Start(int port)
		{
			this.port = port;
			listener = new HttpListener();
			if (useHttps)
			{
				listener.Prefixes.Add($"https://+:{port}/");
				listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			}
			else
			{
				listener.Prefixes.Add($"http://+:{port}/");
			}
			listener.Start();
			listener.BeginGetContext(GetConnectCallBack, listener);
			logNet?.WriteDebug(ToString(), "Server Started, wait for connections");
		}

		/// <summary>
		/// 关闭服务器<br />
		/// Shut down the server
		/// </summary>
		public void Close()
		{
			listener?.Close();
		}

		private async void GetConnectCallBack(IAsyncResult ar)
		{
			object asyncState = ar.AsyncState;
			HttpListener listener = asyncState as HttpListener;
			if (listener == null)
			{
				return;
			}
			HttpListenerContext context = null;
			try
			{
				context = listener.EndGetContext(ar);
			}
			catch (Exception ex5)
			{
				Exception ex2 = ex5;
				logNet?.WriteException(ToString(), ex2);
			}
			int restartcount = 0;
			while (true)
			{
				try
				{
					listener.BeginGetContext(GetConnectCallBack, listener);
				}
				catch (Exception ex5)
				{
					Exception ex4 = ex5;
					logNet?.WriteException(ToString(), ex4);
					restartcount++;
					if (restartcount >= 3)
					{
						logNet?.WriteError(ToString(), "ReGet Content Failed!");
						return;
					}
					HslHelper.ThreadSleep(1000);
					continue;
				}
				break;
			}
			if (context == null)
			{
				return;
			}
			HttpListenerRequest request = context.Request;
			HttpListenerResponse response = context.Response;
			if (response != null)
			{
				try
				{
					if (IsCrossDomain)
					{
						context.Response.AppendHeader("Access-Control-Allow-Origin", request.Headers["Origin"]);
						context.Response.AppendHeader("Access-Control-Allow-Headers", "*");
						context.Response.AppendHeader("Access-Control-Allow-Method", "POST,GET,PUT,OPTIONS,DELETE");
						context.Response.AppendHeader("Access-Control-Allow-Credentials", "true");
						context.Response.AppendHeader("Access-Control-Max-Age", "3600");
					}
					context.Response.AddHeader("Content-type", "text/html; charset=utf-8");
				}
				catch (Exception ex5)
				{
					Exception ex3 = ex5;
					logNet?.WriteError(ToString(), ex3.Message);
				}
			}
			byte[] data = await GetDataFromRequestAsync(request);
			response.StatusCode = 200;
			try
			{
				object ret = await HandleRequest(request, response, data);
				if (ret == null)
				{
					return;
				}
				using Stream stream = response.OutputStream;
				string ret_str = ret as string;
				if (ret_str != null)
				{
					if (string.IsNullOrEmpty(ret_str))
					{
						await stream.WriteAsync(new byte[0], 0, 0);
						return;
					}
					byte[] buffer = encoding.GetBytes(ret_str);
					await stream.WriteAsync(buffer, 0, buffer.Length);
					return;
				}
				byte[] ret_bytes = ret as byte[];
				if (ret_bytes != null)
				{
					response.ContentLength64 = ret_bytes.Length;
					await stream.WriteAsync(ret_bytes, 0, ret_bytes.Length);
				}
			}
			catch (Exception ex)
			{
				logNet?.WriteException(ToString(), "Handle Request[" + request.HttpMethod + "], " + request.RawUrl, ex);
			}
		}

		private byte[] GetDataFromRequest(HttpListenerRequest request)
		{
			try
			{
				MemoryStream memoryStream = new MemoryStream();
				byte[] array = new byte[receiveBufferSize];
				int num = 0;
				do
				{
					num = request.InputStream.Read(array, 0, array.Length);
					if (num > 0)
					{
						memoryStream.Write(array, 0, num);
					}
				}
				while (num != 0);
				return memoryStream.ToArray();
			}
			catch
			{
				return new byte[0];
			}
		}

		private async Task<byte[]> GetDataFromRequestAsync(HttpListenerRequest request)
		{
			try
			{
				MemoryStream ms = new MemoryStream();
				byte[] byteArr = new byte[receiveBufferSize];
				int readLen;
				do
				{
					readLen = await request.InputStream.ReadAsync(byteArr, 0, byteArr.Length);
					if (readLen > 0)
					{
						ms.Write(byteArr, 0, readLen);
					}
				}
				while (readLen != 0);
				return ms.ToArray();
			}
			catch
			{
				return new byte[0];
			}
		}

		/// <summary>
		/// 根据客户端的请求进行处理的核心方法，可以返回自定义的数据内容，只需要集成重写即可。<br />
		/// The core method of processing according to the client's request can return custom data content, and only needs to be integrated and rewritten.
		/// </summary>
		/// <param name="request">请求</param>
		/// <param name="response">回应</param>
		/// <param name="data">Body数据</param>
		/// <returns>返回的内容</returns>
		protected virtual async Task<object> HandleRequest(HttpListenerRequest request, HttpListenerResponse response, byte[] data)
		{
			if (request.HttpMethod == "OPTIONS")
			{
				return "OK";
			}
			if (loginAccess)
			{
				string[] values = request.Headers.GetValues("Authorization");
				if (values == null || values.Length < 1 || string.IsNullOrEmpty(values[0]))
				{
					response.StatusCode = 401;
					response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
					return "";
				}
				try
				{
					string base64String = values[0].Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1];
					string accountString = Encoding.UTF8.GetString(Convert.FromBase64String(base64String));
					string[] account = accountString.Split(new char[1] { ':' }, StringSplitOptions.RemoveEmptyEntries);
					if (account.Length < 2)
					{
						response.StatusCode = 401;
						response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
						return "";
					}
					MqttCredential[] credentials = loginCredentials;
					bool loginEnable = false;
					for (int k = 0; k < credentials.Length; k++)
					{
						if (account[0] == credentials[k].UserName && account[1] == credentials[k].Password)
						{
							loginEnable = true;
							break;
						}
					}
					if (!loginEnable)
					{
						response.StatusCode = 401;
						response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
						return "";
					}
				}
				catch
				{
					response.StatusCode = 401;
					response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
					return "";
				}
			}
			if (request.HttpMethod == "HSL")
			{
				if (request.RawUrl.StartsWith("/Apis"))
				{
					response.AddHeader("Content-type", "application/json; charset=utf-8");
					return GetAllRpcApiInfo().ToJsonString();
				}
				if (request.RawUrl.StartsWith("/Logs"))
				{
					response.AddHeader("Content-type", "application/json; charset=utf-8");
					if (request.RawUrl == "/Logs" || request.RawUrl == "/Logs/")
					{
						return LogStatistics.LogStat.GetStatisticsSnapshot().ToJsonString();
					}
					return LogStatistics.GetStatisticsSnapshot(request.RawUrl.Substring(6)).ToJsonString();
				}
				response.AddHeader("Content-type", "application/json; charset=utf-8");
				return GetAllRpcApiInfo().ToJsonString();
			}
			if (request.HttpMethod == "OPTIONS")
			{
				return "OK";
			}
			MqttRpcApiInfo apiInformation = GetMqttRpcApiInfo(GetMethodName(HttpUtility.UrlDecode(request.RawUrl)));
			if (apiInformation == null)
			{
				if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data; boundary=--------------------------"))
				{
					int index = -1;
					for (int j = 0; j < data.Length - 4; j++)
					{
						if (data[j] == 13 && data[j + 1] == 10 && data[j + 2] == 13 && data[j + 3] == 10)
						{
							index = j + 4;
							break;
						}
					}
					if (index == -1)
					{
						return "Not file content!";
					}
					int last = data.Length - 3;
					for (int i = last; i > 0; i--)
					{
						if (data[i] == 13 && data[i + 1] == 10)
						{
							last = i;
							break;
						}
					}
					if (HandleFileUpload != null)
					{
						string context = encoding.GetString(data, 0, index - 4);
						HttpUploadFile uploadFile = new HttpUploadFile
						{
							FileName = SoftBasic.UrlDecode(Regex.Match(context, "filename=\"[^\"]+").Value.Substring(10), Encoding.UTF8),
							Name = Regex.Match(context, "name=\"[^\"]+").Value.Substring(6),
							Content = data.SelectMiddle(index, last - index)
						};
						return HandleFileUpload(request, response, uploadFile);
					}
				}
				else if (HandleRequestFunc != null)
				{
					return HandleRequestFunc(request, response, encoding.GetString(data));
				}
				return "This is HslWebServer, Thank you for use!";
			}
			response.AddHeader("Content-type", "application/json; charset=utf-8");
			DateTime dateTime = DateTime.Now;
			string url = HttpUtility.UrlDecode(request.RawUrl);
			string body = encoding.GetString(data);
			string result = await HandleObjectMethod(request, url, body, apiInformation, DealWithHttpListenerRequest);
			double timeSpend = Math.Round((DateTime.Now - dateTime).TotalSeconds, 5);
			apiInformation.CalledCountAddOne((long)(timeSpend * 100000.0));
			statisticsDict.StatisticsAdd(apiInformation.ApiTopic, 1L);
			if (apiCalledAction != null)
			{
				HttpApiCalledInfo httpApiCalledInfo = new HttpApiCalledInfo
				{
					HttpMethod = request.HttpMethod,
					Url = url,
					Body = body,
					Result = result,
					CostTime = timeSpend * 1000.0,
					CalledCount = apiInformation.CalledCount
				};
				apiCalledAction?.Invoke(httpApiCalledInfo);
			}
			LogNet?.WriteDebug(ToString(), $"[{request.RemoteEndPoint}] HttpRpc request:[{apiInformation.ApiTopic}] Spend:[{timeSpend * 1000.0:F2} ms] Count:[{apiInformation.CalledCount}]");
			return result;
		}

		private MqttRpcApiInfo GetMqttRpcApiInfo(string apiTopic)
		{
			MqttRpcApiInfo result = null;
			lock (rpcApiLock)
			{
				if (apiTopicServiceDict.ContainsKey(apiTopic))
				{
					result = apiTopicServiceDict[apiTopic];
				}
			}
			return result;
		}

		private void MqttRpcAdd(string apiTopic, MqttRpcApiInfo apiInfo)
		{
			if (apiTopicServiceDict.ContainsKey(apiTopic))
			{
				apiTopicServiceDict[apiTopic] = apiInfo;
			}
			else
			{
				apiTopicServiceDict.Add(apiTopic, apiInfo);
			}
		}

		private void MqttRpcRemove(string apiTopic)
		{
			if (apiTopicServiceDict.ContainsKey(apiTopic))
			{
				apiTopicServiceDict.Remove(apiTopic);
			}
		}

		/// <summary>
		/// 获取当前所有注册的RPC接口信息，将返回一个数据列表。<br />
		/// Get all currently registered RPC interface information, and a data list will be returned.
		/// </summary>
		/// <returns>信息列表</returns>
		public MqttRpcApiInfo[] GetAllRpcApiInfo()
		{
			MqttRpcApiInfo[] result = null;
			lock (rpcApiLock)
			{
				result = apiTopicServiceDict.Values.ToArray();
			}
			return result;
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttServer.RegisterMqttRpcApi(System.String,System.Object)" />
		public void RegisterHttpRpcApi(string api, object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(api, obj))
				{
					MqttRpcAdd(item.ApiTopic, item);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.HttpServer.RegisterHttpRpcApi(System.String,System.Object)" />
		public void RegisterHttpRpcApi(object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(obj))
				{
					MqttRpcAdd(item.ApiTopic, item);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttServer.UnRegisterMqttRpcApi(System.String,System.Object)" />
		public void UnRegisterHttpRpcApi(string api, object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(api, obj))
				{
					MqttRpcRemove(item.ApiTopic);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttServer.UnRegisterMqttRpcApi(System.Object)" />
		public void UnRegisterHttpRpcApi(object obj)
		{
			lock (rpcApiLock)
			{
				foreach (MqttRpcApiInfo item in MqttHelper.GetSyncServicesApiInformationFromObject(obj))
				{
					MqttRpcRemove(item.ApiTopic);
				}
			}
		}

		/// <summary>
		/// 设置登录的账户信息，如果需要自己控制，可以自己实现委托<see cref="P:HslCommunication.Enthernet.HttpServer.HandleRequestFunc" /><br />
		/// Set the login account information, if you need to control by yourself, you can implement the delegation by yourself<see cref="P:HslCommunication.Enthernet.HttpServer.HandleRequestFunc" />
		/// </summary>
		/// <param name="credentials">用户名的列表信息</param>
		public void SetLoginAccessControl(MqttCredential[] credentials)
		{
			if (credentials == null)
			{
				loginAccess = false;
				return;
			}
			if (credentials.Length == 0)
			{
				loginAccess = false;
				return;
			}
			loginAccess = true;
			loginCredentials = credentials;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"HttpServer[{port}]";
		}

		/// <summary>
		/// 使用指定的对象来返回网络的API接口，前提是传入的数据为json参数，返回的数据为json数据，详细参照说明<br />
		/// Use the specified object to return the API interface of the network, 
		/// provided that the incoming data is json parameters and the returned data is json data, 
		/// please refer to the description for details
		/// </summary>
		/// <param name="request">当前的请求信息</param>
		/// <param name="deceodeUrl">已经解码过的Url地址信息</param>
		/// <param name="json">json格式的参数信息</param>
		/// <param name="obj">等待解析的api解析的对象</param>
		/// <param name="action">额外的解析Request参数的方法</param>
		/// <returns>等待返回客户的结果</returns>
		public static async Task<string> HandleObjectMethod(HttpListenerRequest request, string deceodeUrl, string json, object obj, Action<HttpListenerRequest, ISessionContext> action)
		{
			string method = GetMethodName(deceodeUrl);
			if (method.LastIndexOf('/') >= 0)
			{
				method = method.Substring(method.LastIndexOf('/') + 1);
			}
			MethodInfo methodInfo = obj.GetType().GetMethod(method);
			if (methodInfo == null)
			{
				return new OperateResult<string>("Current MqttSync Api ：[" + method + "] not exsist").ToJsonString();
			}
			OperateResult<MqttRpcApiInfo> apiResult = MqttHelper.GetMqttSyncServicesApiFromMethod("", methodInfo, obj);
			if (!apiResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(apiResult).ToJsonString();
			}
			return await HandleObjectMethod(request, deceodeUrl, json, apiResult.Content, action);
		}

		/// <summary>
		/// 根据完整的地址获取当前的url地址信息
		/// </summary>
		/// <param name="url">地址信息</param>
		/// <returns>方法名称</returns>
		public static string GetMethodName(string url)
		{
			string empty = string.Empty;
			empty = ((url.IndexOf('?') <= 0) ? url : url.Substring(0, url.IndexOf('?')));
			if (empty.EndsWith("/") || empty.StartsWith("/"))
			{
				empty = empty.Trim('/');
			}
			return empty;
		}

		private static ISessionContext GetSessionContextFromHeaders(HttpListenerRequest request, Action<HttpListenerRequest, ISessionContext> userParse)
		{
			try
			{
				string[] values = request.Headers.GetValues("Authorization");
				if (values == null || values.Length < 1 || string.IsNullOrEmpty(values[0]))
				{
					return null;
				}
				string s = values[0].Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1];
				string @string = Encoding.UTF8.GetString(Convert.FromBase64String(s));
				string[] array = @string.Split(new char[1] { ':' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length < 1)
				{
					return null;
				}
				SessionContext sessionContext = new SessionContext
				{
					UserName = array[0]
				};
				userParse?.Invoke(request, sessionContext);
				return sessionContext;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// 使用指定的对象来返回网络的API接口，前提是传入的数据为json参数，返回的数据为json数据，详细参照说明<br />
		/// Use the specified object to return the API interface of the network, 
		/// provided that the incoming data is json parameters and the returned data is json data, 
		/// please refer to the description for details
		/// </summary>
		/// <param name="request">当前的请求信息</param>
		/// <param name="deceodeUrl">已经解码过的Url地址信息</param>
		/// <param name="json">json格式的参数信息</param>
		/// <param name="apiInformation">等待解析的api解析的对象</param>
		/// <param name="action">额外的解析Request参数的方法</param>
		/// <returns>等待返回客户的结果</returns>
		public static async Task<string> HandleObjectMethod(HttpListenerRequest request, string deceodeUrl, string json, MqttRpcApiInfo apiInformation, Action<HttpListenerRequest, ISessionContext> action)
		{
			ISessionContext context = GetSessionContextFromHeaders(request, action);
			if (apiInformation.PermissionAttribute != null)
			{
				
				try
				{
					string[] values = request.Headers.GetValues("Authorization");
					if (values == null || values.Length < 1 || string.IsNullOrEmpty(values[0]))
					{
						return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] has none Authorization information, access not permission").ToJsonString();
					}
					string base64String = values[0].Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1];
					string accountString = Encoding.UTF8.GetString(Convert.FromBase64String(base64String));
					string[] account = accountString.Split(new char[1] { ':' }, StringSplitOptions.RemoveEmptyEntries);
					if (account.Length < 1)
					{
						return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] has none Username information, access not permission").ToJsonString();
					}
					if (!apiInformation.PermissionAttribute.CheckUserName(account[0]))
					{
						return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] Check Username[" + account[0] + "] failed, access not permission").ToJsonString();
					}
				}
				catch (Exception ex2)
				{
					return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] Check Username failed, access not permission, reason:" + ex2.Message).ToJsonString();
				}
			}
			try
			{
				if (apiInformation.Method != null)
				{
					MethodInfo methodInfo = apiInformation.Method;
					string apiName2 = apiInformation.ApiTopic;
					if (request.HttpMethod != apiInformation.HttpMethod)
					{
						return new OperateResult("Current Api ：" + apiName2 + " not support diffrent httpMethod").ToJsonString();
					}
					object obj = methodInfo.Invoke(parameters: (!(request.HttpMethod == "GET")) ? HslReflectionHelper.GetParametersFromJson(context, request, methodInfo.GetParameters(), json) : ((deceodeUrl.IndexOf('?') <= 0) ? HslReflectionHelper.GetParametersFromJson(context, request, methodInfo.GetParameters(), json) : HslReflectionHelper.GetParametersFromUrl(context, request, methodInfo.GetParameters(), deceodeUrl)), obj: apiInformation.SourceObject);
					Task task = obj as Task;
					if (task != null)
					{
						await task;
						return task.GetType().GetProperty("Result").GetValue(task, null)
							.ToJsonString();
					}
					return obj.ToJsonString();
				}
				if (apiInformation.Property != null)
				{
					string apiName = apiInformation.ApiTopic;
					if (request.HttpMethod != apiInformation.HttpMethod)
					{
						return new OperateResult("Current Api ：" + apiName + " not support diffrent httpMethod").ToJsonString();
					}
					if (request.HttpMethod != "GET")
					{
						return new OperateResult("Current Api ：" + apiName + " not support POST").ToJsonString();
					}
					return apiInformation.Property.GetValue(apiInformation.SourceObject, null).ToJsonString();
				}
				return new OperateResult("Current Api ：" + deceodeUrl + " not supported").ToJsonString();
			}
			catch (Exception ex)
			{
				return new OperateResult("Current Api ：" + deceodeUrl + " Wrong，Reason：" + ex.Message).ToJsonString();
			}
		}
	}
}
