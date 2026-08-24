using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Pipe;
using HslCommunication.Core.Security;
using HslCommunication.Reflection;
using Newtonsoft.Json.Linq;

namespace HslCommunication.MQTT
{
	/// <summary>
	/// Mqtt协议的辅助类，提供了一些协议相关的基础方法，方便客户端和服务器端一起调用。<br />
	/// The auxiliary class of the Mqtt protocol provides some protocol-related basic methods for the client and server to call together.
	/// </summary>
	public class MqttHelper
	{
		/// <summary>
		/// 根据数据的总长度，计算出剩余的数据长度信息<br />
		/// According to the total length of the data, calculate the remaining data length information
		/// </summary>
		/// <param name="length">数据的总长度</param>
		/// <returns>计算结果</returns>
		public static OperateResult<byte[]> CalculateLengthToMqttLength(int length)
		{
			if (length > 268435455)
			{
				return new OperateResult<byte[]>(StringResources.Language.MQTTDataTooLong);
			}
			if (length < 128)
			{
				return OperateResult.CreateSuccessResult(new byte[1] { (byte)length });
			}
			if (length < 16384)
			{
				return OperateResult.CreateSuccessResult(new byte[2]
				{
					(byte)(length % 128 + 128),
					(byte)(length / 128)
				});
			}
			if (length < 2097152)
			{
				return OperateResult.CreateSuccessResult(new byte[3]
				{
					(byte)(length % 128 + 128),
					(byte)(length / 128 % 128 + 128),
					(byte)(length / 128 / 128)
				});
			}
			return OperateResult.CreateSuccessResult(new byte[4]
			{
				(byte)(length % 128 + 128),
				(byte)(length / 128 % 128 + 128),
				(byte)(length / 128 / 128 % 128 + 128),
				(byte)(length / 128 / 128 / 128)
			});
		}

		/// <summary>
		/// 将一个数据打包成一个mqtt协议的内容<br />
		/// Pack a piece of data into a mqtt protocol
		/// </summary>
		/// <param name="control">控制码</param>
		/// <param name="flags">标记</param>
		/// <param name="variableHeader">可变头的字节内容</param>
		/// <param name="payLoad">负载数据</param>
		/// <param name="aesCryptography">AES数据加密对象</param>
		/// <returns>带有是否成功的结果对象</returns>
		public static OperateResult<byte[]> BuildMqttCommand(byte control, byte flags, byte[] variableHeader, byte[] payLoad, AesCryptography aesCryptography = null)
		{
			control = (byte)(control << 4);
			byte head = (byte)(control | flags);
			return BuildMqttCommand(head, variableHeader, payLoad, aesCryptography);
		}

