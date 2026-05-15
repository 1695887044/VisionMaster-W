using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.Robot.ABB
{
	/// <summary>
	/// ABB机器人的web api接口的客户端，可以方便快速的获取到abb机器人的一些数据信息<br />
	/// The client of ABB robot's web API interface can easily and quickly obtain some data information of ABB robot
	/// </summary>
	/// <remarks>
	/// 参考的界面信息是：http://developercenter.robotstudio.com/webservice/api_reference
	///
	/// 关于额外的地址说明，如果想要查看，可以调用<see cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetSelectStrings" /> 返回字符串列表来看看。
	/// </remarks>
	public class ABBWebApiClient : NetworkWebApiRobotBase, IRobotNet
	{
		/// <summary>
		/// 使用指定的ip地址来初始化对象<br />
		/// Initializes the object using the specified IP address
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		public ABBWebApiClient(string ipAddress)
			: base(ipAddress)
		{
		}

		/// <summary>
		/// 使用指定的ip地址和端口号来初始化对象<br />
		/// Initializes the object with the specified IP address and port number
		/// </summary>
		/// <param name="ipAddress">Ip地址信息</param>
		/// <param name="port">端口号信息</param>
		public ABBWebApiClient(string ipAddress, int port)
			: base(ipAddress, port)
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
		public ABBWebApiClient(string ipAddress, int port, string name, string password)
			: base(ipAddress, port, name, password)
		{
		}

		/// <inheritdoc />
		[HslMqttApi(ApiTopic = "ReadRobotByte", Description = "Read the other side of the data information, usually designed for the GET method information.If you start with url=, you are using native address access")]
		public override OperateResult<byte[]> Read(string address)
		{
			return base.Read(address);
		}

		/// <inheritdoc />
		[HslMqttApi(ApiTopic = "ReadRobotString", Description = "The string data information that reads the other party information, usually designed for the GET method information.If you start with url=, you are using native address access")]
		public override OperateResult<string> ReadString(string address)
		{
			return base.ReadString(address);
		}

		/// <inheritdoc />
		[HslMqttApi(ApiTopic = "WriteRobotByte", Description = "Using POST to request data information from the other party, we need to start with url= to indicate that we are using native address access")]
		public override OperateResult Write(string address, byte[] value)
		{
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi(ApiTopic = "WriteRobotString", Description = "Using POST to request data information from the other party, we need to start with url= to indicate that we are using native address access")]
		public override OperateResult Write(string address, string value)
		{
			return base.Write(address, value);
		}

		/// <inheritdoc />
		protected override OperateResult<string> ReadByAddress(string address)
		{
			if (address.ToUpper() == "ErrorState".ToUpper())
			{
				return GetErrorState();
			}
			if (address.ToUpper() == "jointtarget".ToUpper())
			{
				return GetJointTarget();
			}
			if (address.ToUpper() == "PhysicalJoints".ToUpper())
			{
				return GetJointTarget();
			}
			if (address.ToUpper() == "SpeedRatio".ToUpper())
			{
				return GetSpeedRatio();
			}
			if (address.ToUpper() == "OperationMode".ToUpper())
			{
				return GetOperationMode();
			}
			if (address.ToUpper() == "CtrlState".ToUpper())
			{
				return GetCtrlState();
			}
			if (address.ToUpper() == "ioin".ToUpper())
			{
				return GetIOIn();
			}
			if (address.ToUpper() == "ioout".ToUpper())
			{
				return GetIOOut();
			}
			if (address.ToUpper() == "io2in".ToUpper())
			{
				return GetIO2In();
			}
			if (address.ToUpper() == "io2out".ToUpper())
			{
				return GetIO2Out();
			}
			if (address.ToUpper().StartsWith("log".ToUpper()))
			{
				if (address.Length > 3 && int.TryParse(address.Substring(3), out var result))
				{
					return GetLog(result);
				}
				return GetLog();
			}
			if (address.ToUpper() == "system".ToUpper())
			{
				return GetSystem();
			}
			if (address.ToUpper() == "robtarget".ToUpper())
			{
				return GetRobotTarget();
			}
			if (address.ToUpper() == "ServoEnable".ToUpper())
			{
				return GetServoEnable();
			}
			if (address.ToUpper() == "RapidExecution".ToUpper())
			{
				return GetRapidExecution();
			}
			if (address.ToUpper() == "RapidTasks".ToUpper())
			{
				return GetRapidTasks();
			}
			return base.ReadByAddress(address);
		}

		/// <inheritdoc />
		protected override async Task<OperateResult<string>> ReadByAddressAsync(string address)
		{
			if (address.ToUpper() == "ErrorState".ToUpper())
			{
				return await GetErrorStateAsync();
			}
			if (address.ToUpper() == "jointtarget".ToUpper())
			{
				return await GetJointTargetAsync();
			}
			if (address.ToUpper() == "PhysicalJoints".ToUpper())
			{
				return await GetJointTargetAsync();
			}
			if (address.ToUpper() == "SpeedRatio".ToUpper())
			{
				return await GetSpeedRatioAsync();
			}
			if (address.ToUpper() == "OperationMode".ToUpper())
			{
				return await GetOperationModeAsync();
			}
			if (address.ToUpper() == "CtrlState".ToUpper())
			{
				return await GetCtrlStateAsync();
			}
			if (address.ToUpper() == "ioin".ToUpper())
			{
				return await GetIOInAsync();
			}
			if (address.ToUpper() == "ioout".ToUpper())
			{
				return await GetIOOutAsync();
			}
			if (address.ToUpper() == "io2in".ToUpper())
			{
				return await GetIO2InAsync();
			}
			if (address.ToUpper() == "io2out".ToUpper())
			{
				return await GetIO2OutAsync();
			}
			if (address.ToUpper().StartsWith("log".ToUpper()))
			{
				if (address.Length > 3 && int.TryParse(address.Substring(3), out var length))
				{
					return await GetLogAsync(length);
				}
				return await GetLogAsync();
			}
			if (address.ToUpper() == "system".ToUpper())
			{
				return await GetSystemAsync();
			}
			if (address.ToUpper() == "robtarget".ToUpper())
			{
				return await GetRobotTargetAsync();
			}
			if (address.ToUpper() == "ServoEnable".ToUpper())
			{
				return await GetServoEnableAsync();
			}
			if (address.ToUpper() == "RapidExecution".ToUpper())
			{
				return await GetRapidExecutionAsync();
			}
			if (address.ToUpper() == "RapidTasks".ToUpper())
			{
				return await GetRapidTasksAsync();
			}
			return await base.ReadByAddressAsync(address);
		}

		/// <summary>
		/// 获取当前支持的读取的地址列表<br />
		/// Gets a list of addresses for currently supported reads
		/// </summary>
		/// <returns>数组信息</returns>
		public static List<string> GetSelectStrings()
		{
			return new List<string>
			{
				"ErrorState", "jointtarget", "PhysicalJoints", "SpeedRatio", "OperationMode", "CtrlState", "ioin", "ioout", "io2in", "io2out",
				"log", "system", "robtarget", "ServoEnable", "RapidExecution", "RapidTasks"
			};
		}

		private OperateResult<string> ParseSpanByClass(string content, string className)
		{
			Match match = Regex.Match(content, "<span class=\"" + className + "\">[^<]+");
			if (!match.Success)
			{
				return new OperateResult<string>("Parse None class [" + className + "] Span\r\n" + content);
			}
			return OperateResult.CreateSuccessResult(match.Value.Substring(match.Value.IndexOf('>') + 1));
		}

		private OperateResult<double[]> ParseDoubleListSpanByClass(string content, string className)
		{
			MatchCollection matchCollection = Regex.Matches(content, "<span class=\"" + className + "\">[^<]+");
			double[] array = new double[matchCollection.Count];
			for (int i = 0; i < matchCollection.Count; i++)
			{
				array[i] = Convert.ToDouble(matchCollection[i].Value.Substring(matchCollection[i].Value.IndexOf('>') + 1));
			}
			return OperateResult.CreateSuccessResult(array);
		}

		private OperateResult<string> ParseListSpanByClass<T>(string content, string className, Func<string, T> trans)
		{
			MatchCollection matchCollection = Regex.Matches(content, "<span class=\"" + className + "\">[^<]+");
			JArray jArray = new JArray();
			for (int i = 0; i < matchCollection.Count; i++)
			{
				jArray.Add(trans(matchCollection[i].Value.Substring(matchCollection[i].Value.IndexOf('>') + 1)));
			}
			return OperateResult.CreateSuccessResult(jArray.ToString());
		}

		private JObject ParseListByClass(string content)
		{
			XElement xElement = XElement.Parse(content);
			JObject jObject = new JObject();
			foreach (XElement item in xElement.Elements("span"))
			{
				jObject.Add(item.Attribute("class").Value, (JToken)item.Value);
			}
			return jObject;
		}

		private OperateResult<string> ParseJObjectByClass(string content, string className)
		{
			Match match = Regex.Match(content, "<li class=\"" + className + "\"[\\S\\s]+?</li>");
			if (!match.Success)
			{
				return new OperateResult<string>("Parse None class [" + className + "] List\r\n" + content);
			}
			return OperateResult.CreateSuccessResult(ParseListByClass(match.Value).ToString());
		}

		private OperateResult<string> ParseJArrayByClass(string content, string className, int maxCount = int.MaxValue)
		{
			MatchCollection matchCollection = Regex.Matches(content, "<li class=\"" + className + "\"[\\S\\s]+?</li>");
			JArray jArray = new JArray();
			for (int i = 0; i < matchCollection.Count && i < maxCount; i++)
			{
				jArray.Add(ParseListByClass(matchCollection[i].Value));
			}
			return OperateResult.CreateSuccessResult(jArray.ToString());
		}

		/// <summary>
		/// 获取当前的控制状态，Content属性就是机器人的控制信息<br />
		/// Get the current control state. The Content attribute is the control information of the robot
		/// </summary>
		/// <returns>带有状态信息的结果类对象</returns>
		[HslMqttApi(Description = "Get the current control state. The Content attribute is the control information of the robot")]
		public OperateResult<string> GetCtrlState()
		{
			return ReadString("url=/rw/panel/ctrlstate").Then((string m) => ParseSpanByClass(m, "ctrlstate"));
		}

		/// <summary>
		/// 获取当前的错误状态，Content属性就是机器人的状态信息<br />
		/// Gets the current error state. The Content attribute is the state information of the robot
		/// </summary>
		/// <returns>带有状态信息的结果类对象</returns>
		[HslMqttApi(Description = "Gets the current error state. The Content attribute is the state information of the robot")]
		public OperateResult<string> GetErrorState()
		{
			return ReadString("url=/rw/motionsystem/errorstate").Then((string m) => ParseSpanByClass(m, "err-state"));
		}

		/// <summary>
		/// 获取当前机器人的物理关节点信息，返回json格式的关节信息<br />
		/// Get the physical node information of the current robot and return the joint information in json format
		/// </summary>
		/// <param name="mechunit">操作单元，默认为 ROB_1</param>
		/// <returns>带有关节信息的结果类对象</returns>
		[HslMqttApi(Description = "Get the physical node information of the current robot and return the joint information in json format")]
		public OperateResult<string> GetJointTarget(string mechunit = "ROB_1")
		{
			return ReadString("url=/rw/motionsystem/mechunits/" + mechunit + "/jointtarget").Then((string m) => ParseListSpanByClass(m, "((rax_[0-9]+)|(eax_[a-z]))", (string n) => Convert.ToDouble(n)));
		}

		/// <summary>
		/// 获取当前机器人的速度配比信息<br />
		/// Get the speed matching information of the current robot
		/// </summary>
		/// <returns>带有速度信息的结果类对象</returns>
		[HslMqttApi(Description = "Get the speed matching information of the current robot")]
		public OperateResult<string> GetSpeedRatio()
		{
			return ReadString("url=/rw/panel/speedratio").Then((string m) => ParseSpanByClass(m, "speedratio"));
		}

		/// <summary>
		/// 获取当前机器人的工作模式<br />
		/// Gets the current working mode of the robot
		/// </summary>
		/// <returns>带有工作模式信息的结果类对象</returns>
		[HslMqttApi(Description = "Gets the current working mode of the robot")]
		public OperateResult<string> GetOperationMode()
		{
			return ReadString("url=/rw/panel/opmode").Then((string m) => ParseSpanByClass(m, "opmode"));
		}

		/// <summary>
		/// 获取当前机器人的本机的输入IO<br />
		/// Gets the input IO of the current robot's native
		/// </summary>
		/// <returns>带有IO信息的结果类对象</returns>
		[HslMqttApi(Description = "Gets the input IO of the current robot's native")]
		public OperateResult<string> GetIOIn()
		{
			return ReadString("url=/rw/iosystem/devices/D652_10").Then((string m) => ParseSpanByClass(m, "indata"));
		}

		/// <summary>
		/// 获取当前机器人的本机的输出IO<br />
		/// Gets the output IO of the current robot's native
		/// </summary>
		/// <returns>带有IO信息的结果类对象</returns>
		[HslMqttApi(Description = "Gets the output IO of the current robot's native")]
		public OperateResult<string> GetIOOut()
		{
			return ReadString("url=/rw/iosystem/devices/D652_10").Then((string m) => ParseSpanByClass(m, "outdata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOIn" />
		[HslMqttApi(Description = "Gets the input IO2 of the current robot's native")]
		public OperateResult<string> GetIO2In()
		{
			return ReadString("url=/rw/iosystem/devices/BK5250").Then((string m) => ParseSpanByClass(m, "indata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOOut" />
		[HslMqttApi(Description = "Gets the output IO2 of the current robot's native")]
		public OperateResult<string> GetIO2Out()
		{
			return ReadString("url=/rw/iosystem/devices/BK5250").Then((string m) => ParseSpanByClass(m, "outdata"));
		}

		/// <summary>
		/// 获取当前机器人的日志记录，默认记录为10条<br />
		/// Gets the log record for the current robot, which is 10 by default
		/// </summary>
		/// <param name="logCount">读取的最大的日志总数</param>
		/// <returns>带有IO信息的结果类对象</returns>
		[HslMqttApi(Description = "Gets the log record for the current robot, which is 10 by default")]
		public OperateResult<string> GetLog(int logCount = 10)
		{
			return ReadString("url=/rw/elog/0?lang=zh&amp;resource=title").Then((string m) => ParseJArrayByClass(m, "elog-message-li", logCount));
		}

		/// <summary>
		/// 获取当前机器人的系统信息，版本号，唯一ID等信息<br />
		/// Get the current robot's system information, version number, unique ID and other information
		/// </summary>
		/// <returns>系统的基本信息</returns>
		[HslMqttApi(Description = "Get the current robot's system information, version number, unique ID and other information")]
		public OperateResult<string> GetSystem()
		{
			return ReadString("url=/rw/system").Then((string m) => ParseJObjectByClass(m, "sys-system-li"));
		}

		/// <summary>
		/// 获取机器人的目标坐标信息<br />
		/// Get the current robot's target information
		/// </summary>
		/// <returns>系统的基本信息</returns>
		[HslMqttApi(Description = "Get the current robot's target information")]
		public OperateResult<string> GetRobotTarget()
		{
			return ReadString("url=/rw/motionsystem/mechunits/ROB_1/robtarget").Then((string m) => ParseJObjectByClass(m, "ms-robtargets"));
		}

		/// <summary>
		/// 获取当前机器人的伺服使能状态<br />
		/// Get the current robot servo enable state
		/// </summary>
		/// <returns>机器人的伺服使能状态</returns>
		[HslMqttApi(Description = "Get the current robot servo enable state")]
		public OperateResult<string> GetServoEnable()
		{
			return ReadString("url=/rw/iosystem/signals/Local/DRV_1/DRV1K1").Then((string m) => ParseJObjectByClass(m, "ios-signal"));
		}

		/// <summary>
		/// 获取当前机器人的当前程序运行状态<br />
		/// Get the current program running status of the current robot
		/// </summary>
		/// <returns>机器人的当前的程序运行状态</returns>
		[HslMqttApi(Description = "Get the current program running status of the current robot")]
		public OperateResult<string> GetRapidExecution()
		{
			return ReadString("url=/rw/rapid/execution").Then((string m) => ParseJObjectByClass(m, "rap-execution"));
		}

		/// <summary>
		/// 获取当前机器人的任务列表<br />
		/// Get the task list of the current robot
		/// </summary>
		/// <returns>任务信息的列表</returns>
		[HslMqttApi(Description = "Get the task list of the current robot")]
		public OperateResult<string> GetRapidTasks()
		{
			return ReadString("url=/rw/rapid/tasks").Then((string m) => ParseJArrayByClass(m, "rap-task-li"));
		}

		/// <summary>
		/// 根据给定的名称，获取当前用户的数据值信息。<br />
		/// According to the given name, gets the data value information of the current user
		/// </summary>
		/// <param name="name">数据名称信息，例如 nCurProgIndex</param>
		/// <returns>数值信息</returns>
		public OperateResult<double[]> GetUserValue(string name)
		{
			return ReadString(name.StartsWith("url=", StringComparison.OrdinalIgnoreCase) ? name : ("url=/rw/rapid/symbol/data/RAPID/T_ROB1/user/" + name)).Then((string m) => ParseDoubleListSpanByClass(m, "value"));
		}

		/// <summary>
		/// 获取机器人的IO信号资源<br />
		/// Get an IO signal resource.
		/// </summary>
		/// <returns>系统的基本信息</returns>
		[HslMqttApi(Description = "Get the current robot's target information")]
		public OperateResult<string> GetAnIOSignal(string network = "Local", string unit = "DRV_1", string signal = "DRV1K1")
		{
			return ReadString("url=/rw/iosystem/signals/" + network + "/" + unit + "/" + signal).Then((string m) => ParseJObjectByClass(m, "ios-signal"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetCtrlState" />
		public async Task<OperateResult<string>> GetCtrlStateAsync()
		{
			return (await ReadStringAsync("url=/rw/panel/ctrlstate").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "ctrlstate"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetErrorState" />
		public async Task<OperateResult<string>> GetErrorStateAsync()
		{
			return (await ReadStringAsync("url=/rw/motionsystem/errorstate").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "err-state"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetJointTarget(System.String)" />
		public async Task<OperateResult<string>> GetJointTargetAsync(string mechunit = "ROB_1")
		{
			return (await ReadStringAsync("url=/rw/motionsystem/mechunits/" + mechunit + "/jointtarget").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseListSpanByClass(m, "((rax_[0-9]+)|(eax_[a-z]))", (string n) => Convert.ToDouble(n)));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetSpeedRatio" />
		public async Task<OperateResult<string>> GetSpeedRatioAsync()
		{
			return (await ReadStringAsync("url=/rw/panel/speedratio").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "speedratio"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetOperationMode" />
		public async Task<OperateResult<string>> GetOperationModeAsync()
		{
			return (await ReadStringAsync("url=/rw/panel/opmode").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "opmode"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOIn" />
		public async Task<OperateResult<string>> GetIOInAsync()
		{
			return (await ReadStringAsync("url=/rw/iosystem/devices/D652_10").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "indata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOOut" />
		public async Task<OperateResult<string>> GetIOOutAsync()
		{
			return (await ReadStringAsync("url=/rw/iosystem/devices/D652_10").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "outdata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOIn" />
		public async Task<OperateResult<string>> GetIO2InAsync()
		{
			return (await ReadStringAsync("url=/rw/iosystem/devices/BK5250").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "indata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetIOOut" />
		public async Task<OperateResult<string>> GetIO2OutAsync()
		{
			return (await ReadStringAsync("url=/rw/iosystem/devices/BK5250").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseSpanByClass(m, "outdata"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetLog(System.Int32)" />
		public async Task<OperateResult<string>> GetLogAsync(int logCount = 10)
		{
			return (await ReadStringAsync("url=/rw/elog/0?lang=zh&amp;resource=title").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJArrayByClass(m, "elog-message-li", logCount));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetSystem" />
		public async Task<OperateResult<string>> GetSystemAsync()
		{
			return (await ReadStringAsync("url=/rw/system").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJObjectByClass(m, "sys-system-li"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetRobotTarget" />
		public async Task<OperateResult<string>> GetRobotTargetAsync()
		{
			return (await ReadStringAsync("url=/rw/motionsystem/mechunits/ROB_1/robtarget").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJObjectByClass(m, "ms-robtargets"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetServoEnable" />
		public async Task<OperateResult<string>> GetServoEnableAsync()
		{
			return (await ReadStringAsync("url=/rw/iosystem/signals/Local/DRV_1/DRV1K1").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJObjectByClass(m, "ios-signal"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetRapidExecution" />
		public async Task<OperateResult<string>> GetRapidExecutionAsync()
		{
			return (await ReadStringAsync("url=/rw/rapid/execution").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJObjectByClass(m, "rap-execution"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetRapidTasks" />
		public async Task<OperateResult<string>> GetRapidTasksAsync()
		{
			return (await ReadStringAsync("url=/rw/rapid/tasks").ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJArrayByClass(m, "rap-task-li"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetUserValue(System.String)" />
		public async Task<OperateResult<double[]>> GetUserValueAsync(string name)
		{
			return (await ReadStringAsync(name.StartsWith("url=", StringComparison.OrdinalIgnoreCase) ? name : ("url=/rw/rapid/symbol/data/RAPID/T_ROB1/user/" + name)).ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseDoubleListSpanByClass(m, "value"));
		}

		/// <inheritdoc cref="M:HslCommunication.Robot.ABB.ABBWebApiClient.GetAnIOSignal(System.String,System.String,System.String)" />
		public async Task<OperateResult<string>> GetAnIOSignalAsync(string network = "Local", string unit = "DRV_1", string signal = "DRV1K1")
		{
			return (await ReadStringAsync("url=/rw/iosystem/signals/" + network + "/" + unit + "/" + signal).ConfigureAwait(continueOnCapturedContext: false)).Then((string m) => ParseJObjectByClass(m, "ios-signal"));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ABBWebApiClient[{base.IpAddress}:{base.Port}]";
		}
	}
}