		/// <summary>
		/// 将一个数据打包成一个mqtt协议的内容<br />
		/// Pack a piece of data into a mqtt protocol
		/// </summary>
		/// <param name="head">控制码加标记码</param>
		/// <param name="variableHeader">可变头的字节内容</param>
		/// <param name="payLoad">负载数据</param>
		/// <param name="aesCryptography">AES数据加密对象</param>
		/// <returns>带有是否成功的结果对象</returns>
		public static OperateResult<byte[]> BuildMqttCommand(byte head, byte[] variableHeader, byte[] payLoad, AesCryptography aesCryptography = null)
		{
			if (variableHeader == null)
			{
				variableHeader = new byte[0];
			}
			if (payLoad == null)
			{
				payLoad = new byte[0];
			}
			if (aesCryptography != null)
			{
				payLoad = aesCryptography.Encrypt(payLoad);
			}
			OperateResult<byte[]> operateResult = CalculateLengthToMqttLength(variableHeader.Length + payLoad.Length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			MemoryStream memoryStream = new MemoryStream();
			memoryStream.WriteByte(head);
			memoryStream.Write(operateResult.Content, 0, operateResult.Content.Length);
			if (variableHeader.Length != 0)
			{
				memoryStream.Write(variableHeader, 0, variableHeader.Length);
			}
			if (payLoad.Length != 0)
			{
				memoryStream.Write(payLoad, 0, payLoad.Length);
			}
			return OperateResult.CreateSuccessResult(memoryStream.ToArray());
		}

		/// <summary>
		/// 将字符串打包成utf8编码，并且带有2个字节的表示长度的信息<br />
		/// Pack the string into utf8 encoding, and with 2 bytes of length information
		/// </summary>
		/// <param name="message">文本消息</param>
		/// <returns>打包之后的信息</returns>
		public static byte[] BuildSegCommandByString(string message)
		{
			byte[] message2 = (string.IsNullOrEmpty(message) ? new byte[0] : Encoding.UTF8.GetBytes(message));
			return BuildSegCommandByString(message2);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.BuildSegCommandByString(System.String)" />
		public static byte[] BuildSegCommandByString(byte[] message)
		{
			if (message == null)
			{
				message = new byte[0];
			}
			byte[] array = new byte[message.Length + 2];
			message.CopyTo(array, 2);
			array[0] = (byte)(message.Length / 256);
			array[1] = (byte)(message.Length % 256);
			return array;
		}

		/// <summary>
		/// 从MQTT的缓存信息里，提取文本信息<br />
		/// Extract text information from MQTT cache information
		/// </summary>
		/// <param name="buffer">Mqtt的报文</param>
		/// <param name="index">索引</param>
		/// <returns>值</returns>
		public static string ExtraMsgFromBytes(byte[] buffer, ref int index)
		{
			int num = index;
			int num2 = buffer[index] * 256 + buffer[index + 1];
			index = index + 2 + num2;
			return Encoding.UTF8.GetString(buffer, num + 2, num2);
		}

		/// <summary>
		/// 从MQTT的缓存信息里，提取文本信息<br />
		/// Extract text information from MQTT cache information
		/// </summary>
		/// <param name="buffer">Mqtt的报文</param>
		/// <param name="index">索引</param>
		/// <param name="topics">订阅的主题信息</param>
		/// <param name="qosLevels">订阅的QOs信息</param>
		/// <returns>值</returns>
		public static void ExtraSubscribeMsgFromBytes(byte[] buffer, ref int index, List<string> topics, List<byte> qosLevels)
		{
			int num = buffer[index] * 256 + buffer[index + 1];
			topics.Add(Encoding.UTF8.GetString(buffer, index + 2, num));
			if (index + 2 + num < buffer.Length)
			{
				qosLevels.Add(buffer[index + 2 + num]);
			}
			else
			{
				qosLevels.Add(0);
			}
			index = index + 3 + num;
		}

		/// <summary>
		/// 从MQTT的缓存信息里，提取长度信息<br />
		/// Extract length information from MQTT cache information
		/// </summary>
		/// <param name="buffer">Mqtt的报文</param>
		/// <param name="index">索引</param>
		/// <returns>值</returns>
		public static int ExtraIntFromBytes(byte[] buffer, ref int index)
		{
			int result = buffer[index] * 256 + buffer[index + 1];
			index += 2;
			return result;
		}

		/// <summary>
		/// 从MQTT的缓存信息里，提取长度信息<br />
		/// Extract length information from MQTT cache information
		/// </summary>
		/// <param name="data">数据信息</param>
		/// <returns>值</returns>
		public static byte[] BuildIntBytes(int data)
		{
			return new byte[2]
			{
				BitConverter.GetBytes(data)[1],
				BitConverter.GetBytes(data)[0]
			};
		}

		/// <summary>
		/// 创建MQTT连接服务器的报文信息<br />
		/// Create MQTT connection server message information
		/// </summary>
		/// <param name="connectionOptions">连接配置</param>
		/// <param name="protocol">协议的内容</param>
		/// <param name="rsa">数据加密对象</param>
		/// <returns>返回是否成功的信息</returns>
		public static OperateResult<byte[]> BuildConnectMqttCommand(MqttConnectionOptions connectionOptions, string protocol = "MQTT", RSACryptoServiceProvider rsa = null)
		{
			List<byte> list = new List<byte>();
			list.AddRange(new byte[2] { 0, 4 });
			list.AddRange(Encoding.ASCII.GetBytes(protocol));
			list.Add(4);
			byte b = 0;
			if (connectionOptions.WillMessage != null && !string.IsNullOrEmpty(connectionOptions.WillMessage.Topic) && protocol == "MQTT")
			{
				b = (byte)(b | 4u);
			}
			if (connectionOptions.Credentials != null)
			{
				b = (byte)(b | 0x80u);
				b = (byte)(b | 0x40u);
			}
			if (connectionOptions.CleanSession)
			{
				b = (byte)(b | 2u);
			}
			list.Add(b);
			if (connectionOptions.KeepAlivePeriod.TotalSeconds < 1.0)
			{
				connectionOptions.KeepAlivePeriod = TimeSpan.FromSeconds(1.0);
			}
			byte[] bytes = BitConverter.GetBytes((int)connectionOptions.KeepAlivePeriod.TotalSeconds);
			list.Add(bytes[1]);
			list.Add(bytes[0]);
			List<byte> list2 = new List<byte>();
			list2.AddRange(BuildSegCommandByString(connectionOptions.ClientId));
			if (connectionOptions.WillMessage != null && !string.IsNullOrEmpty(connectionOptions.WillMessage.Topic) && protocol == "MQTT")
			{
				list2.AddRange(BuildSegCommandByString(connectionOptions.WillMessage.Topic));
				list2.AddRange(BuildSegCommandByString(connectionOptions.WillMessage.Payload));
			}
			if (connectionOptions.Credentials != null)
			{
				list2.AddRange(BuildSegCommandByString(connectionOptions.Credentials.UserName));
				list2.AddRange(BuildSegCommandByString(connectionOptions.Credentials.Password));
			}
			if (rsa == null)
			{
				return BuildMqttCommand(1, 0, list.ToArray(), list2.ToArray());
			}
			return BuildMqttCommand(1, 0, rsa.EncryptLargeData(list.ToArray()), rsa.EncryptLargeData(list2.ToArray()));
		}

		/// <summary>
		/// 根据服务器返回的信息判断当前的连接是否是可用的<br />
		/// According to the information returned by the server to determine whether the current connection is available
		/// </summary>
		/// <param name="code">功能码</param>
		/// <param name="data">数据内容</param>
		/// <returns>是否可用的连接</returns>
		public static OperateResult CheckConnectBack(byte code, byte[] data)
		{
			if (code >> 4 != 2)
			{
				return new OperateResult("MQTT Connection Back Is Wrong: " + code);
			}
			if (data.Length < 2)
			{
				return new OperateResult("MQTT Connection Data Is Short: " + SoftBasic.ByteToHexString(data, ' '));
			}
			int num = data[1];
			int num2 = data[0];
			if (num > 0)
			{
				return new OperateResult(num, GetMqttCodeText(num));
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 获取当前的错误的描述信息<br />
		/// Get a description of the current error
		/// </summary>
		/// <param name="status">状态信息</param>
		/// <returns>描述信息</returns>
		public static string GetMqttCodeText(int status)
		{
			return status switch
			{
				1 => StringResources.Language.MQTTStatus01, 
				2 => StringResources.Language.MQTTStatus02, 
				3 => StringResources.Language.MQTTStatus03, 
				4 => StringResources.Language.MQTTStatus04, 
				5 => StringResources.Language.MQTTStatus05, 
				_ => StringResources.Language.UnknownError, 
			};
		}

		/// <summary>
		/// 创建Mqtt发送消息的命令<br />
		/// Create Mqtt command to send messages
		/// </summary>
		/// <param name="message">封装后的消息内容</param>
		/// <param name="aesCryptography">AES数据加密对象</param>
		/// <returns>结果内容</returns>
		public static OperateResult<byte[]> BuildPublishMqttCommand(MqttPublishMessage message, AesCryptography aesCryptography = null)
		{
			byte b = 0;
			if (!message.IsSendFirstTime)
			{
				b = (byte)(b | 8u);
			}
			if (message.Message.Retain)
			{
				b = (byte)(b | 1u);
			}
			if (message.Message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtLeastOnce)
			{
				b = (byte)(b | 2u);
			}
			else if (message.Message.QualityOfServiceLevel == MqttQualityOfServiceLevel.ExactlyOnce)
			{
				b = (byte)(b | 4u);
			}
			else if (message.Message.QualityOfServiceLevel == MqttQualityOfServiceLevel.OnlyTransfer)
			{
				b = (byte)(b | 6u);
			}
			List<byte> list = new List<byte>();
			list.AddRange(BuildSegCommandByString(message.Message.Topic));
			if (message.Message.QualityOfServiceLevel != 0)
			{
				list.Add(BitConverter.GetBytes(message.Identifier)[1]);
				list.Add(BitConverter.GetBytes(message.Identifier)[0]);
			}
			return BuildMqttCommand(3, b, list.ToArray(), message.Message.Payload, aesCryptography);
		}

		/// <summary>
		/// 创建Mqtt发送消息的命令<br />
		/// Create Mqtt command to send messages
		/// </summary>
		/// <param name="topic">主题消息内容</param>
		/// <param name="payload">数据负载</param>
		/// <param name="retain">是否消息驻留</param>
		/// <param name="aesCryptography">AES数据加密对象</param>
		/// <returns>结果内容</returns>
		public static OperateResult<byte[]> BuildPublishMqttCommand(string topic, byte[] payload, bool retain = false, AesCryptography aesCryptography = null)
		{
			return BuildMqttCommand(3, (byte)(retain ? 1 : 0), BuildSegCommandByString(topic), payload, aesCryptography);
		}

		/// <summary>
		/// 创建Mqtt订阅消息的命令<br />
		/// Command to create Mqtt subscription message
		/// </summary>
		/// <param name="message">订阅的主题</param>
		/// <returns>结果内容</returns>
		public static OperateResult<byte[]> BuildSubscribeMqttCommand(MqttSubscribeMessage message)
		{
			List<byte> list = new List<byte>();
			List<byte> list2 = new List<byte>();
			list.Add(BitConverter.GetBytes(message.Identifier)[1]);
			list.Add(BitConverter.GetBytes(message.Identifier)[0]);
			for (int i = 0; i < message.Topics.Length; i++)
			{
				list2.AddRange(BuildSegCommandByString(message.Topics[i]));
				if (message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtMostOnce)
				{
					list2.AddRange(new byte[1]);
				}
				else if (message.QualityOfServiceLevel == MqttQualityOfServiceLevel.AtLeastOnce)
				{
					list2.AddRange(new byte[1] { 1 });
				}
				else
				{
					list2.AddRange(new byte[1] { 2 });
				}
			}
			return BuildMqttCommand(8, 2, list.ToArray(), list2.ToArray());
		}

		/// <summary>
		/// 创建Mqtt取消订阅消息的命令<br />
		/// Create Mqtt unsubscribe message command
		/// </summary>
		/// <param name="message">订阅的主题</param>
		/// <returns>结果内容</returns>
		public static OperateResult<byte[]> BuildUnSubscribeMqttCommand(MqttSubscribeMessage message)
		{
			List<byte> list = new List<byte>();
			List<byte> list2 = new List<byte>();
			list.Add(BitConverter.GetBytes(message.Identifier)[1]);
			list.Add(BitConverter.GetBytes(message.Identifier)[0]);
			for (int i = 0; i < message.Topics.Length; i++)
			{
				list2.AddRange(BuildSegCommandByString(message.Topics[i]));
			}
			return BuildMqttCommand(10, 2, list.ToArray(), list2.ToArray());
		}

		internal static int ExtraQosFromMqttCode(byte code)
		{
			return (((code & 4) == 4) ? 2 : 0) + (((code & 2) == 2) ? 1 : 0);
		}

		internal static MqttQualityOfServiceLevel GetFromQos(int qos)
		{
			MqttQualityOfServiceLevel result = MqttQualityOfServiceLevel.AtMostOnce;
			switch (qos)
			{
			case 1:
				result = MqttQualityOfServiceLevel.AtLeastOnce;
				break;
			case 2:
				result = MqttQualityOfServiceLevel.ExactlyOnce;
				break;
			case 3:
				result = MqttQualityOfServiceLevel.OnlyTransfer;
				break;
			}
			return result;
		}

		internal static OperateResult<MqttClientApplicationMessage> ParseMqttClientApplicationMessage(MqttSession session, byte code, byte[] data)
		{
			try
			{
				bool flag = (code & 8) == 8;
				int num = ExtraQosFromMqttCode(code);
				bool retain = (code & 1) == 1;
				int msgID = 0;
				int index = 0;
				string topic = ExtraMsgFromBytes(data, ref index);
				if (num > 0)
				{
					msgID = ExtraIntFromBytes(data, ref index);
				}
				byte[] array = SoftBasic.ArrayRemoveBegin(data, index);
				if (session.IsAesCryptography && array.Length != 0)
				{
					array = session.AesCryptography.Decrypt(array);
				}
				MqttClientApplicationMessage value = new MqttClientApplicationMessage
				{
					ClientId = session.ClientId,
					QualityOfServiceLevel = GetFromQos(num),
					Retain = retain,
					Topic = topic,
					UserName = session.UserName,
					Payload = array,
					MsgID = msgID
				};
				return OperateResult.CreateSuccessResult(value);
			}
			catch (Exception ex)
			{
				return new OperateResult<MqttClientApplicationMessage>("ParseMqttClientApplicationMessage failed: " + ex.Message);
			}
		}

		/// <summary>
		/// 解析从MQTT接受的客户端信息，解析成实际的Topic数据及Payload数据<br />
		/// Parse the client information received from MQTT and parse it into actual Topic data and Payload data
		/// </summary>
		/// <param name="mqttCode">MQTT的命令码</param>
		/// <param name="data">接收的MQTT原始的消息内容</param>
		/// <param name="aesCryptography">AES数据加密信息</param>
		/// <returns>解析的数据结果信息</returns>
		public static OperateResult<string, byte[]> ExtraMqttReceiveData(byte mqttCode, byte[] data, AesCryptography aesCryptography = null)
		{
			if (data.Length < 2)
			{
				return new OperateResult<string, byte[]>(StringResources.Language.ReceiveDataLengthTooShort + data.Length);
			}
			int num = data[0] * 256 + data[1];
			if (data.Length < 2 + num)
			{
				return new OperateResult<string, byte[]>($"Code[{mqttCode:X2}] ExtraMqttReceiveData Error: {SoftBasic.ByteToHexString(data, ' ')}");
			}
			string value = ((num > 0) ? Encoding.UTF8.GetString(data, 2, num) : string.Empty);
			byte[] array = new byte[data.Length - num - 2];
			Array.Copy(data, num + 2, array, 0, array.Length);
			if (aesCryptography != null)
			{
				try
				{
					array = aesCryptography.Decrypt(array);
				}
				catch (Exception ex)
				{
					return new OperateResult<string, byte[]>("AES Decrypt failed: " + ex.Message);
				}
			}
			return OperateResult.CreateSuccessResult(value, array);
		}

		/// <summary>
		/// 使用指定的对象来返回网络的API接口，前提是传入的数据为json参数，返回的数据为json数据，详细参照说明<br />
		/// Use the specified object to return the API interface of the network, 
		/// provided that the incoming data is json parameters and the returned data is json data, 
		/// please refer to the description for details
		/// </summary>
		/// <param name="mqttSession">当前的对话状态</param>
		/// <param name="message">当前传入的消息内容</param>
		/// <param name="obj">等待解析的api解析的对象</param>
		/// <returns>等待返回客户的结果</returns>
		public static async Task<OperateResult<string>> HandleObjectMethod(MqttSession mqttSession, MqttClientApplicationMessage message, object obj)
		{
			string method = message.Topic;
			if (method.LastIndexOf('/') >= 0)
			{
				method = method.Substring(method.LastIndexOf('/') + 1);
			}
			MethodInfo methodInfo = obj.GetType().GetMethod(method);
			if (methodInfo == null)
			{
				return new OperateResult<string>("Current MqttSync Api ：[" + method + "] not exsist");
			}
			OperateResult<MqttRpcApiInfo> apiResult = GetMqttSyncServicesApiFromMethod("", methodInfo, obj);
			if (!apiResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(apiResult);
			}
			return await HandleObjectMethod(mqttSession, message, apiResult.Content);
		}

		/// <summary>
		/// 使用指定的对象来返回网络的API接口，前提是传入的数据为json参数，返回的数据为json数据，详细参照说明<br />
		/// Use the specified object to return the API interface of the network, 
		/// provided that the incoming data is json parameters and the returned data is json data, 
		/// please refer to the description for details
		/// </summary>
		/// <param name="mqttSession">当前的对话状态</param>
		/// <param name="message">当前传入的消息内容</param>
		/// <param name="apiInformation">当前已经解析好的Api内容对象</param>
		/// <returns>等待返回客户的结果</returns>
		public static async Task<OperateResult<string>> HandleObjectMethod(MqttSession mqttSession, MqttClientApplicationMessage message, MqttRpcApiInfo apiInformation)
		{
			object retObject = null;
			if (apiInformation.PermissionAttribute != null)
			{
				
				if (!apiInformation.PermissionAttribute.CheckClientID(mqttSession.ClientId))
				{
					return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] Check ClientID[" + mqttSession.ClientId + "] failed, access not permission");
				}
				if (!apiInformation.PermissionAttribute.CheckUserName(mqttSession.UserName))
				{
					return new OperateResult<string>("Mqtt RPC Api ：[" + apiInformation.ApiTopic + "] Check Username[" + mqttSession.UserName + "] failed, access not permission");
				}
			}
			try
			{
				if (apiInformation.Method != null)
				{
					string json2 = Encoding.UTF8.GetString(message.Payload);
					if (!string.IsNullOrEmpty(json2))
					{
						JObject.Parse(json2);
					}
					else
					{
						new JObject();
					}
					object[] paras = HslReflectionHelper.GetParametersFromJson(mqttSession, null, apiInformation.Method.GetParameters(), json2);
					object obj = apiInformation.Method.Invoke(apiInformation.SourceObject, paras);
					Task task = obj as Task;
					if (task != null)
					{
						await task;
						retObject = task.GetType().GetProperty("Result")?.GetValue(task, null);
					}
					else
					{
						retObject = obj;
					}
				}
				else if (apiInformation.Property != null)
				{
					retObject = apiInformation.Property.GetValue(apiInformation.SourceObject, null);
				}
			}
			catch (TargetInvocationException ex2)
			{
				return new OperateResult<string>("Mqtt RPC Api Call：[" + apiInformation.ApiTopic + "] Wrong，Reason：" + SoftBasic.GetExceptionMessage(ex2.InnerException));
			}
			catch (Exception ex)
			{
				string json = ((message.Payload == null) ? string.Empty : Encoding.UTF8.GetString(message.Payload));
				return new OperateResult<string>("Mqtt RPC Api Parse Json：[" + apiInformation.ApiTopic + "] Wrong，Reason：" + ex.Message + Environment.NewLine + "Json: " + json);
			}
			return HslReflectionHelper.GetOperateResultJsonFromObj(retObject);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.GetSyncServicesApiInformationFromObject(System.String,System.Object,HslCommunication.Reflection.HslMqttPermissionAttribute)" />
		public static List<MqttRpcApiInfo> GetSyncServicesApiInformationFromObject(object obj)
		{
			Type type = obj as Type;
			if ((object)type != null)
			{
				return GetSyncServicesApiInformationFromObject(type.Name, type);
			}
			return GetSyncServicesApiInformationFromObject(obj.GetType().Name, obj);
		}

		/// <summary>
		/// 根据当前的对象定义的方法信息，获取到所有支持ApiTopic的方法列表信息，包含API名称，示例参数数据，描述信息。<br />
		/// According to the method information defined by the current object, the list information of all methods that support ApiTopic is obtained, 
		/// including the API name, sample parameter data, and description information.
		/// </summary>
		/// <param name="api">指定的ApiTopic的前缀，可以理解为控制器，如果为空，就不携带控制器。</param>
		/// <param name="obj">实际的等待解析的对象</param>
		/// <param name="permissionAttribute">默认的权限特性</param>
		/// <returns>返回所有API说明的列表，类型为<see cref="T:HslCommunication.MQTT.MqttRpcApiInfo" /></returns>
		public static List<MqttRpcApiInfo> GetSyncServicesApiInformationFromObject(string api, object obj, HslMqttPermissionAttribute permissionAttribute = null)
		{
			Type type = null;
			Type type2 = obj as Type;
			if ((object)type2 != null)
			{
				type = type2;
				obj = null;
			}
			else
			{
				type = obj.GetType();
			}
			MethodInfo[] methods = type.GetMethods();
			List<MqttRpcApiInfo> list = new List<MqttRpcApiInfo>();
			MethodInfo[] array = methods;
			foreach (MethodInfo method in array)
			{
				OperateResult<MqttRpcApiInfo> mqttSyncServicesApiFromMethod = GetMqttSyncServicesApiFromMethod(api, method, obj, permissionAttribute);
				if (mqttSyncServicesApiFromMethod.IsSuccess)
				{
					list.Add(mqttSyncServicesApiFromMethod.Content);
				}
			}
			PropertyInfo[] properties = type.GetProperties();
			PropertyInfo[] array2 = properties;
			foreach (PropertyInfo propertyInfo in array2)
			{
				OperateResult<HslMqttApiAttribute, MqttRpcApiInfo> mqttSyncServicesApiFromProperty = GetMqttSyncServicesApiFromProperty(api, propertyInfo, obj, permissionAttribute);
				if (mqttSyncServicesApiFromProperty.IsSuccess)
				{
					if (!mqttSyncServicesApiFromProperty.Content1.PropertyUnfold)
					{
						list.Add(mqttSyncServicesApiFromProperty.Content2);
					}
					else if (propertyInfo.GetValue(obj, null) != null)
					{
						List<MqttRpcApiInfo> syncServicesApiInformationFromObject = GetSyncServicesApiInformationFromObject(mqttSyncServicesApiFromProperty.Content2.ApiTopic, propertyInfo.GetValue(obj, null), permissionAttribute);
						list.AddRange(syncServicesApiInformationFromObject);
					}
				}
			}
			return list;
		}

		private static string GetReturnTypeDescription(Type returnType)
		{
			if (returnType.IsSubclassOf(typeof(OperateResult)))
			{
				if (returnType == typeof(OperateResult))
				{
					return returnType.Name;
				}
				if (returnType.GetProperty("Content") != null)
				{
					return "OperateResult<" + returnType.GetProperty("Content").PropertyType.Name + ">";
				}
				StringBuilder stringBuilder = new StringBuilder("OperateResult<");
				for (int i = 1; i <= 10; i++)
				{
					if (!(returnType.GetProperty("Content" + i) != null))
					{
						break;
					}
					if (i != 1)
					{
						stringBuilder.Append(",");
					}
					stringBuilder.Append(returnType.GetProperty("Content" + i).PropertyType.Name);
				}
				stringBuilder.Append(">");
				return stringBuilder.ToString();
			}
			return returnType.Name;
		}

		/// <summary>
		/// 根据当前的方法的委托信息和类对象，生成<see cref="T:HslCommunication.MQTT.MqttRpcApiInfo" />的API对象信息。
		/// </summary>
		/// <param name="api">Api头信息</param>
		/// <param name="method">方法的委托</param>
		/// <param name="obj">当前注册的API的源对象</param>
		/// <param name="permissionAttribute">默认的权限特性</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<MqttRpcApiInfo> GetMqttSyncServicesApiFromMethod(string api, MethodInfo method, object obj, HslMqttPermissionAttribute permissionAttribute = null)
		{
			object[] customAttributes = method.GetCustomAttributes(typeof(HslMqttApiAttribute), inherit: false);
			if (customAttributes == null || customAttributes.Length == 0)
			{
				return new OperateResult<MqttRpcApiInfo>($"Current Api ：[{method}] not support Api attribute");
			}
			HslMqttApiAttribute hslMqttApiAttribute = (HslMqttApiAttribute)customAttributes[0];
			MqttRpcApiInfo mqttRpcApiInfo = new MqttRpcApiInfo();
			mqttRpcApiInfo.SourceObject = obj;
			mqttRpcApiInfo.Method = method;
			mqttRpcApiInfo.Description = hslMqttApiAttribute.Description;
			mqttRpcApiInfo.HttpMethod = hslMqttApiAttribute.HttpMethod.ToUpper();
			if (string.IsNullOrEmpty(hslMqttApiAttribute.ApiTopic))
			{
				hslMqttApiAttribute.ApiTopic = method.Name;
			}
			if (permissionAttribute == null)
			{
				customAttributes = method.GetCustomAttributes(typeof(HslMqttPermissionAttribute), inherit: false);
				if (customAttributes != null && customAttributes.Length != 0)
				{
					mqttRpcApiInfo.PermissionAttribute = (HslMqttPermissionAttribute)customAttributes[0];
				}
			}
			else
			{
				mqttRpcApiInfo.PermissionAttribute = permissionAttribute;
			}
			if (string.IsNullOrEmpty(api))
			{
				mqttRpcApiInfo.ApiTopic = hslMqttApiAttribute.ApiTopic;
			}
			else
			{
				mqttRpcApiInfo.ApiTopic = api + "/" + hslMqttApiAttribute.ApiTopic;
			}
			ParameterInfo[] parameters = method.GetParameters();
			StringBuilder stringBuilder = new StringBuilder();
			if (method.ReturnType.IsSubclassOf(typeof(Task)))
			{
				stringBuilder.Append("Task<" + GetReturnTypeDescription(method.ReturnType.GetProperty("Result").PropertyType) + ">");
			}
			else
			{
				stringBuilder.Append(GetReturnTypeDescription(method.ReturnType));
			}
			stringBuilder.Append(" ");
			stringBuilder.Append(mqttRpcApiInfo.ApiTopic);
			stringBuilder.Append("(");
			for (int i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].ParameterType != typeof(ISessionContext) && parameters[i].ParameterType != typeof(HttpListenerRequest))
				{
					stringBuilder.Append(parameters[i].ParameterType.Name);
					stringBuilder.Append(" ");
					stringBuilder.Append(parameters[i].Name);
					if (i != parameters.Length - 1)
					{
						stringBuilder.Append(",");
					}
				}
			}
			stringBuilder.Append(")");
			mqttRpcApiInfo.MethodSignature = stringBuilder.ToString();
			mqttRpcApiInfo.ExamplePayload = HslReflectionHelper.GetParametersFromJson(method, parameters).ToString();
			return OperateResult.CreateSuccessResult(mqttRpcApiInfo);
		}

		/// <summary>
		/// 根据当前的方法的委托信息和类对象，生成<see cref="T:HslCommunication.MQTT.MqttRpcApiInfo" />的API对象信息。
		/// </summary>
		/// <param name="api">Api头信息</param>
		/// <param name="property">方法的委托</param>
		/// <param name="obj">当前注册的API的源对象</param>
		/// <param name="permissionAttribute">默认的权限特性</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<HslMqttApiAttribute, MqttRpcApiInfo> GetMqttSyncServicesApiFromProperty(string api, PropertyInfo property, object obj, HslMqttPermissionAttribute permissionAttribute = null)
		{
			object[] customAttributes = property.GetCustomAttributes(typeof(HslMqttApiAttribute), inherit: false);
			if (customAttributes == null || customAttributes.Length == 0)
			{
				return new OperateResult<HslMqttApiAttribute, MqttRpcApiInfo>($"Current Api ：[{property}] not support Api attribute");
			}
			HslMqttApiAttribute hslMqttApiAttribute = (HslMqttApiAttribute)customAttributes[0];
			MqttRpcApiInfo mqttRpcApiInfo = new MqttRpcApiInfo();
			mqttRpcApiInfo.SourceObject = obj;
			mqttRpcApiInfo.Property = property;
			mqttRpcApiInfo.Description = hslMqttApiAttribute.Description;
			mqttRpcApiInfo.HttpMethod = hslMqttApiAttribute.HttpMethod.ToUpper();
			if (string.IsNullOrEmpty(hslMqttApiAttribute.ApiTopic))
			{
				hslMqttApiAttribute.ApiTopic = property.Name;
			}
			if (permissionAttribute == null)
			{
				customAttributes = property.GetCustomAttributes(typeof(HslMqttPermissionAttribute), inherit: false);
				if (customAttributes != null && customAttributes.Length != 0)
				{
					mqttRpcApiInfo.PermissionAttribute = (HslMqttPermissionAttribute)customAttributes[0];
				}
			}
			else
			{
				mqttRpcApiInfo.PermissionAttribute = permissionAttribute;
			}
			if (string.IsNullOrEmpty(api))
			{
				mqttRpcApiInfo.ApiTopic = hslMqttApiAttribute.ApiTopic;
			}
			else
			{
				mqttRpcApiInfo.ApiTopic = api + "/" + hslMqttApiAttribute.ApiTopic;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(GetReturnTypeDescription(property.PropertyType));
			stringBuilder.Append(" ");
			stringBuilder.Append(mqttRpcApiInfo.ApiTopic);
			stringBuilder.Append(" { ");
			if (property.CanRead)
			{
				stringBuilder.Append("get; ");
			}
			if (property.CanWrite)
			{
				stringBuilder.Append("set; ");
			}
			stringBuilder.Append("}");
			mqttRpcApiInfo.MethodSignature = stringBuilder.ToString();
			mqttRpcApiInfo.ExamplePayload = string.Empty;
			return OperateResult.CreateSuccessResult(hslMqttApiAttribute, mqttRpcApiInfo);
		}

		/// <summary>
		/// 判断当前服务器的实际的 topic 的主题，是否满足通配符格式的订阅主题 subTopic
		/// </summary>
		/// <param name="topic">服务器的实际的主题信息</param>
		/// <param name="subTopic">客户端订阅的基于通配符的格式</param>
		/// <returns>如果返回True, 说明当前匹配成功，应该发送订阅操作</returns>
		public static bool CheckMqttTopicWildcards(string topic, string subTopic)
		{
			if (subTopic == "#")
			{
				return true;
			}
			if (subTopic.EndsWith("/#"))
			{
				if (subTopic.Contains("/+/"))
				{
					subTopic = subTopic.Replace("[", "\\[");
					subTopic = subTopic.Replace("]", "\\]");
					subTopic = subTopic.Replace(".", "\\.");
					subTopic = subTopic.Replace("*", "\\*");
					subTopic = subTopic.Replace("{", "\\{");
					subTopic = subTopic.Replace("}", "\\}");
					subTopic = subTopic.Replace("?", "\\?");
					subTopic = subTopic.Replace("$", "\\$");
					subTopic = subTopic.Replace("/+", "/[^/]+");
					subTopic = subTopic.RemoveLast(2);
					subTopic += "(/[\\S\\s]+$|$)";
					return Regex.IsMatch(topic, subTopic);
				}
				if (subTopic.Length == 2)
				{
					return false;
				}
				if (topic == subTopic.RemoveLast(2))
				{
					return true;
				}
				if (topic.StartsWith(subTopic.RemoveLast(1)))
				{
					return true;
				}
				return false;
			}
			if (subTopic == "+")
			{
				return !topic.Contains("/");
			}
			if (subTopic.EndsWith("/+"))
			{
				if (subTopic.Length == 2)
				{
					return false;
				}
				if (!topic.StartsWith(subTopic.RemoveLast(1)))
				{
					return false;
				}
				if (topic.Length == subTopic.Length - 1)
				{
					return false;
				}
				if (topic.Substring(subTopic.Length - 1).Contains("/"))
				{
					return false;
				}
				return true;
			}
			if (subTopic.Contains("/+/"))
			{
				subTopic = subTopic.Replace("[", "\\[");
				subTopic = subTopic.Replace("]", "\\]");
				subTopic = subTopic.Replace(".", "\\.");
				subTopic = subTopic.Replace("*", "\\*");
				subTopic = subTopic.Replace("{", "\\{");
				subTopic = subTopic.Replace("}", "\\}");
				subTopic = subTopic.Replace("?", "\\?");
				subTopic = subTopic.Replace("$", "\\$");
				subTopic = subTopic.Replace("/+", "/[^/]+");
				return Regex.IsMatch(topic, subTopic);
			}
			return topic == subTopic;
		}

		private static OperateResult<int> CalculateMqttRemainingLength(List<byte> buffer)
		{
			if (buffer.Count > 4)
			{
				return new OperateResult<int>("Receive Length is too long!");
			}
			if (buffer.Count == 1)
			{
				return OperateResult.CreateSuccessResult((int)buffer[0]);
			}
			if (buffer.Count == 2)
			{
				return OperateResult.CreateSuccessResult(buffer[0] - 128 + buffer[1] * 128);
			}
			if (buffer.Count == 3)
			{
				return OperateResult.CreateSuccessResult(buffer[0] - 128 + (buffer[1] - 128) * 128 + buffer[2] * 128 * 128);
			}
			return OperateResult.CreateSuccessResult(buffer[0] - 128 + (buffer[1] - 128) * 128 + (buffer[2] - 128) * 128 * 128 + buffer[3] * 128 * 128 * 128);
		}

		/// <summary>
		/// 基于MQTT协议，从网络套接字中接收剩余的数据长度<br />
		/// Receives the remaining data length from the network socket based on the MQTT protocol
		/// </summary>
		/// <param name="pipe">实际的管道对象信息</param>
		/// <returns>网络中剩余的长度数据</returns>
		public static OperateResult<int> ReceiveMqttRemainingLength(CommunicationPipe pipe)
		{
			List<byte> list = new List<byte>();
			OperateResult<byte[]> operateResult;
			do
			{
				operateResult = pipe.Receive(1, 10000);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<int>(operateResult);
				}
				list.Add(operateResult.Content[0]);
			}
			while (operateResult.Content[0] >= 128 && list.Count < 4);
			return CalculateMqttRemainingLength(list);
		}

		/// <summary>
		/// 接收一条完整的MQTT协议的报文信息，包含控制码和负载数据<br />
		/// Receive a message of a completed MQTT protocol, including control code and payload data
		/// </summary>
		/// <param name="pipe">实际的管道对象信息</param>
		/// <param name="timeOut">超时时间</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <returns>结果数据内容</returns>
		public static OperateResult<byte, byte[]> ReceiveMqttMessage(CommunicationPipe pipe, int timeOut, Action<long, long> reportProgress = null)
		{
			OperateResult<byte[]> operateResult = pipe.Receive(1, timeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult);
			}
			OperateResult<int> operateResult2 = ReceiveMqttRemainingLength(pipe);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult2);
			}
			if (operateResult.Content[0] >> 4 == 15)
			{
				reportProgress = null;
			}
			if (operateResult.Content[0] >> 4 == 0)
			{
				reportProgress = null;
			}
			OperateResult<byte[]> operateResult3 = pipe.Receive(operateResult2.Content, 60000, reportProgress);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult3);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content[0], operateResult3.Content);
		}

		/// <summary>
		/// 基于MQTT协议，从网络套接字中接收剩余的数据长度<br />
		/// Receives the remaining data length from the network socket based on the MQTT protocol
		/// </summary>
		/// <typeparam name="T">当前的管道类型</typeparam>
		/// <param name="receive">接收数据的方法</param>
		/// <param name="pipe">实际的管道对象信息</param>
		/// <returns>网络中剩余的长度数据</returns>
		public static OperateResult<int> ReceiveMqttRemainingLength<T>(Func<T, int, int, Action<long, long>, OperateResult<byte[]>> receive, T pipe)
		{
			List<byte> list = new List<byte>();
			OperateResult<byte[]> operateResult;
			do
			{
				operateResult = receive(pipe, 1, 10000, null);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<int>(operateResult);
				}
				list.Add(operateResult.Content[0]);
			}
			while (operateResult.Content[0] >= 128 && list.Count < 4);
			return CalculateMqttRemainingLength(list);
		}

		/// <summary>
		/// 接收一条完整的MQTT协议的报文信息，包含控制码和负载数据<br />
		/// Receive a message of a completed MQTT protocol, including control code and payload data
		/// </summary>
		/// <typeparam name="T">当前的管道类型</typeparam>
		/// <param name="receive">接收数据的方法</param>
		/// <param name="pipe">实际的管道对象信息</param>
		/// <param name="timeOut">超时时间</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <returns>结果数据内容</returns>
		public static OperateResult<byte, byte[]> ReceiveMqttMessage<T>(Func<T, int, int, Action<long, long>, OperateResult<byte[]>> receive, T pipe, int timeOut, Action<long, long> reportProgress = null)
		{
			OperateResult<byte[]> operateResult = receive(pipe, 1, timeOut, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult);
			}
			OperateResult<int> operateResult2 = ReceiveMqttRemainingLength(receive, pipe);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult2);
			}
			if (operateResult.Content[0] >> 4 == 15)
			{
				reportProgress = null;
			}
			if (operateResult.Content[0] >> 4 == 0)
			{
				reportProgress = null;
			}
			OperateResult<byte[]> operateResult3 = receive(pipe, operateResult2.Content, 60000, reportProgress);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(operateResult3);
			}
			return OperateResult.CreateSuccessResult(operateResult.Content[0], operateResult3.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttRemainingLength(HslCommunication.Core.Pipe.CommunicationPipe)" />
		public static async Task<OperateResult<int>> ReceiveMqttRemainingLengthAsync(CommunicationPipe pipe)
		{
			List<byte> buffer = new List<byte>();
			OperateResult<byte[]> read;
			do
			{
				read = await pipe.ReceiveAsync(1, 10000).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<int>(read);
				}
				buffer.Add(read.Content[0]);
			}
			while (read.Content[0] >= 128 && buffer.Count < 4);
			return CalculateMqttRemainingLength(buffer);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttMessage(HslCommunication.Core.Pipe.CommunicationPipe,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<byte, byte[]>> ReceiveMqttMessageAsync(CommunicationPipe pipe, int timeOut, Action<long, long> reportProgress = null)
		{
			OperateResult<byte[]> readCode = await pipe.ReceiveAsync(1, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			if (!readCode.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readCode);
			}
			OperateResult<int> readContentLength = await ReceiveMqttRemainingLengthAsync(pipe).ConfigureAwait(continueOnCapturedContext: false);
			if (!readContentLength.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readContentLength);
			}
			if (readCode.Content[0] >> 4 == 15)
			{
				reportProgress = null;
			}
			if (readCode.Content[0] >> 4 == 0)
			{
				reportProgress = null;
			}
			OperateResult<byte[]> readContent = await pipe.ReceiveAsync(readContentLength.Content, 60000, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!readContent.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readContent);
			}
			return OperateResult.CreateSuccessResult(readCode.Content[0], readContent.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttRemainingLength``1(System.Func{``0,System.Int32,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.OperateResult{System.Byte[]}},``0)" />
		public static async Task<OperateResult<int>> ReceiveMqttRemainingLengthAsync<T>(Func<T, int, int, Action<long, long>, Task<OperateResult<byte[]>>> receive, T pipe)
		{
			List<byte> buffer = new List<byte>();
			OperateResult<byte[]> rece;
			do
			{
				rece = await receive(pipe, 1, 10000, null);
				if (!rece.IsSuccess)
				{
					return OperateResult.CreateFailedResult<int>(rece);
				}
				buffer.Add(rece.Content[0]);
			}
			while (rece.Content[0] >= 128 && buffer.Count < 4);
			if (buffer.Count > 4)
			{
				return new OperateResult<int>("Receive Length is too long!");
			}
			if (buffer.Count == 1)
			{
				return OperateResult.CreateSuccessResult((int)buffer[0]);
			}
			if (buffer.Count == 2)
			{
				return OperateResult.CreateSuccessResult(buffer[0] - 128 + buffer[1] * 128);
			}
			if (buffer.Count == 3)
			{
				return OperateResult.CreateSuccessResult(buffer[0] - 128 + (buffer[1] - 128) * 128 + buffer[2] * 128 * 128);
			}
			return OperateResult.CreateSuccessResult(buffer[0] - 128 + (buffer[1] - 128) * 128 + (buffer[2] - 128) * 128 * 128 + buffer[3] * 128 * 128 * 128);
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttMessage``1(System.Func{``0,System.Int32,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.OperateResult{System.Byte[]}},``0,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<byte, byte[]>> ReceiveMqttMessageAsync<T>(Func<T, int, int, Action<long, long>, Task<OperateResult<byte[]>>> receive, T pipe, int timeOut, Action<long, long> reportProgress = null)
		{
			OperateResult<byte[]> readCode = await receive(pipe, 1, timeOut, null);
			if (!readCode.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readCode);
			}
			OperateResult<int> readContentLength = await ReceiveMqttRemainingLengthAsync(receive, pipe);
			if (!readContentLength.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readContentLength);
			}
			if (readCode.Content[0] >> 4 == 15)
			{
				reportProgress = null;
			}
			if (readCode.Content[0] >> 4 == 0)
			{
				reportProgress = null;
			}
			OperateResult<byte[]> readContent = await receive(pipe, readContentLength.Content, timeOut, reportProgress);
			if (!readContent.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte, byte[]>(readContent);
			}
			return OperateResult.CreateSuccessResult(readCode.Content[0], readContent.Content);
		}

		/// <summary>
		/// 使用MQTT协议从socket接收指定长度的字节数组，然后全部写入到流中，可以指定进度报告<br />
		/// Use the MQTT protocol to receive a byte array of specified length from the socket, and then write all of them to the stream, and you can specify a progress report
		/// </summary>
		/// <param name="pipe">当前的管道对象信息</param>
		/// <param name="stream">数据流</param>
		/// <param name="fileSize">数据大小</param>
		/// <param name="timeOut">超时时间</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">取消的令牌操作信息</param>
		/// <returns>是否操作成功</returns>
		public static OperateResult ReceiveMqttStream(CommunicationPipe pipe, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			long num = 0L;
			while (num < fileSize)
			{
				OperateResult<byte, byte[]> operateResult = ReceiveMqttMessage(pipe, timeOut);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				if (operateResult.Content1 == 0)
				{
					pipe?.CloseCommunication();
					return new OperateResult(Encoding.UTF8.GetString(operateResult.Content2));
				}
				if (aesCryptography != null)
				{
					try
					{
						operateResult.Content2 = aesCryptography.Decrypt(operateResult.Content2);
					}
					catch (Exception ex)
					{
						pipe?.CloseCommunication();
						return new OperateResult("AES Decrypt file stream failed: " + ex.Message);
					}
				}
				OperateResult operateResult2 = NetSupport.WriteStream(stream, operateResult.Content2);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				num += operateResult.Content2.Length;
				byte[] array = new byte[16];
				BitConverter.GetBytes(num).CopyTo(array, 0);
				BitConverter.GetBytes(fileSize).CopyTo(array, 8);
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult operateResult3 = pipe.Send(BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content);
					if (!operateResult3.IsSuccess)
					{
						pipe?.CloseCommunication();
						return operateResult3;
					}
					pipe?.CloseCommunication();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult operateResult4 = pipe.Send(BuildMqttCommand(100, null, array).Content);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				reportProgress?.Invoke(num, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttStream(HslCommunication.Core.Pipe.CommunicationPipe,System.IO.Stream,System.Int64,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		public static async Task<OperateResult> ReceiveMqttStreamAsync(CommunicationPipe pipe, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			long already = 0L;
			while (already < fileSize)
			{
				OperateResult<byte, byte[]> receive = await ReceiveMqttMessageAsync(pipe, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!receive.IsSuccess)
				{
					return receive;
				}
				if (receive.Content1 == 0)
				{
					pipe?.CloseCommunication();
					return new OperateResult(Encoding.UTF8.GetString(receive.Content2));
				}
				if (aesCryptography != null)
				{
					try
					{
						receive.Content2 = aesCryptography.Decrypt(receive.Content2);
					}
					catch (Exception ex2)
					{
						Exception ex = ex2;
						pipe?.CloseCommunication();
						return new OperateResult("AES Decrypt file stream failed: " + ex.Message);
					}
				}
				OperateResult write = await NetSupport.WriteStreamAsync(stream, receive.Content2);
				if (!write.IsSuccess)
				{
					return write;
				}
				already += receive.Content2.Length;
				byte[] ack = new byte[16];
				BitConverter.GetBytes(already).CopyTo(ack, 0);
				BitConverter.GetBytes(fileSize).CopyTo(ack, 8);
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult cancel = await pipe.SendAsync(BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content).ConfigureAwait(continueOnCapturedContext: false);
					if (!cancel.IsSuccess)
					{
						pipe?.CloseCommunication();
						return cancel;
					}
					pipe?.CloseCommunication();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult send = await pipe.SendAsync(BuildMqttCommand(100, null, ack).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!send.IsSuccess)
				{
					return send;
				}
				reportProgress?.Invoke(already, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 使用MQTT协议将流中的数据读取到字节数组，然后都写入到socket里面，可以指定进度报告，主要用于将文件发送到网络。<br />
		/// Use the MQTT protocol to read the data in the stream into a byte array, and then write them all into the socket. 
		/// You can specify a progress report, which is mainly used to send files to the network.
		/// </summary>
		/// <param name="pipe">当前的管道对象信息</param>
		/// <param name="stream">流</param>
		/// <param name="fileSize">总的数据大小</param>
		/// <param name="timeOut">超时信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">取消操作的令牌信息</param>
		/// <returns>是否操作成功</returns>
		public static OperateResult SendMqttStream(CommunicationPipe pipe, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			byte[] array = new byte[102400];
			long num = 0L;
			stream.Position = 0L;
			while (num < fileSize)
			{
				OperateResult<int> operateResult = NetSupport.ReadStream(stream, array);
				if (!operateResult.IsSuccess)
				{
					pipe?.CloseCommunication();
					return operateResult;
				}
				num += operateResult.Content;
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult operateResult2 = pipe.Send(BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content);
					if (!operateResult2.IsSuccess)
					{
						pipe?.CloseCommunication();
						return operateResult2;
					}
					pipe?.CloseCommunication();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult operateResult3 = pipe.Send(BuildMqttCommand(100, null, array.SelectBegin(operateResult.Content), aesCryptography).Content);
				if (!operateResult3.IsSuccess)
				{
					pipe?.CloseCommunication();
					return operateResult3;
				}
				OperateResult<byte, byte[]> operateResult4 = ReceiveMqttMessage(pipe, timeOut);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				if (operateResult4.Content1 == 0)
				{
					pipe?.CloseCommunication();
					return new OperateResult(Encoding.UTF8.GetString(operateResult4.Content2));
				}
				reportProgress?.Invoke(num, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.SendMqttStream(HslCommunication.Core.Pipe.CommunicationPipe,System.IO.Stream,System.Int64,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		public static async Task<OperateResult> SendMqttStreamAsync(CommunicationPipe pipe, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			byte[] buffer = new byte[102400];
			long already = 0L;
			stream.Position = 0L;
			while (already < fileSize)
			{
				OperateResult<int> read = await NetSupport.ReadStreamAsync(stream, buffer).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					pipe?.CloseCommunication();
					return read;
				}
				already += read.Content;
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult cancel = await pipe.SendAsync(BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content).ConfigureAwait(continueOnCapturedContext: false);
					if (!cancel.IsSuccess)
					{
						pipe?.CloseCommunication();
						return cancel;
					}
					pipe?.CloseCommunication();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult write = await pipe.SendAsync(BuildMqttCommand(100, null, buffer.SelectBegin(read.Content), aesCryptography).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!write.IsSuccess)
				{
					pipe?.CloseCommunication();
					return write;
				}
				OperateResult<byte, byte[]> receive = await ReceiveMqttMessageAsync(pipe, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!receive.IsSuccess)
				{
					return receive;
				}
				if (receive.Content1 == 0)
				{
					pipe?.CloseCommunication();
					return new OperateResult(Encoding.UTF8.GetString(receive.Content2));
				}
				reportProgress?.Invoke(already, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 使用MQTT协议将一个文件发送到网络上去，需要指定文件名，保存的文件名，可选指定文件描述信息，进度报告<br />
		/// To send a file to the network using the MQTT protocol, you need to specify the file name, the saved file name, 
		/// optionally specify the file description information, and the progress report
		/// </summary>
		/// <param name="pipe">当前的管道对象信息</param>
		/// <param name="filename">文件名称</param>
		/// <param name="servername">对方接收后保存的文件名</param>
		/// <param name="filetag">文件的描述信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌</param>
		/// <returns>是否操作成功</returns>
		public static OperateResult SendMqttFile(CommunicationPipe pipe, string filename, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			FileInfo fileInfo = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult operateResult = pipe.Send(BuildMqttCommand(0, null, Encoding.UTF8.GetBytes(StringResources.Language.FileNotExist)).Content);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				pipe?.CloseCommunication();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			string[] data = new string[3]
			{
				servername,
				fileInfo.Length.ToString(),
				filetag
			};
			OperateResult operateResult2 = pipe.Send(BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(data)).Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte, byte[]> operateResult3 = ReceiveMqttMessage(pipe, 60000);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (operateResult3.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult(Encoding.UTF8.GetString(operateResult3.Content2));
			}
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = SendMqttStream(pipe, stream, fileInfo.Length, 60000, reportProgress, aesCryptography, cancelToken);
				}
				return result;
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.SendMqttFile(HslCommunication.Core.Pipe.CommunicationPipe,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		public static async Task<OperateResult> SendMqttFileAsync(CommunicationPipe pipe, string filename, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			FileInfo info = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult notFoundResult = await pipe.SendAsync(BuildMqttCommand(0, null, Encoding.UTF8.GetBytes(StringResources.Language.FileNotExist)).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!notFoundResult.IsSuccess)
				{
					return notFoundResult;
				}
				pipe?.CloseCommunication();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			string[] array = new string[3]
			{
				servername,
				info.Length.ToString(),
				filetag
			};
			OperateResult sendResult = await pipe.SendAsync(BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(array)).Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			OperateResult<byte, byte[]> check = await ReceiveMqttMessageAsync(pipe, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (check.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult(Encoding.UTF8.GetString(check.Content2));
			}
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = await SendMqttStreamAsync(pipe, fs, info.Length, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				return result;
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <summary>
		/// 使用MQTT协议将一个数据流发送到网络上去，需要保存的文件名，可选指定文件描述信息，进度报告<br />
		/// Use the MQTT protocol to send a data stream to the network, the file name that needs to be saved, optional file description information, progress report
		/// </summary>
		/// <param name="pipe">当前的管道对象信息</param>
		/// <param name="stream">数据流</param>
		/// <param name="servername">对方接收后保存的文件名</param>
		/// <param name="filetag">文件的描述信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌信息</param>
		/// <returns>是否操作成功</returns>
		public static OperateResult SendMqttFile(CommunicationPipe pipe, Stream stream, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			string[] data = new string[3]
			{
				servername,
				stream.Length.ToString(),
				filetag
			};
			OperateResult operateResult = pipe.Send(BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(data)).Content);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte, byte[]> operateResult2 = ReceiveMqttMessage(pipe, 60000);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult2.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult(Encoding.UTF8.GetString(operateResult2.Content2));
			}
			try
			{
				return SendMqttStream(pipe, stream, stream.Length, 60000, reportProgress, aesCryptography, cancelToken);
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.SendMqttFile(HslCommunication.Core.Pipe.CommunicationPipe,System.IO.Stream,System.String,System.String,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		public static async Task<OperateResult> SendMqttFileAsync(CommunicationPipe pipe, Stream stream, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			string[] array = new string[3]
			{
				servername,
				stream.Length.ToString(),
				filetag
			};
			OperateResult sendResult = await pipe.SendAsync(BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(array)).Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			OperateResult<byte, byte[]> check = await ReceiveMqttMessageAsync(pipe, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (check.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult(Encoding.UTF8.GetString(check.Content2));
			}
			try
			{
				return await SendMqttStreamAsync(pipe, stream, stream.Length, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <summary>
		/// 使用MQTT协议从网络接收字节数组，然后写入文件或流中，支持进度报告<br />
		/// Use MQTT protocol to receive byte array from the network, and then write it to file or stream, support progress report
		/// </summary>
		/// <param name="pipe">当前的管道对象信息</param>
		/// <param name="source">文件名或是流</param>
		/// <param name="reportProgress">进度报告</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌信息</param>
		/// <returns>是否操作成功，如果成功，携带文件基本信息</returns>
		public static OperateResult<FileBaseInfo> ReceiveMqttFile(CommunicationPipe pipe, object source, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			OperateResult<byte, byte[]> operateResult = ReceiveMqttMessage(pipe, 60000);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult);
			}
			if (operateResult.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult<FileBaseInfo>(Encoding.UTF8.GetString(operateResult.Content2));
			}
			FileBaseInfo fileBaseInfo = new FileBaseInfo();
			string[] array = HslProtocol.UnPackStringArrayFromByte(operateResult.Content2);
			fileBaseInfo.Name = array[0];
			fileBaseInfo.Size = long.Parse(array[1]);
			fileBaseInfo.Tag = array[2];
			pipe.Send(BuildMqttCommand(100, null, null).Content);
			try
			{
				OperateResult operateResult2 = null;
				string text = source as string;
				if (text != null)
				{
					using (FileStream stream = new FileStream(text, FileMode.Create, FileAccess.Write))
					{
						operateResult2 = ReceiveMqttStream(pipe, stream, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken);
					}
					if (!operateResult2.IsSuccess)
					{
						if (File.Exists(text))
						{
							File.Delete(text);
						}
						return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult2);
					}
				}
				else
				{
					Stream stream2 = source as Stream;
					if (stream2 == null)
					{
						throw new Exception("Not Supported Type");
					}
					operateResult2 = ReceiveMqttStream(pipe, stream2, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken);
				}
				return OperateResult.CreateSuccessResult(fileBaseInfo);
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.MQTT.MqttHelper.ReceiveMqttFile(HslCommunication.Core.Pipe.CommunicationPipe,System.Object,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		public static async Task<OperateResult<FileBaseInfo>> ReceiveMqttFileAsync(CommunicationPipe pipe, object source, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			OperateResult<byte, byte[]> receiveFileInfo = await ReceiveMqttMessageAsync(pipe, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!receiveFileInfo.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(receiveFileInfo);
			}
			if (receiveFileInfo.Content1 == 0)
			{
				pipe?.CloseCommunication();
				return new OperateResult<FileBaseInfo>(Encoding.UTF8.GetString(receiveFileInfo.Content2));
			}
			FileBaseInfo fileBaseInfo = new FileBaseInfo();
			string[] array = HslProtocol.UnPackStringArrayFromByte(receiveFileInfo.Content2);
			fileBaseInfo.Name = array[0];
			fileBaseInfo.Size = long.Parse(array[1]);
			fileBaseInfo.Tag = array[2];
			await pipe.SendAsync(BuildMqttCommand(100, null, null).Content).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				OperateResult write = null;
				string savename = source as string;
				if (savename != null)
				{
					using (FileStream fs = new FileStream(savename, FileMode.Create, FileAccess.Write))
					{
						write = await ReceiveMqttStreamAsync(pipe, fs, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
					}
					if (!write.IsSuccess)
					{
						if (File.Exists(savename))
						{
							File.Delete(savename);
						}
						return OperateResult.CreateFailedResult<FileBaseInfo>(write);
					}
				}
				else
				{
					Stream stream = source as Stream;
					if (stream == null)
					{
						throw new Exception("Not Supported Type");
					}
					await ReceiveMqttStreamAsync(pipe, stream, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				return OperateResult.CreateSuccessResult(fileBaseInfo);
			}
			catch (Exception ex)
			{
				pipe?.CloseCommunication();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}
	}
}
